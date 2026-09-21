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

const ROLE_NAME_TO_VALUE: Record<string, Role> = {
    Viewer: Role.Viewer,
    Requester: Role.Requester,
    Manager: Role.Manager,
    Admin: Role.Admin,
};

const ROLE_VALUES = new Set<number>(Object.values(Role));

export function isRole(value: unknown): value is Role {
    return typeof value === 'number' && ROLE_VALUES.has(value);
}

export function roleFromApi(value: Role | string | number | null | undefined): Role {
    if (isRole(value)) {
        return value;
    }
    if (typeof value === 'string') {
        if (value in ROLE_NAME_TO_VALUE) {
            return ROLE_NAME_TO_VALUE[value];
        }
        const numeric = Number(value);
        if (isRole(numeric)) {
            return numeric;
        }
    }
    return Role.Viewer;
}

export type Permission =
    | 'content.view'
    | 'content.edit'
    | 'content.add'
    | 'content.delete'
    | 'content.request'
    | 'subjects.view'
    | 'subjects.edit'
    | 'downloads.view'
    | 'downloads.control'
    | 'downloads.manage'
    | 'indexers.manage'
    | 'clients.manage'
    | 'servers.manage'
    | 'folders.manage'
    | 'jobs.view'
    | 'jobs.run'
    | 'jobs.schedule'
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

export interface PermissionGrant {
    requiredRole: Role;

    granted: boolean;
}

export interface EffectivePermissions {
    role: Role;

    permissions: Record<string, PermissionGrant>;
}

export type PermissionCatalog = Partial<Record<Permission, Role>>;

export function permissionCatalogFromEffective(effective: EffectivePermissions): PermissionCatalog {
    const catalog: PermissionCatalog = {};
    for (const [key, grant] of Object.entries(effective.permissions)) {
        catalog[key as Permission] = grant.requiredRole;
    }
    return catalog;
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
