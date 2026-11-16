using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MountZantas;

class Install { public string Name { get; set; } public string Path { get; set; } public string Store { get; set; } }

class Config {
    public List<Install> Installs { get; set; } = new();
    public int Selected { get; set; }
    public HashSet<string> Favorites { get; set; } = new();
    public Dictionary<string, List<string>> Presets { get; set; } = new();
    public bool UpdateOnStartup { get; set; }
    public bool CloseOnLaunch { get; set; }
    public bool AutoUpdateApp { get; set; } = true;
    public string Mirror { get; set; } = "0x0a.de";
    public string Accent { get; set; } = "#6a4cff";
    public string LastMap { get; set; }

    // MOUNTZANTAS_DATA relocates all settings, caches and tools (used for safe testing against a scratch Celeste copy).
    public static string DataDir => Environment.GetEnvironmentVariable("MOUNTZANTAS_DATA") is { Length: > 0 } d ? d : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MountZantas");
    static string FilePath => System.IO.Path.Combine(DataDir, "config.json");
    public static Config Load() { try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); } }
    public void Save() { try { Directory.CreateDirectory(DataDir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { } }
    public Install Current => Installs.Count == 0 ? null : Installs[Math.Clamp(Selected, 0, Installs.Count - 1)];
}

static class Net {
    public static readonly HttpClient Http = Make();
    static HttpClient Make() { var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("MountZantas/0.1"); return c; }

    // Downloads with up to 3 attempts: CDN nodes occasionally drop a TLS handshake or connection, and a retry usually succeeds.
    public static async Task Download(string url, string dest, IProgress<int> p = null) {
        for (int attempt = 1; ; attempt++) {
            try { await DownloadOnce(url, dest, p); return; }
            catch (Exception ex) when (attempt < 3 && IsTransient(ex)) { try { File.Delete(dest); } catch { } await Task.Delay(1200 * attempt); }
            catch (Exception ex) when (IsTransient(ex)) { try { File.Delete(dest); } catch { } throw new Exception("The download server dropped the connection three times in a row (" + (ex.InnerException?.Message ?? ex.Message) + "). Please try again in a moment."); }
        }
    }
    static bool IsTransient(Exception ex) {
        if (ex is HttpRequestException h) return h.StatusCode == null || (int)h.StatusCode >= 500 || (int)h.StatusCode == 429;   // no 4xx retries except "too many requests"
        return ex is IOException || ex is TaskCanceledException || ex is System.Security.Authentication.AuthenticationException;
    }
    static async Task DownloadOnce(string url, string dest, IProgress<int> p) {
        using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead); r.EnsureSuccessStatusCode();
        long total = r.Content.Headers.ContentLength ?? -1, done = 0;
        using var s = await r.Content.ReadAsStreamAsync(); using var f = File.Create(dest);
        var buf = new byte[81920]; int n;
        while ((n = await s.ReadAsync(buf)) > 0) { await f.WriteAsync(buf.AsMemory(0, n)); done += n; if (total > 0) p?.Report((int)(done * 100 / total)); }
    }
    public static string XxHash(string file) { var h = new System.IO.Hashing.XxHash64(); using var s = File.OpenRead(file); h.Append(s); return Convert.ToHexString(h.GetCurrentHash()).ToLowerInvariant(); }
    public static string Sha256(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)).ToLowerInvariant(); }

    // SHA-256 that GitHub publishes for a release asset, or null.
    public static async Task<string> GitHubDigest(string tagApiUrl, string assetName) {
        var j = JsonDocument.Parse(await Http.GetStringAsync(tagApiUrl)).RootElement;
        foreach (var a in j.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == assetName && a.TryGetProperty("digest", out var d) && d.GetString() is string s && s.StartsWith("sha256:")) return s.Substring(7);
        return null;
    }
}

record ModEntry(string Name, string Version, long Updated, string Mirror, string Url, string[] Hashes, long Size);

