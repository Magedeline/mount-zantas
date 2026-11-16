using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MountZantas;

// Finds Celeste installs: Steam, Epic, GOG and itch on Windows; Steam and itch on Linux and macOS; plus common custom folders.
static class Detect {
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", "Mods", "Content", "Saves", "Backups", "AppData", "orig", "bin", "obj", "Library", ".cache", ".local", ".steam", ".config", ".nuget" };

    // The folder Everest patches. On macOS that is inside the app bundle; elsewhere it is the folder that holds Celeste.exe.
    public static string ResolveRoot(string path) {
        if (string.IsNullOrEmpty(path)) return null;
        if (File.Exists(Path.Combine(path, "Celeste.exe"))) return path;
        foreach (var inner in new[] { Path.Combine(path, "Contents", "Resources"), Path.Combine(path, "Celeste.app", "Contents", "Resources") })
            if (File.Exists(Path.Combine(inner, "Celeste.exe"))) return inner;
        return null;
    }

    // A real install has Celeste.exe and a Content folder (this skips stripped reference-library copies).
    public static bool IsRealInstall(string root) => root != null && File.Exists(Path.Combine(root, "Celeste.exe")) && Directory.Exists(Path.Combine(root, "Content"));

    static void Scan(string dir, int depth, List<(string, string)> into) {
        if (depth < 0 || string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        if (ResolveRoot(dir) != null) { into.Add((File.Exists(Path.Combine(dir, ".itch", "receipt.json.gz")) ? "itch" : "Other", dir)); return; }
        foreach (var d in Directory.EnumerateDirectories(dir)) {
            var n = Path.GetFileName(d);
            if (SkipDirs.Contains(n) || n.Contains("backup", StringComparison.OrdinalIgnoreCase) || n.Contains("overwrite", StringComparison.OrdinalIgnoreCase)) continue;
            try { Scan(d, depth - 1, into); } catch { }
        }
    }

    // Steam keeps extra library folders listed in libraryfolders.vdf.
    public static IEnumerable<string> SteamLibraries(IEnumerable<string> steamRoots) {
        foreach (var steam in steamRoots.Where(Directory.Exists)) {
            yield return steam;
            foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) yield return m.Groups[1].Value.Replace("\\\\", "\\");
        }
    }

    static void DetectWindows(List<(string, string)> c) {
        if (!OperatingSystem.IsWindows()) return;
        try {
            foreach (var k in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" }) {
                var steam = (Registry.GetValue(k, "SteamPath", null) ?? Registry.GetValue(k, "InstallPath", null)) as string; if (steam == null) continue;
                foreach (var lib in SteamLibraries(new[] { steam })) c.Add(("Steam", Path.Combine(lib, "steamapps", "common", "Celeste")));
            }
        } catch { }
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        try { var itch = Path.Combine(appdata, "itch", "apps"); if (Directory.Exists(itch)) foreach (var d in Directory.GetDirectories(itch)) c.Add(("itch", d)); } catch { }
        try {
            foreach (var root in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" }) {
                using var k = Registry.LocalMachine.OpenSubKey(root); if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames()) { using var g = k.OpenSubKey(sub); if ((g?.GetValue("gameName") as string) == "Celeste" && g.GetValue("path") is string gp) c.Add(("GOG", gp)); }
            }
        } catch { }
        try {
            var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(epic)) foreach (var f in Directory.GetFiles(epic, "*.item")) { var j = JsonDocument.Parse(File.ReadAllText(f)).RootElement; if (j.TryGetProperty("DisplayName", out var n) && n.GetString() == "Celeste" && j.TryGetProperty("InstallLocation", out var l)) c.Add(("Epic", l.GetString())); }
        } catch { }
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady)) { c.Add(("Steam", Path.Combine(d.Name, "Program Files (x86)", "Steam", "steamapps", "common", "Celeste"))); c.Add(("Other", Path.Combine(d.Name, "Games", "Celeste"))); }
        // Custom locations (e.g. an itch library on the Desktop): look a few folders deep for Celeste.exe.
        foreach (var sf in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile })
            try { Scan(Environment.GetFolderPath(sf), 3, c); } catch { }
    }

    // `home` is a parameter so the checks can run against a fake home folder.
    public static void DetectUnix(List<(string, string)> c, string home, bool mac) {
        var steamRoots = mac
            ? new[] { Path.Combine(home, "Library", "Application Support", "Steam") }
            : new[] { Path.Combine(home, ".local", "share", "Steam"), Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".steam", "root"), Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam") };
        foreach (var lib in SteamLibraries(steamRoots)) c.Add(("Steam", Path.Combine(lib, "steamapps", "common", "Celeste")));
        var itch = mac ? Path.Combine(home, "Library", "Application Support", "itch", "apps") : Path.Combine(home, ".config", "itch", "apps");
        try { if (Directory.Exists(itch)) foreach (var d in Directory.GetDirectories(itch)) c.Add(("itch", d)); } catch { }
        if (mac) c.Add(("Other", "/Applications/Celeste.app"));
        foreach (var sub in new[] { "Games", "Desktop", "Documents" }) try { Scan(Path.Combine(home, sub), 3, c); } catch { }
    }

    public static List<Install> FindAll() {
        var c = new List<(string store, string path)>();
        if (Platform.Windows) DetectWindows(c); else DetectUnix(c, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Platform.Mac);
        return Normalize(c);
    }

    // Resolve each candidate to its patchable folder, keep real installs only, drop duplicates, and name them.
    public static List<Install> Normalize(IEnumerable<(string store, string path)> candidates) {
        var cmp = Platform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return candidates.Select(x => (x.store, root: ResolveRoot(x.path))).Where(x => IsRealInstall(x.root))
            .Select(x => (x.store, path: Path.GetFullPath(x.root).TrimEnd('\\', '/'))).GroupBy(x => x.path, cmp).Select(g => g.First())
            .Select((x, n) => new Install { Name = $"{x.store} (#{n + 1})", Path = x.path, Store = x.store }).ToList();
    }
}
