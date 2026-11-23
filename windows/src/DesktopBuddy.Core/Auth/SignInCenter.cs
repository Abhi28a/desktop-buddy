namespace DesktopBuddy.Core;

// Port of SignInCenter (SignInCenter.swift), without UI.
// Tracks browser sign-ins per account so every screen shows the same state and can cancel,
// reopen or copy the link (for when the browser opens in the wrong profile).
//
// Threading: all state is guarded by a lock. `Changed` and the other events may fire on any thread;
// the UI should marshal to its dispatcher. Events are raised outside the lock.
//
// Where the Mac version touches AppState, this raises events for the app to wire up:
//   SignedIn(accountID)             -> state.expired.Remove(id); state.wrongAccount.Remove(id); state.RefreshSignedIn()
//   WrongAccount(accountID, address)-> state.expired.Remove(id); state.wrongAccount[id] = address; state.RefreshSignedIn()
//   AddressAccepted(accountID, addr)-> set the account's email to addr, clear wrongAccount[id], poll the account

public sealed class SignInCenter
{
    public static SignInCenter Shared { get; } = new();

    private readonly OAuth? _oauth;
    private readonly Lock _lock = new();
    /// accountID -> sign-in link (null until the link is ready). A key present means "waiting".
    private readonly Dictionary<string, Uri?> _waiting = [];
    private readonly Dictionary<string, string> _errors = [];
    private readonly Dictionary<string, string> _wrongAddress = [];
    private readonly Dictionary<string, int> _attempt = [];
    private string? _copied;

    public SignInCenter(OAuth? oauth = null) => _oauth = oauth;

    private OAuth Oauth => _oauth ?? OAuth.Shared;

    /// Something changed (waiting, link, error or copied state): refresh the screen.
    public event Action? Changed;
    /// A sign-in finished as the right account. A refresh token is saved.
    public event Action<string>? SignedIn;
    /// A sign-in finished, but as a different address than the account's (accountID, address that signed in).
    public event Action<string, string>? WrongAccount;
    /// The person chose "Use <address>" (accountID, address): switch the account's email and check it now.
    public event Action<string, string>? AddressAccepted;

    public bool IsWaiting(string id) { lock (_lock) return _waiting.ContainsKey(id); }

    /// The browser link, once it is ready.
    public Uri? Link(string id) { lock (_lock) return _waiting.TryGetValue(id, out var u) ? u : null; }

    /// The last sign-in error for the account, if any.
    public string? Error(string id) { lock (_lock) return _errors.GetValueOrDefault(id); }

    /// The account whose link was just copied (shows "Copied" for 2 seconds).
    public string? Copied { get { lock (_lock) return _copied; } }

    /// The address that actually signed in when it wasn't the account's own.
    public string? WrongAddress(string id) { lock (_lock) return _wrongAddress.GetValueOrDefault(id); }

    /// Starts (or restarts) sign-in. Returns true when the account is signed in as the right address.
    public async Task<bool> StartAsync(AccountConfig account)
    {
        int n;
        lock (_lock)
        {
            n = _attempt.GetValueOrDefault(account.Id) + 1;
            _attempt[account.Id] = n;
            _errors.Remove(account.Id);
            _wrongAddress.Remove(account.Id);
            _waiting[account.Id] = null;
        }
        Changed?.Invoke();

        try
        {
            await Oauth.SignInAsync(account, url =>
            {
                lock (_lock)
                {
                    if (_attempt.GetValueOrDefault(account.Id) != n) return;
                    _waiting[account.Id] = url;
                }
                Changed?.Invoke();
            }).ConfigureAwait(false);

            lock (_lock)
            {
                if (_attempt.GetValueOrDefault(account.Id) != n) return false;
                _waiting.Remove(account.Id);
            }
            Changed?.Invoke();

            // Catch the "approved in the wrong browser profile" case before showing anyone's mail.
            // A brand-new account has no email yet: whoever signs in is the account being added.
            List<string>? emails = null;
            if (account.Email.Length > 0)
            {
                try { emails = await Oauth.SignedInEmailsAsync(account).ConfigureAwait(false); }
                catch (Exception) { emails = null; }
            }
            if (emails is { Count: > 0 } && !emails.Contains(account.Email.ToLowerInvariant()))
            {
                lock (_lock) _wrongAddress[account.Id] = emails[0];
                WrongAccount?.Invoke(account.Id, emails[0]);
                Changed?.Invoke();
                return false;
            }
            SignedIn?.Invoke(account.Id);
            return true;
        }
        catch (Exception error)
        {
            // A newer attempt replaced this one: leave its state alone.
            lock (_lock)
            {
                if (_attempt.GetValueOrDefault(account.Id) != n) return false;
                _waiting.Remove(account.Id);
                if (error is AuthException { Kind: AuthErrorKind.Cancelled })
                {
                    // fall through to Changed below
                }
                else
                {
                    _errors[account.Id] = error.Message;
                }
            }
            Changed?.Invoke();
            return false;
        }
    }

    /// Cancels a sign-in that is waiting for the browser.
    public void Cancel(string id)
    {
        lock (_lock)
        {
            _attempt[id] = _attempt.GetValueOrDefault(id) + 1;
            _waiting.Remove(id);
        }
        Oauth.CancelSignIn(id);
        Changed?.Invoke();
    }

    /// Opens the sign-in link again in the browser.
    public void Reopen(string id)
    {
        var url = Link(id);
        if (url is not null) Oauth.Open(url.AbsoluteUri);
    }

    /// The link as text for "Copy Sign-in Link": put it on the clipboard. Marks the account as "Copied" for 2 seconds.
    public string? CopyLink(string id)
    {
        var url = Link(id);
        if (url is null) return null;
        lock (_lock) _copied = id;
        Changed?.Invoke();
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ =>
        {
            var cleared = false;
            lock (_lock)
            {
                if (_copied == id) { _copied = null; cleared = true; }
            }
            if (cleared) Changed?.Invoke();
        }, TaskScheduler.Default);
        return url.AbsoluteUri;
    }

    public void ClearError(string id)
    {
        lock (_lock) _errors.Remove(id);
        Changed?.Invoke();
    }

    /// Keep the sign-in but switch the account to the address that actually signed in.
    /// Raises AddressAccepted(accountID, address); the app updates the account and polls it.
    public void AcceptSignedInAddress(string id)
    {
        string? actual;
        lock (_lock)
        {
            if (!_wrongAddress.Remove(id, out actual)) return;
        }
        AddressAccepted?.Invoke(id, actual);
        Changed?.Invoke();
    }
}

/// The words on the "waiting for the browser" panel (SignInWaitingView in SignInCenter.swift), word for word.
public static class SignInWaitingText
{
    public const string Title = "Finish signing in in your browser";
    public const string Cancel = "Cancel";
    public const string CopyLink = "Copy Sign-in Link";
    public const string Copied = "Copied";
    public const string OpenAgain = "Open Again";

    public static string Subtitle(string email) => email.Length == 0
        ? "Choose the account you want to add and allow access. This updates by itself when you're done."
        : $"Sign in as {email} and allow access. This updates by itself when you're done.";

    /// Tooltip on Copy Sign-in Link.
    public static string CopyHelp(string email) => $"Paste this link into the browser profile signed in as {email}";

    /// Footnote shown when the panel isn't compact.
    public static string WrongProfileHint(string email) => email.Length == 0
        ? "Opened in the wrong browser profile? Copy the link and paste it into the profile you use for this account."
        : $"Opened in the wrong browser profile? Copy the link and paste it into the profile signed in as {email}.";
}
