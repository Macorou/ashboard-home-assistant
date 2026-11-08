using System.Text.Json;
using AshboardVision.Sdk.Testing;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

public sealed class DailyRendererTests
{
    private static readonly string[] Themes = ["classique", "liquid-glass", "pixel"];

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "manifest.json");

    private static string SettingsFor(string shape, int values)
    {
        var keys = new[] { "consumption", "production", "import", "export", "cost", "resale" }.Take(values);
        return $$"""
            {"display":"daily","day_shape":"{{shape}}","day_values":[{{string.Join(",", keys.Select(k => $"\"{k}\""))}}],
             "day_consumption":{"id":"sensor.a"},"day_production":{"id":"sensor.b"},"day_import":{"id":"sensor.c"},
             "day_export":{"id":"sensor.d"},"day_cost":{"id":"sensor.e"},"day_price_mode":"typed","day_price":"0,2"}
            """;
    }

    private static FakeHomeAssistant Sensors(string value)
    {
        var ha = new FakeHomeAssistant();
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            ha.Responses["/api/states/sensor." + id] = (200, JsonSerializer.Serialize(new { entity_id = "sensor." + id, state = value, attributes = new { unit_of_measurement = "kWh" } }));
        }
        ha.Responses["/api/states/sensor.e"] = (200, JsonSerializer.Serialize(new { entity_id = "sensor.e", state = value, attributes = new { unit_of_measurement = "EUR" } }));
        return ha;
    }

    private static async Task<(ModuleTestHost<TestableModule> Host, TestableModule.Holder Probe)> StartAsync(FakeHomeAssistant ha, string settings)
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "La condition attendue n'est pas arrivée en cinq secondes.");
    }

    private static int Ink(SKBitmap bitmap, int xFrom, int xTo)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = xFrom; x < xTo; x++)
                if (bitmap.GetPixel(x, y).Alpha > 0) count++;
        return count;
    }

    [Theory]
    [InlineData(false, 3, 1, "Agrandis la case : 4 × 1 minimum")]
    [InlineData(false, 4, 1, null)]
    [InlineData(false, 6, 2, null)]
    [InlineData(true, 2, 2, "Agrandis la case : 2 × 3 minimum")]
    [InlineData(true, 1, 3, "Agrandis la case : 2 × 3 minimum")]
    [InlineData(true, 2, 3, null)]
    public void TooSmallMessage(bool list, int columns, int rows, string? expected) =>
        Assert.Equal(expected, DailyLayout.TooSmallMessage(list ? DailyShape.List : DailyShape.Strip, new GridSize(columns, rows)));

    [Theory]
    [InlineData(4, 3, "Agrandis la case : 5 × 3 minimum")]
    [InlineData(5, 2, "Agrandis la case : 5 × 3 minimum")]
    [InlineData(5, 3, null)]
    public void Energy_TooSmallMessage(int columns, int rows, string? expected) =>
        Assert.Equal(expected, EnergyRenderer.TooSmallMessage(new GridSize(columns, rows)));

    [Theory]
    [InlineData("strip", 4, 1)]
    [InlineData("list", 2, 3)]
    public async Task Render_Strip_And_List_DrawSomething(string shape, int columns, int rows)
    {
        var (host, probe) = await StartAsync(Sensors("12.34"), SettingsFor(shape, 5));
        await using var _ = host;
        await WaitUntilAsync(() => ((DailyCard)probe.Module!.Card!).Current is not null);

        foreach (var theme in Themes)
        {
            using var bitmap = host.Render(new GridSize(columns, rows), theme);
            Assert.True(Ink(bitmap, 0, bitmap.Width) > 100, $"{shape} {theme} : rien n'est dessiné");
        }
    }

    [Fact]
    public async Task Render_TooSmall_DrawsTheMessage_EvenWithoutData()
    {
        var ha = Sensors("1");
        ha.Delay = ct => Task.Delay(Timeout.Infinite, ct);
        var (host, probe) = await StartAsync(ha, SettingsFor("strip", 5));
        await using var _ = host;

        Assert.Null(((DailyCard)probe.Module!.Card!).Current);
        using var bitmap = host.Render(new GridSize(3, 1), "classique");
        Assert.True(Ink(bitmap, 0, bitmap.Width) > 100);
    }

    [Fact]
    public async Task Render_HugeNumbers_StayInsideTheCard()
    {
        var (host, probe) = await StartAsync(Sensors("123456.7"), SettingsFor("strip", 6));
        await using var _ = host;
        await WaitUntilAsync(() => ((DailyCard)probe.Module!.Card!).Current is not null);

        using var bitmap = host.Render(new GridSize(4, 1), "classique");
        Assert.True(Ink(bitmap, 0, bitmap.Width) > 100);
        Assert.Equal(0, Ink(bitmap, 0, 2));
        Assert.Equal(0, Ink(bitmap, bitmap.Width - 2, bitmap.Width));
    }

    [Fact]
    public async Task Energy_TooSmall_StopsAnimating()
    {
        var ha = new FakeHomeAssistant();
        ha.Responses["/api/states/sensor.pv"] = (200, """{"entity_id":"sensor.pv","state":"1124","attributes":{"unit_of_measurement":"W"}}""");
        ha.Responses["/api/states/sensor.grid"] = (200, """{"entity_id":"sensor.grid","state":"-352","attributes":{"unit_of_measurement":"W"}}""");
        var (host, probe) = await StartAsync(ha, """{ "solar": { "id": "sensor.pv" }, "grid": { "id": "sensor.grid" } }""");
        await using var _ = host;
        var card = (EnergyCard)probe.Module!.Card!;
        await WaitUntilAsync(() => card.Current is not null);

        using (host.Render(new GridSize(5, 3), "classique")) { }
        Assert.True(card.IsAnimating);
        using (host.Render(new GridSize(4, 2), "classique")) { }
        Assert.False(card.IsAnimating);
    }
}
