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
            {"solar":{{Entity("sensor.pv")}},"grid_mode":"two","grid_import":{{Entity("sensor.imp")}},"grid_export":{{Entity("sensor.exp")}},
             "grid":{{Entity("sensor.ignored")}},"home":{{Entity("sensor.pv")}},"battery_mode":"one","battery":{{Entity("sensor.bat")}},
             "battery_charge":{{Entity("sensor.ignored2")}}}
            """;

        Assert.True(EnergyConfig.TryRead(Settings(json), out var config, out _));

        Assert.Equal(["sensor.bat", "sensor.exp", "sensor.imp", "sensor.pv"], config!.EntityIds.Order().ToArray());
    }
}
