using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HomeAssistant;

internal enum DailyValue { Consumption, Production, Import, Export, Cost, Resale }

internal enum DailyShape { Strip, List }

internal enum ResalePrice { None, Typed, Sensor }

/// <summary>
/// Les réglages de la carte « Bilan du jour ». <c>Values</c> ne garde que les valeurs affichables, dans l'ordre choisi : une valeur sans capteur
/// est écartée, et « revente » demande un capteur d'export et un mode de prix. Les champs qui ne concernent pas le mode de prix choisi sont vides
/// ou nuls, comme dans <see cref="EnergyConfig"/>.
/// </summary>
internal sealed record DailyConfig(DailyShape Shape, IReadOnlyList<DailyValue> Values, string Consumption, string Production,
    string Import, string Export, string Cost, ResalePrice PriceMode, double? TypedPrice, string PriceSensor)
{
    private static readonly string[] ValueKeys = ["consumption", "production", "import", "export", "cost", "resale"];

    private static readonly Regex PricePattern = new(@"^[0-9]+([.,][0-9]+)?\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Lit les réglages. Si rien n'est affichable, <paramref name="config"/> est null et <paramref name="problem"/> dit quoi choisir.
    /// </summary>
    internal static bool TryRead(JsonElement settings, out DailyConfig? config, out string? problem)
    {
        config = null;
        problem = null;

        var shape = SettingsReader.Choice(settings, "day_shape", "strip", "strip", "list") == "list" ? DailyShape.List : DailyShape.Strip;

        var consumption = SettingsReader.EntityId(settings, "day_consumption");
        var production = SettingsReader.EntityId(settings, "day_production");
        var import = SettingsReader.EntityId(settings, "day_import");
        var export = SettingsReader.EntityId(settings, "day_export");
        var cost = SettingsReader.EntityId(settings, "day_cost");

        var priceMode = SettingsReader.Choice(settings, "day_price_mode", "none", "none", "typed", "sensor") switch
        {
            "typed" => ResalePrice.Typed,
            "sensor" => ResalePrice.Sensor,
            _ => ResalePrice.None,
        };
        double? typedPrice = null;
        var priceSensor = "";
        if (priceMode == ResalePrice.Typed)
        {
            typedPrice = ParsePrice(SettingsReader.Text(settings, "day_price"));
        }
        else if (priceMode == ResalePrice.Sensor)
        {
            priceSensor = SettingsReader.EntityId(settings, "day_price_sensor");
        }

        var resaleReady = export.Length > 0 && (priceMode == ResalePrice.Typed || (priceMode == ResalePrice.Sensor && priceSensor.Length > 0));

        var values = new List<DailyValue>();
        foreach (var key in SettingsReader.List(settings, "day_values", ValueKeys, ValueKeys))
        {
            DailyValue? shown = key switch
            {
                "consumption" when consumption.Length > 0 => DailyValue.Consumption,
                "production" when production.Length > 0 => DailyValue.Production,
                "import" when import.Length > 0 => DailyValue.Import,
                "export" when export.Length > 0 => DailyValue.Export,
                "cost" when cost.Length > 0 => DailyValue.Cost,
                "resale" when resaleReady => DailyValue.Resale,
                _ => null,
            };
            if (shown is { } value)
            {
                values.Add(value);
            }
        }

        if (values.Count == 0)
        {
            problem = "Choisis au moins un capteur pour le Bilan du jour.";
            return false;
        }

        config = new DailyConfig(shape, values, consumption, production, import, export, cost, priceMode, typedPrice, priceSensor);
        return true;
    }

    /// <summary>
    /// Le prix saisi en euros par kWh (virgule ou point), ou null s'il est vide, négatif ou mal formé. Accepte uniquement des chiffres ASCII
    /// pour que la lecture ne lève jamais d'exception.
    /// </summary>
    internal static double? ParsePrice(string text)
    {
        if (!PricePattern.IsMatch(text))
        {
            return null;
        }
        return double.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
            && double.IsFinite(price) ? price : null;
    }

    /// <summary>Les capteurs à relire, sans doublon ni vide ; seulement ceux des valeurs affichées.</summary>
    internal IReadOnlyCollection<string> EntityIds =>
        Values.SelectMany(value => value == DailyValue.Resale && PriceMode == ResalePrice.Sensor
                ? new[] { Export, PriceSensor }
                : new[] { SensorOf(value) })
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private string SensorOf(DailyValue value) => value switch
    {
        DailyValue.Consumption => Consumption,
        DailyValue.Production => Production,
        DailyValue.Import => Import,
        DailyValue.Export or DailyValue.Resale => Export,
        _ => Cost,
    };
}
