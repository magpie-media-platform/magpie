using System;

namespace Magpie.Models.RootFolders;

/// <summary>
/// A root folder where content of a specific type is stored.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Path">The filesystem path.</param>
/// <param name="ContentTypeId">The content type this folder is for.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record RootFolder(
    Guid Id,
    string Name,
    string Path,
    Guid ContentTypeId,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
