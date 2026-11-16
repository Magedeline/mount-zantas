using System.IO.Compression;
using System.Text;

namespace MountZantas;

// ---------- Celeste .bin map reader (just enough for an overview image) ----------
class MapElement {
    public string Name; public Dictionary<string, object> Attrs = new(); public List<MapElement> Children = new();
    public string Str(string k) => Attrs.TryGetValue(k, out var v) ? v?.ToString() : null;
    public int Int(string k) => Attrs.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 0;
}

record MapRoom(string Name, int X, int Y, int W, int H, string Solids);   // pixels; solids = tile grid text
record MapInfo(List<MapRoom> Rooms, List<Rect> Fillers, string Package);
record struct Rect(int X, int Y, int Width, int Height);

// A rendered overview: BGRA pixels, 4 bytes each, row by row.
record RawImage(byte[] Bgra, int Width, int Height, string Info);

static class MapBin {
    public static MapInfo Read(Stream s) {
        using var r = new BinaryReader(s, Encoding.UTF8, true);
        if (r.ReadString() != "CELESTE MAP") throw new Exception("Not a Celeste .bin map.");
        string package = r.ReadString(); var lookup = new string[r.ReadInt16()]; for (int i = 0; i < lookup.Length; i++) lookup[i] = r.ReadString();
        var root = Elem(r, lookup); var rooms = new List<MapRoom>(); var fill = new List<Rect>();
        foreach (var c in root.Children) {
            if (c.Name == "levels") foreach (var l in c.Children.Where(x => x.Name == "level")) rooms.Add(new MapRoom(l.Str("name") ?? "", l.Int("x"), l.Int("y"), l.Int("width"), l.Int("height"), l.Children.FirstOrDefault(x => x.Name == "solids")?.Str("innerText") ?? ""));
            else if (c.Name == "Filler") foreach (var rc in c.Children.Where(x => x.Name == "rect")) fill.Add(new Rect(rc.Int("x"), rc.Int("y"), rc.Int("w"), rc.Int("h")));
        }
        return new MapInfo(rooms, fill, package);
    }

    static MapElement Elem(BinaryReader r, string[] lookup) {
        var e = new MapElement { Name = lookup[r.ReadInt16()] }; int attrs = r.ReadByte();
        for (int i = 0; i < attrs; i++) {
            string key = lookup[r.ReadInt16()]; byte type = r.ReadByte(); object v;
            switch (type) {
                case 0: v = r.ReadBoolean(); break; case 1: v = (int)r.ReadByte(); break; case 2: v = (int)r.ReadInt16(); break; case 3: v = r.ReadInt32(); break; case 4: v = r.ReadSingle(); break;
                case 5: v = lookup[r.ReadInt16()]; break; case 6: v = r.ReadString(); break;
                case 7: { int n = r.ReadInt16(); var b = r.ReadBytes(n); var sb = new StringBuilder(); for (int k = 0; k + 1 < n; k += 2) sb.Append((char)b[k + 1], b[k]); v = sb.ToString(); break; }
                default: throw new Exception("Unknown attribute type " + type);
            }
            e.Attrs[key] = v;
        }
        int kids = r.ReadInt16(); for (int i = 0; i < kids; i++) e.Children.Add(Elem(r, lookup)); return e;
    }

