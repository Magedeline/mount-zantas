using System.Drawing.Drawing2D;

namespace MountZantas;

static class Brand {
    public const string Name = "Mount Zantas";
    public static readonly Color Bg = Color.FromArgb(0x14, 0x15, 0x1e), Panel = Color.FromArgb(0x1f, 0x21, 0x2d), Panel2 = Color.FromArgb(0x2a, 0x2c, 0x3b),
        Hover = Color.FromArgb(0x3a, 0x3d, 0x52), Text = Color.FromArgb(0xee, 0xee, 0xf6), Dim = Color.FromArgb(0x9a, 0x9d, 0xb2),
        Green = Color.FromArgb(0x2f, 0x6b, 0x40), Orange = Color.FromArgb(0x7a, 0x5c, 0x1c);
    public static Color Accent = ColorTranslator.FromHtml("#6a4cff");

    // Original logo: twin volcano peaks (Shasta and Shastina) with a snowcap and a star.
    public static Bitmap Logo(int s) {
        var b = new Bitmap(s, s); using var g = Graphics.FromImage(b); g.SmoothingMode = SmoothingMode.AntiAlias; float u = s / 100f;
        PointF P(float x, float y) => new(x * u, y * u);
        using (var back = new SolidBrush(ControlPaint.Dark(Accent))) g.FillPolygon(back, new[] { P(50, 86), P(74, 48), P(98, 86) });
        using (var main = new SolidBrush(Accent)) g.FillPolygon(main, new[] { P(6, 86), P(44, 20), P(82, 86) });
        using (var snow = new SolidBrush(Color.White)) g.FillPolygon(snow, new[] { P(44, 20), P(55, 39), P(49.5f, 35), P(44, 42), P(38.5f, 35), P(33, 39) });
        using (var star = new SolidBrush(Color.FromArgb(255, 235, 150))) g.FillPolygon(star, new[] { P(82, 12), P(84, 19), P(91, 21), P(84, 23), P(82, 30), P(80, 23), P(73, 21), P(80, 19) });
        return b;
    }
    public static Icon AppIcon() { using var b = Logo(64); return Icon.FromHandle(b.GetHicon()); }
}

