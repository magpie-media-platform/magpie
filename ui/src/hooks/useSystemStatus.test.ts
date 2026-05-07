import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';

import * as api from '@/lib/api';
import { useSystemStatus, useSystemInfo, useSystemHealth } from './useSystemStatus';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

vi.mock('./useDashboard', () => ({
    useIndexerHealth: vi.fn(() => ({
        data: { healthy: 2, unhealthy: 1, unknown: 0, stale: 0 },
        isLoading: false,
        error: null,
    })),
    useDownloadClientHealth: vi.fn(() => ({
        data: { healthy: 1, unhealthy: 0, unknown: 1, stale: 0 },
        isLoading: false,
        error: null,
    })),
    useMediaServerHealth: vi.fn(() => ({
        data: { healthy: 1, unhealthy: 0, unknown: 0, stale: 0 },
        isLoading: false,
        error: null,
    })),
    useActiveDownloads: vi.fn(() => ({
        data: {
            counts: { downloading: 1, queued: 2, stalled: 0 },
            items: [
                { id: 'dl-1', title: 'Test Download', progress: 50, state: 'downloading', size: '1 GB', eta: '10 min' },
            ],
        },
        isLoading: false,
        error: null,
    })),
    useRecentJobs: vi.fn(() => ({
        data: [{ id: 'job-1', name: 'Test Job', status: 'completed', duration: '5s', timestamp: '1 min ago' }],
        isLoading: false,
        error: null,
    })),
}));

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
            if (path === '/system/health') {
                return Promise.resolve({ status: 'healthy' });
            }
            if (path === '/system/health/database') {
                return Promise.resolve({
                    name: 'SQLite',
                    status: 'healthy',
                    message: 'Database connection successful',
                    responseTimeMs: 5,
                });
            }
            if (path === '/system/health/scheduler') {
                return Promise.resolve({
                    name: 'Scheduler',
                    status: 'healthy',
                    message: 'Scheduler infrastructure accessible, 2 due trigger(s)',
                    responseTimeMs: 3,
                });
            }
            if (path === '/system/info') {
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
                return Promise.resolve([
                    {
                        id: 'rf-1',
                        name: 'Movies',
                        path: '/media/movies',
                        totalBytes: 1000000000000,
                        freeBytes: 500000000000,
                        usedBytes: 500000000000,
                    },
                    {
                        id: 'rf-2',
                        name: 'TV Shows',
                        path: '/media/tv',
                        totalBytes: 2000000000000,
                        freeBytes: 1500000000000,
                        usedBytes: 500000000000,
                    },
                ]);
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

        expect(result.current.services.api.status).toBe('healthy');
        expect(result.current.services.api.message).toBe('All endpoints responding normally');
        expect(result.current.services.database.name).toBe('SQLite');
        expect(result.current.services.database.status).toBe('healthy');
        expect(result.current.services.database.message).toBe('Database connection successful');
        expect(result.current.services.database.responseTime).toBe(5);
        expect(result.current.services.scheduler.name).toBe('Scheduler');
        expect(result.current.services.scheduler.status).toBe('healthy');
        expect(result.current.services.scheduler.responseTime).toBe(3);

        expect(result.current.systemInfo.runtime).toBe('.NET 10.0.0');
        expect(result.current.systemInfo.operatingSystem).toBe('Linux');
        expect(result.current.systemInfo.architecture).toBe('x64');
        expect(result.current.systemInfo.systemRoot).toBe('/opt/magpie');
        expect(result.current.systemInfo.nodeId).toBe('node-1');
        expect(result.current.systemInfo.startedAt).toBe('2024-01-01T12:00:00Z');

        expect(result.current.integrations.indexers).toEqual({ healthy: 2, unhealthy: 1, unknown: 0, stale: 0 });
        expect(result.current.integrations.downloadClients).toEqual({ healthy: 1, unhealthy: 0, unknown: 1, stale: 0 });
        expect(result.current.integrations.mediaServers).toEqual({ healthy: 1, unhealthy: 0, unknown: 0, stale: 0 });

        expect(result.current.downloadQueue.counts).toEqual({ downloading: 1, queued: 2, stalled: 0 });
        expect(result.current.downloadQueue.activeDownloads).toHaveLength(1);

        expect(result.current.recentJobs).toHaveLength(1);

        expect(result.current.diskSpace).toHaveLength(2);
        expect(result.current.diskSpace[0].name).toBe('Movies');
        expect(result.current.diskSpace[0].totalBytes).toBe(1000000000000);
        expect(result.current.diskSpace[0].freeBytes).toBe(500000000000);
        expect(result.current.diskSpace[0].usedBytes).toBe(500000000000);
        expect(result.current.diskSpace[1].name).toBe('TV Shows');
        expect(result.current.diskSpace[1].totalBytes).toBe(2000000000000);
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

    it('handles API errors gracefully', async () => {
        mockApiGet.mockImplementation((path: string) => {
            if (path === '/system/health') {
                return Promise.reject(new Error('Network error'));
            }
            if (path === '/system/health/database') {
                return Promise.resolve({
                    name: 'SQLite',
                    status: 'healthy',
                    message: 'Database connection successful',
                    responseTimeMs: 5,
                });
            }
            if (path === '/system/health/scheduler') {
                return Promise.resolve({
                    name: 'Scheduler',
                    status: 'healthy',
                    message: 'Scheduler infrastructure accessible, 0 due trigger(s)',
                    responseTimeMs: 2,
                });
            }
            if (path === '/system/info') {
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
                return Promise.resolve([]);
            }
            return Promise.reject(new Error(`Unknown endpoint: ${path}`));
        });

        const { result } = renderHook(() => useSystemStatus(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.error).toBeTruthy();
        });

        expect(result.current.services.api.status).toBe('unhealthy');
    });

    it('maps unhealthy status correctly', async () => {
        mockApiGet.mockImplementation((path: string) => {
            if (path === '/system/health') {
                return Promise.resolve({ status: 'unhealthy' });
            }
            if (path === '/system/health/database') {
                return Promise.resolve({
                    name: 'SQLite',
                    status: 'healthy',
                    message: 'Database connection successful',
                    responseTimeMs: 5,
                });
            }
            if (path === '/system/health/scheduler') {
                return Promise.resolve({
                    name: 'Scheduler',
                    status: 'healthy',
                    message: 'Scheduler infrastructure accessible, 0 due trigger(s)',
                    responseTimeMs: 2,
                });
            }
            if (path === '/system/info') {
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
                return Promise.resolve([]);
            }
            return Promise.reject(new Error(`Unknown endpoint: ${path}`));
        });

        const { result } = renderHook(() => useSystemStatus(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.services.api.status).toBe('unhealthy');
        });

        expect(result.current.services.api.message).toBe('Service unavailable');
    });

    it('shows unhealthy database when health endpoint fails', async () => {
        mockApiGet.mockImplementation((path: string) => {
            if (path === '/system/health') {
                return Promise.resolve({ status: 'healthy' });
            }
            if (path === '/system/health/database') {
                return Promise.reject(new Error('Connection refused'));
            }
            if (path === '/system/health/scheduler') {
                return Promise.resolve({
                    name: 'Scheduler',
                    status: 'healthy',
                    message: 'Scheduler infrastructure accessible, 0 due trigger(s)',
                    responseTimeMs: 2,
                });
            }
            if (path === '/system/info') {
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
                return Promise.resolve([]);
            }
            return Promise.reject(new Error(`Unknown endpoint: ${path}`));
        });

        const { result } = renderHook(() => useSystemStatus(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.services.database.status).toBe('unhealthy');
        });

        expect(result.current.services.database.message).toBe('Health check failed');
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
    });
});

describe('useSystemHealth', () => {
    beforeEach(() => {
        vi.clearAllMocks();

        mockApiGet.mockResolvedValue({ status: 'healthy' });
    });

    it('fetches system health from API', async () => {
        const { result } = renderHook(() => useSystemHealth(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.data).toBeDefined();
        });

        expect(result.current.data?.status).toBe('healthy');
    });
});
