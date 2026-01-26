using System;

namespace Magpie.Api.EndpointGroups;

internal sealed record CreateApiKeyRequest(Guid UserId, string Name, DateTimeOffset? ExpiresAt);
