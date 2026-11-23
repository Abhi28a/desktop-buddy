using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DesktopBuddy.Core;

// Port of LoopbackReceiver in OAuth.swift: a one-shot HTTP listener on the loopback interface that
// captures the OAuth redirect. Built on TcpListener (HttpListener needs URL reservations on some prefixes).
//
// - Listens on 127.0.0.1 only, or also on [::1] when `alsoIPv6` is set (so "localhost" works
//   whichever address the browser picks). Port 0 picks a free port; read it from Port after Start().
// - Requests without `code` or `error` (favicon, browser pre-connects) get a 404 and the wait goes on.
// - The first redirect with `code` or `error` gets the friendly page, ends the wait and closes the listener.
// - Cancel() ends the wait at once and frees the port. Timeout is 300 s by default.

public sealed class LoopbackReceiver : IDisposable
{
    private sealed record Outcome(Dictionary<string, string>? Params, AuthException? Error);

    private readonly int _requestedPort;
    private readonly bool _alsoIPv6;
    private readonly Lock _lock = new();
    private readonly List<TcpListener> _listeners = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Outcome> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _disposed;

    public LoopbackReceiver(int port = 0, bool alsoIPv6 = false)
    {
        _requestedPort = port;
        _alsoIPv6 = alsoIPv6;
    }

    /// The port actually bound (valid after Start()).
    public int Port { get; private set; }

    /// Binds the port now. Throws AuthException(PortInUse) when it is taken. Safe to call twice.
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed || _result.Task.IsCompleted) return;
            TcpListener v4;
            try
            {
                v4 = new TcpListener(IPAddress.Loopback, _requestedPort) { ExclusiveAddressUse = true };
                v4.Start();
            }
            catch (SocketException)
            {
                throw AuthErrors.PortInUse(_requestedPort);
            }
            Port = ((IPEndPoint)v4.LocalEndpoint).Port;
            _listeners.Add(v4);

            if (_alsoIPv6)
            {
                try
                {
                    var v6 = new TcpListener(IPAddress.IPv6Loopback, Port) { ExclusiveAddressUse = true };
                    v6.Start();
                    _listeners.Add(v6);
                }
                catch (SocketException) { /* no IPv6 loopback: the browser falls back to 127.0.0.1 */ }
            }
            _started = true;
            foreach (var l in _listeners) _ = AcceptLoopAsync(l);
        }
    }

    /// Ends the wait right away (user pressed Cancel, or started a new attempt) and frees the port.
    public void Cancel()
    {
        _result.TrySetResult(new Outcome(null, AuthErrors.Cancelled()));
        StopListeners();
    }

    /// Waits for the redirect and returns its query parameters. Throws AuthException(Cancelled) on
    /// cancel or timeout, AuthException(PortInUse) if the port can't be bound.
    public async Task<Dictionary<string, string>> WaitForCallbackAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!_result.Task.IsCompleted) Start();
        using var timer = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(300));
        using var timerReg = timer.Token.Register(Cancel);
        using var callerReg = ct.Register(Cancel);
        try
        {
            var outcome = await _result.Task.ConfigureAwait(false);
            if (outcome.Error is not null) throw outcome.Error;
            return outcome.Params!;
        }
        finally
        {
            StopListeners();
        }
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
        _result.TrySetResult(new Outcome(null, AuthErrors.Cancelled()));
        StopListeners();
    }

    private void StopListeners()
    {
        List<TcpListener> toStop;
        lock (_lock)
        {
            toStop = [.. _listeners];
            _listeners.Clear();
        }
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var l in toStop)
        {
            try { l.Stop(); } catch (Exception) { }
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        try
        {
            while (!_result.Task.IsCompleted)
            {
                var client = await listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _ = HandleAsync(client);
            }
        }
        catch (Exception)
        {
            // Listener stopped (cancel, finished, disposed): nothing more to accept.
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                using var readLimit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                readLimit.CancelAfter(TimeSpan.FromSeconds(5));   // a pre-connect that never sends is dropped
                var stream = client.GetStream();
                var request = await ReadHeadAsync(stream, readLimit.Token).ConfigureAwait(false);
                var query = ParseRequestQuery(request);

                if (!query.ContainsKey("code") && !query.ContainsKey("error"))
                {
                    await WriteAsync(stream, Response("Not found", "", "404 Not Found")).ConfigureAwait(false);
                    return;
                }
                var ok = query.ContainsKey("code");
                query.TryGetValue("error_description", out var desc);
                query.TryGetValue("error", out var err);
                await WriteAsync(stream, Response(ok ? "You're signed in" : "Sign-in didn't finish",
                    ok ? "You can close this tab and go back to Desktop Buddy." : (desc ?? err ?? ""))).ConfigureAwait(false);
                client.Close();
                _result.TrySetResult(new Outcome(query, null));
            }
        }
        catch (Exception)
        {
            // A broken or slow connection must not end the wait.
        }
    }

    private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[16_384];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            // The request line is all we need; stop as soon as it is complete.
            if (Array.IndexOf(buffer, (byte)'\n', 0, total) >= 0) break;
        }
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// The query parameters of a raw HTTP request ("GET /path?code=abc&state=xyz HTTP/1.1 ...").
    public static Dictionary<string, string> ParseRequestQuery(string request)
    {
        var firstLine = request.Split('\n', 2)[0].TrimEnd('\r');
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var path = parts.Length > 1 ? parts[1] : "";
        return ParseQuery(path);
    }

    /// Query parameters of a path or URL ("/cb?code=a&state=b"); a repeated name keeps its last value.
    public static Dictionary<string, string> ParseQuery(string pathOrUrl)
    {
        var result = new Dictionary<string, string>();
        var q = pathOrUrl.IndexOf('?');
        if (q < 0) return result;
        var query = pathOrUrl[(q + 1)..];
        var hash = query.IndexOf('#');
        if (hash >= 0) query = query[..hash];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = Decode(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
            if (name.Length > 0) result[name] = value;
        }
        return result;
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }

    private static Task WriteAsync(NetworkStream stream, byte[] data) => stream.WriteAsync(data).AsTask();

    /// The friendly page the browser shows after redirecting back (same look as the Mac app).
    /// The text is HTML-escaped because it can come from the redirect URL.
    public static byte[] Response(string title, string sub = "", string status = "200 OK")
    {
        var html =
            "<html><head><meta name=\"viewport\" content=\"width=device-width\"><meta charset=\"utf-8\"><title>Desktop Buddy</title></head>\n"
            + "<body style=\"font-family:-apple-system,system-ui,'Segoe UI Variable','Segoe UI',sans-serif;background:#f5f5f7;color:#1d1d1f;display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">\n"
            + "<div style=\"text-align:center\"><div style=\"font-size:44px\">✓</div>\n"
            + "<h1 style=\"font-weight:600;letter-spacing:-0.02em;margin:12px 0 6px\">" + WebUtility.HtmlEncode(title) + "</h1>\n"
            + "<p style=\"color:#6e6e73;margin:0\">" + WebUtility.HtmlEncode(sub) + "</p></div></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        return [.. head, .. body];
    }
}
