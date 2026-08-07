import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { useSetupStatus, useCreateSetupAdmin } from './useSetup';
import type { SetupStatus } from '../lib/setupApi';

const mockFetch = vi.fn();
global.fetch = mockFetch;

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
            },
            mutations: {
                retry: false,
            },
        },
    });

    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

describe('useSetup', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('useSetupStatus', () => {
        it('returns setup status from the API', async () => {
            const mockStatus: SetupStatus = {
                isRequired: false,
                isComplete: true,
                requiresAdminClaim: false,
            };

            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 200,
                json: async () => mockStatus,
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const { result } = renderHook(() => useSetupStatus(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            expect(result.current.data).toEqual(mockStatus);
        });

        it('handles loading state', () => {
            mockFetch.mockImplementation(() => new Promise(() => {}));

            const { result } = renderHook(() => useSetupStatus(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);
        });

        it('handles error state', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 500,
                json: async () => ({ title: 'Internal Server Error' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const { result } = renderHook(() => useSetupStatus(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isError).toBe(true);
            });
        });
    });

    describe('useCreateSetupAdmin', () => {
        it('creates admin and invalidates setup status query', async () => {
            const mockUser = {
                id: 'user-1',
                username: 'admin',
                email: null,
                role: 3,
            };

            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 201,
                json: async () => mockUser,
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const { result } = renderHook(() => useCreateSetupAdmin(), {
                wrapper: createWrapper(),
            });

            result.current.mutate({
                username: 'admin',
                password: 'password123',
            });

            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            expect(result.current.data).toEqual(mockUser);
        });

        it('handles mutation error', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 400,
                json: async () => ({ title: 'Setup already complete' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const { result } = renderHook(() => useCreateSetupAdmin(), {
                wrapper: createWrapper(),
            });

            result.current.mutate({
                username: 'admin',
                password: 'password123',
            });

            await waitFor(() => {
                expect(result.current.isError).toBe(true);
            });
        });
    });
});
