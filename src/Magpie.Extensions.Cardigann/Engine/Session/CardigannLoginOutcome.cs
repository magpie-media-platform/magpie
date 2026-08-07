using Magpie.Models;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed record CardigannLoginOutcome
{
    public required bool Success { get; init; }

    public string? Message { get; init; }

    public ConfigurationFailureKind FailureKind { get; init; }
}
