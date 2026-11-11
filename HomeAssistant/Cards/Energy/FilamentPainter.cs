using SkiaSharp;

namespace HomeAssistant;

/// <summary>
/// Les lignes de fond et les filaments de courant (spec § 8). Chaque tracé est aplati une fois en une table de points espacés d'un pixel ; un
/// filament est une tête blanche suivie d'une queue de 20 points. La lueur est un second disque, plus large et peu opaque, sous chaque point.
/// Dessine dans l'espace 700 × 434 : le canevas porte déjà la mise à l'échelle.
/// </summary>
internal sealed class FilamentPainter : IDisposable
{
    internal const int TailLength = 20;
    internal const float TailSpacing = 3.4f;
    internal const float LineWidth = 1.1f;
    internal const float ActiveLineOpacity = .32f;
    internal const float IdleLineOpacity = .06f;
    internal const double BaseSpeed = 55;
    internal const double SpeedPerKilowatt = 75;

    private const float GlowExtra = 2.6f;
    private const float GlowOpacity = .22f;
    private const int Channels = 8;
    private const int BatteryToGridChannel = 7;

    private static readonly SKColor BatteryViolet = SKColor.Parse("#b58cff");

    private sealed class Table(SKPath path, SKPoint[] points, float length) : IDisposable
    {
        public SKPath Path { get; } = path;
        public SKPoint[] Points { get; } = points;
        public float Length { get; } = length;

        public SKPoint At(float s)
        {
            s = Math.Clamp(s, 0, Length);
            var i = Math.Min((int)s, Points.Length - 2);
            var f = s - i;
            var a = Points[i];
            var b = Points[i + 1];
            return new SKPoint(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
        }

        public void Dispose() => Path.Dispose();
    }

    private readonly Dictionary<FlowPath, Table> _tables = new(ReferenceEqualityComparer.Instance);
    private readonly double[] _phase = new double[Channels];
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _dot = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    /// <summary>Distance parcourue, en px, par les filaments d'un flux. <paramref name="batteryToGrid"/> : le sens inverse du tracé batterie-réseau.</summary>
    internal double PhaseOf(FlowKey key, bool batteryToGrid = false) => _phase[batteryToGrid ? BatteryToGridChannel : (int)key];

    internal static double PowerOf(EnergyFlows flows, FlowKey key) => key switch
    {
        FlowKey.SolarToHome => flows.SolarToHome,
        FlowKey.SolarToGrid => flows.SolarToGrid,
        FlowKey.GridToHome => flows.GridToHome,
        FlowKey.SolarToBattery => flows.SolarToBattery,
        FlowKey.BatteryToHome => flows.BatteryToHome,
        FlowKey.GridToBattery => flows.GridToBattery,
        FlowKey.HomeToCar => flows.HomeToCar,
        _ => 0,
    };

    /// <summary>Vitesse d'un filament, en px/s : 55 + kW × 75.</summary>
    internal static double SpeedFor(double watts) => BaseSpeed + watts / 1000 * SpeedPerKilowatt;

    /// <summary>Fait avancer les filaments des flux actifs ; un flux nul ne bouge pas.</summary>
    public void Advance(double dtSeconds, EnergyFlows flows)
    {
        if (dtSeconds <= 0) return;
        for (var channel = 0; channel < Channels; channel++)
        {
            var watts = channel == BatteryToGridChannel ? flows.BatteryToGrid : PowerOf(flows, (FlowKey)channel);
            if (watts > 0) _phase[channel] += SpeedFor(watts) * dtSeconds;
        }
    }

