namespace DesktopBuddy.Core;

// Port of AuthError (OAuth.swift). Messages are copied word for word; they are shown to people.
// The exception type itself (AuthException / AuthErrorKind) lives in Contracts.cs.

public static class AuthErrors
{
    public static AuthException MissingClient(Provider p) => new(AuthErrorKind.MissingClient,
        $"No {p.Name()} sign-in client is set up. Ask your admin for the team setup file, or add a client ID under Advanced.");

    public static AuthException NotSignedIn() => new(AuthErrorKind.NotSignedIn, "Not signed in.");

    public static AuthException Expired() => new(AuthErrorKind.Expired, "Sign-in expired. Sign in again to keep checking this account.");

    public static AuthException Cancelled() => new(AuthErrorKind.Cancelled, "Sign-in was cancelled.");

    public static AuthException PortInUse(int port) => new(AuthErrorKind.PortInUse,
        $"Port {port} is busy. Change the sign-in port under Advanced and try again.");

    public static AuthException BadResponse(string message) => new(AuthErrorKind.BadResponse, message);
}
