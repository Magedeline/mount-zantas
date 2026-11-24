using System.Text.Json;

namespace MountZantas;

// Mount Zantas updating itself from its own GitHub releases (github.com/Magedeline/mount-zantas).
// The download is only used if its SHA-256 matches the digest GitHub publishes for the release asset.
static class SelfUpdate {
    public const string Repo = "Magedeline/mount-zantas";

    public record Release(string Tag, string Version, string Url, string Sha256, string Name);

    // Returns the latest release if it is newer than `current`, else null. Windows builds use MountZantas.exe.
    public static async Task<Release> Check(string current, string assetName = "MountZantas.exe") {
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest")).RootElement;
        if (j.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (j.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        var tag = j.GetProperty("tag_name").GetString();
        var ver = tag.TrimStart('v', 'V');
        if (!ModDb.Newer(current, ver)) return null;
        foreach (var a in j.GetProperty("assets").EnumerateArray()) {
            if (a.GetProperty("name").GetString() != assetName) continue;
            var dg = a.TryGetProperty("digest", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
            if (dg == null || !dg.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;   // no checksum, no auto-update
            var url = a.GetProperty("browser_download_url").GetString();
            if (!url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.Ordinal)) return null;
            return new Release(tag, ver, url, dg.Substring(7).ToLowerInvariant(), assetName);
        }
        return null;
    }

    // Downloads the release next to `exePath` and verifies it. Returns the path of the verified new file.
    public static async Task<string> Download(Release r, string exePath, IProgress<int> prog = null) {
        var tmp = exePath + ".new";
        if (File.Exists(tmp)) File.Delete(tmp);
        await Net.Download(r.Url, tmp, prog);
        if (!string.Equals(Net.Sha256(tmp), r.Sha256, StringComparison.OrdinalIgnoreCase)) {
            File.Delete(tmp);
            throw new InvalidDataException("The downloaded update did not match its published checksum, so it was discarded.");
        }
        return tmp;
    }

    // Windows lets a running exe be renamed: move it aside, put the new one in its place. Caller then starts it and exits.
    public static void Swap(string exePath, string verifiedNew) {
        var old = exePath + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exePath, old);
        File.Move(verifiedNew, exePath);
    }

    // Remove leftovers from a previous update.
    public static void Cleanup(string exePath) {
        foreach (var f in new[] { exePath + ".old", exePath + ".new" })
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }
}
