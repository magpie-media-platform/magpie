namespace Magpie.CommandLine.Descriptors;

internal sealed record MagpieCommandParameterDescriptor(
    string Name,
    string CliName,
    bool IsCollection);
