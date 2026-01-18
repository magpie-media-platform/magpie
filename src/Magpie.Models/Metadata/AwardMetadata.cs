using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// An award nomination or win for content.
/// </summary>
/// <remarks>
/// Awards can be for the content itself or for specific work related to it.
/// </remarks>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="AwardBody">The organization that issues the award.</param>
/// <param name="Name">The name of the award.</param>
/// <param name="Category">The award category.</param>
/// <param name="Status">The nomination status (e.g., "Won", "Nominated", "Pending", "Retracted").</param>
/// <param name="Year">The year of the award, if applicable.</param>
/// <param name="Subject">The description of what the award was for, if applicable.</param>
/// <param name="Language">The language of the award body, if applicable.</param>
/// <param name="Country">The country of the award body, if applicable.</param>
public sealed record AwardMetadata(
    Guid Id,
    int Ordinal,
    string AwardBody,
    string Name,
    string? Category,
    string? Status,
    int? Year,
    string? Subject,
    string? Language,
    string? Country);
