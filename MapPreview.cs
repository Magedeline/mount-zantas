using System.Drawing.Drawing2D;

namespace MountZantas;

// ---------- The image box ----------
class MapView : Control {
    Bitmap img;
    string placeholder = "MAP IMAGE\r\nfile.bin";
    public MapView() {
        DoubleBuffered = true;
        BackColor = Brand.Panel;
        ResizeRedraw = true;
    }
    public void Set(Bitmap b, string text = null) {
        img?.Dispose();
        img = b;
        if (text != null) placeholder = text;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(Brand.Panel);
        if (img == null) {
            using var f = Ui.F(15, true);
            TextRenderer.DrawText(
                g,
                placeholder,
                f,
                ClientRectangle,
                Brand.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else {
            float s = Math.Min((Width - 16f) / img.Width, (Height - 16f) / img.Height);
            int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
            var r = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
            g.InterpolationMode = s >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, r);
            using var pen = new Pen(Brand.Panel2);
            g.DrawRectangle(pen, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
        }
        using var border = new Pen(Brand.Panel2, 2);
        g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
    }
    protected override void Dispose(bool d) {
        if (d) img?.Dispose();
        base.Dispose(d);
    }
}

// ---------- The panel: image + file name + picker (shared by the Topo and Lönn pages) ----------
class MapPanel : Panel {
    readonly MainForm app;
    readonly MapView view = new() { Dock = DockStyle.Fill };
    readonly Label name = Ui.Lbl("file.bin", 12, true), info = Ui.Lbl("No map chosen yet.", 9.5f, false, Brand.Dim);
    readonly Button openEditor;
    Button pickBtn;
    readonly Action<string> openIn;
    readonly string editorName;
    string currentPath;
    MapSource current;
    int token;

    // openIn: opens a loose .bin in the editor (null when the editor can't be told which file to open).
    public MapPanel(MainForm app, string editorName, Action<string> openIn) {
        this.app = app;
        this.openIn = openIn;
        this.editorName = editorName;
        BackColor = Brand.Bg;
        Padding = new Padding(14, 0, 0, 0);
        Dock = DockStyle.Fill;
        var title = Ui.Lbl("Map preview", 14, true);
        title.Dock = DockStyle.Top;
        title.Height = 34;
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 118 };
        name.Location = new Point(2, 8);
        info.Location = new Point(4, 36);
        var row = new FlowLayoutPanel {
            Left = 0,
            Top = 66,
            Width = 900,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        row.Controls.Add(Ui.Btn("Choose map...", Choose));
        pickBtn = Ui.Btn("Maps in my mods ▾", PickFromMods);
        row.Controls.Add(pickBtn);
        openEditor = Ui.Btn("Open in " + editorName, OpenInEditor);
        openEditor.BackColor = Brand.Green;
        openEditor.Visible = openIn != null;
        openEditor.Enabled = false;
        row.Controls.Add(openEditor);
        row.Controls.Add(Ui.Btn("Copy path", () => { if (currentPath != null) Clipboard.SetText(currentPath); }));
        bottom.Controls.AddRange(new Control[] { name, info, row });
        Controls.Add(view);
        Controls.Add(bottom);
        Controls.Add(title);
        Load(
            app.Cfg.LastMap is string p && File.Exists(p) ? new MapSource("", Path.GetFileName(p), p, null, null) : Default(),
            false);
    }
    MapSource Default() {
        var all = MapFinder.Scan(app.Cfg.Current, 400).Where(m => m.Loose).ToList();
        return all.FirstOrDefault(m => m.Display.Contains("City", StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault();
    }

    void OpenInEditor() {
        if (current == null || openIn == null) return;
        try {
            if (current.Loose) {
                openIn(current.Path);
                return;
            }
            var dest = MapFinder.CopyPath(current);
            if (File.Exists(dest) && !MapFinder.SameAsZip(current, dest)) {
                var r = MessageBox.Show(
                    $"A copy of this map already exists and differs from the one in the zip (it may contain your edits):\r\n\r\n{dest}\r\n\r\nYes = open the existing copy\r\nNo = extract a fresh copy next to it\r\nCancel = do nothing",
                    Brand.Name,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.No) {
                    dest = MapFinder.UniquePath(dest);
                    MapFinder.ExtractTo(current, dest);
                }
            } else if (!File.Exists(dest)) MapFinder.ExtractTo(current, dest);
            info.Text = "Extracted a copy to: " + dest + "  (the zip is unchanged)";
            currentPath = dest;
            openIn(dest);
        } catch (Exception ex) { info.Text = "Couldn't open the map: " + ex.Message; }
    }

    void Choose() {
        using var d = new OpenFileDialog {
            Filter = "Celeste map|*.bin",
            Title = "Choose a map (.bin)",
            InitialDirectory = app.Cfg.Current != null && Directory.Exists(Mods.Dir(app.Cfg.Current)) ? Mods.Dir(app.Cfg.Current) : null
        };
        if (d.ShowDialog() == DialogResult.OK) Load(new MapSource("", Path.GetFileName(d.FileName), d.FileName, null, null), true);
    }
    async void PickFromMods() {
        info.Text = "Looking for maps in your mods...";
        var inst = app.Cfg.Current;
        var maps = await Task.Run(() => MapFinder.Scan(inst));
        info.Text = maps.Count + " map(s) found in your mods.";
        if (maps.Count == 0) return;
        var menu = Ui.DarkMenu();
        foreach (var g in maps.GroupBy(m => m.Mod).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
            var sub = new ToolStripMenuItem($"{g.Key}  ({g.Count()})");
            foreach (var m in g.OrderBy(m => m.Display, StringComparer.OrdinalIgnoreCase).Take(80)) {
                var ms = m;
                sub.DropDownItems.Add(new ToolStripMenuItem(m.Display, null, (_, _) => Load(ms, true)));
            }
            if (g.Count() > 80) sub.DropDownItems.Add(new ToolStripMenuItem($"... {g.Count() - 80} more (use Choose map...)") { Enabled = false });
            sub.DropDown.BackColor = Brand.Panel2;
            sub.DropDown.ForeColor = Brand.Text;
            sub.DropDown.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            menu.Items.Add(sub);
        }
        menu.Show(pickBtn, new Point(0, pickBtn.Height));   // anchored under the button
    }
    async void Load(MapSource s, bool remember) {
        if (s == null) {
            current = null;
            currentPath = null;
            openEditor.Enabled = false;
            view.Set(null, "MAP IMAGE\r\nfile.bin");
            name.Text = "file.bin";
            return;
        }
        int my = ++token;
        name.Text = (s.Mod.Length > 0 ? s.Mod + " · " : "") + Path.GetFileName(s.Display);
        info.Text = "Rendering...";
        current = s;
        currentPath = s.Loose ? s.Path : null;
        openEditor.Enabled = openIn != null;
        openEditor.Text = s.Loose || openIn == null ? "Open in " + editorName : "Open copy in " + editorName;
        try {
            var accent = Brand.Accent;
            var (bmp, text) = await Task.Run(() => MapRender.Render(MapFinder.Load(s), accent));
            if (my != token || IsDisposed) {
                bmp.Dispose();
                return;
            }
            view.Set(bmp);
            info.Text = text + (s.Loose ? "" : openIn != null ? "  ·  inside a zip: opens as a copy, the zip stays unchanged" : "  ·  inside a zip (preview only)");
            if (remember && s.Loose) {
                app.Cfg.LastMap = s.Path;
                app.Cfg.Save();
            }
        } catch (Exception ex) {
            if (my == token) {
                view.Set(null, "Couldn't read this map");
                info.Text = ex.Message;
            }
        }
    }
}
