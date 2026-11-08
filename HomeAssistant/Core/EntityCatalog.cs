using System.Globalization;
using System.Text;
using System.Text.Json;
using Trofeo.ModuleKit;

namespace HomeAssistant;

/// <summary>Ce que cherche un champ de recherche : une puissance (W, kW), un pourcentage (niveau de batterie), une énergie (Wh, kWh, MWh), un montant ou un prix au kWh.</summary>
internal enum EntityKind { Power, Percent, Energy, Money, Price }

/// <summary>Filtre, trie et met en forme les capteurs proposés par les champs de recherche des réglages.</summary>
internal static class EntityCatalog
{
    public const int MaxResults = 50;

    /// <summary>
    /// Les champs de niveau de batterie cherchent des pourcentages, ceux du bilan du jour des énergies, un coût ou un prix ;
    /// tous les autres des puissances.
    /// </summary>
    public static EntityKind KindFor(string fieldKey) => fieldKey switch
    {
        "battery_soc" or "battery_soc2" => EntityKind.Percent,
        "day_consumption" or "day_production" or "day_import" or "day_export" => EntityKind.Energy,
        "day_cost" => EntityKind.Money,
        "day_price_sensor" => EntityKind.Price,
        _ => EntityKind.Power,
    };

    /// <summary>
    /// Au plus <see cref="MaxResults"/> capteurs <c>sensor.*</c> du bon genre dont le nom ou l'identifiant contient la recherche (sans
    /// accents ni casse). D'abord ceux dont le nom ou l'identifiant commence par la recherche, puis les autres, chaque groupe par nom ;
    /// pour un pourcentage, les capteurs de batterie passent avant tous les autres ; pour une énergie, un coût ou un prix, ceux dont le nom
    /// ou l'identifiant évoque le jour (<c>jour</c>, <c>journ</c>, <c>today</c>, <c>daily</c>) passent en tête.
    /// </summary>
    public static IReadOnlyList<LookupResult> Search(IEnumerable<HaState> states, EntityKind kind, string query)
    {
        var needle = Normalize((query ?? "").Trim());

        return states
            .Where(s => s.EntityId.StartsWith("sensor.", StringComparison.Ordinal) && IsOfKind(s, kind))
            .Select(s => (State: s, Name: NameOf(s), Key: Normalize(NameOf(s)), Id: Normalize(s.EntityId), ShortId: Normalize(s.EntityId["sensor.".Length..])))
            .Where(e => needle.Length == 0 || e.Key.Contains(needle, StringComparison.Ordinal) || e.Id.Contains(needle, StringComparison.Ordinal))
            .OrderBy(e => kind == EntityKind.Percent && e.State.DeviceClass == "battery" ? 0 : 1)
            .ThenBy(e => kind is EntityKind.Energy or EntityKind.Money or EntityKind.Price && IsDaily(e.Key, e.Id) ? 0 : 1)
            .ThenBy(e => e.Key.StartsWith(needle, StringComparison.Ordinal) || e.ShortId.StartsWith(needle, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Take(MaxResults)
            .Select(e => new LookupResult(Label(e.State, e.Name), Value(e.State.EntityId, e.Name)))
            .ToList();
    }

    private static bool IsOfKind(HaState state, EntityKind kind) => kind switch
    {
        EntityKind.Power => state.Unit is "W" or "kW",
        EntityKind.Energy => IsUnit(state, "Wh", "kWh", "MWh"),
        EntityKind.Money => state.DeviceClass == "monetary" || IsUnit(state, "€", "EUR"),
        EntityKind.Price => state.Unit is not null && state.Unit.EndsWith("/kWh", StringComparison.OrdinalIgnoreCase),
        _ => state.Unit == "%",
    };

    private static bool IsUnit(HaState state, params string[] units) =>
        state.Unit is not null && units.Contains(state.Unit, StringComparer.OrdinalIgnoreCase);

    /// <summary>Le nom ou l'identifiant (déjà normalisés) évoque un total du jour.</summary>
    private static bool IsDaily(string name, string id) =>
        new[] { "jour", "journ", "today", "daily" }.Any(word => name.Contains(word, StringComparison.Ordinal) || id.Contains(word, StringComparison.Ordinal));

    private static string NameOf(HaState state) =>
        string.IsNullOrWhiteSpace(state.FriendlyName) ? state.EntityId : state.FriendlyName.Trim();

    private static string Label(HaState state, string name)
    {
        var unavailable = state.State is "unavailable" or "unknown";
        var reading = unavailable || string.IsNullOrEmpty(state.Unit) ? state.State : state.State + " " + state.Unit;
        return $"{name} · {reading} ({state.EntityId})";
    }

    private static JsonElement Value(string id, string name) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["id"] = id, ["name"] = name });

    /// <summary>Minuscules sans accents : « Énergie » devient « energie ».</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
