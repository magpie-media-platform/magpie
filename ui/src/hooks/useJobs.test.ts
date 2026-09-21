import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider, type Query } from '@tanstack/react-query';
import { createElement } from 'react';

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
        const queryString = searchParams.toString();
        return queryString ? `${path}?${queryString}` : path;
    }),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPost } from '@/lib/api';
import {
    useJobs,
    useJobExecution,
    useJobSpecs,
    isTerminalJobExecutionStatus,
    terminalJobExecutionStatuses,
    type ApiJobExecution,
    type ApiJobSpec,
    type JobExecutionStatusValue,
    type PagedJobExecutions,
} from './useJobs';
import { jobQueryKeys } from './useJobs';

const mockApiJobs: ApiJobExecution[] = [
    {
        id: 'job-1',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'Content',
        status: 'Running',
        progress: 45,
        statusMessage: 'Processing items...',
        startedAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-15T10:30:00Z',
    },
    {
        id: 'job-2',
        jobType: 'SearchMissing',
        scope: 'Content',
        status: 'Queued',
        createdAt: '2024-01-15T10:32:00Z',
    },
    {
        id: 'job-3',
        scheduleTriggerId: 'trigger-1',
        jobType: 'RssSync',
        scope: 'Administrative',
        status: 'Completed',
        progress: 100,
        statusMessage: 'Synced 50 items',
        startedAt: '2024-01-15T10:00:00Z',
        completedAt: '2024-01-15T10:02:30Z',
        createdAt: '2024-01-15T10:00:00Z',
    },
];

const mockPagedResponse: PagedJobExecutions = {
    items: mockApiJobs,
    pageNumber: 1,
    pageSize: 25,
    totalItems: 3,
};

const mockSpecs: ApiJobSpec[] = [
    {
        jobType: 'ContentMetadataRefreshItem',
        displayName: 'Content metadata refresh item',
        description: 'Refreshes metadata.',
        category: 'Content',
        scope: 'Content',
        enabled: false,
        onDemand: false,
        supportedTriggerModes: ['Manual', 'Schedule', 'Event'],
        parameters: [
            {
                name: 'contentHierarchyItemId',
                description: 'The content item id.',
                type: 0,
                required: true,
                defaultValue: null,
            },
        ],
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

function createWrapperWithClient() {
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

    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);

    return { queryClient, Wrapper };
}

function getRefetchIntervalForJob(queryClient: QueryClient, id: string) {
    const cache = queryClient.getQueryCache();
    const query = cache.find({ queryKey: jobQueryKeys.detail(id) });
    expect(query, `query for ${id} should be in cache`).toBeDefined();
    const observer = query!.observers[0];
    expect(observer, `query for ${id} should have an observer`).toBeDefined();
    const refetchInterval = observer.options.refetchInterval as
        | number
        | false
        | ((query: Query<ApiJobExecution, Error>) => number | false | undefined);
    return refetchInterval;
}

describe('useJobs', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetching jobs', () => {
        it('should fetch jobs from the API', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            expect(result.current.isLoading).toBe(true);

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/jobs');
        });

        it('should return jobs from API', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.jobs).toHaveLength(3);
            expect(result.current.jobs[0].jobType).toBe('ContentMetadataRefreshItem');
            expect(result.current.jobs[1].jobType).toBe('SearchMissing');
            expect(result.current.jobs[2].jobType).toBe('RssSync');
        });

        it('should return pagination info', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.pagination).toEqual({
                pageNumber: 1,
                pageSize: 25,
                totalItems: 3,
            });
        });

        it('should pass query parameters to the API', async () => {
            mockApiGet.mockResolvedValue({ ...mockPagedResponse, items: [mockApiJobs[0]] });

            const { result } = renderHook(
                () => useJobs({ status: ['Running'], jobType: 'ContentMetadataRefreshItem' }),
                {
                    wrapper: createWrapper(),
                },
            );

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/jobs?status=Running&jobType=ContentMetadataRefreshItem');
        });

        it('should return empty array on error', async () => {
            mockApiGet.mockRejectedValue(new Error('Network error'));

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.jobs).toHaveLength(0);
            expect(result.current.error).toBeInstanceOf(Error);
        });

        it('should return empty array when API returns empty list', async () => {
            mockApiGet.mockResolvedValue({ items: [], pageNumber: 1, pageSize: 25, totalItems: 0 });

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.jobs).toHaveLength(0);
            expect(result.current.error).toBeNull();
        });
    });

    describe('running jobs', () => {
        it('should run a job via POST /jobs', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue('new-job-id');

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.runJob({ jobType: 'ContentMetadataRefreshItem' });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/jobs/enqueue', { jobType: 'ContentMetadataRefreshItem' });
        });

        it('should run immediate jobs via POST /jobs/execute-immediate', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue({ executionId: 'new-job-id', status: 'Completed' });

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.runJob({
                    jobType: 'IndexerFeedSyncScan',
                    mode: 'Immediate',
                    parameters: { dryRun: 'true' },
                });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/jobs/execute-immediate', {
                extensionId: undefined,
                jobType: 'IndexerFeedSyncScan',
                parameters: { dryRun: 'true' },
            });
        });

        it('should set isRunning while running', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolvePost: (value: string) => void;
            const postPromise = new Promise<string>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.runJob({ jobType: 'ContentMetadataRefreshItem' });
            });

            await waitFor(() => {
                expect(result.current.isRunning).toBe(true);
            });

            act(() => {
                resolvePost!('new-job-id');
            });

            await waitFor(() => {
                expect(result.current.isRunning).toBe(false);
            });
        });
    });

    describe('cancelling jobs', () => {
        it('should cancel a job via POST /jobs/{id}/cancel', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);
            mockApiPost.mockResolvedValue(undefined);

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            await act(async () => {
                await result.current.cancelJob('job-1');
            });

            expect(mockApiPost).toHaveBeenCalledWith('/jobs/job-1/cancel');
        });

        it('should set isCancelling while cancelling', async () => {
            mockApiGet.mockResolvedValue(mockPagedResponse);

            let resolvePost: () => void;
            const postPromise = new Promise<void>((resolve) => {
                resolvePost = resolve;
            });
            mockApiPost.mockReturnValue(postPromise);

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            act(() => {
                void result.current.cancelJob('job-1');
            });

            await waitFor(() => {
                expect(result.current.isCancelling).toBe(true);
            });

            act(() => {
                resolvePost!();
            });

            await waitFor(() => {
                expect(result.current.isCancelling).toBe(false);
            });
        });
    });

    describe('job data transformation', () => {
        it('should preserve all API fields', async () => {
            mockApiGet.mockResolvedValue({ ...mockPagedResponse, items: [mockApiJobs[2]] });

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const job = result.current.jobs[0];
            expect(job.id).toBe('job-3');
            expect(job.scheduleTriggerId).toBe('trigger-1');
            expect(job.jobType).toBe('RssSync');
            expect(job.scope).toBe('Administrative');
            expect(job.status).toBe('Completed');
            expect(job.progress).toBe(100);
            expect(job.statusMessage).toBe('Synced 50 items');
            expect(job.startedAt).toBe('2024-01-15T10:00:00Z');
            expect(job.completedAt).toBe('2024-01-15T10:02:30Z');
            expect(job.createdAt).toBe('2024-01-15T10:00:00Z');
        });

        it('should handle null optional fields', async () => {
            mockApiGet.mockResolvedValue({ ...mockPagedResponse, items: [mockApiJobs[1]] });

            const { result } = renderHook(() => useJobs(), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(result.current.jobs[0].scheduleTriggerId).toBeUndefined();
            expect(result.current.jobs[0].progress).toBeUndefined();
        });
    });
});

