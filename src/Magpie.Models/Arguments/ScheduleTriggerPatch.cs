using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ScheduleTrigger"/>.
/// </summary>
/// <param name="Id">The <see cref="ScheduleTrigger.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="ScheduleTrigger.Name"/> patch</param>
/// <param name="Schedule">The <see cref="ScheduleTrigger.Schedule"/> patch</param>
/// <param name="ExecutionMode">The <see cref="ScheduleTrigger.ExecutionMode"/> patch</param>
/// <param name="Enabled">The <see cref="ScheduleTrigger.Enabled"/> patch</param>
/// <param name="Parameters">The <see cref="ScheduleTrigger.Parameters"/> patch</param>
public sealed record ScheduleTriggerPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    ScalarPatch<string>? Schedule = null,
    ScalarPatch<JobExecutionMode>? ExecutionMode = null,
    ScalarPatch<bool>? Enabled = null,
    DictionaryPatch<string, string, string>? Parameters = null);
