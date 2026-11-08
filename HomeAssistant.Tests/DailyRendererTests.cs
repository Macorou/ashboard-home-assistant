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

    private static int InkIn(SKBitmap bitmap, int xFrom, int xTo, int yFrom, int yTo)
    {
        var count = 0;
        for (var y = Math.Max(0, yFrom); y < Math.Min(bitmap.Height, yTo); y++)
            for (var x = Math.Max(0, xFrom); x < Math.Min(bitmap.Width, xTo); x++)
                if (bitmap.GetPixel(x, y).Alpha > 0) count++;
        return count;
    }

    [Theory]
    [InlineData("classique")]
    [InlineData("liquid-glass")]
    [InlineData("pixel")]
    public async Task Render_HugeNumbers_Strip_AreReducedToTheirColumn(string theme)
    {
        var (host, probe) = await StartAsync(Sensors("123456.7"), SettingsFor("strip", 6));
        await using var _ = host;
        await WaitUntilAsync(() => ((DailyCard)probe.Module!.Card!).Current is not null);

        using var bitmap = host.Render(new GridSize(4, 1), theme);
        // La forme (464 × 100) est mise à l'échelle et centrée dans le bitmap, dont la taille dépend du thème.
        var scale = Math.Min(bitmap.Width / 464f, bitmap.Height / 100f);
        var ox = (bitmap.Width - 464 * scale) / 2;
        var oy = (bitmap.Height - 100 * scale) / 2;
        Assert.True(Ink(bitmap, 0, bitmap.Width) > 100);
        Assert.Equal(0, Ink(bitmap, 0, 2));
        Assert.Equal(0, Ink(bitmap, bitmap.Width - 2, bitmap.Width));

        // Le séparateur (trait d'un pixel à x) touche les pixels x - 1 et x : on regarde les deux colonnes de part et d'autre.
        for (var i = 1; i <= 5; i++)
        {
            var x = (int)Math.Round(ox + (4 + i * (456 / 6f)) * scale);
            Assert.True(InkIn(bitmap, x - 1, x + 1, (int)(oy + 40 * scale), (int)(oy + 70 * scale)) > 0, $"{theme} : séparateur {i} introuvable");
            Assert.Equal(0, InkIn(bitmap, x - 3, x - 1, (int)(oy + 40 * scale), (int)(oy + 70 * scale)));
            Assert.Equal(0, InkIn(bitmap, x + 1, x + 3, (int)(oy + 40 * scale), (int)(oy + 70 * scale)));
        }
    }

    [Theory]
    [InlineData("classique")]
    [InlineData("liquid-glass")]
    [InlineData("pixel")]
    public async Task Render_HugeNumbers_List_AreReducedToTheirRow(string theme)
    {
        var (host, probe) = await StartAsync(Sensors("98765432123.7"), SettingsFor("list", 6));
        await using var _ = host;
        await WaitUntilAsync(() => ((DailyCard)probe.Module!.Card!).Current is not null);

        using var bitmap = host.Render(new GridSize(2, 3), theme);
        var scale = Math.Min(bitmap.Width / 226f, bitmap.Height / 322f);
        var ox = (bitmap.Width - 226 * scale) / 2;
        var oy = (bitmap.Height - 322 * scale) / 2;
        Assert.True(Ink(bitmap, 0, bitmap.Width) > 100);
        Assert.Equal(0, Ink(bitmap, 0, (int)(ox + 13 * scale)));
        Assert.Equal(0, Ink(bitmap, (int)Math.Ceiling(ox + 213 * scale), bitmap.Width));

        // Pour chaque ligne, le bord droit du libellé se mesure sur un rendu à petits nombres (qui restent à droite, après x = 150).
        // Le nombre garde 8 px de marge devant lui : sans réduction il déborde jusque dans la zone qui suit le libellé.
        var (smallHost, smallProbe) = await StartAsync(Sensors("1"), SettingsFor("list", 6));
        await using var __ = smallHost;
        await WaitUntilAsync(() => ((DailyCard)smallProbe.Module!.Card!).Current is not null);
        using var small = smallHost.Render(new GridSize(2, 3), theme);

        var rowHeight = (322 - 12) / 6f;
        for (var row = 0; row < 6; row++)
        {
            var top = (int)(oy + (6 + rowHeight * row) * scale) + 3;
            var bottom = (int)(oy + (6 + rowHeight * (row + 1)) * scale) - 3;
            var labelEnd = 0;
            for (var x = 0; x < ox + 150 * scale; x++)
                if (InkIn(small, x, x + 1, top, bottom) > 0) labelEnd = x;
            Assert.True(labelEnd > ox + 39 * scale, $"{theme} ligne {row} : libellé introuvable");
            Assert.Equal(0, InkIn(bitmap, labelEnd + 1, labelEnd + 5, top, bottom));
        }
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
