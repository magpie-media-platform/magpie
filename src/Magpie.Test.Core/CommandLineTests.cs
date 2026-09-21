using System;
using System.CommandLine;

using Magpie.CommandLine.Dispatch;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
[NonParallelizable]
public sealed class CommandLineTests
{
    private string? _previousKey;
    private CommandTransportOptionSet _options = null!;
    private Command _command = null!;

    [SetUp]
    public void SetUp()
    {
        _previousKey = Environment.GetEnvironmentVariable("MAGPIE_API_KEY");
        Environment.SetEnvironmentVariable("MAGPIE_API_KEY", "environment-key");
        _options = new CommandTransportOptionSet();
        _command = new Command("test");
        _options.AddTo(_command);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("MAGPIE_API_KEY", _previousKey);
    }

    [TestCase("", null)]
    [TestCase("--url https://magpie.example", "environment-key")]
    [TestCase("--url https://magpie.example --api-key explicit-key", "explicit-key")]
    public void Bind_ResolvesApiKey_WhenOptionsAreParsed(string arguments, string? key)
    {
        // Arrange
        var parsed = _command.Parse(arguments);

        // Act
        var result = _options.Bind(parsed);

        // Assert
        Assert.That(result.ApiKey, Is.EqualTo(key));
    }
}
