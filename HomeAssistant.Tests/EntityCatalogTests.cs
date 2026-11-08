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

    [Theory]
    [InlineData("battery_soc", true)]
    [InlineData("battery_soc2", true)]
    [InlineData("solar", false)]
    [InlineData("battery_charge", false)]
    public void KindFor_MapsFields(string key, bool percent) =>
        Assert.Equal(percent ? EntityKind.Percent : EntityKind.Power, EntityCatalog.KindFor(key));
}
