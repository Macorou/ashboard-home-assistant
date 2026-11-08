using AshboardVision.Sdk;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Ce que la carte sait : la dernière lecture, les flux qui en découlent, et si elle est périmée (lecture en panne).</summary>
internal sealed record EnergyState(EnergySnapshot Snapshot, EnergyFlows Flows, bool Stale);

/// <summary>
/// La carte d'énergie : relit les capteurs toutes les 3 s (une lecture à la fois), calcule les flux et annonce l'état de santé du module
/// (spec § 6), puis dessine le résultat avec <see cref="EnergyRenderer"/>.
/// </summary>
internal sealed class EnergyCard(CardContext context, EnergyConfig config) : PollingCard(context, RefreshEvery)
{
    internal static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(3);

    private readonly HashSet<string> _warnedUnits = new(StringComparer.Ordinal);

    /// <summary>Le dernier état publié, remplacé en entier ; null tant qu'aucune lecture n'a réussi.</summary>
    internal volatile EnergyState? Current;

    private readonly EnergyRenderer _renderer = new(config);

    public override bool IsAnimating => !IsClosed && !_renderer.ShowsMessage && _renderer.IsAnimating(Current);

    protected override IReadOnlyCollection<string> EntityIds => config.EntityIds.ToArray();

    public override void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        if (!IsClosed)
        {
            _renderer.Render(canvas, ctx, Current);
        }
    }

    /// <summary>Une unité est inconnue si elle est donnée et n'est ni W ni kW : elle est alors traitée comme des W.</summary>
    internal static bool IsUnknownUnit(string? unit) =>
        unit is not null
        && !string.Equals(unit.Trim(), "W", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(unit.Trim(), "kW", StringComparison.OrdinalIgnoreCase);

    protected override void OnRead(IReadOnlyDictionary<string, HaState> states)
    {
        WarnUnknownUnits(states);
        var snapshot = EnergyReader.Combine(config, states);

        if (snapshot.UnavailableGridEntity is { } grid)
        {
            MarkStale();
            Report(ModuleHealth.Offline, $"Le capteur du réseau {grid} est indisponible. Vérifie qu'il fonctionne dans Home Assistant.");
            return;
        }

        Current = new EnergyState(snapshot, EnergyFlows.Compute(snapshot.Input), false);
        Report(ModuleHealth.Online, null);
    }

    protected override void MarkStale()
    {
        if (Current is { Stale: false } current)
        {
            Current = current with { Stale = true };
        }
    }

    private void WarnUnknownUnits(IReadOnlyDictionary<string, HaState> states)
    {
        foreach (var (id, state) in states)
        {
            // Le niveau de batterie est en % : ce n'est pas une puissance.
            if (id != config.BatterySoc && IsUnknownUnit(state.Unit) && _warnedUnits.Add(id))
            {
                Context.Services.Log.Warning($"Unité « {state.Unit} » inconnue pour {id} : elle est traitée comme des W.");
            }
        }
    }

    protected override void DisposeRendering() => _renderer.Dispose();
}