static class ModDb {
    const string Url = "https://maddie480.ovh/celeste/everest_update.yaml";
    static Dictionary<string, ModEntry> cache;
    public static async Task<Dictionary<string, ModEntry>> Get(bool force = false) {
        if (cache != null && !force) return cache;
        var y = await Net.Http.GetStringAsync(Url);
        var d = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        var rx = new Regex(@"^(?<n>[^\s#][^\n]*?):\r?\n  Version: (?<v>[^\n\r]+)\r?\n  LastUpdate: (?<t>\d+)\r?\n  MirrorName: '?(?<m>[^'\r\n]+)'?\r?\n  URL: (?<u>[^\r\n]+)\r?\n  xxHash:\r?\n(?<h>(?:  - [0-9a-fA-F]+\r?\n)+)(?:  Size: (?<s>\d+))?", RegexOptions.Multiline);
        foreach (Match m in rx.Matches(y)) {
            var hashes = Regex.Matches(m.Groups["h"].Value, @"- ([0-9a-fA-F]+)").Select(x => x.Groups[1].Value).ToArray();
            var name = m.Groups["n"].Value.Trim('\'', '"');
            d[name] = new ModEntry(name, m.Groups["v"].Value.Trim(), long.Parse(m.Groups["t"].Value), m.Groups["m"].Value.Trim(), m.Groups["u"].Value.Trim(), hashes, m.Groups["s"].Success ? long.Parse(m.Groups["s"].Value) : 0);
        }
        return cache = d;
    }
    public static bool Newer(string have, string latest) => Version.TryParse(Clean(have), out var a) && Version.TryParse(Clean(latest), out var b) && b > a;
    static string Clean(string v) { var m = Regex.Match(v ?? "", @"\d+(\.\d+){0,3}"); return m.Success ? m.Value : ""; }
}

class ModInfo { public string File, Name, Version; public bool IsDir, Enabled; public string[] Deps = Array.Empty<string>(); }

static class Mods {
    public static string Dir(Install i) => Path.Combine(i.Path, "Mods");
    static string BlacklistPath(Install i) => Path.Combine(Dir(i), "blacklist.txt");

    public static (string name, string ver, string[] deps) ParseYaml(string y) {
        var n = Regex.Match(y, @"^\s*-?\s*Name:\s*(.+)$", RegexOptions.Multiline); var v = Regex.Match(y, @"^\s*Version:\s*(.+)$", RegexOptions.Multiline);
        var deps = new List<string>(); int i = y.IndexOf("Dependencies:");
        if (i >= 0) { var b = y.Substring(i); int o = b.IndexOf("OptionalDependencies:"); if (o >= 0) b = b.Substring(0, o); foreach (Match m in Regex.Matches(b, @"-\s*Name:\s*(.+)")) deps.Add(m.Groups[1].Value.Trim()); }
        return (n.Success ? n.Groups[1].Value.Trim() : null, v.Success ? v.Groups[1].Value.Trim() : "", deps.ToArray());
    }
    public static string YamlOfZip(string zip) { using var z = ZipFile.OpenRead(zip); var e = z.GetEntry("everest.yaml") ?? z.GetEntry("everest.yml"); if (e == null) return ""; using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }

