using SkiaSharp;
using static HomeAssistant.IsoPainter;

namespace HomeAssistant;

/// <summary>Batterie murale (<c>battery</c> de la maquette), dans les coordonnées locales de la maquette. Elle n'a pas d'image allumée.</summary>
internal static class BatteryArt
{
    private const double W = 2, D = 6, H = 9.5;
    private static readonly Projection I = Iso(10, 60, 100);

    /// <summary>Les cinq segments de charge, du bas vers le haut, en coordonnées locales (quatre points chacun). Leur piste sombre est cuite dans l'image ; la lueur est dessinée à part.</summary>
    internal static readonly SKPoint[][] Segments = Enumerable.Range(0, 5).Select(SegmentPolygon).ToArray();

    private static SKPoint[] SegmentPolygon(int i)
    {
        var z0 = 1.85 + i * 1.28;
        return Rect(FX(I, W), 1.5, 4.5, z0, z0 + .92);
    }

    public static void Draw(IsoPainter p)
    {
        p.Poly(Rect(FTop(I, H), 0, W, 0, D), "#fff");
        p.Poly(Rect(FX(I, W), 0, D, 0, H), ArtPalette.RenderX);
        p.Poly(Rect(FY(I, D), 0, W, 0, H), ArtPalette.RenderY);
        p.Poly(Rect(FX(I, W), 0, D, 0, .25), "#2b3448");
        p.Poly(Rect(FY(I, D), 0, W, 0, .25), "#1f2738");
        p.Poly(Rect(FX(I, W), 1, 5, 1.4, 8.3), ArtPalette.Anthra, Stroke.Of("#1b2334", 1));
        foreach (var segment in Segments) p.Poly(segment, "#151b2a");

        var vents = new List<(SKPoint, SKPoint)>();
        for (var i = 1; i < 6; i++)
        {
            var x = W * i / 6;
            vents.Add((I(x, D, 2), I(x, D, 7.5)));
        }
        p.Lines(Stroke.Of("#5f6e88", .8f), vents);
        p.Lines(Stroke.Of("#fff", 1.1f, .75f, roundCap: true),
        [
            (I(W, 0, H), I(W, D, H)),
            (I(W, D, H), I(0, D, H)),
            (I(W, D, H), I(W, D, 0)),
        ]);
    }
}
