using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Security;
using Magpie.Sdk.Security;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Security;

[TestFixture]
public sealed class LocalPasswordHasherTests
{
    [TestCase(
        true,
        TestName = "HashPasswordUnsaltedAsync_UsesExistingKey_WhenAnotherScopeWinsCreationWithAnIoFailure")]
    [TestCase(
        false,
        TestName = "HashPasswordUnsaltedAsync_UsesExistingKey_WhenAnotherScopeWinsCreationWithAnInvalidOperation")]
    public async Task HashPasswordUnsaltedAsync_UsesExistingKey_WhenAnotherScopeWinsCreation(bool ioFailure)
    {
        // Arrange
        var store = new Mock<IKeyStore>();
        store.SetupSequence(value => value.KeyExistsAsync("api-key-hmac", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false).ReturnsAsync(true);
        store.Setup(value => value.CreateHmacKeyAsync("api-key-hmac", It.IsAny<CancellationToken>()))
            .ThrowsAsync(ioFailure ? new IOException("Exists.") : new InvalidOperationException("Exists."));
        store.Setup(value => value.ComputeHmacAsync("api-key-hmac", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        var subject = new LocalPasswordHasher(store.Object);

        // Act
        var hash = await subject.HashPasswordUnsaltedAsync("key", default).ConfigureAwait(false);

        // Assert
        Assert.That(hash, Is.EqualTo("AQID"));
    }

    [Test]
    public void HashPasswordUnsaltedAsync_Throws_WhenCreationFailsWithoutAnExistingKey()
    {
        // Arrange
        var store = new Mock<IKeyStore>();
        store.Setup(value => value.KeyExistsAsync("api-key-hmac", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        store.Setup(value => value.CreateHmacKeyAsync("api-key-hmac", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full."));
        var subject = new LocalPasswordHasher(store.Object);

        // Act
        var error = Assert.ThrowsAsync<IOException>(() => subject.HashPasswordUnsaltedAsync("key", default));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Disk full."));
    }
}