    public static HashSet<string> Disabled(Install i) {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var l in File.ReadAllLines(BlacklistPath(i))) { var t = l.Trim(); if (t.Length > 0 && !t.StartsWith("#")) s.Add(t); } } catch { }
        return s;
    }
    public static void WriteDisabled(Install i, IEnumerable<string> disabled) {
        var lines = new List<string> { "# This is the blacklist. Lines starting with # are ignored.", "# File generated by Mount Zantas", "" };
        lines.AddRange(disabled.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        File.WriteAllLines(BlacklistPath(i), lines);
    }

    // After some mods were just disabled: the enabled mods that they needed (directly or through other helpers)
    // and that no remaining enabled mod requires any more.
    public static List<ModInfo> UnusedAfterDisable(List<ModInfo> all, IEnumerable<ModInfo> justDisabled) {
        var by = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase); foreach (var m in all) by[m.Name] = m;
        var cand = new HashSet<ModInfo>(); var todo = new Stack<ModInfo>(justDisabled);
        while (todo.Count > 0) foreach (var d in todo.Pop().Deps) if (!d.StartsWith("Everest", StringComparison.OrdinalIgnoreCase) && by.TryGetValue(d, out var dm) && dm.Enabled && cand.Add(dm)) todo.Push(dm);
        bool changed = true;   // a candidate stays if an enabled non-candidate (or a kept candidate) still needs it
        while (changed) {
            changed = false;
            foreach (var c in cand.ToList()) {
                bool needed = all.Any(m => m.Enabled && m != c && !cand.Contains(m) && m.Deps.Any(d => d.Equals(c.Name, StringComparison.OrdinalIgnoreCase)));
                if (needed) { cand.Remove(c); changed = true; }
            }
        }
        return cand.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<ModInfo> Scan(Install i) {
        var res = new List<ModInfo>(); var dir = Dir(i); if (!Directory.Exists(dir)) return res; var off = Disabled(i);
        foreach (var z in Directory.GetFiles(dir, "*.zip")) {
            try { var (n, v, d) = ParseYaml(YamlOfZip(z)); res.Add(new ModInfo { File = Path.GetFileName(z), Name = n ?? Path.GetFileNameWithoutExtension(z), Version = v, Deps = d }); }
            catch { res.Add(new ModInfo { File = Path.GetFileName(z), Name = Path.GetFileNameWithoutExtension(z), Version = "?" }); }
        }
        foreach (var d in Directory.GetDirectories(dir)) {
            var name = Path.GetFileName(d); if (name.Equals("Cache", StringComparison.OrdinalIgnoreCase)) continue;
            var y = new[] { "everest.yaml", "everest.yml" }.Select(f => Path.Combine(d, f)).FirstOrDefault(File.Exists);
            if (y == null) continue; var (n, v, deps) = ParseYaml(File.ReadAllText(y));
            res.Add(new ModInfo { File = name, Name = n ?? name, Version = v, IsDir = true, Deps = deps });
        }
        foreach (var m in res) m.Enabled = !off.Contains(m.File);
        return res.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Download a mod from the public database (checksum verified), then any missing required dependencies.
    public static async Task InstallWithDeps(Install inst, string name, Config cfg, Action<string> log, IProgress<int> prog = null, HashSet<string> seen = null) {
        seen ??= new(StringComparer.OrdinalIgnoreCase); if (!seen.Add(name)) return;
        var db = await ModDb.Get(); if (!db.TryGetValue(name, out var e)) { log($"  ! {name} is not in the mod database"); return; }
        Directory.CreateDirectory(Dir(inst)); var file = Path.Combine(Dir(inst), name + ".zip");
        log($"Downloading {name} {e.Version}");
        var sources = cfg.Mirror == "gamebanana" ? new[] { e.Url } : new[] { "https://celestemodupdater.0x0a.de/banana-mirror/" + e.Mirror + ".zip", e.Url };
        bool ok = false;
        foreach (var s in sources) {
            try { await Net.Download(s, file, prog); if (e.Hashes.Contains(Net.XxHash(file), StringComparer.OrdinalIgnoreCase)) { ok = true; break; } log("  checksum mismatch, trying next source"); }
            catch (Exception ex) { log("  download failed: " + ex.Message); }
            try { File.Delete(file); } catch { }
        }
        if (!ok) throw new Exception($"{name} could not be downloaded and verified.");
        log($"  verified {name}");
        var have = Scan(inst).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in ParseYaml(YamlOfZip(file)).deps)
            if (dep != "Everest" && dep != "EverestCore" && !have.Contains(dep)) await InstallWithDeps(inst, dep, cfg, log, null, seen);
    }
}

record EverestBuild(int Version, string Branch, string Download, DateTime Date, string Author, string Description);

