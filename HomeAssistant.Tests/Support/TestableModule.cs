namespace HomeAssistant.Tests;

/// <summary>Le module avec un faux client HTTP. Le test pose <see cref="Handler"/> avant de créer l'hôte ; deux tests en parallèle ne se voient pas.</summary>
internal sealed class TestableModule : HomeAssistantModule
{
    public static readonly AsyncLocal<HttpMessageHandler?> Handler = new();

    /// <summary>Si posée avant la création de l'hôte, reçoit l'instance du module, pour appeler <c>LookupAsync</c> et <c>RunActionAsync</c>.</summary>
    public static readonly AsyncLocal<Holder?> Probe = new();

    public TestableModule()
    {
        if (Probe.Value is { } holder)
        {
            holder.Module = this;
        }
    }

    /// <summary>Le thème du dernier dessin, pour que la galerie pose l'image sur le fond du thème.</summary>
    public Trofeo.ModuleKit.Theme? LastTheme { get; private set; }

    public override void Render(SkiaSharp.SKCanvas canvas, Trofeo.ModuleKit.ModuleRenderContext ctx)
    {
        LastTheme = ctx.Theme;
        base.Render(canvas, ctx);
    }

    internal override HttpClient CreateHttpClient(string? certificateKey) =>
        new(Handler.Value ?? throw new InvalidOperationException("Pose TestableModule.Handler avant StartAsync."), disposeHandler: false);

    public sealed class Holder
    {
        public TestableModule? Module;
    }
}