describe('useJobSpecs', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch job specs from the API', async () => {
        mockApiGet.mockResolvedValue(mockSpecs);

        const { result } = renderHook(() => useJobSpecs(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/jobs/specs');
        expect(result.current.specs).toEqual(mockSpecs);
    });

    it('should return empty specs on error', async () => {
        mockApiGet.mockRejectedValue(new Error('Network error'));

        const { result } = renderHook(() => useJobSpecs(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.specs).toEqual([]);
        expect(result.current.error).toBeInstanceOf(Error);
    });
});

describe('useJobExecution', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('should fetch a single job execution by ID', async () => {
        mockApiGet.mockResolvedValue(mockApiJobs[0]);

        const { result } = renderHook(() => useJobExecution('job-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/jobs/job-1');
        expect(result.current.job?.id).toBe('job-1');
        expect(result.current.job?.jobType).toBe('ContentMetadataRefreshItem');
    });

    it('should not fetch when disabled', async () => {
        const { result } = renderHook(() => useJobExecution('job-1', false), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.job).toBeNull();
        });

        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should not fetch when ID is empty', async () => {
        const { result } = renderHook(() => useJobExecution(''), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.job).toBeNull();
        });

        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('should return error on failure', async () => {
        mockApiGet.mockRejectedValue(new Error('Not found'));

        const { result } = renderHook(() => useJobExecution('job-1'), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.job).toBeNull();
        expect(result.current.error).toBeInstanceOf(Error);
    });

    describe('polling gating', () => {
        it('exposes the terminal-status set drawn from the JobExecutionStatus enum', () => {
            expect(Array.from(terminalJobExecutionStatuses).sort()).toEqual(['Cancelled', 'Completed', 'Failed']);
        });

        it.each<[string, JobExecutionStatusValue, boolean]>([
            ['Queued', 'Queued', false],
            ['Running', 'Running', false],
            ['Completed', 'Completed', true],
            ['Failed', 'Failed', true],
            ['Cancelled', 'Cancelled', true],
            ['CancellationRequested', 'CancellationRequested', false],
        ])('isTerminalJobExecutionStatus(%s) === %s', (_label, status, expected) => {
            expect(isTerminalJobExecutionStatus(status)).toBe(expected);
        });

        it('isTerminalJobExecutionStatus(undefined) is false', () => {
            expect(isTerminalJobExecutionStatus(undefined)).toBe(false);
        });

        it('does not poll in-flight jobs', async () => {
            const inflight: ApiJobExecution = { ...mockApiJobs[0], status: 'Running' };
            mockApiGet.mockResolvedValue(inflight);

            const { queryClient, Wrapper } = createWrapperWithClient();
            const { result } = renderHook(() => useJobExecution('job-1'), { wrapper: Wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const refetchInterval = getRefetchIntervalForJob(queryClient, 'job-1');
            expect(refetchInterval).toBeUndefined();
        });

        it.each<[string, JobExecutionStatusValue]>([
            ['Completed', 'Completed'],
            ['Failed', 'Failed'],
            ['Cancelled', 'Cancelled'],
        ])('does not poll terminal status %s', async (_label, status) => {
            const terminal: ApiJobExecution = { ...mockApiJobs[0], status };
            mockApiGet.mockResolvedValue(terminal);

            const { queryClient, Wrapper } = createWrapperWithClient();
            const { result } = renderHook(() => useJobExecution('job-1'), { wrapper: Wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const refetchInterval = getRefetchIntervalForJob(queryClient, 'job-1');
            expect(refetchInterval).toBeUndefined();
        });
    });
});
