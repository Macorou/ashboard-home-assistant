using System.Text.Json;
using AshboardVision.Sdk;
using AshboardVision.Sdk.Testing;
using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

public sealed class ModuleStartupTests
{
    private const string SolarSetting = """{ "solar": { "id": "sensor.pv", "name": "PV" } }""";

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "manifest.json");

    private static ModuleTestHost<TestableModule> Host(FakeHomeAssistant ha, string? url, string? token, string settingsJson = "{}", IReadOnlyList<ModulePermission>? granted = null)
    {
        TestableModule.Handler.Value = ha;
        var settings = JsonDocument.Parse(settingsJson).RootElement;
        var host = new ModuleTestHost<TestableModule>(ManifestPath, settings, granted);
        if (url is not null)
        {
            host.Secrets.Set("ha_url", url);
        }

        if (token is not null)
        {
            host.Secrets.Set("ha_token", token);
        }

        return host;
    }

    private static (int Columns, int Rows) Size(JsonElement element) =>
        (element.GetProperty("columns").GetInt32(), element.GetProperty("rows").GetInt32());

    [Fact]
    public void Manifest_DeclaresPresetsAndPermissions()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = manifest.RootElement;
        var presets = root.GetProperty("presets");

        Assert.Equal("macorou.home-assistant", root.GetProperty("id").GetString());
        Assert.Equal((5, 3), Size(root.GetProperty("minSize")));
        Assert.Equal((5, 3), Size(presets.GetProperty("compact")));
        Assert.Equal((6, 4), Size(presets.GetProperty("ideal")));
        Assert.Equal((8, 4), Size(presets.GetProperty("large")));
        Assert.Equal(["reseau-local", "internet"], root.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    [Fact]
    public async Task Start_WithoutSecrets_IsNotConfiguredAndMakesNoRequest()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, null, null);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("adresse", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_WithSecretsButNoSensor_IsNotConfigured()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "192.168.1.10:8123", "jeton");

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("production", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_LocalhostAddress_ExplainsItIsNotSupported()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "http://localhost:8123", "jeton", SolarSetting);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("localhost", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_NoPermissionGranted_IsNotConfigured()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "192.168.1.10:8123", "jeton", SolarSetting, granted: []);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("permission", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_PrivateIpWithoutLocalNetwork_SaysToTickIt()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "192.168.1.10:8123", "jeton", SolarSetting, granted: [ModulePermission.Internet]);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("Réseau local", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_PublicIpWithoutInternet_SaysToTickIt()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "https://8.8.8.8", "jeton", SolarSetting, granted: [ModulePermission.LocalNetwork]);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("Internet", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_HostnameWithOnePermission_TriesToConnect()
    {
        // Aucune connexion n'existe avant la tâche 7 : on vérifie seulement que la permission a passé, le module s'arrête au capteur.
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "https://maison.duckdns.org", "jeton", granted: [ModulePermission.Internet]);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("production", host.StatusMessage);
    }

    [Fact]
    public async Task Start_HostnameWithoutPermission_SaysToTickOne()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "https://maison.duckdns.org", "jeton", SolarSetting, granted: []);

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Contains("Réseau local", host.StatusMessage);
        Assert.Contains("Internet", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }
}
