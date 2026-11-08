using System.Text.Json;
using AshboardVision.Sdk.Testing;
using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

public sealed class EnergyCardTests
{
    private const string Settings = """{ "solar": { "id": "sensor.pv" }, "grid": { "id": "sensor.grid" } }""";

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "manifest.json");

    private static string StateJson(string id, string state, string unit) =>
        JsonSerializer.Serialize(new { entity_id = id, state, attributes = new { unit_of_measurement = unit } });

    /// <summary>Les valeurs de l'auteur : 1 124 W produits, 352,38 W vendus, donc 771,62 W à la maison.</summary>
    private static FakeHomeAssistant Author(string solar = "1124", string grid = "-352.38")
    {
        var ha = new FakeHomeAssistant();
        Set(ha, "sensor.pv", solar);
        Set(ha, "sensor.grid", grid);
        return ha;
    }

    private static void Set(FakeHomeAssistant ha, string id, string state, string unit = "W") =>
        ha.Responses["/api/states/" + id] = (200, StateJson(id, state, unit));

    private static async Task<(ModuleTestHost<TestableModule> Host, TestableModule.Holder Probe)> StartAsync(FakeHomeAssistant ha)
    {
        TestableModule.Handler.Value = ha;
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        var host = new ModuleTestHost<TestableModule>(ManifestPath, JsonDocument.Parse(Settings).RootElement);
        host.Secrets.Set("ha_url", "192.168.1.10:8123");
        host.Secrets.Set("ha_token", "abc");
        await host.StartAsync();
        return (host, probe);
    }

    private static EnergyCard CardOf(TestableModule.Holder probe) => (EnergyCard)probe.Module!.Card!;

    private static int Count(FakeHomeAssistant ha)
    {
        lock (ha.Requests)
        {
            return ha.Requests.Count;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "La condition attendue n'est pas arrivée en cinq secondes.");
    }

    /// <summary>Attend que le faux serveur ait reçu les requêtes de <paramref name="reads"/> lectures (deux capteurs chacune).</summary>
    private static Task ReadsAsync(FakeHomeAssistant ha, int reads) => WaitUntilAsync(() => Count(ha) >= reads * 2);

    [Fact]
    public async Task Poll_PublishesFlows_AndGoesOnline()
    {
        var ha = Author();
        var (host, probe) = await StartAsync(ha);
        await using var _ = host;

        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        var state = CardOf(probe).Current!;
        Assert.Equal(771.62, state.Flows.Home, 2);
        Assert.Equal(771.62, state.Flows.SolarToHome, 2);
        Assert.Equal(352.38, state.Flows.SolarToGrid, 2);
        Assert.False(state.Stale);
        Assert.Equal(ModuleHealth.Online, host.Health);
    }

    [Fact]
    public async Task Poll_EveryThreeSeconds()
    {
        var ha = Author();
        var (host, _) = await StartAsync(ha);
        await using var _h = host;
        await ReadsAsync(ha, 1);

        host.Time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        Assert.Equal(2, Count(ha));

        host.Time.Advance(TimeSpan.FromSeconds(1));
        await ReadsAsync(ha, 2);
        Assert.Equal(4, Count(ha));
    }

    [Fact]
    public async Task Unauthorized_GoesOfflineImmediately()
    {
        var ha = Author();
        ha.Responses["/api/states/sensor.pv"] = (401, "{}");
        var (host, _) = await StartAsync(ha);
        await using var _h = host;

        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        Assert.Contains("clé API", host.StatusMessage);
    }

    [Fact]
    public async Task MissingEntity_NamesIt()
    {
        var ha = Author();
        ha.Responses.Remove("/api/states/sensor.grid");
        var (host, _) = await StartAsync(ha);
        await using var _h = host;

        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        Assert.Contains("sensor.grid", host.StatusMessage);
    }

    [Fact]
    public async Task NetworkFailure_OfflineOnlyAfterThree_ThenRecovers()
    {
        var ha = Author();
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;
        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        ha.Failure = new HttpRequestException("réseau coupé");
        var before = Count(ha);
        for (var failure = 1; failure <= 3; failure++)
        {
            host.Time.Advance(TimeSpan.FromSeconds(3));
            await WaitUntilAsync(() => Count(ha) >= before + failure * 2);
            await Task.Delay(50);
            Assert.Equal(failure < 3 ? ModuleHealth.Online : ModuleHealth.Offline, host.Health);
        }

        Assert.Contains("injoignable", host.StatusMessage);

        ha.Failure = null;
        host.Time.Advance(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => host.Health == ModuleHealth.Online);
        Assert.False(CardOf(probe).Current!.Stale);
        Assert.Null(host.StatusMessage);
    }

    [Fact]
    public async Task Stale_KeepsLastValues()
    {
        var ha = Author();
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;
        await WaitUntilAsync(() => CardOf(probe).Current is not null);
        var good = CardOf(probe).Current!;

        ha.Responses["/api/states/sensor.pv"] = (401, "{}");
        host.Time.Advance(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        var stale = CardOf(probe).Current!;
        Assert.True(stale.Stale);
        Assert.Equal(good.Snapshot, stale.Snapshot);
        Assert.Equal(good.Flows, stale.Flows);
    }

    [Fact]
    public async Task GridUnavailable_GoesOffline_NamingEntity()
    {
        var ha = Author(grid: "unavailable");
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;

        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        Assert.Contains("sensor.grid", host.StatusMessage);
        Assert.Null(CardOf(probe).Current);
    }

    [Fact]
    public async Task GridUnavailable_AfterGoodRead_KeepsStaleValues_ThenRecovers()
    {
        var ha = Author();
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;
        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        Set(ha, "sensor.grid", "unavailable");
        host.Time.Advance(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        Assert.True(CardOf(probe).Current!.Stale);
        Assert.Equal(771.62, CardOf(probe).Current!.Flows.Home, 2);

        Set(ha, "sensor.grid", "-352.38");
        host.Time.Advance(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => host.Health == ModuleHealth.Online);
        Assert.False(CardOf(probe).Current!.Stale);
    }

    [Fact]
    public async Task SolarUnavailable_StaysOnline()
    {
        var ha = Author(solar: "unavailable");
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;

        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        Assert.Equal(ModuleHealth.Online, host.Health);
        Assert.Equal(0, CardOf(probe).Current!.Flows.SolarToHome);
    }

    [Fact]
    public async Task SlowServer_NeverOverlapsReads()
    {
        var ha = Author();
        var gate = new TaskCompletionSource();
        ha.Delay = async ct => await gate.Task.WaitAsync(ct);
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;
        await ReadsAsync(ha, 1);

        host.Time.Advance(TimeSpan.FromSeconds(9));
        await Task.Delay(100);
        Assert.Equal(2, Count(ha));

        gate.SetResult();
        await WaitUntilAsync(() => CardOf(probe).Current is not null);
        host.Time.Advance(TimeSpan.FromSeconds(3));
        await ReadsAsync(ha, 2);
        Assert.Equal(4, Count(ha));
    }

    [Fact]
    public async Task Dispose_DuringRead_DoesNotThrow()
    {
        var ha = Author();
        ha.Delay = ct => Task.Delay(Timeout.Infinite, ct);
        var (host, _) = await StartAsync(ha);
        await ReadsAsync(ha, 1);

        var closing = host.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(closing, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(closing, finished);
        await closing;
        host.Time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(2, Count(ha));
    }

    [Fact]
    public async Task TwoCases_KeepSeparateStates()
    {
        var first = Author(solar: "1000", grid: "-100");
        var second = Author(solar: "50", grid: "300");
        var (hostA, probeA) = await StartAsync(first);
        await using var _a = hostA;
        var (hostB, probeB) = await StartAsync(second);
        await using var _b = hostB;

        await WaitUntilAsync(() => CardOf(probeA).Current is not null && CardOf(probeB).Current is not null);

        Assert.Equal(900, CardOf(probeA).Current!.Flows.Home, 2);
        Assert.Equal(350, CardOf(probeB).Current!.Flows.Home, 2);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("W", false)]
    [InlineData("kW", false)]
    [InlineData(" KW ", false)]
    [InlineData("w", false)]
    [InlineData("MW", true)]
    [InlineData("A", true)]
    public void IsUnknownUnit_AcceptsOnlyWattsAndKilowatts(string? unit, bool unknown) =>
        Assert.Equal(unknown, EnergyCard.IsUnknownUnit(unit));
}
