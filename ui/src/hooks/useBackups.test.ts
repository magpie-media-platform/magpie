import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiDelete: vi.fn(),
}));

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { useBackups, type ApiBackup, type BackupCapabilities } from './useBackups';

const mockApiBackups: ApiBackup[] = [
    {
        id: '20240115_103000',
        name: 'Scheduled 20240115_103000',
        createdAt: '2024-01-15T10:30:00Z',
        sizeBytes: 15_728_640,
        kind: 'Scheduled',
    },
    {
        id: '20240110_120000',
        name: 'pre-migration',
        createdAt: '2024-01-10T12:00:00Z',
        sizeBytes: 12_582_912,
    },
];

const backupCapabilities: BackupCapabilities = {
    provider: 'SQLite',
    backupRestoreSupported: true,
    optimizeSupported: false,
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

function mockDatabaseRequests(
    backups: ApiBackup[] = mockApiBackups,
    capabilities: BackupCapabilities = backupCapabilities,
): void {
    vi.mocked(apiGet).mockImplementation((path: string) => {
        if (path === '/database') {
            return Promise.resolve(capabilities);
        }
        if (path === '/database/backups') {
            return Promise.resolve(backups);
        }
        return Promise.reject(new Error(`Unexpected GET ${path}`));
    });
}

beforeEach(() => {
    vi.clearAllMocks();
    mockDatabaseRequests();
    vi.mocked(apiPost).mockResolvedValue('execution-1');
});

afterEach(() => {
    vi.restoreAllMocks();
});

describe('useBackups', () => {
    it('loads backup capabilities and backups from the database endpoints', async () => {
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(apiGet).toHaveBeenCalledWith('/database');
        expect(apiGet).toHaveBeenCalledWith('/database/backups');
        expect(result.current.capabilities).toEqual(backupCapabilities);
    });

    it('does not load backups when the provider does not support restoration', async () => {
        mockDatabaseRequests([], { ...backupCapabilities, backupRestoreSupported: false });

        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(apiGet).toHaveBeenCalledWith('/database');
        expect(apiGet).not.toHaveBeenCalledWith('/database/backups');
        expect(result.current.backupRestoreSupported).toBe(false);
        expect(result.current.backups).toEqual([]);
    });

    it('transforms server backups using their supplied name, kind, and size', async () => {
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.backups).toEqual([
            {
                id: '20240115_103000',
                name: 'Scheduled 20240115_103000',
                createdAt: '2024-01-15T10:30:00Z',
                sizeBytes: 15_728_640,
                type: 'scheduled',
            },
            {
                id: '20240110_120000',
                name: 'pre-migration',
                createdAt: '2024-01-10T12:00:00Z',
                sizeBytes: 12_582_912,
                type: 'manual',
            },
        ]);
    });

    it('surfaces a database request error and returns no backups', async () => {
        vi.mocked(apiGet).mockImplementation((path: string) => {
            if (path === '/database') {
                return Promise.resolve(backupCapabilities);
            }
            return Promise.reject(new Error('Network error'));
        });

        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.backups).toEqual([]);
        expect(result.current.error).toBeInstanceOf(Error);
    });

    it('queues an unnamed database backup job', async () => {
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        let response: void;
        await act(async () => {
            response = await result.current.createBackup(undefined);
        });

        expect(apiPost).toHaveBeenCalledWith('/jobs/enqueue', {
            jobType: 'DatabaseBackup',
            parameters: undefined,
        });
        expect(response!).toBeUndefined();
    });

    it('queues a named database backup job', async () => {
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.createBackup({ name: 'before-upgrade' });
        });

        expect(apiPost).toHaveBeenCalledWith('/jobs/enqueue', {
            jobType: 'DatabaseBackup',
            parameters: { name: 'before-upgrade' },
        });
    });

    it('sets isCreating while a backup job request is pending', async () => {
        let resolveCreate: (value: string) => void;
        vi.mocked(apiPost).mockReturnValue(
            new Promise<string>((resolve) => {
                resolveCreate = resolve;
            }),
        );

        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        act(() => {
            void result.current.createBackup(undefined);
        });

        await waitFor(() => {
            expect(result.current.isCreating).toBe(true);
        });

        act(() => {
            resolveCreate!('execution-1');
        });

        await waitFor(() => {
            expect(result.current.isCreating).toBe(false);
        });
    });

    it('deletes a backup from the database endpoint', async () => {
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.deleteBackup('backup with spaces');
        });

        expect(apiDelete).toHaveBeenCalledWith('/database/backups/backup%20with%20spaces');
    });

    it('sets isDeleting while deletion is pending', async () => {
        let resolveDelete: () => void;
        vi.mocked(apiDelete).mockReturnValue(
            new Promise<void>((resolve) => {
                resolveDelete = resolve;
            }),
        );

        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        act(() => {
            void result.current.deleteBackup('20240115_103000');
        });

        await waitFor(() => {
            expect(result.current.isDeleting).toBe(true);
        });

        act(() => {
            resolveDelete!();
        });

        await waitFor(() => {
            expect(result.current.isDeleting).toBe(false);
        });
    });

    it('restores a backup through the database endpoint', async () => {
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await act(async () => {
            await result.current.restoreBackup('20240115_103000');
        });

        expect(apiPost).toHaveBeenCalledWith('/database/backups/20240115_103000/restore');
    });

    it('Refetch_ReloadsTheDeniedReasons_WhenCalled', async () => {
        // Arrange
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });
        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
        mockDatabaseRequests(mockApiBackups, {
            provider: 'SQLite',
            backupRestoreSupported: true,
            optimizeSupported: false,
            deleteDeniedReason: 'Delete needs write access to the backup directory',
        });

        // Act
        await act(async () => {
            await result.current.refetch();
        });

        // Assert
        await waitFor(() => {
            expect(result.current.capabilities?.deleteDeniedReason).toBe(
                'Delete needs write access to the backup directory',
            );
        });
    });

    it('DeleteBackup_ReloadsTheDeniedReasons_WhenTheDeleteFails', async () => {
        // Arrange
        const { result } = renderHook(() => useBackups(), { wrapper: createWrapper() });
        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });
        mockDatabaseRequests(mockApiBackups, {
            provider: 'SQLite',
            backupRestoreSupported: true,
            optimizeSupported: false,
            deleteDeniedReason: 'Delete needs write access to the backup directory',
        });
        vi.mocked(apiDelete).mockRejectedValue(new Error('Delete needs write access to the backup directory'));

        // Act
        await act(async () => {
            await result.current.deleteBackup('20240115_103000').catch(() => undefined);
        });

        // Assert
        await waitFor(() => {
            expect(result.current.capabilities?.deleteDeniedReason).toBe(
                'Delete needs write access to the backup directory',
            );
        });
    });
});
