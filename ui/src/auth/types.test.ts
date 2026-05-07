import { describe, expect, it } from 'vitest';

import { Role, RoleName, hasPermission, getRequiredRole, meetsRoleRequirement, type Permission } from './types';

describe('Role', () => {
    it('defines numeric values in ascending order', () => {
        expect(Role.Viewer).toBe(0);
        expect(Role.Requester).toBe(1);
        expect(Role.Manager).toBe(2);
        expect(Role.Admin).toBe(3);
    });

    it('has higher values for more privileged roles', () => {
        expect(Role.Requester).toBeGreaterThan(Role.Viewer);
        expect(Role.Manager).toBeGreaterThan(Role.Requester);
        expect(Role.Admin).toBeGreaterThan(Role.Manager);
    });
});

describe('RoleName', () => {
    it('maps all roles to display names', () => {
        expect(RoleName[Role.Viewer]).toBe('Viewer');
        expect(RoleName[Role.Requester]).toBe('Requester');
        expect(RoleName[Role.Manager]).toBe('Manager');
        expect(RoleName[Role.Admin]).toBe('Admin');
    });
});

describe('hasPermission', () => {
    it('grants Viewer permissions to all roles', () => {
        expect(hasPermission(Role.Viewer, 'content.view')).toBe(true);
        expect(hasPermission(Role.Requester, 'content.view')).toBe(true);
        expect(hasPermission(Role.Manager, 'content.view')).toBe(true);
        expect(hasPermission(Role.Admin, 'content.view')).toBe(true);
    });

    it('grants Requester permissions to Requester and above', () => {
        expect(hasPermission(Role.Viewer, 'content.request')).toBe(false);
        expect(hasPermission(Role.Requester, 'content.request')).toBe(true);
        expect(hasPermission(Role.Manager, 'content.request')).toBe(true);
        expect(hasPermission(Role.Admin, 'content.request')).toBe(true);
    });

    it('grants Manager permissions to Manager and above', () => {
        expect(hasPermission(Role.Viewer, 'content.add')).toBe(false);
        expect(hasPermission(Role.Requester, 'content.add')).toBe(false);
        expect(hasPermission(Role.Manager, 'content.add')).toBe(true);
        expect(hasPermission(Role.Admin, 'content.add')).toBe(true);
    });

    it('grants Admin permissions only to Admin', () => {
        expect(hasPermission(Role.Viewer, 'users.manage')).toBe(false);
        expect(hasPermission(Role.Requester, 'users.manage')).toBe(false);
        expect(hasPermission(Role.Manager, 'users.manage')).toBe(false);
        expect(hasPermission(Role.Admin, 'users.manage')).toBe(true);
    });
});

describe('getRequiredRole', () => {
    it('returns Viewer for view permissions', () => {
        expect(getRequiredRole('content.view')).toBe(Role.Viewer);
        expect(getRequiredRole('activity.view')).toBe(Role.Viewer);
        expect(getRequiredRole('system.view')).toBe(Role.Viewer);
    });

    it('returns Requester for request permissions', () => {
        expect(getRequiredRole('content.request')).toBe(Role.Requester);
        expect(getRequiredRole('wishlists.own')).toBe(Role.Requester);
    });

    it('returns Manager for management permissions', () => {
        expect(getRequiredRole('content.add')).toBe(Role.Manager);
        expect(getRequiredRole('indexers.manage')).toBe(Role.Manager);
        expect(getRequiredRole('settings.view')).toBe(Role.Manager);
    });

    it('returns Admin for admin-only permissions', () => {
        expect(getRequiredRole('users.manage')).toBe(Role.Admin);
        expect(getRequiredRole('backups.manage')).toBe(Role.Admin);
        expect(getRequiredRole('settings.edit')).toBe(Role.Admin);
    });
});

describe('meetsRoleRequirement', () => {
    it('returns true when user role equals required role', () => {
        expect(meetsRoleRequirement(Role.Viewer, Role.Viewer)).toBe(true);
        expect(meetsRoleRequirement(Role.Manager, Role.Manager)).toBe(true);
        expect(meetsRoleRequirement(Role.Admin, Role.Admin)).toBe(true);
    });

    it('returns true when user role exceeds required role', () => {
        expect(meetsRoleRequirement(Role.Admin, Role.Viewer)).toBe(true);
        expect(meetsRoleRequirement(Role.Manager, Role.Requester)).toBe(true);
    });

    it('returns false when user role is below required role', () => {
        expect(meetsRoleRequirement(Role.Viewer, Role.Admin)).toBe(false);
        expect(meetsRoleRequirement(Role.Requester, Role.Manager)).toBe(false);
    });
});

