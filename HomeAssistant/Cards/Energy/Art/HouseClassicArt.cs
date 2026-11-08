using SkiaSharp;
using static HomeAssistant.IsoPainter;

namespace HomeAssistant;

/// <summary>Maison traditionnelle (<c>houseClassic</c> de la maquette), dans les coordonnées locales de la maquette.</summary>
internal static class HouseClassicArt
{
    public static void Draw(IsoPainter p)
    {
        var I = Iso(11.2f, 90, 75);
        const double W = 10, D = 7, H = 3.3, Hr = 6.2;
        Face fy = (x, z) => I(x, D, z);
        Face fx = (y, z) => I(W, y, z);

        // murs
        p.Poly([I(W, 0, H), I(W, 0, 0), I(W, D, 0), I(W, D, H), I(W, D / 2, Hr)], ArtPalette.CreamX);
        p.Poly(Rect(fy, 0, W, 0, H), ArtPalette.CreamY);

        // soubassement
        p.Poly(Rect(fx, 0, D, 0, .45), "#8d8f99");
        p.Poly(Rect(fy, 0, W, 0, .45), "#6d6f7a");
        var joints = new List<(SKPoint, SKPoint)>();
        for (var i = 1; i < 16; i++)
        {
            joints.Add((fx(D * i / 16, 0), fx(D * i / 16, .45)));
            joints.Add((fy(W * i / 16, 0), fy(W * i / 16, .45)));
        }
        p.Lines(Stroke.Of("#000", .7f, .22f), joints);

        // façade longue
        void Shutter(Face f, double a0, double a1, double b0, double b1) =>
            p.Poly(Rect(f, a0, a1, b0, b1), "#3f6470", Stroke.Of("#26404a", .7f));
        Shutter(fy, .55, 1, .95, 2.55);
        p.Pane(fy, 1, 2.5, .95, 2.55, 2, 2, "#f4f6fa");
        Shutter(fy, 2.5, 2.95, .95, 2.55);
        Shutter(fy, 7.05, 7.5, .95, 2.55);
        p.Pane(fy, 7.5, 9, .95, 2.55, 2, 2, "#f4f6fa");
        Shutter(fy, 9, 9.45, .95, 2.55);
        p.Poly(Rect(fy, 4.15, 5.85, .45, 2.65), ArtPalette.WoodD, Stroke.Of("#f4f6fa", 1.4f));
        p.Pane(fy, 4.5, 5.5, 1.9, 2.4, 2, 1, "#f4f6fa");
        p.Poly(Rect(fy, 5.5, 5.6, 1.25, 1.55), "#e6c98d");

        // marquise
        p.Poly([I(3.9, 7, 2.95), I(6.1, 7, 2.95), I(6.1, 7.8, 2.7), I(3.9, 7.8, 2.7)], "#3a465e");
        p.Poly([I(3.9, 7.8, 2.7), I(6.1, 7.8, 2.7), I(6.1, 7.8, 2.6), I(3.9, 7.8, 2.6)], "#e8edf5");

        // pignon
        p.Pane(fx, 1.6, 5.4, .85, 2.6, 4, 1, "#f4f6fa");
        p.Pane(fx, 2.8, 4.2, 3.7, 4.75, 2, 2, "#f4f6fa");
        p.Poly(Rect(fy, 0, W, H - .3, H), Fill.Solid("#000", .22f));

        // toit
        double E = H - .25, ov = .55, xa = -.5, xb = W + .5;
        SKPoint Fr(double x, double t) => I(x, D + ov + (D / 2 - D - ov) * t, E + (Hr - E) * t);
        p.Poly([Fr(xa, 0), Fr(xb, 0), Fr(xb, 1), Fr(xa, 1)], ArtPalette.Slate);
        var tiles = new List<(SKPoint, SKPoint)>();
        const int n = 9;
        for (var r = 1; r < n; r++) tiles.Add((Fr(xa, (double)r / n), Fr(xb, (double)r / n)));
        for (var r = 0; r < n; r++)
        {
            const int cols = 22;
            for (var c = 0; c <= cols; c++)
            {
                var xc = xa + (xb - xa) * (c + (r % 2 == 1 ? .5 : 0)) / cols;
                if (xc <= xb) tiles.Add((Fr(xc, (double)r / n), Fr(xc, (double)(r + 1) / n)));
            }
        }
        p.Lines(Stroke.Of("#10182a", .65f, .4f), tiles);

        // gouttière et rives
        p.Poly([Fr(xa, 0), Fr(xb, 0), I(xb, D + ov, E - .22), I(xa, D + ov, E - .22)], "#dfe5ef");
        const double t = .34;
        p.Poly([I(xb, D + ov, E), I(xb, D / 2, Hr), I(xb, -ov, E), I(xb, -ov, E - t), I(xb, D / 2, Hr - t), I(xb, D + ov, E - t)],
            "#f0f3f8", Stroke.Of("#aab7cd", .6f, roundJoin: true));
        p.Lines(Stroke.Of("#e8edf5", 2.2f, roundCap: true), [(Fr(xa, 1), Fr(xb, 1))]);

        // cheminée
        Face fc = (y, z) => I(7.7, y, z);
        Face fd = (x, z) => I(x, 4.1, z);
        p.Poly(Rect(fc, 3.1, 4.1, 5.75, 7.1), "#d1b9a2");
        p.Poly(Rect(fd, 6.7, 7.7, 5.75, 7.1), "#9c8370");
        p.Poly([I(6.55, 2.95, 7.1), I(7.85, 2.95, 7.1), I(7.85, 4.25, 7.1), I(6.55, 4.25, 7.1)], "#ebeff5");
        p.Poly([I(7.85, 2.95, 7.1), I(7.85, 4.25, 7.1), I(7.85, 4.25, 6.95), I(7.85, 2.95, 6.95)], "#b9c5d8");
        p.Poly([I(6.55, 4.25, 7.1), I(7.85, 4.25, 7.1), I(7.85, 4.25, 6.95), I(6.55, 4.25, 6.95)], "#8d9bb3");
    }
}
