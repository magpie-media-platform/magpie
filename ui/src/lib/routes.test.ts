import { describe, expect, it } from 'vitest';

import { isActivePath } from './routes';

describe('isActivePath', () => {
    describe('exact-match paths (/ and /admin)', () => {
        it('returns true when pathname is exactly /', () => {
            expect(isActivePath('/', '/')).toBe(true);
        });

        it('returns false when pathname is a sub-path of /', () => {
            expect(isActivePath('/library', '/')).toBe(false);
        });

        it('returns false when pathname is /admin/users and path is /', () => {
            expect(isActivePath('/admin/users', '/')).toBe(false);
        });

        it('returns true when pathname is exactly /admin', () => {
            expect(isActivePath('/admin', '/admin')).toBe(true);
        });

        it('returns false when pathname is a sub-path of /admin', () => {
            expect(isActivePath('/admin/users', '/admin')).toBe(false);
        });

        it('returns false when pathname is /admin-panel and path is /admin', () => {
            expect(isActivePath('/admin-panel', '/admin')).toBe(false);
        });
    });

    describe('prefix-match paths (all other routes)', () => {
        it('returns true when pathname exactly matches path', () => {
            expect(isActivePath('/library', '/library')).toBe(true);
        });

        it('returns true when pathname is a sub-path', () => {
            expect(isActivePath('/library/123', '/library')).toBe(true);
        });

        it('returns true for deeply nested sub-path', () => {
            expect(isActivePath('/settings/profile/edit', '/settings')).toBe(true);
        });

        it('returns false when pathname does not start with path', () => {
            expect(isActivePath('/downloads', '/library')).toBe(false);
        });

        it('returns false when pathname is a prefix of path', () => {
            expect(isActivePath('/lib', '/library')).toBe(false);
        });
    });
});
