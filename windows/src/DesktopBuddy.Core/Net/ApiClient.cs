using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopBuddy.Core;

// Port of APIClient in GoogleSources.swift: a minimal authorised JSON GET against the
// Google and Microsoft Graph REST APIs. Errors become ApiException; auth failures AuthException.

/// Gives an access token for an account (the OAuth class does; tests can fake it).
public interface ITokenProvider
{
    Task<string> AccessTokenAsync(AccountConfig account, CancellationToken ct = default);
}

public sealed class ApiClient
{
    private static HttpClient? _http;

    /// The HttpClient every request goes through. Tests replace it with one that has a fake handler.
    public static HttpClient Http
    {
        get => _http ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };   // each request sets its own timeout
        set => _http = value;
    }

    private readonly ITokenProvider? _tokens;
    private readonly HttpClient? _client;

    public AccountConfig Account { get; }

    /// `tokens` and `http` default to the app-wide ones (OAuth.Shared, ApiClient.Http).
    public ApiClient(AccountConfig account, ITokenProvider? tokens = null, HttpClient? http = null)
    {
        Account = account;
        _tokens = tokens;
        _client = http;
    }

    private HttpClient Client => _client ?? Http;

    /// Authorised GET returning the JSON object (empty when the body isn't a JSON object).
    /// `baseUrl` may already carry a query; `query` is added to it.
    public async Task<JsonObject> GetAsync(string baseUrl,
        IEnumerable<KeyValuePair<string, string>>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        var url = BuildUrl(baseUrl, query);
        var uri = new Uri(url);
        var token = await (_tokens ?? OAuth.Shared).AccessTokenAsync(Account, ct).ConfigureAwait(false);

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headers is not null)
            foreach (var h in headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);

        var (status, json) = await SendAsync(Client, req, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        if (status < 200 || status >= 300)
        {
            var err = json.Obj("error");
            var msg = err.Str("message") ?? err.Str("code") ?? $"HTTP {status}";
            throw new ApiException(status, uri.Host, msg);
        }
        return json;
    }

    /// Sends a request and reads the body as a JSON object (empty when it isn't one). Enforces `timeout`.
    internal static async Task<(int Status, JsonObject Json)> SendAsync(HttpClient client, HttpRequestMessage req, TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            return ((int)resp.StatusCode, ParseObject(body));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The request timed out.");
        }
    }

    internal static JsonObject ParseObject(string body)
    {
        try { return JsonNode.Parse(body) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    /// The request address: `baseUrl` plus the query items, percent-encoded ("$" stays readable in names).
    public static string BuildUrl(string baseUrl, IEnumerable<KeyValuePair<string, string>>? query)
    {
        if (query is null) return baseUrl;
        var parts = query.Select(kv => Uri.EscapeDataString(kv.Key).Replace("%24", "$") + "=" + Uri.EscapeDataString(kv.Value)).ToList();
        if (parts.Count == 0) return baseUrl;
        return baseUrl + (baseUrl.Contains('?') ? "&" : "?") + string.Join("&", parts);
    }
}