    // One pixel per tile (8x8 px) so even 100+ room maps stay small; very large maps are sampled. accent* tints the solid tiles.
    public static RawImage Render(MapInfo m, byte accentR, byte accentG, byte accentB) {
        if (m.Rooms.Count == 0 && m.Fillers.Count == 0) throw new Exception("This map has no rooms.");
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        void Grow(int x, int y, int w, int h) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x + w); maxY = Math.Max(maxY, y + h); }
        foreach (var r in m.Rooms) Grow(FloorDiv(r.X, 8), FloorDiv(r.Y, 8), (r.W + 7) / 8, (r.H + 7) / 8); foreach (var f in m.Fillers) Grow(f.X, f.Y, f.Width, f.Height);
        int TW = maxX - minX, TH = maxY - minY; if (TW <= 0 || TH <= 0) throw new Exception("This map has no usable size.");
        int k = (long)TW * TH > 6_000_000 ? (int)Math.Ceiling(Math.Sqrt((double)TW * TH / 6_000_000.0)) : 1; if ((long)TW * TH / ((long)k * k) > 20_000_000) throw new Exception("Map is too large to preview.");
        int W = (TW + k - 1) / k, H = (TH + k - 1) / k; var buf = new byte[W * H * 4];
        (byte r, byte g, byte b) bg = (0x14, 0x15, 0x1e), filler = (0x24, 0x26, 0x38), room = (0x2c, 0x30, 0x4a), solid = ((byte)Math.Min(255, accentR / 2 + 150), (byte)Math.Min(255, accentG / 2 + 150), (byte)Math.Min(255, accentB / 2 + 150));
        void Put(int x, int y, (byte r, byte g, byte b) c) { int i = (y * W + x) * 4; buf[i] = c.b; buf[i + 1] = c.g; buf[i + 2] = c.r; buf[i + 3] = 255; }
        void Fill(int x, int y, int w, int h, (byte, byte, byte) c) { for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++) Put(xx, yy, c); }
        void FillT(int tx, int ty, int tw, int th, (byte, byte, byte) c) { int x0 = tx / k, y0 = ty / k; Fill(x0, y0, Math.Max(1, (tx + tw + k - 1) / k - x0), Math.Max(1, (ty + th + k - 1) / k - y0), c); }   // tile units -> pixels
        Fill(0, 0, W, H, bg); foreach (var f in m.Fillers) FillT(f.X - minX, f.Y - minY, f.Width, f.Height, filler);
        foreach (var r in m.Rooms) {
            int rx = FloorDiv(r.X, 8) - minX, ry = FloorDiv(r.Y, 8) - minY; FillT(rx, ry, (r.W + 7) / 8, (r.H + 7) / 8, room); int row = 0;
            foreach (var line in r.Solids.Split('\n')) { var l = line.TrimEnd('\r'); for (int c = 0; c < l.Length; c++) if (l[c] != '0' && l[c] != ' ') { int x = (rx + c) / k, y = (ry + row) / k; if (x >= 0 && y >= 0 && x < W && y < H) Put(x, y, solid); } row++; }
        }
        return new RawImage(buf, W, H, $"{m.Rooms.Count} room(s)  ·  {TW} × {TH} tiles" + (k > 1 ? $"  ·  overview (1 px = {k} tiles)" : ""));
    }
    static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}

// ---------- Where maps can come from ----------
record MapSource(string Mod, string Display, string Path, string Zip, string Entry) { public bool Loose => Path != null; }

static class MapFinder {
    public static List<MapSource> Scan(Install inst, int limit = 3000) {
        var list = new List<MapSource>(); if (inst == null) return list; var dir = Mods.Dir(inst); if (!Directory.Exists(dir)) return list;
        foreach (var d in Directory.GetDirectories(dir)) {
            var maps = System.IO.Path.Combine(d, "Maps"); if (!Directory.Exists(maps)) continue; var mod = System.IO.Path.GetFileName(d);
            try { foreach (var f in Directory.EnumerateFiles(maps, "*.bin", SearchOption.AllDirectories)) { if (list.Count >= limit) return list; list.Add(new MapSource(mod, f.Substring(maps.Length + 1), f, null, null)); } } catch { }
        }
        foreach (var z in Directory.GetFiles(dir, "*.zip")) {
            try {
                using var zf = ZipFile.OpenRead(z); var mod = System.IO.Path.GetFileNameWithoutExtension(z);
                foreach (var e in zf.Entries) if (e.FullName.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) { if (list.Count >= limit) return list; list.Add(new MapSource(mod, e.FullName.Substring(5), null, z, e.FullName)); }
            } catch { }
        }
        return list;
    }

    // ----- Opening a map that lives inside a zip: extract just that one .bin into our own maps folder (the zip is never changed) -----
    public static string CopyPath(MapSource s) {
        var safeMod = string.Concat((s.Mod.Length > 0 ? s.Mod : "zip").Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Config.DataDir, "maps", safeMod)); var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, s.Display.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!dest.StartsWith(root + System.IO.Path.DirectorySeparatorChar, Platform.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new Exception("That map has an unsafe path inside the zip; refusing to extract it.");
        return dest;
    }
    static byte[] ZipBytes(MapSource s) { using var zf = ZipFile.OpenRead(s.Zip); using var st = zf.GetEntry(s.Entry).Open(); using var ms = new MemoryStream(); st.CopyTo(ms); return ms.ToArray(); }
    public static bool SameAsZip(MapSource s, string dest) => File.Exists(dest) && File.ReadAllBytes(dest).AsSpan().SequenceEqual(ZipBytes(s));
    public static string UniquePath(string dest) { var d = System.IO.Path.GetDirectoryName(dest); var n = System.IO.Path.GetFileNameWithoutExtension(dest); var e = System.IO.Path.GetExtension(dest); for (int i = 2; ; i++) { var p = System.IO.Path.Combine(d, $"{n} ({i}){e}"); if (!File.Exists(p)) return p; } }
    public static void ExtractTo(MapSource s, string dest) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)); File.WriteAllBytes(dest, ZipBytes(s)); }

    public static MapInfo Load(MapSource s) {
        if (s.Loose) { using var fs = File.OpenRead(s.Path); return MapBin.Read(fs); }
        using var zf = ZipFile.OpenRead(s.Zip); using var st = zf.GetEntry(s.Entry).Open(); using var ms = new MemoryStream(); st.CopyTo(ms); ms.Position = 0; return MapBin.Read(ms);
    }
}
