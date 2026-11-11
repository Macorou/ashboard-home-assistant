using System.Globalization;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>
/// Compose la carte d'énergie (spec § 8) : halos, lignes de fond, filaments, dessins, lumières dynamiques, textes, dans cet ordre. Tout est posé
/// dans l'espace 700 × 434, mis à l'échelle uniformément et centré dans <c>ctx.Bounds</c>. Les dessins sont des images préparées à l'échelle
/// finale ; seuls les halos, les filaments, les fondus de lumière, le reflet, la jauge et les textes sont recalculés à chaque image.
/// </summary>
internal sealed class EnergyRenderer : IDisposable
{
    private const double EaseSeconds = .35;
    private const double MaxStepSeconds = .5;
    private const double SheenMinWatts = 100;
    private const double SettledWatts = .5;
    private const float RefFontSize = 100;
    private const float UnitSize = .42f;
    private const float UnitOpacity = .65f;
    private const float StaleOpacity = .5f;
    private const float TextMargin = 8;

    private static readonly SKColor ProductionColor = SKColor.Parse("#ffd166");
    private static readonly SKColor BatteryColor = SKColor.Parse("#cdb3ff");
    private static readonly SKColor SellColor = SKColor.Parse("#34e39a");
    private static readonly SKColor BuyColor = SKColor.Parse("#ff7a45");
    private static readonly SKColor BatterySegmentColor = SKColor.Parse("#b58cff");

    private static readonly Dictionary<string, (SKColor Color, float Alpha)> HaloColors = new()
    {
        ["solar"] = (SKColor.Parse("#ffb627"), .5f),
        ["home"] = (SKColor.Parse("#3f7cff"), .42f),
        ["exp"] = (SKColor.Parse("#26e08f"), .45f),
        ["imp"] = (SKColor.Parse("#ff6a3d"), .45f),
        ["bat"] = (SKColor.Parse("#9a6bff"), .42f),
    };

    private static readonly SKPath PanelPlane = BuildPanelPlane();

    private static readonly SKColor CarPink = SKColor.Parse("#ff7eb6");
    private static readonly SKColor CarWaitingColor = SKColor.Parse("#c98aa8");
    private static readonly SKColor PlugColor = SKColor.Parse("#c9d1de");
    private static readonly SKColor BadgeDisc = new(20, 24, 32, 217);
    private const float UnpluggedOpacity = .55f;
    private const float WaitingLineOpacity = .16f;

    private static readonly SKPath PlugShape = BuildPlug();
    private static readonly SKPath BoltShape = BuildBolt();

    private readonly record struct Quantities(double Solar, double Home, double Export, double Import, double Charge, double Discharge, double Car);

    private readonly EnergyConfig _config;
    private readonly EnergyLayout _layout;
    private readonly ArtCache _art = new();
    private readonly FilamentPainter _filaments = new();
    private readonly Dictionary<string, SKShader> _haloShaders = [];
    private readonly Dictionary<float, SKFont> _fonts = [];
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _image = new() { IsAntialias = true };
    private readonly SKPath _scratch = new();
    private readonly SKColorFilter _unpluggedFilter = BuildUnpluggedFilter();
    private Theme? _fontTheme;
    private SKFont? _measureFont;
    private Quantities? _shown;
    private DateTimeOffset? _lastNow;
    private double _clock;

    internal EnergyRenderer(EnergyConfig config)
    {
        _config = config;
        _layout = EnergyLayouts.Get(config.Layout, config.Battery != BatteryMode.None, config.HasCar);
    }

    private bool HasBattery => _config.Battery != BatteryMode.None;

    /// <summary>Ce qu'on sait de la voiture : sans lecture (ou sans voiture réglée), elle compte comme débranchée et sans mesure.</summary>
    private CarReading? CarOf(EnergyState state) =>
        _config.HasCar ? state.Snapshot.Car ?? new CarReading(null, null, null, CarAspect.Unplugged) : null;

