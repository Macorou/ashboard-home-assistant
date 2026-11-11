using System.Globalization;

namespace HomeAssistant;

/// <summary>Une valeur du Bilan du jour, prête à afficher : le nombre écrit avec une virgule et son unité. Une valeur manquante a <c>Number</c> égal à <see cref="DailyReader.Missing"/> et une unité vide.</summary>
internal sealed record DailyItem(DailyValue Value, string Number, string Unit);

/// <summary>Transforme les états Home Assistant en valeurs affichables du Bilan du jour.</summary>
internal static class DailyReader
{
    /// <summary>Affiché à la place d'une valeur qui manque ou qui n'est pas un nombre.</summary>
    internal const string Missing = "—";

    private const string Euro = "€";

    private const string Litres = "L";

    private static readonly string[] EnergyUnits = ["Wh", "kWh", "MWh"];

    private static readonly string[] CubicMetres = ["m³", "m3"];

    /// <summary>Lit une valeur par réglage, dans l'ordre de <c>config.Values</c>.</summary>
    internal static IReadOnlyList<DailyItem> Read(DailyConfig config, IReadOnlyDictionary<string, HaState> states)
    {
        var items = new List<DailyItem>(config.Values.Count);
        foreach (var value in config.Values)
        {
            items.Add(ReadValue(config, value, states));
        }
        return items;
    }

    /// <summary>Le libellé court d'une valeur, affiché sous son nombre.</summary>
    internal static string LabelOf(DailyValue value) => value switch
    {
        DailyValue.Consumption => "Conso",
        DailyValue.Production => "Prod.",
        DailyValue.Import => "Import",
        DailyValue.Export => "Export",
        DailyValue.Cost => "Coût",
        DailyValue.Resale => "Revente",
        DailyValue.Car => "Voiture",
        DailyValue.Water => "Eau",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Vrai pour une unité d'énergie non vide qui n'est ni <c>Wh</c>, ni <c>kWh</c>, ni <c>MWh</c>, casse ignorée.</summary>
    internal static bool IsUnknownEnergyUnit(string? unit)
    {
        var trimmed = unit?.Trim();
        return !string.IsNullOrEmpty(trimmed) && !EnergyUnits.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Vrai pour une unité de volume non vide qui n'est ni <c>L</c>, ni <c>m³</c> (ou <c>m3</c>), casse ignorée.</summary>
    internal static bool IsUnknownWaterUnit(string? unit)
    {
        var trimmed = unit?.Trim();
        return !string.IsNullOrEmpty(trimmed) && !string.Equals(trimmed, Litres, StringComparison.OrdinalIgnoreCase)
            && !CubicMetres.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }

    private static DailyItem ReadValue(DailyConfig config, DailyValue value, IReadOnlyDictionary<string, HaState> states) => value switch
    {
        DailyValue.Consumption => Energy(value, StateOf(config.Consumption, states)),
        DailyValue.Production => Energy(value, StateOf(config.Production, states)),
        DailyValue.Import => Energy(value, StateOf(config.Import, states)),
        DailyValue.Export => Energy(value, StateOf(config.Export, states)),
        DailyValue.Cost => Cost(StateOf(config.Cost, states)),
        DailyValue.Resale => Resale(config, states),
        DailyValue.Car => Energy(value, StateOf(config.Car, states)),
        DailyValue.Water => Water(StateOf(config.Water, states)),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static HaState? StateOf(string id, IReadOnlyDictionary<string, HaState> states) =>
        id.Length > 0 && states.TryGetValue(id, out var state) ? state : null;

    private static DailyItem Energy(DailyValue value, HaState? state)
    {
        var kilowattHours = KilowattHours(state);
        return kilowattHours is null ? Absent(value) : new DailyItem(value, Format(kilowattHours.Value, "F1"), "kWh");
    }

    private static DailyItem Cost(HaState? state)
    {
        var amount = EnergyReader.Number(state);
        return amount is null ? Absent(DailyValue.Cost) : new DailyItem(DailyValue.Cost, Format(amount.Value, "F2"), Currency(state!.Unit));
    }

    /// <summary>L'eau en litres entiers : les m³ sont convertis, une unité absente ou inconnue est lue comme des litres.</summary>
    private static DailyItem Water(HaState? state)
    {
        var value = EnergyReader.Number(state);
        if (value is null)
        {
            return Absent(DailyValue.Water);
        }
        var litres = CubicMetres.Contains(state!.Unit?.Trim(), StringComparer.OrdinalIgnoreCase) ? value.Value * 1000 : value.Value;
        return new DailyItem(DailyValue.Water, Format(Math.Round(litres, MidpointRounding.AwayFromZero), "F0"), Litres);
    }

    /// <summary>Le gain : l'export en kWh multiplié par le prix, dans la devise du prix.</summary>
    private static DailyItem Resale(DailyConfig config, IReadOnlyDictionary<string, HaState> states)
    {
        var kilowattHours = KilowattHours(StateOf(config.Export, states));
        var rate = config.PriceMode == ResalePrice.Typed ? TypedPrice(config.TypedPrice) : SensorPrice(StateOf(config.PriceSensor, states));
        if (kilowattHours is not { } kwh || rate is not { } price)
        {
            return Absent(DailyValue.Resale);
        }
        return new DailyItem(DailyValue.Resale, Format(kwh * price.PerKwh, "F2"), price.Currency);
    }

    /// <summary>Le prix tapé, toujours en euros par kWh.</summary>
    private static (double PerKwh, string Currency)? TypedPrice(double? price) => price is { } perKwh ? (perKwh, Euro) : null;

    /// <summary>L'énergie en kWh : Wh et MWh sont convertis, une unité absente ou inconnue est lue comme des kWh.</summary>
    private static double? KilowattHours(HaState? state)
    {
        var value = EnergyReader.Number(state);
        if (value is null)
        {
            return null;
        }
        var unit = state!.Unit?.Trim();
        return string.Equals(unit, "Wh", StringComparison.OrdinalIgnoreCase) ? value / 1000
            : string.Equals(unit, "MWh", StringComparison.OrdinalIgnoreCase) ? value * 1000
            : value;
    }

    /// <summary>
    /// Le prix en euros par kWh et sa devise, lus sur le capteur. Sans unité, c'est des €/kWh ; <c>c€/kWh</c> est divisé par 100.
    /// Une autre unité que <c>/kWh</c> est écartée, faute de savoir la convertir.
    /// </summary>
    private static (double PerKwh, string Currency)? SensorPrice(HaState? state)
    {
        var price = EnergyReader.Number(state);
        if (price is null)
        {
            return null;
        }
        var unit = state!.Unit?.Trim() ?? "";
        if (unit.Length == 0)
        {
            return (price.Value, Euro);
        }
        if (!unit.EndsWith("/kWh", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var currency = unit[..^"/kWh".Length].Trim();
        return string.Equals(currency, "c€", StringComparison.OrdinalIgnoreCase)
            ? (price.Value / 100, Euro)
            : (price.Value, Currency(currency));
    }

    /// <summary>La devise écrite : <c>EUR</c> devient <c>€</c>, une unité absente est l'euro, les autres sont gardées telles quelles.</summary>
    private static string Currency(string? unit)
    {
        var trimmed = unit?.Trim() ?? "";
        return trimmed.Length == 0 || string.Equals(trimmed, "EUR", StringComparison.OrdinalIgnoreCase) ? Euro : trimmed;
    }

    private static DailyItem Absent(DailyValue value) => new(value, Missing, "");

    private static string Format(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
}
