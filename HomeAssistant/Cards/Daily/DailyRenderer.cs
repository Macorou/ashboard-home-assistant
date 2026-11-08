using SkiaSharp;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>
/// Dessine le Bilan du jour (spec § 6) dans l'espace de sa forme (464 × 100 ou 226 × 322), mis à l'échelle uniformément et centré dans
/// <c>ctx.Bounds</c>, sur le fond transparent du module. Une case trop petite affiche son message, même sans données.
/// </summary>
internal sealed class DailyRenderer(DailyConfig config) : IDisposable
{
    private const float RefFontSize = 100;
    private const float StaleOpacity = .5f;
    private const float UnitSize = .58f;
    private const float UnitOpacity = .68f;
    private const byte SeparatorAlpha = 46;

    // Bande
    private const float StripPad = 4, StripIcon = 16, StripIconTop = 18, StripNumberBaseline = 62, StripLabelBaseline = 80;
    private const float StripSeparatorTop = 14, StripSeparatorBottom = 86, StripLabelSize = 12, StripSidePad = 4;

    // Liste
    private const float ListPadX = 13, ListPadY = 6, ListIcon = 18, ListIconGap = 8, ListLabelSize = 14, ListNumberSize = 24, ListLabelMaxWidth = 80;

    private readonly Dictionary<float, SKFont> _fonts = [];
    private readonly SKPaint _text = new() { IsAntialias = true };
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _icon = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = DailyIcons.StrokeWidth,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };
    private Theme? _fontTheme;
    private SKFont? _measureFont;

    internal void Render(SKCanvas canvas, ModuleRenderContext ctx, DailyState? state)
    {
        // La taille se vérifie avant tout, même sans données.
        if (DailyLayout.TooSmallMessage(config.Shape, ctx.Size) is { } message)
        {
            ModuleDrawing.DrawActionMessage(canvas, ctx.Bounds, ctx.Theme, ctx.TextScale, message);
            return;
        }

        var bounds = ctx.Bounds;
        if (state is null || state.Items.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        var strip = config.Shape == DailyShape.Strip;
        var width = strip ? DailyLayout.StripWidth : DailyLayout.ListWidth;
        var height = strip ? DailyLayout.StripHeight : DailyLayout.ListHeight;
        var scale = Math.Min(bounds.Width / width, bounds.Height / height);
        var originX = bounds.Left + (bounds.Width - width * scale) / 2;
        var originY = bounds.Top + (bounds.Height - height * scale) / 2;
        var fade = state.Stale ? StaleOpacity : 1f;

        EnsureFonts(ctx.Theme);
        canvas.Save();
        canvas.ClipRect(bounds);
        if (strip) DrawStrip(canvas, ctx, state.Items, scale, originX, originY, fade);
        else DrawList(canvas, ctx, state.Items, scale, originX, originY, fade);
        canvas.Restore();
    }

    // ----- Bande -----

    private void DrawStrip(SKCanvas canvas, ModuleRenderContext ctx, IReadOnlyList<DailyItem> items, float s, float ox, float oy, float fade)
    {
        var count = items.Count;
        var columnWidth = (DailyLayout.StripWidth - 2 * StripPad) / count;
        var numberSize = count >= 6 ? 21f : 23f;
        var maxWidth = (columnWidth - 2 * StripSidePad) * s;

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var centerX = ox + (StripPad + columnWidth * (i + .5f)) * s;

            if (i > 0)
            {
                var x = ox + (StripPad + columnWidth * i) * s;
                DrawSeparator(canvas, ctx, x, oy + StripSeparatorTop * s, x, oy + StripSeparatorBottom * s, s, fade);
            }

            DrawIcon(canvas, item.Value, centerX - StripIcon * s / 2, oy + StripIconTop * s, StripIcon * s, fade);

            // Les kWh n'ont pas d'unité dans la bande ; un montant garde sa devise, sur la même chaîne.
            var number = item.Unit.Length == 0 || item.Unit == "kWh" ? item.Number : item.Number + " " + item.Unit;
            DrawCentered(canvas, ctx, number, numberSize, maxWidth, centerX, oy + StripNumberBaseline * s, s, ctx.Theme.Text, fade);
            DrawCentered(canvas, ctx, DailyReader.LabelOf(item.Value), StripLabelSize, maxWidth, centerX, oy + StripLabelBaseline * s, s,
                ctx.Theme.TextMuted, fade);
        }
    }

    private void DrawCentered(SKCanvas canvas, ModuleRenderContext ctx, string text, float designSize, float maxWidth, float centerX,
        float baseline, float s, SKColor color, float fade)
    {
        var wanted = designSize * s * ctx.TextScale;
        var size = EnergyRenderer.FitSize(text, wanted, maxWidth, MeasureReference);
        using var temp = size < wanted ? ModuleDrawing.Font(ctx.Theme, size) : null;
        _text.Color = Fade(color, fade);
        ModuleDrawing.DrawText(canvas, text, centerX, baseline, SKTextAlign.Center, temp ?? FontAt(ctx.Theme, wanted), _text);
    }

    // ----- Liste -----

    private void DrawList(SKCanvas canvas, ModuleRenderContext ctx, IReadOnlyList<DailyItem> items, float s, float ox, float oy, float fade)
    {
        var count = items.Count;
        var rowHeight = (DailyLayout.ListHeight - 2 * ListPadY) / count;
        var left = ListPadX;
        var right = DailyLayout.ListWidth - ListPadX;
        var labelX = ox + (left + ListIcon + ListIconGap) * s;

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var top = ListPadY + rowHeight * i;
            var centerY = oy + (top + rowHeight / 2) * s;

            if (i < count - 1)
            {
                var y = oy + (top + rowHeight) * s;
                DrawSeparator(canvas, ctx, ox + left * s, y, ox + right * s, y, s, fade);
            }

            DrawIcon(canvas, item.Value, ox + left * s, centerY - ListIcon * s / 2, ListIcon * s, fade);

            // Libellé à gauche, réduit s'il ne tient pas ; le nombre prend la place qui reste.
            var label = DailyReader.LabelOf(item.Value);
            var labelWanted = ListLabelSize * s * ctx.TextScale;
            var labelSize = EnergyRenderer.FitSize(label, labelWanted, ListLabelMaxWidth * s, MeasureReference);
            using var labelTemp = labelSize < labelWanted ? ModuleDrawing.Font(ctx.Theme, labelSize) : null;
            var labelFont = labelTemp ?? FontAt(ctx.Theme, labelWanted);
            _text.Color = Fade(ctx.Theme.TextMuted, fade);
            ModuleDrawing.DrawText(canvas, label, labelX, centerY + labelSize * .35f, SKTextAlign.Left, labelFont, _text);
            var labelWidth = ModuleDrawing.MeasureText(label, labelFont);

            DrawNumberRight(canvas, ctx, item, ox + right * s, centerY, s, (right - left - ListIcon - ListIconGap) * s - labelWidth - 8 * s, fade);
        }
    }

    /// <summary>Le nombre en grand, l'unité à 0,58 fois la taille et à 68 % d'opacité, alignés à droite sur la même ligne de base.</summary>
    private void DrawNumberRight(SKCanvas canvas, ModuleRenderContext ctx, DailyItem item, float right, float centerY, float s, float maxWidth, float fade)
    {
        var unit = item.Unit.Length == 0 ? "" : " " + item.Unit;
        var wanted = ListNumberSize * s * ctx.TextScale;
        var size = EnergyRenderer.FitSize(item.Number, wanted, maxWidth,
            (_, sz) => MeasureReference(item.Number, sz) + MeasureReference(unit, sz * UnitSize));
        using var numberTemp = size < wanted ? ModuleDrawing.Font(ctx.Theme, size) : null;
        var numberFont = numberTemp ?? FontAt(ctx.Theme, wanted);
        using var unitFont = unit.Length > 0 ? ModuleDrawing.Font(ctx.Theme, size * UnitSize) : null;

        var unitWidth = unitFont is null ? 0 : ModuleDrawing.MeasureText(unit, unitFont);
        var numberWidth = ModuleDrawing.MeasureText(item.Number, numberFont);
        var baseline = centerY + size * .36f;

        _text.Color = Fade(ctx.Theme.Text, fade);
        ModuleDrawing.DrawText(canvas, item.Number, right - unitWidth - numberWidth, baseline, SKTextAlign.Left, numberFont, _text);
        if (unitFont is not null)
        {
            _text.Color = Fade(ctx.Theme.Text, fade * UnitOpacity);
            ModuleDrawing.DrawText(canvas, unit, right - unitWidth, baseline, SKTextAlign.Left, unitFont, _text);
        }
    }

    // ----- Éléments communs -----

    private void DrawSeparator(SKCanvas canvas, ModuleRenderContext ctx, float x0, float y0, float x1, float y1, float s, float fade)
    {
        _line.StrokeWidth = Math.Max(1, s);
        _line.Color = Fade(ctx.Theme.TextMuted.WithAlpha(SeparatorAlpha), fade);
        canvas.DrawLine(x0, y0, x1, y1, _line);
    }

    private void DrawIcon(SKCanvas canvas, DailyValue value, float x, float y, float size, float fade)
    {
        _icon.Color = Fade(DailyIcons.ColorOf(value), fade);
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(size / DailyIcons.Box);
        canvas.DrawPath(DailyIcons.PathOf(value), _icon);
        canvas.Restore();
    }

    private static SKColor Fade(SKColor color, float factor) => color.WithAlpha((byte)Math.Clamp((int)Math.Round(color.Alpha * factor), 0, 255));

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

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        _measureFont?.Dispose();
        _text.Dispose();
        _line.Dispose();
        _icon.Dispose();
    }
}
