import type { UIExtension } from '@/extensions/types';

export type SlotContext = Record<string, string | undefined>;

interface BuildExtensionAttributesOptions {
    extension: UIExtension;
    apiBaseUrl: string;
    userId: string | null;
    username: string | null;
    role: string | null;
    entityId?: string;
    entityType?: string;
    context?: SlotContext;
}

export function buildExtensionAttributes({
    extension,
    apiBaseUrl,
    userId,
    username,
    role,
    entityId,
    entityType,
    context,
}: BuildExtensionAttributesOptions): Record<string, string> {
    const attributes: Record<string, string> = { ...extension.attributes };

    for (const [key, value] of Object.entries(context ?? {})) {
        if (value !== undefined && value !== '') {
            attributes[`data-magpie-${key}`] = value;
        }
    }

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
