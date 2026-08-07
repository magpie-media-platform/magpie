using System.Collections.Generic;

namespace Magpie.QueryLanguage;

/// <summary>
/// A parse error with its position in the input.
/// </summary>
/// <param name="Message">The error message</param>
/// <param name="Position">The position in the input of the error</param>
/// <param name="Expected">The tokens that are valid at this position</param>
public readonly record struct ParseError(string Message, int Position, IReadOnlyList<string> Expected);
