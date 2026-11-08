using SkiaSharp;

namespace HomeAssistant;

internal enum FlowKey { SolarToHome, SolarToGrid, GridToHome, SolarToBattery, BatteryToHome, GridToBattery }

/// <summary>Un dessin posé dans l'espace 700 × 434 : le groupe SVG <c>translate(X,Y) scale(Scale)</c> de la maquette. La maison est toujours posée en <see cref="ArtKind.HouseModern"/> : le moteur la remplace selon le style choisi.</summary>
internal sealed record ArtPlacement(ArtKind Kind, float X, float Y, float Scale);

/// <summary>Un tracé de flux : courbe de Bézier cubique (quatre points) et couleur de sa source.</summary>
internal sealed record FlowPath(FlowKey Key, SKColor Color, SKPoint[] Bezier);

/// <summary>
/// Un emplacement de texte. Rôles : <c>solarLabel</c>, <c>solarValue</c>, <c>homeLabel</c>, <c>homeValue</c>, <c>gridLabel</c>, <c>gridValue</c>,
/// <c>batLabel</c>, <c>batValue</c> et <c>share</c> (la part solaire sur sa propre ligne ; absent, elle suit « Consommation · »).
/// </summary>
internal sealed record TextSlot(string Role, float X, float Y, bool Centered, float Size);

/// <summary>Un halo de fond. Rôles : <c>solar</c>, <c>exp</c>, <c>imp</c>, <c>home</c>, <c>bat</c>.</summary>
internal sealed record HaloSpot(string Role, float X, float Y, float Radius);

internal sealed record EnergyLayout(IReadOnlyList<ArtPlacement> Arts, IReadOnlyList<FlowPath> Flows, IReadOnlyList<TextSlot> Texts, IReadOnlyList<HaloSpot> Halos);

/// <summary>Les six dispositions de la maquette (trois pointes, avec et sans batterie), dans l'espace 700 × 434. Les valeurs sont celles des <c>svg data-variant</c>.</summary>
internal static class EnergyLayouts
{
    internal const float Width = 700;
    internal const float Height = 434;

    private static readonly SKColor Gold = SKColor.Parse("#ffc23d");
    private static readonly SKColor Green = SKColor.Parse("#34e39a");
    private static readonly SKColor Orange = SKColor.Parse("#ff7a45");
    private static readonly SKColor Violet = SKColor.Parse("#b58cff");

    private static readonly Dictionary<(LayoutKind, bool), EnergyLayout> Cache = [];

