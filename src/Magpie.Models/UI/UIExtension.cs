using System;
using System.Collections.Generic;

namespace Magpie.Models.UI;

/// <summary>
/// A UI slot contribution that an extension supplies.
/// </summary>
public sealed record UIExtension
{
    /// <summary>
    /// The id of this UI extension.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the extension that owns this UI extension.
    /// </summary>
    public required Guid ExtensionId { get; init; }

    /// <summary>
    /// The display name of this UI extension.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The id of the frontend slot that renders this component.
    /// </summary>
    public required string Slot { get; init; }

    /// <summary>
    /// How the component changes the slot.
    /// </summary>
    public string Modifier { get; init; } = "After";

    /// <summary>
    /// The tag name of the web component.
    /// </summary>
    public required string TagName { get; init; }

    /// <summary>
    /// The render order of the component. Lower values render first.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// The custom attributes for the web component, keyed by attribute name.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}