    /// <summary>Vrai tant que quelque chose bouge : un flux, le reflet des panneaux, la jauge qui respire, ou des valeurs qui n'ont pas fini de s'approcher de leur cible.</summary>
    internal bool IsAnimating(EnergyState? state)
    {
        if (state is null || state.Stale) return false;
        var flows = state.Flows;
        if (flows.SolarToHome > 0 || flows.SolarToGrid > 0 || flows.GridToHome > 0 || flows.SolarToBattery > 0
            || flows.BatteryToHome > 0 || flows.GridToBattery > 0 || flows.BatteryToGrid > 0 || (_config.HasCar && flows.HomeToCar > 0))
        {
            return true;
        }

        var target = TargetOf(state);
        if (target.Solar >= SheenMinWatts) return true;
        if (BatteryDirection(target) != 0 && HasBattery && state.Snapshot.BatterySocPercent is not null) return true;
        return _shown is { } shown && !Settled(shown, target);
    }

    /// <summary>Le message à afficher si la case est plus petite que 5 × 3, sinon null.</summary>
    internal static string? TooSmallMessage(GridSize size) =>
        size.Columns < 5 || size.Rows < 3 ? "Agrandis la case : 5 × 3 minimum" : null;

    /// <summary>Vrai si le dernier dessin a montré le message « case trop petite » : rien ne bouge alors.</summary>
    internal bool ShowsMessage { get; private set; }

    internal void Render(SKCanvas canvas, ModuleRenderContext ctx, EnergyState? state)
    {
        // La taille se vérifie avant tout, même sans données.
        if (TooSmallMessage(ctx.Size) is { } message)
        {
            ShowsMessage = true;
            ModuleDrawing.DrawActionMessage(canvas, ctx.Bounds, ctx.Theme, ctx.TextScale, message);
            return;
        }

        ShowsMessage = false;
        if (state is null) return;
        var bounds = ctx.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var target = TargetOf(state);
        Step(ctx.Now, state, target);
        var shown = _shown ?? target;

        var scale = Math.Min(bounds.Width / EnergyLayouts.Width, bounds.Height / EnergyLayouts.Height);
        var originX = bounds.Left + (bounds.Width - EnergyLayouts.Width * scale) / 2;
        var originY = bounds.Top + (bounds.Height - EnergyLayouts.Height * scale) / 2;
        var stale = state.Stale;
        var selling = target.Export > target.Import;

        canvas.Save();
        canvas.ClipRect(bounds);

        canvas.Save();
        canvas.Translate(originX, originY);
        canvas.Scale(scale);
        DrawHalos(canvas, shown, stale);
        var car = CarOf(state);
        var carLine = car?.Aspect switch { CarAspect.Unplugged => 0f, CarAspect.Waiting => WaitingLineOpacity, _ => -1f };
        _filaments.Draw(canvas, _layout, stale ? Still(state.Flows) : state.Flows, carLine);
        canvas.Restore();

        foreach (var placement in _layout.Arts)
        {
            DrawArt(canvas, placement, scale, originX, originY, shown, state, selling);
        }

        DrawTexts(canvas, ctx, state, shown, target, scale, originX, originY, selling);
        canvas.Restore();
    }

    private Quantities TargetOf(EnergyState state)
    {
        var input = state.Snapshot.Input;
        var battery = HasBattery;
        return new Quantities(Math.Max(0, input.Solar), state.Flows.Home, Math.Max(0, input.Export), Math.Max(0, input.Import),
            battery ? Math.Max(0, input.Charge) : 0, battery ? Math.Max(0, input.Discharge) : 0,
            CarOf(state)?.Watts is { } carWatts ? Math.Max(0, carWatts) : 0);
    }

    private static int BatteryDirection(Quantities q) =>
        q.Charge >= EnergyFlows.NoiseFloorWatts ? 1 : q.Discharge >= EnergyFlows.NoiseFloorWatts ? -1 : 0;

    private static bool Settled(Quantities a, Quantities b) =>
        Math.Abs(a.Solar - b.Solar) < SettledWatts && Math.Abs(a.Home - b.Home) < SettledWatts && Math.Abs(a.Export - b.Export) < SettledWatts
        && Math.Abs(a.Import - b.Import) < SettledWatts && Math.Abs(a.Charge - b.Charge) < SettledWatts && Math.Abs(a.Discharge - b.Discharge) < SettledWatts
        && Math.Abs(a.Car - b.Car) < SettledWatts;

    private static EnergyFlows Still(EnergyFlows flows) =>
        flows with { SolarToHome = 0, BatteryToHome = 0, GridToHome = 0, SolarToBattery = 0, GridToBattery = 0, SolarToGrid = 0, BatteryToGrid = 0, HomeToCar = 0 };