    internal static EnergyLayout Get(LayoutKind kind, bool withBattery)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((kind, withBattery), out var layout))
                Cache[(kind, withBattery)] = layout = Build(kind, withBattery);
            return layout;
        }
    }

    private static EnergyLayout Build(LayoutKind kind, bool bat) => kind switch
    {
        LayoutKind.Top => bat ? TopBat() : TopNoBat(),
        LayoutKind.Left => bat ? LeftBat() : LeftNoBat(),
        _ => bat ? BottomBat() : BottomNoBat(),
    };

    private static ArtPlacement Art(ArtKind kind, float x, float y, float scale) => new(kind, x, y, scale);

    private static HaloSpot Halo(string role, float x, float y, float r) => new(role, x, y, r);

    private static TextSlot Label(string role, float x, float y, bool centered) => new(role, x, y, centered, 14);

    private static TextSlot Value(string role, float x, float y, bool centered, float size) => new(role, x, y, centered, size);

    private static FlowPath Flow(FlowKey key, SKColor color, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3) =>
        new(key, color, [new(x0, y0), new(x1, y1), new(x2, y2), new(x3, y3)]);

    private static EnergyLayout TopNoBat() => new(
        [Art(ArtKind.Panels, 265, 0, .85f), Art(ArtKind.HouseModern, 55, 205, .9f), Art(ArtKind.Pylon, 490, 225, .9f)],
        [
            Flow(FlowKey.SolarToHome, Gold, 350, 76, 300, 130, 240, 210, 181, 295),
            Flow(FlowKey.SolarToGrid, Green, 350, 76, 420, 150, 510, 180, 539, 260),
            Flow(FlowKey.GridToHome, Orange, 526, 282, 450, 335, 330, 340, 203, 317),
        ],
        [
            Label("solarLabel", 440, 50, false), Value("solarValue", 440, 98, false, 42),
            Label("share", 350, 272, true),
            Label("homeLabel", 145, 384, true), Value("homeValue", 145, 424, true, 40),
            Label("gridLabel", 580, 384, true), Value("gridValue", 580, 424, true, 40),
        ],
        [Halo("solar", 350, 70, 230), Halo("exp", 585, 320, 220), Halo("imp", 585, 320, 220), Halo("home", 150, 310, 240)]);

    private static EnergyLayout TopBat() => new(
        [Art(ArtKind.Panels, 265, 0, .85f), Art(ArtKind.HouseModern, 55, 205, .9f), Art(ArtKind.Pylon, 490, 225, .9f), Art(ArtKind.Battery, 321, 172, .7f)],
        [
            Flow(FlowKey.SolarToHome, Gold, 350, 76, 300, 130, 240, 210, 181, 295),
            Flow(FlowKey.SolarToGrid, Green, 350, 76, 420, 150, 510, 180, 539, 260),
            Flow(FlowKey.SolarToBattery, Gold, 350, 76, 350, 110, 350, 150, 350, 195),
            Flow(FlowKey.BatteryToHome, Violet, 335, 245, 290, 262, 240, 285, 190, 305),
            Flow(FlowKey.GridToBattery, Orange, 526, 282, 470, 268, 420, 252, 372, 240),
            Flow(FlowKey.GridToHome, Orange, 526, 290, 450, 375, 330, 375, 203, 325),
        ],
        [
            Label("solarLabel", 440, 50, false), Value("solarValue", 440, 98, false, 42),
            Label("batLabel", 350, 298, true), Value("batValue", 350, 336, true, 34),
            Label("homeLabel", 145, 384, true), Value("homeValue", 145, 424, true, 40),
            Label("gridLabel", 580, 384, true), Value("gridValue", 580, 424, true, 40),
        ],
        [Halo("solar", 350, 70, 230), Halo("exp", 585, 320, 220), Halo("imp", 585, 320, 220), Halo("home", 150, 310, 240), Halo("bat", 350, 230, 170)]);

    private static EnergyLayout LeftNoBat() => new(
        [Art(ArtKind.HouseModern, 10, 40, 1.4f), Art(ArtKind.Panels, 350, 10, .75f), Art(ArtKind.Pylon, 360, 280, .8f)],
        [
            Flow(FlowKey.SolarToHome, Gold, 425, 77, 360, 90, 290, 120, 206, 180),
            Flow(FlowKey.SolarToGrid, Green, 430, 90, 472, 150, 472, 230, 442, 290),
            Flow(FlowKey.GridToHome, Orange, 392, 343, 340, 335, 290, 280, 241, 215),
        ],
        [
            Label("homeLabel", 150, 322, true), Value("homeValue", 150, 384, true, 58),
            Label("solarLabel", 515, 62, false), Value("solarValue", 515, 110, false, 42),
            Label("gridLabel", 515, 330, false), Value("gridValue", 515, 378, false, 42),
        ],
        [Halo("home", 150, 170, 260), Halo("solar", 430, 80, 210), Halo("exp", 440, 350, 210), Halo("imp", 440, 350, 210)]);

    private static EnergyLayout LeftBat() => new(
        [Art(ArtKind.HouseModern, 10, 40, 1.4f), Art(ArtKind.Panels, 350, 10, .75f), Art(ArtKind.Pylon, 360, 280, .8f), Art(ArtKind.Battery, 343, 155, .7f)],
        [
            Flow(FlowKey.SolarToHome, Gold, 425, 77, 360, 90, 290, 120, 206, 180),
            Flow(FlowKey.SolarToGrid, Green, 430, 90, 478, 150, 478, 230, 442, 290),
            Flow(FlowKey.SolarToBattery, Gold, 415, 85, 405, 110, 395, 135, 378, 170),
            Flow(FlowKey.BatteryToHome, Violet, 348, 212, 320, 220, 285, 216, 245, 200),
            Flow(FlowKey.GridToBattery, Orange, 432, 305, 425, 288, 410, 274, 384, 250),
            Flow(FlowKey.GridToHome, Orange, 392, 343, 340, 335, 290, 280, 241, 215),
        ],
        [
            Label("homeLabel", 150, 322, true), Value("homeValue", 150, 384, true, 58),
            Label("solarLabel", 515, 62, false), Value("solarValue", 515, 110, false, 42),
            Label("batLabel", 515, 196, false), Value("batValue", 515, 244, false, 42),
            Label("gridLabel", 515, 330, false), Value("gridValue", 515, 378, false, 42),
        ],
        [Halo("home", 150, 170, 260), Halo("solar", 430, 80, 210), Halo("exp", 440, 350, 210), Halo("imp", 440, 350, 210), Halo("bat", 375, 205, 150)]);

    private static EnergyLayout BottomNoBat() => new(
        [Art(ArtKind.Panels, 30, 6, .85f), Art(ArtKind.Pylon, 530, 6, .9f), Art(ArtKind.HouseModern, 222, 165, 1.35f)],
        [
            Flow(FlowKey.SolarToGrid, Green, 115, 82, 300, 170, 440, 150, 566, 63),
            Flow(FlowKey.SolarToHome, Gold, 115, 82, 180, 160, 230, 260, 303, 327),
            Flow(FlowKey.GridToHome, Orange, 566, 63, 530, 180, 490, 270, 445, 334),
        ],
        [
            Label("homeLabel", 350, 40, true), Value("homeValue", 350, 100, true, 58),
            Label("solarLabel", 105, 210, true), Value("solarValue", 105, 258, true, 42),
            Label("gridLabel", 595, 210, true), Value("gridValue", 595, 258, true, 42),
        ],
        [Halo("solar", 115, 90, 230), Halo("exp", 600, 90, 230), Halo("imp", 600, 90, 230), Halo("home", 350, 310, 260)]);

    private static EnergyLayout BottomBat() => new(
        [Art(ArtKind.Panels, 30, 6, .85f), Art(ArtKind.Pylon, 530, 6, .9f), Art(ArtKind.HouseModern, 222, 165, 1.35f), Art(ArtKind.Battery, 323, 53, .65f)],
        [
            Flow(FlowKey.SolarToGrid, Green, 120, 52, 250, 2, 450, 2, 566, 42),
            Flow(FlowKey.SolarToHome, Gold, 115, 82, 180, 160, 230, 260, 303, 327),
            Flow(FlowKey.SolarToBattery, Gold, 140, 74, 210, 82, 270, 90, 325, 100),
            Flow(FlowKey.BatteryToHome, Violet, 350, 135, 350, 160, 350, 190, 350, 220),
            Flow(FlowKey.GridToBattery, Orange, 566, 63, 500, 75, 430, 88, 378, 100),
            Flow(FlowKey.GridToHome, Orange, 566, 63, 530, 180, 490, 270, 445, 334),
        ],
        [
            Label("solarLabel", 105, 210, true), Value("solarValue", 105, 258, true, 42),
            Label("homeLabel", 105, 330, true), Value("homeValue", 105, 378, true, 42),
            Label("gridLabel", 595, 210, true), Value("gridValue", 595, 258, true, 42),
            Label("batLabel", 595, 330, true), Value("batValue", 595, 378, true, 42),
        ],
        [Halo("solar", 115, 90, 230), Halo("exp", 600, 90, 230), Halo("imp", 600, 90, 230), Halo("home", 350, 310, 260), Halo("bat", 350, 100, 150)]);
}
