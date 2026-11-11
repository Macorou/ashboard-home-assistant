using Trofeo.ModuleKit;

namespace HomeAssistant.Tests;

public sealed class EntityCatalogTests
{
    private static HaState S(string id, string state, string? unit, string? name = null, string? deviceClass = null) =>
        new(id, state, unit, deviceClass, name);

    private static string[] Ids(IEnumerable<LookupResult> results) =>
        results.Select(r => r.Value.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public void Search_Power_KeepsOnlyWattSensors()
    {
        var states = new[]
        {
            S("sensor.pv", "1124", "W", "Solaire"),
            S("sensor.grid", "1.2", "kW", "Réseau"),
            S("sensor.total", "5000", "kWh", "Total"),
            S("sensor.temp", "21", "°C", "Salon"),
            S("binary_sensor.door", "on", null, "Porte"),
            S("light.lampe", "1000", "W", "Lampe"),
        };

        var results = EntityCatalog.Search(states, EntityKind.Power, "");

        Assert.Equal(["sensor.grid", "sensor.pv"], Ids(results));
    }

    [Fact]
    public void Search_FormatsLabelAndValue()
    {
        var results = EntityCatalog.Search([S("sensor.pv", "1124", "W", "Solaire")], EntityKind.Power, "");

        var result = Assert.Single(results);
        Assert.Equal("Solaire · 1124 W (sensor.pv)", result.Label);
        Assert.Equal("Solaire", result.Value.GetProperty("name").GetString());
        Assert.Equal("sensor.pv", result.Value.GetProperty("id").GetString());
    }

    [Fact]
    public void Search_UnavailableSensor_StaysListedWithItsState()
    {
        var results = EntityCatalog.Search([S("sensor.pv", "unavailable", "W", "Solaire")], EntityKind.Power, "");

        Assert.Equal("Solaire · unavailable (sensor.pv)", Assert.Single(results).Label);
    }

    [Fact]
    public void Search_Percent_PrefersBatteryDeviceClass()
    {
        var states = new[]
        {
            S("sensor.humidity", "40", "%", "Humidité", "humidity"),
            S("sensor.zbattery", "80", "%", "Zeta", "battery"),
            S("sensor.abattery", "70", "%", "Alpha"),
            S("sensor.power", "10", "W", "Puissance"),
        };

        var results = EntityCatalog.Search(states, EntityKind.Percent, "");

        Assert.Equal(["sensor.zbattery", "sensor.abattery", "sensor.humidity"], Ids(results));
    }

    [Fact]
    public void Search_IsAccentAndCaseInsensitive()
    {
        var states = new[]
        {
            S("sensor.maison", "300", "W", "Énergie maison"),
            S("sensor.pv", "100", "W", "solaire"),
        };

        Assert.Equal(["sensor.maison"], Ids(EntityCatalog.Search(states, EntityKind.Power, "energie")));
        Assert.Equal(["sensor.pv"], Ids(EntityCatalog.Search(states, EntityKind.Power, "SOLAIRE")));
    }

    [Fact]
    public void Search_MatchesEntityId()
    {
        var states = new[] { S("sensor.inverter_out", "100", "W", "Onduleur"), S("sensor.pv", "100", "W", "Solaire") };

        Assert.Equal(["sensor.inverter_out"], Ids(EntityCatalog.Search(states, EntityKind.Power, "inverter")));
    }

    [Fact]
    public void Search_PutsPrefixMatchesFirst()
    {
        var states = new[] { S("sensor.a", "1", "W", "Ma solaire"), S("sensor.b", "1", "W", "Solaire nord"), S("sensor.c", "1", "W", "Solaire est") };

        Assert.Equal(["sensor.c", "sensor.b", "sensor.a"], Ids(EntityCatalog.Search(states, EntityKind.Power, "solaire")));
    }

    [Fact]
    public void Search_IdPrefixIgnoresSensorDomain()
    {
        var states = new[] { S("sensor.b", "1", "W", "Alpha total"), S("sensor.total_x", "1", "W", "Zeta") };

        Assert.Equal(["sensor.total_x", "sensor.b"], Ids(EntityCatalog.Search(states, EntityKind.Power, "total")));
    }

    [Fact]
    public void Search_CapsAtFifty()
    {
        var states = Enumerable.Range(0, 80).Select(i => S($"sensor.s{i:00}", "1", "W", $"Capteur {i:00}")).ToArray();

        var results = EntityCatalog.Search(states, EntityKind.Power, "");

        Assert.Equal(50, results.Count);
        Assert.Equal("sensor.s00", Ids(results)[0]);
    }

    [Fact]
    public void Search_EmptyQuery_ListsAll()
    {
        var states = new[] { S("sensor.b", "1", "W", "B"), S("sensor.a", "1", "W", "A") };

        Assert.Equal(["sensor.a", "sensor.b"], Ids(EntityCatalog.Search(states, EntityKind.Power, "  ")));
    }

    [Fact]
    public void Search_Energy_KeepsOnlyEnergyUnits()
    {
        var states = new[]
        {
            S("sensor.a", "1", "Wh", "A"),
            S("sensor.b", "1", "kWh", "B"),
            S("sensor.c", "1", "MWh", "C"),
            S("sensor.d", "1", "W", "D"),
            S("sensor.e", "1", "€", "E"),
        };

        Assert.Equal(["sensor.a", "sensor.b", "sensor.c"], Ids(EntityCatalog.Search(states, EntityKind.Energy, "")));
    }

    [Fact]
    public void Search_Energy_IgnoresUnitCase()
    {
        Assert.Equal(["sensor.a"], Ids(EntityCatalog.Search([S("sensor.a", "1", "KWH", "A")], EntityKind.Energy, "")));
    }

    [Fact]
    public void Search_Money_KeepsMonetary()
    {
        var states = new[]
        {
            S("sensor.a", "1", "USD", "A", "monetary"),
            S("sensor.b", "1", "€", "B"),
            S("sensor.c", "1", "EUR", "C"),
            S("sensor.d", "1", "EUR/kWh", "D"),
            S("sensor.e", "1", "kWh", "E"),
        };

        Assert.Equal(["sensor.a", "sensor.b", "sensor.c"], Ids(EntityCatalog.Search(states, EntityKind.Money, "")));
    }

    [Fact]
    public void Search_Price_KeepsPerKwh()
    {
        var states = new[]
        {
            S("sensor.a", "1", "€/kWh", "A"),
            S("sensor.b", "1", "EUR/kWh", "B"),
            S("sensor.c", "1", "c€/kWh", "C"),
            S("sensor.d", "1", "€", "D"),
        };

        Assert.Equal(["sensor.a", "sensor.b", "sensor.c"], Ids(EntityCatalog.Search(states, EntityKind.Price, "")));
    }

    [Fact]
    public void Search_Water_KeepsVolumes_AndRanksDailyFirst()
    {
        var states = new[]
        {
            S("sensor.a_total", "1", "m³", "Compteur d'eau"),
            S("sensor.b", "1", "L", "Eau du jour"),
            S("sensor.c", "1", "M3", "Citerne"),
            S("sensor.d", "1", null, "Débit", "water"),
            S("sensor.e", "1", "kWh", "Énergie"),
            S("sensor.f", "1", "L/min", "Débit douche"),
        };

        Assert.Equal(["sensor.b", "sensor.c", "sensor.a_total", "sensor.d"], Ids(EntityCatalog.Search(states, EntityKind.Water, "")));
    }

    [Fact]
    public void Search_Energy_RanksDailyFirst()
    {
        var states = new[]
        {
            S("sensor.a_total", "1", "kWh", "Compteur total"),
            S("sensor.b", "1", "kWh", "Production du jour"),
            S("sensor.pv_today", "1", "kWh", "Solaire"),
            S("sensor.daily_x", "1", "kWh", "Zeta"),
        };

        Assert.Equal(["sensor.b", "sensor.pv_today", "sensor.daily_x", "sensor.a_total"], Ids(EntityCatalog.Search(states, EntityKind.Energy, "")));
    }

    [Fact]
    public void Search_Energy_SejourIsNotDaily()
    {
        var states = new[]
        {
            S("sensor.alpha", "1", "kWh", "Alpha"),
            S("sensor.compteur_sejour", "1", "kWh", "Compteur séjour"),
            S("sensor.prod", "1", "kWh", "Production du jour"),
        };

        Assert.Equal(["sensor.prod", "sensor.alpha", "sensor.compteur_sejour"], Ids(EntityCatalog.Search(states, EntityKind.Energy, "")));
    }

    [Fact]
    public void Search_Energy_SejourWithTodayIsStillDaily()
    {
        var states = new[]
        {
            S("sensor.alpha", "1", "kWh", "Alpha"),
            S("sensor.sejour_x", "1", "kWh", "Séjour today"),
        };

        Assert.Equal(["sensor.sejour_x", "sensor.alpha"], Ids(EntityCatalog.Search(states, EntityKind.Energy, "")));
    }

    [Fact]
    public void Search_Plug_KeepsBinarySensors_PlugFirst()
    {
        var states = new[]
        {
            S("binary_sensor.door", "off", null, "Door", "door"),
            S("binary_sensor.car_plug", "on", null, "Car plug", "plug"),
            S("binary_sensor.charging", "off", null, "Charging", "battery_charging"),
            S("sensor.x", "5", "W", "X"),
        };

        Assert.Equal(["binary_sensor.car_plug", "binary_sensor.charging", "binary_sensor.door"], Ids(EntityCatalog.Search(states, EntityKind.Plug, "")));
    }

    [Fact]
    public void Search_Plug_MatchesQuery_OnShortId()
    {
        var states = new[] { S("binary_sensor.car_plug", "on", null), S("binary_sensor.door", "off", null) };

        Assert.Equal(["binary_sensor.car_plug"], Ids(EntityCatalog.Search(states, EntityKind.Plug, "car")));
    }

    [Theory]
    [InlineData("car_plug", "Plug")]
    [InlineData("car_soc", "Percent")]
    [InlineData("car_power", "Power")]
    [InlineData("battery_soc", "Percent")]
    [InlineData("battery_soc2", "Percent")]
    [InlineData("solar", "Power")]
    [InlineData("battery_charge", "Power")]
    [InlineData("day_consumption", "Energy")]
    [InlineData("day_production", "Energy")]
    [InlineData("day_import", "Energy")]
    [InlineData("day_export", "Energy")]
    [InlineData("day_car", "Energy")]
    [InlineData("day_water", "Water")]
    [InlineData("day_cost", "Money")]
    [InlineData("day_price_sensor", "Price")]
    public void KindFor_MapsFields(string key, string kind) =>
        Assert.Equal(Enum.Parse<EntityKind>(kind), EntityCatalog.KindFor(key));
}
