export const Role = {
    Viewer: 0,

    Requester: 1,

    Manager: 2,

    Admin: 3,
} as const;

export type Role = (typeof Role)[keyof typeof Role];

export const RoleName: Record<Role, string> = {
    [Role.Viewer]: 'Viewer',
    [Role.Requester]: 'Requester',
    [Role.Manager]: 'Manager',
    [Role.Admin]: 'Admin',
};

export type Permission =
    | 'content.view'
    | 'content.edit'
    | 'content.add'
    | 'content.delete'
    | 'content.request'
    | 'subjects.view'
    | 'subjects.edit'
    | 'downloads.view'
    | 'downloads.manage'
    | 'indexers.manage'
    | 'clients.manage'
    | 'servers.manage'
    | 'folders.manage'
    | 'jobs.view'
    | 'jobs.run'
    | 'jobs.manage'
    | 'wishlists.view'
    | 'wishlists.own'
    | 'wishlists.manage'
    | 'quality.view'
    | 'quality.manage'
    | 'tags.view'
    | 'tags.manage'
    | 'filters.view'
    | 'filters.own'
    | 'filters.manage'
    | 'activity.view'
    | 'calendar.view'
    | 'system.view'
    | 'settings.view'
    | 'settings.edit'
    | 'users.manage'
    | 'extensions.manage'
    | 'backups.manage'
    | 'files.browse';

const permissionMatrix: Record<Permission, Role> = {
    'content.view': Role.Viewer,
    'content.edit': Role.Requester,
    'content.add': Role.Manager,
    'content.delete': Role.Manager,
    'content.request': Role.Requester,

    'subjects.view': Role.Viewer,
    'subjects.edit': Role.Requester,

    'downloads.view': Role.Viewer,
    'downloads.manage': Role.Manager,

    'indexers.manage': Role.Manager,
    'clients.manage': Role.Manager,
    'servers.manage': Role.Manager,
    'folders.manage': Role.Manager,

    'jobs.view': Role.Viewer,
    'jobs.run': Role.Manager,
    'jobs.manage': Role.Admin,

    'wishlists.view': Role.Viewer,
    'wishlists.own': Role.Requester,
    'wishlists.manage': Role.Manager,

    'quality.view': Role.Viewer,
    'quality.manage': Role.Manager,

    'tags.view': Role.Viewer,
    'tags.manage': Role.Manager,

    'filters.view': Role.Viewer,
    'filters.own': Role.Requester,
    'filters.manage': Role.Manager,

    'activity.view': Role.Viewer,
    'calendar.view': Role.Viewer,

    'system.view': Role.Viewer,

    'settings.view': Role.Manager,
    'settings.edit': Role.Admin,

    'users.manage': Role.Admin,
    'extensions.manage': Role.Manager,
    'backups.manage': Role.Admin,

    'files.browse': Role.Admin,
};

export function hasPermission(role: Role, permission: Permission): boolean {
    const requiredRole = permissionMatrix[permission];
    return role >= requiredRole;
}

export function getRequiredRole(permission: Permission): Role {
    return permissionMatrix[permission];
}

export function meetsRoleRequirement(userRole: Role, requiredRole: Role): boolean {
    return userRole >= requiredRole;
}

export interface User {
    id: string;

    username: string;

    role: Role;

    email?: string | null;
}

export interface AuthState {
    isLoading: boolean;

    isAuthenticated: boolean;

    user: User | null;
}
