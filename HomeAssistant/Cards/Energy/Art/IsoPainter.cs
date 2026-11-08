using SkiaSharp;

namespace HomeAssistant;

/// <summary>Projection isométrique (x, y, z) vers un point du dessin, de la forme <c>Iso(k, ox, oy)</c> de la maquette.</summary>
internal delegate SKPoint Projection(double x, double y, double z);

/// <summary>Un rectangle de l'espace, vu comme une fonction de deux coordonnées vers un point du dessin (<c>fx</c>, <c>fy</c>, <c>ftop</c> de la maquette).</summary>
internal delegate SKPoint Face(double a, double b);

/// <summary>
/// Remplissage ou couleur de trait : une couleur unie, ou un dégradé linéaire. Un dégradé en <c>objectBoundingBox</c> (x1, y1, x2, y2 entre 0 et 1)
/// est calé sur la boîte englobante de chaque forme dessinée, comme en SVG ; un dégradé en <c>userSpaceOnUse</c> reçoit des points absolus du dessin.
/// </summary>
internal sealed class Fill
{
    private readonly SKColor _color;
    private readonly (float Offset, SKColor Color)[]? _stops;
    private readonly SKPoint _p1, _p2;
    private readonly bool _userSpace;

    private Fill(SKColor color) => _color = color;

    private Fill((float Offset, SKColor Color)[] stops, SKPoint p1, SKPoint p2, bool userSpace)
    {
        _stops = stops;
        _p1 = p1;
        _p2 = p2;
        _userSpace = userSpace;
    }

    /// <summary>Couleur unie ; <paramref name="alpha"/> équivaut à fill-opacity ou à une couleur rgba().</summary>
    public static Fill Solid(string hex, float alpha = 1f) => new(Color(hex, alpha));

    /// <summary>Couleur unie à partir de composantes, comme <c>rgba(70,40,18,.55)</c>.</summary>
    public static Fill Rgba(byte r, byte g, byte b, float alpha) => new(Scale(new SKColor(r, g, b), alpha));

    /// <summary>Dégradé calé sur la boîte englobante de la forme (<c>objectBoundingBox</c>).</summary>
    public static Fill Box(float x1, float y1, float x2, float y2, params (float Offset, string Hex)[] stops) =>
        new(Convert(stops), new SKPoint(x1, y1), new SKPoint(x2, y2), false);

    /// <summary>Dégradé en coordonnées absolues du dessin (<c>userSpaceOnUse</c>).</summary>
    public static Fill User(float x1, float y1, float x2, float y2, params (float Offset, string Hex)[] stops) =>
        new(Convert(stops), new SKPoint(x1, y1), new SKPoint(x2, y2), true);

    public static implicit operator Fill(string hex) => Solid(hex);

    /// <summary>Le même remplissage, avec toutes ses couleurs multipliées par <paramref name="opacity"/> (attribut <c>opacity</c> d'une forme sans trait).</summary>
    public Fill WithOpacity(float opacity)
    {
        if (_stops is null) return new Fill(Scale(_color, opacity));
        return new Fill(_stops.Select(s => (s.Offset, Scale(s.Color, opacity))).ToArray(), _p1, _p2, _userSpace);
    }

    internal static SKColor Color(string hex, float alpha = 1f) => Scale(SKColor.Parse(hex), alpha);

    private static SKColor Scale(SKColor c, float f) => c.WithAlpha((byte)Math.Clamp((int)Math.Round(c.Alpha * f), 0, 255));

    private static (float Offset, SKColor Color)[] Convert((float Offset, string Hex)[] stops) =>
        stops.Select(s => (s.Offset, SKColor.Parse(s.Hex))).ToArray();

