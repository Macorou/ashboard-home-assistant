using SkiaSharp;

namespace HomeAssistant;

/// <summary>Les icônes et les couleurs d'accent des valeurs, reprises de la maquette (tracés dans un carré de 24).</summary>
internal static class DailyIcons
{
    internal const float Box = 24;
    internal const float StrokeWidth = 1.9f;

    private static readonly Dictionary<DailyValue, SKPath> Paths = new()
    {
        [DailyValue.Consumption] = Parse("M3 11.5 12 4l9 7.5", "M5.5 9.8V20h13V9.8", "M10 20v-5.5h4V20"),
        [DailyValue.Production] = Parse(circle: (12, 12, 4.2f),
            "M12 2.5v2.4M12 19.1v2.4M2.5 12h2.4M19.1 12h2.4M5.3 5.3 7 7M17 17l1.7 1.7M18.7 5.3 17 7M7 17l-1.7 1.7"),
        [DailyValue.Import] = Parse("M12 3.5v13", "m6.5 11.5 5.5 5.5 5.5-5.5", "M5 20.5h14"),
        [DailyValue.Export] = Parse("M12 17V4", "M6.5 9.5 12 4l5.5 5.5", "M5 20.5h14"),
        [DailyValue.Cost] = Parse("M18 6.2A7.5 7.5 0 1 0 18 17.8", "M3.5 10.2h10M3.5 13.8h9"),
        [DailyValue.Resale] = Parse(circle: (12, 12, 9f), "M15.5 8.5A4.3 4.3 0 1 0 15.5 15.5", "M7 11h6M7 13.2h5"),
    };

    /// <summary>Le tracé de l'icône, dans un carré de 24 ; à dessiner en trait de <see cref="StrokeWidth"/>.</summary>
    internal static SKPath PathOf(DailyValue value) => Paths[value];

    internal static SKColor ColorOf(DailyValue value) => SKColor.Parse(value switch
    {
        DailyValue.Consumption => "#8fb4ff",
        DailyValue.Production => "#ffd166",
        DailyValue.Import => "#ff7a45",
        DailyValue.Export => "#34e39a",
        DailyValue.Cost => "#ff9db0",
        _ => "#5ff0c0",
    });

    private static SKPath Parse(params string[] data) => Parse(null, data);

    private static SKPath Parse((float X, float Y, float R)? circle, params string[] data)
    {
        var path = new SKPath();
        if (circle is { } c)
        {
            path.AddCircle(c.X, c.Y, c.R);
        }
        foreach (var item in data)
        {
            path.AddPath(SKPath.ParseSvgPathData(item));
        }
        return path;
    }
}
