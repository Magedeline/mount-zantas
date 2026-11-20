namespace MountZantas;

// Updating installed mod zips from the Everest mod database. Every download is checked against the database's xxHash.
static class ModUpdate {
    public static async Task Zip(Install inst, ModInfo m, ModEntry e, Config cfg, Action<string> log, IProgress<int> p = null) {
        var dest = Path.Combine(Mods.Dir(inst), m.File); var tmp = dest + ".new"; log($"Updating {m.Name} {m.Version} -> {e.Version}");
        var srcs = cfg.Mirror == "gamebanana" ? new[] { e.Url } : new[] { "https://celestemodupdater.0x0a.de/banana-mirror/" + e.Mirror + ".zip", e.Url };
        foreach (var s in srcs) {
            try { await Net.Download(s, tmp, p); if (e.Hashes.Contains(Net.XxHash(tmp), StringComparer.OrdinalIgnoreCase)) { File.Move(tmp, dest, true); log("  verified and replaced"); return; } log("  checksum mismatch"); }
            catch (Exception ex) { log("  failed: " + ex.Message); }
            try { File.Delete(tmp); } catch { }
        }
        throw new Exception($"{m.Name} could not be updated safely.");
    }
    public static async Task<int> All(Install inst, Config cfg, Action<string> log, IProgress<int> p = null) {
        var db = await ModDb.Get(); int n = 0;
        foreach (var m in Mods.Scan(inst).Where(m => !m.IsDir && db.TryGetValue(m.Name, out var e) && ModDb.Newer(m.Version, e.Version))) { try { await Zip(inst, m, db[m.Name], cfg, log, p); n++; } catch (Exception ex) { log("  " + ex.Message); } }
        return n;
    }
}