static class Ui {
    public static Font F(float size = 10f, bool bold = false) => new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);

    public static Button Btn(string text, Action click, int w = 0, int h = 36) {
        var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Brand.Panel2, ForeColor = Brand.Text, Height = h, Cursor = Cursors.Hand, Font = F(), Margin = new Padding(4), UseVisualStyleBackColor = false };
        if (w > 0) b.Width = w; else { b.AutoSize = true; b.Padding = new Padding(10, 0, 10, 0); }
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Brand.Hover; b.Click += (_, _) => click(); return b;
    }
    public static Button Big(string text, string sub, Action click) {
        var b = Btn(text, click, 0, 64); b.AutoSize = false; b.Dock = DockStyle.Fill; b.Font = F(15, true); b.TextAlign = ContentAlignment.MiddleLeft; b.Padding = new Padding(18, 0, 0, 0);
        b.Text = sub == null ? text : text; return b;
    }
    public static Label Lbl(string t, float size = 10, bool bold = false, Color? c = null) => new() { Text = t, Font = F(size, bold), ForeColor = c ?? Brand.Text, AutoSize = true, UseMnemonic = false, BackColor = Color.Transparent };
    public static Panel Card(Control inner = null) { var p = new Panel { BackColor = Brand.Panel, Padding = new Padding(14), Dock = DockStyle.Fill, Margin = new Padding(6) }; if (inner != null) p.Controls.Add(inner); return p; }

    public static void Grid(DataGridView g) {
        g.BackgroundColor = Brand.Panel; g.BorderStyle = BorderStyle.None; g.GridColor = Brand.Bg; g.EnableHeadersVisualStyles = false; g.RowHeadersVisible = false;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Brand.Panel2, ForeColor = Brand.Text, SelectionBackColor = Brand.Panel2, Font = F(10, true) };
        g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Brand.Panel, ForeColor = Brand.Text, SelectionBackColor = Brand.Hover, SelectionForeColor = Brand.Text };
        g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize; g.RowTemplate.Height = 30; g.Font = F();
    }
    public static TextBox Text(string placeholder = "") => new() { BackColor = Brand.Panel2, ForeColor = Brand.Text, BorderStyle = BorderStyle.FixedSingle, Font = F(11), PlaceholderText = placeholder };
    public static CheckBox Check(string t, bool v = false) => new() { Text = t, Checked = v, ForeColor = Brand.Text, AutoSize = true, Font = F(), Margin = new Padding(8, 8, 8, 0) };
    public static DropButton Combo(params string[] items) => new(items);

    public static ContextMenuStrip DarkMenu() => new() { Renderer = new ToolStripProfessionalRenderer(new DarkColors()), BackColor = Brand.Panel2, ForeColor = Brand.Text, Font = F(10), ShowImageMargin = false };

    // Dark column headers for ListView (the default header is bright white).
    public static void DarkList(ListView lv) {
        lv.OwnerDraw = true;
        lv.DrawColumnHeader += (s, e) => {
            using var b = new SolidBrush(Brand.Panel2); e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, F(10, true), new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), Brand.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        lv.DrawItem += (s, e) => e.DrawDefault = true; lv.DrawSubItem += (s, e) => e.DrawDefault = true;
        // Stretch the last column to the edge so there is no bright strip left of the scrollbar.
        lv.Resize += (_, _) => FitList(lv); lv.HandleCreated += (_, _) => FitList(lv);
    }
    public static void FitList(ListView lv) {
        try {
            if (lv.Columns.Count == 0 || !lv.IsHandleCreated || lv.ClientSize.Width <= 0) return; int used = 0; for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
            bool scroll = false;   // ClientSize already excludes a visible scrollbar; item bounds can be unavailable mid-layout, so treat any failure as "unknown"
            try { if (lv.Items.Count > 0) { var last = lv.Items[lv.Items.Count - 1]; scroll = last.Index >= 0 && last.Bounds.Bottom > lv.ClientSize.Height; } } catch { }
            lv.Columns[lv.Columns.Count - 1].Width = Math.Max(120, lv.ClientSize.Width - used - (scroll ? 4 : SystemInformation.VerticalScrollBarWidth + 4));
        } catch { }   // purely cosmetic sizing: never let it crash the launcher
    }

    // A titled card that groups related settings.
    public static Control Section(string title, params Control[] rows) {
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Brand.Panel, Padding = new Padding(18, 12, 18, 14), Margin = new Padding(0, 0, 14, 14), MinimumSize = new Size(540, 0) };
        p.Controls.Add(new Label { Text = title, Font = F(13, true), ForeColor = Brand.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        foreach (var r in rows) p.Controls.Add(r); return p;
    }

    public static string Prompt(string title, string label) {
        using var f = new Form { Text = title, Width = 420, Height = 160, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Brand.Bg, ForeColor = Brand.Text, Font = F() };
        var l = Lbl(label); l.Location = new Point(14, 14); var tb = Text(); tb.SetBounds(14, 42, 376, 28);
        var ok = Btn("OK", () => { }, 80); ok.DialogResult = DialogResult.OK; ok.Location = new Point(310, 80); f.Controls.AddRange(new Control[] { l, tb, ok }); f.AcceptButton = ok;
        return f.ShowDialog() == DialogResult.OK && tb.Text.Trim().Length > 0 ? tb.Text.Trim() : null;
    }
}

class DarkColors : ProfessionalColorTable {
    public override Color MenuItemSelected => Brand.Hover; public override Color MenuItemSelectedGradientBegin => Brand.Hover; public override Color MenuItemSelectedGradientEnd => Brand.Hover;
    public override Color MenuItemBorder => Brand.Hover; public override Color MenuBorder => Brand.Hover; public override Color ToolStripDropDownBackground => Brand.Panel2;
    public override Color ImageMarginGradientBegin => Brand.Panel2; public override Color ImageMarginGradientMiddle => Brand.Panel2; public override Color ImageMarginGradientEnd => Brand.Panel2;
    public override Color SeparatorDark => Brand.Hover; public override Color SeparatorLight => Brand.Hover;
}

