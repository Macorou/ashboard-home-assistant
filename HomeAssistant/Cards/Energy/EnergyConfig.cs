using System.Text.Json;

namespace HomeAssistant;

internal enum LayoutKind { Top, Left, Bottom }

internal enum HouseStyle { Modern, Classic }

internal enum BatteryMode { None, One, Two }

/// <summary>
/// Les réglages de la carte d'énergie (spec § 5). Les champs qui ne concernent pas le mode choisi (un seul ou deux capteurs, batterie ou non)
/// sont toujours vides ou faux, quoi qu'il reste dans <c>layout.json</c>.
/// </summary>
internal sealed record EnergyConfig(LayoutKind Layout, HouseStyle House, string Solar, bool GridTwo, string Grid, bool GridInverted,
    string GridImport, string GridExport, string Home, BatteryMode Battery, string BatteryPower, bool BatteryInverted,
    string BatteryCharge, string BatteryDischarge, string BatterySoc)
{
    /// <summary>
    /// Lit les réglages. Si un champ requis manque, <paramref name="config"/> est null et <paramref name="problem"/> est un message de statut
    /// qui dit quel champ remplir.
    /// </summary>
    internal static bool TryRead(JsonElement settings, out EnergyConfig? config, out string? problem)
    {
        config = null;
        problem = null;

        var layout = SettingsReader.Choice(settings, "layout", "top", "top", "left", "bottom") switch
        {
            "left" => LayoutKind.Left,
            "bottom" => LayoutKind.Bottom,
            _ => LayoutKind.Top,
        };
        var house = SettingsReader.Choice(settings, "house", "modern", "modern", "classic") == "classic" ? HouseStyle.Classic : HouseStyle.Modern;

        var solar = SettingsReader.EntityId(settings, "solar");
        if (solar.Length == 0)
        {
            problem = "Choisis le capteur de production solaire dans les réglages de la case.";
            return false;
        }

        var gridTwo = SettingsReader.Choice(settings, "grid_mode", "one", "one", "two") == "two";
        string grid = "", gridImport = "", gridExport = "";
        var gridInverted = false;
        if (gridTwo)
        {
            // « grid » est le capteur d'achat affiché à l'écran ; « grid_import » est l'ancien champ, lu pour les cases déjà configurées.
            gridImport = SettingsReader.EntityId(settings, "grid");
            if (gridImport.Length == 0)
            {
                gridImport = SettingsReader.EntityId(settings, "grid_import");
            }

            gridExport = SettingsReader.EntityId(settings, "grid_export");
            if (gridImport.Length == 0)
            {
                problem = "Choisis le capteur réseau, celui de l'achat, dans les réglages de la case.";
                return false;
            }
            if (gridExport.Length == 0)
            {
                problem = "Choisis le capteur de vente du réseau dans les réglages de la case.";
                return false;
            }
        }
        else
        {
            grid = SettingsReader.EntityId(settings, "grid");
            gridInverted = SettingsReader.Bool(settings, "grid_inverted", false);
            if (grid.Length == 0)
            {
                problem = "Choisis le capteur du réseau dans les réglages de la case.";
                return false;
            }
        }

        var battery = SettingsReader.Choice(settings, "battery_mode", "none", "none", "one", "two") switch
        {
            "one" => BatteryMode.One,
            "two" => BatteryMode.Two,
            _ => BatteryMode.None,
        };
        string batteryPower = "", batteryCharge = "", batteryDischarge = "", batterySoc = "";
        var batteryInverted = false;
        if (battery == BatteryMode.One)
        {
            batteryPower = SettingsReader.EntityId(settings, "battery");
            batteryInverted = SettingsReader.Bool(settings, "battery_inverted", false);
            batterySoc = SettingsReader.EntityId(settings, "battery_soc");
            if (batteryPower.Length == 0)
            {
                problem = "Choisis le capteur de puissance de la batterie dans les réglages de la case.";
                return false;
            }
        }
        else if (battery == BatteryMode.Two)
        {
            batteryCharge = SettingsReader.EntityId(settings, "battery_charge");
            batteryDischarge = SettingsReader.EntityId(settings, "battery_discharge");
            batterySoc = SettingsReader.EntityId(settings, "battery_soc2");
            if (batteryCharge.Length == 0)
            {
                problem = "Choisis le capteur de charge de la batterie dans les réglages de la case.";
                return false;
            }
            if (batteryDischarge.Length == 0)
            {
                problem = "Choisis le capteur de décharge de la batterie dans les réglages de la case.";
                return false;
            }
        }

        config = new EnergyConfig(layout, house, solar, gridTwo, grid, gridInverted, gridImport, gridExport,
            SettingsReader.EntityId(settings, "home"), battery, batteryPower, batteryInverted, batteryCharge, batteryDischarge, batterySoc);
        return true;
    }

    /// <summary>Les capteurs à relire, sans doublon ni vide ; seuls ceux du mode actif.</summary>
    internal IReadOnlyCollection<string> EntityIds =>
        new[] { Solar, Grid, GridImport, GridExport, Home, BatteryPower, BatteryCharge, BatteryDischarge, BatterySoc }
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
