namespace HomeAssistant.Tests;

public sealed class DailyReaderTests
{
    private static HaState S(string id, string state, string? unit = null) => new(id, state, unit, null, null);

    private static DailyConfig Config(DailyValue value, ResalePrice mode = ResalePrice.None, double? typed = null, string priceSensor = "") =>
        new(DailyShape.Strip, [value], "sensor.conso", "sensor.prod", "sensor.imp", "sensor.exp", "sensor.cout", mode, typed, priceSensor);

    private static DailyItem ReadOne(DailyConfig config, params HaState[] states) =>
        DailyReader.Read(config, states.ToDictionary(s => s.EntityId)).Single();

    [Fact]
    public void Energy_IsOneDecimal_WithComma()
    {
        var item = ReadOne(Config(DailyValue.Consumption), S("sensor.conso", "24.82", "kWh"));

        Assert.Equal(("24,8", "kWh"), (item.Number, item.Unit));
    }

    [Theory]
    [InlineData("7800", "Wh")]
    [InlineData("0.0078", "MWh")]
    [InlineData("7.8", "KWH")]
    public void Energy_ConvertsWhAndMWh(string state, string unit)
    {
        var item = ReadOne(Config(DailyValue.Consumption), S("sensor.conso", state, unit));

        Assert.Equal(("7,8", "kWh"), (item.Number, item.Unit));
    }

    [Fact]
    public void Energy_NoUnit_IsKwh()
    {
        var item = ReadOne(Config(DailyValue.Consumption), S("sensor.conso", "7.8"));

        Assert.Equal(("7,8", "kWh"), (item.Number, item.Unit));
    }

    [Fact]
    public void Energy_UnknownUnit_IsReadAsKwh()
    {
        var item = ReadOne(Config(DailyValue.Consumption), S("sensor.conso", "7.8", "W"));

        Assert.Equal(("7,8", "kWh"), (item.Number, item.Unit));
    }

    [Fact]
    public void Cost_IsTwoDecimals()
    {
        var item = ReadOne(Config(DailyValue.Cost), S("sensor.cout", "2.914", "€"));

        Assert.Equal(("2,91", "€"), (item.Number, item.Unit));
    }

    [Fact]
    public void Cost_Eur_IsWrittenEuro()
    {
        var item = ReadOne(Config(DailyValue.Cost), S("sensor.cout", "2.914", "EUR"));

        Assert.Equal(("2,91", "€"), (item.Number, item.Unit));
    }

    [Fact]
    public void Cost_OtherCurrency_IsKept()
    {
        var item = ReadOne(Config(DailyValue.Cost), S("sensor.cout", "2.9", "USD"));

        Assert.Equal(("2,90", "USD"), (item.Number, item.Unit));
    }

    [Fact]
    public void Cost_NoUnit_IsEuro()
    {
        var item = ReadOne(Config(DailyValue.Cost), S("sensor.cout", "2.9"));

        Assert.Equal(("2,90", "€"), (item.Number, item.Unit));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("nan")]
    [InlineData("Infinity")]
    public void Unusable_IsMissing(string state)
    {
        var item = ReadOne(Config(DailyValue.Cost), S("sensor.cout", state, "€"));

        Assert.Equal(("—", ""), (item.Number, item.Unit));
    }

