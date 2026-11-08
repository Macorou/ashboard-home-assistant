using System.Net;
using System.Text;

namespace HomeAssistant.Tests;

/// <summary>Un faux serveur Home Assistant : rend les réponses posées dans <see cref="Responses"/> (clé : chemin et requête) et note chaque requête.</summary>
internal sealed class FakeHomeAssistant : HttpMessageHandler
{
    private readonly object _gate = new();

    public Dictionary<string, (int Status, string Body)> Responses { get; } = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Appelée avant chaque réponse : permet de simuler un serveur lent ou de bloquer jusqu'à l'annulation.</summary>
    public Func<CancellationToken, Task>? Delay { get; set; }

    /// <summary>Si posée, chaque requête échoue avec cette exception (panne réseau, échec TLS).</summary>
    public Exception? Failure { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Requests.Add(request);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        if (Delay is { } delay)
        {
            await delay(cancellationToken).ConfigureAwait(false);
        }

        var key = request.RequestUri?.PathAndQuery ?? "";
        (int Status, string Body) answer;
        lock (_gate)
        {
            answer = Responses.TryGetValue(key, out var found) ? found : (404, "{}");
        }

        return new HttpResponseMessage((HttpStatusCode)answer.Status)
        {
            Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
