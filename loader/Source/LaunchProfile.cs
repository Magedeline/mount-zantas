using System;
using System.IO;

namespace Celeste.Mod.ZantasLoader;

// Mount Zantas can start Celeste with one of its mod presets without touching Mods/blacklist.txt.
// It writes two lists into the Mods folder and passes them to Everest on the command line:
//     --blacklist zantas-profile-off.txt   (mods this preset turns off)
//     --whitelist zantas-profile-on.txt    (mods this preset turns on, even if blacklist.txt disables them)
// The first line of the "off" file names the preset, so the loader can show which one is active.
public static class LaunchProfile {
    public const string OffFileName = "zantas-profile-off.txt";

    public const string NameHeader = "# Zantas profile: ";

    // The preset name, or null when Celeste was started normally.
    public static string ActiveName() {
        string path = Everest.Loader.PathTemporaryBlacklist;
        if (string.IsNullOrEmpty(path)) {
            return null;
        }
        if (!Path.GetFileName(path).Equals(OffFileName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        if (!File.Exists(path)) {
            return null;
        }
        foreach (string line in File.ReadLines(path)) {
            if (line.StartsWith(NameHeader)) {
                return line.Substring(NameHeader.Length).Trim();
            }
        }
        return null;
    }
}
