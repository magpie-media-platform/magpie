import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPost, apiDelete } from '@/lib/api';
import { useBackups, type ApiBackup, type BackupCreateResponse } from './useBackups';

const mockApiBackups: ApiBackup[] = [
    {
        id: '20240115_103000',
        name: 'Scheduled 20240115_103000',
        createdAt: '2024-01-15T10:30:00Z',
        sizeBytes: 15728640,
    },
    {
        id: '20240114_000000',
        name: 'Scheduled 20240114_000000',
        createdAt: '2024-01-14T00:00:00Z',
        sizeBytes: 15466496,
    },
    {
        id: '20240110_120000',
        name: 'pre-migration',
        createdAt: '2024-01-10T12:00:00Z',
        sizeBytes: 12582912,
    },
];

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('useBackups', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching backups', () => {
        it('should fetch backups from the API', async () => {
            mockApiGet.mockResolvedValue(mockApiBackups);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/backups');
        });

        it('should transform API backups to UI format using server-supplied name and size', async () => {
            mockApiGet.mockResolvedValue(mockApiBackups);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups).toHaveLength(3);

            const scheduledBackup = result.current.backups.find((b) => b.id === '20240115_103000');
            expect(scheduledBackup).toBeDefined();
            expect(scheduledBackup?.name).toBe('Scheduled 20240115_103000');
            expect(scheduledBackup?.type).toBe('scheduled');
            expect(scheduledBackup?.sizeBytes).toBe(15728640);

            const manualBackup = result.current.backups.find((b) => b.id === '20240110_120000');
            expect(manualBackup).toBeDefined();
            expect(manualBackup?.name).toBe('pre-migration');
            expect(manualBackup?.type).toBe('manual');
            expect(manualBackup?.sizeBytes).toBe(12582912);
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns nothing', async () => {
            mockApiGet.mockResolvedValue([]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('creating backups', () => {
        it('should create a backup', async () => {
            mockApiGet.mockResolvedValue([]);
            const createResponse: BackupCreateResponse = { id: '20240115_150000' };
            mockApiPost.mockResolvedValue(createResponse);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            let response: BackupCreateResponse | undefined;
            await act(async () => {
                response = await result.current.createBackup();
            });

            expect(mockApiPost).toHaveBeenCalledWith('/backups', undefined);
            expect(response?.id).toBe('20240115_150000');
        });

        it('should create a backup with custom name', async () => {
            mockApiGet.mockResolvedValue([]);
            const createResponse: BackupCreateResponse = { id: 'my-custom-backup' };
            mockApiPost.mockResolvedValue(createResponse);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.createBackup({ name: 'my-custom-backup' });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/backups', { name: 'my-custom-backup' });
        });

        it('should set isCreating while creating', async () => {
            mockApiGet.mockResolvedValue([]);
            let resolveCreate: (value: BackupCreateResponse) => void;
            const createPromise = new Promise<BackupCreateResponse>((resolve) => {
                resolveCreate = resolve;
            });
            mockApiPost.mockReturnValue(createPromise);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.createBackup();
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(true);
            });

            act(() => {
                resolveCreate!({ id: 'new-backup' });
            });

            await waitFor(() => {
                expect(result.current.isCreating).toBe(false);
            });
        });
    });

    describe('deleting backups', () => {
        it('should delete a backup', async () => {
            mockApiGet.mockResolvedValue(mockApiBackups);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteBackup('20240115_103000');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/backups/20240115_103000');
        });

        it('should round-trip a user-supplied name through create and list', async () => {
            mockApiGet.mockResolvedValue([
                {
                    id: '20240115_140000',
                    name: 'before upgrade',
                    createdAt: '2024-01-15T14:00:00Z',
                    sizeBytes: 4096,
                },
            ]);
            mockApiPost.mockResolvedValue({ id: '20240115_140000' } as BackupCreateResponse);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const backup = result.current.backups[0];
            expect(backup.name).toBe('before upgrade');
            expect(backup.type).toBe('manual');
            expect(backup.sizeBytes).toBe(4096);
        });

        it('should URL-encode backup IDs', async () => {
            mockApiGet.mockResolvedValue([]);
            mockApiDelete.mockResolvedValue(undefined);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.deleteBackup('backup with spaces');
            });

            expect(mockApiDelete).toHaveBeenCalledWith('/backups/backup%20with%20spaces');
        });

        it('should set isDeleting while deleting', async () => {
            mockApiGet.mockResolvedValue(mockApiBackups);
            let resolveDelete: () => void;
            const deletePromise = new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
            mockApiDelete.mockReturnValue(deletePromise);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.deleteBackup('20240115_103000');
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
    });

    describe('restoring backups', () => {
        it('should restore a backup', async () => {
            mockApiGet.mockResolvedValue(mockApiBackups);
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.restoreBackup('20240115_103000');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/backups/20240115_103000/restore');
        });

        it('should set isRestoring while restoring', async () => {
            mockApiGet.mockResolvedValue(mockApiBackups);
            let resolveRestore: () => void;
            const restorePromise = new Promise<void>((resolve) => {
                resolveRestore = resolve;
            });
            mockApiPost.mockReturnValue(restorePromise);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                result.current.restoreBackup('20240115_103000');
            });

            await waitFor(() => {
                expect(result.current.isRestoring).toBe(true);
            });

            act(() => {
                resolveRestore!();
            });

            await waitFor(() => {
                expect(result.current.isRestoring).toBe(false);
            });
        });
    });

    describe('backup type inference', () => {
        it('treats names starting with "Scheduled " as scheduled', async () => {
            mockApiGet.mockResolvedValue([
                {
                    id: '20240115_103000',
                    name: 'Scheduled 20240115_103000',
                    createdAt: '2024-01-15T10:30:00Z',
                    sizeBytes: 1024,
                },
                {
                    id: '20240114_000000',
                    name: 'Scheduled 20240114_000000',
                    createdAt: '2024-01-14T00:00:00Z',
                    sizeBytes: 1024,
                },
            ]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups[0].type).toBe('scheduled');
            expect(result.current.backups[1].type).toBe('scheduled');
        });

        it('treats user-supplied names as manual', async () => {
            mockApiGet.mockResolvedValue([
                {
                    id: '20240110_120000',
                    name: 'pre-migration',
                    createdAt: '2024-01-10T12:00:00Z',
                    sizeBytes: 1024,
                },
                {
                    id: '20240108_080000',
                    name: 'my-backup',
                    createdAt: '2024-01-08T08:00:00Z',
                    sizeBytes: 1024,
                },
            ]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups[0].type).toBe('manual');
            expect(result.current.backups[1].type).toBe('manual');
        });
    });

    describe('backup name handling', () => {
        it('passes through the server-supplied name', async () => {
            mockApiGet.mockResolvedValue([
                {
                    id: '20240115_103000',
                    name: 'Scheduled 20240115_103000',
                    createdAt: '2024-01-15T10:30:00Z',
                    sizeBytes: 1024,
                },
            ]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups[0].name).toBe('Scheduled 20240115_103000');
        });

        it('preserves a user-typed name verbatim', async () => {
            mockApiGet.mockResolvedValue([
                {
                    id: '20240110_120000',
                    name: 'pre-migration',
                    createdAt: '2024-01-10T12:00:00Z',
                    sizeBytes: 1024,
                },
            ]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups[0].name).toBe('pre-migration');
        });
    });

    describe('size handling', () => {
        it('passes through the server-supplied size', async () => {
            mockApiGet.mockResolvedValue([
                { id: 'backup-1', name: 'backup-1', createdAt: '2024-01-15T10:30:00Z', sizeBytes: 4096 },
            ]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups[0].sizeBytes).toBe(4096);
        });

        it('falls back to 0 when the API omits sizeBytes', async () => {
            mockApiGet.mockResolvedValue([
                { id: 'backup-1', name: 'backup-1', createdAt: '2024-01-15T10:30:00Z' } as unknown as ApiBackup,
            ]);

            const { result } = renderHook(() => useBackups(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.backups[0].sizeBytes).toBe(0);
        });
    });
});
