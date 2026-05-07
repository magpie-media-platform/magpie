using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Security;

using NUnit.Framework;

namespace Magpie.Test.Auth;

/// <summary>
/// Locks the seed-data hash format in
/// <c>Magpie.Data.EntityTypeConfiguration</c> against
/// <see cref="LocalPasswordHasher"/>. If either side changes its format these tests
/// fail loudly so the seed and verifier do not silently desync.
/// </summary>
[TestFixture]
public class SeededRootUserTests
{
    /// <summary>The seed plaintext password for the bundled root admin.</summary>
    private const string SeedPassword = "magpie";

    /// <summary>The seed PasswordSalt persisted by EntityTypeConfiguration.</summary>
    private const string SeedSalt = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>The seed PasswordHash persisted by EntityTypeConfiguration.</summary>
    private const string SeedHash = "vsgOXycyiKgFzjiJqQTfDXzlqXbDZCN4zBxH5yDa56Y=";

    [Test]
    public async Task LocalPasswordHasher_VerifiesSeededHash_ForSeededPassword()
    {
        var hasher = new LocalPasswordHasher(keyStore: null!);

        var verified = await hasher.VerifyPasswordAsync(SeedPassword, SeedHash, SeedSalt, CancellationToken.None);

        Assert.That(verified, Is.True);
    }

    [Test]
    public async Task LocalPasswordHasher_RejectsSeededHash_ForWrongPassword()
    {
        var hasher = new LocalPasswordHasher(keyStore: null!);

        var verified = await hasher.VerifyPasswordAsync("not-the-password", SeedHash, SeedSalt, CancellationToken.None);

        Assert.That(verified, Is.False);
    }
}
