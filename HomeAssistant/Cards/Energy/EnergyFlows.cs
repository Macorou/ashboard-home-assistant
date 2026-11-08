namespace HomeAssistant;

/// <summary>Les grandeurs mesurées, en W. Une valeur négative est ramenée à 0 au calcul.</summary>
internal readonly record struct EnergyInput(double Solar, double Import, double Export, double Charge, double Discharge, double? HomeSensor);

/// <summary>
/// Répartition des flux d'énergie entre panneaux, maison, batterie et réseau (spec § 7), en W. Le soleil sert la maison, puis la batterie, et
/// le reste part au réseau.
/// </summary>
internal sealed record EnergyFlows(double SolarToHome, double BatteryToHome, double GridToHome, double SolarToBattery,
    double GridToBattery, double SolarToGrid, double BatteryToGrid, double Home, double SolarSharePercent)
{
    /// <summary>Sous ce seuil, un flux vaut 0 : il ne doit pas animer le bruit de mesure.</summary>
    internal const double NoiseFloorWatts = 10;

    internal static EnergyFlows Compute(EnergyInput input)
    {
        var solar = Clamp(input.Solar);
        var import = Clamp(input.Import);
        var export = Clamp(input.Export);
        var charge = Clamp(input.Charge);
        var discharge = Clamp(input.Discharge);
        var home = input.HomeSensor is { } sensor
            ? Clamp(sensor)
            : Math.Max(0, solar + import - export + discharge - charge);

        // Tous les flux sont calculés sans seuil : le seuil ne s'applique qu'à la fin.
        var solarToHome = Math.Min(solar, home);
        var batteryToHome = Math.Min(discharge, home - solarToHome);
        var gridToHome = Math.Min(import, home - solarToHome - batteryToHome);
        var solarToBattery = Math.Min(solar - solarToHome, charge);
        var gridToBattery = Math.Min(import - gridToHome, charge - solarToBattery);
        var solarToGrid = Math.Min(export, solar - solarToHome - solarToBattery);
        var batteryToGrid = Math.Min(export - solarToGrid, discharge - batteryToHome);
        var solarSharePercent = home > 0 ? solarToHome / home * 100 : 0;

        return new EnergyFlows(
            SolarToHome: BelowNoise(solarToHome),
            BatteryToHome: BelowNoise(batteryToHome),
            GridToHome: BelowNoise(gridToHome),
            SolarToBattery: BelowNoise(solarToBattery),
            GridToBattery: BelowNoise(gridToBattery),
            SolarToGrid: BelowNoise(solarToGrid),
            BatteryToGrid: BelowNoise(batteryToGrid),
            Home: home,
            SolarSharePercent: solarSharePercent);
    }

    private static double Clamp(double watts) => Math.Max(0, watts);

    private static double BelowNoise(double watts) => watts < NoiseFloorWatts ? 0 : watts;
}
