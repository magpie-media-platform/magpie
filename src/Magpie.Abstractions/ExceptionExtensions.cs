using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Magpie.Abstractions;

/// <summary>
/// Extension methods for <see cref="Exception"/>.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Tries to extract an error code from an exception.
    /// </summary>
    /// <param name="exception">The exception to extract an error code from.</param>
    /// <param name="errorCode">The extracted error code, if available.</param>
    /// <returns><see langword="true"/> if an error code was extracted, otherwise <see langword="false"/>.</returns>
    public static bool TryGetErrorCode(this Exception exception, [NotNullWhen(true)] out string? errorCode)
    {
        if (exception is DbException dbException)
        {
            errorCode = dbException.ErrorCode.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        errorCode = null;
        return false;
    }
}
