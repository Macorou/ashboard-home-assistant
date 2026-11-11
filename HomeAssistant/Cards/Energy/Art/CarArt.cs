using SkiaSharp;

namespace HomeAssistant;

/// <summary>
/// Petite citadine électrique (<c>build(color)</c> de la maquette « voiture »), dans les coordonnées locales de la maquette. Elle est dessinée
/// d'après la géométrie en 3D (profil de caisse extrudé, faces visibles seulement, lumière fixe) et ne porte aucune marque. Seule la couleur
/// de la caisse change d'un dessin à l'autre ; elle n'a pas d'image allumée.
/// </summary>
internal static class CarArt
{
    private const double W = 3.5, L = 8.1, WG = W - 0.42;

    /// <summary>Centre du badge (pourcentage de charge), au-dessus du toit, en coordonnées locales.</summary>
    internal static readonly SKPoint BadgeAnchor = new(60, -3);

    private static readonly (double Y, double Z)[] Body =
        [(0.12, 0.42), (0, 0.95), (0.04, 1.5), (0.32, 1.86), (5.85, 1.74), (7.35, 1.52), (7.98, 1.3), (8.1, 0.95), (8.02, 0.5), (7.8, 0.38), (0.3, 0.38)];

    private static readonly (double Y, double Z)[] Cabin = [(0.36, 1.84), (0.86, 2.62), (1.6, 2.8), (4.25, 2.82), (5.85, 1.76)];

    // Direction de la lumière : normalize(0.45, -0.25, 1).
    private static readonly double[] LightDir = Normalize(0.45, -0.25, 1);

    internal static ArtKind KindFor(CarColor color) => color switch
    {
        CarColor.Blue => ArtKind.CarBlue,
        CarColor.Yellow => ArtKind.CarYellow,
        CarColor.White => ArtKind.CarWhite,
        _ => throw new ArgumentOutOfRangeException(nameof(color)),
    };

    private static double[] Normalize(double x, double y, double z)
    {
        var m = Math.Sqrt(x * x + y * y + z * z);
        return [x / m, y / m, z / m];
    }

    private static string Hex(double r, double g, double b) =>
        $"#{(int)Math.Round(Math.Clamp(r, 0, 255)):x2}{(int)Math.Round(Math.Clamp(g, 0, 255)):x2}{(int)Math.Round(Math.Clamp(b, 0, 255)):x2}";

    /// <summary>Éclaircit (f &gt; 1) ou assombrit (f &lt; 1) une couleur, comme <c>shade</c> de la maquette.</summary>
    private static string Shade(string hex, double f)
    {
        var c = new[] { 1, 3, 5 }.Select(i => (double)Convert.ToInt32(hex.Substring(i, 2), 16)).ToArray();
        return f >= 1
            ? Hex(c[0] + (255 - c[0]) * (f - 1) * 1.6, c[1] + (255 - c[1]) * (f - 1) * 1.6, c[2] + (255 - c[2]) * (f - 1) * 1.6)
            : Hex(c[0] * f, c[1] * f, c[2] * f);
    }

    private static double LightOf(double[] n) => 0.6 + 0.48 * Math.Max(0, n[0] * LightDir[0] + n[1] * LightDir[1] + n[2] * LightDir[2]);

