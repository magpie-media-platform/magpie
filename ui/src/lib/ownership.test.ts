import { describe, expect, it } from 'vitest';

import { Role } from '@/auth';

import { canEditOwned } from './ownership';

function makeHasRole(userRole: Role) {
    return (minRole: Role) => userRole >= minRole;
}

describe('canEditOwned', () => {
    const userId = 'user-1';
    const otherId = 'user-2';

    it('allows owner to edit their own locked item', () => {
        const item = { ownerId: userId, ownerLock: true };
        expect(canEditOwned(item, userId, false, makeHasRole(Role.Requester), Role.Requester)).toBe(true);
    });

    it('denies non-owner from editing a locked item', () => {
        const item = { ownerId: otherId, ownerLock: true };
        expect(canEditOwned(item, userId, false, makeHasRole(Role.Requester), Role.Requester)).toBe(false);
    });

    it('allows manager to edit shared item (null ownerId)', () => {
        const item = { ownerId: null, ownerLock: false };
        expect(canEditOwned(item, userId, true, makeHasRole(Role.Manager), Role.Requester)).toBe(true);
    });

    it('denies non-manager from editing shared item', () => {
        const item = { ownerId: null, ownerLock: false };
        expect(canEditOwned(item, userId, false, makeHasRole(Role.Requester), Role.Requester)).toBe(false);
    });

    it('allows manager to edit unlocked item regardless of ownership', () => {
        const item = { ownerId: otherId, ownerLock: false };
        expect(canEditOwned(item, userId, true, makeHasRole(Role.Manager), Role.Requester)).toBe(true);
    });

    it('allows requester to edit their own unlocked item', () => {
        const item = { ownerId: userId, ownerLock: false };
        expect(canEditOwned(item, userId, false, makeHasRole(Role.Requester), Role.Requester)).toBe(true);
    });

    it('denies viewer from editing any item', () => {
        const item = { ownerId: userId, ownerLock: false };
        expect(canEditOwned(item, userId, false, makeHasRole(Role.Viewer), Role.Requester)).toBe(false);
    });

    it('denies requester from editing another users unlocked item', () => {
        const item = { ownerId: otherId, ownerLock: false };
        expect(canEditOwned(item, userId, false, makeHasRole(Role.Requester), Role.Requester)).toBe(false);
    });

    it('allows manager to edit shared locked item', () => {
        const item = { ownerId: null, ownerLock: true };
        expect(canEditOwned(item, userId, true, makeHasRole(Role.Manager), Role.Requester)).toBe(true);
    });

    it('handles undefined userId gracefully', () => {
        const item = { ownerId: userId, ownerLock: false };
        expect(canEditOwned(item, undefined, false, makeHasRole(Role.Requester), Role.Requester)).toBe(false);
    });
});
