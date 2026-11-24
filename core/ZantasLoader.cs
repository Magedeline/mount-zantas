using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace MountZantas;

// What the in-game Zantas Loader reports after the game has loaded (zantas-session.json in the Celeste folder).
// The property names match loader/Source/SessionReport.cs.
class ZantasSession {
    public string Loader { get; set; }
    public string Everest { get; set; }
    public string Profile { get; set; }
    public DateTime Started { get; set; }
    public List<ZantasSessionMod> Mods { get; set; } = new();
    public List<string> Problems { get; set; } = new();
}

class ZantasSessionMod {
    public string Name { get; set; }
    public string Version { get; set; }
    public string File { get; set; }
    public bool Code { get; set; }
}

// Zantas Loader is Mount Zantas's own mod loader layer. It is an Everest core mod (source in loader/),
// so Everest still loads every mod and all Everest mods keep working. Mount Zantas:
//   - installs it into Mods/ (it ships inside this program as an embedded zip),
//   - reads the load report it writes after each game start,
//   - starts Celeste with a mod preset without changing Mods/blacklist.txt.
static class ZantasLoader {
    public const string ZipName = "ZantasLoader.zip";
    public const string ProfileOffFile = "zantas-profile-off.txt";
    public const string ProfileOnFile = "zantas-profile-on.txt";
    public const string SessionFile = "zantas-session.json";

    // The line the loader looks for to show the preset name (see loader/Source/LaunchProfile.cs).
    const string ProfileNameHeader = "# Zantas profile: ";

    // The loader zip built into this program, or null if this build was made without it.
    static byte[] Bundled() {
        using var stream = typeof(ZantasLoader).Assembly.GetManifestResourceStream(ZipName);
        if (stream == null) {
            return null;
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    public static bool IsBundled => typeof(ZantasLoader).Assembly.GetManifestResourceInfo(ZipName) != null;

    static string ZipPath(Install i) => Path.Combine(Mods.Dir(i), ZipName);

    static string VersionInZip(Stream zipStream) {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("everest.yaml");
        if (entry == null) {
            return null;
        }
        using var reader = new StreamReader(entry.Open());
        var (_, version, _) = Mods.ParseYaml(reader.ReadToEnd());
        return version;
    }

    public static string BundledVersion() {
        var bytes = Bundled();
        if (bytes == null) {
            return null;
        }
        return VersionInZip(new MemoryStream(bytes));
    }

    // The installed version, or null if Zantas Loader is not in this install's Mods folder.
    public static string InstalledVersion(Install i) {
        var path = ZipPath(i);
        if (!File.Exists(path)) {
            return null;
        }
        try {
            using var file = File.OpenRead(path);
            return VersionInZip(file);
        } catch {
            return "?";
        }
    }

    // True when the bundled loader is newer than the installed one (or it isn't installed yet).
    public static bool NeedsInstall(Install i) {
        var have = InstalledVersion(i);
        var bundled = BundledVersion();
        if (bundled == null) {
            return false;
        }
        if (have == null) {
            return true;
        }
        return ModDb.Newer(have, bundled);
    }

    public static void Install(Install i, Action<string> log) {
        var bytes = Bundled();
        if (bytes == null) {
            throw new Exception("This build of Mount Zantas was made without Zantas Loader (loader/ZantasLoader.zip was missing).");
        }
        Directory.CreateDirectory(Mods.Dir(i));
        File.WriteAllBytes(ZipPath(i), bytes);

        // Make sure it isn't switched off in blacklist.txt, or Everest would skip it.
        var disabled = Mods.Disabled(i);
        if (disabled.Remove(ZipName)) {
            Mods.WriteDisabled(i, disabled);
        }
        log("Zantas Loader " + BundledVersion() + " installed into Mods.");
    }

    public static void Remove(Install i, Action<string> log) {
        var path = ZipPath(i);
        if (File.Exists(path)) {
            File.Delete(path);
        }
        log("Zantas Loader removed. Everest and your other mods were not touched.");
    }

    // The report from the last game start, or null if there is none (or it can't be read).
    public static ZantasSession ReadSession(Install i) {
        try {
            var text = File.ReadAllText(Path.Combine(i.Path, SessionFile));
            return JsonSerializer.Deserialize<ZantasSession>(text);
        } catch {
            return null;
        }
    }

    // A one-line summary for the UI.
    public static string Summary(ZantasSession s) {
        if (s == null) {
            return "No load report yet. Start Celeste once with Zantas Loader installed.";
        }
        var when = s.Started.ToString("yyyy-MM-dd HH:mm");
        var preset = s.Profile == null ? "" : $", preset \"{s.Profile}\"";
        var problems = s.Problems.Count == 0 ? "no problems" : $"{s.Problems.Count} problem(s)";
        return $"Last start {when}: {s.Mods.Count} mods loaded{preset}, {problems}.";
    }

    // Writes the preset's on/off lists into Mods/ and returns the command-line arguments that make Everest use them.
    // blacklist.txt is not changed, so a normal start afterwards still uses the user's own mod choices.
    //     --blacklist: mods this preset turns off (added to blacklist.txt for this start only)
    //     --whitelist: mods this preset turns on (loaded even if blacklist.txt disables them)
    public static string PrepareProfile(Install i, string presetName, IEnumerable<string> offFiles) {
        var off = new HashSet<string>(offFiles, StringComparer.OrdinalIgnoreCase);
        // The loader itself always stays on.
        off.Remove(ZipName);

        var on = new List<string>();
        foreach (var mod in Mods.Scan(i)) {
            if (!off.Contains(mod.File)) {
                on.Add(mod.File);
            }
        }
        if (!on.Contains(ZipName, StringComparer.OrdinalIgnoreCase)) {
            on.Add(ZipName);
        }

        var offLines = new List<string> {
            ProfileNameHeader + presetName,
            "# Written by Mount Zantas for one game start. Your blacklist.txt is unchanged."
        };
        offLines.AddRange(off.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        var onLines = new List<string> {
            ProfileNameHeader + presetName,
            "# Written by Mount Zantas for one game start."
        };
        onLines.AddRange(on.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        File.WriteAllLines(Path.Combine(Mods.Dir(i), ProfileOffFile), offLines);
        File.WriteAllLines(Path.Combine(Mods.Dir(i), ProfileOnFile), onLines);
        return $"--blacklist {ProfileOffFile} --whitelist {ProfileOnFile}";
    }
}
