using System.Net;

namespace ScaleLab.LoadGen;

/// <summary>
/// Sends a client's negotiate request through one shared connection pool while keeping that client's cookies.
/// </summary>
/// <remarks>
/// By default every <c>HubConnection</c> creates its own <c>HttpClient</c>, and the pooled HTTP connection left behind
/// by its negotiate request stays open for a minute. At ten thousand clients that is ten thousand idle sockets through
/// the proxy, which would inflate exactly the per-connection memory being measured. Sharing one handler removes them,
/// but the per-client <see cref="CookieContainer"/> must survive: the proxy's affinity cookie is set on the negotiate
/// response and SignalR hands the same container to the WebSocket, which is how the upgrade request finds its server.
/// </remarks>
internal sealed class CookieForwardingHandler(HttpMessageInvoker shared, CookieContainer cookies) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request has no URI.");
        var header = cookies.GetCookieHeader(uri);
        if (header.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", header);
        }

        var response = await shared.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var value in setCookies)
            {
                cookies.SetCookies(uri, value);
            }
        }

        // The shared invoker is deliberately not owned (this is not a DelegatingHandler): disposing one client's
        // HttpClient disposes this wrapper only, and the pool keeps serving the others.
        return response;
    }
}
