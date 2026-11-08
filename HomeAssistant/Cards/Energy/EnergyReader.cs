using System.Globalization;

namespace HomeAssistant;

/// <summary>Ce qu'on retient d'une lecture de tous les capteurs : les entrées du calcul et ce qui manque.</summary>
internal sealed record EnergySnapshot(EnergyInput Input, double? BatterySocPercent, bool BatteryPowerKnown, string? UnavailableGridEntity);

/// <summary>Transforme les états Home Assistant en entrées du calcul des flux (spec § 6).</summary>
internal static class EnergyReader
{
    /// <summary>La puissance en W ; null si l'état n'est pas un nombre fini (<c>unavailable</c>, <c>unknown</c>, vide, texte, <c>NaN</c>).</summary>
    internal static double? Watts(HaState? state)
    {
        if (!TryNumber(state, out var value))
            return null;
        return string.Equals(state!.Unit?.Trim(), "kW", StringComparison.OrdinalIgnoreCase) ? value * 1000 : value;
    }

    /// <summary>Le niveau en %, borné à 0..100 ; null si l'état n'est pas un nombre fini.</summary>
    internal static double? Percent(HaState? state) =>
        TryNumber(state, out var value) ? Math.Clamp(value, 0, 100) : null;

    internal static EnergySnapshot Combine(EnergyConfig config, IReadOnlyDictionary<string, HaState> states)
    {
        double? Read(string id) => id.Length > 0 && states.TryGetValue(id, out var state) ? Watts(state) : null;

        var solar = Read(config.Solar) ?? 0;

        double import, export;
        string? unavailableGrid = null;
        if (config.GridTwo)
        {
            var imported = Read(config.GridImport);
            var exported = Read(config.GridExport);
            unavailableGrid = imported is null ? config.GridImport : exported is null ? config.GridExport : null;
            import = imported ?? 0;
            export = exported ?? 0;
        }
        else
        {
            var net = Read(config.Grid);
            if (net is null)
                unavailableGrid = config.Grid;
            var toGrid = config.GridInverted ? net ?? 0 : -(net ?? 0);
            import = Math.Max(0, -toGrid);
            export = Math.Max(0, toGrid);
        }

        double charge = 0, discharge = 0;
        var batteryKnown = false;
        switch (config.Battery)
        {
            case BatteryMode.One:
                var power = Read(config.BatteryPower);
                batteryKnown = power is not null;
                var discharging = config.BatteryInverted ? -(power ?? 0) : power ?? 0;
                discharge = Math.Max(0, discharging);
                charge = Math.Max(0, -discharging);
                break;
            case BatteryMode.Two:
                var charging = Read(config.BatteryCharge);
                var discharged = Read(config.BatteryDischarge);
                batteryKnown = charging is not null && discharged is not null;
                charge = charging ?? 0;
                discharge = discharged ?? 0;
                break;
        }

        double? soc = null;
        if (config.Battery != BatteryMode.None && config.BatterySoc.Length > 0 && states.TryGetValue(config.BatterySoc, out var socState))
            soc = Percent(socState);

        return new EnergySnapshot(new EnergyInput(solar, import, export, charge, discharge, Read(config.Home)), soc, batteryKnown, unavailableGrid);
    }

    private static bool TryNumber(HaState? state, out double value)
    {
        value = 0;
        return state is not null
            && double.TryParse(state.State.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}
