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
        Assert.Equal(Enum.GetValues<FlowKey>().Where(k => k != FlowKey.HomeToCar).OrderBy(k => k), layout.Flows.Select(f => f.Key).OrderBy(k => k));
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

    public static TheoryData<string, bool> LayoutCombos => new()
    {
        { nameof(LayoutKind.Top), false }, { nameof(LayoutKind.Top), true },
        { nameof(LayoutKind.Left), false }, { nameof(LayoutKind.Left), true },
        { nameof(LayoutKind.Bottom), false }, { nameof(LayoutKind.Bottom), true },
    };

    private static bool Inside(float x, float y) => x >= 0 && x <= EnergyLayouts.Width && y >= 0 && y <= EnergyLayouts.Height;

    [Theory]
    [MemberData(nameof(LayoutCombos))]
    public void Layouts_WithCar_AddCarArt_Flow_AndSlots(string layout, bool battery)
    {
        var result = EnergyLayouts.Get(Enum.Parse<LayoutKind>(layout), battery, true);

        var car = Assert.Single(result.Arts, a => a.Kind == ArtKind.CarBlue);
        var width = 106 * car.Scale;
        var height = 86 * car.Scale;
        Assert.True(Inside(car.X, car.Y) && Inside(car.X + width, car.Y + height));

        var house = Assert.Single(result.Arts, a => a.Kind == ArtKind.HouseModern);
        var flow = Assert.Single(result.Flows, f => f.Key == FlowKey.HomeToCar);
        Assert.Equal(SKColor.Parse("#ff7eb6"), flow.Color);
        var start = flow.Bezier[0];
        Assert.InRange(start.X, house.X - 40, house.X + 200 * house.Scale + 40);
        Assert.InRange(start.Y, house.Y - 40, house.Y + 180 * house.Scale + 40);
        Assert.All(flow.Bezier, p => Assert.True(Inside(p.X, p.Y)));

        Assert.Single(result.Texts, t => t.Role == "carLabel");
        Assert.Single(result.Texts, t => t.Role == "carValue");
        Assert.All(result.Texts, t => Assert.True(Inside(t.X, t.Y)));
    }

    [Theory]
    [MemberData(nameof(LayoutCombos))]
    public void Layouts_WithoutCar_AreUnchanged(string layout, bool battery)
    {
        var kind = Enum.Parse<LayoutKind>(layout);
        var old = EnergyLayouts.Get(kind, battery);
        var without = EnergyLayouts.Get(kind, battery, false);

        Assert.Equal(old.Arts, without.Arts);
        Assert.Equal(old.Texts, without.Texts);
        Assert.Equal(old.Flows.Select(f => (f.Key, f.Color)), without.Flows.Select(f => (f.Key, f.Color)));
        Assert.Equal(old.Flows.SelectMany(f => f.Bezier), without.Flows.SelectMany(f => f.Bezier));
    }

    // ----- Filaments -----

    [Fact]
    public void Filament_HomeToCar_HasItsOwnChannel()
    {
        using var painter = new FilamentPainter();
        var flows = new EnergyFlows(0, 0, 0, 0, 0, 0, 500, 0, 0, HomeToCar: 7400);

        painter.Advance(1, flows);

        Assert.Equal(FilamentPainter.SpeedFor(7400), painter.PhaseOf(FlowKey.HomeToCar), 6);
        Assert.Equal(FilamentPainter.SpeedFor(500), painter.PhaseOf(FlowKey.GridToBattery, true), 6);
    }

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

    // ----- Voiture -----

    private static readonly SKColor CarPink = SKColor.Parse("#ff7eb6");

    private static EnergyConfig CarConfig(LayoutKind layout = LayoutKind.Left, bool battery = false) =>
        Config(layout, battery) with { HasCar = true, CarPower = "sensor.car", CarPlug = "binary_sensor.plug", CarSoc = "sensor.car_soc" };

    private static EnergyState CarState(CarAspect aspect, double? watts = null, double? soc = 86, double solar = 0, double import = 0, bool stale = false)
    {
        var power = watts ?? (aspect == CarAspect.Charging ? 7400 : 0);
        var input = new EnergyInput(solar, import, 0, 0, 0, null, power);
        var reading = new CarReading(power, aspect != CarAspect.Unplugged, soc, aspect);
        return new EnergyState(new EnergySnapshot(input, null, true, null, reading), EnergyFlows.Compute(input), stale);
    }

    private static SKRect CarBox(LayoutKind layout = LayoutKind.Left, bool battery = false)
    {
        var car = Assert.Single(EnergyLayouts.Get(layout, battery, true).Arts, a => a.Kind == ArtKind.CarBlue);
        return new SKRect(car.X, car.Y, car.X + 106 * car.Scale, car.Y + 86 * car.Scale);
    }

    private static long AlphaSum(SKBitmap bitmap, SKRect box)
    {
        long sum = 0;
        for (var y = (int)Math.Max(0, box.Top); y < Math.Min(bitmap.Height, box.Bottom); y++)
            for (var x = (int)Math.Max(0, box.Left); x < Math.Min(bitmap.Width, box.Right); x++)
                sum += bitmap.GetPixel(x, y).Alpha;
        return sum;
    }

    private static bool IsPink(SKColor c) =>
        c.Alpha > 200 && Math.Abs(c.Red - CarPink.Red) < 40 && Math.Abs(c.Green - CarPink.Green) < 40 && Math.Abs(c.Blue - CarPink.Blue) < 40;

    private static int PinkCount(SKBitmap bitmap, SKRect zone)
    {
        var count = 0;
        for (var y = (int)Math.Max(0, zone.Top); y < Math.Min(bitmap.Height, zone.Bottom); y++)
            for (var x = (int)Math.Max(0, zone.Left); x < Math.Min(bitmap.Width, zone.Right); x++)
                if (IsPink(bitmap.GetPixel(x, y))) count++;
        return count;
    }

    private static SKRect CarValueZone(LayoutKind layout = LayoutKind.Left, bool battery = false)
    {
        var slot = Assert.Single(EnergyLayouts.Get(layout, battery, true).Texts, t => t.Role == "carValue");
        return new SKRect(slot.X, slot.Y - slot.Size, EnergyLayouts.Width, slot.Y + slot.Size * .3f);
    }

    [Theory]
    [InlineData("Unplugged")]
    [InlineData("Waiting")]
    [InlineData("Charging")]
    public void Render_Car_DrawsTheCar_InEachAspect(string aspect)
    {
        using var renderer = new EnergyRenderer(CarConfig());
        using var withCar = Draw(renderer, CarState(Enum.Parse<CarAspect>(aspect)));

        Assert.True(AlphaSum(withCar, CarBox()) > 20_000);
    }

    [Fact]
    public void Render_Unplugged_IsDimmerThanWaiting()
    {
        using var unplugged = new EnergyRenderer(CarConfig());
        using var waiting = new EnergyRenderer(CarConfig());
        using var a = Draw(unplugged, CarState(CarAspect.Unplugged));
        using var b = Draw(waiting, CarState(CarAspect.Waiting));

        Assert.True(AlphaSum(a, CarBox()) < .75 * AlphaSum(b, CarBox()));
    }

    [Fact]
    public void Render_Charging_ShowsPinkValue_AndAnimates()
    {
        using var renderer = new EnergyRenderer(CarConfig());
        var state = CarState(CarAspect.Charging);
        using var bitmap = Draw(renderer, state);

        Assert.True(PinkCount(bitmap, CarValueZone()) > 20);
        Assert.True(renderer.IsAnimating(state));
    }

    [Fact]
    public void Render_Waiting_DoesNotAnimate_WhenNothingElseMoves()
    {
        using var renderer = new EnergyRenderer(CarConfig());
        var state = CarState(CarAspect.Waiting);
        using var first = Draw(renderer, state);
        using var second = Draw(renderer, state, now: Start.AddSeconds(5));

        Assert.False(renderer.IsAnimating(state));
    }

    [Fact]
    public void Render_CarModeNone_SameAsBefore()
    {
        var input = new EnergyInput(1500, 300, 0, 0, 0, null, 7400);
        var leftover = new CarReading(7400, true, 50, CarAspect.Charging);
        var withLeftovers = new EnergyState(new EnergySnapshot(input, null, true, null, leftover), EnergyFlows.Compute(input), false);
        var before = new EnergyState(new EnergySnapshot(input with { Car = 0 }, null, true, null), EnergyFlows.Compute(input with { Car = 0 }), false);

        using var a = new EnergyRenderer(Config(LayoutKind.Left) with { CarPower = "sensor.car", CarPlug = "x", CarSoc = "y", CarColor = CarColor.Yellow });
        using var b = new EnergyRenderer(Config(LayoutKind.Left));
        using var bitmapA = Draw(a, withLeftovers);
        using var bitmapB = Draw(b, before);

        Assert.True(Bytes(bitmapA).AsSpan().SequenceEqual(Bytes(bitmapB)));
    }

    [Fact]
    public void Render_CarPowerUnknown_ShowsDash()
    {
        var input = new EnergyInput(0, 0, 0, 0, 0, null);
        var unknown = new EnergyState(new EnergySnapshot(input, null, true, null, new CarReading(null, true, null, CarAspect.Waiting)), EnergyFlows.Compute(input), false);
        using var renderer = new EnergyRenderer(CarConfig());
        using var other = new EnergyRenderer(CarConfig());
        using var dash = Draw(renderer, unknown);
        using var zero = Draw(other, CarState(CarAspect.Waiting));
        var zone = CarValueZone();

        Assert.True(InkCount(Crop(dash, zone)) > 0);
        Assert.False(Bytes(Crop(dash, zone)).AsSpan().SequenceEqual(Bytes(Crop(zero, zone))));
    }

    private static SKBitmap Crop(SKBitmap source, SKRect zone)
    {
        var crop = new SKBitmap((int)zone.Width, (int)zone.Height);
        using var canvas = new SKCanvas(crop);
        canvas.DrawBitmap(source, -zone.Left, -zone.Top);
        return crop;
    }

    [Theory]
    [InlineData("Top")]
    [InlineData("Left")]
    [InlineData("Bottom")]
    public void Render_HugeCarPower_StaysInsideCard_At5x3(string layout)
    {
        var kind = Enum.Parse<LayoutKind>(layout);
        using var renderer = new EnergyRenderer(CarConfig(kind));
        var state = CarState(CarAspect.Charging, watts: 11000);
        const float Width = 500, Height = 310;
        using var bitmap = new SKBitmap(700, 434);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var ctx = new ModuleRenderContext(new SKRect(40, 40, 40 + Width, 40 + Height), new GridSize(5, 3), TestTheme(), 1.3f, Start);
        renderer.Render(canvas, ctx, state);

        Assert.Equal(0, PinkCount(bitmap, new SKRect(0, 0, 40, 434)));
        Assert.Equal(0, PinkCount(bitmap, new SKRect(40 + Width, 0, 700, 434)));
        Assert.Equal(0, PinkCount(bitmap, new SKRect(40 + Width - 4, 0, 40 + Width, 434)));
    }

    [Fact]
    public void CarLabel_Texts()
    {
        Assert.Equal("Voiture · en charge · 86 %", EnergyRenderer.CarLabel(new CarReading(7400, true, 86, CarAspect.Charging)));
        Assert.Equal("Voiture · branchée", EnergyRenderer.CarLabel(new CarReading(0, true, null, CarAspect.Waiting)));
        Assert.Equal("Voiture · débranchée · 12 %", EnergyRenderer.CarLabel(new CarReading(0, false, 12.4, CarAspect.Unplugged)));
    }

    private static byte[] Bytes(SKBitmap bitmap) => bitmap.Bytes;
}
