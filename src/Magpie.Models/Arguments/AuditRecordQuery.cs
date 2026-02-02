using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query criteria for <see cref="AuditRecord"/>s. 
/// </summary>
/// <param name="EntityId"><inheritdoc cref="AuditRecord.EntityId" path="/summary"/></param>
/// <param name="ParentEntityId"><inheritdoc cref="AuditRecord.ParentEntityId" path="/summary"/></param>
/// <param name="EntityType"><inheritdoc cref="AuditRecord.EntityType" path="/summary"/></param>
/// <param name="Category"><inheritdoc cref="AuditRecord.Category" path="/summary"/></param>
/// <param name="Operation"><inheritdoc cref="AuditRecord.Operation" path="/summary"/></param>
/// <param name="Description"><inheritdoc cref="AuditRecord.Detail" path="/summary"/></param>
/// <param name="Timestamp"><inheritdoc cref="AuditRecord.Timestamp" path="/summary"/></param>
/// <param name="UserId"><inheritdoc cref="AuditRecord.UserId" path="/summary"/></param>
public sealed record AuditRecordQuery(
    string? EntityId = null,
    string? ParentEntityId = null,
    string? EntityType = null,
    string? Category = null,
    string? Operation = null,
    string? Description = null,
    Range<DateTime>? Timestamp = null,
    Guid? UserId = null);
