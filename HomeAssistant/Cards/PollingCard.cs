using AshboardVision.Sdk;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>
/// Une carte qui relit Home Assistant à intervalle régulier (une lecture à la fois), annonce l'état de santé du module (spec § 6)
/// et se ferme proprement. Les cartes filles disent quoi lire (<see cref="EntityIds"/>), que faire du résultat (<see cref="OnRead"/>)
/// et comment marquer leurs données périmées (<see cref="MarkStale"/>).
/// </summary>
internal abstract class PollingCard(CardContext context, TimeSpan every) : ICard
{
    internal const int FailuresBeforeOffline = 3;

    /// <summary>Après un refus de la clé, on n'insiste pas : Home Assistant peut bannir l'adresse après trop d'essais.</summary>
    internal static readonly TimeSpan RetryAfterUnauthorized = TimeSpan.FromSeconds(60);

    private const string UnknownProblem = "Impossible de lire Home Assistant. Vérifie l'adresse, la clé API et les permissions du module.";

    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private System.Threading.ITimer? _timer;
    private volatile Task _refresh = Task.CompletedTask;
    private volatile bool _closed;

    // Seulement touchés par la lecture en cours, qui est unique.
    private int _failures;
    private DateTimeOffset? _retryAt;
    private (ModuleHealth Health, string? Message)? _status;

    protected CardContext Context { get; } = context;

    /// <summary>Vrai une fois la carte fermée : plus rien ne doit être lu ni dessiné.</summary>
    protected bool IsClosed => _closed;

    /// <summary>Les entités à lire à chaque actualisation.</summary>
    protected abstract IReadOnlyCollection<string> EntityIds { get; }

    /// <summary>Reçoit une lecture réussie, après la remise à zéro des échecs.</summary>
    protected abstract void OnRead(IReadOnlyDictionary<string, HaState> states);

    /// <summary>Marque les données publiées comme périmées quand la lecture est en panne.</summary>
    protected abstract void MarkStale();

    /// <summary>Libère ce que la carte fille dessine ; appelé en dernier à la fermeture.</summary>
    protected virtual void DisposeRendering() { }

    public abstract bool IsAnimating { get; }

    public abstract void Render(SKCanvas canvas, ModuleRenderContext ctx);

    public void Start()
    {
        Refresh();
        if (!_closed)
        {
            _timer = Context.Services.Time.CreateTimer(_ => Refresh(), null, every, every);
        }
    }

    private void Refresh()
    {
        try
        {
            lock (_gate)
            {
                if (!_closed && _refresh.IsCompleted && !(_retryAt is { } retryAt && Context.Services.Time.GetUtcNow() < retryAt))
                {
                    _refresh = RefreshAsync(_stop.Token);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Context.Services.Log.Warning("Actualisation ignorée : " + ex.Message);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var ids = EntityIds.ToArray();
            var reads = ids.Select(id => Context.Client.GetStateAsync(id, ct)).ToArray();
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

            var states = ids.Zip(reads, (id, read) => (id, state: read.Result)).ToDictionary(pair => pair.id, pair => pair.state, StringComparer.Ordinal);
            _failures = 0;
            _retryAt = null;
            OnRead(states);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Rien ne doit sortir d'une tâche de fond, surtout pas après la fermeture.
            if (!_closed)
            {
                Context.Services.Log.Warning("Lecture de Home Assistant ignorée : " + ex.GetType().Name);
            }
        }
    }

    private void Fail(IReadOnlyList<Exception?> errors)
    {
        var problems = errors.OfType<HaProblem>().ToList();
        var fatal = problems.FirstOrDefault(p => p.Kind is HaProblemKind.Unauthorized or HaProblemKind.EntityNotFound or HaProblemKind.Certificate);
        if (fatal is not null)
        {
            if (fatal.Kind == HaProblemKind.Unauthorized)
            {
                _retryAt = Context.Services.Time.GetUtcNow() + RetryAfterUnauthorized;
            }

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

        Context.Invalidate();
    }

    /// <summary>Annonce l'état seulement s'il change, puis demande un nouveau dessin.</summary>
    protected void Report(ModuleHealth health, string? message)
    {
        if (_status != (health, message))
        {
            _status = (health, message);
            Context.SetStatus(health, message);
        }

        Context.Invalidate();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
        }

        _timer?.Dispose();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _refresh.ConfigureAwait(false);
        Context.Client.Dispose();
        _stop.Dispose();
        DisposeRendering();
    }
}
