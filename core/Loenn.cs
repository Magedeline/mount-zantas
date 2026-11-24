using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace MountZantas;

// Lönn, the map editor: installed per user from the official release for this OS (Windows zip, Linux AppImage, macOS app bundle).
static class Loenn {
    public static string Dir => Path.Combine(Config.DataDir, "loenn");
    public static string Installed() {
        try { return File.ReadAllText(Path.Combine(Dir, "version.txt")).Trim(); } catch { return null; }
    }

    public static bool IsAssetFor(string name, bool windows, bool mac) => windows ? name.EndsWith("windows.zip") : mac ? name.EndsWith("macos.app.zip") : name.EndsWith("linux.AppImage");
    public static bool IsMyAsset(string name) => IsAssetFor(name, Platform.Windows, Platform.Mac);

    public static async Task<(string tag, string url, string sha, string name)> Latest() {
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync("https://api.github.com/repos/CelestialCartographers/Loenn/releases/latest")).RootElement;
        foreach (var a in j.GetProperty("assets").EnumerateArray()) {
            var n = a.GetProperty("name").GetString();
            if (IsMyAsset(n)) return (j.GetProperty("tag_name").GetString(), a.GetProperty("browser_download_url").GetString(), a.TryGetProperty("digest", out var d) ? d.GetString()?.Replace("sha256:", "") : null, n);
        }
        throw new Exception("No " + Platform.Name + " build found in the latest Lönn release.");
    }

    public static async Task Install(Action<string> log, IProgress<int> prog) {
        var (tag, url, sha, name) = await Latest();
        if (sha == null) throw new Exception("Lönn release has no published checksum; refusing to install.");
        var tmp = Path.Combine(Path.GetTempPath(), "mountzantas-" + name);
        log($"Downloading Lönn {tag}...");
        await Net.Download(url, tmp, prog);
        if (!Net.Sha256(tmp).Equals(sha, StringComparison.OrdinalIgnoreCase)) {
            File.Delete(tmp);
            throw new Exception("Lönn download failed its checksum.");
        }
        log("Verified (SHA-256). Installing...");
        if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        Directory.CreateDirectory(Dir);
        if (name.EndsWith(".AppImage")) {
            var dest = Path.Combine(Dir, "Loenn.AppImage");
            File.Move(tmp, dest);
            Platform.MakeExecutable(dest);
        }
        else {
            ZipFile.ExtractToDirectory(tmp, Dir, true);
            File.Delete(tmp);
        }
        File.WriteAllText(Path.Combine(Dir, "version.txt"), tag);
        log("Lönn installed.");
    }

    public static string FindApp() {
        if (!Directory.Exists(Dir)) return null;
        if (Platform.Mac) return Directory.GetDirectories(Dir, "*.app", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (Platform.Linux) return Directory.GetFiles(Dir, "*.AppImage", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return Directory.GetFiles(Dir, "*.exe", SearchOption.AllDirectories).OrderBy(f => Path.GetFileName(f).StartsWith("L", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
    }

    // map: optional .bin to open at startup (Lönn's launch arguments take the file as a positional argument).
    public static void Launch(string map = null) {
        var app = FindApp() ?? throw new Exception("Lönn is not installed yet. Use \"Install / update Lönn\" first.");
        var args = new List<string>();
        // In test mode (MOUNTZANTAS_DATA set) keep Lönn's own settings away from the real Lönn/Olympus configuration.
        if (Environment.GetEnvironmentVariable("MOUNTZANTAS_DATA") is { Length: > 0 }) {
            args.Add("--storage-directory");
            args.Add(Path.Combine(Config.DataDir, "loenn-storage"));
        }
        if (map != null) args.Add(map);
        ProcessStartInfo psi;
        if (Platform.Mac) {
            psi = new ProcessStartInfo("open");
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(app);
            if (args.Count > 0) {
                psi.ArgumentList.Add("--args");
                foreach (var a in args) psi.ArgumentList.Add(a);
            }
        }
        else {
            psi = new ProcessStartInfo(app) { WorkingDirectory = Path.GetDirectoryName(app) };
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        psi.UseShellExecute = false;
        Process.Start(psi);
    }
}
