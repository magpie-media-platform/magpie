using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Magpie.Abstractions.Security;
using Magpie.Core.Security;
using Magpie.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Security;

[TestFixture]
public class DataProtectionServiceTests
{
    private string _dataRoot = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IDataProtectionService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "magpie-secret-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        _loggerFactory = NullLoggerFactory.Instance;

        var hostEnvironment = new MagpieHostEnvironment(
            NodeId: "test-node",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: _dataRoot,
            ExtensionsRoot: _dataRoot,
            ConfigRoot: _dataRoot,
            DataRoot: _dataRoot,
            StartedAt: DateTime.UtcNow);
        var keyStore = new AtomicFileKeyStore(
            hostEnvironment,
            _loggerFactory.CreateLogger<AtomicFileKeyStore>());
        var dataProtectionProvider = new DefaultDataProtectionProvider(
            keyStore,
            _loggerFactory.CreateLogger<DefaultDataProtectionProvider>());
        _subject = new DataProtectionService(dataProtectionProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Test]
    public void ProtectThenUnprotect_RoundTripsTheOriginalSettings_WhenSettingsHaveSecrets()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["apiKey"] = "super-secret-value"
        };

        // Act
        var protectedSettings = _subject.Protect(settings);
        var unprotected = _subject.Unprotect(protectedSettings);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(protectedSettings, Has.Count.EqualTo(1));
            Assert.That(protectedSettings.ContainsKey("__magpieSecret"), Is.True);
            Assert.That(
                protectedSettings["__magpieSecret"],
                Does.StartWith($"v1:{_subject.ActiveKeyId}:"));
            Assert.That(
                protectedSettings.Values.Any(v => v.Contains("super-secret-value", StringComparison.Ordinal)),
                Is.False);
            Assert.That(unprotected, Is.EquivalentTo(settings));
        }
    }

    [Test]
    public void TryGetKeyId_ReturnsTrueWithActiveKeyId_WhenSettingsAreEncrypted()
    {
        // Arrange
        var protectedSettings = _subject.Protect(new Dictionary<string, string> { ["apiKey"] = "secret" });

        // Act
        var found = _subject.TryGetKeyId(protectedSettings, out var keyId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(keyId, Is.EqualTo(_subject.ActiveKeyId));
        }
    }

    [Test]
    public void TryGetKeyId_ReturnsFalse_WhenNoEnvelopeIsPresent()
    {
        // Arrange
        var unencrypted = new Dictionary<string, string> { ["apiKey"] = "plaintext" };

        // Act
        var found = _subject.TryGetKeyId(unencrypted, out var keyId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(keyId, Is.Empty);
        }
    }

    [Test]
    public void Unprotect_Throws_WhenKeyIdIsUnknown()
    {
        // Arrange
        var tampered = new Dictionary<string, string>
        {
            ["__magpieSecret"] = "v1:deadbeefdeadbeef:AAAA"
        };

        // Act
        // Assert
        Assert.That(() => _subject.Unprotect(tampered), Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void Unprotect_Throws_WhenNoEnvelopeIsPresent()
    {
        // Arrange
        var unencrypted = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["apiKey"] = "plaintext-secret"
        };

        // Act
        // Assert
        Assert.That(() => _subject.Unprotect(unencrypted), Throws.InstanceOf<InvalidOperationException>());
    }
}
