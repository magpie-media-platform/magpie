using Magpie.Models.Api;
using Magpie.Models.Data;

namespace Magpie.Api;

internal static class UserExtensions
{
    public static UserResponse ToResponse(this User user) =>
        new()
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            ExtensionId = user.ExtensionId,
            ProviderType = user.ProviderType,
            ExternalId = user.ExternalId,
            Settings = user.Settings,
            Enabled = user.Enabled,
            Role = user.Role,
            FailedLoginAttempts = user.FailedLoginAttempts,
            LockedUntil = user.LockedUntil,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            CreatedByUserId = user.CreatedByUserId,
            ModifiedAt = user.ModifiedAt,
            ModifiedByUserId = user.ModifiedByUserId,
        };
}
