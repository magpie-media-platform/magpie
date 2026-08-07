import { describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import { createElement } from 'react';

import { AuthContext, type AuthContextValue } from './AuthContext';
import { useAuth } from './useAuth';
import { Role } from './types';

const mockUser = { id: 'u1', username: 'testuser', role: Role.Admin };

const mockContextValue: AuthContextValue = {
    isLoading: false,
    isAuthenticated: true,
    user: mockUser,
    hasPermission: () => true,
    getRequiredRole: () => undefined,
    hasRole: () => true,
    signIn: () => {},
    signOut: async () => {},
    login: async () => {},
};

function createWrapper(value: AuthContextValue) {
    return ({ children }: { children: React.ReactNode }) => createElement(AuthContext.Provider, { value }, children);
}

describe('useAuth', () => {
    it('returns context value when inside AuthProvider', () => {
        const { result } = renderHook(() => useAuth(), {
            wrapper: createWrapper(mockContextValue),
        });

        expect(result.current.isAuthenticated).toBe(true);
        expect(result.current.user).toBe(mockUser);
        expect(result.current.isLoading).toBe(false);
    });

    it('returns default context value when used outside AuthProvider', () => {
        const { result } = renderHook(() => useAuth());

        expect(result.current.isLoading).toBe(true);
        expect(result.current.isAuthenticated).toBe(false);
        expect(result.current.user).toBeNull();
        expect(result.current.hasPermission('users.manage')).toBe(false);
    });
});
