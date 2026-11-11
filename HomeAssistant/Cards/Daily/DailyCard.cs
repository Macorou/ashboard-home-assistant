using AshboardVision.Sdk;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Ce que la carte sait : les valeurs prêtes à afficher, et si elles sont périmées (lecture en panne).</summary>
internal sealed record DailyState(IReadOnlyList<DailyItem> Items, bool Stale);

/// <summary>
/// La carte « Bilan du jour » : relit les capteurs toutes les 30 s (une lecture à la fois), prépare les valeurs à afficher et annonce
/// l'état de santé du module (spec § 6), puis dessine le résultat avec <see cref="DailyRenderer"/>.
/// </summary>
internal sealed class DailyCard(CardContext context, DailyConfig config) : PollingCard(context, RefreshEvery)
{
    internal static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);

    private readonly HashSet<string> _warnedUnits = new(StringComparer.Ordinal);

    /// <summary>Le dernier état publié, remplacé en entier ; null tant qu'aucune lecture n'a réussi.</summary>
    internal volatile DailyState? Current;

    private readonly DailyRenderer _renderer = new(config);

    public override bool IsAnimating => false;

    protected override IReadOnlyCollection<string> EntityIds => config.EntityIds;

    public override void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        if (!IsClosed)
        {
            _renderer.Render(canvas, ctx, Current);
        }
    }

    protected override void OnRead(IReadOnlyDictionary<string, HaState> states)
    {
        WarnUnknownUnits(states);
        Current = new DailyState(DailyReader.Read(config, states), false);
        Report(ModuleHealth.Online, null);
    }

    protected override void MarkStale()
    {
        if (Current is { Stale: false } current)
        {
            Current = current with { Stale = true };
        }
    }

    /// <summary>Les cinq capteurs d'énergie et celui de l'eau sont concernés : le coût et le prix ont leurs propres unités.</summary>
    private void WarnUnknownUnits(IReadOnlyDictionary<string, HaState> states)
    {
        foreach (var id in new[] { config.Consumption, config.Production, config.Import, config.Export, config.Car })
        {
            Warn(states, id, DailyReader.IsUnknownEnergyUnit, "kWh");
        }
        Warn(states, config.Water, DailyReader.IsUnknownWaterUnit, "litres");
    }

    private void Warn(IReadOnlyDictionary<string, HaState> states, string id, Func<string?, bool> isUnknown, string readAs)
    {
        if (id.Length > 0 && states.TryGetValue(id, out var state) && isUnknown(state.Unit) && _warnedUnits.Add(id))
        {
            Context.Services.Log.Warning($"Unité « {state.Unit} » inconnue pour {id} : elle est traitée comme des {readAs}.");
        }
    }

    protected override void DisposeRendering() => _renderer.Dispose();
}
