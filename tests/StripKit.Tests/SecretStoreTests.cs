using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The encrypted API-key store. Round-trips per provider, persists across instances, clears on a
/// blank value, and — the point of it — never writes the plaintext key to disk. On Windows this is
/// genuine DPAPI; off-Windows it degrades to base64 (still not plaintext), so these assertions hold
/// on any CI host.
/// </summary>
public class SecretStoreTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"stripkit_secrets_{Guid.NewGuid():N}.dat");

    [Fact]
    public void Set_then_get_round_trips_per_provider()
    {
        var path = TempPath();
        try
        {
            var store = new DpapiSecretStore(path);
            store.Has(AiProvider.Claude).Should().BeFalse();

            store.Set(AiProvider.Claude, "sk-ant-123");
            store.Set(AiProvider.OpenAI, "sk-openai-456");

            store.Has(AiProvider.Claude).Should().BeTrue();
            store.Get(AiProvider.Claude).Should().Be("sk-ant-123");
            store.Get(AiProvider.OpenAI).Should().Be("sk-openai-456");
            store.Get(AiProvider.Gemini).Should().BeNull("no key was stored for Gemini");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Keys_persist_across_instances()
    {
        var path = TempPath();
        try
        {
            new DpapiSecretStore(path).Set(AiProvider.Gemini, "AIza-789");
            new DpapiSecretStore(path).Get(AiProvider.Gemini).Should().Be("AIza-789");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_on_disk_file_never_contains_the_plaintext_key()
    {
        var path = TempPath();
        try
        {
            new DpapiSecretStore(path).Set(AiProvider.Claude, "super-secret-key-value");

            File.ReadAllText(path).Should().NotContain("super-secret-key-value",
                "the key is encrypted/encoded at rest, never written in the clear");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_blank_value_clears_and_clear_removes_the_key()
    {
        var path = TempPath();
        try
        {
            var store = new DpapiSecretStore(path);
            store.Set(AiProvider.Claude, "k");
            store.Set(AiProvider.Claude, "   ");
            store.Has(AiProvider.Claude).Should().BeFalse("a blank value clears the key");

            store.Set(AiProvider.OpenAI, "k2");
            store.Clear(AiProvider.OpenAI);
            store.Get(AiProvider.OpenAI).Should().BeNull();
        }
        finally { File.Delete(path); }
    }
}