    /// <summary>Avance l'horloge, adoucit les valeurs affichées vers leur cible et fait avancer les filaments. La première image prend la cible telle quelle.</summary>
    private void Step(DateTimeOffset now, EnergyState state, Quantities target)
    {
        var dt = _lastNow is { } last ? Math.Clamp((now - last).TotalSeconds, 0, MaxStepSeconds) : 0;
        _lastNow = now;

        if (_shown is not { } shown)
        {
            _shown = target;
        }
        else
        {
            var k = 1 - Math.Exp(-dt / EaseSeconds);
            double Ease(double from, double to) => from + (to - from) * k;
            _shown = new Quantities(Ease(shown.Solar, target.Solar), Ease(shown.Home, target.Home), Ease(shown.Export, target.Export),
                Ease(shown.Import, target.Import), Ease(shown.Charge, target.Charge), Ease(shown.Discharge, target.Discharge), Ease(shown.Car, target.Car));
        }

        if (!state.Stale)
        {
            _clock += dt;
            _filaments.Advance(dt, state.Flows);
        }
    }

    // ----- Halos -----

    private void DrawHalos(SKCanvas canvas, Quantities q, bool stale)
    {
        for (var i = 0; i < _layout.Halos.Count; i++)
        {
            var halo = _layout.Halos[i];
            var intensity = halo.Role switch
            {
                "solar" => Math.Min(1, q.Solar / 2600),
                "exp" => Math.Min(1, q.Export / 1200),
                "imp" => Math.Min(1, q.Import / 900),
                "bat" => Math.Min(1, (q.Charge + q.Discharge) / 1200),
                _ => .4 + Math.Min(.6, q.Home / 3000),
            };
            if (stale) intensity *= StaleOpacity;
            if (intensity < .004) continue;

            var phase = 1.7 * i;
            var x = halo.X + 26 * Math.Sin(_clock * .35 + phase);
            var y = halo.Y + 16 * Math.Cos(_clock * .27 + phase);

            _fill.Shader = HaloShader(halo.Role);
            _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(intensity * 255), 0, 255));
            canvas.Save();
            canvas.Translate((float)x, (float)y);
            canvas.Scale(halo.Radius);
            canvas.DrawCircle(0, 0, 1, _fill);
            canvas.Restore();
        }

        _fill.Shader = null;
    }

    private SKShader HaloShader(string role)
    {
        if (_haloShaders.TryGetValue(role, out var shader)) return shader;
        var (color, alpha) = HaloColors[role];
        return _haloShaders[role] = SKShader.CreateRadialGradient(SKPoint.Empty, 1, [color.WithAlpha((byte)Math.Round(alpha * 255)), color.WithAlpha(0)], null, SKShaderTileMode.Clamp);
    }

    // ----- Dessins et lumières -----

    private void DrawArt(SKCanvas canvas, ArtPlacement placement, float layoutScale, float originX, float originY,
        Quantities q, EnergyState state, bool selling)
    {
        var kind = placement.Kind == ArtKind.HouseModern && _config.House == HouseStyle.Classic ? ArtKind.HouseClassic : placement.Kind;
        if (kind == ArtKind.CarBlue) kind = CarArt.KindFor(_config.CarColor);
        var total = layoutScale * placement.Scale;
        var art = _art.Get(kind, total);
        var car = CarOf(state);

        // Images posées sur des pixels entiers ; les lumières vectorielles partent du même point pour rester collées au dessin.
        var left = MathF.Round(originX + layoutScale * placement.X - art.Origin.X);
        var top = MathF.Round(originY + layoutScale * placement.Y - art.Origin.Y);
        var sampling = new SKSamplingOptions();
        if (kind is ArtKind.CarBlue or ArtKind.CarYellow or ArtKind.CarWhite && car?.Aspect == CarAspect.Unplugged)
        {
            // Débranchée : la voiture s'efface, presque grise et sombre.
            _image.Color = SKColors.White.WithAlpha((byte)Math.Round(UnpluggedOpacity * 255));
            _image.ColorFilter = _unpluggedFilter;
            canvas.DrawImage(art.Dark, left, top, sampling, _image);
            _image.ColorFilter = null;
        }
        else
        {
            canvas.DrawImage(art.Dark, left, top, sampling, null);
        }

        var lit = kind switch
        {
            ArtKind.HouseModern or ArtKind.HouseClassic => Math.Min(1, .28 + q.Home / 2400),
            ArtKind.Panels => Math.Min(.95, q.Solar / 3600),
            _ => 0,
        };
        if (lit > .004)
        {
            _image.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(lit * 255), 0, 255));
            canvas.DrawImage(art.Lit, left, top, sampling, _image);
        }

        canvas.Save();
        canvas.Translate(left + art.Origin.X, top + art.Origin.Y);
        canvas.Scale(total);
        switch (kind)
        {
            case ArtKind.Panels: DrawSheen(canvas, q.Solar); break;
            case ArtKind.Pylon: DrawInsulators(canvas, selling); break;
            case ArtKind.Battery: DrawGauge(canvas, state); break;
            case ArtKind.CarBlue or ArtKind.CarYellow or ArtKind.CarWhite: DrawCarBadge(canvas, car?.Aspect ?? CarAspect.Unplugged); break;
        }
        canvas.Restore();
    }

    /// <summary>Désature (0,15) et assombrit (0,75) : la voiture débranchée.</summary>
    private static SKColorFilter BuildUnpluggedFilter()
    {
        const float Saturation = .15f, Darken = .75f;
        float[] luma = [.2126f, .7152f, .0722f];
        var matrix = new float[20];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                matrix[row * 5 + column] = Darken * (luma[column] * (1 - Saturation) + (row == column ? Saturation : 0));
            }
        }
        matrix[18] = 1;
        return SKColorFilter.CreateColorMatrix(matrix);
    }

    // Tracés du badge, tirés de la maquette « voiture », centrés sur (0, 0).
    private static SKPath BuildPlug()
    {
        var path = new SKPath();
        path.MoveTo(-3, -6); path.LineTo(-3, -2);
        path.MoveTo(3, -6); path.LineTo(3, -2);
        path.MoveTo(-5, -2); path.LineTo(5, -2); path.LineTo(5, .5f);
        path.ArcTo(5, 5, 0, SKPathArcSize.Small, SKPathDirection.Clockwise, -5, .5f);
        path.Close();
        path.MoveTo(0, 5); path.LineTo(0, 8);
        return path;
    }

    private static SKPath BuildBolt()
    {
        var path = new SKPath();
        path.MoveTo(1.5f, -6); path.LineTo(-3.5f, 1); path.LineTo(-.2f, 1); path.LineTo(-1.5f, 6);
        path.LineTo(3.5f, -1); path.LineTo(.2f, -1);
        path.Close();
        return path;
    }

    /// <summary>La pastille au-dessus du toit : une prise quand la voiture est branchée, un éclair rose qui pulse quand elle charge. Coordonnées locales de la voiture.</summary>
    private void DrawCarBadge(SKCanvas canvas, CarAspect aspect)
    {
        if (aspect == CarAspect.Unplugged) return;
        var charging = aspect == CarAspect.Charging;
        var color = charging ? CarPink : PlugColor;
        var opacity = charging ? .55 + .45 * (.5 + .5 * Math.Sin(_clock * 3.9)) : 1;

        canvas.Save();
        canvas.Translate(CarArt.BadgeAnchor.X, CarArt.BadgeAnchor.Y);

        _fill.Color = Fade(BadgeDisc, (float)opacity);
        canvas.DrawCircle(0, 0, 9, _fill);
        _stroke.StrokeWidth = 1.4f;
        _stroke.Color = Fade(color, (float)opacity);
        canvas.DrawCircle(0, 0, 9, _stroke);

        if (charging)
        {
            _fill.Color = Fade(color, (float)opacity);
            canvas.DrawPath(BoltShape, _fill);
        }
        else
        {
            _stroke.StrokeWidth = 1.5f;
            _stroke.StrokeCap = SKStrokeCap.Round;
            canvas.DrawPath(PlugShape, _stroke);
            _stroke.StrokeCap = SKStrokeCap.Butt;
        }

        canvas.Restore();
    }

    private static SKPath BuildPanelPlane()
    {
        var path = new SKPath();
        path.MoveTo(PanelsArt.Plane(0, 0));
        path.LineTo(PanelsArt.Plane(1, 0));
        path.LineTo(PanelsArt.Plane(1, 1));
        path.LineTo(PanelsArt.Plane(0, 1));
        path.Close();
        return path;
    }

    /// <summary>Le reflet qui passe sur les panneaux, découpé au plan des modules. Coordonnées locales des panneaux.</summary>
    private void DrawSheen(SKCanvas canvas, double solar)
    {
        var power = (float)Math.Min(1, solar / 3000);
        if (solar < SheenMinWatts) return;

        const float Width = .22f, Skew = .2f;
        var u = (float)(_clock * .22 % 1) * 1.9f - .55f;
        SKPoint[] quad = [PanelsArt.Plane(u, 0), PanelsArt.Plane(u + Width, 0), PanelsArt.Plane(u + Width + Skew, 1), PanelsArt.Plane(u + Skew, 1)];
        var minX = quad.Min(p => p.X);
        var maxX = quad.Max(p => p.X);

        _scratch.Reset();
        _scratch.MoveTo(quad[0]);
        for (var i = 1; i < quad.Length; i++) _scratch.LineTo(quad[i]);
        _scratch.Close();

        using var shader = SKShader.CreateLinearGradient(new SKPoint(minX, 0), new SKPoint(maxX, 0),
            [SKColors.White.WithAlpha(0), SKColors.White.WithAlpha(140), SKColors.White.WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        _fill.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)Math.Round(power * 255), 0, 255));
        canvas.Save();
        canvas.ClipPath(PanelPlane, SKClipOperation.Intersect, true);
        canvas.DrawPath(_scratch, _fill);
        canvas.Restore();
        _fill.Shader = null;
    }

    /// <summary>Les quatre isolateurs : verts quand on vend, orange sinon. Coordonnées locales du pylône.</summary>
    private void DrawInsulators(SKCanvas canvas, bool selling)
    {
        var color = selling ? SellColor : BuyColor;
        foreach (var center in PylonArt.InsulatorCenters)
        {
            _fill.Color = color.WithAlpha(90);
            canvas.DrawCircle(center, PylonArt.InsulatorRadius * 2.1f, _fill);
            _fill.Color = color;
            canvas.DrawCircle(center, PylonArt.InsulatorRadius, _fill);
        }
    }

    /// <summary>Les cinq segments de la batterie, du bas vers le haut ; celui du dessus respire pendant une charge ou une décharge. Coordonnées locales de la batterie.</summary>
    private void DrawGauge(SKCanvas canvas, EnergyState state)
    {
        if (state.Snapshot.BatterySocPercent is not { } soc) return;

        var lit = soc / 20;
        var whole = (int)Math.Floor(lit);
        var top = Math.Min(4, whole);
        var direction = BatteryDirection(TargetOf(state));
        for (var i = 0; i < BatteryArt.Segments.Length; i++)
        {
            var opacity = i < whole ? .95 : i == whole ? Math.Max(.12, lit - whole) : 0;
            if (direction != 0 && i == top) opacity = .25 + .7 * (.5 + .5 * Math.Sin(_clock * 3.2));
            if (opacity < .02) continue;

            _scratch.Reset();
            var segment = BatteryArt.Segments[i];
            _scratch.MoveTo(segment[0]);
            for (var k = 1; k < segment.Length; k++) _scratch.LineTo(segment[k]);
            _scratch.Close();

            var alpha = (float)opacity;
            _stroke.StrokeWidth = 3.5f;
            _stroke.Color = BatterySegmentColor.WithAlpha((byte)Math.Round(alpha * 70));
            canvas.DrawPath(_scratch, _stroke);
            _fill.Color = BatterySegmentColor.WithAlpha((byte)Math.Round(alpha * 255));
            canvas.DrawPath(_scratch, _fill);
        }
    }

    // ----- Textes -----

    private void DrawTexts(SKCanvas canvas, ModuleRenderContext ctx, EnergyState state, Quantities shown, Quantities target,
        float layoutScale, float originX, float originY, bool selling)
    {
        EnsureFonts(ctx.Theme);
        var share = (int)Math.Round(state.Flows.SolarSharePercent, MidpointRounding.AwayFromZero);
        var shareText = $"{share} % solaire";
        var hasShareSlot = _layout.Texts.Any(slot => slot.Role == "share");
        var fade = state.Stale ? StaleOpacity : 1f;
        var gridColor = selling ? SellColor : BuyColor;
        var car = CarOf(state);

        foreach (var slot in _layout.Texts)
        {
            switch (slot.Role)
            {
                case "solarLabel": DrawLabel(canvas, ctx, slot, "Production", layoutScale, originX, originY, fade); break;
                case "solarValue": DrawValue(canvas, ctx, slot, FormatPower(shown.Solar), ProductionColor, layoutScale, originX, originY, fade); break;
                case "share": DrawLabel(canvas, ctx, slot, shareText, layoutScale, originX, originY, fade); break;
                case "homeLabel":
                    DrawLabel(canvas, ctx, slot, hasShareSlot ? "Consommation" : "Consommation · " + shareText, layoutScale, originX, originY, fade);
                    break;
                case "homeValue": DrawValue(canvas, ctx, slot, FormatPower(shown.Home), ctx.Theme.Text, layoutScale, originX, originY, fade); break;
                case "gridLabel": DrawLabel(canvas, ctx, slot, selling ? "Vendu au réseau" : "Acheté au réseau", layoutScale, originX, originY, fade); break;
                case "gridValue":
                    DrawValue(canvas, ctx, slot, FormatPower(Math.Max(shown.Export, shown.Import)), gridColor, layoutScale, originX, originY, fade);
                    break;
                case "carLabel" when car is not null: DrawLabel(canvas, ctx, slot, CarLabel(car), layoutScale, originX, originY, fade); break;
                case "carValue" when car is not null:
                    DrawValue(canvas, ctx, slot, car.Watts is null ? "—" : FormatPower(shown.Car),
                        car.Aspect switch { CarAspect.Charging => CarPink, CarAspect.Waiting => CarWaitingColor, _ => ctx.Theme.TextMuted },
                        layoutScale, originX, originY, fade);
                    break;
                case "batLabel": DrawLabel(canvas, ctx, slot, BatteryLabel(state, target), layoutScale, originX, originY, fade); break;
                case "batValue":
                    DrawValue(canvas, ctx, slot, state.Snapshot.BatteryPowerKnown ? FormatPower(Math.Max(shown.Charge, shown.Discharge)) : "—",
                        BatteryColor, layoutScale, originX, originY, fade);
                    break;
            }
        }
    }

    private static string BatteryLabel(EnergyState state, Quantities target)
    {
        var direction = BatteryDirection(target);
        var name = direction > 0 ? "En charge" : direction < 0 ? "En décharge" : "Batterie";
        return state.Snapshot.BatterySocPercent is { } soc ? $"{name} · {(int)Math.Round(soc, MidpointRounding.AwayFromZero)} %" : name;
    }

    /// <summary>« Voiture · en charge · 86 % » : l'état, puis le niveau de la batterie quand on le connaît.</summary>
    internal static string CarLabel(CarReading car)
    {
        var name = car.Aspect switch
        {
            CarAspect.Charging => "Voiture · en charge",
            CarAspect.Waiting => "Voiture · branchée",
            _ => "Voiture · débranchée",
        };
        return car.SocPercent is { } soc ? $"{name} · {(int)Math.Round(soc, MidpointRounding.AwayFromZero)} %" : name;
    }

    private void DrawLabel(SKCanvas canvas, ModuleRenderContext ctx, TextSlot slot, string text, float layoutScale, float originX, float originY, float fade)
    {
        var wanted = slot.Size * layoutScale * ctx.TextScale;
        var size = FitSize(text, wanted, MaxWidth(slot) * layoutScale, MeasureReference);
        using var temp = size < wanted ? ModuleDrawing.Font(ctx.Theme, size) : null;
        var font = temp ?? FontAt(ctx.Theme, wanted);
        using var paint = new SKPaint { IsAntialias = true, Color = Fade(ctx.Theme.TextMuted, fade) };
        var x = originX + slot.X * layoutScale;
        var baseline = originY + slot.Y * layoutScale;
        ModuleDrawing.DrawText(canvas, text, x, baseline, slot.Centered ? SKTextAlign.Center : SKTextAlign.Left, font, paint);
    }

    /// <summary>Le nombre en grand, l'unité à 0,42 fois la taille et à 65 % d'opacité, sur la même ligne de base.</summary>
    private void DrawValue(SKCanvas canvas, ModuleRenderContext ctx, TextSlot slot, string text, SKColor color, float layoutScale, float originX, float originY, float fade)
    {
        var split = text.IndexOf(' ');
        var number = split < 0 ? text : text[..split];
        var unit = split < 0 ? "" : text[split..]; // garde l'espace de tête, comme la maquette

        var wanted = slot.Size * layoutScale * ctx.TextScale;
        var size = FitSize(text, wanted, MaxWidth(slot) * layoutScale, (_, s) => MeasureReference(number, s) + MeasureReference(unit, s * UnitSize));
        using var numberTemp = size < wanted ? ModuleDrawing.Font(ctx.Theme, size) : null;
        var numberFont = numberTemp ?? FontAt(ctx.Theme, wanted);
        using var unitFont = ModuleDrawing.Font(ctx.Theme, size * UnitSize);

        var numberWidth = ModuleDrawing.MeasureText(number, numberFont);
        var unitWidth = unit.Length > 0 ? ModuleDrawing.MeasureText(unit, unitFont) : 0;
        var x = originX + slot.X * layoutScale;
        if (slot.Centered) x -= (numberWidth + unitWidth) / 2;
        var baseline = originY + slot.Y * layoutScale;

        using var paint = new SKPaint { IsAntialias = true, Color = Fade(color, fade) };
        ModuleDrawing.DrawText(canvas, number, x, baseline, SKTextAlign.Left, numberFont, paint);
        if (unit.Length > 0)
        {
            paint.Color = Fade(color, fade * UnitOpacity);
            ModuleDrawing.DrawText(canvas, unit, x + numberWidth, baseline, SKTextAlign.Left, unitFont, paint);
        }
    }

    private static SKColor Fade(SKColor color, float factor) => color.WithAlpha((byte)Math.Clamp((int)Math.Round(color.Alpha * factor), 0, 255));

    /// <summary>Largeur disponible autour d'un emplacement, dans l'espace 700 × 434 : jamais au-delà du bord de la carte.</summary>
    private static float MaxWidth(TextSlot slot) =>
        slot.MaxWidth ??
        (slot.Centered
            ? 2 * Math.Min(slot.X, EnergyLayouts.Width - slot.X) - TextMargin
            : EnergyLayouts.Width - slot.X - TextMargin);

    private float MeasureReference(string text, float size) =>
        text.Length == 0 ? 0 : ModuleDrawing.MeasureText(text, _measureFont!) * size / RefFontSize;

    private void EnsureFonts(Theme theme)
    {
        if (_fontTheme is not null && _fontTheme.Equals(theme) && _measureFont is not null) return;
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        _measureFont?.Dispose();
        _measureFont = ModuleDrawing.Font(theme, RefFontSize);
        _fontTheme = theme;
    }

    private SKFont FontAt(Theme theme, float size)
    {
        if (!_fonts.TryGetValue(size, out var font)) _fonts[size] = font = ModuleDrawing.Font(theme, size);
        return font;
    }

    // ----- Formats -----

    /// <summary>« 640 W » sous 1000 W, « 3,42 kW » au-delà ; arrondi au watt avant de choisir l'unité.</summary>
    internal static string FormatPower(double watts)
    {
        var rounded = double.IsFinite(watts) ? Math.Round(Math.Max(0, watts), MidpointRounding.AwayFromZero) : 0;
        return rounded < 1000
            ? rounded.ToString("0", CultureInfo.InvariantCulture) + " W"
            : (rounded / 1000).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " kW";
    }

    /// <summary>La taille de police qui fait tenir <paramref name="text"/> dans <paramref name="maxWidth"/> : celle demandée si elle tient, sinon réduite au prorata.</summary>
    internal static float FitSize(string text, float wanted, float maxWidth, Func<string, float, float> measure)
    {
        if (wanted <= 0 || maxWidth <= 0 || text.Length == 0) return wanted;
        var width = measure(text, wanted);
        return width <= maxWidth || width <= 0 ? wanted : wanted * maxWidth / width;
    }

    public void Dispose()
    {
        _art.Dispose();
        _filaments.Dispose();
        foreach (var shader in _haloShaders.Values) shader.Dispose();
        _haloShaders.Clear();
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        _measureFont?.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _image.Dispose();
        _scratch.Dispose();
        _unpluggedFilter.Dispose();
    }
}
