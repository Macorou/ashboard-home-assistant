using SkiaSharp;
using static HomeAssistant.IsoPainter;

namespace HomeAssistant;

/// <summary>Trois modules de panneaux solaires sur leur structure (<c>panels</c> de la maquette), dans les coordonnées locales de la maquette.</summary>
internal static class PanelsArt
{
    private static readonly Projection PI = Iso(15, 70, 66);

    /// <summary>Point du plan des modules, (u, v) entre 0 et 1 (u de gauche à droite, v de bas en haut), en coordonnées locales. Sert au reflet animé.</summary>
    internal static SKPoint Plane(float u, float v) => P(u, v);

    private static SKPoint P(double u, double v) => PI(9 * u, 4.4 * (1 - v), 3.4 * v);

    public static void Draw(IsoPainter p)
    {
        const int Col = 5, Row = 6;
        long seed = 7;
        double Rnd()
        {
            seed = seed * 16807 % 2147483647;
            return seed / 2147483647.0;
        }

        // structure
        var a = PI(9, 4.4, 0);
        var b = PI(9, 0, 3.4);
        var c = PI(9, 0, -.2);
        p.Lines(new Stroke(ArtPalette.AluS, 3, RoundCap: true), [(b, c), (c, a), (PI(9, 2.2, 1.7), c)]);

        // épaisseur du module
        SKPoint fl = P(0, 0), fr = P(1, 0), br = P(1, 1), bl = P(0, 1);
        const float dz = 3.2f;
        p.Poly([fl, fr, new SKPoint(fr.X, fr.Y + dz), new SKPoint(fl.X, fl.Y + dz)], "#9aa8bf");
        p.Poly([fr, br, new SKPoint(br.X, br.Y + dz), new SKPoint(fr.X, fr.Y + dz)], "#6e7d98");

        // verre
        p.Poly([fl, fr, br, bl], "#050a16");
        var bus = new List<(SKPoint, SKPoint)>();
        const double gapU = .0045, gapV = .006;
        for (var m = 0; m < 3; m++)
        {
            double u0 = m / 3.0 + .008, u1 = (m + 1) / 3.0 - .008;
            for (var col = 0; col < Col; col++)
            {
                for (var r = 0; r < Row; r++)
                {
                    double a0 = u0 + (u1 - u0) * col / Col + gapU, a1 = u0 + (u1 - u0) * (col + 1) / Col - gapU;
                    double b0 = .014 + .972 * r / Row + gapV, b1 = .014 + .972 * (r + 1) / Row - gapV;
                    SKPoint[] q4 = [P(a0, b0), P(a1, b0), P(a1, b1), P(a0, b1)];
                    p.Poly(q4, ArtPalette.Cell);
                    // La valeur est tirée à chaque cellule, allumée ou non, pour suivre l'ordre de la maquette.
                    var j = (float)Math.Round(.72 + Rnd() * .28, 2, MidpointRounding.AwayFromZero);
                    if (p.Lit) p.Poly(q4, ArtPalette.CellLit.WithOpacity(j));
                    var a3 = a0 + (a1 - a0) / 3;
                    var a6 = a0 + (a1 - a0) * 2 / 3;
                    bus.Add((P(a3, b0), P(a3, b1)));
                    bus.Add((P(a6, b0), P(a6, b1)));
                }
            }
        }
        p.Lines(Stroke.Of("#dfe9ff", .55f, .32f), bus);

        // cadres des modules
        for (var m = 0; m < 3; m++)
        {
            double uA = m / 3.0 + .004, uB = (m + 1) / 3.0 - .004;
            p.Poly([P(uA, .004), P(uB, .004), P(uB, .996), P(uA, .996)], null, Stroke.Of("#e6ecf6", 1.6f, roundJoin: true));
        }
    }
}
