using System.Text.Json;

namespace HomeAssistant;

/// <summary>
/// Lecture prudente des réglages d'une case : une clé absente, d'un autre type ou hors des valeurs permises donne toujours une valeur
/// de repli, jamais une exception (l'utilisateur peut avoir modifié <c>layout.json</c> à la main).
/// </summary>
internal static class SettingsReader
{
    /// <summary>Le texte du réglage, sans espaces autour ; vide si la clé manque ou n'est pas un texte.</summary>
    public static string Text(JsonElement settings, string key) =>
        TryGet(settings, key, out var value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";

    /// <summary>La case à cocher, ou <paramref name="fallback"/> si la clé manque ou n'est pas un booléen.</summary>
    public static bool Bool(JsonElement settings, string key, bool fallback) =>
        TryGet(settings, key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    /// <summary>La valeur choisie si elle fait partie de <paramref name="allowed"/>, sinon <paramref name="fallback"/>.</summary>
    public static string Choice(JsonElement settings, string key, string fallback, params string[] allowed)
    {
        var text = Text(settings, key);
        return Array.IndexOf(allowed, text) >= 0 ? text : fallback;
    }

    /// <summary>
    /// Les valeurs de la liste, dans leur ordre, sans doublon et sans celles hors de <paramref name="allowed"/> ; un tableau vide donne
    /// une liste vide. <paramref name="fallback"/> si la clé manque ou n'est pas un tableau.
    /// </summary>
    public static IReadOnlyList<string> List(JsonElement settings, string key, IReadOnlyList<string> allowed, IReadOnlyList<string> fallback)
    {
        if (!TryGet(settings, key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (text is not null && allowed.Contains(text) && !items.Contains(text))
            {
                items.Add(text);
            }
        }
        return items;
    }

    /// <summary>L'identifiant d'une entité choisie par une recherche (<c>{"id":"sensor.x","name":"…"}</c>) ; vide si absent ou mal formé.</summary>
    public static string EntityId(JsonElement settings, string key) =>
        TryGet(settings, key, out var value) ? Text(value, "id") : "";

    private static bool TryGet(JsonElement settings, string key, out JsonElement value)
    {
        value = default;
        return settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(key, out value);
    }
}
