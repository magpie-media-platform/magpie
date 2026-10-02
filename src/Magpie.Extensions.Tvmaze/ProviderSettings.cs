using System;
using System.Globalization;

namespace Magpie.Extensions.Tvmaze;

internal sealed record ProviderSettings
{
    public const string ContactKey = "contact";

    public const string DefaultContact = "magpie-metadata";

    public const string GroupName = "magpie.tvmaze";

    public string? Contact { get; init; }

    public string BuildUserAgent(Version version)
    {
        var contact = string.IsNullOrWhiteSpace(Contact) ? DefaultContact : Contact.Trim();
        return string.Format(
            CultureInfo.InvariantCulture,
            "Magpie/{0} (TVmaze metadata provider; {1})",
            version.ToString(),
            contact);
    }
}
