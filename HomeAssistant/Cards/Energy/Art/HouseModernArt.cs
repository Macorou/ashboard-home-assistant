using SkiaSharp;
using static HomeAssistant.IsoPainter;

namespace HomeAssistant;

/// <summary>Maison contemporaine (<c>houseModern</c> de la maquette), dans les coordonnées locales de la maquette.</summary>
internal static class HouseModernArt
{
    public static void Draw(IsoPainter p)
    {
        var I = Iso(11.2f, 88, 73);

        // rez-de-chaussée
        p.Box(I, 0, 10, 0, 7.5, 0, 3, "#fff", ArtPalette.RenderX, ArtPalette.RenderY);
        p.Poly(Rect(FX(I, 10), 0, 7.5, 0, .18), "#2b3448");
        p.Poly(Rect(FY(I, 7.5), 0, 10, 0, .18), "#1f2738");
        p.Pane(FY(I, 7.5), .6, 6.4, .35, 2.7, 5, 1, "#20293b");
        p.Poly(Rect(FY(I, 7.5), 7, 8.3, .18, 2.55), ArtPalette.WoodD, Stroke.Of("#20293b", 1.2f));
        p.Poly(Rect(FY(I, 7.5), 8.08, 8.14, 1.1, 1.75), "#e8edf5");
        p.Pane(FY(I, 7.5), 8.8, 9.6, 1.2, 2.55, 1, 1, "#20293b");
        p.Pane(FX(I, 10), 1, 6.6, .35, 2.7, 4, 1, "#20293b");
        p.Poly(Rect(FY(I, 7.5), 0, 10, 2.78, 3), Fill.Solid("#000", .25f));
        p.Poly(Rect(FX(I, 10), 0, 7.5, 2.78, 3), Fill.Solid("#000", .18f));

        // dalle et terrasse
        p.Box(I, -.4, 10.4, -.4, 7.9, 3, 3.25, "#dfe6f1", "#eef2f8", "#aab7cd");
        p.Poly(Rect(FTop(I, 3.25), -.2, 2.3, -.2, 7.7), ArtPalette.Deck);
        var deck = new List<(SKPoint, SKPoint)>();
        for (var i = 1; i < 9; i++)
        {
            var y = -.2 + 7.9 * i / 9;
            deck.Add((I(-.2, y, 3.25), I(2.3, y, 3.25)));
        }
        p.Lines(Stroke.Of("#6d5236", .7f, .6f), deck);

        // étage
        p.Box(I, 2.3, 10.4, .1, 7.9, 3.25, 5.75, "#fff", ArtPalette.Wood, ArtPalette.Anthra);
        p.Slats(FX(I, 10.4), .1, 7.9, 3.25, 5.75, 22, Fill.Rgba(70, 40, 18, .55f));
        p.Pane(FX(I, 10.4), 2.8, 6.2, 3.7, 5.35, 3, 1, "#20293b");
        p.Pane(FY(I, 7.9), 3.1, 9.6, 3.75, 5.25, 5, 1, "#10172a");

        // toit plat
        p.Box(I, 2, 10.75, -.2, 8.2, 5.75, 6, ArtPalette.RoofFlat, "#f0f3f8", "#b9c5d8");
        p.Lines(Stroke.Of("#fff", 1.3f, roundCap: true),
        [
            (I(10.75, -.2, 6), I(10.75, 8.2, 6)),
            (I(10.75, 8.2, 6), I(2, 8.2, 6)),
        ]);

        // garde-corps en verre
        p.Poly(Rect(FY(I, 7.7), -.2, 2.3, 3.25, 4.1), Fill.Solid("#bfe3ff", .22f), Stroke.Of("#e8f4ff", 1f, .8f));
    }
}
