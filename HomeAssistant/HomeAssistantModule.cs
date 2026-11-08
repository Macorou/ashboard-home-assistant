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

        // Provisoire : la Task 2 remplace ce contrôle par la normalisation complète de l'adresse.
        if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase) || url.Contains("127.0.0.1", StringComparison.Ordinal))
        {
            SetStatus(ModuleHealth.NotConfigured, "Une adresse localhost n'est pas prise en charge : indique l'adresse réseau de ton Home Assistant, par exemple 192.168.1.10:8123.");
            return Task.CompletedTask;
        }

        if (!Services.Has(ModulePermission.LocalNetwork) && !Services.Has(ModulePermission.Internet))
        {
            SetStatus(ModuleHealth.NotConfigured, "Accorde la permission « Réseau local » ou « Internet » à ce module dans la fenêtre de réglage d'Ashboard Vision.");
            return Task.CompletedTask;
        }

        if (SettingsReader.EntityId(Services.Settings, "solar").Length == 0)
        {
            SetStatus(ModuleHealth.NotConfigured, "Choisis ton capteur de production dans les réglages du module.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Rien à dessiner tant que les cartes ne sont pas branchées : le logiciel affiche lui-même le message d'état.</summary>
    public override void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
    }
}
