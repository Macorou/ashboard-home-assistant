using SkiaSharp;

namespace HomeAssistant;

internal enum ArtKind { HouseModern, HouseClassic, Panels, Pylon, Battery }

/// <summary>
/// Les deux images d'un dessin à une échelle donnée. <see cref="Dark"/> : fenêtres et cellules éteintes ; <see cref="Lit"/> : allumées
/// (lumières et halo cuits), à fondre sur <see cref="Dark"/>. Le pylône et la batterie n'ont rien d'allumé : leur <see cref="Lit"/> est transparent.
/// <para>
/// <see cref="Scale"/> est le nombre de pixels par unité des coordonnées locales de la maquette (celles du groupe SVG avant sa transformation).
/// <see cref="Origin"/> est la position, en pixels de l'image, du point local (0, 0) : pour poser le dessin avec son coin local (lx, ly),
/// dessine l'image en (lx × Scale − Origin.X, ly × Scale − Origin.Y), les deux en pixels.
/// </para>
/// </summary>
internal sealed record ArtImages(SKImage Dark, SKImage Lit, SKPoint Origin, float Scale);

/// <summary>Dessine chaque dessin une fois par échelle, dans des images de la taille voulue (pas de réduction d'image : les traits restent nets).</summary>
internal sealed class ArtCache : IDisposable
{
    private const float Margin = 1f;

    private readonly Dictionary<(ArtKind, float), ArtImages> _images = [];
    private readonly object _gate = new();
    private bool _disposed;

    public ArtImages Get(ArtKind kind, float scale)
    {
        if (!(scale > 0) || float.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_images.TryGetValue((kind, scale), out var images))
                _images[(kind, scale)] = images = Render(kind, scale);
            return images;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var images in _images.Values)
            {
                images.Dark.Dispose();
                images.Lit.Dispose();
            }
            _images.Clear();
        }
    }

    private static void Draw(ArtKind kind, IsoPainter p)
    {
        switch (kind)
        {
            case ArtKind.HouseModern: HouseModernArt.Draw(p); break;
            case ArtKind.HouseClassic: HouseClassicArt.Draw(p); break;
            case ArtKind.Panels: PanelsArt.Draw(p); break;
            case ArtKind.Pylon: PylonArt.Draw(p); break;
            case ArtKind.Battery: BatteryArt.Draw(p); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static bool HasLit(ArtKind kind) => kind is ArtKind.HouseModern or ArtKind.HouseClassic or ArtKind.Panels;

    private static ArtImages Render(ArtKind kind, float scale)
    {
        // Première passe, sur un enregistrement : mesure l'encre (traits et halos compris) en coordonnées locales.
        SKRect ink;
        using (var recorder = new SKPictureRecorder())
        {
            var canvas = recorder.BeginRecording(new SKRect(-2000, -2000, 2000, 2000));
            var painter = new IsoPainter(canvas) { Lit = true };
            Draw(kind, painter);
            ink = painter.Ink;
            using var _ = recorder.EndRecording();
        }

        var left = (int)Math.Floor(ink.Left * scale - Margin);
        var top = (int)Math.Floor(ink.Top * scale - Margin);
        var width = Math.Max(1, (int)Math.Ceiling(ink.Right * scale + Margin) - left);
        var height = Math.Max(1, (int)Math.Ceiling(ink.Bottom * scale + Margin) - top);

        SKImage Paint(bool lit)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            if (!lit || HasLit(kind))
            {
                canvas.Translate(-left, -top);
                canvas.Scale(scale);
                Draw(kind, new IsoPainter(canvas) { Lit = lit });
            }
            return surface.Snapshot();
        }

        return new ArtImages(Paint(false), Paint(true), new SKPoint(-left, -top), scale);
    }
}
