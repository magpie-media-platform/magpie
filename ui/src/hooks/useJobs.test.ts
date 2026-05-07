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
    isTerminalJobExecutionStatus,
    jobExecutionStatusLabels,
    jobExecutionStatusVariants,
    jobScopeLabels,
    terminalJobExecutionStatuses,
    type ApiJobExecution,
    type JobExecutionStatusValue,
    type PagedJobExecutions,
} from './useJobs';
import { jobQueryKeys } from './useJobs';
import { POLLING_INTERVALS } from '@/lib/queryClient';

const mockApiJobs: ApiJobExecution[] = [
    {
        id: 'job-1',
        jobType: 'RefreshMetadata',
        scope: 2,
        status: 2,
        progress: 45,
        statusMessage: 'Processing items...',
        startedAt: '2024-01-15T10:30:00Z',
        createdAt: '2024-01-15T10:30:00Z',
    },
    {
        id: 'job-2',
        jobType: 'SearchMissing',
        scope: 2,
        status: 1,
        createdAt: '2024-01-15T10:32:00Z',
    },
    {
        id: 'job-3',
        scheduleTriggerId: 'trigger-1',
        jobType: 'RssSync',
        scope: 1,
        status: 3,
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

// Read the refetchInterval option that React Query stores on the active query
// observer. The hook passes a function (query) => 5000 | false; we invoke it
// directly with a fake Query whose data carries the status under test, so the
// gating logic is exercised without timers or real polling.
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
            expect(result.current.jobs[0].jobType).toBe('RefreshMetadata');
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

            const { result } = renderHook(() => useJobs({ status: 2, jobType: 'RefreshMetadata' }), {
                wrapper: createWrapper(),
            });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            expect(mockApiGet).toHaveBeenCalledWith('/jobs?status=2&jobType=RefreshMetadata');
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
                await result.current.runJob({ jobType: 'RefreshMetadata' });
            });

            expect(mockApiPost).toHaveBeenCalledWith('/jobs', { jobType: 'RefreshMetadata' });
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
                result.current.runJob({ jobType: 'RefreshMetadata' });
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
                result.current.cancelJob('job-1');
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
            expect(job.scope).toBe(1);
            expect(job.status).toBe(3);
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

    describe('status labels and variants', () => {
        it('should export status labels for all values', () => {
            expect(jobExecutionStatusLabels[0]).toBe('Created');
            expect(jobExecutionStatusLabels[1]).toBe('Queued');
            expect(jobExecutionStatusLabels[2]).toBe('Running');
            expect(jobExecutionStatusLabels[3]).toBe('Completed');
            expect(jobExecutionStatusLabels[4]).toBe('Failed');
            expect(jobExecutionStatusLabels[5]).toBe('Cancelled');
            expect(jobExecutionStatusLabels[6]).toBe('Cancellation Requested');
        });

        it('should export status variants for all values', () => {
            expect(jobExecutionStatusVariants[0]).toBe('created');
            expect(jobExecutionStatusVariants[1]).toBe('queued');
            expect(jobExecutionStatusVariants[2]).toBe('running');
            expect(jobExecutionStatusVariants[3]).toBe('completed');
            expect(jobExecutionStatusVariants[4]).toBe('failed');
            expect(jobExecutionStatusVariants[5]).toBe('cancelled');
            expect(jobExecutionStatusVariants[6]).toBe('cancelling');
        });

        it('should export scope labels for all values', () => {
            expect(jobScopeLabels[0]).toBe('System');
            expect(jobScopeLabels[1]).toBe('Administrative');
            expect(jobScopeLabels[2]).toBe('Content');
            expect(jobScopeLabels[3]).toBe('User');
        });
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
        expect(result.current.job?.jobType).toBe('RefreshMetadata');
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
            // Mirrors Magpie.Models.Enums.JobExecutionStatus terminal members:
            // Completed (3), Failed (4), Cancelled (5).
            expect(Array.from(terminalJobExecutionStatuses).sort()).toEqual([3, 4, 5]);
        });

        it.each<[string, JobExecutionStatusValue, boolean]>([
            ['Created (0)', 0, false],
            ['Queued (1)', 1, false],
            ['Running (2)', 2, false],
            ['Completed (3)', 3, true],
            ['Failed (4)', 4, true],
            ['Cancelled (5)', 5, true],
            ['CancellationRequested (6)', 6, false],
        ])('isTerminalJobExecutionStatus(%s) === %s', (_label, status, expected) => {
            expect(isTerminalJobExecutionStatus(status)).toBe(expected);
        });

        it('isTerminalJobExecutionStatus(undefined) is false', () => {
            expect(isTerminalJobExecutionStatus(undefined)).toBe(false);
        });

        it('refetchInterval evaluates to fast polling for in-flight jobs', async () => {
            const inflight: ApiJobExecution = { ...mockApiJobs[0], status: 2 };
            mockApiGet.mockResolvedValue(inflight);

            const { queryClient, Wrapper } = createWrapperWithClient();
            const { result } = renderHook(() => useJobExecution('job-1'), { wrapper: Wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const refetchInterval = getRefetchIntervalForJob(queryClient, 'job-1');
            expect(typeof refetchInterval).toBe('function');
            const fakeQuery = { state: { data: inflight } } as unknown as Query<ApiJobExecution, Error>;
            expect((refetchInterval as (q: Query<ApiJobExecution, Error>) => number | false)(fakeQuery)).toBe(
                POLLING_INTERVALS.fast,
            );
        });

        it.each<[string, JobExecutionStatusValue]>([
            ['Completed', 3],
            ['Failed', 4],
            ['Cancelled', 5],
        ])('refetchInterval evaluates to false for terminal status %s', async (_label, status) => {
            const terminal: ApiJobExecution = { ...mockApiJobs[0], status };
            mockApiGet.mockResolvedValue(terminal);

            const { queryClient, Wrapper } = createWrapperWithClient();
            const { result } = renderHook(() => useJobExecution('job-1'), { wrapper: Wrapper });

            await waitFor(() => {
                expect(result.current.isLoading).toBe(false);
            });

            const refetchInterval = getRefetchIntervalForJob(queryClient, 'job-1');
            expect(typeof refetchInterval).toBe('function');
            const fakeQuery = { state: { data: terminal } } as unknown as Query<ApiJobExecution, Error>;
            expect((refetchInterval as (q: Query<ApiJobExecution, Error>) => number | false)(fakeQuery)).toBe(false);
        });
    });
});
