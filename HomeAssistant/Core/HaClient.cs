using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;

namespace HomeAssistant;

/// <summary>L'état d'une entité Home Assistant tel que lu par <c>GET /api/states/&lt;entity_id&gt;</c>.</summary>
internal sealed record HaState(string EntityId, string State, string? Unit, string? DeviceClass, string? FriendlyName);

/// <summary>Ce qui a empêché de lire Home Assistant.</summary>
internal enum HaProblemKind { Unauthorized, EntityNotFound, Unreachable, Certificate, BadResponse }

/// <summary>Un échec de lecture, avec un message prêt à afficher (français, sans le jeton).</summary>
internal sealed class HaProblem(HaProblemKind kind, string userMessage, string? entityId = null, Exception? inner = null)
    : Exception(userMessage, inner)
{
    public HaProblemKind Kind { get; } = kind;

    /// <summary>L'entité en cause, pour <see cref="HaProblemKind.EntityNotFound"/>.</summary>
    public string? EntityId { get; } = entityId;
}

/// <summary>
/// Client REST en lecture seule de Home Assistant (uniquement des <c>GET</c>). L'appelant pose <c>BaseAddress</c> et <c>Timeout</c> sur le
/// <see cref="HttpClient"/>. Le jeton est relu à chaque appel et n'est jamais gardé.
/// </summary>
internal sealed class HaClient(HttpClient http, Func<string?> readToken) : IDisposable
{
    /// <summary>Vérifie qu'on parle bien à Home Assistant : la racine de son API répond un objet JSON avec <c>message</c>.</summary>
    public async Task PingAsync(CancellationToken ct)
    {
        var body = await SendAsync("api/", null, ct).ConfigureAwait(false);
        Parse(body, root => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out _) ? true : throw Bad());
    }

    public async Task<HaState> GetStateAsync(string entityId, CancellationToken ct)
    {
        var body = await SendAsync("api/states/" + Uri.EscapeDataString(entityId), entityId, ct).ConfigureAwait(false);
        return Parse(body, element => ReadState(element) ?? throw Bad());
    }

    public async Task<IReadOnlyList<HaState>> GetAllStatesAsync(CancellationToken ct)
    {
        var body = await SendAsync("api/states", null, ct).ConfigureAwait(false);
        return Parse<IReadOnlyList<HaState>>(body, root =>
        {
            if (root.ValueKind != JsonValueKind.Array)
            {
                throw Bad();
            }

            var list = new List<HaState>();
            foreach (var item in root.EnumerateArray())
            {
                if (ReadState(item) is { } state)
                {
                    list.Add(state);
                }
            }

            return list;
        });
    }

    public void Dispose() => http.Dispose();

    private async Task<string> SendAsync(string path, string? entityId, CancellationToken ct)
    {
        var token = (readToken() ?? "").Trim();
        if (token.Length == 0 || !token.All(c => c is > ' ' and < '\u007f'))
        {
            throw Unauthorized();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw Unauthorized();
                case HttpStatusCode.NotFound when entityId is not null:
                    throw new HaProblem(HaProblemKind.EntityNotFound, $"Home Assistant ne connaît pas l'entité {entityId}. Vérifie son nom dans les réglages.", entityId);
            }

            if ((int)response.StatusCode >= 500)
            {
                throw Unreachable($"Le serveur a répondu l'erreur {(int)response.StatusCode}. Vérifie que Home Assistant est démarré et, si tu en utilises un, son reverse proxy.", null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Bad();
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw Unreachable(NoAnswerMessage(), ex);
        }
        catch (HttpRequestException ex)
        {
            throw Find<AuthenticationException>(ex) is not null
                ? new HaProblem(HaProblemKind.Certificate, "Le certificat de Home Assistant a changé ou n'est pas de confiance. Si tu l'as renouvelé, utilise « Oublier le certificat » dans les réglages.", inner: ex)
                : Unreachable(DescribeFailure(ex), ex);
        }
    }

    /// <summary>Dit ce qui a empêché de joindre Home Assistant, avec l'hôte et le port en cause, pour que la correction soit évidente.</summary>
    private string DescribeFailure(HttpRequestException ex)
    {
        if (http.BaseAddress is not { } address)
        {
            return UnreachableMessage;
        }

        var socketError = Find<SocketException>(ex)?.SocketErrorCode;
        if (ex.HttpRequestError == HttpRequestError.NameResolutionError || socketError is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData)
        {
            return $"Le nom {address.Host} est introuvable. Vérifie l'orthographe de l'adresse de Home Assistant.";
        }

        var cutOff = ex.HttpRequestError == HttpRequestError.ResponseEnded || socketError is SocketError.ConnectionReset or SocketError.ConnectionAborted;
        return socketError switch
        {
            SocketError.ConnectionRefused when address.Port == DefaultPort =>
                $"Rien ne répond sur le port {DefaultPort} de {address.Host}. Vérifie que Home Assistant est démarré et qu'il utilise bien ce port.",
            SocketError.ConnectionRefused =>
                $"Rien ne répond sur le port {address.Port} de {address.Host}. Home Assistant utilise le port {DefaultPort} par défaut : essaie {address.Scheme}://{address.Host}:{DefaultPort}.",
            SocketError.TimedOut => NoAnswerMessage(),
            SocketError.AccessDenied =>
                $"Windows bloque la connexion vers {address.Host}. Accorde au module la permission « Réseau local » ou « Internet », ou les deux.",
            _ when cutOff && address.Scheme == Uri.UriSchemeHttp =>
                $"{address.Host} a coupé la connexion en http. Si Home Assistant est en HTTPS, écris l'adresse ainsi : https://{address.Host}:{address.Port}.",
            _ => UnreachableMessage,
        };
    }

    private string NoAnswerMessage() =>
        http.BaseAddress is { } address
            ? $"Home Assistant ne répond pas à {address.Authority}. Vérifie l'adresse, le port et les permissions du module (réseau local ou internet)."
            : UnreachableMessage;

    private static T Parse<T>(string body, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return read(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new HaProblem(HaProblemKind.BadResponse, BadMessage, inner: ex);
        }
    }

    private static HaState? ReadState(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !TryString(element, "entity_id", out var id)
            || !element.TryGetProperty("state", out var stateElement)
            || stateElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? unit = null, deviceClass = null, friendlyName = null;
        if (element.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
        {
            unit = Optional(attributes, "unit_of_measurement");
            deviceClass = Optional(attributes, "device_class");
            friendlyName = Optional(attributes, "friendly_name");
        }

        return new HaState(id!, stateElement.GetString() ?? "", unit, deviceClass, friendlyName);
    }

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        value = element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        return !string.IsNullOrEmpty(value);
    }

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    /// <summary>La première exception du type voulu dans la chaîne des causes, ou null.</summary>
    private static T? Find<T>(Exception ex) where T : Exception
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is T found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Le port par défaut de Home Assistant, en http comme en https.</summary>
    private const int DefaultPort = 8123;

    private const string UnreachableMessage = "Home Assistant est injoignable. Vérifie l'adresse et les permissions du module (réseau local ou internet).";

    private const string BadMessage = "Home Assistant a répondu, mais sa réponse est illisible. Vérifie l'adresse : est-ce bien celle de Home Assistant ?";

    private static HaProblem Bad() => new(HaProblemKind.BadResponse, BadMessage);

    private static HaProblem Unauthorized() =>
        new(HaProblemKind.Unauthorized, "Home Assistant refuse ta clé API. Crée une nouvelle clé API dans ton profil Home Assistant et colle-la dans les réglages.");

    private static HaProblem Unreachable(string message, Exception? inner) =>
        new(HaProblemKind.Unreachable, message, inner: inner);
}
