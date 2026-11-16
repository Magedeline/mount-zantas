using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;

namespace MountZantas;

// ---------- Celeste .bin map reader (just enough for an overview image) ----------
class MapElement { public string Name; public Dictionary<string, object> Attrs = new(); public List<MapElement> Children = new(); public string Str(string k) => Attrs.TryGetValue(k, out var v) ? v?.ToString() : null; public int Int(string k) => Attrs.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 0; }

record MapRoom(string Name, int X, int Y, int W, int H, string Solids);   // pixels; solids = tile grid text
record MapInfo(List<MapRoom> Rooms, List<Rectangle> Fillers, string Package);

static class MapBin {
    public static MapInfo Read(Stream s) {
        using var r = new BinaryReader(s, Encoding.UTF8, true);
        if (r.ReadString() != "CELESTE MAP") throw new Exception("Not a Celeste .bin map.");
        string package = r.ReadString(); var lookup = new string[r.ReadInt16()]; for (int i = 0; i < lookup.Length; i++) lookup[i] = r.ReadString();
        var root = Elem(r, lookup); var rooms = new List<MapRoom>(); var fill = new List<Rectangle>();
        foreach (var c in root.Children) {
            if (c.Name == "levels") foreach (var l in c.Children.Where(x => x.Name == "level")) rooms.Add(new MapRoom(l.Str("name") ?? "", l.Int("x"), l.Int("y"), l.Int("width"), l.Int("height"), l.Children.FirstOrDefault(x => x.Name == "solids")?.Str("innerText") ?? ""));
            else if (c.Name == "Filler") foreach (var rc in c.Children.Where(x => x.Name == "rect")) fill.Add(new Rectangle(rc.Int("x"), rc.Int("y"), rc.Int("w"), rc.Int("h")));
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

    // One pixel per tile (8x8 px) so even 100+ room maps stay small; the view scales it to fit.
    public static (Bitmap bmp, string info) Render(MapInfo m, Color accent) {
        if (m.Rooms.Count == 0 && m.Fillers.Count == 0) throw new Exception("This map has no rooms.");
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        void Grow(int x, int y, int w, int h) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x + w); maxY = Math.Max(maxY, y + h); }
        foreach (var r in m.Rooms) Grow(FloorDiv(r.X, 8), FloorDiv(r.Y, 8), (r.W + 7) / 8, (r.H + 7) / 8); foreach (var f in m.Fillers) Grow(f.X, f.Y, f.Width, f.Height);
        int TW = maxX - minX, TH = maxY - minY; if (TW <= 0 || TH <= 0) throw new Exception("This map has no usable size.");
        // Normally 1 pixel per tile; very large maps are sampled (k tiles per pixel) so the image stays a sensible size.
        int k = (long)TW * TH > 6_000_000 ? (int)Math.Ceiling(Math.Sqrt((double)TW * TH / 6_000_000.0)) : 1; if ((long)TW * TH / ((long)k * k) > 20_000_000) throw new Exception("Map is too large to preview.");
        int W = (TW + k - 1) / k, H = (TH + k - 1) / k; minX = minX; var buf = new byte[W * H * 4]; var bg = Color.FromArgb(0x14, 0x15, 0x1e); var filler = Color.FromArgb(0x24, 0x26, 0x38); var room = Color.FromArgb(0x2c, 0x30, 0x4a);
        var solid = Color.FromArgb(Math.Min(255, accent.R / 2 + 150), Math.Min(255, accent.G / 2 + 150), Math.Min(255, accent.B / 2 + 150));
        void Put(int x, int y, Color c) { int i = (y * W + x) * 4; buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; buf[i + 3] = 255; }
        void Fill(int x, int y, int w, int h, Color c) { for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++) Put(xx, yy, c); }
        void FillT(int tx, int ty, int tw, int th, Color c) { int x0 = tx / k, y0 = ty / k; Fill(x0, y0, Math.Max(1, (tx + tw + k - 1) / k - x0), Math.Max(1, (ty + th + k - 1) / k - y0), c); }   // tile units -> pixels
        Fill(0, 0, W, H, bg); foreach (var f in m.Fillers) FillT(f.X - minX, f.Y - minY, f.Width, f.Height, filler);
        foreach (var r in m.Rooms) {
            int rx = FloorDiv(r.X, 8) - minX, ry = FloorDiv(r.Y, 8) - minY; FillT(rx, ry, (r.W + 7) / 8, (r.H + 7) / 8, room); int row = 0;
            foreach (var line in r.Solids.Split('\n')) { var l = line.TrimEnd('\r'); for (int c = 0; c < l.Length; c++) if (l[c] != '0' && l[c] != ' ') { int x = (rx + c) / k, y = (ry + row) / k; if (x >= 0 && y >= 0 && x < W && y < H) Put(x, y, solid); } row++; }
        }
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb); var bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, bd.Scan0, buf.Length); bmp.UnlockBits(bd);
        return (bmp, $"{m.Rooms.Count} room(s)  ·  {TW} × {TH} tiles" + (k > 1 ? $"  ·  overview (1 px = {k} tiles)" : ""));
    }
    static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}

