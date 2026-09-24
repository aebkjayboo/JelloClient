using System.Net;
using System.Net.Http;
using JelloClient.Services;

namespace JelloClient.Roblox;

internal sealed class RobloxRequestException : Exception
{
    public RobloxRequestException(string stage, string url, HttpStatusCode status, string body, string guidance)
        : base($"{stage} failed with {(int)status} {status}.")
    {
        Stage = stage;
        Url = url;
        StatusCode = status;
        Body = body;
        Guidance = guidance;
    }

    public string Stage { get; }

    public string Url { get; }

    public HttpStatusCode StatusCode { get; }

    public string Body { get; }

    public string Guidance { get; }
}

internal static class RobloxHttp
{
    private const int MaxLoggedBodyChars = 600;

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient http,
        string url,
        string stage,
        string guidance,
        HttpCompletionOption completion,
        CancellationToken ct)
    {
        Log.Write("RobloxHttp::Get", $"{stage}: GET {url}");

        var response = await http.GetAsync(url, completion, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            await ThrowDescribedAsync(response, stage, url, guidance, ct).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public static async Task<string> GetStringAsync(
        HttpClient http,
        string url,
        string stage,
        string guidance,
        CancellationToken ct)
    {
        using var response = await GetAsync(http, url, stage, guidance, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public static async Task ThrowDescribedAsync(
        HttpResponseMessage response,
        string stage,
        string url,
        string guidance,
        CancellationToken ct)
    {
        string body = await ReadBodyAsync(response, ct).ConfigureAwait(false);

        Log.Write("RobloxHttp::Failure", $"{stage}: {(int)response.StatusCode} {response.StatusCode} for {url}");

        if (body.Length > 0)
        {
            Log.Write("RobloxHttp::Failure", $"Response body: {body}");
        }
        else
        {
            Log.Write("RobloxHttp::Failure", "Response body was empty");
        }

        throw new RobloxRequestException(stage, url, response.StatusCode, body, guidance);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            body = body.Replace("\r", " ").Replace("\n", " ").Trim();

            return body.Length > MaxLoggedBodyChars ? body[..MaxLoggedBodyChars] + "..." : body;
        }
        catch (Exception ex)
        {
            return $"(could not read the response body: {ex.Message})";
        }
    }
}

internal static class InstallErrors
{
    public static string Describe(Exception exception) => exception switch
    {
        OperationCanceledException => "Install cancelled.",

        InvalidChannelException channel => channel.Message,

        RobloxRequestException request =>
            $"{request.Stage} failed ({(int)request.StatusCode} {request.StatusCode}). {request.Guidance}",

        HttpRequestException http =>
            $"Could not reach Roblox: {http.Message} Check your connection, then try again.",

        _ => $"Install failed at an unexpected step: {exception.Message}"
    };
}
