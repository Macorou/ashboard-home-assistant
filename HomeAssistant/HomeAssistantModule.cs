using System.Text.Json;
using AshboardVision.Sdk;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>
/// Le module « Home Assistant » : il lit une installation Home Assistant (lecture seule) et dessine la carte choisie dans les réglages de
/// la case. Pour l'instant, il vérifie seulement que la case est configurée et le dit sinon.
/// </summary>
public class HomeAssistantModule : CommunityModule
{
    /// <summary>
    /// Le client HTTP du module. C'est le point d'entrée des tests : une sous-classe du projet de tests le redéfinit pour y glisser de
    /// fausses réponses de Home Assistant.
    /// </summary>
    internal virtual HttpClient CreateHttpClient(string? certificateKey) => Services.CreateHttpClient(certificateKey);

    private ICard? _card;

    /// <summary>La carte affichée dans la case, ou null tant que le module n'est pas configuré. Les tests la lisent.</summary>
    internal ICard? Card => _card;

    private const string StartupFillConnection = "Indique l'adresse de Home Assistant et ta clé API dans les réglages du module.";

    /// <summary>Vérifie la configuration de la case, choisit la carte d'après le réglage « display » et lance sa lecture.</summary>
    public override Task StartAsync(CancellationToken ct)
    {
        var client = CreateClient(StartupFillConnection, checkPermissions: true, out var problem);
        if (client is null)
        {
            SetStatus(ModuleHealth.NotConfigured, problem);
            return Task.CompletedTask;
        }

        if (!EnergyConfig.TryRead(Services.Settings, out var config, out var configProblem))
        {
            client.Dispose();
            SetStatus(ModuleHealth.NotConfigured, configProblem);
            return Task.CompletedTask;
        }

        // Une seule carte existe pour l'instant ; le réglage garde la place des suivantes.
        _ = SettingsReader.Choice(Services.Settings, "display", "energy", "energy");
        var card = new EnergyCard(new CardContext(Services, client, Invalidate, (health, message) => SetStatus(health, message)), config!);
        _card = card;
        card.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Le message qui dit quelle permission cocher, ou null si l'adresse est accessible. Une IP privée demande « Réseau local », une IP
    /// publique « Internet » ; un nom d'hôte ne dit pas où il mène, il suffit donc d'une des deux.
    /// </summary>
    private string? MissingPermissionMessage(HaAddress address)
    {
        const string LocalNetworkMessage = "Accorde la permission « Réseau local » à ce module dans la fenêtre de réglage d'Ashboard Vision : l'adresse est sur ton réseau.";
        const string InternetMessage = "Accorde la permission « Internet » à ce module dans la fenêtre de réglage d'Ashboard Vision : l'adresse est sur Internet.";
        const string EitherMessage = "Accorde la permission « Réseau local » ou « Internet » à ce module dans la fenêtre de réglage d'Ashboard Vision.";

        if (address.IsIpLiteral)
        {
            if (address.IsPrivate)
            {
                return Services.Has(ModulePermission.LocalNetwork) ? null : LocalNetworkMessage;
            }

            return Services.Has(ModulePermission.Internet) ? null : InternetMessage;
        }

        return Services.Has(ModulePermission.LocalNetwork) || Services.Has(ModulePermission.Internet) ? null : EitherMessage;
    }

    private const string FillConnectionFirst = "Remplis d'abord l'adresse et la clé API";

    /// <summary>
    /// Construit le client à partir des secrets <c>ha_url</c> et <c>ha_token</c> (jamais des réglages), ou rend null avec le message à
    /// montrer. Sert aux recherches et aux boutons, qui tournent sur une instance où <c>StartAsync</c> n'a pas eu lieu. L'appelant
    /// libère le client.
    /// </summary>
    internal HaClient? TryCreateClient(out string? problem) => CreateClient(FillConnectionFirst, checkPermissions: false, out problem);

    private HaClient? CreateClient(string emptyMessage, bool checkPermissions, out string? problem)
    {
        var url = (Services.Secrets.Get("ha_url") ?? "").Trim();
        var token = (Services.Secrets.Get("ha_token") ?? "").Trim();
        if (url.Length == 0 || token.Length == 0)
        {
            problem = emptyMessage;
            return null;
        }

        if (!HaAddress.TryParse(url, out var address))
        {
            problem = "L'adresse de Home Assistant n'est pas valide : écris-la comme 192.168.1.10:8123 ou https://maison.exemple.fr.";
            return null;
        }

        if (address.IsLocalhost)
        {
            problem = "Une adresse localhost n'est pas prise en charge : indique l'adresse réseau de ton Home Assistant, par exemple 192.168.1.10:8123.";
            return null;
        }

        if (checkPermissions && MissingPermissionMessage(address) is { } permissionMessage)
        {
            problem = permissionMessage;
            return null;
        }

        var http = CreateHttpClient(address.CertificateKey);
        http.BaseAddress = address.Root;
        http.Timeout = TimeSpan.FromSeconds(8);
        problem = null;
        // Le jeton est relu dans le coffre à chaque requête, comme le veut HaClient.
        return new HaClient(http, () => Services.Secrets.Get("ha_token"));
    }

    /// <summary>Propose les capteurs du champ demandé. Ne lève jamais : un échec devient un résultat unique qui dit quoi faire.</summary>
    public override async Task<IReadOnlyList<LookupResult>> LookupAsync(string fieldKey, string query, CancellationToken ct)
    {
        try
        {
            using var client = TryCreateClient(out var problem);
            if (client is null)
            {
                return [Guidance(problem!)];
            }

            var states = await client.GetAllStatesAsync(ct).ConfigureAwait(false);
            return EntityCatalog.Search(states, EntityCatalog.KindFor(fieldKey), query);
        }
        catch (Exception ex) when (IsReportable(ex, ct))
        {
            return [Guidance(MessageOf(ex))];
        }
    }

    /// <summary>« test » vérifie la connexion ; « forget_cert » oublie l'empreinte du certificat épinglé.</summary>
    public override async Task<ActionResult> RunActionAsync(string actionKey, CancellationToken ct)
    {
        switch (actionKey)
        {
            case "test":
                try
                {
                    using var client = TryCreateClient(out var problem);
                    if (client is null)
                    {
                        return new ActionResult(false, problem!);
                    }

                    await client.PingAsync(ct).ConfigureAwait(false);
                    return new ActionResult(true, "Connexion réussie à Home Assistant.");
                }
                catch (Exception ex) when (IsReportable(ex, ct))
                {
                    return new ActionResult(false, MessageOf(ex));
                }

            case "forget_cert":
                Services.Secrets.Set("cert:ha", null);
                return new ActionResult(true, "Certificat oublié : Home Assistant sera reconnu de nouveau à la prochaine connexion.");

            default:
                return new ActionResult(false, "Cette action n'existe pas.");
        }
    }

    /// <summary>Un résultat sans valeur utile : un objet vide, que <see cref="SettingsReader.EntityId"/> lit comme « aucun capteur ».</summary>
    private static LookupResult Guidance(string message) => new(message, JsonSerializer.SerializeToElement(new { }));

    /// <summary>Tout échec est montré, sauf l'annulation par l'appelant et le manque de mémoire.</summary>
    private static bool IsReportable(Exception ex, CancellationToken ct) =>
        ex is not OutOfMemoryException && !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private static string MessageOf(Exception ex) =>
        ex is HaProblem
            ? ex.Message
            : "Impossible de lire Home Assistant. Vérifie l'adresse, la clé API et les permissions du module.";

    public override bool IsAnimating => _card?.IsAnimating ?? false;

    public override void Render(SKCanvas canvas, ModuleRenderContext ctx) => _card?.Render(canvas, ctx);

    /// <summary>Arrête la carte, qui attend la fin de sa lecture avant de libérer le client.</summary>
    public override async ValueTask DisposeAsync()
    {
        if (_card is { } card)
        {
            await card.DisposeAsync().ConfigureAwait(false);
        }
    }
}