// A dark dropdown: looks like a button, opens a dark menu. Same SelectedIndex / SelectedIndexChanged shape as ComboBox.
class DropButton : Button {
    readonly string[] items; int idx; public event EventHandler SelectedIndexChanged;
    public DropButton(string[] items) {
        this.items = items; FlatStyle = FlatStyle.Flat; BackColor = Brand.Panel2; ForeColor = Brand.Text; Font = Ui.F(); Height = 34; Width = 320; TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(10, 0, 0, 0); Cursor = Cursors.Hand; UseVisualStyleBackColor = false; Margin = new Padding(0, 2, 0, 6); FlatAppearance.BorderSize = 0; FlatAppearance.MouseOverBackColor = Brand.Hover;
        Click += (_, _) => Pop(); SelectedIndex = 0;
    }
    public int SelectedIndex { get => idx; set { idx = Math.Clamp(value, 0, items.Length - 1); Text = items[idx] + "   ▾"; SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } }
    void Pop() { var m = Ui.DarkMenu(); m.MinimumSize = new Size(Width, 0); for (int i = 0; i < items.Length; i++) { int k = i; m.Items.Add(new ToolStripMenuItem(items[i], null, (_, _) => SelectedIndex = k) { Checked = i == idx }); } m.Show(this, new Point(0, Height)); }
}

// Borderless branded splash: shown at startup and briefly when Celeste is launched.
class SplashForm : Form {
    readonly string status; float t; readonly System.Windows.Forms.Timer anim = new() { Interval = 30 };
    public SplashForm(string status) {
        this.status = status; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; Size = new Size(560, 300);
        BackColor = Brand.Bg; ShowInTaskbar = false; TopMost = true; DoubleBuffered = true; Icon = Brand.AppIcon();
        anim.Tick += (_, _) => { t = (t + 0.012f) % 1f; Invalidate(new Rectangle(0, Height - 24, Width, 24)); }; anim.Start();
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var pen = new Pen(Brand.Accent, 3)) g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        using (var logo = Brand.Logo(140)) g.DrawImage(logo, 50, 62, 140, 140);
        using (var f = Ui.F(34, true)) g.DrawString(Brand.Name, f, Brushes.White, 205, 82);
        using (var f = Ui.F(11)) { g.DrawString("Celeste launcher and mod manager", f, Brushes.Silver, 209, 142); g.DrawString(status, f, Brushes.White, 209, 176); }
        int w = 140, x = (int)((Width + w) * t) - w; using (var b = new SolidBrush(Brand.Accent)) g.FillRectangle(b, Math.Max(x, 0), Height - 8, Math.Min(w, Math.Max(0, Math.Min(x + w, Width) - Math.Max(x, 0))), 4);
    }
    protected override void Dispose(bool d) { if (d) anim.Dispose(); base.Dispose(d); }
    public static void ShowFor(string status, int ms) {
        var s = new SplashForm(status); s.Show(); var t = new System.Windows.Forms.Timer { Interval = ms }; t.Tick += (_, _) => { t.Stop(); t.Dispose(); s.Close(); }; t.Start();
    }
}

// Log + progress strip shared by pages that run downloads.
class LogBox : Panel {
    readonly TextBox tb = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Brand.Panel, ForeColor = Brand.Dim, BorderStyle = BorderStyle.None, Font = Ui.F(9.5f) };
    readonly ProgressBar pb = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Continuous };
    public IProgress<int> Prog;
    public LogBox() { Dock = DockStyle.Bottom; Height = 130; Padding = new Padding(0, 6, 0, 0); Controls.Add(tb); Controls.Add(pb); Prog = new Progress<int>(v => pb.Value = Math.Clamp(v, 0, 100)); }
    public void Log(string s) { if (InvokeRequired) { BeginInvoke(() => Log(s)); return; } tb.AppendText(s + "\r\n"); }
    public void Reset() { pb.Value = 0; }
}
