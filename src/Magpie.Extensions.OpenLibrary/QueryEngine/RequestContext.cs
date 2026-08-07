using System;
using System.Globalization;
using System.Linq;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record RequestContext
{
    public required string UserAgent { get; init; }

    internal static RequestContext ForContact(string? contact)
    {
        var qualified = Qualify(contact);
        var version = typeof(RequestContext).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return new RequestContext
        {
            UserAgent = qualified is null
                ? string.Format(CultureInfo.InvariantCulture, "Magpie/{0} (Open Library metadata provider)", version)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Magpie/{0} (Open Library metadata provider; {1})",
                    version,
                    qualified)
        };
    }

    private static string? Qualify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var contact = value!.Trim();
        var at = contact.IndexOf('@');
        var qualified = (at > 0 && at < contact.Length - 1) || contact.Count(char.IsDigit) >= 7;
        return qualified ? contact : null;
    }
}