    /// <param name="carLineOpacity">
    /// Le tracé de la voiture : <c>-1</c> garde la règle habituelle ; <c>0</c> ne le trace pas ; toute autre valeur fixe l'opacité de sa ligne de fond
    /// tant que son flux est nul (un flux actif garde la ligne et les filaments habituels).
    /// </param>
    public void Draw(SKCanvas canvas, EnergyLayout layout, EnergyFlows flows, float carLineOpacity = -1)
    {
        foreach (var path in layout.Flows)
        {
            var forward = PowerOf(flows, path.Key);
            var backward = path.Key == FlowKey.GridToBattery ? flows.BatteryToGrid : 0;
            if (path.Key == FlowKey.HomeToCar && carLineOpacity == 0) continue;
            var table = TableFor(path);

            var idle = path.Key == FlowKey.HomeToCar && carLineOpacity > 0 ? carLineOpacity : IdleLineOpacity;
            _line.Color = (forward > 0 || backward <= 0 ? path.Color : BatteryViolet).WithAlpha(Alpha(forward > 0 || backward > 0 ? ActiveLineOpacity : idle));
            canvas.DrawPath(table.Path, _line);

            if (forward > 0) DrawComets(canvas, table, path.Color, _phase[(int)path.Key], reverse: false);
            if (backward > 0) DrawComets(canvas, table, BatteryViolet, _phase[BatteryToGridChannel], reverse: true);
        }
    }

    private void DrawComets(SKCanvas canvas, Table table, SKColor color, double phase, bool reverse)
    {
        var length = table.Length;
        if (length <= 0) return;
        var heads = Math.Max(1, (int)Math.Round(length / 110.0, MidpointRounding.AwayFromZero));
        for (var i = 0; i < heads; i++)
        {
            var head = (phase + i * (double)length / heads) % length;
            for (var t = 0; t < TailLength; t++)
            {
                var s = (float)head - t * TailSpacing;
                if (s < 0) break;
                var p = table.At(reverse ? length - s : s);
                var fade = 1f - t / (float)TailLength;
                var radius = 1.5f * fade + .35f;
                var opacity = MathF.Pow(fade, 1.5f);
                var tint = t == 0 ? SKColors.White : color;

                _dot.Color = tint.WithAlpha(Alpha(opacity * GlowOpacity));
                canvas.DrawCircle(p, radius + GlowExtra, _dot);
                _dot.Color = tint.WithAlpha(Alpha(opacity));
                canvas.DrawCircle(p, radius, _dot);
            }
        }
    }

    private static byte Alpha(float opacity) => (byte)Math.Clamp((int)MathF.Round(opacity * 255), 0, 255);

    private Table TableFor(FlowPath path)
    {
        if (_tables.TryGetValue(path, out var table)) return table;

        var b = path.Bezier;
        var skPath = new SKPath();
        skPath.MoveTo(b[0]);
        skPath.CubicTo(b[1], b[2], b[3]);

        // Aplatit la courbe en 256 segments, puis rééchantillonne à un point par pixel de longueur.
        const int Steps = 256;
        var dense = new SKPoint[Steps + 1];
        var cumulative = new float[Steps + 1];
        for (var i = 0; i <= Steps; i++)
        {
            dense[i] = Cubic(b, i / (float)Steps);
            if (i > 0) cumulative[i] = cumulative[i - 1] + SKPoint.Distance(dense[i - 1], dense[i]);
        }

        var length = cumulative[Steps];
        var points = new SKPoint[(int)MathF.Floor(length) + 2];
        var seg = 0;
        for (var d = 0; d < points.Length; d++)
        {
            var target = Math.Min(d, length);
            while (seg < Steps - 1 && cumulative[seg + 1] < target) seg++;
            var span = cumulative[seg + 1] - cumulative[seg];
            var f = span > 0 ? (target - cumulative[seg]) / span : 0;
            points[d] = new SKPoint(dense[seg].X + (dense[seg + 1].X - dense[seg].X) * f, dense[seg].Y + (dense[seg + 1].Y - dense[seg].Y) * f);
        }

        return _tables[path] = new Table(skPath, points, length);
    }

    private static SKPoint Cubic(SKPoint[] b, float t)
    {
        var u = 1 - t;
        float w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
        return new SKPoint(w0 * b[0].X + w1 * b[1].X + w2 * b[2].X + w3 * b[3].X, w0 * b[0].Y + w1 * b[1].Y + w2 * b[2].Y + w3 * b[3].Y);
    }

    public void Dispose()
    {
        foreach (var table in _tables.Values) table.Dispose();
        _tables.Clear();
        _line.Dispose();
        _dot.Dispose();
    }
}
