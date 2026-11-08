using System.Text.Json;
using AshboardVision.Sdk;
using AshboardVision.Sdk.Testing;

namespace HomeAssistant.Tests;

public sealed class ModuleActionsTests
{
    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "manifest.json");

    /// <summary>Un hôte sans réglages (comme la fenêtre de réglage pour une recherche) ; <c>Probe</c> donne l'instance, créée au démarrage de l'hôte (les tests le démarrent, la sonde est posée avant).</summary>
    private static (ModuleTestHost<TestableModule> Host, TestableModule.Holder Probe) Host(FakeHomeAssistant ha, string? url = "192.168.1.10:8123", string? token = "abc", IReadOnlyList<ModulePermission>? granted = null)
    {
        TestableModule.Handler.Value = ha;
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        var host = new ModuleTestHost<TestableModule>(ManifestPath, default(JsonElement), granted);
        if (url is not null)
        {
            host.Secrets.Set("ha_url", url);
        }

        if (token is not null)
        {
            host.Secrets.Set("ha_token", token);
        }

        return (host, probe);
    }

    private const string StatesJson = """
        [
          {"entity_id":"sensor.pv","state":"1124","attributes":{"unit_of_measurement":"W","friendly_name":"Solaire"}},
          {"entity_id":"sensor.temp","state":"21","attributes":{"unit_of_measurement":"°C","friendly_name":"Salon"}}
        ]
        """;

    [Fact]
    public async Task Lookup_ReadsSecretsOnly()
    {
        var ha = new FakeHomeAssistant();
        ha.Responses["/api/states"] = (200, StatesJson);
        var (host, probe) = Host(ha);
        await using var _ = host;
        await host.StartAsync();

        var results = await probe.Module!.LookupAsync("solar", "sol", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Solaire · 1124 W (sensor.pv)", result.Label);
        Assert.Equal("Bearer abc", Assert.Single(ha.Requests).Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Lookup_NoSecrets_ReturnsGuidance()
    {
        var ha = new FakeHomeAssistant();
        var (host, probe) = Host(ha, null, null);
        await using var _ = host;
        await host.StartAsync();

        var results = await probe.Module!.LookupAsync("solar", "", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Remplis d'abord l'adresse et la clé API", result.Label);
        Assert.Equal(JsonValueKind.Null, result.Value.ValueKind);
        Assert.Equal("", SettingsReader.EntityId(JsonSerializer.SerializeToElement(new { solar = result.Value }), "solar"));
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Lookup_NetworkError_ReturnsOneResultWithoutThrowing()
    {
        var ha = new FakeHomeAssistant { Failure = new HttpRequestException("down") };
        var (host, probe) = Host(ha);
        await using var _ = host;
        await host.StartAsync();

        var results = await probe.Module!.LookupAsync("solar", "", CancellationToken.None);

        Assert.Contains("injoignable", Assert.Single(results).Label);
    }

    [Fact]
    public async Task Lookup_CancelledByCaller_Throws()
    {
        var ha = new FakeHomeAssistant { Delay = ct => Task.Delay(Timeout.Infinite, ct) };
        var (host, probe) = Host(ha);
        await using var _ = host;
        await host.StartAsync();
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.Module!.LookupAsync("solar", "", cts.Token));
    }

    [Fact]
    public async Task Action_Test_Ok()
    {
        var ha = new FakeHomeAssistant();
        ha.Responses["/api/"] = (200, """{"message":"API running."}""");
        var (host, probe) = Host(ha);
        await using var _ = host;
        await host.StartAsync();

        var result = await probe.Module!.RunActionAsync("test", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("Connexion réussie à Home Assistant.", result.Message);
    }

    [Fact]
    public async Task Action_Test_Unauthorized_SaysSo()
    {
        var ha = new FakeHomeAssistant();
        ha.Responses["/api/"] = (401, "{}");
        var (host, probe) = Host(ha);
        await using var _ = host;
        await host.StartAsync();

        var result = await probe.Module!.RunActionAsync("test", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("refuse ta clé API", result.Message);
    }

    [Fact]
    public async Task Action_Test_WithoutPermission_SaysWhichOneToTick()
    {
        var ha = new FakeHomeAssistant();
        var (host, probe) = Host(ha, granted: []);
        await using var _ = host;
        await host.StartAsync();

        var result = await probe.Module!.RunActionAsync("test", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("Réseau local", result.Message);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Lookup_WithoutPermission_SaysWhichOneToTick()
    {
        var ha = new FakeHomeAssistant();
        var (host, probe) = Host(ha, granted: []);
        await using var _ = host;
        await host.StartAsync();

        var results = await probe.Module!.LookupAsync("solar", "sol", CancellationToken.None);

        Assert.Contains("Réseau local", Assert.Single(results).Label);
        Assert.Empty(ha.Requests);
    }

    [Fact]
    public async Task Action_Test_WithoutAddress_AsksToFillIt()
    {
        var (host, probe) = Host(new FakeHomeAssistant(), null, "abc");
        await using var _ = host;
        await host.StartAsync();

        var result = await probe.Module!.RunActionAsync("test", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("adresse", result.Message);
    }

    [Fact]
    public async Task Action_ForgetCert_ClearsFingerprint()
    {
        var (host, probe) = Host(new FakeHomeAssistant());
        await using var _ = host;
        await host.StartAsync();
        host.Secrets.Set("cert:ha", "AB:CD");

        var result = await probe.Module!.RunActionAsync("forget_cert", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(host.Secrets.Get("cert:ha"));
    }

    [Fact]
    public async Task Action_ForgetCert_WithoutFingerprint_IsStillOk()
    {
        var (host, probe) = Host(new FakeHomeAssistant());
        await using var _ = host;
        await host.StartAsync();

        var result = await probe.Module!.RunActionAsync("forget_cert", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Contains("de nouveau", result.Message);
    }

    [Fact]
    public async Task Action_Unknown_IsNotOk()
    {
        var (host, probe) = Host(new FakeHomeAssistant());
        await using var _ = host;
        await host.StartAsync();

        Assert.False((await probe.Module!.RunActionAsync("nope", CancellationToken.None)).Ok);
    }
}
