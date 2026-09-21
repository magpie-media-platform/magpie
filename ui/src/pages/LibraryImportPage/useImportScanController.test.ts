import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, render, screen, act, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, useEffect, useRef, type ReactNode } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        if (!params) {
            return path;
        }
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const query = searchParams.toString();
        return query ? `${path}?${query}` : path;
    }),
}));

import { apiGet, apiPost } from '@/lib/api';
import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { ToastList } from '@/components/ui/Toast/ToastList';
import { ScanCompletionTrackerProvider } from '@/hooks/useScanCompletionTracker';
import { useImportScanController, type ScanTarget } from './useImportScanController';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0, staleTime: 0 },
            mutations: { retry: false },
        },
    });
    const Wrapper = ({ children }: { children: ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper, queryClient };
}

function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>((res) => {
        resolve = res;
    });
    return { promise, resolve };
}

function pendingPage(totalItems: number) {
    return { items: [], pageNumber: 1, pageSize: 1, totalItems };
}

describe('useImportScanController', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('settles and can restart a scan of everything when the queued start fails', async () => {
        // Arrange
        mockApiPost
            .mockResolvedValueOnce('exec-first')
            .mockRejectedValueOnce(new Error('Server unavailable'))
            .mockResolvedValueOnce('exec-retry');
        mockApiGet.mockImplementation(async (url) =>
            url.startsWith('/jobs/')
                ? { id: url.slice('/jobs/'.length), status: 'Completed' }
                : { items: [], totalItems: 0 },
        );
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
        });
        const { result } = renderHook(() => useImportScanController(), {
            wrapper: ({ children }) => createElement(QueryClientProvider, { client: queryClient }, children),
        });

        // Act
        await act(async () =>
            result.current.startScanAll([
                { id: 'folder-1', name: 'First' },
                { id: 'folder-2', name: 'Second' },
                { id: 'folder-3', name: 'Third' },
            ]),
        );

        // Assert
        await waitFor(() => expect(result.current.isScanning).toBe(false));
        expect(result.current.executionId).toBeNull();
        expect(result.current.lastSummary).toEqual({
            rootFolderName: 'All root folders',
            outcome: 'failed',
            toImport: 0,
        });
        expect(mockApiPost).toHaveBeenCalledTimes(2);

        // Act
        await act(async () => result.current.startScan('folder-3', 'Third'));

        // Assert
        await waitFor(() =>
            expect(result.current.lastSummary).toEqual({ rootFolderName: 'Third', outcome: 'completed', toImport: 0 }),
        );
        expect(mockApiPost).toHaveBeenCalledTimes(3);
    });

    it.each(['before', 'after'])('Scan_CountUnavailable_WhenCountFetchFails %s', async (failure) => {
        // Arrange
        const counts = vi.fn().mockResolvedValue({ items: [], totalItems: 8 });
        if (failure === 'before') {
            counts.mockRejectedValueOnce(new Error('Unavailable'));
        } else {
            counts.mockResolvedValueOnce({ items: [], totalItems: 3 }).mockRejectedValueOnce(new Error('Unavailable'));
        }
        mockApiPost.mockResolvedValueOnce('exec-count');
        mockApiGet.mockImplementation(async (url) =>
            url.startsWith('/jobs/') ? { id: 'exec-count', status: 'Completed' } : counts(),
        );
        const queryClient = new QueryClient({
            defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
        });
        const { result } = renderHook(() => useImportScanController(), {
            wrapper: ({ children }) => createElement(QueryClientProvider, { client: queryClient }, children),
        });

        // Act
        await act(async () => result.current.startScanAll([{ id: 'folder-1', name: 'Archive' }]));

        // Assert
        await waitFor(() =>
            expect(result.current.lastSummary).toEqual({
                rootFolderName: 'All root folders',
                outcome: 'completed',
                toImport: null,
            }),
        );
        expect(result.current.isScanning).toBe(false);
    });

    it('exposes live progress + status message from the running job', async () => {
        mockApiPost.mockResolvedValue('exec-1');
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                return {
                    id: 'exec-1',
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: 'Running',
                    progress: 55,
                    statusMessage: 'Scanning a.mkv.',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                return pendingPage(0);
            }
            throw new Error(`unexpected ${url}`);
        });

        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useImportScanController(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.startScan('folder-1', 'Movies');
        });

        await waitFor(() => {
            expect(result.current.isScanning).toBe(true);
            expect(result.current.progress).toBe(55);
            expect(result.current.statusMessage).toBe('Scanning a.mkv.');
        });
    });

    it('computes the completion summary as the pending-rows total delta', async () => {
        mockApiPost.mockResolvedValue('exec-done');
        let pendingTotal = 0;
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                return {
                    id: 'exec-done',
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: 'Completed',
                    progress: 100,
                    statusMessage: 'Scan complete.',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                return pendingPage(pendingTotal);
            }
            throw new Error(`unexpected ${url}`);
        });

        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useImportScanController(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.startScan('folder-1', 'Movies');
            pendingTotal = 32;
        });

        await waitFor(() => {
            expect(result.current.lastSummary).not.toBeNull();
        });
        expect(result.current.lastSummary).toMatchObject({
            rootFolderName: 'Movies',
            outcome: 'completed',
            toImport: 32,
        });
        expect(result.current.isScanning).toBe(false);
    });

    it('re-enables the controls when the job reports 100% complete before its status flips to terminal', async () => {
        mockApiPost.mockResolvedValue('exec-lag');
        let pendingTotal = 0;
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                return {
                    id: 'exec-lag',
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: 'Running',
                    progress: 100,
                    statusMessage: 'Scan complete.',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                return pendingPage(pendingTotal);
            }
            throw new Error(`unexpected ${url}`);
        });

        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useImportScanController(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.startScan('folder-1', 'Movies');
            pendingTotal = 7;
        });

        await waitFor(() => {
            expect(result.current.isScanning).toBe(false);
        });
        expect(result.current.lastSummary).toMatchObject({
            rootFolderName: 'Movies',
            outcome: 'completed',
            toImport: 7,
        });
    });

    it('scans every folder in sequence, sums toImport, and downgrades the aggregate outcome on a failed folder', async () => {
        const folders = [
            { id: 'folder-1', name: 'Movies' },
            { id: 'folder-2', name: 'Shows' },
        ];
        const jobStatusByExec: Record<string, string> = {
            'exec-1': 'Completed',
            'exec-2': 'Failed',
        };
        const importsByFolder: Record<string, number> = {
            'folder-1': 10,
            'folder-2': 5,
        };
        const pendingCallCount: Record<string, number> = {};

        let postCount = 0;
        mockApiPost.mockImplementation(async () => {
            postCount += 1;
            return `exec-${postCount}`;
        });

        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                const id = url.slice('/jobs/'.length);
                return {
                    id,
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: jobStatusByExec[id] ?? 'Running',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                const params = new URLSearchParams(url.split('?')[1] ?? '');
                const folderId = params.get('rootFolderId') ?? '';
                const seen = pendingCallCount[folderId] ?? 0;
                pendingCallCount[folderId] = seen + 1;
                return pendingPage(seen === 0 ? 0 : (importsByFolder[folderId] ?? 0));
            }
            throw new Error(`unexpected ${url}`);
        });

        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useImportScanController(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.startScanAll(folders);
        });

        await waitFor(() => {
            expect(result.current.lastSummary).not.toBeNull();
        });

        const scanCalls = mockApiPost.mock.calls.map((call) => call[0]);
        expect(scanCalls).toEqual(['/jobs/enqueue', '/jobs/enqueue']);

        expect(result.current.lastSummary).toMatchObject({
            rootFolderName: 'All root folders',
            outcome: 'failed',
            toImport: 15,
        });
        expect(result.current.isScanning).toBe(false);
    });

    it('resets the batch at completion even if the first folder reports a late poll mid-advance', async () => {
        const folders = [
            { id: 'folder-1', name: 'Movies' },
            { id: 'folder-2', name: 'Shows' },
        ];
        const importsByFolder: Record<string, number> = { 'folder-1': 10, 'folder-2': 5 };
        const pendingCallCount: Record<string, number> = {};

        const secondPost = deferred<string>();
        let postCount = 0;
        mockApiPost.mockImplementation(async () => {
            postCount += 1;
            if (postCount === 2) {
                return secondPost.promise;
            }
            return `exec-${postCount}`;
        });

        const jobProgressById: Record<string, { status: string; progress: number }> = {
            'exec-1': { status: 'Running', progress: 100 },
            'exec-2': { status: 'Running', progress: 100 },
        };
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                const id = url.slice('/jobs/'.length);
                const state = jobProgressById[id] ?? { status: 'Running', progress: 100 };
                return {
                    id,
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: state.status,
                    progress: state.progress,
                    statusMessage: 'Scan complete.',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                const params = new URLSearchParams(url.split('?')[1] ?? '');
                const folderId = params.get('rootFolderId') ?? '';
                const seen = pendingCallCount[folderId] ?? 0;
                pendingCallCount[folderId] = seen + 1;
                return pendingPage(seen === 0 ? 0 : (importsByFolder[folderId] ?? 0));
            }
            throw new Error(`unexpected ${url}`);
        });

        const { Wrapper, queryClient } = createWrapper();
        const { result } = renderHook(() => useImportScanController(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.startScanAll(folders);
        });

        await waitFor(() => {
            expect(postCount).toBe(2);
        });

        await act(async () => {
            queryClient.setQueryData(['jobs', 'detail', 'exec-1'], {
                id: 'exec-1',
                jobType: 'RootFolderContentDiscovery',
                scope: 'Content',
                status: 'Completed',
                progress: 100,
                statusMessage: 'Scan complete.',
                createdAt: '2024-01-01T00:00:00Z',
            });
        });

        await act(async () => {
            secondPost.resolve('exec-2');
            await secondPost.promise;
        });

        await waitFor(() => {
            expect(result.current.isScanning).toBe(false);
        });
        expect(result.current.statusMessage).toBeNull();
        expect(result.current.lastSummary).toMatchObject({
            rootFolderName: 'All root folders',
            outcome: 'completed',
            toImport: 15,
        });
    });

    it('toasts the all-folders batch scope, not the last folder, when scanning every root folder', async () => {
        const folders: ScanTarget[] = [
            { id: 'folder-1', name: 'Movies' },
            { id: 'folder-2', name: 'TV' },
        ];

        let postCount = 0;
        mockApiPost.mockImplementation(async () => {
            postCount += 1;
            return `exec-${postCount}`;
        });

        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                const id = url.slice('/jobs/'.length);
                return {
                    id,
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: 'Completed',
                    progress: 100,
                    statusMessage: 'Scan complete.',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                return pendingPage(0);
            }
            throw new Error(`unexpected ${url}`);
        });

        const Harness = () => {
            const scan = useImportScanController();
            const started = useRef(false);
            useEffect(() => {
                if (started.current) {
                    return;
                }
                started.current = true;
                void scan.startScanAll(folders);
            }, [scan]);
            return null;
        };

        const Providers = ({ children }: { children: ReactNode }) =>
            createElement(
                QueryClientProvider,
                {
                    client: new QueryClient({
                        defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
                    }),
                },
                createElement(
                    MemoryRouter,
                    null,
                    createElement(
                        ToastStateProvider,
                        null,
                        createElement(
                            ToastProvider,
                            null,
                            createElement(
                                ScanCompletionTrackerProvider,
                                null,
                                children,
                                createElement(ToastList),
                                createElement(ToastViewport),
                            ),
                        ),
                    ),
                ),
            );

        render(createElement(Providers, null, createElement(Harness)));

        expect(await screen.findByText('"All root folders" is up to date. Nothing new to import.')).toBeInTheDocument();
        expect(screen.queryByText(/"Movies"/)).not.toBeInTheDocument();
        expect(screen.queryByText(/"TV"/)).not.toBeInTheDocument();
    });

    it('reports a failed outcome when the scan job fails', async () => {
        mockApiPost.mockResolvedValue('exec-fail');
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/jobs/')) {
                return {
                    id: 'exec-fail',
                    jobType: 'RootFolderContentDiscovery',
                    scope: 'Content',
                    status: 'Failed',
                    createdAt: '2024-01-01T00:00:00Z',
                };
            }
            if (url.startsWith('/import/candidates')) {
                return pendingPage(0);
            }
            throw new Error(`unexpected ${url}`);
        });

        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useImportScanController(), { wrapper: Wrapper });

        await act(async () => {
            await result.current.startScan('folder-1', 'Movies');
        });

        await waitFor(() => {
            expect(result.current.lastSummary?.outcome).toBe('failed');
        });
    });
});
