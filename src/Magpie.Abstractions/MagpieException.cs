using System;

namespace Magpie.Abstractions;

/// <summary>
/// The base error type for Magpie.
/// </summary>
public class MagpieException(string message) : Exception(message)
{
}
