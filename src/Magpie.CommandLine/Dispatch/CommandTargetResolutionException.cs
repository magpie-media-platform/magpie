using System;

namespace Magpie.CommandLine.Dispatch;

internal sealed class CommandTargetResolutionException(string message) : Exception(message);
