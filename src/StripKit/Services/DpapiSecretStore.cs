using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// <see cref="ISecretStore"/> backed by Windows DPAPI (<see cref="ProtectedData"/>,
/// <see cref="DataProtectionScope.CurrentUser"/>). Keys are encrypted with the logged-in Windows
/// account and written to <c>%APPDATA%/StripKit/secrets.dat</c> as base64 ciphertext, so copying the
/// file to another machine or user account yields nothing usable.
/// </summary>
/// <remarks>
/// StripKit ships win-x64 only. Off-Windows (dev/test on another OS), encryption degrades to a
/// base64 passthrough so round-trips still work locally — a blob written that way will simply fail
/// to decrypt on Windows and be treated as "no key" (the user re-enters it). The Windows ship path
/// is always genuinely encrypted.
/// </remarks>
public sealed class DpapiSecretStore : ISecretStore
{
    // App-scoped additional entropy mixed into the DPAPI blob. Not the security boundary — the
    // Windows user account is — it just ties the ciphertext to StripKit.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StripKit.SecretStore.v1");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, string> _protected;   // provider name → base64 ciphertext

    /// <param name="path">Override the secrets file location (tests point this at a temp file);
    /// defaults to <c>%APPDATA%/StripKit/secrets.dat</c>.</param>
    public DpapiSecretStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        _protected = Load(_path);
    }

    public string? Get(AiProvider provider)
    {
        if (!_protected.TryGetValue(provider.ToString(), out var blob) || string.IsNullOrEmpty(blob))
            return null;
        try
        {
            return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(blob)));
        }
        catch
        {
            return null;   // wrong user / corrupt / cross-platform blob → treat as no key
        }
    }

    public void Set(AiProvider provider, string? apiKey)
    {
        var name = provider.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
            _protected.Remove(name);
        else
            _protected[name] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(apiKey.Trim())));
        Save();
    }

    public void Clear(AiProvider provider)
    {
        if (_protected.Remove(provider.ToString())) Save();
    }

    public bool Has(AiProvider provider) =>
        _protected.TryGetValue(provider.ToString(), out var b) && !string.IsNullOrEmpty(b);

    // ---- crypto (Windows DPAPI; base64 passthrough elsewhere for dev/test only) ----

    private static byte[] Protect(byte[] plain)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        return plain;
    }

    private static byte[] Unprotect(byte[] cipher)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        return cipher;
    }

    // ---- persistence (best-effort) ----

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                       ?? new Dictionary<string, string>();
        }
        catch
        {
            // corrupt / unreadable → start empty
        }
        return new Dictionary<string, string>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_protected, Json));
        }
        catch
        {
            // best-effort; never crash the app over a failed secret write
        }
    }

    private static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StripKit", "secrets.dat");
}