    [Fact]
    public void Missing_WhenSensorIsAbsent()
    {
        var item = DailyReader.Read(Config(DailyValue.Consumption), new Dictionary<string, HaState>()).Single();

        Assert.Equal(("—", ""), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_TypedPrice()
    {
        var item = ReadOne(Config(DailyValue.Resale, ResalePrice.Typed, 0.13), S("sensor.exp", "1.6", "kWh"));

        Assert.Equal(("0,21", "€"), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_ConvertsExportUnit()
    {
        var item = ReadOne(Config(DailyValue.Resale, ResalePrice.Typed, 0.13), S("sensor.exp", "1600", "Wh"));

        Assert.Equal(("0,21", "€"), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_ZeroPrice_IsZero()
    {
        var item = ReadOne(Config(DailyValue.Resale, ResalePrice.Typed, 0), S("sensor.exp", "1.6", "kWh"));

        Assert.Equal(("0,00", "€"), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_TypedPriceInvalid_IsMissing()
    {
        var item = ReadOne(Config(DailyValue.Resale, ResalePrice.Typed, null), S("sensor.exp", "1.6", "kWh"));

        Assert.Equal(("—", ""), (item.Number, item.Unit));
    }

    [Theory]
    [InlineData("0.13", "€/kWh")]
    [InlineData("0.13", "EUR/kWh")]
    [InlineData("13", "c€/kWh")]
    [InlineData("0.13", null)]
    public void Resale_SensorPrice(string price, string? unit)
    {
        var config = Config(DailyValue.Resale, ResalePrice.Sensor, priceSensor: "sensor.prix");

        var item = ReadOne(config, S("sensor.exp", "1.6", "kWh"), S("sensor.prix", price, unit));

        Assert.Equal(("0,21", "€"), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_SensorOtherCurrency_IsKept()
    {
        var config = Config(DailyValue.Resale, ResalePrice.Sensor, priceSensor: "sensor.prix");

        var item = ReadOne(config, S("sensor.exp", "1.6", "kWh"), S("sensor.prix", "0.13", "USD/kWh"));

        Assert.Equal(("0,21", "USD"), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_SensorPriceWithAnotherTimeUnit_IsMissing()
    {
        var config = Config(DailyValue.Resale, ResalePrice.Sensor, priceSensor: "sensor.prix");

        var item = ReadOne(config, S("sensor.exp", "1.6", "kWh"), S("sensor.prix", "0.13", "€/MWh"));

        Assert.Equal(("—", ""), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_ExportUnavailable_IsMissing()
    {
        var config = Config(DailyValue.Resale, ResalePrice.Typed, 0.13);

        var item = ReadOne(config, S("sensor.exp", "unavailable", "kWh"));

        Assert.Equal(("—", ""), (item.Number, item.Unit));
    }

    [Fact]
    public void Resale_PriceUnavailable_IsMissing()
    {
        var config = Config(DailyValue.Resale, ResalePrice.Sensor, priceSensor: "sensor.prix");

        var item = ReadOne(config, S("sensor.exp", "1.6", "kWh"), S("sensor.prix", "unknown", "€/kWh"));

        Assert.Equal(("—", ""), (item.Number, item.Unit));
    }

    [Fact]
    public void Read_KeepsTheOrderOfValues()
    {
        var config = new DailyConfig(DailyShape.Strip, [DailyValue.Cost, DailyValue.Consumption], "sensor.conso", "", "", "", "sensor.cout",
            ResalePrice.None, null, "");
        var states = new Dictionary<string, HaState>
        {
            ["sensor.conso"] = S("sensor.conso", "7.8", "kWh"),
            ["sensor.cout"] = S("sensor.cout", "2.9", "€"),
        };

        var items = DailyReader.Read(config, states);

        Assert.Equal([DailyValue.Cost, DailyValue.Consumption], items.Select(item => item.Value).ToArray());
        Assert.Equal(["2,90", "7,8"], items.Select(item => item.Number).ToArray());
    }

    [Theory]
    [InlineData("Consumption", "Conso")]
    [InlineData("Production", "Prod.")]
    [InlineData("Import", "Import")]
    [InlineData("Export", "Export")]
    [InlineData("Cost", "Coût")]
    [InlineData("Resale", "Revente")]
    public void LabelOf_IsTheShortLabel(string value, string label) =>
        Assert.Equal(label, DailyReader.LabelOf(Enum.Parse<DailyValue>(value)));

    [Theory]
    [InlineData("Wh", false)]
    [InlineData("kwh", false)]
    [InlineData("MWh", false)]
    [InlineData(null, false)]
    [InlineData("W", true)]
    [InlineData("€", true)]
    public void IsUnknownEnergyUnit_IgnoresCase(string? unit, bool expected) =>
        Assert.Equal(expected, DailyReader.IsUnknownEnergyUnit(unit));
}
