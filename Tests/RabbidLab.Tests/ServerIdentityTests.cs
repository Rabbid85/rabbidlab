using Microsoft.Extensions.DependencyInjection;
using RabbidLab.Client;
using RabbidLab.Server;

namespace RabbidLab.Tests;

/// <summary>The server's identity, and the client taking a changed HTTPS key only on its signature.</summary>
public sealed class ServerIdentityTests : IDisposable
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "rabbidlab-identity-" + Guid.NewGuid().ToString("N"));
    private readonly TestApp app;

    public ServerIdentityTests()
    {
        Directory.CreateDirectory(data);
        app = new TestApp(data);
    }

    public void Dispose()
    {
        app.Dispose();
        try
        {
            Directory.Delete(data, recursive: true);
        }
        catch (IOException)
        {
            // Left for the temp folder's own cleanup.
        }
    }

    [Fact]
    public void A_client_takes_a_new_key_only_when_this_servers_identity_signed_its_own_number_with_that_key()
    {
        var identity = app.Services.GetRequiredService<ServerIdentity>();
        var (modulus, exponent) = identity.Public();
        const string nonce = "c2l4dHktZm91ci1yYW5kb20tYnl0ZXMtYXQtbGVhc3QtaGVyZQ==";
        const string key = "9AF5D88151A494BA6A85BCA3A8B1DCC41951F7D98F9129743E94A59E6E5A2DF1";
        var sig = identity.Sign(nonce, key);
        using var other = System.Security.Cryptography.RSA.Create(3072);
        var stranger = other.ExportParameters(false);

        Assert.True(Pin.Vouched(modulus, exponent, nonce, key, sig));
        Assert.True(Pin.Vouched(modulus, exponent, nonce, key.ToLowerInvariant(), sig)); // the fingerprint's case doesn't matter
        Assert.False(Pin.Vouched(modulus, exponent, "another number from another time, played back again", key, sig));
        Assert.False(Pin.Vouched(modulus, exponent, nonce, "0000000000000000000000000000000000000000000000000000000000000000", sig)); // another key
        Assert.False(Pin.Vouched(Convert.ToBase64String(stranger.Modulus!), Convert.ToBase64String(stranger.Exponent!), nonce, key, sig)); // another identity
        Assert.False(Pin.Vouched(modulus, exponent, nonce, key, "not a signature"));
    }

    [Fact]
    public void The_identity_stays_the_same_after_the_server_starts_again()
    {
        var first = app.Services.GetRequiredService<ServerIdentity>().Public();
        using var again = new TestApp(data);
        var second = again.Services.GetRequiredService<ServerIdentity>().Public();

        Assert.Equal(first, second);
        Assert.Equal(384, Convert.FromBase64String(first.Modulus).Length); // 3072 bits
    }
}
