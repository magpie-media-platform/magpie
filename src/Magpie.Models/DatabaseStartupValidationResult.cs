namespace Magpie.Models;

/// <summary>
/// The result of a database startup validation.
/// </summary>
/// <param name="IsValid">Whether the database is valid for startup</param>
/// <param name="ValidationError">The validation error, or <see langword="null"/> when the database is valid</param>
public sealed record DatabaseStartupValidationResult(bool IsValid, string? ValidationError = null);
