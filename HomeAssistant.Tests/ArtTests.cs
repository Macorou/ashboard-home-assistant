using SkiaSharp;

namespace HomeAssistant.Tests;

public sealed class ArtTests
{
    // Boîte de chaque dessin dans les coordonnées de la maquette, plus 12 unités pour les traits épais et les halos.
    private const float Slack = 12;

    public static TheoryData<string, float, float> Boxes => new()
    {
        { nameof(ArtKind.HouseModern), 200, 180 },
        { nameof(ArtKind.HouseClassic), 200, 180 },
        { nameof(ArtKind.Panels), 200, 180 },
        { nameof(ArtKind.Pylon), 200, 180 },
        { nameof(ArtKind.Battery), 85, 145 },
    };

    [Fact]
    public void Iso_MatchesMockupFormula()
    {
        var iso = IsoPainter.Iso(10, 60, 100);

        var p = iso(2, 6, 9.5);
        Assert.Equal(60 + (2 - 6) * .866f * 10, p.X, 2);
        Assert.Equal(100 + (2 + 6) * .5f * 10 - 9.5f * 10, p.Y, 2);

        var q = IsoPainter.Iso(11.2f, 88, 73)(10, 0, 3);
        Assert.Equal(88 + 10 * .866f * 11.2f, q.X, 2);
        Assert.Equal(73 + 5 * 11.2f - 3 * 11.2f, q.Y, 2);
    }

    [Theory]
    [MemberData(nameof(Boxes))]
    public void EveryArt_DrawsSomething_WithinBox(string name, float width, float height)
    {
        using var cache = new ArtCache();
        var art = cache.Get(Enum.Parse<ArtKind>(name), 1);

        var ink = InkBounds(art.Dark);

        Assert.False(ink.IsEmpty);
        Assert.True(ink.Left - art.Origin.X >= -Slack, $"gauche {ink.Left - art.Origin.X}");
        Assert.True(ink.Top - art.Origin.Y >= -Slack, $"haut {ink.Top - art.Origin.Y}");
        Assert.True(ink.Right - art.Origin.X <= width + Slack, $"droite {ink.Right - art.Origin.X}");
        Assert.True(ink.Bottom - art.Origin.Y <= height + Slack, $"bas {ink.Bottom - art.Origin.Y}");
    }

    [Theory]
    [InlineData(nameof(ArtKind.HouseModern))]
    [InlineData(nameof(ArtKind.HouseClassic))]
    public void LitDiffersFromDark_ForArtsWithLights(string name)
    {
        using var cache = new ArtCache();
        var art = cache.Get(Enum.Parse<ArtKind>(name), 2);

        Assert.NotEqual(Pixels(art.Dark), Pixels(art.Lit));
    }

    [Fact]
    public void Panels_LitDiffersFromDark()
    {
        using var cache = new ArtCache();
        var art = cache.Get(ArtKind.Panels, 1);

        Assert.NotEqual(Pixels(art.Dark), Pixels(art.Lit));
    }

    [Fact]
    public void Pylon_LitIsEmpty()
    {
        using var cache = new ArtCache();
        var art = cache.Get(ArtKind.Pylon, 1);

        Assert.True(InkBounds(art.Lit).IsEmpty);
        Assert.False(InkBounds(art.Dark).IsEmpty);
        Assert.Equal(art.Dark.Width, art.Lit.Width);
        Assert.Equal(art.Dark.Height, art.Lit.Height);
    }

    [Fact]
    public void Cache_ReusesSameImage_ForSameScale()
    {
        using var cache = new ArtCache();

        var a = cache.Get(ArtKind.HouseModern, 1.5f);
        var b = cache.Get(ArtKind.HouseModern, 1.5f);
        var c = cache.Get(ArtKind.HouseModern, 2f);

        Assert.Same(a, b);
        Assert.NotSame(a, c);
        Assert.True(c.Dark.Width > a.Dark.Width);
        Assert.Equal(1.5f, a.Scale);
    }

    [Fact]
    public void Origin_LocatesLocalZero_AtAnyScale()
    {
        using var cache = new ArtCache();
        var one = cache.Get(ArtKind.Battery, 1);
        var four = cache.Get(ArtKind.Battery, 4);

        // Le bord droit et le bas de l'encre sont au même endroit local aux deux échelles, à un pixel de l'image à 1 près.
        var inkOne = InkBounds(one.Dark);
        var inkFour = InkBounds(four.Dark);
        Assert.InRange((inkFour.Right - four.Origin.X) / 4 - (inkOne.Right - one.Origin.X), -1.5f, 1.5f);
        Assert.InRange((inkFour.Bottom - four.Origin.Y) / 4 - (inkOne.Bottom - one.Origin.Y), -1.5f, 1.5f);
        Assert.InRange((inkFour.Left - four.Origin.X) / 4 - (inkOne.Left - one.Origin.X), -1.5f, 1.5f);
        Assert.InRange((inkFour.Top - four.Origin.Y) / 4 - (inkOne.Top - one.Origin.Y), -1.5f, 1.5f);
    }

    [Fact]
    public void Panels_PlaneCorners_FollowMockupProjection()
    {
        var iso = IsoPainter.Iso(15, 70, 66);

        var bottomLeft = PanelsArt.Plane(0, 0);
        var topRight = PanelsArt.Plane(1, 1);

        Assert.Equal(iso(0, 4.4, 0).X, bottomLeft.X, 3);
        Assert.Equal(iso(0, 4.4, 0).Y, bottomLeft.Y, 3);
        Assert.Equal(iso(9, 0, 3.4).X, topRight.X, 3);
        Assert.Equal(iso(9, 0, 3.4).Y, topRight.Y, 3);
    }

    [Fact]
    public void Battery_ExposesFiveSegments_BottomToTop()
    {
        Assert.Equal(5, BatteryArt.Segments.Length);
        Assert.All(BatteryArt.Segments, s => Assert.Equal(4, s.Length));
        for (var i = 1; i < 5; i++)
            Assert.True(BatteryArt.Segments[i][0].Y < BatteryArt.Segments[i - 1][0].Y);
        Assert.Equal(4, PylonArt.InsulatorCenters.Length);
        Assert.Equal(3.2f, PylonArt.InsulatorRadius);
    }

    [Fact]
    public void Art_WriteSamples()
    {
        var folder = Path.Combine(RepositoryRoot(), "gallery", "art");
        Directory.CreateDirectory(folder);
        using var cache = new ArtCache();

        foreach (var kind in Enum.GetValues<ArtKind>())
        {
            var art = cache.Get(kind, 3);
            var gap = 24;
            var info = new SKImageInfo(art.Dark.Width * 2 + gap * 3, art.Dark.Height + gap * 2);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColor.Parse("#06080e"));
            surface.Canvas.DrawImage(art.Dark, gap, gap);
            surface.Canvas.DrawImage(art.Dark, art.Dark.Width + gap * 2, gap);
            surface.Canvas.DrawImage(art.Lit, art.Dark.Width + gap * 2, gap);
            using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(folder, kind + ".png"));
            png.SaveTo(file);
        }

        Assert.Equal(5, Directory.GetFiles(folder, "*.png").Length);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HomeAssistant.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }

    private static SKRectI InkBounds(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0) continue;
                l = Math.Min(l, x);
                t = Math.Min(t, y);
                r = Math.Max(r, x + 1);
                b = Math.Max(b, y + 1);
            }
        }
        return r < 0 ? SKRectI.Empty : new SKRectI(l, t, r, b);
    }
}
