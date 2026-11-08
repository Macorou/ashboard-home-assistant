namespace HomeAssistant.Tests;

public sealed class EnergyReaderTests
{
    private static HaState S(string id, string state, string? unit = "W") => new(id, state, unit, null, null);

    private static Dictionary<string, HaState> States(params HaState[] states) => states.ToDictionary(s => s.EntityId);

    private static EnergyConfig Config(bool gridTwo = false, bool gridInverted = false, BatteryMode battery = BatteryMode.None,
        bool batteryInverted = false, string home = "", string soc = "") =>
        new(LayoutKind.Top, HouseStyle.Modern, "sensor.pv", gridTwo,
            gridTwo ? "" : "sensor.grid", gridInverted, gridTwo ? "sensor.imp" : "", gridTwo ? "sensor.exp" : "", home,
            battery, battery == BatteryMode.One ? "sensor.bat" : "", batteryInverted,
            battery == BatteryMode.Two ? "sensor.chg" : "", battery == BatteryMode.Two ? "sensor.dis" : "", soc);

    [Fact]
    public void Watts_ConvertsKilowatts()
    {
        Assert.Equal(1500, EnergyReader.Watts(S("sensor.a", "1.5", "kW")));
        Assert.Equal(1500, EnergyReader.Watts(S("sensor.a", "1500", "W")));
        Assert.Equal(42, EnergyReader.Watts(S("sensor.a", "42", "VA")));
        Assert.Equal(42, EnergyReader.Watts(S("sensor.a", "42", null)));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("12,5")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Watts_NonNumeric_IsNull(string text)
    {
        Assert.Null(EnergyReader.Watts(S("sensor.a", text)));
    }

    [Fact]
    public void Watts_NullState_IsNull() => Assert.Null(EnergyReader.Watts(null));

    [Fact]
    public void Combine_GridSigned_PositiveIsImport()
    {
        var plus = EnergyReader.Combine(Config(), States(S("sensor.pv", "0"), S("sensor.grid", "300")));
        var minus = EnergyReader.Combine(Config(), States(S("sensor.pv", "0"), S("sensor.grid", "-300")));

        Assert.Equal(300, plus.Input.Import);
        Assert.Equal(0, plus.Input.Export);
        Assert.Equal(0, minus.Input.Import);
        Assert.Equal(300, minus.Input.Export);
    }

    [Fact]
    public void Combine_GridInverted()
    {
        var snapshot = EnergyReader.Combine(Config(gridInverted: true), States(S("sensor.pv", "0"), S("sensor.grid", "300")));

        Assert.Equal(0, snapshot.Input.Import);
        Assert.Equal(300, snapshot.Input.Export);
    }

    [Fact]
    public void Combine_GridTwoSensors()
    {
        var snapshot = EnergyReader.Combine(Config(gridTwo: true), States(S("sensor.pv", "0"), S("sensor.imp", "0.2", "kW"), S("sensor.exp", "50")));

        Assert.Equal(200, snapshot.Input.Import);
        Assert.Equal(50, snapshot.Input.Export);
        Assert.Null(snapshot.UnavailableGridEntity);
    }

    [Fact]
    public void Combine_GridTwoSensors_OneUnavailable_NamesIt()
    {
        var snapshot = EnergyReader.Combine(Config(gridTwo: true), States(S("sensor.pv", "0"), S("sensor.imp", "10"), S("sensor.exp", "unavailable")));

        Assert.Equal("sensor.exp", snapshot.UnavailableGridEntity);
    }

    [Fact]
    public void Combine_BatterySigned_PositiveIsDischarge()
    {
        var plus = EnergyReader.Combine(Config(battery: BatteryMode.One), States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.bat", "400")));
        var minus = EnergyReader.Combine(Config(battery: BatteryMode.One), States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.bat", "-400")));

        Assert.Equal(400, plus.Input.Discharge);
        Assert.Equal(0, plus.Input.Charge);
        Assert.Equal(0, minus.Input.Discharge);
        Assert.Equal(400, minus.Input.Charge);
        Assert.True(plus.BatteryPowerKnown);
    }

    [Fact]
    public void Combine_BatteryInverted()
    {
        var snapshot = EnergyReader.Combine(Config(battery: BatteryMode.One, batteryInverted: true),
            States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.bat", "400")));

        Assert.Equal(400, snapshot.Input.Charge);
        Assert.Equal(0, snapshot.Input.Discharge);
    }

    [Fact]
    public void Combine_BatteryTwo()
    {
        var snapshot = EnergyReader.Combine(Config(battery: BatteryMode.Two),
            States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.chg", "0.6", "kW"), S("sensor.dis", "0")));

        Assert.Equal(600, snapshot.Input.Charge);
        Assert.Equal(0, snapshot.Input.Discharge);
        Assert.True(snapshot.BatteryPowerKnown);
    }

    [Fact]
    public void Combine_SolarUnavailable_IsZeroWithoutProblem()
    {
        var snapshot = EnergyReader.Combine(Config(), States(S("sensor.pv", "unavailable"), S("sensor.grid", "100")));

        Assert.Equal(0, snapshot.Input.Solar);
        Assert.Null(snapshot.UnavailableGridEntity);
    }

    [Fact]
    public void Combine_SolarMissingFromStates_IsZero()
    {
        var snapshot = EnergyReader.Combine(Config(), States(S("sensor.grid", "100")));

        Assert.Equal(0, snapshot.Input.Solar);
    }

    [Fact]
    public void Combine_GridUnavailable_NamesEntity()
    {
        var snapshot = EnergyReader.Combine(Config(), States(S("sensor.pv", "500"), S("sensor.grid", "unknown")));

        Assert.Equal("sensor.grid", snapshot.UnavailableGridEntity);
        Assert.Equal(0, snapshot.Input.Import);
        Assert.Equal(0, snapshot.Input.Export);
    }

    [Fact]
    public void Combine_HomeSensor_IsUsed()
    {
        var snapshot = EnergyReader.Combine(Config(home: "sensor.home"),
            States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.home", "1.2", "kW")));

        Assert.Equal(1200, snapshot.Input.HomeSensor);
    }

    [Fact]
    public void Combine_HomeSensorUnavailable_FallsBackToComputed()
    {
        var snapshot = EnergyReader.Combine(Config(home: "sensor.home"),
            States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.home", "unavailable")));

        Assert.Null(snapshot.Input.HomeSensor);
    }

    [Fact]
    public void Combine_BatteryUnavailable_ZeroAndNotKnown()
    {
        var snapshot = EnergyReader.Combine(Config(battery: BatteryMode.One),
            States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.bat", "unavailable")));

        Assert.Equal(0, snapshot.Input.Charge);
        Assert.Equal(0, snapshot.Input.Discharge);
        Assert.False(snapshot.BatteryPowerKnown);
    }

    [Fact]
    public void Combine_NoBattery_SocNull()
    {
        var snapshot = EnergyReader.Combine(Config(), States(S("sensor.pv", "0"), S("sensor.grid", "0")));

        Assert.Null(snapshot.BatterySocPercent);
        Assert.False(snapshot.BatteryPowerKnown);
    }

    [Fact]
    public void Combine_BatterySoc_IsRead()
    {
        var snapshot = EnergyReader.Combine(Config(battery: BatteryMode.One, soc: "sensor.soc"),
            States(S("sensor.pv", "0"), S("sensor.grid", "0"), S("sensor.bat", "0"), S("sensor.soc", "76.5", "%")));

        Assert.Equal(76.5, snapshot.BatterySocPercent);
    }

    [Fact]
    public void Percent_NonNumeric_IsNull()
    {
        Assert.Null(EnergyReader.Percent(S("sensor.soc", "unknown", "%")));
        Assert.Null(EnergyReader.Percent(S("sensor.soc", "NaN", "%")));
        Assert.Null(EnergyReader.Percent(null));
    }
}
