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

    /// <summary>Vérifie la configuration de la case ; ne lance aucune requête.</summary>
    public override Task StartAsync(CancellationToken ct)
    {
        var url = (Services.Secrets.Get("ha_url") ?? "").Trim();
        var token = (Services.Secrets.Get("ha_token") ?? "").Trim();
        if (url.Length == 0 || token.Length == 0)
        {
            SetStatus(ModuleHealth.NotConfigured, "Indique l'adresse de Home Assistant et ta clé API dans les réglages du module.");
            return Task.CompletedTask;
        }

        if (!HaAddress.TryParse(url, out var address))
        {
            SetStatus(ModuleHealth.NotConfigured, "L'adresse de Home Assistant n'est pas valide : écris-la comme 192.168.1.10:8123 ou https://maison.exemple.fr.");
            return Task.CompletedTask;
        }

        if (address.IsLocalhost)
        {
            SetStatus(ModuleHealth.NotConfigured, "Une adresse localhost n'est pas prise en charge : indique l'adresse réseau de ton Home Assistant, par exemple 192.168.1.10:8123.");
            return Task.CompletedTask;
        }

        var permissionMessage = MissingPermissionMessage(address);
        if (permissionMessage is not null)
        {
            SetStatus(ModuleHealth.NotConfigured, permissionMessage);
            return Task.CompletedTask;
        }

        if (SettingsReader.EntityId(Services.Settings, "solar").Length == 0)
        {
            SetStatus(ModuleHealth.NotConfigured, "Choisis ton capteur de production dans les réglages du module.");
        }

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

    /// <summary>Rien à dessiner tant que les cartes ne sont pas branchées : le logiciel affiche lui-même le message d'état.</summary>
    public override void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
    }
}
