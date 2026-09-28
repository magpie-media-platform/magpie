import { describe, expect, it } from 'vitest';

import { Role, type Permission } from '@/auth';
import { testHasPermission } from '@/test/permissions';

import { canEditOwned } from './ownership';

const userId = 'user-1';
const otherId = 'user-2';

describe('canEditOwned', () => {
    it.each<[Role, Permission, string | null, boolean, boolean]>([
        [Role.Viewer, 'wishlists.own', userId, false, false],
        [Role.Viewer, 'wishlists.own', userId, true, false],
        [Role.Viewer, 'wishlists.own', otherId, false, false],
        [Role.Viewer, 'wishlists.own', otherId, true, false],
        [Role.Viewer, 'wishlists.own', null, false, false],
        [Role.Viewer, 'wishlists.own', null, true, false],
        [Role.Requester, 'wishlists.own', userId, false, true],
        [Role.Requester, 'wishlists.own', userId, true, true],
        [Role.Requester, 'wishlists.own', otherId, false, false],
        [Role.Requester, 'wishlists.own', otherId, true, false],
        [Role.Requester, 'wishlists.own', null, false, false],
        [Role.Requester, 'wishlists.own', null, true, false],
        [Role.Manager, 'wishlists.own', userId, false, true],
        [Role.Manager, 'wishlists.own', userId, true, true],
        [Role.Manager, 'wishlists.own', otherId, false, false],
        [Role.Manager, 'wishlists.own', otherId, true, false],
        [Role.Manager, 'wishlists.own', null, false, false],
        [Role.Manager, 'wishlists.own', null, true, false],
        [Role.Admin, 'wishlists.own', userId, false, true],
        [Role.Admin, 'wishlists.own', userId, true, true],
        [Role.Admin, 'wishlists.own', otherId, false, true],
        [Role.Admin, 'wishlists.own', otherId, true, false],
        [Role.Admin, 'wishlists.own', null, false, true],
        [Role.Admin, 'wishlists.own', null, true, true],
        [Role.Viewer, 'filters.own', userId, false, true],
        [Role.Viewer, 'filters.own', userId, true, true],
        [Role.Viewer, 'filters.own', otherId, false, false],
        [Role.Viewer, 'filters.own', null, false, false],
        [Role.Manager, 'filters.own', otherId, false, false],
        [Role.Manager, 'filters.own', null, false, false],
        [Role.Admin, 'filters.own', otherId, false, true],
        [Role.Admin, 'filters.own', otherId, true, false],
        [Role.Admin, 'filters.own', null, false, true],
    ])('role %s with %s on owner %s, locked %s, returns %s', (role, permission, ownerId, ownerLock, expected) => {
        // Act
        const result = canEditOwned(
            { ownerId, ownerLock },
            userId,
            role === Role.Admin,
            testHasPermission(role, permission),
        );

        // Assert
        expect(result).toBe(expected);
    });

    it('denies an owned item when the user is unknown', () => {
        // Act
        const result = canEditOwned({ ownerId: userId, ownerLock: false }, undefined, false, true);

        // Assert
        expect(result).toBe(false);
    });
});
