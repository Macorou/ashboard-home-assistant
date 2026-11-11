using System.Text.Json;

namespace HomeAssistant.Tests;

public sealed class DailyConfigTests
{
    private static readonly string[] SixValues = ["consumption", "production", "import", "export", "cost", "resale"];

    private static JsonElement Settings(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Entity(string id) => $$"""{"id":"{{id}}","name":"x"}""";

    [Fact]
    public void TryRead_Defaults_ToStripAndListedOrder()
    {
        var json = $$"""
            {"day_consumption":{{Entity("sensor.conso")}},"day_production":{{Entity("sensor.prod")}},
             "day_import":{{Entity("sensor.imp")}},"day_export":{{Entity("sensor.exp")}},"day_cost":{{Entity("sensor.cout")}}}
            """;

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(DailyShape.Strip, config!.Shape);
        Assert.Equal(
            new[] { DailyValue.Consumption, DailyValue.Production, DailyValue.Import, DailyValue.Export, DailyValue.Cost },
            config.Values.ToArray());
    }

    [Fact]
    public void TryRead_Defaults_PutCarAndWaterLast()
    {
        var json = $$"""
            {"day_water":{{Entity("sensor.eau")}},"day_car":{{Entity("sensor.voiture")}},"day_consumption":{{Entity("sensor.conso")}}}
            """;

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(new[] { DailyValue.Consumption, DailyValue.Car, DailyValue.Water }, config!.Values.ToArray());
        Assert.Equal(("sensor.voiture", "sensor.eau"), (config.Car, config.Water));
    }

    [Fact]
    public void TryRead_CarAndWater_FollowTheChosenOrder_AndAreRead()
    {
        var json = $$"""
            {"day_values":["water","cost","car"],"day_water":{{Entity("sensor.eau")}},"day_car":{{Entity("sensor.voiture")}},
             "day_cost":{{Entity("sensor.cout")}}}
            """;

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(new[] { DailyValue.Water, DailyValue.Cost, DailyValue.Car }, config!.Values.ToArray());
        Assert.Equal(new[] { "sensor.eau", "sensor.cout", "sensor.voiture" }, config.EntityIds.ToArray());
    }

    [Fact]
    public void TryRead_CarAndWaterWithoutSensor_AreDropped()
    {
        var json = $$"""{"day_values":["car","water","consumption"],"day_consumption":{{Entity("sensor.conso")}}}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(new[] { DailyValue.Consumption }, config!.Values.ToArray());
    }

    [Fact]
    public void TryRead_FollowsTheChosenOrder()
    {
        var json = $$"""
            {"day_values":["cost","import"],"day_cost":{{Entity("sensor.cout")}},"day_import":{{Entity("sensor.imp")}}}
            """;

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(new[] { DailyValue.Cost, DailyValue.Import }, config!.Values.ToArray());
    }

    [Fact]
    public void TryRead_ReadsListShape()
    {
        var json = $$"""{"day_shape":"list","day_consumption":{{Entity("sensor.conso")}}}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(DailyShape.List, config!.Shape);
    }

    [Fact]
    public void TryRead_ValueWithoutSensor_IsDropped()
    {
        var json = $$"""{"day_consumption":{{Entity("sensor.conso")}}}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(new[] { DailyValue.Consumption }, config!.Values.ToArray());
        Assert.DoesNotContain(DailyValue.Cost, config.Values);
    }

    [Fact]
    public void TryRead_NoDisplayableValue_IsNotConfigured()
    {
        var ok = DailyConfig.TryRead(Settings("{}"), out var config, out var problem);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Equal("Choisis au moins un capteur pour le Bilan du jour.", problem);
    }

    [Fact]
    public void TryRead_ResaleNeedsPriceMode()
    {
        var json = $$"""{"day_export":{{Entity("sensor.exp")}}}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.DoesNotContain(DailyValue.Resale, config!.Values);
    }

    [Fact]
    public void TryRead_TypedPrice_ShowsResale()
    {
        var json = $$"""{"day_export":{{Entity("sensor.exp")}},"day_price_mode":"typed","day_price":"0,13"}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Contains(DailyValue.Resale, config!.Values);
        Assert.Equal(ResalePrice.Typed, config.PriceMode);
        Assert.Equal(0.13, config.TypedPrice);
    }

    [Fact]
    public void TryRead_TypedPriceEmpty_StillShowsResale()
    {
        var json = $$"""{"day_export":{{Entity("sensor.exp")}},"day_price_mode":"typed"}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Contains(DailyValue.Resale, config!.Values);
        Assert.Null(config.TypedPrice);
    }

    [Fact]
    public void TryRead_SensorPriceWithoutSensor_DropsResale()
    {
        var json = $$"""{"day_export":{{Entity("sensor.exp")}},"day_price_mode":"sensor"}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.DoesNotContain(DailyValue.Resale, config!.Values);
    }

    [Fact]
    public void TryRead_SensorPrice_ShowsResaleAndItsSensor()
    {
        var json = $$"""{"day_export":{{Entity("sensor.exp")}},"day_price_mode":"sensor","day_price_sensor":{{Entity("sensor.prix")}}}""";

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Contains(DailyValue.Resale, config!.Values);
        Assert.Equal("sensor.prix", config.PriceSensor);
        Assert.Equal(new[] { "sensor.exp", "sensor.prix" }, config.EntityIds.ToArray());
    }

    [Fact]
    public void EntityIds_AreDistinct_AndOnlyForShownValues()
    {
        var json = $$"""
            {"day_consumption":{{Entity("sensor.a")}},"day_export":{{Entity("sensor.a")}},"day_cost":{{Entity("sensor.cout")}},
             "day_values":["consumption","export"]}
            """;

        Assert.True(DailyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(new[] { "sensor.a" }, config!.EntityIds.ToArray());
    }

    [Theory]
    [InlineData("0,13", 0.13)]
    [InlineData("0.13", 0.13)]
    [InlineData("0", 0.0)]
    public void ParsePrice_ReadsPlainNonNegativeNumbers(string text, double expected)
    {
        Assert.Equal(expected, DailyConfig.ParsePrice(text));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1,2,3")]
    [InlineData("1.234,5")]
    [InlineData("")]
    public void ParsePrice_RejectsEverythingElse(string text)
    {
        Assert.Null(DailyConfig.ParsePrice(text));
    }

    [Fact]
    public void List_IgnoresBadEntries()
    {
        var settings = Settings("""{"day_values":["cost",3,"cost","nope","import"]}""");

        var values = SettingsReader.List(settings, "day_values", SixValues, SixValues);

        Assert.Equal(new[] { "cost", "import" }, values.ToArray());
    }

    [Fact]
    public void List_NotAnArray_UsesFallback()
    {
        var settings = Settings("""{"day_values":"cost"}""");

        var values = SettingsReader.List(settings, "day_values", SixValues, SixValues);

        Assert.Equal(SixValues, values.ToArray());
    }

    [Fact]
    public void List_EmptyArray_MeansNothing()
    {
        var json = $$"""{"day_values":[],"day_consumption":{{Entity("sensor.conso")}},"day_cost":{{Entity("sensor.cout")}}}""";

        var ok = DailyConfig.TryRead(Settings(json), out var config, out var problem);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Equal("Choisis au moins un capteur pour le Bilan du jour.", problem);
    }
}