// ---------- Where maps can come from ----------
record MapSource(string Mod, string Display, string Path, string Zip, string Entry) { public bool Loose => Path != null; }

static class MapFinder {
    public static List<MapSource> Scan(Install inst, int limit = 3000) {
        var list = new List<MapSource>(); if (inst == null) return list; var dir = Mods.Dir(inst); if (!Directory.Exists(dir)) return list;
        foreach (var d in Directory.GetDirectories(dir)) {
            var maps = Path.Combine(d, "Maps"); if (!Directory.Exists(maps)) continue; var mod = Path.GetFileName(d);
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
        var safeMod = string.Concat((s.Mod.Length > 0 ? s.Mod : "zip").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var root = Path.GetFullPath(Path.Combine(Config.DataDir, "maps", safeMod)); var dest = Path.GetFullPath(Path.Combine(root, s.Display.Replace('/', Path.DirectorySeparatorChar)));
        if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("That map has an unsafe path inside the zip; refusing to extract it.");
        return dest;
    }
    static byte[] ZipBytes(MapSource s) { using var zf = ZipFile.OpenRead(s.Zip); using var st = zf.GetEntry(s.Entry).Open(); using var ms = new MemoryStream(); st.CopyTo(ms); return ms.ToArray(); }
    public static bool SameAsZip(MapSource s, string dest) => File.Exists(dest) && File.ReadAllBytes(dest).AsSpan().SequenceEqual(ZipBytes(s));
    public static string UniquePath(string dest) { var d = Path.GetDirectoryName(dest); var n = Path.GetFileNameWithoutExtension(dest); var e = Path.GetExtension(dest); for (int i = 2; ; i++) { var p = Path.Combine(d, $"{n} ({i}){e}"); if (!File.Exists(p)) return p; } }
    public static void ExtractTo(MapSource s, string dest) { Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.WriteAllBytes(dest, ZipBytes(s)); }

    public static MapInfo Load(MapSource s) {
        if (s.Loose) { using var fs = File.OpenRead(s.Path); return MapBin.Read(fs); }
        using var zf = ZipFile.OpenRead(s.Zip); using var st = zf.GetEntry(s.Entry).Open(); using var ms = new MemoryStream(); st.CopyTo(ms); ms.Position = 0; return MapBin.Read(ms);
    }
}

// ---------- The image box ----------
class MapView : Control {
    Bitmap img; string placeholder = "MAP IMAGE\r\nfile.bin";
    public MapView() { DoubleBuffered = true; BackColor = Brand.Panel; ResizeRedraw = true; }
    public void Set(Bitmap b, string text = null) { img?.Dispose(); img = b; if (text != null) placeholder = text; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.Clear(Brand.Panel);
        if (img == null) { using var f = Ui.F(15, true); TextRenderer.DrawText(g, placeholder, f, ClientRectangle, Brand.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
        else {
            float s = Math.Min((Width - 16f) / img.Width, (Height - 16f) / img.Height); int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s)); var r = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
            g.InterpolationMode = s >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(img, r);
            using var pen = new Pen(Brand.Panel2); g.DrawRectangle(pen, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
        }
        using var border = new Pen(Brand.Panel2, 2); g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
    }
    protected override void Dispose(bool d) { if (d) img?.Dispose(); base.Dispose(d); }
}

// ---------- The panel: image + file name + picker (shared by the Topo and Lönn pages) ----------
class MapPanel : Panel {
    readonly MainForm app; readonly MapView view = new() { Dock = DockStyle.Fill }; readonly Label name = Ui.Lbl("file.bin", 12, true), info = Ui.Lbl("No map chosen yet.", 9.5f, false, Brand.Dim);
    readonly Button openEditor; Button pickBtn; readonly Action<string> openIn; readonly string editorName; string currentPath; MapSource current; int token;

    // openIn: opens a loose .bin in the editor (null when the editor can't be told which file to open).
    public MapPanel(MainForm app, string editorName, Action<string> openIn) {
        this.app = app; this.openIn = openIn; this.editorName = editorName; BackColor = Brand.Bg; Padding = new Padding(14, 0, 0, 0); Dock = DockStyle.Fill;
        var title = Ui.Lbl("Map preview", 14, true); title.Dock = DockStyle.Top; title.Height = 34;
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 118 }; name.Location = new Point(2, 8); info.Location = new Point(4, 36);
        var row = new FlowLayoutPanel { Left = 0, Top = 66, Width = 900, Height = 48, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(Ui.Btn("Choose map...", Choose)); pickBtn = Ui.Btn("Maps in my mods ▾", PickFromMods); row.Controls.Add(pickBtn);
        openEditor = Ui.Btn("Open in " + editorName, OpenInEditor); openEditor.BackColor = Brand.Green; openEditor.Visible = openIn != null; openEditor.Enabled = false; row.Controls.Add(openEditor);
        row.Controls.Add(Ui.Btn("Copy path", () => { if (currentPath != null) Clipboard.SetText(currentPath); }));
        bottom.Controls.AddRange(new Control[] { name, info, row });
        Controls.Add(view); Controls.Add(bottom); Controls.Add(title);
        Load(app.Cfg.LastMap is string p && File.Exists(p) ? new MapSource("", Path.GetFileName(p), p, null, null) : Default(), false);
    }
    MapSource Default() { var all = MapFinder.Scan(app.Cfg.Current, 400).Where(m => m.Loose).ToList(); return all.FirstOrDefault(m => m.Display.Contains("City", StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault(); }

    void OpenInEditor() {
        if (current == null || openIn == null) return;
        try {
            if (current.Loose) { openIn(current.Path); return; }
            var dest = MapFinder.CopyPath(current);
            if (File.Exists(dest) && !MapFinder.SameAsZip(current, dest)) {
                var r = MessageBox.Show($"A copy of this map already exists and differs from the one in the zip (it may contain your edits):\r\n\r\n{dest}\r\n\r\nYes = open the existing copy\r\nNo = extract a fresh copy next to it\r\nCancel = do nothing", Brand.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return; if (r == DialogResult.No) { dest = MapFinder.UniquePath(dest); MapFinder.ExtractTo(current, dest); }
            } else if (!File.Exists(dest)) MapFinder.ExtractTo(current, dest);
            info.Text = "Extracted a copy to: " + dest + "  (the zip is unchanged)"; currentPath = dest; openIn(dest);
        } catch (Exception ex) { info.Text = "Couldn't open the map: " + ex.Message; }
    }

    void Choose() {
        using var d = new OpenFileDialog { Filter = "Celeste map|*.bin", Title = "Choose a map (.bin)", InitialDirectory = app.Cfg.Current != null && Directory.Exists(Mods.Dir(app.Cfg.Current)) ? Mods.Dir(app.Cfg.Current) : null };
        if (d.ShowDialog() == DialogResult.OK) Load(new MapSource("", Path.GetFileName(d.FileName), d.FileName, null, null), true);
    }
    async void PickFromMods() {
        info.Text = "Looking for maps in your mods..."; var inst = app.Cfg.Current; var maps = await Task.Run(() => MapFinder.Scan(inst)); info.Text = maps.Count + " map(s) found in your mods.";
        if (maps.Count == 0) return; var menu = Ui.DarkMenu();
        foreach (var g in maps.GroupBy(m => m.Mod).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
            var sub = new ToolStripMenuItem($"{g.Key}  ({g.Count()})");
            foreach (var m in g.OrderBy(m => m.Display, StringComparer.OrdinalIgnoreCase).Take(80)) { var ms = m; sub.DropDownItems.Add(new ToolStripMenuItem(m.Display, null, (_, _) => Load(ms, true))); }
            if (g.Count() > 80) sub.DropDownItems.Add(new ToolStripMenuItem($"... {g.Count() - 80} more (use Choose map...)") { Enabled = false });
            sub.DropDown.BackColor = Brand.Panel2; sub.DropDown.ForeColor = Brand.Text; sub.DropDown.Renderer = new ToolStripProfessionalRenderer(new DarkColors()); menu.Items.Add(sub);
        }
        menu.Show(pickBtn, new Point(0, pickBtn.Height));   // anchored under the button
    }
    async void Load(MapSource s, bool remember) {
        if (s == null) { current = null; currentPath = null; openEditor.Enabled = false; view.Set(null, "MAP IMAGE\r\nfile.bin"); name.Text = "file.bin"; return; }
        int my = ++token; name.Text = (s.Mod.Length > 0 ? s.Mod + " · " : "") + Path.GetFileName(s.Display); info.Text = "Rendering..."; current = s; currentPath = s.Loose ? s.Path : null; openEditor.Enabled = openIn != null; openEditor.Text = s.Loose || openIn == null ? "Open in " + editorName : "Open copy in " + editorName;
        try {
            var accent = Brand.Accent; var (bmp, text) = await Task.Run(() => MapBin.Render(MapFinder.Load(s), accent));
            if (my != token || IsDisposed) { bmp.Dispose(); return; } view.Set(bmp); info.Text = text + (s.Loose ? "" : openIn != null ? "  ·  inside a zip: opens as a copy, the zip stays unchanged" : "  ·  inside a zip (preview only)");
            if (remember && s.Loose) { app.Cfg.LastMap = s.Path; app.Cfg.Save(); }
        } catch (Exception ex) { if (my == token) { view.Set(null, "Couldn't read this map"); info.Text = ex.Message; } }
    }
}
