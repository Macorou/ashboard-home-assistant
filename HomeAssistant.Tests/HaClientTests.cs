using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace HomeAssistant.Tests;

public sealed class HaClientTests
{
    private const string Token = "secret-token-value";

    private static HaClient Create(FakeHomeAssistant fake, string? token = Token) =>
        new(new HttpClient(fake) { BaseAddress = new Uri("http://192.168.1.10:8123/"), Timeout = TimeSpan.FromSeconds(8) }, () => token);

    private static string StateJson(string id, string state, string? unit = "W", string? deviceClass = "power", string? name = null)
    {
        var attrs = new List<string>();
        if (unit is not null) attrs.Add($"\"unit_of_measurement\":\"{unit}\"");
        if (deviceClass is not null) attrs.Add($"\"device_class\":\"{deviceClass}\"");
        if (name is not null) attrs.Add($"\"friendly_name\":\"{name}\"");
        return $"{{\"entity_id\":\"{id}\",\"state\":\"{state}\",\"attributes\":{{{string.Join(",", attrs)}}}}}";
    }

    private static async Task<HaProblem> ProblemOf(Func<Task> action) => await Assert.ThrowsAsync<HaProblem>(action);

    [Fact]
    public async Task Get_SendsBearerAndParsesUnit()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.pv_power"] = (200, StateJson("sensor.pv_power", "1234.5", "kW"));
        using var client = Create(fake);

        var state = await client.GetStateAsync("sensor.pv_power", CancellationToken.None);

