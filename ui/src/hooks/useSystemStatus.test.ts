import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';

import * as api from '@/lib/api';
import { useDiskSpace, useSystemStatus, useSystemInfo } from './useSystemStatus';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

vi.mock('@/auth', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/auth')>();
    return {
        ...original,
        useAuth: () => ({ isAuthenticated: true }) as ReturnType<typeof original.useAuth>,
    };
});

const mockApiGet = vi.mocked(api.apiGet);

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    return function Wrapper({ children }: { children: ReactNode }) {
        return createElement(QueryClientProvider, { client: queryClient }, children);
    };
}

describe('useSystemStatus', () => {
    beforeEach(() => {
        vi.clearAllMocks();

        mockApiGet.mockImplementation((path: string) => {
            if (path === '/system') {
                return Promise.resolve({
                    version: '1.0.0',
                    runtime: '.NET 10.0.0',
                    operatingSystem: 'Linux',
                    architecture: 'x64',
                    systemRoot: '/opt/magpie',
                    nodeId: 'node-1',
                    startedAt: '2024-01-01T12:00:00Z',
                });
            }
            if (path === '/system/disk-space') {
                return Promise.resolve({
                    folders: [
                        {
                            id: 'rf-1',
                            name: 'Movies',
                            path: '/media/movies',
                            totalBytes: 1000000000000,
                            freeBytes: 500000000000,
                            usedBytes: 500000000000,
                            contentBytes: 300000000000,
                        },
                        {
                            id: 'rf-2',
                            name: 'TV Shows',
                            path: '/media/tv',
                            totalBytes: 2000000000000,
                            freeBytes: 1500000000000,
                            usedBytes: 500000000000,
                            contentBytes: 0,
                        },
                    ],
                });
            }
            return Promise.reject(new Error(`Unknown endpoint: ${path}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('fetches and returns system status data', async () => {
        const { result } = renderHook(() => useSystemStatus(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.systemInfo.version).toBe('1.0.0');
        });

        expect(result.current.systemInfo.runtime).toBe('.NET 10.0.0');
        expect(result.current.systemInfo.operatingSystem).toBe('Linux');
        expect(result.current.systemInfo.architecture).toBe('x64');
        expect(result.current.systemInfo.systemRoot).toBe('/opt/magpie');
        expect(result.current.systemInfo.nodeId).toBe('node-1');
        expect(result.current.systemInfo.startedAt).toBe('2024-01-01T12:00:00Z');

        expect(result.current.diskSpace).toHaveLength(2);
        expect(result.current.diskSpace[0].name).toBe('Movies');
        expect(result.current.diskSpace[0].totalBytes).toBe(1000000000000);
        expect(result.current.diskSpace[0].freeBytes).toBe(500000000000);
        expect(result.current.diskSpace[0].usedBytes).toBe(500000000000);
        expect(result.current.diskSpace[0].contentBytes).toBe(300000000000);
        expect(result.current.diskSpace[1].name).toBe('TV Shows');
        expect(result.current.diskSpace[1].totalBytes).toBe(2000000000000);
        expect(result.current.diskSpace[1].contentBytes).toBe(0);
        expect(mockApiGet).toHaveBeenCalledTimes(2);
        expect(mockApiGet).not.toHaveBeenCalledWith('/system/health', expect.anything());
    });

    it('returns loading state initially', async () => {
        const { result } = renderHook(() => useSystemStatus(), {
            wrapper: createWrapper(),
        });

        expect(result.current.isLoading).toBe(true);

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
    });

    it('stays loading until disk-space data resolves', async () => {
        let resolveDiskSpace: ((value: unknown) => void) | undefined;
        mockApiGet.mockImplementation((path: string) => {
            if (path === '/system') {
                return Promise.resolve({
                    version: '1.0.0',
                    runtime: '.NET 10.0.0',
                    operatingSystem: 'Linux',
                    architecture: 'x64',
                    systemRoot: '/opt/magpie',
                    nodeId: 'node-1',
                    startedAt: '2024-01-01T12:00:00Z',
                });
            }
            if (path === '/system/disk-space') {
                return new Promise((resolve) => {
                    resolveDiskSpace = resolve;
                });
            }
            return Promise.reject(new Error(`Unknown endpoint: ${path}`));
        });

        const { result } = renderHook(() => useSystemStatus(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.systemInfo.version).toBe('1.0.0');
        });
        expect(result.current.isLoading).toBe(true);

        act(() => {
            resolveDiskSpace?.({ folders: [], totalBytes: 0, freeBytes: 0, usedBytes: 0 });
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
    });
});

describe('useSystemInfo', () => {
    beforeEach(() => {
        vi.clearAllMocks();

        mockApiGet.mockResolvedValue({
            version: '2.0.0',
            runtime: '.NET 11.0.0',
            operatingSystem: 'Windows 11',
            architecture: 'arm64',
            systemRoot: 'C:\\Magpie',
            nodeId: 'node-2',
            startedAt: '2024-06-15T08:00:00Z',
        });
    });

    it('fetches system info from API', async () => {
        const { result } = renderHook(() => useSystemInfo(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.data).toBeDefined();
        });

        expect(result.current.data?.version).toBe('2.0.0');
        expect(result.current.data?.runtime).toBe('.NET 11.0.0');
        expect(result.current.data?.operatingSystem).toBe('Windows 11');
        expect(result.current.data?.architecture).toBe('arm64');
        expect(mockApiGet).toHaveBeenCalledWith('/system');
    });
});

describe('useDiskSpace', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue({
            folders: [
                {
                    id: 'rf-1',
                    name: 'Movies',
                    path: '/media/movies',
                    totalBytes: 1000000000000,
                    freeBytes: 750000000000,
                    usedBytes: 250000000000,
                    contentBytes: 200000000000,
                },
            ],
        });
    });

    it('fetches only disk-space data from the existing system endpoint', async () => {
        const { result } = renderHook(() => useDiskSpace(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.folders).toHaveLength(1);
        });

        expect(result.current.folders[0]).toMatchObject({
            id: 'rf-1',
            name: 'Movies',
            usedBytes: 250000000000,
            freeBytes: 750000000000,
            totalBytes: 1000000000000,
            contentBytes: 200000000000,
        });
        expect(mockApiGet).toHaveBeenCalledTimes(1);
        expect(mockApiGet).toHaveBeenCalledWith('/system/disk-space');
    });
});
