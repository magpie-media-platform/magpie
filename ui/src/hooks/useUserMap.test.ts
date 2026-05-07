import { describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';

import { Role } from '@/auth';

vi.mock('@/auth', async () => {
    const actual = await vi.importActual('@/auth');
    return {
        ...actual,
        useAuth: () => ({
            user: { id: 'current-user', username: 'currentuser', role: Role.Admin },
            isAuthenticated: true,
        }),
    };
});

vi.mock('@/hooks/useUsers', () => ({
    useUsers: () => ({
        users: [
            { id: 'user-1', username: 'alice' },
            { id: 'user-2', username: 'bob' },
        ],
    }),
}));

import { useUserMap } from './useUserMap';

describe('useUserMap', () => {
    it('returns a map including the current user', () => {
        const { result } = renderHook(() => useUserMap());

        expect(result.current.get('current-user')).toBe('currentuser');
    });

    it('includes users from useUsers', () => {
        const { result } = renderHook(() => useUserMap());

        expect(result.current.get('user-1')).toBe('alice');
        expect(result.current.get('user-2')).toBe('bob');
    });

    it('returns a Map instance', () => {
        const { result } = renderHook(() => useUserMap());

        expect(result.current).toBeInstanceOf(Map);
        expect(result.current.size).toBe(3);
    });
});
