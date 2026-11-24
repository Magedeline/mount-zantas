using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MountZantas;

// What Mount Zantas's own loading screen shows while Celeste starts.
class GameStartState {
    public string Title = "Starting Celeste";
    public string Detail = "Booting Everest";
    // 0 to 1.
    public double Progress;
    // True once the game window is up (or the game has closed); the loading screen then goes away.
    public bool Done;
    // True when Celeste closed before it finished loading.
    public bool Exited;
}

// Mount Zantas shows its own loading screen instead of Everest's splash.
// It starts Celeste with --disable-splash (an Everest option) and follows Everest's log.txt
// to know which mod is loading, the same way Everest's splash shows "Loading <mod> [n/N]".
//
// Progress is split in two parts:
//   - mods: the first 55%, counted against the mods Everest will load (zips first, then folders, each sorted by name),
//   - game content: the last 45%, one step for each "- ... LOAD: <ms>" line Everest prints (graphics, audio, levels...).
class GameStart {
    // Everest's own command-line option that skips its splash window.
    public const string DisableSplashArg = "--disable-splash";

    // Everest prints one "- NAME LOAD: 123ms" line per content step, in this order.
    static readonly string[] ContentSteps = {
        "graphics", "the mountain", "audio", "graphics data", "mountain data",
        "dialog and fonts", "modded mountain", "modded mountain data", "levels", "textures"
    };

    const double ModShare = 0.55;

    static readonly Regex CodeModule = new(@"Registered code module .+ for module (.+?) \S+\.\s*$");
    static readonly Regex DelayedMod = new(@"Dependencies of mod (.+?) \S+ are now satisfied: loading");
    static readonly Regex AllModsLoaded = new(@"\[loader\] Loaded \d+ modules");
    static readonly Regex ContentStep = new(@"^\s*- .+ LOAD: \d+ms");

    readonly Process game;
    readonly string logPath;
    readonly byte[] oldLogStart;
    readonly List<string> loadOrder;
    readonly Dictionary<string, int> positionByName = new(StringComparer.OrdinalIgnoreCase);
    readonly DateTime startedAt = DateTime.UtcNow;

    long readPosition;
    string partialLine = "";
    bool readingNewLog;
    int modsLoaded;
    bool modsDone;
    int contentStepsDone;
    bool contentDone;
    // When "DONE LOADING" was seen; used where the game window can't be checked.
    DateTime? doneLoadingAt;

    public readonly GameStartState State = new();

    // True when this install has Everest, so the loading screen has something to follow.
    public static bool CanShow(Install install) {
        return EverestSvc.EverestVersion(install) != null;
    }

    // The arguments with Everest's splash switched off.
    public static string WithoutEverestSplash(string args) {
        if (args != null && args.Contains(DisableSplashArg)) {
            return args;
        }
        if (string.IsNullOrWhiteSpace(args)) {
            return DisableSplashArg;
        }
        return args + " " + DisableSplashArg;
    }

    // Call this just before starting Celeste, so the previous log.txt is not mistaken for the new one.
    public static byte[] SnapshotLog(Install install) {
        return ReadStart(Path.Combine(install.Path, "log.txt"));
    }

    public GameStart(Install install, Process game, byte[] oldLogStart, string args, string presetName = null) {
        this.game = game;
        this.oldLogStart = oldLogStart;
        logPath = Path.Combine(install.Path, "log.txt");
        loadOrder = LoadOrder(install, args);
        for (int i = 0; i < loadOrder.Count; i++) {
            positionByName.TryAdd(loadOrder[i], i);
        }
        if (presetName != null) {
            State.Title = "Starting Celeste with " + presetName;
        }
    }

