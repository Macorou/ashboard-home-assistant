using SkiaSharp;

namespace HomeAssistant.Tests;

/// <summary>
/// L'icône du module (<c>HomeAssistant/icon.png</c>) : une maison contemporaine tirée du dessin du module, avec un filament d'or, sur fond transparent.
/// Sans <c>WRITE_ICON=1</c>, le test vérifie seulement que le fichier existe et fait 256 × 256 pixels. Avec <c>WRITE_ICON=1</c>, il le réécrit.
/// </summary>
public sealed class IconTests
{
    private const int Side = 256;
    private const float HouseSide = 200;

    private static readonly SKColor Gold = SKColor.Parse("#ffc23d");

    private static string IconPath => Path.Combine(RepositoryRoot(), "HomeAssistant", "icon.png");

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HomeAssistant.slnx"))) return dir.FullName;
        }

        throw new DirectoryNotFoundException("HomeAssistant.slnx est introuvable au-dessus de " + AppContext.BaseDirectory);
    }

    /// <summary>La maison (dessins éteints puis allumés, à pleine opacité) et un filament d'or qui monte jusqu'à elle, depuis le bas à gauche.</summary>
    private static byte[] RenderIcon()
    {
        using var cache = new ArtCache();

        // La maison est mesurée à l'échelle 1, puis redessinée à l'échelle qui la fait tenir dans HouseSide pixels.
        var measure = cache.Get(ArtKind.HouseModern, 1);
        var scale = HouseSide / Math.Max(measure.Lit.Width, measure.Lit.Height);
        var art = cache.Get(ArtKind.HouseModern, scale);

        var left = (Side - art.Lit.Width) / 2f + 14;
        var top = (Side - art.Lit.Height) / 2f - 6;

        using var surface = SKSurface.Create(new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawImage(art.Dark, left, top);
        canvas.DrawImage(art.Lit, left, top);

        // Le filament : un court arc d'or depuis le bas à gauche, jusqu'au pied de la façade.
        var from = new SKPoint(26, 236);
        var to = new SKPoint(left + art.Lit.Width * .34f, top + art.Lit.Height * .84f);
        var control = new SKPoint((from.X + to.X) / 2, (from.Y + to.Y) / 2 + 18);
        using var path = new SKPath();
        path.MoveTo(from);
        path.QuadTo(control, to);

        using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 9, StrokeCap = SKStrokeCap.Round, Color = Gold.WithAlpha(60) };
        using var core = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, StrokeCap = SKStrokeCap.Round, Color = Gold };
        using var head = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
        canvas.DrawPath(path, glow);
        canvas.DrawPath(path, core);
        canvas.DrawCircle(from, 3f, head);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void WriteIcon()
    {
        var file = IconPath;
        if (Environment.GetEnvironmentVariable("WRITE_ICON") == "1")
        {
            File.WriteAllBytes(file, RenderIcon());
        }

        Assert.True(File.Exists(file), $"L'icône est absente : {file}. Lance le test avec WRITE_ICON=1 pour la produire.");
        var bytes = File.ReadAllBytes(file);
        Assert.True(bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G', "Ce n'est pas un PNG.");

        using var codec = SKCodec.Create(file);
        Assert.NotNull(codec);
        Assert.Equal(Side, codec.Info.Width);
        Assert.Equal(Side, codec.Info.Height);
    }
}
