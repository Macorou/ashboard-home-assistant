namespace HomeAssistant.Tests;

public sealed class EnergyFlowsTests
{
    [Fact]
    public void AuthorsCase_NoBattery()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 1124, Import: 0, Export: 352.38, Charge: 0, Discharge: 0, HomeSensor: null));

        Assert.Equal(771.62, flows.Home, 2);
        Assert.Equal(771.62, flows.SolarToHome, 2);
        Assert.Equal(352.38, flows.SolarToGrid, 2);
    }

    [Fact]
    public void Sun_ChargesBatteryThenExports()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 3420, Import: 0, Export: 660, Charge: 900, Discharge: 0, HomeSensor: 1860));

        Assert.Equal(1860, flows.SolarToHome, 2);
        Assert.Equal(900, flows.SolarToBattery, 2);
        Assert.Equal(660, flows.SolarToGrid, 2);
    }

    [Fact]
    public void Cloudy_BatteryAndGridFeedHome()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 980, Import: 440, Export: 0, Charge: 0, Discharge: 820, HomeSensor: null));

        Assert.Equal(2240, flows.Home, 2);
        Assert.Equal(980, flows.SolarToHome, 2);
        Assert.Equal(820, flows.BatteryToHome, 2);
        Assert.Equal(440, flows.GridToHome, 2);
    }

    [Fact]
    public void OffPeak_GridChargesBattery()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 0, Import: 2520, Export: 0, Charge: 2000, Discharge: 0, HomeSensor: null));

        Assert.Equal(520, flows.Home, 2);
        Assert.Equal(520, flows.GridToHome, 2);
        Assert.Equal(2000, flows.GridToBattery, 2);
    }

    [Fact]
    public void BatterySellsToGrid()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 0, Import: 0, Export: 700, Charge: 0, Discharge: 1000, HomeSensor: null));

        Assert.Equal(300, flows.Home, 2);
        Assert.Equal(300, flows.BatteryToHome, 2);
        Assert.Equal(700, flows.BatteryToGrid, 2);
    }

    [Fact]
    public void SmallFlows_AreZero()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 5, Import: 200, Export: 0, Charge: 0, Discharge: 0, HomeSensor: null));

        Assert.Equal(0, flows.SolarToHome, 2);
        Assert.Equal(200, flows.GridToHome, 2);
    }

    [Fact]
    public void SolarShare_IsHundredWhenSelfSufficient()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 2000, Import: 0, Export: 500, Charge: 0, Discharge: 0, HomeSensor: null));

        Assert.Equal(100, flows.SolarSharePercent, 2);
    }

    [Fact]
    public void HomeAndSolarShare_AreComputedBeforeNoiseFloor()
    {
        // Le soleil ne fournit que 5 W à une maison qui en consomme 205 : le flux solaire tombe sous le seuil, mais la part reste de 2,44 %.
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 5, Import: 200, Export: 0, Charge: 0, Discharge: 0, HomeSensor: null));

        Assert.Equal(205, flows.Home, 2);
        Assert.Equal(0, flows.SolarToHome, 2);
        Assert.Equal(2.44, flows.SolarSharePercent, 2);
    }

    [Fact]
    public void NoHome_ShareIsZero()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 0, Import: 0, Export: 0, Charge: 0, Discharge: 0, HomeSensor: null));

        Assert.Equal(0, flows.Home, 2);
        Assert.Equal(0, flows.SolarSharePercent, 2);
    }

    [Fact]
    public void NegativeHomeSensor_IsClampedToZero()
    {
        var flows = EnergyFlows.Compute(new EnergyInput(Solar: 1000, Import: 0, Export: 0, Charge: 0, Discharge: 0, HomeSensor: -50));

        Assert.Equal(0, flows.Home, 2);
        Assert.Equal(0, flows.SolarToHome, 2);
    }

    [Theory]
    [InlineData(-3.0, 0.0, 0.0, 0.0, 0.0, null)]
    [InlineData(2000.0, 0.0, 0.0, 0.0, 0.0, 100.0)]
    [InlineData(0.0, 300.0, 300.0, 0.0, 0.0, null)]
    public void Incoherent_NoNegativeFlow(double solar, double import, double export, double charge, double discharge, double? home)
    {
        var flows = EnergyFlows.Compute(new EnergyInput(solar, import, export, charge, discharge, home));

        Assert.True(flows.SolarToHome >= 0);
        Assert.True(flows.BatteryToHome >= 0);
        Assert.True(flows.GridToHome >= 0);
        Assert.True(flows.SolarToBattery >= 0);
        Assert.True(flows.GridToBattery >= 0);
        Assert.True(flows.SolarToGrid >= 0);
        Assert.True(flows.BatteryToGrid >= 0);
        Assert.True(flows.Home >= 0);

        var s = Math.Max(0, solar);
        var i = Math.Max(0, import);
        var e = Math.Max(0, export);
        var c = Math.Max(0, charge);
        var d = Math.Max(0, discharge);
        Assert.True(flows.SolarToHome + flows.SolarToBattery + flows.SolarToGrid <= s);
        Assert.True(flows.BatteryToHome + flows.BatteryToGrid <= d);
        Assert.True(flows.GridToHome + flows.GridToBattery <= i);
        Assert.True(flows.SolarToBattery + flows.GridToBattery <= c);
        Assert.True(flows.SolarToGrid + flows.BatteryToGrid <= e);
        Assert.True(flows.SolarToHome + flows.BatteryToHome + flows.GridToHome <= flows.Home);
    }

    [Fact]
    public void HomeToCar_OnlyWhenCharging()
    {
        Assert.Equal(7400, EnergyFlows.Compute(new EnergyInput(3000, 0, 0, 0, 0, null, Car: 7400)).HomeToCar);
        Assert.Equal(0, EnergyFlows.Compute(new EnergyInput(3000, 0, 0, 0, 0, null, Car: 40)).HomeToCar);
    }

    [Fact]
    public void Car_DoesNotChangeHome()
    {
        var without = EnergyFlows.Compute(new EnergyInput(3000, 5000, 0, 0, 0, null, Car: 0));
        var with = EnergyFlows.Compute(new EnergyInput(3000, 5000, 0, 0, 0, null, Car: 7400));

        Assert.Equal(without.Home, with.Home);
    }
}
