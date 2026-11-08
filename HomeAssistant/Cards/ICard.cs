using AshboardVision.Sdk;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Ce qu'une carte reçoit du module : les services, le client Home Assistant (la carte le libère), et les deux retours vers la case.</summary>
internal sealed record CardContext(ModuleServices Services, HaClient Client, Action Invalidate, Action<ModuleHealth, string?> SetStatus);

/// <summary>Une carte que le module affiche dans la case. <see cref="Start"/> lance sa lecture de Home Assistant ; la fermeture l'arrête.</summary>
internal interface ICard : IAsyncDisposable
{
    bool IsAnimating { get; }

    void Start();

    void Render(SKCanvas canvas, ModuleRenderContext ctx);
}
