import type { UIExtension } from '@/extensions/types';

export interface BuildExtensionAttributesOptions {
    extension: UIExtension;
    apiBaseUrl: string;
    userId: string | null;
    username: string | null;
    role: string | null;
    entityId?: string;
    entityType?: string;
}

export function buildExtensionAttributes({
    extension,
    apiBaseUrl,
    userId,
    username,
    role,
    entityId,
    entityType,
}: BuildExtensionAttributesOptions): Record<string, string> {
    const attributes: Record<string, string> = { ...extension.attributes };

    attributes['data-magpie-api-base'] = apiBaseUrl;
    attributes['data-magpie-extension-id'] = extension.extensionId;
    attributes['data-magpie-extension-name'] = extension.name;

    if (userId) {
        attributes['data-magpie-user-id'] = userId;
    }
    if (username) {
        attributes['data-magpie-username'] = username;
    }
    if (role) {
        attributes['data-magpie-role'] = role;
    }
    if (entityId) {
        attributes['data-magpie-entity-id'] = entityId;
    }
    if (entityType) {
        attributes['data-magpie-entity-type'] = entityType;
    }

    return attributes;
}
