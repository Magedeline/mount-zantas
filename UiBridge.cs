using System.Drawing.Imaging;

namespace MountZantas;

// The Windows (WinForms) side of the portable core: turns the core's raw bytes/pixels into System.Drawing bitmaps.

static class Images {
    public static bool Allowed(string url) => ImageStore.Allowed(url);
    public static int Clear() => ImageStore.Clear();

    // Each caller gets its own bitmap, so a control can dispose it freely.
    public static async Task<Bitmap> Get(string url) {
        var bytes = await ImageStore.GetBytes(url);
        if (bytes == null) return null;
        try {
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return ImageStore.IsWebP(bytes) ? DecodeWebP(bytes) : null; }   // Windows' built-in decoder can't read WebP, which GameBanana uses for many newer images
    }
    static Bitmap DecodeWebP(byte[] d) {
        using var sk = SkiaSharp.SKBitmap.Decode(d);
        if (sk == null || sk.Width <= 0 || sk.Width * (long)sk.Height > 40_000_000) return null;
        using var img = SkiaSharp.SKImage.FromBitmap(sk);
        using var png = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(png.ToArray());
        using var gdi = Image.FromStream(ms);
        return new Bitmap(gdi);
    }
}

static class MapRender {
    public static (Bitmap bmp, string info) Render(MapInfo m, Color accent) {
        var raw = MapBin.Render(m, accent.R, accent.G, accent.B);
        var bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(
            new Rectangle(0, 0, raw.Width, raw.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(raw.Bgra, 0, bd.Scan0, raw.Bgra.Length);
        bmp.UnlockBits(bd);
        return (bmp, raw.Info);
    }
}
