using System;

namespace Magpie.CommandLine.Dispatch;

internal sealed record CommandTarget(CommandTransportKind Kind, Uri? Url, string? ApiKey);
