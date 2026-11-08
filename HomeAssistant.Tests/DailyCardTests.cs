using System.Text.Json;
using AshboardVision.Sdk.Testing;
using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

public sealed class DailyCardTests
{
    private const string Settings = """
        {"display":"daily","day_values":["consumption","import","cost"],
         "day_consumption":{"id":"sensor.conso"},"day_import":{"id":"sensor.imp"},"day_cost":{"id":"sensor.cout"}}
        """;

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "manifest.json");

    private static string StateJson(string id, string state, string unit) =>
        JsonSerializer.Serialize(new { entity_id = id, state, attributes = new { unit_of_measurement = unit } });

    private static FakeHomeAssistant Author()
    {
        var ha = new FakeHomeAssistant();
        Set(ha, "sensor.conso", "12.34", "kWh");
        Set(ha, "sensor.imp", "5000", "Wh");
        Set(ha, "sensor.cout", "2.5", "EUR");
        return ha;
    }

    private static void Set(FakeHomeAssistant ha, string id, string state, string unit) =>
        ha.Responses["/api/states/" + id] = (200, StateJson(id, state, unit));

    private static async Task<(ModuleTestHost<TestableModule> Host, TestableModule.Holder Probe)> StartAsync(FakeHomeAssistant ha, string settings = Settings)
    {
        TestableModule.Handler.Value = ha;
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        var host = new ModuleTestHost<TestableModule>(ManifestPath, JsonDocument.Parse(settings).RootElement);
        host.Secrets.Set("ha_url", "192.168.1.10:8123");
        host.Secrets.Set("ha_token", "abc");
        await host.StartAsync();
        return (host, probe);
    }

    private static DailyCard CardOf(TestableModule.Holder probe) => (DailyCard)probe.Module!.Card!;

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

    private static Task ReadsAsync(FakeHomeAssistant ha, int requests) => WaitUntilAsync(() => Count(ha) >= requests);

    [Fact]
    public async Task Poll_PublishesItems_InTheChosenOrder()
    {
        var (host, probe) = await StartAsync(Author());
        await using var _ = host;

        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        var state = CardOf(probe).Current!;
        Assert.Equal([DailyValue.Consumption, DailyValue.Import, DailyValue.Cost], state.Items.Select(i => i.Value).ToArray());
        Assert.Equal(["12,3", "5,0", "2,50"], state.Items.Select(i => i.Number).ToArray());
        Assert.Equal(["kWh", "kWh", "€"], state.Items.Select(i => i.Unit).ToArray());
        Assert.False(state.Stale);
        Assert.Equal(ModuleHealth.Online, host.Health);
    }

    [Fact]
    public async Task Poll_EveryThirtySeconds()
    {
        var ha = Author();
        var (host, _) = await StartAsync(ha);
        await using var _h = host;
        await ReadsAsync(ha, 3);

        host.Time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(100);
        Assert.Equal(3, Count(ha));

        host.Time.Advance(TimeSpan.FromSeconds(1));
        await ReadsAsync(ha, 6);
        Assert.Equal(6, Count(ha));
    }

    [Fact]
    public async Task UnavailableSensor_ShowsMissing_AndStaysOnline()
    {
        var ha = Author();
        Set(ha, "sensor.imp", "unavailable", "Wh");
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;

        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        var items = CardOf(probe).Current!.Items;
        Assert.Equal(DailyReader.Missing, items[1].Number);
        Assert.Equal("12,3", items[0].Number);
        Assert.Equal(ModuleHealth.Online, host.Health);
    }

    [Fact]
    public async Task SameSensorForTwoValues_IsReadOnce()
    {
        var ha = new FakeHomeAssistant();
        Set(ha, "sensor.a", "7", "kWh");
        var settings = """{"display":"daily","day_values":["consumption","import"],"day_consumption":{"id":"sensor.a"},"day_import":{"id":"sensor.a"}}""";
        var (host, probe) = await StartAsync(ha, settings);
        await using var _h = host;

        await WaitUntilAsync(() => CardOf(probe).Current is not null);
        Assert.Equal(1, Count(ha));
        Assert.Equal(2, CardOf(probe).Current!.Items.Count);

        host.Time.Advance(TimeSpan.FromSeconds(30));
        await ReadsAsync(ha, 2);
        await Task.Delay(100);
        Assert.Equal(2, Count(ha));
    }

    [Fact]
    public async Task Unauthorized_GoesOfflineImmediately()
    {
        var ha = Author();
        ha.Responses["/api/states/sensor.conso"] = (401, "{}");
        var (host, _) = await StartAsync(ha);
        await using var _h = host;

        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        Assert.Contains("clé API", host.StatusMessage);
    }

    [Fact]
    public async Task SensorNotFound_GoesOffline_AndKeepsItemsStale()
    {
        var ha = Author();
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;
        await WaitUntilAsync(() => CardOf(probe).Current is not null);
        var good = CardOf(probe).Current!;

        ha.Responses.Remove("/api/states/sensor.cout");
        host.Time.Advance(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => host.Health == ModuleHealth.Offline);

        Assert.Contains("sensor.cout", host.StatusMessage);
        var stale = CardOf(probe).Current!;
        Assert.True(stale.Stale);
        Assert.Equal(good.Items, stale.Items);
    }

    [Fact]
    public async Task ThreeFailures_GoOffline_ThenRecover()
    {
        var ha = Author();
        var (host, probe) = await StartAsync(ha);
        await using var _h = host;
        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        ha.Failure = new HttpRequestException("réseau coupé");
        var before = Count(ha);
        for (var failure = 1; failure <= 3; failure++)
        {
            host.Time.Advance(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(() => Count(ha) >= before + failure * 3);
            await Task.Delay(50);
            Assert.Equal(failure < 3 ? ModuleHealth.Online : ModuleHealth.Offline, host.Health);
        }

        Assert.True(CardOf(probe).Current!.Stale);

        ha.Failure = null;
        host.Time.Advance(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => host.Health == ModuleHealth.Online);
        Assert.False(CardOf(probe).Current!.Stale);
        Assert.Null(host.StatusMessage);
    }

    [Fact]
    public async Task IsNeverAnimating()
    {
        var (host, probe) = await StartAsync(Author());
        await using var _h = host;
        await WaitUntilAsync(() => CardOf(probe).Current is not null);

        Assert.False(CardOf(probe).IsAnimating);
    }
}
