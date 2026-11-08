using AshboardVision.Sdk;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Ce que la carte sait : la dernière lecture, les flux qui en découlent, et si elle est périmée (lecture en panne).</summary>
internal sealed record EnergyState(EnergySnapshot Snapshot, EnergyFlows Flows, bool Stale);

/// <summary>
/// La carte d'énergie : relit les capteurs toutes les 3 s (une lecture à la fois), calcule les flux et annonce l'état de santé du module
/// (spec § 6). Le dessin arrive avec le moteur de rendu.
/// </summary>
internal sealed class EnergyCard(CardContext context, EnergyConfig config) : ICard
{
    internal static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(3);
    internal const int FailuresBeforeOffline = 3;

    private const string UnknownProblem = "Impossible de lire Home Assistant. Vérifie l'adresse, la clé API et les permissions du module.";

    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _warnedUnits = new(StringComparer.Ordinal);
    private System.Threading.ITimer? _timer;
    private volatile Task _refresh = Task.CompletedTask;
    private volatile bool _closed;

    // Seulement touchés par la lecture en cours, qui est unique.
    private int _failures;
    private (ModuleHealth Health, string? Message)? _status;

    /// <summary>Le dernier état publié, remplacé en entier ; null tant qu'aucune lecture n'a réussi.</summary>
    internal volatile EnergyState? Current;

    public bool IsAnimating => false;

    public void Start()
    {
        Refresh();
        if (!_closed)
        {
            _timer = context.Services.Time.CreateTimer(_ => Refresh(), null, RefreshEvery, RefreshEvery);
        }
    }

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
    }

    /// <summary>Une unité est inconnue si elle est donnée et n'est ni W ni kW : elle est alors traitée comme des W.</summary>
    internal static bool IsUnknownUnit(string? unit) =>
        unit is not null
        && !string.Equals(unit.Trim(), "W", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(unit.Trim(), "kW", StringComparison.OrdinalIgnoreCase);

    private void Refresh()
    {
        try
        {
            lock (_gate)
            {
                if (!_closed && _refresh.IsCompleted)
                {
                    _refresh = RefreshAsync(_stop.Token);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            context.Services.Log.Warning("Actualisation ignorée : " + ex.Message);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var ids = config.EntityIds.ToArray();
            var reads = ids.Select(id => context.Client.GetStateAsync(id, ct)).ToArray();
            try
            {
                await Task.WhenAll(reads).ConfigureAwait(false);
            }
            catch (Exception) when (reads.Any(read => read.IsFaulted || read.IsCanceled))
            {
                // Chaque lecture est examinée plus bas : WhenAll ne rend que la première erreur.
            }

            if (_closed || ct.IsCancellationRequested)
            {
                return;
            }

            if (reads.Any(read => !read.IsCompletedSuccessfully))
            {
                Fail(reads.Where(read => read.IsFaulted).Select(read => read.Exception!.InnerException).ToList());
                return;
            }

            Succeed(ids.Zip(reads, (id, read) => (id, state: read.Result)).ToDictionary(pair => pair.id, pair => pair.state, StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Rien ne doit sortir d'une tâche de fond, surtout pas après la fermeture.
            if (!_closed)
            {
                context.Services.Log.Warning("Lecture de Home Assistant ignorée : " + ex.GetType().Name);
            }
        }
    }

    private void Succeed(Dictionary<string, HaState> states)
    {
        WarnUnknownUnits(states);
        var snapshot = EnergyReader.Combine(config, states);
        _failures = 0;

        if (snapshot.UnavailableGridEntity is { } grid)
        {
            MarkStale();
            Report(ModuleHealth.Offline, $"Le capteur du réseau {grid} est indisponible. Vérifie qu'il fonctionne dans Home Assistant.");
            return;
        }

        Current = new EnergyState(snapshot, EnergyFlows.Compute(snapshot.Input), false);
        Report(ModuleHealth.Online, null);
    }

    private void Fail(IReadOnlyList<Exception?> errors)
    {
        var problems = errors.OfType<HaProblem>().ToList();
        var fatal = problems.FirstOrDefault(p => p.Kind is HaProblemKind.Unauthorized or HaProblemKind.EntityNotFound or HaProblemKind.Certificate);
        if (fatal is not null)
        {
            MarkStale();
            Report(ModuleHealth.Offline, fatal.Message);
            return;
        }

        _failures++;
        if (_failures >= FailuresBeforeOffline)
        {
            MarkStale();
            Report(ModuleHealth.Offline, problems.FirstOrDefault()?.Message ?? UnknownProblem);
            return;
        }

        context.Invalidate();
    }

    private void MarkStale()
    {
        if (Current is { Stale: false } current)
        {
            Current = current with { Stale = true };
        }
    }

    /// <summary>Annonce l'état seulement s'il change, puis demande un nouveau dessin.</summary>
    private void Report(ModuleHealth health, string? message)
    {
        if (_status != (health, message))
        {
            _status = (health, message);
            context.SetStatus(health, message);
        }

        context.Invalidate();
    }

    private void WarnUnknownUnits(Dictionary<string, HaState> states)
    {
        foreach (var (id, state) in states)
        {
            // Le niveau de batterie est en % : ce n'est pas une puissance.
            if (id != config.BatterySoc && IsUnknownUnit(state.Unit) && _warnedUnits.Add(id))
            {
                context.Services.Log.Warning($"Unité « {state.Unit} » inconnue pour {id} : elle est traitée comme des W.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;
        _timer?.Dispose();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _refresh.ConfigureAwait(false);
        context.Client.Dispose();
        _stop.Dispose();
    }
}