    /// <summary>Applique ce remplissage à <paramref name="paint"/>. Le shader éventuel est à libérer par l'appelant.</summary>
    internal void Apply(SKPaint paint, SKRect bounds, out SKShader? shader)
    {
        shader = null;
        if (_stops is null)
        {
            paint.Color = _color;
            return;
        }
        var colors = _stops.Select(s => s.Color).ToArray();
        var pos = _stops.Select(s => s.Offset).ToArray();
        if (_userSpace)
        {
            shader = SKShader.CreateLinearGradient(_p1, _p2, colors, pos, SKShaderTileMode.Clamp);
        }
        else
        {
            // Le dégradé est défini dans le carré unité, puis étiré sur la boîte : ses lignes d'égale couleur suivent la déformation, comme en SVG.
            var m = SKMatrix.CreateScaleTranslation(bounds.Width, bounds.Height, bounds.Left, bounds.Top);
            shader = SKShader.CreateLinearGradient(_p1, _p2, colors, pos, SKShaderTileMode.Clamp, m);
        }
        paint.Shader = shader;
    }
}

/// <summary>Un trait : couleur (ou dégradé), épaisseur, extrémités et jointures arrondies ou non. L'opacité du trait est celle de la couleur.</summary>
internal sealed record Stroke(Fill Paint, float Width, bool RoundCap = false, bool RoundJoin = false)
{
    public static Stroke Of(string hex, float width, float alpha = 1f, bool roundCap = false, bool roundJoin = false) =>
        new(Fill.Solid(hex, alpha), width, roundCap, roundJoin);
}

/// <summary>
/// Pinceau des dessins isométriques : porte la formule de projection, les polygones, les traits et les fenêtres de la maquette.
/// Il dessine dans les coordonnées locales de la maquette (celles du groupe SVG avant sa transformation) : l'échelle et le décalage
/// sont ceux du canevas. <see cref="Lit"/> choisit l'image « allumée » (fenêtres et cellules éclairées) plutôt que l'image « éteinte ».
/// </summary>
internal sealed class IsoPainter(SKCanvas canvas)
{
    private SKRect _ink = SKRect.Empty;
    private bool _hasInk;

    /// <summary>Vrai pour l'image allumée : les éléments <c>data-win</c> et <c>data-cell</c> de la maquette y sont dessinés.</summary>
    public bool Lit { get; init; }

    /// <summary>Boîte englobante de tout ce qui a été dessiné (traits et halos compris), en coordonnées locales.</summary>
    public SKRect Ink => _hasInk ? _ink : SKRect.Empty;

    /// <summary>(x, y, z) vers le dessin : x̂ = ox + (x−y)·0,866·k ; ŷ = oy + (x+y)·0,5·k − z·k.</summary>
    public static Projection Iso(float k, float ox, float oy) =>
        (x, y, z) => new SKPoint((float)(ox + (x - y) * .866 * k), (float)(oy + (x + y) * .5 * k - z * k));

    public static SKPoint[] Rect(Face f, double a0, double a1, double b0, double b1) => [f(a0, b0), f(a1, b0), f(a1, b1), f(a0, b1)];
    public static Face FX(Projection i, double x) => (y, z) => i(x, y, z);
    public static Face FY(Projection i, double y) => (x, z) => i(x, y, z);
    public static Face FTop(Projection i, double z) => (x, y) => i(x, y, z);

