using System.Text.Json;

namespace HomeAssistant.Tests;

public sealed class EnergyConfigTests
{
    private static JsonElement Settings(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Entity(string id) => $$"""{"id":"{{id}}","name":"x"}""";

    [Fact]
    public void TryRead_MissingGridSensor_NamesTheField()
    {
        var ok = EnergyConfig.TryRead(Settings($$"""{"solar":{{Entity("sensor.pv")}}}"""), out var config, out var problem);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Contains("réseau", problem);
    }

    [Fact]
    public void TryRead_MissingSolar_NamesProduction()
    {
        var ok = EnergyConfig.TryRead(Settings("{}"), out _, out var problem);

        Assert.False(ok);
        Assert.Contains("production", problem);
    }

    [Fact]
    public void TryRead_BatteryOneWithoutSensor_NamesTheField()
    {
        var json = $$"""{"solar":{{Entity("sensor.pv")}},"grid":{{Entity("sensor.grid")}},"battery_mode":"one"}""";

        var ok = EnergyConfig.TryRead(Settings(json), out var config, out var problem);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Contains("batterie", problem);
    }

    [Fact]
    public void TryRead_BatteryNone_IgnoresBatteryFields()
    {
        var json = $$"""
            {"solar":{{Entity("sensor.pv")}},"grid":{{Entity("sensor.grid")}},"battery_mode":"none",
             "battery":{{Entity("sensor.bat")}},"battery_inverted":true,"battery_soc":{{Entity("sensor.soc")}}}
            """;

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(BatteryMode.None, config!.Battery);
        Assert.Equal("", config.BatteryPower);
        Assert.False(config.BatteryInverted);
        Assert.Equal("", config.BatterySoc);
    }

    [Fact]
    public void TryRead_Defaults_TopModernOneNone()
    {
        var json = $$"""{"solar":{{Entity("sensor.pv")}},"grid":{{Entity("sensor.grid")}}}""";

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out var problem));

        Assert.Null(problem);
        Assert.Equal(LayoutKind.Top, config!.Layout);
        Assert.Equal(HouseStyle.Modern, config.House);
        Assert.False(config.GridTwo);
        Assert.False(config.GridInverted);
        Assert.Equal(BatteryMode.None, config.Battery);
        Assert.Equal("", config.Home);
    }

    [Fact]
    public void TryRead_BatteryTwo_TakesSocFromSoc2()
    {
        var json = $$"""
            {"solar":{{Entity("sensor.pv")}},"grid":{{Entity("sensor.grid")}},"battery_mode":"two",
             "battery_charge":{{Entity("sensor.c")}},"battery_discharge":{{Entity("sensor.d")}},
             "battery_soc":{{Entity("sensor.one")}},"battery_soc2":{{Entity("sensor.two")}}}
            """;

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal("sensor.two", config!.BatterySoc);
    }

    [Fact]
    public void TryRead_LayoutAndHouse_AreRead()
    {
        var json = $$"""{"layout":"left","house":"classic","solar":{{Entity("sensor.pv")}},"grid":{{Entity("sensor.grid")}}}""";

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(LayoutKind.Left, config!.Layout);
        Assert.Equal(HouseStyle.Classic, config.House);
    }

    [Fact]
    public void EntityIds_NoDuplicates_OnlyActiveMode()
    {
        var json = $$"""
            {"solar":{{Entity("sensor.pv")}},"grid_mode":"two","grid":{{Entity("sensor.imp")}},"grid_export":{{Entity("sensor.exp")}},
             "home":{{Entity("sensor.pv")}},"battery_mode":"one","battery":{{Entity("sensor.bat")}},
             "battery_charge":{{Entity("sensor.ignored2")}}}
            """;

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(["sensor.bat", "sensor.exp", "sensor.imp", "sensor.pv"], config!.EntityIds.Order().ToArray());
    }

    [Fact]
    public void TryRead_TwoSensors_GridIsThePurchaseSensor()
    {
        var json = $$"""
            {"solar":{{Entity("sensor.pv")}},"grid_mode":"two","grid":{{Entity("sensor.ignored")}},"grid_import":{{Entity("sensor.imp")}},
             "grid_export":{{Entity("sensor.exp")}}}
            """;

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal("sensor.ignored", config!.GridImport);
        Assert.Equal("", config.Grid);
        Assert.False(config.GridInverted);
    }

    [Fact]
    public void TryRead_TwoSensors_FallsBackOnLegacyGridImport()
    {
        var json = $$"""{"solar":{{Entity("sensor.pv")}},"grid_mode":"two","grid_import":{{Entity("sensor.imp")}},"grid_export":{{Entity("sensor.exp")}}}""";

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal("sensor.imp", config!.GridImport);
    }

    private static string Base => $$"""{"solar":{{Entity("sensor.pv")}},"grid":{{Entity("sensor.grid")}}""";

    [Fact]
    public void Car_ReadsAllFields()
    {
        var json = Base + $$""","car_mode":"one","car_power":{{Entity("sensor.car_power")}},"car_plug":{{Entity("binary_sensor.car_plug")}},"car_soc":{{Entity("sensor.car_soc")}},"car_color":"yellow"}""";

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.True(config!.HasCar);
        Assert.Equal("sensor.car_power", config.CarPower);
        Assert.Equal("binary_sensor.car_plug", config.CarPlug);
        Assert.Equal("sensor.car_soc", config.CarSoc);
        Assert.Equal(CarColor.Yellow, config.CarColor);
        Assert.Contains("sensor.car_power", config.EntityIds);
        Assert.Contains("binary_sensor.car_plug", config.EntityIds);
        Assert.Contains("sensor.car_soc", config.EntityIds);
    }

    [Fact]
    public void Car_WithoutPowerSensor_IsNotConfigured()
    {
        var ok = EnergyConfig.TryRead(Settings(Base + ""","car_mode":"one"}"""), out var config, out var problem);

        Assert.False(ok);
        Assert.Null(config);
        Assert.Equal("Choisis le capteur de puissance de recharge de la voiture dans les réglages de la case.", problem);
    }

    [Fact]
    public void Car_ModeNone_IgnoresLeftoverFields()
    {
        var json = Base + $$""","car_mode":"none","car_power":{{Entity("sensor.car_power")}}}""";

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.False(config!.HasCar);
        Assert.Equal("", config.CarPower);
        Assert.DoesNotContain(config.EntityIds, id => id.Contains("car"));
    }

    [Fact]
    public void Car_DefaultColor_IsBlue()
    {
        var json = Base + $$""","car_mode":"one","car_power":{{Entity("sensor.car_power")}}}""";

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(CarColor.Blue, config!.CarColor);
    }
}