describe('Permission Matrix', () => {
    const permissionMatrix: Array<{
        permission: Permission;
        minRole: Role;
        description: string;
    }> = [
        { permission: 'content.view', minRole: Role.Viewer, description: 'Dashboard/Content list view' },
        { permission: 'content.request', minRole: Role.Requester, description: 'Request content' },
        { permission: 'content.add', minRole: Role.Manager, description: 'Add content' },
        { permission: 'content.edit', minRole: Role.Requester, description: 'Edit content' },
        { permission: 'content.delete', minRole: Role.Manager, description: 'Delete content' },

        { permission: 'subjects.view', minRole: Role.Viewer, description: 'View subjects' },
        { permission: 'subjects.edit', minRole: Role.Requester, description: 'Edit subjects' },

        { permission: 'downloads.view', minRole: Role.Viewer, description: 'View downloads' },
        { permission: 'downloads.manage', minRole: Role.Manager, description: 'Manage downloads' },

        { permission: 'indexers.manage', minRole: Role.Manager, description: 'Manage indexers' },
        { permission: 'clients.manage', minRole: Role.Manager, description: 'Manage download clients' },
        { permission: 'servers.manage', minRole: Role.Manager, description: 'Manage media servers' },
        { permission: 'folders.manage', minRole: Role.Manager, description: 'Manage root folders' },

        { permission: 'jobs.view', minRole: Role.Viewer, description: 'View jobs' },
        { permission: 'jobs.run', minRole: Role.Manager, description: 'Run jobs' },
        { permission: 'jobs.manage', minRole: Role.Admin, description: 'Manage jobs' },

        { permission: 'wishlists.view', minRole: Role.Viewer, description: 'View wishlists' },
        { permission: 'wishlists.own', minRole: Role.Requester, description: 'Own wishlists' },
        { permission: 'wishlists.manage', minRole: Role.Manager, description: 'Manage wishlists' },

        { permission: 'quality.view', minRole: Role.Viewer, description: 'View quality tiers' },
        { permission: 'quality.manage', minRole: Role.Manager, description: 'Manage quality tiers' },

        { permission: 'tags.view', minRole: Role.Viewer, description: 'View tags' },
        { permission: 'tags.manage', minRole: Role.Manager, description: 'Manage tags' },

        { permission: 'filters.view', minRole: Role.Viewer, description: 'View filters' },
        { permission: 'filters.own', minRole: Role.Requester, description: 'Own filters' },
        { permission: 'filters.manage', minRole: Role.Manager, description: 'Manage filters' },

        { permission: 'activity.view', minRole: Role.Viewer, description: 'View activity' },
        { permission: 'calendar.view', minRole: Role.Viewer, description: 'View calendar' },

        { permission: 'system.view', minRole: Role.Viewer, description: 'View system status' },

        { permission: 'settings.view', minRole: Role.Manager, description: 'View settings' },
        { permission: 'settings.edit', minRole: Role.Admin, description: 'Edit settings' },

        { permission: 'users.manage', minRole: Role.Admin, description: 'Manage users' },
        { permission: 'extensions.manage', minRole: Role.Manager, description: 'Manage extensions' },
        { permission: 'backups.manage', minRole: Role.Admin, description: 'Manage backups' },
    ];

    const allRoles: Role[] = [Role.Viewer, Role.Requester, Role.Manager, Role.Admin];

    describe('hasPermission for all permissions', () => {
        for (const { permission, minRole, description } of permissionMatrix) {
            describe(`${permission} (${description})`, () => {
                for (const role of allRoles) {
                    const shouldHave = role >= minRole;
                    const roleName = RoleName[role];

                    it(`${roleName} ${shouldHave ? 'has' : 'lacks'} permission`, () => {
                        expect(hasPermission(role, permission)).toBe(shouldHave);
                    });
                }
            });
        }
    });

    describe('getRequiredRole for all permissions', () => {
        for (const { permission, minRole, description } of permissionMatrix) {
            it(`${permission} requires ${RoleName[minRole]} (${description})`, () => {
                expect(getRequiredRole(permission)).toBe(minRole);
            });
        }
    });

    describe('permission counts by role', () => {
        it('Admin has all permissions', () => {
            const adminPermissions = permissionMatrix.filter((p) => hasPermission(Role.Admin, p.permission));
            expect(adminPermissions.length).toBe(permissionMatrix.length);
        });

        it('Manager has more permissions than Requester', () => {
            const managerCount = permissionMatrix.filter((p) => hasPermission(Role.Manager, p.permission)).length;
            const requesterCount = permissionMatrix.filter((p) => hasPermission(Role.Requester, p.permission)).length;
            expect(managerCount).toBeGreaterThan(requesterCount);
        });

        it('Requester has more permissions than Viewer', () => {
            const requesterCount = permissionMatrix.filter((p) => hasPermission(Role.Requester, p.permission)).length;
            const viewerCount = permissionMatrix.filter((p) => hasPermission(Role.Viewer, p.permission)).length;
            expect(requesterCount).toBeGreaterThan(viewerCount);
        });
    });
});
