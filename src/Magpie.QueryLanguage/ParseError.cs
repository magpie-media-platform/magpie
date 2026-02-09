using System.Collections.Generic;

namespace Magpie.QueryLanguage;

/// <summary>
/// Represents a parse error with position information.
/// </summary>
/// <param name="Message">The error message.</param>
/// <param name="Position">The position in the input where the error occurred.</param>
/// <param name="Expected">The tokens that were expected at this position.</param>
public readonly record struct ParseError(string Message, int Position, IReadOnlyList<string> Expected);
