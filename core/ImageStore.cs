namespace MountZantas;

// Safe thumbnails: only fetched over HTTPS from known image hosts, size-limited and cached on disk.
// Decoding is left to the UI layer, because each UI toolkit has its own image type.
static class ImageStore {
    static readonly SemaphoreSlim gate = new(6);
    static readonly Dictionary<string, Task<byte[]>> mem = new();
    public static string LastError;   // why the last fetch failed (for diagnostics)
    public static string CacheDir => Path.Combine(Config.DataDir, "cache", "img");

    public static bool Allowed(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        return u.Host == "images.gamebanana.com" || (u.Host == "raw.githubusercontent.com" && u.AbsolutePath.StartsWith("/EverestAPI/EverestAPI.github.io/"));
    }

    public static Task<byte[]> GetBytes(string url) {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<byte[]>(null);
        lock (mem) {
            if (!mem.TryGetValue(url, out var t)) mem[url] = t = Remember(url);
            return t;
        }
    }

    // One retry for a flaky host, and a failure is never kept: the next request tries again.
    static async Task<byte[]> Remember(string url) {
        var d = await Fetch(url);
        if (d == null && Allowed(url)) {
            await Task.Delay(800);
            d = await Fetch(url);
        }
        if (d == null)
            lock (mem) mem.Remove(url);
        return d;
    }

    static async Task<byte[]> Fetch(string url) {
        if (!Allowed(url)) return null;
        await gate.WaitAsync();
        try {
            Directory.CreateDirectory(CacheDir);
            var path = Path.Combine(
                CacheDir,
                Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".img");
            byte[] data;
            if (File.Exists(path)) data = await File.ReadAllBytesAsync(path);
            else {
                using var r = await Net.Http.GetAsync(url);
                r.EnsureSuccessStatusCode();
                data = await r.Content.ReadAsByteArrayAsync();
                if (data.Length > 8_000_000) return null;
                await File.WriteAllBytesAsync(path, data);
            }
            return data;
        } catch (Exception ex) {
            LastError = ex.GetType().Name + ": " + ex.Message;
            return null;
        } finally { gate.Release(); }
    }

    public static int Clear() {
        int n = 0;
        try {
            if (Directory.Exists(CacheDir))
                foreach (var f in Directory.GetFiles(CacheDir)) {
                    try {
                        File.Delete(f);
                        n++;
                    } catch { }
                }
        } catch { }
        lock (mem) mem.Clear();
        return n;
    }

    public static bool IsWebP(byte[] d) => d != null && d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P';
}
