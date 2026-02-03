using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.UI;

/// <summary>
/// A UI slot definition provided by an extension.
/// </summary>
public sealed record UIExtension
{
    /// <summary>
    /// The unique identifier of the UI extension.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The predefined location where this component renders.
    /// </summary>
    public UISlot Slot { get; init; }

    /// <summary>
    /// How the component modifies the slot.
    /// </summary>
    public UISlotModifier Modifier { get; init; }

    /// <summary>
    /// The web component tag name.
    /// </summary>
    public required string TagName { get; init; }

    /// <summary>
    /// Lower values render first.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Custom attributes passed to the web component.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}