    private static SKRect Bounds(SKPoint[] pts) =>
        new(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));

    private static SKPath PolyPath(SKPoint[] pts)
    {
        var path = new SKPath();
        path.MoveTo(pts[0]);
        for (var i = 1; i < pts.Length; i++) path.LineTo(pts[i]);
        path.Close();
        return path;
    }

    /// <summary>Polygone rempli et/ou tracé. <paramref name="glow"/> ajoute le halo <c>winGlow</c> (flou gaussien d'écart-type 3 fusionné avec la source).</summary>
    public void Poly(SKPoint[] pts, Fill? fill, Stroke? stroke = null, bool glow = false)
    {
        using var path = PolyPath(pts);
        Draw(path, Bounds(pts), fill, stroke, glow);
    }

    /// <summary>Plusieurs segments tracés d'un seul trait (un seul chemin : pas de double opacité aux croisements).</summary>
    public void Lines(Stroke stroke, IEnumerable<(SKPoint From, SKPoint To)> segments)
    {
        using var path = new SKPath();
        var any = false;
        foreach (var (a, b) in segments)
        {
            path.MoveTo(a);
            path.LineTo(b);
            any = true;
        }
        if (any) Draw(path, path.Bounds, null, stroke, false);
    }

    /// <summary>Chemin au format des attributs <c>d</c> du SVG.</summary>
    public void Path(string svgData, Stroke stroke)
    {
        using var path = SKPath.ParseSvgPathData(svgData);
        Draw(path, path.Bounds, null, stroke, false);
    }

    /// <summary>Rectangle à coins arrondis, rempli.</summary>
    public void RoundRect(float x, float y, float w, float h, float r, Fill fill)
    {
        using var path = new SKPath();
        path.AddRoundRect(new SKRect(x, y, x + w, y + h), r, r);
        Draw(path, path.Bounds, fill, null, false);
    }

    private void Draw(SKPath path, SKRect bounds, Fill? fill, Stroke? stroke, bool glow)
    {
        var ink = bounds;
        if (stroke is not null) ink.Inflate(stroke.Width / 2 + 1, stroke.Width / 2 + 1);
        if (glow)
        {
            // Région du filtre : x,y = -40 %, largeur et hauteur = 180 % de la boîte ; le flou y est coupé.
            var region = new SKRect(bounds.Left - .4f * bounds.Width, bounds.Top - .4f * bounds.Height,
                bounds.Right + .4f * bounds.Width, bounds.Bottom + .4f * bounds.Height);
            var reach = new SKRect(bounds.Left - 9, bounds.Top - 9, bounds.Right + 9, bounds.Bottom + 9);
            reach.Intersect(region);
            ink = SKRect.Union(ink, reach);
            canvas.Save();
            canvas.ClipRect(region, SKClipOperation.Intersect, true);
            if (fill is not null)
            {
                using var blur = SKImageFilter.CreateBlur(3, 3);
                using var halo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, ImageFilter = blur };
                fill.Apply(halo, bounds, out var hs);
                canvas.DrawPath(path, halo);
                hs?.Dispose();
            }
        }
        if (fill is not null)
        {
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            fill.Apply(paint, bounds, out var shader);
            canvas.DrawPath(path, paint);
            shader?.Dispose();
        }
        if (stroke is not null)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = stroke.Width,
                StrokeCap = stroke.RoundCap ? SKStrokeCap.Round : SKStrokeCap.Butt,
                StrokeJoin = stroke.RoundJoin ? SKStrokeJoin.Round : SKStrokeJoin.Miter,
                StrokeMiter = 4,
            };
            stroke.Paint.Apply(paint, bounds, out var shader);
            canvas.DrawPath(path, paint);
            shader?.Dispose();
        }
        if (glow) canvas.Restore();
        _ink = _hasInk ? SKRect.Union(_ink, ink) : ink;
        _hasInk = true;
    }

    /// <summary>Parallélépipède : dessus, face de droite (x = x1) et face de gauche (y = y1), dans cet ordre.</summary>
    public void Box(Projection i, double x0, double x1, double y0, double y1, double z0, double z1, Fill top, Fill right, Fill left)
    {
        Poly(Rect(FTop(i, z1), x0, x1, y0, y1), top);
        Poly(Rect(FX(i, x1), y0, y1, z0, z1), right);
        Poly(Rect(FY(i, y1), x0, x1, z0, z1), left);
    }

    /// <summary>Fenêtre : vitre sombre, lumière (image allumée seulement), croisillons, reflet, cadre. Les croisillons restent au-dessus de la lumière.</summary>
    public void Pane(Face f, double a0, double a1, double b0, double b1, int n, int m, string frame)
    {
        Poly(Rect(f, a0, a1, b0, b1), "#1c2436");
        if (Lit) Poly(Rect(f, a0, a1, b0, b1), ArtPalette.WinLit, null, glow: true);
        var bars = new List<(SKPoint, SKPoint)>();
        for (var i = 1; i < n; i++)
        {
            var a = a0 + (a1 - a0) * i / n;
            bars.Add((f(a, b0), f(a, b1)));
        }
        for (var i = 1; i < m; i++)
        {
            var b = b0 + (b1 - b0) * i / m;
            bars.Add((f(a0, b), f(a1, b)));
        }
        Lines(Stroke.Of(frame, 1.1f), bars);
        var w = a1 - a0;
        Poly([f(a0 + w * .12, b1), f(a0 + w * .34, b1), f(a0 + w * .16, b0), f(a0, b0)], Fill.Solid("#fff", .13f));
        Poly(Rect(f, a0, a1, b0, b1), null, Stroke.Of(frame, 1.5f, roundJoin: true));
    }

    /// <summary>Lattes verticales régulières (a0..a1 en n intervalles).</summary>
    public void Slats(Face f, double a0, double a1, double b0, double b1, int n, Fill color)
    {
        var d = new List<(SKPoint, SKPoint)>();
        for (var i = 0; i <= n; i++)
        {
            var a = a0 + (a1 - a0) * i / n;
            d.Add((f(a, b0), f(a, b1)));
        }
        Lines(new Stroke(color, .9f), d);
    }
}