static class EverestSvc {
    const string Feed = "https://maddie480.ovh/celeste/everest-versions";
    public static async Task<List<EverestBuild>> Versions() {
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync(Feed)).RootElement; var l = new List<EverestBuild>();
        foreach (var e in j.EnumerateArray())
            l.Add(new EverestBuild(e.GetProperty("version").GetInt32(), e.GetProperty("branch").GetString(), e.GetProperty("mainDownload").GetString(),
                DateTime.TryParse(e.GetProperty("date").GetString(), out var d) ? d.ToLocalTime() : default,
                e.TryGetProperty("author", out var a) ? a.GetString() : "", e.TryGetProperty("description", out var ds) ? ds.GetString() : ""));
        return l.OrderByDescending(x => x.Version).ToList();
    }
    public static string CelesteVersion(Install i) {
        foreach (var f in new[] { "Celeste.dll", "Celeste.exe" }) { try { var p = Path.Combine(i.Path, f); if (File.Exists(p) && FileVersionInfo.GetVersionInfo(p).FileVersion is string v && v != "1.0.0.0") return v; } catch { } }
        return "1.4.0.0";   // Celeste's only released version; patched copies report a launcher stub version
    }
    public static string EverestVersion(Install i) {
        var p = Path.Combine(i.Path, "Celeste.Mod.mm.dll"); if (!File.Exists(p)) return null;
        try { var b = File.ReadAllText(Path.Combine(i.Path, "update-build.txt")).Trim(); if (int.TryParse(b, out var n) && n > 0) return n.ToString(); } catch { }
        return "(build unknown)";   // the DLL's own version number is not the Everest build number, so don't show it as one
    }

    public static async Task Install(Install inst, EverestBuild b, Action<string> log, IProgress<int> prog, bool allowUnverified) {
        var tmp = Path.Combine(Path.GetTempPath(), "mountzantas-everest.zip");
        log($"Downloading Everest {b.Version} ({b.Branch})...");
        await Net.Download(b.Download, tmp, prog);
        var m = Regex.Match(b.Download, @"github\.com/EverestAPI/Everest/releases/download/([^/]+)/main\.zip");
        string want = m.Success ? await Net.GitHubDigest("https://api.github.com/repos/EverestAPI/Everest/releases/tags/" + m.Groups[1].Value, "main.zip") : null;
        if (want != null) {
            if (!Net.Sha256(tmp).Equals(want, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); throw new Exception("Everest download failed its checksum. Nothing was installed."); }
            log("Download verified (SHA-256).");
        } else if (!allowUnverified) { File.Delete(tmp); throw new Exception("This build has no published checksum (beta/dev builds come from Azure DevOps). Tick \"Allow unverified builds\" to install it anyway."); }
        else log("WARNING: installing a build with no published checksum.");
        log("Extracting...");
        var root = Path.GetFullPath(inst.Path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        await Task.Run(() => {
            using var zf = ZipFile.OpenRead(tmp);
            foreach (var e in zf.Entries) {
                var rel = e.FullName; if (rel.StartsWith("main/")) rel = rel.Substring(5); if (rel.Length == 0 || rel.EndsWith("/")) continue;
                var dest = Path.GetFullPath(Path.Combine(inst.Path, rel)); if (!dest.StartsWith(root, Platform.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;   // zip-slip guard
                Directory.CreateDirectory(Path.GetDirectoryName(dest)); e.ExtractToFile(dest, true);
            }
        });
        File.Delete(tmp);
        log("Running the Everest patcher...");
        var exe = Platform.MiniInstallerPath(inst.Path); if (!File.Exists(exe)) throw new Exception("The Everest patcher for this system (" + Path.GetFileName(exe) + ") was not in the download.");
        Platform.MakeExecutable(exe);
        var p = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = inst.Path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        var o = await p.StandardOutput.ReadToEndAsync(); await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new Exception("Patching failed:\r\n" + o.Substring(Math.Max(0, o.Length - 600)));
        if (!File.Exists(Path.Combine(inst.Path, "Celeste.Mod.mm.dll"))) throw new Exception("The patcher finished but Everest is not present; the install did not complete.");
        File.WriteAllText(Path.Combine(inst.Path, "update-build.txt"), b.Version.ToString());   // the same marker Olympus writes, so the build number is shown correctly
        log("Everest installed (build " + b.Version + ").");
    }

    // Everest keeps the untouched game files in "orig"; restoring them and removing Celeste.Mod.mm.dll returns to vanilla.
    public static void Uninstall(Install inst, Action<string> log) {
        var orig = Path.Combine(inst.Path, "orig"); if (!Directory.Exists(orig)) throw new Exception("No 'orig' backup folder found, so Everest can't be removed safely.");
        foreach (var f in Directory.GetFiles(orig)) File.Copy(f, Path.Combine(inst.Path, Path.GetFileName(f)), true);
        var mm = Path.Combine(inst.Path, "Celeste.Mod.mm.dll"); if (File.Exists(mm)) File.Delete(mm);
        var ub = Path.Combine(inst.Path, "update-build.txt"); if (File.Exists(ub)) File.Delete(ub);
        log("Everest removed. Your Mods and Saves folders were not touched.");
    }
}

// GameBanana browser (public API). Every file has a published MD5 that is checked before anything is installed.
record GbMod(int Id, string Name, string Submitter, string Category, int Views, int Likes, DateTime Updated, string Url, string Thumb, DateTime Added);
record GbFile(long Id, string Name, DateTime Date, int Downloads, long Size, string Description, string Md5, string DownloadUrl);
record GbProfile(string Name, string Url, string Submitter, string Text, List<GbFile> Files, List<string> Images, int Likes, int Downloads, DateTime Added);

static class GameBanana {
    const string Api = "https://gamebanana.com/apiv11/"; const int CelesteGame = 6460;
    static DateTime Unix(long s) => DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;
    static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
    static int Int(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    public static async Task<(List<GbMod> items, int total)> Search(string q, int page) {
        var url = string.IsNullOrWhiteSpace(q)
            ? $"{Api}Mod/Index?_nPage={page}&_nPerpage=30&_aFilters%5BGeneric_Game%5D={CelesteGame}&_sSort=Generic_MostDownloaded"
            : $"{Api}Util/Search/Results?_sSearchString={Uri.EscapeDataString(q.Trim())}&_idGameRow={CelesteGame}&_nPage={page}&_nPerpage=30";
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync(url)).RootElement; var list = new List<GbMod>();
        foreach (var r in j.GetProperty("_aRecords").EnumerateArray()) {
            if (Str(r, "_sModelName") != "Mod") continue;
            string sub = r.TryGetProperty("_aSubmitter", out var s) ? Str(s, "_sName") : "", cat = r.TryGetProperty("_aRootCategory", out var c) ? Str(c, "_sName") : "";
            long upd = r.TryGetProperty("_tsDateModified", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt64() : 0;
            long added = r.TryGetProperty("_tsDateAdded", out var da) && da.ValueKind == JsonValueKind.Number ? da.GetInt64() : 0;
            list.Add(new GbMod(r.GetProperty("_idRow").GetInt32(), Str(r, "_sName"), sub, cat, Int(r, "_nViewCount"), Int(r, "_nLikeCount"), upd > 0 ? Unix(upd) : default, Str(r, "_sProfileUrl"), PreviewUrls(r, "_sFile220").FirstOrDefault(), added > 0 ? Unix(added) : default));
        }
        int total = j.TryGetProperty("_aMetadata", out var md) && md.TryGetProperty("_nRecordCount", out var rc) && rc.ValueKind == JsonValueKind.Number ? rc.GetInt32() : list.Count;
        return (list, total);
    }

    public static async Task<GbProfile> Profile(int id) {
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync($"{Api}Mod/{id}/ProfilePage")).RootElement; var files = new List<GbFile>();
        if (j.TryGetProperty("_aFiles", out var fs) && fs.ValueKind == JsonValueKind.Array)
            foreach (var f in fs.EnumerateArray())
                files.Add(new GbFile(f.GetProperty("_idRow").GetInt64(), Str(f, "_sFile"), f.TryGetProperty("_tsDateAdded", out var d) && d.ValueKind == JsonValueKind.Number ? Unix(d.GetInt64()) : default,
                    Int(f, "_nDownloadCount"), f.TryGetProperty("_nFilesize", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0, Str(f, "_sDescription"), Str(f, "_sMd5Checksum"), Str(f, "_sDownloadUrl")));
        var sub = j.TryGetProperty("_aSubmitter", out var s) ? Str(s, "_sName") : "";
        long added = j.TryGetProperty("_tsDateAdded", out var da) && da.ValueKind == JsonValueKind.Number ? da.GetInt64() : 0;
        return new GbProfile(Str(j, "_sName"), Str(j, "_sProfileUrl"), sub, HtmlToText(Str(j, "_sText")), files.OrderByDescending(f => f.Date).ToList(), PreviewUrls(j, "_sFile530").ToList(), Int(j, "_nLikeCount"), Int(j, "_nDownloadCount"), added > 0 ? Unix(added) : default);
    }

    // Preview screenshot URLs (e.g. field "_sFile220" = 220px thumbnail, "_sFile530" = 530px). Only images.gamebanana.com is used.
    public static IEnumerable<string> PreviewUrls(JsonElement rec, string field) {
        if (!rec.TryGetProperty("_aPreviewMedia", out var pm) || pm.ValueKind != JsonValueKind.Object || !pm.TryGetProperty("_aImages", out var ims) || ims.ValueKind != JsonValueKind.Array) yield break;
        foreach (var im in ims.EnumerateArray()) {
            var b = Str(im, "_sBaseUrl"); var f = Str(im, field); if (string.IsNullOrEmpty(f)) f = Str(im, "_sFile");
            if (!string.IsNullOrEmpty(b) && !string.IsNullOrEmpty(f)) yield return b.TrimEnd('/') + "/" + f;
        }
    }

    public static string HtmlToText(string html) {
        if (string.IsNullOrEmpty(html)) return "";
        var t = Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h\d)\s*/?>", "\n", RegexOptions.IgnoreCase); t = Regex.Replace(t, @"<\s*li[^>]*>", "• ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<[^>]+>", ""); t = System.Net.WebUtility.HtmlDecode(t); t = Regex.Replace(t, @"[ \t]+\n", "\n"); t = Regex.Replace(t, @"\n{3,}", "\n\n"); return t.Trim();
    }

    public static string Md5(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(System.Security.Cryptography.MD5.HashData(s)).ToLowerInvariant(); }

    // Downloads to dest and verifies GameBanana's published MD5. Deletes the file and throws on any mismatch.
    public static async Task DownloadVerified(GbFile f, string dest, IProgress<int> prog) {
        if (string.IsNullOrEmpty(f.Md5)) throw new Exception("This file has no published checksum, so it won't be installed.");
        if (!f.DownloadUrl.StartsWith("https://gamebanana.com/")) throw new Exception("Unexpected download location; refusing.");
        await Net.Download(f.DownloadUrl, dest, prog);
        if (!Md5(dest).Equals(f.Md5, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(dest); } catch { } throw new Exception("Download failed its checksum. Nothing was installed."); }
    }

    public static async Task InstallFile(Install inst, GbFile f, Config cfg, Action<string> log, IProgress<int> prog) {
        Directory.CreateDirectory(Mods.Dir(inst)); var safe = Path.GetFileName(f.Name); if (!safe.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new Exception("Only .zip mod files can be installed.");
        var tmp = Path.Combine(Path.GetTempPath(), "mz-gb-" + f.Id + ".zip"); log($"Downloading {safe}...");
        await DownloadVerified(f, tmp, prog); log("Verified (MD5 published by GameBanana).");
        string yaml; try { yaml = Mods.YamlOfZip(tmp); } catch { yaml = ""; }
        if (!yaml.Contains("Name:")) { File.Delete(tmp); throw new Exception("That zip has no everest.yaml, so it isn't an Everest mod. Nothing was installed."); }
        File.Move(tmp, Path.Combine(Mods.Dir(inst), safe), true); log($"Installed {safe}.");
        var have = Mods.Scan(inst).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in Mods.ParseYaml(yaml).deps) if (dep != "Everest" && dep != "EverestCore" && !have.Contains(dep)) await Mods.InstallWithDeps(inst, dep, cfg, log, null, seen);
    }
}

// Community news (the same public feed Olympus shows).
record NewsItem(string Title, string Text, string Image, string Link);
static class News {
    public static async Task<List<NewsItem>> Get() {
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync("https://maddie480.ovh/celeste/olympus-news.json")).RootElement; var l = new List<NewsItem>();
        foreach (var e in j.EnumerateArray()) {
            string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
            l.Add(new NewsItem(S("title"), S("shortDescription"), S("image"), S("link")));
        }
        return l;
    }
}

// Topo: the CEL map editor (github.com/Magedeline/Topo). Installed per user, launched with CELESTE_DIR pointing at the selected install.
static class Topo {
    // Topo is a Windows (WinForms) program, so it is only offered on Windows.
    public static bool Supported => Platform.Windows;
    static void Need() { if (!Supported) throw new Exception("Topo is only available on Windows."); }
    public static string Dir => Path.Combine(Config.DataDir, "topo");
    public static string Installed() { try { return File.ReadAllText(Path.Combine(Dir, "version.txt")).Trim(); } catch { return null; } }
    public static async Task<(string tag, string url, string sha)> Latest(bool ai) {
        var j = JsonDocument.Parse(await Net.Http.GetStringAsync("https://api.github.com/repos/Magedeline/Topo/releases/latest")).RootElement;
        var rx = new Regex(ai ? @"^Topo-AI-.+-win-x64\.zip$" : @"^Topo-\d.+-win-x64\.zip$");
        foreach (var a in j.GetProperty("assets").EnumerateArray()) {
            var n = a.GetProperty("name").GetString(); if (!rx.IsMatch(n)) continue;
            return (j.GetProperty("tag_name").GetString(), a.GetProperty("browser_download_url").GetString(), a.TryGetProperty("digest", out var d) ? d.GetString()?.Replace("sha256:", "") : null);
        }
        throw new Exception("No matching Topo download found in the latest release.");
    }
    public static async Task Install(bool ai, Action<string> log, IProgress<int> prog) {
        Need(); var (tag, url, sha) = await Latest(ai); if (sha == null) throw new Exception("The Topo release has no published checksum; refusing to install it.");
        var tmp = Path.Combine(Path.GetTempPath(), "mountzantas-topo.zip"); log($"Downloading Topo {tag}{(ai ? " (+ AI)" : "")}..."); await Net.Download(url, tmp, prog);
        if (!Net.Sha256(tmp).Equals(sha, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); throw new Exception("Topo download failed its checksum. Nothing was installed."); }
        log("Verified (SHA-256)."); Extract(tmp, tag + (ai ? " + AI" : ""), log); File.Delete(tmp);
    }
    public static void InstallLocal(string zip, Action<string> log) { log("Installing from a local file (no published checksum to compare against)..."); Extract(zip, "local file", log); }
    static void Extract(string zip, string label, Action<string> log) {
        using (var z = ZipFile.OpenRead(zip)) if (z.GetEntry("Topo.exe") == null) throw new Exception("That zip doesn't contain Topo.exe at its top level.");
        if (Directory.Exists(Dir)) Directory.Delete(Dir, true); Directory.CreateDirectory(Dir);
        ZipFile.ExtractToDirectory(zip, Dir, true); File.WriteAllText(Path.Combine(Dir, "version.txt"), label); log("Topo installed.");
    }
    public static void Launch(Install inst, string map = null) {
        Need(); var exe = Path.Combine(Dir, "Topo.exe"); if (!File.Exists(exe)) throw new Exception("Topo is not installed yet.");
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = Dir, UseShellExecute = false };
        if (map != null) { psi.ArgumentList.Add("--map"); psi.ArgumentList.Add(map); }   // Topo: --map <file.bin>
        if (inst != null) psi.Environment["CELESTE_DIR"] = inst.Path;   // Topo checks CELESTE_DIR when locating the game
        Process.Start(psi);
    }
}

