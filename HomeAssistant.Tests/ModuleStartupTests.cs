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
        Assert.Equal((2, 1), Size(root.GetProperty("minSize")));
        Assert.Equal((5, 3), Size(presets.GetProperty("compact")));
        Assert.Equal((6, 4), Size(presets.GetProperty("ideal")));
        Assert.Equal((8, 4), Size(presets.GetProperty("large")));
        Assert.Equal(["reseau-local", "internet"], root.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    private static JsonElement[] ManifestFields(JsonDocument manifest) =>
        manifest.RootElement.GetProperty("settings").EnumerateArray().ToArray();

    private static JsonElement Field(JsonElement[] fields, string key) =>
        Assert.Single(fields, f => f.GetProperty("key").GetString() == key);

    private static void AssertVisibleWhen(JsonElement field, string key, string value)
    {
        var rule = field.GetProperty("visibleWhen");
        Assert.Equal(key, rule.GetProperty("key").GetString());
        Assert.Equal(value, rule.GetProperty("value").GetString());
    }

    [Fact]
    public void Manifest_DailyFields_OnlyShowForDaily()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var fields = ManifestFields(manifest);

        var daily = fields.Where(f => f.GetProperty("key").GetString()!.StartsWith("day_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(10, daily.Length);
        foreach (var field in daily)
        {
            var key = field.GetProperty("key").GetString();
            if (key is "day_price" or "day_price_sensor")
            {
                continue;
            }

            AssertVisibleWhen(field, "display", "daily");
        }

        AssertVisibleWhen(Field(fields, "day_price"), "day_price_mode", "typed");
        AssertVisibleWhen(Field(fields, "day_price_sensor"), "day_price_mode", "sensor");
        Assert.Equal(12, Field(fields, "day_price").GetProperty("maxLength").GetInt32());
        Assert.Equal("0,13", Field(fields, "day_price").GetProperty("placeholder").GetString());
    }

    [Fact]
    public void Manifest_DailyFields_FollowDisplayInOrder()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var keys = ManifestFields(manifest).Select(f => f.GetProperty("key").GetString()!).ToList();

        var start = keys.IndexOf("display") + 1;
        Assert.Equal(
            ["day_shape", "day_values", "day_consumption", "day_production", "day_import", "day_export", "day_cost", "day_price_mode", "day_price", "day_price_sensor"],
            keys.GetRange(start, 10));
    }

    [Fact]
    public void Manifest_DisplayOffersDaily()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var display = Field(ManifestFields(manifest), "display");

        Assert.Equal(["energy", "daily"], display.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()!).ToArray());
        Assert.Equal("energy", display.GetProperty("default").GetString());
    }

    [Fact]
    public void Manifest_DayValues_IsAnOrderedListOfSix()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var field = Field(ManifestFields(manifest), "day_values");

        Assert.Equal("list", field.GetProperty("kind").GetString());
        var expected = new[] { "consumption", "production", "import", "export", "cost", "resale" };
        Assert.Equal(expected, field.GetProperty("default").EnumerateArray().Select(v => v.GetString()!).ToArray());
        Assert.Equal(expected, field.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()!).ToArray());
    }

    [Fact]
    public void Manifest_GridFields_FollowDisplay()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var fields = ManifestFields(manifest);

        AssertVisibleWhen(Field(fields, "grid"), "display", "energy");
        AssertVisibleWhen(Field(fields, "grid_inverted"), "display", "energy");
        AssertVisibleWhen(Field(fields, "grid_export"), "grid_mode", "two");
        Assert.DoesNotContain(fields, f => f.GetProperty("key").GetString() == "grid_import");
    }

    [Fact]
    public void Manifest_ShowsTheAddress_AndOnlyTheKeyIsSecret()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var fields = manifest.RootElement.GetProperty("settings").EnumerateArray().ToArray();

        var address = Assert.Single(fields, f => f.GetProperty("key").GetString() == "ha_address");
        Assert.Equal("text", address.GetProperty("kind").GetString());
        var secret = Assert.Single(fields, f => f.GetProperty("kind").GetString() == "secret");
        Assert.Equal("ha_token", secret.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Start_AddressInSettings_IsRememberedForLookups()
    {
        await using var host = Host(new FakeHomeAssistant(), null, "abc", """{ "ha_address": "192.168.1.20:8123" }""");

        await host.StartAsync();

        Assert.Equal("192.168.1.20:8123", host.Secrets.Get("ha_url"));
        Assert.DoesNotContain("adresse", host.StatusMessage ?? "");
    }

    [Fact]
    public async Task Start_AddressInSettings_ReplacesTheRememberedOne()
    {
        await using var host = Host(new FakeHomeAssistant(), "192.168.1.10:8123", "abc", """{ "ha_address": "192.168.1.20:8123" }""");

        await host.StartAsync();

        Assert.Equal("192.168.1.20:8123", host.Secrets.Get("ha_url"));
    }

    [Theory]
    [InlineData("localhost:8123")]
    [InlineData("ftp://maison.exemple.fr")]
    public async Task Start_UnusableAddress_IsNotRemembered(string address)
    {
        var settings = JsonSerializer.Serialize(new Dictionary<string, string> { ["ha_address"] = address });
        await using var host = Host(new FakeHomeAssistant(), "192.168.1.10:8123", "abc", settings);

        await host.StartAsync();

        Assert.Equal("192.168.1.10:8123", host.Secrets.Get("ha_url"));
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
    public async Task Start_DailyDisplay_CreatesDailyCard()
    {
        var ha = new FakeHomeAssistant();
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        await using var host = Host(ha, "192.168.1.10:8123", "jeton", """{ "display": "daily", "day_consumption": { "id": "sensor.conso" } }""");

        await host.StartAsync();

        Assert.IsType<DailyCard>(probe.Module!.Card);
    }

    [Fact]
    public async Task Start_DailyWithoutSensor_IsNotConfigured()
    {
        var ha = new FakeHomeAssistant();
        await using var host = Host(ha, "192.168.1.10:8123", "jeton", """{ "display": "daily" }""");

        await host.StartAsync();

        Assert.Equal(ModuleHealth.NotConfigured, host.Health);
        Assert.Equal("Choisis au moins un capteur pour le Bilan du jour.", host.StatusMessage);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Start_NoDisplaySetting_StaysEnergy()
    {
        var ha = new FakeHomeAssistant();
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        await using var host = Host(ha, "192.168.1.10:8123", "jeton",
            """{ "solar": { "id": "sensor.pv" }, "grid": { "id": "sensor.grid" } }""");

        await host.StartAsync();

        Assert.IsType<EnergyCard>(probe.Module!.Card);
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
        // On vérifie seulement que la permission a passé : sans capteur choisi, le module s'arrête à la configuration, sans requête.
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