        Assert.Equal("sensor.pv_power", state.EntityId);
        Assert.Equal("1234.5", state.State);
        Assert.Equal("kW", state.Unit);
        Assert.Equal("power", state.DeviceClass);
        Assert.Null(state.FriendlyName);
        Assert.Equal("Bearer " + Token, fake.Requests.Single().Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Get_401_IsUnauthorized()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (401, "{}");
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.Unauthorized, problem.Kind);
        Assert.Contains("clé API", problem.Message);
        Assert.DoesNotContain(Token, problem.Message);
        Assert.True(problem.Message.Length < 200);
    }

    [Fact]
    public async Task Get_404_IsEntityNotFoundWithEntityId()
    {
        var fake = new FakeHomeAssistant();
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.absent", CancellationToken.None));

        Assert.Equal(HaProblemKind.EntityNotFound, problem.Kind);
        Assert.Equal("sensor.absent", problem.EntityId);
        Assert.Contains("sensor.absent", problem.Message);
        Assert.True(problem.Message.Length < 200);
    }

    [Fact]
    public async Task Get_Html_IsBadResponse()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (200, "<!DOCTYPE html><html><body>Login</body></html>");
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.BadResponse, problem.Kind);
        Assert.True(problem.Message.Length < 200);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"state\":\"1\"}")]
    [InlineData("{\"entity_id\":\"sensor.x\"}")]
    public async Task Get_WrongShape_IsBadResponse(string body)
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (200, body);
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.BadResponse, problem.Kind);
    }

    [Fact]
    public async Task GetAll_NotAnArray_IsBadResponse()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states"] = (200, "{}");
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetAllStatesAsync(CancellationToken.None));

        Assert.Equal(HaProblemKind.BadResponse, problem.Kind);
    }

    [Fact]
    public async Task Get_NetworkFailure_IsUnreachable()
    {
        var fake = new FakeHomeAssistant { Failure = new HttpRequestException("connexion refusée") };
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.Unreachable, problem.Kind);
        Assert.Contains("permission", problem.Message);
        Assert.True(problem.Message.Length < 200);
    }

    [Fact]
    public async Task Get_TlsFailure_IsCertificate()
    {
        var fake = new FakeHomeAssistant
        {
            Failure = new HttpRequestException("ssl", new AuthenticationException("certificat invalide")),
        };
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.Certificate, problem.Kind);
        Assert.Contains("Oublier le certificat", problem.Message);
        Assert.True(problem.Message.Length < 200);
    }

    [Fact]
    public async Task Get_Timeout_IsUnreachable()
    {
        var fake = new FakeHomeAssistant { Failure = new TaskCanceledException("timeout") };
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.Unreachable, problem.Kind);
    }

    private static async Task<HaProblem> FailureAt(string address, Exception failure)
    {
        using var client = new HaClient(
            new HttpClient(new FakeHomeAssistant { Failure = failure }) { BaseAddress = new Uri(address) },
            () => Token);
        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));
        Assert.Equal(HaProblemKind.Unreachable, problem.Kind);
        Assert.DoesNotContain(Token, problem.Message);
        Assert.True(problem.Message.Length < 200, problem.Message);
        return problem;
    }

    private static HttpRequestException Socket(SocketError error, HttpRequestError kind = HttpRequestError.ConnectionError) =>
        new(kind, "échec", new SocketException((int)error));

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.TryAgain)]
    [InlineData(SocketError.NoData)]
    public async Task Get_UnknownHost_NamesTheHost(SocketError error)
    {
        var problem = await FailureAt("https://ha.exemple.fr:8123/", Socket(error, HttpRequestError.NameResolutionError));

        Assert.Contains("ha.exemple.fr", problem.Message);
        Assert.Contains("introuvable", problem.Message);
    }

    [Fact]
    public async Task Get_ConnectionRefused_OnAnotherPort_Suggests8123()
    {
        var problem = await FailureAt("https://ha.exemple.fr/", Socket(SocketError.ConnectionRefused));

        Assert.Contains("port 443", problem.Message);
        Assert.Contains("https://ha.exemple.fr:8123", problem.Message);
    }

    [Fact]
    public async Task Get_ConnectionRefused_On8123_DoesNotSuggestTheSamePort()
    {
        var problem = await FailureAt("https://192.168.1.10:8123/", Socket(SocketError.ConnectionRefused));

        Assert.Contains("port 8123", problem.Message);
        Assert.DoesNotContain("essaie", problem.Message);
    }

    public static TheoryData<HttpRequestException> CutConnections =>
    [
        new HttpRequestException(HttpRequestError.ResponseEnded, "fin prématurée"),
        Socket(SocketError.ConnectionReset, HttpRequestError.Unknown),
        Socket(SocketError.ConnectionAborted, HttpRequestError.Unknown),
    ];

    [Theory]
    [MemberData(nameof(CutConnections))]
    public async Task Get_ConnectionCutOnHttp_SuggestsHttps(HttpRequestException failure)
    {
        var problem = await FailureAt("http://192.168.1.10:8123/", failure);

        Assert.Contains("https://192.168.1.10:8123", problem.Message);
    }

    [Theory]
    [MemberData(nameof(CutConnections))]
    public async Task Get_ConnectionCutOnHttps_StaysGeneric(HttpRequestException failure)
    {
        var problem = await FailureAt("https://192.168.1.10:8123/", failure);

        Assert.Contains("injoignable", problem.Message);
    }

    [Fact]
    public async Task Get_AccessDenied_PointsToThePermissions()
    {
        var problem = await FailureAt("https://ha.exemple.fr:8123/", Socket(SocketError.AccessDenied));

        Assert.Contains("ha.exemple.fr", problem.Message);
        Assert.Contains("Réseau local", problem.Message);
        Assert.Contains("Internet", problem.Message);
    }

    [Fact]
    public async Task Get_Timeout_SaysWhereNothingAnswers()
    {
        var problem = await FailureAt("https://192.168.1.10:8123/", new TaskCanceledException("timeout"));

        Assert.Contains("ne répond pas", problem.Message);
        Assert.Contains("192.168.1.10:8123", problem.Message);
        Assert.Contains("permission", problem.Message);
    }

    [Fact]
    public async Task Get_SocketTimeout_SaysWhereNothingAnswers()
    {
        var problem = await FailureAt("https://192.168.1.10:8123/", Socket(SocketError.TimedOut));

        Assert.Contains("ne répond pas", problem.Message);
        Assert.Contains("192.168.1.10:8123", problem.Message);
    }

    [Fact]
    public async Task Get_5xx_GivesTheStatus()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (502, "<html>Bad Gateway</html>");
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Contains("502", problem.Message);
        Assert.True(problem.Message.Length < 200, problem.Message);
    }

    [Fact]
    public async Task Get_CallerCancellation_Propagates()
    {
        var fake = new FakeHomeAssistant { Delay = ct => Task.Delay(Timeout.Infinite, ct) };
        using var client = Create(fake);
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStateAsync("sensor.x", cts.Token));
    }

    [Fact]
    public async Task GetAll_ParsesFriendlyNameAndDeviceClass()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states"] = (200,
            "[" + StateJson("sensor.a", "5", "W", "power", "Panneaux") + "," + StateJson("light.b", "on", null, null) + "]");
        using var client = Create(fake);

        var all = await client.GetAllStatesAsync(CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal("Panneaux", all[0].FriendlyName);
        Assert.Equal("power", all[0].DeviceClass);
        Assert.Equal("W", all[0].Unit);
        Assert.Null(all[1].Unit);
        Assert.Null(all[1].DeviceClass);
    }

    [Fact]
    public async Task Token_WithWhitespaceAndNewline_IsTrimmed()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (200, StateJson("sensor.x", "1"));
        using var client = Create(fake, " abc\r\n");

        await client.GetStateAsync("sensor.x", CancellationToken.None);

        Assert.Equal("Bearer abc", fake.Requests.Single().Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Token_IsReadAtEachCall()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (200, StateJson("sensor.x", "1"));
        var token = "one";
        using var client = new HaClient(new HttpClient(fake) { BaseAddress = new Uri("http://ha.test/") }, () => token);

        await client.GetStateAsync("sensor.x", CancellationToken.None);
        token = "two";
        await client.GetStateAsync("sensor.x", CancellationToken.None);

        Assert.Equal(["Bearer one", "Bearer two"], fake.Requests.Select(r => r.Headers.Authorization?.ToString()));
    }

    [Fact]
    public async Task EntityId_IsEscapedInPath()
    {
        var fake = new FakeHomeAssistant();
        using var client = Create(fake);

        await Assert.ThrowsAsync<HaProblem>(() => client.GetStateAsync("a/b c", CancellationToken.None));

        Assert.Equal("/api/states/a%2Fb%20c", fake.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task OnlyGetIsEverSent()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/"] = (200, "{\"message\":\"API running.\"}");
        fake.Responses["/api/states"] = (200, "[]");
        fake.Responses["/api/states/sensor.x"] = (200, StateJson("sensor.x", "1"));
        using var client = Create(fake);

        await client.PingAsync(CancellationToken.None);
        await client.GetAllStatesAsync(CancellationToken.None);
        await client.GetStateAsync("sensor.x", CancellationToken.None);

        Assert.Equal(3, fake.Requests.Count);
        Assert.All(fake.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Ping_Hits_ApiRoot()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/"] = (200, "{\"message\":\"API running.\"}");
        using var client = Create(fake);

        await client.PingAsync(CancellationToken.None);

        Assert.Equal("/api/", fake.Requests.Single().RequestUri!.PathAndQuery);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Get_5xx_IsUnreachable(int status)
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (status, "<html>Bad Gateway</html>");
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.Unreachable, problem.Kind);
    }

    [Theory]
    [InlineData("ab\ncd")]
    [InlineData("abé")]
    [InlineData("   ")]
    public async Task Token_NotUsableInHeader_IsUnauthorizedWithoutRequest(string token)
    {
        var fake = new FakeHomeAssistant();
        using var client = Create(fake, token);

        var problem = await ProblemOf(() => client.GetStateAsync("sensor.x", CancellationToken.None));

        Assert.Equal(HaProblemKind.Unauthorized, problem.Kind);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("<html><body>Routeur</body></html>")]
    [InlineData("""{"version":"1"}""")]
    [InlineData("[]")]
    public async Task Ping_200WithoutMessage_IsBadResponse(string body)
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/"] = (200, body);
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.PingAsync(CancellationToken.None));

        Assert.Equal(HaProblemKind.BadResponse, problem.Kind);
    }

    [Fact]
    public async Task Get_EmptyState_IsValid()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.x"] = (200, StateJson("sensor.x", ""));
        using var client = Create(fake);

        var state = await client.GetStateAsync("sensor.x", CancellationToken.None);

        Assert.Equal("", state.State);
        Assert.Equal("sensor.x", state.EntityId);
    }

    [Fact]
    public async Task Get_MissingOrNonStringState_IsBadResponse()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/states/sensor.a"] = (200, """{"entity_id":"sensor.a"}""");
        fake.Responses["/api/states/sensor.b"] = (200, """{"entity_id":"sensor.b","state":12}""");
        using var client = Create(fake);

        Assert.Equal(HaProblemKind.BadResponse, (await ProblemOf(() => client.GetStateAsync("sensor.a", CancellationToken.None))).Kind);
        Assert.Equal(HaProblemKind.BadResponse, (await ProblemOf(() => client.GetStateAsync("sensor.b", CancellationToken.None))).Kind);
    }

    [Fact]
    public async Task Ping_401_IsUnauthorized()
    {
        var fake = new FakeHomeAssistant();
        fake.Responses["/api/"] = (401, "{}");
        using var client = Create(fake);

        var problem = await ProblemOf(() => client.PingAsync(CancellationToken.None));

        Assert.Equal(HaProblemKind.Unauthorized, problem.Kind);
    }
}
