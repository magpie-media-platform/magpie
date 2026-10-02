import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
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
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
}));

import { apiGet } from '@/lib/api';
import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { ToastList } from '@/components/ui/Toast/ToastList';
import {
    ScanCompletionTrackerProvider,
    useScanCompletionTracker,
    type ScanBatchInfo,
} from './useScanCompletionTracker';
import type { ApiJobExecution } from './useJobs';
import type { PagedLibraryImportCandidates } from './useLibraryImportCandidates';

function makePage(totalItems: number): PagedLibraryImportCandidates {
    return { items: [], pageNumber: 1, pageSize: 1, totalItems };
}

function Providers({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0, staleTime: 0 },
            mutations: { retry: false },
        },
    });
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <ToastStateProvider>
                    <ToastProvider>
                        <ScanCompletionTrackerProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ScanCompletionTrackerProvider>
                    </ToastProvider>
                </ToastStateProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

function TrackOnMount({ executionId, rootFolderName }: { executionId: string; rootFolderName: string }) {
    const { trackScan } = useScanCompletionTracker();
    useEffect(() => {
        trackScan(executionId, rootFolderName);
    }, [executionId, rootFolderName, trackScan]);
    return null;
}

function TrackBatchOnMount({
    folders,
}: {
    folders: { executionId: string; rootFolderName: string; batch: ScanBatchInfo }[];
}) {
    const { trackScan } = useScanCompletionTracker();
    useEffect(() => {
        for (const folder of folders) {
            trackScan(folder.executionId, folder.rootFolderName, folder.batch);
        }
    }, [folders, trackScan]);
    return null;
}

function makeJobExecution(overrides: Partial<ApiJobExecution> = {}): ApiJobExecution {
    return {
        id: 'exec-1',
        jobType: 'RootFolderContentDiscovery',
        scope: 'Content',
        status: 'Running',
        createdAt: new Date().toISOString(),
        ...overrides,
    } as ApiJobExecution;
}

describe('useScanCompletionTracker', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('toasts the delta of files to import when a tracked scan completes', async () => {
        let pendingCalls = 0;

        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                pendingCalls += 1;
                return makePage(pendingCalls === 1 ? 10 : 14);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ status: pendingCalls > 0 ? 'Completed' : 'Running' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        render(
            <Providers>
                <TrackOnMount executionId="exec-1" rootFolderName="Movies" />
            </Providers>,
        );

        const toast = await screen.findByText('"Movies": 4 files ready to import.');
        expect(toast).toBeInTheDocument();
        expect(screen.getByText(/Scan complete/i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Import now' })).toBeInTheDocument();
    });

    it('refreshes the import queue and content when a tracked scan completes', async () => {
        const invalidate = vi.spyOn(QueryClient.prototype, 'invalidateQueries');
        let pendingCalls = 0;
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                pendingCalls += 1;
                return makePage(0);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ status: pendingCalls > 0 ? 'Completed' : 'Running' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        render(
            <Providers>
                <TrackOnMount executionId="exec-1" rootFolderName="Movies" />
            </Providers>,
        );

        await screen.findByText('"Movies" is up to date. Nothing new to import.');
        expect(invalidate).toHaveBeenCalledWith({ queryKey: ['content'] });
    });

    it('toasts an "up to date" message when no new files are found', async () => {
        let pendingSnapshotCaptured = false;

        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                pendingSnapshotCaptured = true;
                return makePage(5);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ status: pendingSnapshotCaptured ? 'Completed' : 'Running' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        render(
            <Providers>
                <TrackOnMount executionId="exec-1" rootFolderName="Movies" />
            </Providers>,
        );

        expect(await screen.findByText('"Movies" is up to date. Nothing new to import.')).toBeInTheDocument();
    });

    it('does not state a queued count when the baseline pending read fails', async () => {
        let pendingCalls = 0;

        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                pendingCalls += 1;
                if (pendingCalls === 1) {
                    throw new Error('offline');
                }
                return makePage(14);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ status: pendingCalls > 0 ? 'Completed' : 'Running' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        render(
            <Providers>
                <TrackOnMount executionId="exec-1" rootFolderName="Movies" />
            </Providers>,
        );

        expect(
            await screen.findByText('"Movies" finished, but Magpie could not read the import queue.'),
        ).toBeInTheDocument();
        expect(screen.queryByText(/ready to import/)).not.toBeInTheDocument();
        expect(screen.queryByText(/up to date/)).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Open import' })).toBeInTheDocument();
    });

    it('does not claim nothing was queued when the post-scan pending read fails', async () => {
        let pendingCalls = 0;

        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                pendingCalls += 1;
                if (pendingCalls > 1) {
                    throw new Error('offline');
                }
                return makePage(10);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ status: pendingCalls > 0 ? 'Completed' : 'Running' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        render(
            <Providers>
                <TrackOnMount executionId="exec-1" rootFolderName="Movies" />
            </Providers>,
        );

        expect(
            await screen.findByText('"Movies" finished, but Magpie could not read the import queue.'),
        ).toBeInTheDocument();
        expect(screen.queryByText(/up to date/)).not.toBeInTheDocument();
    });

    it('toasts the batch scope label once for an all-folders batch, not each folder', async () => {
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                return makePage(0);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ id: url.slice('/jobs/'.length), status: 'Completed' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        const batchId = 'batch-1';
        render(
            <Providers>
                <TrackBatchOnMount
                    folders={[
                        {
                            executionId: 'exec-1',
                            rootFolderName: 'Movies',
                            batch: { id: batchId, label: 'All root folders', total: 2, index: 1 },
                        },
                        {
                            executionId: 'exec-2',
                            rootFolderName: 'TV',
                            batch: { id: batchId, label: 'All root folders', total: 2, index: 2 },
                        },
                    ]}
                />
            </Providers>,
        );

        expect(await screen.findByText('"All root folders" is up to date. Nothing new to import.')).toBeInTheDocument();
        expect(screen.queryByText(/"Movies"/)).not.toBeInTheDocument();
        expect(screen.queryByText(/"TV"/)).not.toBeInTheDocument();
        expect(screen.getAllByText(/is up to date/)).toHaveLength(1);
    });

    it('toasts a failure message when the scan job terminates as Failed', async () => {
        mockApiGet.mockImplementation(async (url: string) => {
            if (url.startsWith('/import/candidates')) {
                return makePage(0);
            }
            if (url.startsWith('/jobs/')) {
                return makeJobExecution({ status: 'Failed' });
            }
            throw new Error(`unexpected url ${url}`);
        });

        render(
            <Providers>
                <TrackOnMount executionId="exec-1" rootFolderName="Movies" />
            </Providers>,
        );

        expect(await screen.findByText(/scan failed/i)).toBeInTheDocument();
    });
});
