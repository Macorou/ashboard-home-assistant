using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

public sealed class EnergyRendererTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private static Theme TestTheme() => new("test", new SKColor(6, 8, 14), new SKColor(16, 20, 32), 1f, 12f,
        SKColors.White, new SKColor(138, 147, 166), SKColors.CornflowerBlue, SKColors.Orange, SKTypeface.Default);

    private static EnergyConfig Config(LayoutKind layout = LayoutKind.Top, bool battery = false, HouseStyle house = HouseStyle.Modern) =>
        new(layout, house, "sensor.pv", false, "sensor.grid", false, "", "", "", battery ? BatteryMode.One : BatteryMode.None,
            battery ? "sensor.bat" : "", false, "", "", battery ? "sensor.soc" : "");

    private static EnergyState State(double solar = 0, double import = 0, double export = 0, double charge = 0, double discharge = 0,
        double? soc = null, bool powerKnown = true, bool stale = false)
    {
        var input = new EnergyInput(solar, import, export, charge, discharge, null);
        return new EnergyState(new EnergySnapshot(input, soc, powerKnown, null), EnergyFlows.Compute(input), stale);
    }

    private static ModuleRenderContext Context(float width, float height, DateTimeOffset now, float textScale = 1f) =>
        new(new SKRect(0, 0, width, height), new GridSize(6, 4), TestTheme(), textScale, now);

    private static SKBitmap Draw(EnergyRenderer renderer, EnergyState? state, float width = 700, float height = 434, DateTimeOffset? now = null, float textScale = 1f)
    {
        var bitmap = new SKBitmap((int)width, (int)height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Render(canvas, Context(width, height, now ?? Start, textScale), state);
        return bitmap;
    }

    private static int InkCount(SKBitmap bitmap, int minAlpha = 1)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha >= minAlpha) count++;
        return count;
    }

    // ----- Formats -----

    [Theory]
    [InlineData(640, "640 W")]
    [InlineData(999.6, "1,00 kW")]
    [InlineData(3420, "3,42 kW")]
    [InlineData(123456, "123,46 kW")]
    [InlineData(0, "0 W")]
    public void FormatPower_RoundsToWattBeforeChoosingUnit(double watts, string expected) =>
        Assert.Equal(expected, EnergyRenderer.FormatPower(watts));

    [Fact]
    public void FitSize_ShrinksLongLabel_AtMinSizeAndLargeText()
    {
        // 5 x 3 : l'échelle de la carte vaut 0,744 ; texte grand : x 1,2. Emplacement centré à 105 (disposition du bas, avec batterie).
        const string Label = "Consommation · 100 % solaire";
        float Measure(string text, float size) => text.Length * size * .55f;
        var wanted = 14f * .744f * 1.2f;
        var maxWidth = (2 * 105f - 8f) * .744f;

        var fitted = EnergyRenderer.FitSize(Label, wanted, maxWidth, Measure);

        Assert.True(Measure(Label, wanted) > maxWidth, "le cas doit déborder sans réduction");
        Assert.True(fitted < wanted);
        Assert.True(Measure(Label, fitted) <= maxWidth + .01f);
        Assert.Equal(wanted, EnergyRenderer.FitSize("Production", wanted, maxWidth, Measure));
    }

    // ----- Dispositions -----

    [Fact]
    public void Layouts_Top_NoBattery_MatchesMockup()
    {
        var layout = EnergyLayouts.Get(LayoutKind.Top, false);

        Assert.Equal([ArtKind.Panels, ArtKind.HouseModern, ArtKind.Pylon], layout.Arts.Select(a => a.Kind));
        Assert.Equal(new ArtPlacement(ArtKind.Panels, 265, 0, .85f), layout.Arts[0]);
        Assert.Equal(new ArtPlacement(ArtKind.HouseModern, 55, 205, .9f), layout.Arts[1]);
        Assert.Equal(new ArtPlacement(ArtKind.Pylon, 490, 225, .9f), layout.Arts[2]);

        var s2h = layout.Flows.Single(f => f.Key == FlowKey.SolarToHome);
        Assert.Equal(SKColor.Parse("#ffc23d"), s2h.Color);
        Assert.Equal([new SKPoint(350, 76), new SKPoint(300, 130), new SKPoint(240, 210), new SKPoint(181, 295)], s2h.Bezier);
        Assert.Equal(3, layout.Flows.Count);
        Assert.Equal(new TextSlot("share", 350, 272, true, 14), layout.Texts.Single(t => t.Role == "share"));
        Assert.Equal(new HaloSpot("solar", 350, 70, 230), layout.Halos[0]);
    }

    [Theory]
    [InlineData(nameof(LayoutKind.Top))]
    [InlineData(nameof(LayoutKind.Left))]
    [InlineData(nameof(LayoutKind.Bottom))]
    public void Layouts_WithBattery_HasSixFlows(string name)
    {
        var layout = EnergyLayouts.Get(Enum.Parse<LayoutKind>(name), true);

        Assert.Equal(6, layout.Flows.Count);
        Assert.Equal(Enum.GetValues<FlowKey>().OrderBy(k => k), layout.Flows.Select(f => f.Key).OrderBy(k => k));
        Assert.Contains(layout.Arts, a => a.Kind == ArtKind.Battery);
        Assert.Contains(layout.Texts, t => t.Role == "batValue");
    }

    [Theory]
    [InlineData(nameof(LayoutKind.Top))]
    [InlineData(nameof(LayoutKind.Left))]
    [InlineData(nameof(LayoutKind.Bottom))]
    public void Layouts_NoBattery_HasNoBatteryArtOrSlot(string name)
    {
        var layout = EnergyLayouts.Get(Enum.Parse<LayoutKind>(name), false);

        Assert.DoesNotContain(layout.Arts, a => a.Kind == ArtKind.Battery);
        Assert.DoesNotContain(layout.Texts, t => t.Role.StartsWith("bat", StringComparison.Ordinal));
        Assert.DoesNotContain(layout.Halos, h => h.Role == "bat");
        Assert.DoesNotContain(layout.Flows, f => f.Key is FlowKey.SolarToBattery or FlowKey.BatteryToHome or FlowKey.GridToBattery);
    }

    // ----- Filaments -----

    private static SKBitmap DrawFilaments(FilamentPainter painter, EnergyFlows flows)
    {
        var bitmap = new SKBitmap(700, 434);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        painter.Draw(canvas, EnergyLayouts.Get(LayoutKind.Top, true), flows);
        return bitmap;
    }

    [Fact]
    public void Filament_ZeroFlow_DrawsNothing()
    {
        using var painter = new FilamentPainter();
        painter.Advance(1, EnergyFlows.Compute(default));

        using var bitmap = DrawFilaments(painter, EnergyFlows.Compute(default));

        // Seules subsistent les lignes de fond, à 6 % d'opacité : aucun point de filament (qui serait bien plus opaque).
        Assert.Equal(0, InkCount(bitmap, 40));
        Assert.True(InkCount(bitmap) > 0);
        Assert.Equal(0, painter.PhaseOf(FlowKey.SolarToHome));
    }

    [Fact]
    public void Filament_ActiveFlow_DrawsOpaqueDots()
    {
        using var painter = new FilamentPainter();
        var flows = EnergyFlows.Compute(new EnergyInput(3000, 0, 1000, 0, 0, null));
        painter.Advance(1, flows);

        using var bitmap = DrawFilaments(painter, flows);

        Assert.True(InkCount(bitmap, 200) > 20);
    }

    [Fact]
    public void Filament_SpeedFollowsPower()
    {
        using var painter = new FilamentPainter();
        var flows = EnergyFlows.Compute(new EnergyInput(2000, 0, 0, 0, 0, 2000));

        painter.Advance(1, flows);
        painter.Advance(.5, flows);

        Assert.Equal(1.5 * (55 + 2 * 75), painter.PhaseOf(FlowKey.SolarToHome), 6);
        Assert.Equal(0, painter.PhaseOf(FlowKey.SolarToGrid));
    }

    // ----- Rendu -----

    [Fact]
    public void Render_NoData_DrawsNothing()
    {
        using var renderer = new EnergyRenderer(Config());

        using var bitmap = Draw(renderer, null);

        Assert.Equal(0, InkCount(bitmap));
    }

    [Fact]
    public void Render_WithData_DrawsSomething()
    {
        using var renderer = new EnergyRenderer(Config(battery: true));

        using var bitmap = Draw(renderer, State(solar: 3420, export: 660, charge: 900, soc: 64));

        Assert.True(InkCount(bitmap) > 20000);
    }

    [Fact]
    public void Render_Stale_StopsAnimating()
    {
        using var renderer = new EnergyRenderer(Config());
        var fresh = State(solar: 3420, export: 1560);
        var stale = fresh with { Stale = true };

        Assert.True(renderer.IsAnimating(fresh));
        Assert.False(renderer.IsAnimating(stale));
        using var bitmap = Draw(renderer, stale);
        Assert.True(InkCount(bitmap) > 0);
    }

    [Fact]
    public void IsAnimating_FalseWhenNothingMoves()
    {
        using var renderer = new EnergyRenderer(Config(battery: true));

        Assert.False(renderer.IsAnimating(null));
        Assert.False(renderer.IsAnimating(State()));
        Assert.False(renderer.IsAnimating(State(soc: 50, charge: 5)));
        Assert.True(renderer.IsAnimating(State(solar: 500, export: 400)));
        Assert.True(renderer.IsAnimating(State(charge: 500, soc: 50)));
    }

    private static EnergyState HomeOnly(double watts)
    {
        var input = new EnergyInput(0, 0, 0, 0, 0, watts);
        return new EnergyState(new EnergySnapshot(input, null, true, null), EnergyFlows.Compute(input), false);
    }

    [Fact]
    public void Values_SnapToFirstState_ThenEaseTowardNewTarget()
    {
        using var renderer = new EnergyRenderer(Config());
        var now = Start;
        using (Draw(renderer, HomeOnly(300), now: now)) { }
        // Premier état : affiché tel quel, rien ne part de zéro.
        Assert.False(renderer.IsAnimating(HomeOnly(300)));

        // Nouvelle cible : les valeurs s'en approchent, image après image, puis se posent.
        var target = HomeOnly(900);
        Assert.True(renderer.IsAnimating(target));
        now = now.AddMilliseconds(100);
        using (Draw(renderer, target, now: now)) { }
        Assert.True(renderer.IsAnimating(target));
        for (var i = 0; i < 20; i++)
        {
            now = now.AddMilliseconds(500);
            using var _ = Draw(renderer, target, now: now);
        }

        Assert.False(renderer.IsAnimating(target));
    }

    [Fact]
    public void Render_IsUniformlyScaledAndCentered_At8x4()
    {
        var state = State(solar: 1124, export: 352);
        using var small = new EnergyRenderer(Config());
        using var wide = new EnergyRenderer(Config());
        using var tall = new EnergyRenderer(Config());
        using var reference = Draw(small, state, 700, 434);
        using var wider = Draw(wide, state, 940, 434);
        using var taller = Draw(tall, state, 700, 534);

        // 940 de large : 120 px de marge de chaque côté, échelle 1. 534 de haut : 50 px de marge en haut et en bas.
        Assert.Equal(0, MaxDifference(reference, wider, 120, 0));
        Assert.Equal(0, MaxDifference(reference, taller, 0, 50));
    }

    private static int MaxDifference(SKBitmap reference, SKBitmap larger, int offsetX, int offsetY)
    {
        var max = 0;
        // On compare le cœur : les halos, coupés par le bord de la case de référence, y sont identiques.
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                var a = reference.GetPixel(x, y);
                var b = larger.GetPixel(x + offsetX, y + offsetY);
                max = Math.Max(max, Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha))));
            }
        }

        return max;
    }

    [Fact]
    public void Render_ScalesWithBounds()
    {
        using var renderer = new EnergyRenderer(Config());
        using var bitmap = Draw(renderer, State(solar: 1000, export: 500), 350, 217);

        Assert.True(InkCount(bitmap) > 0);
    }

    [Fact]
    public void Render_BatteryPowerUnknown_ShowsDash()
    {
        using var known = new EnergyRenderer(Config(battery: true));
        using var unknown = new EnergyRenderer(Config(battery: true));

        using var a = Draw(known, State(discharge: 600, soc: 40, powerKnown: true));
        using var b = Draw(unknown, State(discharge: 600, soc: 40, powerKnown: false));

        Assert.NotEqual(Bytes(a), Bytes(b));
    }

    [Fact]
    public void Render_DisposeIsIdempotent()
    {
        var renderer = new EnergyRenderer(Config());
        using var _ = Draw(renderer, State(solar: 500));
        renderer.Dispose();
        renderer.Dispose();
    }

#if !DEBUG
    [Fact]
    public void Render_AverageCost()
    {
        // 6 x 4 avec batterie, soleil, charge : tout bouge. La moyenne vaut le coût d'une image, hors transfert vers l'écran.
        using var renderer = new EnergyRenderer(Config(battery: true));
        var state = State(solar: 3420, export: 660, charge: 900, soc: 64);
        using var bitmap = new SKBitmap(702, 434);
        using var canvas = new SKCanvas(bitmap);
        var now = Start;

        void Frame()
        {
            canvas.Clear(SKColors.Transparent);
            now = now.AddMilliseconds(83);
            renderer.Render(canvas, Context(702, 434, now), state);
        }

        for (var i = 0; i < 10; i++) Frame();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 300; i++) Frame();
        watch.Stop();

        var average = watch.Elapsed.TotalMilliseconds / 300;
        Console.WriteLine($"Coût moyen d'un rendu : {average:F2} ms");
        Assert.True(average < 15, $"{average:F2} ms par rendu");
    }
#endif

    private static byte[] Bytes(SKBitmap bitmap) => bitmap.Bytes;
}
