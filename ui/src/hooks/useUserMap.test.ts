import { describe, expect, it, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';

import { Role } from '@/auth';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    buildUrl: vi.fn((path: string) => path),
}));

const mockUseAuth = vi.fn();
vi.mock('@/auth', async () => {
    const actual = await vi.importActual<typeof import('@/auth')>('@/auth');
    return {
        ...actual,
        useAuth: () => mockUseAuth(),
    };
});

import { apiGet } from '@/lib/api';
import { useUserMap } from './useUserMap';

const mockApiGet = vi.mocked(apiGet);

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    return ({ children }: { children: ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

beforeEach(() => {
    vi.clearAllMocks();
});

describe('useUserMap', () => {
    it('fetches the users list and resolves owner names for an admin', async () => {
        mockUseAuth.mockReturnValue({
            user: { id: 'current-user', username: 'currentuser', role: Role.Admin },
            hasPermission: () => true,
        });
        mockApiGet.mockResolvedValue({
            items: [
                { id: 'user-1', username: 'alice', role: 'Viewer' },
                { id: 'user-2', username: 'bob', role: 'Manager' },
            ],
            pageNumber: 1,
            pageSize: 50,
            totalItems: 2,
        });

        const { result } = renderHook(() => useUserMap(), { wrapper: createWrapper() });

        await waitFor(() => expect(result.current.get('user-1')).toBe('alice'));

        expect(result.current.get('user-2')).toBe('bob');
        expect(result.current.get('current-user')).toBe('currentuser');
        expect(mockApiGet).toHaveBeenCalledWith('/users');
    });

    it('does not request the users list for a non-admin and maps only the current user', async () => {
        mockUseAuth.mockReturnValue({
            user: { id: 'current-user', username: 'currentuser', role: Role.Requester },
            hasPermission: () => false,
        });

        const { result } = renderHook(() => useUserMap(), { wrapper: createWrapper() });

        await waitFor(() => expect(result.current.get('current-user')).toBe('currentuser'));

        expect(mockApiGet).not.toHaveBeenCalled();
        expect(result.current.size).toBe(1);
    });
});
