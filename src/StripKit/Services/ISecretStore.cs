using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Stores the user's AI-provider API keys encrypted at rest (per-user, Windows DPAPI). App-only;
/// view models depend only on this abstraction. One key per provider — reading a provider with no
/// stored key returns <c>null</c>. The on-disk file holds ciphertext, never the raw key.
/// </summary>
public interface ISecretStore
{
    /// <summary>The stored key for a provider, or <c>null</c> if none is set or it can't be decrypted
    /// (e.g. the file was copied from another Windows account).</summary>
    string? Get(AiProvider provider);

    /// <summary>Stores (encrypted) the key for a provider. A null/blank value clears it.</summary>
    void Set(AiProvider provider, string? apiKey);

    /// <summary>Removes any stored key for a provider.</summary>
    void Clear(AiProvider provider);

    /// <summary>True when a non-empty key is stored for the provider.</summary>
    bool Has(AiProvider provider);
}
