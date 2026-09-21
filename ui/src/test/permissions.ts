import { Role, type EffectivePermissions, type Permission, type PermissionGrant } from '@/auth/types';

export const TEST_PERMISSION_CATALOG: Record<Permission, Role> = {
    'content.view': Role.Viewer,
    'content.edit': Role.Manager,
    'content.add': Role.Requester,
    'content.delete': Role.Manager,
    'content.request': Role.Requester,

    'subjects.view': Role.Viewer,
    'subjects.edit': Role.Requester,

    'downloads.view': Role.Viewer,
    'downloads.control': Role.Requester,
    'downloads.manage': Role.Manager,

    'indexers.manage': Role.Admin,
    'clients.manage': Role.Admin,
    'servers.manage': Role.Admin,
    'folders.manage': Role.Admin,

    'jobs.view': Role.Viewer,
    'jobs.run': Role.Requester,
    'jobs.schedule': Role.Requester,
    'jobs.manage': Role.Manager,

    'wishlists.view': Role.Viewer,
    'wishlists.own': Role.Requester,
    'wishlists.manage': Role.Requester,

    'quality.view': Role.Viewer,
    'quality.manage': Role.Admin,

    'tags.view': Role.Viewer,
    'tags.manage': Role.Manager,

    'filters.view': Role.Viewer,
    'filters.own': Role.Viewer,
    'filters.manage': Role.Viewer,

    'activity.view': Role.Viewer,
    'calendar.view': Role.Viewer,

    'system.view': Role.Viewer,

    'settings.view': Role.Manager,
    'settings.edit': Role.Admin,

    'users.manage': Role.Admin,
    'extensions.manage': Role.Admin,
    'backups.manage': Role.Admin,

    'files.browse': Role.Manager,
};

export function testHasPermission(role: Role, permission: Permission): boolean {
    const required = TEST_PERMISSION_CATALOG[permission];
    return required !== undefined && role >= required;
}

export function testEffectivePermissions(role: Role = Role.Admin): EffectivePermissions {
    const permissions: Record<string, PermissionGrant> = {};
    for (const key of Object.keys(TEST_PERMISSION_CATALOG) as Permission[]) {
        const requiredRole = TEST_PERMISSION_CATALOG[key];
        permissions[key] = { requiredRole, granted: role >= requiredRole };
    }
    return { role, permissions };
}