/// <summary>Les dégradés de la maquette (<c>&lt;defs&gt;</c>) utilisés par les dessins.</summary>
internal static class ArtPalette
{
    public static readonly Fill RenderX = Fill.Box(0, 0, 1, 1, (0, "#f6f8fc"), (1, "#c3cede"));
    public static readonly Fill RenderY = Fill.Box(0, 0, 1, 1, (0, "#aab7cd"), (1, "#7c8ba6"));
    public static readonly Fill Anthra = Fill.Box(0, 0, 1, 1, (0, "#4a566e"), (1, "#2b3448"));
    public static readonly Fill Wood = Fill.Box(0, 0, 1, 1, (0, "#dba46c"), (1, "#8f5a30"));
    public static readonly Fill WoodD = Fill.Box(0, 0, 1, 1, (0, "#a06c3f"), (1, "#5f3a1e"));
    public static readonly Fill Deck = Fill.Box(0, 0, 1, 1, (0, "#c9a278"), (1, "#94704c"));
    public static readonly Fill RoofFlat = Fill.Box(0, 0, 1, 1, (0, "#39445c"), (1, "#1b2334"));
    public static readonly Fill CreamX = Fill.Box(0, 0, 1, 1, (0, "#fff6e8"), (1, "#ddcdb3"));
    public static readonly Fill CreamY = Fill.Box(0, 0, 1, 1, (0, "#c9b89d"), (1, "#9c8b72"));
    public static readonly Fill Slate = Fill.Box(0, 0, 1, 1, (0, "#5a6780"), (1, "#2a3346"));
    public static readonly Fill WinLit = Fill.Box(0, 0, 0, 1, (0, "#fff2c4"), (1, "#ffb445"));
    public static readonly Fill Cell = Fill.Box(0, 0, 1, 1, (0, "#14305e"), (1, "#060f24"));
    public static readonly Fill CellLit = Fill.Box(0, 0, 1, 1, (0, "#6fc1ff"), (.6f, "#2a6be6"), (1, "#1a3f9e"));
    public static readonly Fill Steel = Fill.User(0, 0, 0, 160, (0, "#f4f7fc"), (1, "#7486a6"));
    public static readonly Fill AluS = Fill.User(120, 0, 200, 170, (0, "#eef2f8"), (1, "#6f7f9b"));
}
