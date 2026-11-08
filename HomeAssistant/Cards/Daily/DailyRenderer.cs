using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Dessine le Bilan du jour. Le dessin vient avec la tâche suivante : pour l'instant la carte reste vide.</summary>
internal sealed class DailyRenderer(DailyConfig config) : IDisposable
{
    private readonly DailyConfig _config = config;

    internal void Render(SKCanvas canvas, ModuleRenderContext ctx, DailyState? state)
    {
    }

    public void Dispose()
    {
    }
}
