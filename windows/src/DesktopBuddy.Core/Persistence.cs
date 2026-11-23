using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopBuddy.Core;

// Port of Sources/DesktopBuddy/Persistence.swift (Paths and JSONFile).
// The Keychain becomes ISecretStore: the app uses Windows Credential Manager,
// tests use MemorySecretStore.

public static class Paths
{
    private static string? _override;

    /// %LOCALAPPDATA%\DesktopBuddy, or DESKTOPBUDDY_HOME (tests and snapshots use a throwaway folder).
    public static string SupportDir
    {
        get
        {
            var dir = _override
                ?? Environment.GetEnvironmentVariable("DESKTOPBUDDY_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopBuddy");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// Points storage at another folder (tests). Pass null to go back to the default.
    public static void UseFolder(string? dir) => _override = dir;

    public static string Config => Path.Combine(SupportDir, "config.json");
    public static string Inbox => Path.Combine(SupportDir, "inbox.json");
    public static string Seen => Path.Combine(SupportDir, "seen.json");
    public static string Snoozed => Path.Combine(SupportDir, "snoozed.json");
    public static string StatusLog => Path.Combine(SupportDir, "status.log");
    public static string TeamJson => Path.Combine(SupportDir, "team.json");
}

/// ISO 8601 with whole seconds in UTC ("2026-11-14T10:00:00Z"), the same as Swift's .iso8601.
public sealed class Iso8601Converter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        Converters = { new Iso8601Converter() },
    };

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception)
        {
            return null;   // a broken file behaves like a missing one, as on the Mac
        }
    }

    /// Writes atomically: to a temp file first, then swapped into place.
    public static void Save<T>(T value, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Same as the Mac: saving is best effort.
        }
    }

    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Decode<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

/// Where refresh tokens and client secrets live. Never in config.json.
/// The app uses Windows Credential Manager; tests use MemorySecretStore.
public interface ISecretStore
{
    string? Get(string key);
    /// Saves a value; null or empty removes it.
    void Set(string key, string? value);
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _store = [];
    private readonly Lock _lock = new();

    public string? Get(string key)
    {
        lock (_lock) return _store.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, string? value)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(value)) _store.Remove(key);
            else _store[key] = value;
        }
    }
}

/// The secret store the running app uses. The app sets this at startup.
public static class Secrets
{
    public static ISecretStore Store { get; set; } = new MemorySecretStore();
}
