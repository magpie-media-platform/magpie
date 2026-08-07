namespace Magpie.Models.Security;

/// <summary>
/// A password hash and its salt.
/// </summary>
/// <param name="Hash">The password hash</param>
/// <param name="Salt">The salt for the hash</param>
public sealed record HashedPassword(string Hash, string Salt);
