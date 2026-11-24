namespace MountZantas;

// The Zantas mark: a two-peak mountain with a gear in its middle and a star in the gear.
// Shapes are in a 100 x 100 box and given as x, y, x, y, ... so the Windows and Avalonia apps draw the same thing.
static class ZantasMark {
    // Mountain outline: a small peak on the left, the main peak on the right of it.
    public static readonly float[] Mountain = { 4, 90, 36, 34, 44, 42, 56, 16, 96, 90 };

    // Snow on the main peak, with a jagged lower edge.
    public static readonly float[] Snow = { 56, 16, 64.1f, 31, 60, 28, 56, 32.5f, 52, 28, 49.1f, 31 };

    // Snow on the small peak.
    public static readonly float[] SmallSnow = { 36, 34, 42, 40, 39, 38.5f, 36.5f, 41, 34, 38.5f, 32.6f, 40 };

    // Where the gear sits, and its size.
    public const float GearX = 50;
    public const float GearY = 65;
    public const float HubRadius = 10.5f;

    // The gear: 8 teeth around the centre.
    public static readonly float[] Gear = MakeGear(GearX, GearY, 8, 18.5f, 14.5f);

    // The star in the middle of the gear (5 points).
    public static readonly float[] Star = MakeStar(GearX, GearY + 0.6f, 8, 3.4f);

    static float[] MakeGear(float cx, float cy, int teeth, float outer, float inner) {
        var points = new List<float>();
        double step = 2 * Math.PI / teeth;
        for (int i = 0; i < teeth; i++) {
            double middle = i * step - Math.PI / 2;
            // Each tooth: up the left side, across the top, down the right side.
            Add(points, cx, cy, inner, middle - step * 0.32);
            Add(points, cx, cy, outer, middle - step * 0.17);
            Add(points, cx, cy, outer, middle + step * 0.17);
            Add(points, cx, cy, inner, middle + step * 0.32);
        }
        return points.ToArray();
    }

    static float[] MakeStar(float cx, float cy, float outer, float inner) {
        var points = new List<float>();
        for (int i = 0; i < 10; i++) {
            double angle = i * Math.PI / 5 - Math.PI / 2;
            float radius = i % 2 == 0 ? outer : inner;
            Add(points, cx, cy, radius, angle);
        }
        return points.ToArray();
    }

    static void Add(List<float> points, float cx, float cy, float radius, double angle) {
        points.Add(cx + radius * (float)Math.Cos(angle));
        points.Add(cy + radius * (float)Math.Sin(angle));
    }
}
