import { describe, expect, it } from 'vitest';

import {
    Role,
    RoleName,
    roleFromApi,
    meetsRoleRequirement,
    permissionCatalogFromEffective,
    type EffectivePermissions,
} from './types';

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

describe('roleFromApi', () => {
    it('maps the server string enum names to ordinals', () => {
        expect(roleFromApi('Viewer')).toBe(Role.Viewer);
        expect(roleFromApi('Requester')).toBe(Role.Requester);
        expect(roleFromApi('Manager')).toBe(Role.Manager);
        expect(roleFromApi('Admin')).toBe(Role.Admin);
    });

    it('passes through numeric ordinals and numeric strings', () => {
        expect(roleFromApi(2)).toBe(Role.Manager);
        expect(roleFromApi('3')).toBe(Role.Admin);
    });

    it('falls back to Viewer for unknown or missing values', () => {
        expect(roleFromApi(undefined)).toBe(Role.Viewer);
        expect(roleFromApi(null)).toBe(Role.Viewer);
        expect(roleFromApi('Nope')).toBe(Role.Viewer);
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

describe('permissionCatalogFromEffective', () => {
    it('projects the server payload into a permission -> required-role lookup', () => {
        const effective: EffectivePermissions = {
            role: Role.Manager,
            permissions: {
                'content.view': { requiredRole: Role.Viewer, granted: true },
                'content.add': { requiredRole: Role.Manager, granted: true },
                'users.manage': { requiredRole: Role.Admin, granted: false },
            },
        };

        const catalog = permissionCatalogFromEffective(effective);

        expect(catalog['content.view']).toBe(Role.Viewer);
        expect(catalog['content.add']).toBe(Role.Manager);
        expect(catalog['users.manage']).toBe(Role.Admin);
    });

    it('does not carry granted-ness; only the required role per key', () => {
        const effective: EffectivePermissions = {
            role: Role.Viewer,
            permissions: {
                'users.manage': { requiredRole: Role.Admin, granted: false },
            },
        };

        const catalog = permissionCatalogFromEffective(effective);

        expect(catalog['users.manage']).toBe(Role.Admin);
    });

    it('yields an empty catalog for an empty payload', () => {
        const catalog = permissionCatalogFromEffective({ role: Role.Viewer, permissions: {} });

        expect(Object.keys(catalog)).toHaveLength(0);
    });
});