    public static void Draw(IsoPainter p, string bodyHex)
    {
        var iso = IsoPainter.Iso(10, 72, 24);

        // Chaque face est tracée de sa propre couleur sur 0,35 (comme stroke="fill" de la maquette), ce qui colmate les jointures.
        void Add((double X, double Y, double Z)[] poly, Fill fill, bool glow = false) =>
            p.Poly(poly.Select(q => iso(q.X, q.Y, q.Z)).ToArray(), fill, new Stroke(fill, .35f, RoundJoin: true), glow);

        (double, double, double)[] Circle(double x, double yc, double zc, double r, int n = 28, double t0 = 0, double t1 = Math.PI * 2) =>
            Enumerable.Range(0, n + 1).Select(i =>
            {
                var t = t0 + (t1 - t0) * i / n;
                return (x, yc + r * Math.Cos(t), zc + r * Math.Sin(t));
            }).ToArray();

        // Profil vu de côté (y vers l'avant, z vers le haut) extrudé entre x = a et x = b ; seules les faces tournées vers la caméra sont dessinées.
        void Extrude((double Y, double Z)[] profile, double a, double b, Func<int, string?> colorOf, Fill sideFill)
        {
            double area = 0;
            for (var i = 0; i < profile.Length; i++)
            {
                var (y1, z1) = profile[i];
                var (y2, z2) = profile[(i + 1) % profile.Length];
                area += y1 * z2 - y2 * z1;
            }
            var s = area > 0 ? 1 : -1;
            for (var i = 0; i < profile.Length; i++)
            {
                var (y1, z1) = profile[i];
                var (y2, z2) = profile[(i + 1) % profile.Length];
                var n = Normalize(0, s * (z2 - z1), -s * (y2 - y1));
                if (n[1] + n[2] <= 0.01) continue;
                var baseColor = colorOf(i);
                if (baseColor is null) continue;
                Add([(a, y1, z1), (b, y1, z1), (b, y2, z2), (a, y2, z2)], Fill.Solid(Shade(baseColor, LightOf(n))));
            }
            Add(profile.Select(q => (b, q.Y, q.Z)).ToArray(), sideFill);
        }

        // Ombre au sol : le halo remplace le flou de la maquette.
        Add([(-0.35, -0.25, 0), (W + 0.35, -0.25, 0), (W + 0.35, L + 0.25, 0), (-0.35, L + 0.25, 0)], Fill.Rgba(0, 0, 0, .5f), glow: true);
        // Roues du côté caché.
        Add(Circle(0.3, 1.4, 0.78, 0.78), "#0b0e13");
        Add(Circle(0.3, 6.55, 0.78, 0.78), "#0b0e13");

        // Caisse.
        var side = Fill.Box(0, 0, 0, 1, (0, Shade(bodyHex, 1.08)), (1, Shade(bodyHex, 0.72)));
        Extrude(Body, 0, W, _ => bodyHex, side);

        // Habitacle en retrait, toit noir.
        Extrude(Cabin, 0.42, WG, i => i == 3 ? "#41597c" : "#232a37", "#1c222d");
        // Pare-brise : reflet.
        Add([(0.9, 4.45, 2.7), (1.6, 4.3, 2.78), (2.2, 5.5, 1.95), (1.5, 5.62, 1.88)], Fill.Rgba(255, 255, 255, .12f));

        // Vitres latérales, montant central, reflet.
        var glass = Fill.Box(0, 0, 0, 1, (0, "#4b6386"), (1, "#121a2a"));
        Add([(WG + .01, 0.8, 1.95), (WG + .01, 1.22, 2.52), (WG + .01, 1.8, 2.66), (WG + .01, 4.2, 2.68), (WG + .01, 5.55, 1.92)], glass);
        Add([(WG + .02, 3.1, 1.92), (WG + .02, 3.32, 1.92), (WG + .02, 3.32, 2.69), (WG + .02, 3.1, 2.69)], "#1c222d");
        Add([(WG + .02, 3.6, 2.6), (WG + .02, 4.1, 2.62), (WG + .02, 4.7, 2.05), (WG + .02, 4.25, 2.02)], Fill.Rgba(255, 255, 255, .10f));

        // Détails du flanc.
        const double X = W + 0.01;
        Add([(X, 2.25, 0.38), (X, 5.7, 0.38), (X, 5.7, 0.56), (X, 2.25, 0.56)], "#1a1f28");
        Add([(X, 0.4, 1.47), (X, 7.6, 1.27), (X, 7.6, 1.3), (X, 0.4, 1.5)], Fill.Rgba(255, 255, 255, .28f));
        Add([(X, 3.22, 0.58), (X, 3.28, 0.58), (X, 3.28, 1.8), (X, 3.22, 1.8)], Fill.Rgba(0, 0, 0, .28f));
        Add([(X, 5.5, 0.95), (X, 5.56, 0.95), (X, 5.56, 1.75), (X, 5.5, 1.75)], Fill.Rgba(0, 0, 0, .22f));
        Add([(X, 2.55, 1.55), (X, 2.95, 1.55), (X, 2.95, 1.63), (X, 2.55, 1.63)], Fill.Rgba(0, 0, 0, .35f));
        Add([(X, 4.65, 1.53), (X, 5.05, 1.53), (X, 5.05, 1.61), (X, 4.65, 1.61)], Fill.Rgba(0, 0, 0, .35f));
        Add([(X, 7.5, 1.32), (X, 7.98, 1.3), (X, 8.06, 1.1), (X, 7.7, 1.16)], "#dfe9f5");
        Add([(X, 0.03, 1.5), (X, 0.42, 1.66), (X, 0.38, 1.4), (X, 0.06, 1.28)], "#c1223a");
        // Rétroviseur.
        Add([(W + 0.3, 5.3, 1.84), (W + 0.3, 5.72, 1.8), (W + 0.3, 5.72, 2.06), (W + 0.3, 5.36, 2.08)], "#1c222d");

        // Face avant : phares fins, feux de jour verticaux, calandre.
        (double Y, double Z) Up(double t) => (7.98 + 0.12 * t, 1.3 - 0.35 * t);
        (double Y, double Z) Lo(double t) => (8.1 - 0.08 * t, 0.95 - 0.45 * t);
        (double, double, double)[] OnStrip(Func<double, (double Y, double Z)> f, double x0, double x1, double t0, double t1)
        {
            var (ya, za) = f(t0);
            var (yb, zb) = f(t1);
            return [(x0, ya, za), (x1, ya, za), (x1, yb, zb), (x0, yb, zb)];
        }
        Add(OnStrip(Lo, 1.05, W - 1.05, 0.18, 0.8), "#121821");
        Add(OnStrip(Lo, 1.35, W - 1.35, 0.25, 0.7), Fill.Box(0, 0, 1, 0, (0, "#2a3343"), (.5f, "#4a5568"), (1, "#2a3343")));
        foreach (var (x0, x1) in new[] { (0.18, 1.12), (W - 1.12, W - 0.18) })
        {
            Add(OnStrip(Up, x0, x1, 0.1, 0.42), "#dfe9f5", glow: true);
            var fx = x0 < 1 ? 0.9 : W - 0.98;
            Add(OnStrip(Up, fx, fx + 0.08, 0.42, 1), "#ffffff", glow: true);
            Add(OnStrip(Lo, fx, fx + 0.08, 0, 0.62), "#ffffff", glow: true);
        }

        // Passages de roue, puis roues du côté visible.
        var rim = Fill.Box(0, 0, 1, 1, (0, "#e6ebf2"), (1, "#7d8899"));
        foreach (var yc in new[] { 1.4, 6.55 })
        {
            Add(Circle(X + .01, yc, 0.78, 0.98, 20, 0, Math.PI).Concat(Circle(X + .01, yc, 0.78, 0.82, 20, Math.PI, 0)).ToArray(), "#1a1f28");
            Add(Circle(X + .02, yc, 0.78, 0.78), "#14171e");
            Add(Circle(X + .03, yc, 0.78, 0.52), rim);
            for (var k = 0; k < 5; k++)
            {
                var t = k * Math.PI * 2 / 5 + 0.3;
                var t2 = t + 0.42;
                Add([
                    (X + .04, yc + 0.2 * Math.Cos(t), 0.78 + 0.2 * Math.Sin(t)), (X + .04, yc + 0.47 * Math.Cos(t), 0.78 + 0.47 * Math.Sin(t)),
                    (X + .04, yc + 0.47 * Math.Cos(t2), 0.78 + 0.47 * Math.Sin(t2)), (X + .04, yc + 0.2 * Math.Cos(t2), 0.78 + 0.2 * Math.Sin(t2)),
                ], Fill.Rgba(30, 36, 48, .75f));
            }
            Add(Circle(X + .05, yc, 0.78, 0.13, 12), "#2a3140");
        }
    }
}
