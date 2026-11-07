using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Zarpazo.Update;

/// <summary>Asks GitHub which version is the latest. It sends nothing but the request itself.</summary>
internal sealed class UpdateChecker(HttpClient http, Version current)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Never throws for network or format problems: they come back as a failed <see cref="UpdateCheck"/>.</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseFeed.LatestUrl);
            request.Headers.UserAgent.ParseAdd($"Zarpazo-Updater/{current}"); // GitHub refuses requests without one
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheck.UpToDate; // no release published yet
            }
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheck.Failed($"GitHub no respondió bien (código {(int)response.StatusCode}). Inténtalo más tarde.");
            }

            ReleaseInfo? latest = ReleaseFeed.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return latest != null && latest.Version > current ? UpdateCheck.Found(latest) : UpdateCheck.UpToDate;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return UpdateCheck.Failed("GitHub tardó demasiado en responder. Inténtalo más tarde.");
        }
        catch (HttpRequestException)
        {
            return UpdateCheck.Failed("No hay conexión con GitHub.");
        }
        catch (JsonException)
        {
            return UpdateCheck.Failed("GitHub devolvió una respuesta que no se entiende.");
        }
    }
}
