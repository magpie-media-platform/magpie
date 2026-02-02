using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="EventTrigger"/>.
/// </summary>
/// <param name="Id">The <see cref="EventTrigger.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="EventTrigger.Name"/> patch.</param>
/// <param name="EventFilter">The <see cref="EventTrigger.EventFilter"/> patch.</param>
/// <param name="ExecutionMode">The <see cref="EventTrigger.ExecutionMode"/> patch.</param>
/// <param name="Enabled">The <see cref="EventTrigger.Enabled"/> patch.</param>
/// <param name="Parameters">The <see cref="EventTrigger.Parameters"/> patch.</param>
public sealed record EventTriggerPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    ScalarPatch<EventFilter>? EventFilter = null,
    ScalarPatch<JobExecutionMode>? ExecutionMode = null,
    ScalarPatch<bool>? Enabled = null,
    DictionaryPatch<string, string, string>? Parameters = null);
