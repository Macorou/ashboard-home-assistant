using System.Text.Json;
using AshboardVision.Sdk.Testing;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

/// <summary>
/// Écrit la galerie d'images du contrôle visuel dans <c>gallery/</c> (ignoré par git) : chaque image est posée sur le fond du thème, car le canevas
/// du module est transparent. Les noms trient par disposition : <c>top_modern_bat_6x4_classique.png</c>.
/// </summary>
public sealed class GalleryTests
{
    private static readonly string[] Themes = ["classique", "liquid-glass", "pixel"];
    private static readonly (int Columns, int Rows)[] Sizes = [(5, 3), (6, 4), (8, 4)];
    private static readonly string[] Layouts = ["top", "left", "bottom"];
    private static readonly string[] Houses = ["modern", "classic"];

    /// <summary>Les trois scénarios de la maquette (soleil, nuages, nuit), sans et avec batterie. Réseau : positif à l'achat ; batterie : positive à la décharge.</summary>
    private sealed record Scenario(string Name, double Solar, double Grid, double BatteryPlain, double GridWithBattery, double Battery, double Soc);

    private static readonly Scenario[] Scenarios =
    [
        new("sun", 3420, -1560, 0, -660, -900, 64),
        new("clouds", 980, 1260, 0, 440, 820, 41),
        new("night", 0, 640, 0, 0, 640, 78),
    ];

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "manifest.json");

    private static string GalleryDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HomeAssistant.slnx")))
            {
                var gallery = Path.Combine(dir.FullName, "gallery");
                Directory.CreateDirectory(gallery);
                return gallery;
            }
        }

        throw new DirectoryNotFoundException("HomeAssistant.slnx est introuvable au-dessus de " + AppContext.BaseDirectory);
    }

    private static void Set(FakeHomeAssistant ha, string id, double value, string unit = "W") =>
        ha.Responses["/api/states/" + id] = (200, JsonSerializer.Serialize(new
        {
            entity_id = id,
            state = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            attributes = new { unit_of_measurement = unit },
        }));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "La carte n'a pas reçu de lecture en cinq secondes.");
    }

    private static string SettingsJson(string layout, string house, bool battery) =>
        $$"""
        { "layout": "{{layout}}", "house": "{{house}}", "solar": { "id": "sensor.pv" }, "grid": { "id": "sensor.grid" }
          {{(battery ? """, "battery_mode": "one", "battery": { "id": "sensor.bat" }, "battery_soc": { "id": "sensor.soc" }""" : "")}} }
        """;

    /// <summary>Démarre une carte sur les valeurs du scénario, la fait tourner deux secondes (le reflet et les filaments sont alors en vue) et rend chaque taille et chaque thème demandés.</summary>
    private static async Task<List<string>> WriteSetAsync(string directory, string layout, string house, bool battery, Scenario scenario,
        IEnumerable<(int Columns, int Rows)> sizes, IEnumerable<string> themes, string suffix)
    {
        var ha = new FakeHomeAssistant();
        Set(ha, "sensor.pv", scenario.Solar);
        Set(ha, "sensor.grid", battery ? scenario.GridWithBattery : scenario.Grid);
        if (battery)
        {
            Set(ha, "sensor.bat", scenario.Battery);
            Set(ha, "sensor.soc", scenario.Soc, "%");
        }

        TestableModule.Handler.Value = ha;
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        await using var host = new ModuleTestHost<TestableModule>(ManifestPath, JsonDocument.Parse(SettingsJson(layout, house, battery)).RootElement);
        host.Secrets.Set("ha_url", "192.168.1.10:8123");
        host.Secrets.Set("ha_token", "abc");
        await host.StartAsync();
        var card = (EnergyCard)probe.Module!.Card!;
        await WaitUntilAsync(() => card.Current is not null);

        var sizeList = sizes.ToList();
        var themeList = themes.ToList();
        for (var i = 0; i < 4; i++)
        {
            host.Time.Advance(TimeSpan.FromMilliseconds(500));
            using var _ = host.Render(new GridSize(sizeList[0].Columns, sizeList[0].Rows), themeList[0]);
        }

        var written = new List<string>();
        foreach (var (columns, rows) in sizeList)
        {
            foreach (var theme in themeList)
            {
                using var module = host.Render(new GridSize(columns, rows), theme);
                Assert.True(HasDrawing(module), $"{layout} {house} {battery} {columns}x{rows} {theme} : rien n'est dessiné");

                var file = Path.Combine(directory, $"{layout}_{house}_{(battery ? "bat" : "nobat")}_{columns}x{rows}_{theme}{suffix}.png");
                SaveOnPanel(module, probe.Module!.LastTheme!, file);
                written.Add(file);
            }
        }

        return written;
    }

    private static bool HasDrawing(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y += 2)
            for (var x = 0; x < bitmap.Width; x += 2)
                if (bitmap.GetPixel(x, y).Alpha > 0) return true;
        return false;
    }

    /// <summary>Pose l'image du module sur le fond du thème, puis sur son panneau (coins arrondis, opacité du thème).</summary>
    private static void SaveOnPanel(SKBitmap module, Theme theme, string file)
    {
        using var surface = SKSurface.Create(new SKImageInfo(module.Width, module.Height));
        var canvas = surface.Canvas;
        canvas.Clear(theme.Background);
        using var panel = new SKPaint { IsAntialias = true, Color = theme.Panel.WithAlpha((byte)Math.Clamp((int)(theme.PanelOpacity * 255), 0, 255)) };
        canvas.DrawRoundRect(new SKRect(0, 0, module.Width, module.Height), theme.CornerRadius, theme.CornerRadius, panel);
        canvas.DrawBitmap(module, 0, 0);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(file, data.ToArray());
    }

    /// <summary>Les valeurs de la maquette, six affichées : conso, prod., import, export, coût, revente (0,13 €/kWh).</summary>
    private static async Task<List<string>> WriteDailyAsync(string directory, string shape, IEnumerable<(int Columns, int Rows)> sizes)
    {
        var ha = new FakeHomeAssistant();
        void Sensor(string id, string value, string unit) =>
            ha.Responses["/api/states/" + id] = (200, JsonSerializer.Serialize(new { entity_id = id, state = value, attributes = new { unit_of_measurement = unit } }));
        Sensor("sensor.conso", "24.8", "kWh");
        Sensor("sensor.prod", "7.8", "kWh");
        Sensor("sensor.imp", "18.6", "kWh");
        Sensor("sensor.exp", "1.6", "kWh");
        Sensor("sensor.cout", "2.91", "EUR");

        TestableModule.Handler.Value = ha;
        var probe = new TestableModule.Holder();
        TestableModule.Probe.Value = probe;
        var settings = $$"""
            {"display":"daily","day_shape":"{{shape}}","day_values":["consumption","production","import","export","cost","resale"],
             "day_consumption":{"id":"sensor.conso"},"day_production":{"id":"sensor.prod"},"day_import":{"id":"sensor.imp"},
             "day_export":{"id":"sensor.exp"},"day_cost":{"id":"sensor.cout"},"day_price_mode":"typed","day_price":"0,13"}
            """;
        await using var host = new ModuleTestHost<TestableModule>(ManifestPath, JsonDocument.Parse(settings).RootElement);
        host.Secrets.Set("ha_url", "192.168.1.10:8123");
        host.Secrets.Set("ha_token", "abc");
        await host.StartAsync();
        var card = (DailyCard)probe.Module!.Card!;
        await WaitUntilAsync(() => card.Current is not null);

        var written = new List<string>();
        foreach (var (columns, rows) in sizes)
        {
            foreach (var theme in Themes)
            {
                using var module = host.Render(new GridSize(columns, rows), theme);
                Assert.True(HasDrawing(module), $"daily {shape} {columns}x{rows} {theme} : rien n'est dessiné");

                var file = Path.Combine(directory, $"daily_{shape}_{columns}x{rows}_{theme}.png");
                SaveOnPanel(module, probe.Module!.LastTheme!, file);
                written.Add(file);
            }
        }

        return written;
    }

    [Fact]
    public async Task WriteAll()
    {
        var directory = GalleryDirectory();
        foreach (var old in Directory.EnumerateFiles(directory, "*.png")) File.Delete(old);

        var written = new List<string>();
        foreach (var layout in Layouts)
        {
            foreach (var house in Houses)
            {
                foreach (var battery in new[] { false, true })
                {
                    // Soleil partout.
                    written.AddRange(await WriteSetAsync(directory, layout, house, battery, Scenarios[0], Sizes, Themes, ""));
                    // Nuages et nuit : seulement en 6 x 4, thème classique.
                    foreach (var scenario in Scenarios.Skip(1))
                    {
                        written.AddRange(await WriteSetAsync(directory, layout, house, battery, scenario, [(6, 4)], ["classique"], "_" + scenario.Name));
                    }
                }
            }
        }

        written.AddRange(await WriteDailyAsync(directory, "strip", [(4, 1), (6, 1), (4, 2)]));
        written.AddRange(await WriteDailyAsync(directory, "list", [(2, 3), (3, 4)]));

        // 3 dispositions x 2 maisons x 2 (batterie) x (9 soleil + 2 scénarios), puis 5 tailles du Bilan du jour x 3 thèmes.
        Assert.Equal(3 * 2 * 2 * (3 * 3 + 2) + 5 * 3, written.Count);
        Assert.All(written, file => Assert.True(new FileInfo(file).Length > 2000, file));
        foreach (var theme in Themes)
        {
            Assert.Contains(written, file => file.EndsWith($"_{theme}.png", StringComparison.Ordinal));
        }
    }
}
