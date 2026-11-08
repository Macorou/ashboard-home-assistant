using SkiaSharp;

namespace HomeAssistant;

/// <summary>Pylône électrique (<c>pylon</c> de la maquette), dans les coordonnées locales de la maquette. Il n'a pas d'image allumée.</summary>
internal static class PylonArt
{
    /// <summary>Rayon des isolateurs, dessinés à part par la carte (leur couleur suit le sens du flux avec le réseau).</summary>
    internal const float InsulatorRadius = 3.2f;

    /// <summary>Centres des quatre isolateurs, en coordonnées locales.</summary>
    internal static readonly SKPoint[] InsulatorCenters = [new(40, 63), new(160, 63), new(54, 39), new(146, 39)];

    public static void Draw(IsoPainter p)
    {
        var back = Stroke.Of("#4d5c78", 1.6f, roundJoin: true);
        p.Path("M78 142 92 62M122 142 108 62", back);

        var steel = new Stroke(ArtPalette.Steel, 2.4f, RoundCap: true, RoundJoin: true);
        p.Path("M62 150 88 62 97 12 100 2 103 12 112 62 138 150", steel);
        p.Path("M68.5 128h63M75 106h50M81.5 84h37M88 62h24M91 44h18M94 28h12", steel);
        p.Path("M62 150 131.5 128 75 106 118.5 84 88 62M138 150 68.5 128 125 106 81.5 84 112 62M91 44 106 28M109 44 94 28", steel);
        p.Path("M88 62 40 54 91 44M112 62 160 54 109 44M94 28 54 30 97 12M106 28 146 30 103 12", steel);

        p.RoundRect(55, 149, 14, 4, 1, "#dfe7f5");
        p.RoundRect(131, 149, 14, 4, 1, "#dfe7f5");
        p.Path("M40 54v8M160 54v8M54 30v8M146 30v8", Stroke.Of("#c8d4e8", 2.4f, roundCap: true));
    }
}