    // The mod names in the order Everest loads them: enabled zips sorted by file name, then enabled folders.
    // A Mount Zantas preset start (--whitelist) loads exactly the mods in its list.
    static List<string> LoadOrder(Install install, string args) {
        List<ModInfo> mods;
        try {
            mods = Mods.Scan(install);
        } catch {
            return new List<string>();
        }

        HashSet<string> onlyThese = null;
        var whitelist = Regex.Match(args ?? "", @"--whitelist\s+(\S+)");
        if (whitelist.Success) {
            onlyThese = ReadList(Path.Combine(Mods.Dir(install), whitelist.Groups[1].Value));
        }

        bool WillLoad(ModInfo m) {
            if (onlyThese != null) {
                return onlyThese.Contains(m.File);
            }
            return m.Enabled;
        }

        var zips = mods.Where(m => !m.IsDir && WillLoad(m)).OrderBy(m => m.File, StringComparer.OrdinalIgnoreCase);
        var folders = mods.Where(m => m.IsDir && WillLoad(m)).OrderBy(m => m.File, StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        order.AddRange(zips.Select(m => m.Name));
        order.AddRange(folders.Select(m => m.Name));
        return order;
    }

    static HashSet<string> ReadList(string path) {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (var line in File.ReadAllLines(path)) {
                var t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) {
                    set.Add(t);
                }
            }
        } catch { }
        return set;
    }

    // The first bytes of a log file (its first line holds the start time, so it differs between game starts).
    static byte[] ReadStart(string path) {
        try {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[96];
            int n = f.Read(buffer, 0, buffer.Length);
            return buffer.Take(n).ToArray();
        } catch {
            return null;
        }
    }

    // Call this a few times a second from the UI; it reads what Everest has written since the last call.
    public GameStartState Poll() {
        if (State.Done) {
            return State;
        }
        ReadNewLines();
        CheckGame();
        UpdateProgress();
        return State;
    }

    void ReadNewLines() {
        if (!readingNewLog) {
            var start = ReadStart(logPath);
            if (start == null || start.Length == 0) {
                return;
            }
            bool sameAsBefore = oldLogStart != null && start.SequenceEqual(oldLogStart);
            if (sameAsBefore) {
                return;
            }
            readingNewLog = true;
        }

        string text;
        try {
            using var f = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (f.Length < readPosition) {
                // The file was replaced; start again from the top.
                readPosition = 0;
                partialLine = "";
            }
            f.Seek(readPosition, SeekOrigin.Begin);
            var buffer = new byte[f.Length - readPosition];
            int n = f.Read(buffer, 0, buffer.Length);
            readPosition += n;
            text = Encoding.UTF8.GetString(buffer, 0, n);
        } catch {
            return;
        }

        // Keep the last piece if it doesn't end with a newline yet; it is finished on the next read.
        var lines = (partialLine + text).Split('\n');
        partialLine = lines[^1];
        for (int i = 0; i < lines.Length - 1; i++) {
            HandleLine(lines[i].TrimEnd('\r'));
        }
    }

    void HandleLine(string line) {
        var code = CodeModule.Match(line);
        if (code.Success) {
            ModLoading(code.Groups[1].Value);
            return;
        }
        var delayed = DelayedMod.Match(line);
        if (delayed.Success) {
            ModLoading(delayed.Groups[1].Value);
            return;
        }
        if (AllModsLoaded.IsMatch(line)) {
            modsDone = true;
            modsLoaded = Math.Max(modsLoaded, loadOrder.Count);
            State.Detail = "Loading " + ContentSteps[0];
            return;
        }
        if (ContentStep.IsMatch(line)) {
            modsDone = true;
            contentStepsDone++;
            if (contentStepsDone < ContentSteps.Length) {
                State.Detail = "Loading " + ContentSteps[contentStepsDone];
            } else {
                State.Detail = "Almost done...";
            }
            return;
        }
        if (line.StartsWith("DONE LOADING")) {
            contentDone = true;
            State.Detail = "Almost done...";
        }
    }

    void ModLoading(string name) {
        if (modsDone) {
            return;
        }
        modsLoaded++;
        if (positionByName.TryGetValue(name, out int position)) {
            modsLoaded = Math.Max(modsLoaded, position + 1);
        }
        int total = Math.Max(loadOrder.Count, modsLoaded);
        State.Detail = $"Loading {name} [{modsLoaded}/{total}]";
    }

    void CheckGame() {
        try {
            if (game == null) {
                return;
            }
            if (game.HasExited) {
                State.Done = true;
                State.Exited = !contentDone;
                return;
            }
            // On Windows the game window only counts once it is visible, which Everest does right after loading.
            if (contentDone && Platform.Windows) {
                game.Refresh();
                if (game.MainWindowHandle != IntPtr.Zero) {
                    State.Done = true;
                }
            }
        } catch { }

        // Elsewhere (or if the window can't be seen) give the window a moment after "DONE LOADING".
        if (contentDone && !State.Done && doneLoadingAt == null) {
            doneLoadingAt = DateTime.UtcNow;
        }
        if (doneLoadingAt != null && DateTime.UtcNow - doneLoadingAt > TimeSpan.FromSeconds(3)) {
            State.Done = true;
        }

        // Never stay up forever (for example if the log can't be read).
        if (DateTime.UtcNow - startedAt > TimeSpan.FromMinutes(10)) {
            State.Done = true;
        }
    }

    void UpdateProgress() {
        double mods;
        if (modsDone) {
            mods = 1;
        } else if (loadOrder.Count == 0) {
            mods = 0;
        } else {
            mods = Math.Min(1, (double)modsLoaded / loadOrder.Count);
        }
        double content = Math.Min(1, (double)contentStepsDone / ContentSteps.Length);
        if (contentDone) {
            content = 1;
        }
        State.Progress = mods * ModShare + content * (1 - ModShare);
    }
}
