using System.Net.Http.Json;
using System.Text.Json;
using ScaleLab.Core.Measurement;

namespace ScaleLab.LoadGen;

/// <summary>Talks to the hub servers' and the proxy's <c>/api</c> endpoints.</summary>
internal sealed class LabApi(HttpClient http, string? key) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static LabApi Create(string? key) =>
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, key);

    public async Task<ProcessStats> StatsAsync(Uri server, bool forceGc, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, new Uri(server, $"/api/stats?gc={(forceGc ? "true" : "false")}"));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProcessStats>(Json, ct)
            ?? throw new InvalidOperationException($"{server} returned empty stats.");
    }

    public async Task BroadcastAsync(Uri server, string hub, long sequence, int bytes, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, new Uri(server, $"/api/broadcast/{hub}?seq={sequence}&bytes={bytes}"));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Waits until every server answers <c>/healthz</c>.</summary>
    public async Task WaitHealthyAsync(IEnumerable<Uri> servers, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        foreach (var server in servers)
        {
            while (true)
            {
                try
                {
                    using var response = await http.GetAsync(new Uri(server, "/healthz"), ct);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline)
                {
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"{server} did not become healthy within {timeout}.");
                }

                await Task.Delay(500, ct);
            }
        }
    }

    public void Dispose() => http.Dispose();

    private HttpRequestMessage Request(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrEmpty(key))
        {
            request.Headers.Add("X-Lab-Key", key);
        }

        return request;
    }
}
