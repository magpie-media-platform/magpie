import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, useSearchParams } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';
import type { ApiJobSpec, ApiJobExecution, JobExecutionStatusValue, JobScopeValue } from '@/hooks/useJobs';
import type { ApiScheduleTrigger } from '@/hooks/useScheduleTriggers';
import type { ApiEventTrigger } from '@/hooks/useEventTriggers';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
        apiDelete: vi.fn(),
    };
});

import { apiGet, apiPatch, apiPost } from '@/lib/api';

import { JobsPage } from './JobsPage';

const mockApiGet = vi.mocked(apiGet);
const mockApiPatch = vi.mocked(apiPatch);
const mockApiPost = vi.mocked(apiPost);

const mockJobs: ApiJobExecution[] = [
    {
        id: 'exec-1',
        jobType: 'RSS Sync',
        scope: 'Content' as JobScopeValue,
        status: 'Running' as JobExecutionStatusValue,
        progress: 45,
        createdAt: '2024-01-15T10:30:00Z',
        startedAt: '2024-01-15T10:30:00Z',
        completedAt: null,
        statusMessage: 'Checking feed: NZBgeek',
        correlationId: 'corr-alpha',
    },
    {
        id: 'exec-2',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'Content' as JobScopeValue,
        status: 'Running' as JobExecutionStatusValue,
        progress: 78,
        createdAt: '2024-01-15T10:28:00Z',
        startedAt: '2024-01-15T10:28:00Z',
        completedAt: null,
        statusMessage: 'Processing: Breaking Bad S05E16',
        correlationId: 'corr-alpha',
    },
    {
        id: 'exec-3',
        jobType: 'Search Missing',
        scope: 'Content' as JobScopeValue,
        status: 'Queued' as JobExecutionStatusValue,
        progress: 0,
        createdAt: '2024-01-15T10:32:00Z',
        startedAt: null,
        completedAt: null,
        statusMessage: 'Waiting in queue',
    },
    {
        id: 'exec-4',
        jobType: 'Cleanup Downloads',
        scope: 'Content' as JobScopeValue,
        status: 'Completed' as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T10:00:00Z',
        startedAt: '2024-01-15T10:00:00Z',
        completedAt: '2024-01-15T10:02:30Z',
        statusMessage: 'Removed 5 completed downloads',
    },
    {
        id: 'exec-5',
        jobType: 'Sync Plex Library',
        scope: 'Content' as JobScopeValue,
        status: 'Completed' as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T09:45:00Z',
        startedAt: '2024-01-15T09:45:00Z',
        completedAt: '2024-01-15T09:46:15Z',
        statusMessage: 'Synced 1,234 items',
    },
    {
        id: 'exec-6',
        jobType: 'Backup Database',
        scope: 'Administrative' as JobScopeValue,
        status: 'Completed' as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T09:00:00Z',
        startedAt: '2024-01-15T09:00:00Z',
        completedAt: '2024-01-15T09:00:45Z',
        statusMessage: 'Backup created: magpie_20240115_090000.db',
    },
    {
        id: 'exec-7',
        jobType: 'Import Provider I List',
        scope: 'Content' as JobScopeValue,
        status: 'Failed' as JobExecutionStatusValue,
        progress: 34,
        createdAt: '2024-01-15T08:30:00Z',
        startedAt: '2024-01-15T08:30:00Z',
        completedAt: '2024-01-15T08:31:12Z',
        statusMessage: 'API error: Rate limit exceeded',
    },
    {
        id: 'exec-8',
        jobType: 'RSS Sync',
        scope: 'Content' as JobScopeValue,
        status: 'Cancelled' as JobExecutionStatusValue,
        progress: 22,
        createdAt: '2024-01-15T07:30:00Z',
        startedAt: '2024-01-15T07:30:00Z',
        completedAt: '2024-01-15T07:31:00Z',
        statusMessage: 'Cancelled by user',
    },
];

const mockScheduleTriggers: ApiScheduleTrigger[] = [
    {
        id: 'trigger-1',
        name: 'Hourly RSS Sync',
        jobType: 'RSS Sync',
        scope: 'Content' as JobScopeValue,
        schedule: '0 * * * *',
        executionMode: 'Queue',
        enabled: true,
        nextRunAt: '2024-01-15T12:00:00Z',
        lastRunAt: '2024-01-15T11:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-2',
        name: 'Daily Metadata Refresh',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'Content' as JobScopeValue,
        schedule: '0 6 * * *',
        executionMode: 'Queue',
        enabled: true,
        nextRunAt: '2024-01-16T06:00:00Z',
        lastRunAt: '2024-01-15T06:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-3',
        name: 'Weekly Database Backup',
        jobType: 'Backup Database',
        scope: 'Administrative' as JobScopeValue,
        schedule: '0 0 * * 0',
        executionMode: 'Queue',
        enabled: true,
        nextRunAt: '2024-01-21T00:00:00Z',
        lastRunAt: '2024-01-14T00:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-4',
        name: 'Nightly Missing Search',
        jobType: 'Search Missing',
        scope: 'Content' as JobScopeValue,
        schedule: '0 2 * * *',
        executionMode: 'Queue',
        enabled: false,
        nextRunAt: null,
        lastRunAt: '2024-01-14T02:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-5',
        name: 'Plex Library Sync',
        jobType: 'Sync Plex Library',
        scope: 'Content' as JobScopeValue,
        schedule: '0 */6 * * *',
        executionMode: 'Queue',
        enabled: true,
        nextRunAt: '2024-01-15T18:00:00Z',
        lastRunAt: '2024-01-15T12:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockEventTriggers: ApiEventTrigger[] = [
    {
        id: 'event-1',
        name: 'Refresh on Download Complete',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'Content' as JobScopeValue,
        eventFilter: {
            eventName: { value: 'download\\.completed' },
            eventContext: {
                items: {
                    'content.type': { operator: 'Regex', value: '^kind-x$', negate: true },
                },
            },
        },
        enabled: true,
        delaySeconds: 0,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-2',
        name: 'Search on Content Added',
        jobType: 'Search Missing',
        scope: 'Content' as JobScopeValue,
        eventFilter: {
            eventName: { value: 'content\\.added' },
            eventContext: {
                items: {
                    contentType: { value: 'episode' },
                },
            },
        },
        enabled: true,
        delaySeconds: 0,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-3',
        name: 'Sync Plex on Import',
        jobType: 'Sync Plex Library',
        scope: 'Content' as JobScopeValue,
        eventFilter: {
            eventName: { value: 'file\\.imported' },
        },
        enabled: true,
        delaySeconds: 0,
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockJobSpecs: ApiJobSpec[] = [
    {
        jobType: 'ContentMetadataRefreshItem',
        displayName: 'Content metadata refresh item',
        description: 'Refreshes metadata for one content item.',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: false,
        onDemand: false,
        supportedTriggerModes: ['Manual', 'Schedule', 'Event'],
        parameters: [],
    },
    {
        jobType: 'ReleaseSearchItem',
        displayName: 'Release search item',
        description: 'Searches available releases for one content item.',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: false,
        onDemand: false,
        supportedTriggerModes: ['Manual', 'Schedule', 'Event'],
        parameters: [],
    },
];

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
}

function TestWrapper({
    children,
    role = Role.Admin,
    initialRoute = '/',
    queryClient,
}: {
    children: ReactNode;
    role?: Role;
    initialRoute?: string;
    queryClient?: QueryClient;
}) {
    const mockUser = { id: '1', username: 'test', role };
    const client = queryClient ?? createTestQueryClient();

    return (
        <QueryClientProvider client={client}>
            <MemoryRouter initialEntries={[initialRoute]}>
                <AuthProvider initialUser={mockUser} initialPermissions={testEffectivePermissions()}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

const EXECUTIONS_PAGE_SIZE = 50;

function countByStatus(jobs: ApiJobExecution[]): Record<string, number> {
    const counts: Record<string, number> = {};
    for (const job of jobs) {
        counts[job.status] = (counts[job.status] ?? 0) + 1;
    }
    return counts;
}

function pageJobs(url: string, source: ApiJobExecution[]) {
    const query = url.includes('?') ? new URLSearchParams(url.slice(url.indexOf('?') + 1)) : new URLSearchParams();

    const jobType = query.get('jobType');
    const wireStatuses = query.getAll('status');
    const correlationId = query.get('correlationId');
    const pageNumber = Number.parseInt(query.get('pageNumber') ?? '1', 10) || 1;
    const pageSize = Number.parseInt(query.get('pageSize') ?? String(EXECUTIONS_PAGE_SIZE), 10) || EXECUTIONS_PAGE_SIZE;

    let filtered = [...source];
    if (jobType) {
        filtered = filtered.filter((job) => job.jobType === jobType);
    }
    if (wireStatuses.length > 0) {
        const allowed = new Set(wireStatuses);
        filtered = filtered.filter((job) => allowed.has(job.status));
    }
    if (correlationId) {
        filtered = filtered.filter((job) => job.correlationId === correlationId);
    }
    filtered.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());

    const start = (pageNumber - 1) * pageSize;
    return {
        items: filtered.slice(start, start + pageSize),
        pageNumber,
        pageSize,
        totalItems: filtered.length,
    };
}

function defaultApiGetResponse(url: string) {
    if (url === '/jobs/specs') {
        return Promise.resolve(mockJobSpecs);
    }
    if (url === '/jobs/status-counts') {
        return Promise.resolve(countByStatus(mockJobs));
    }
    if (url === '/jobs' || url.startsWith('/jobs?')) {
        return Promise.resolve(pageJobs(url, mockJobs));
    }
    if (url === '/schedule-triggers') {
        return Promise.resolve(mockScheduleTriggers);
    }
    if (url === '/event-triggers' || url.startsWith('/event-triggers?')) {
        return Promise.resolve(mockEventTriggers);
    }
    return Promise.reject(new Error(`Unmocked URL: ${url}`));
}

function cleanupRadixBodyState() {
    document.querySelectorAll('[data-radix-focus-guard]').forEach((element) => element.remove());
    document.body.style.pointerEvents = '';
    document.body.removeAttribute('data-scroll-locked');
    if (document.body.getAttribute('style') === '') {
        document.body.removeAttribute('style');
    }
}

beforeEach(() => {
    vi.clearAllMocks();
    cleanupRadixBodyState();
    mockApiGet.mockImplementation(defaultApiGetResponse);
    mockApiPatch.mockResolvedValue(undefined);
    mockApiPost.mockResolvedValue('new-job-id');
});

afterEach(() => {
    vi.restoreAllMocks();
    cleanupRadixBodyState();
});

describe('JobsPage', () => {
    it('renders with correct data-page attribute', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="jobs"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'System Jobs', level: 1 })).toBeInTheDocument();
    });

    it('renders the system-scoped page description', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText('View job history and configure automatic triggers.')).toBeInTheDocument();
    });

    it('does not render a title count adornment', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /job history/i })).toBeInTheDocument();
        });

        expect(screen.queryByTestId('jobs-page__count')).not.toBeInTheDocument();
    });

    it('renders the page icon as decorative', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const icon = screen.getByTestId('jobs-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('JobsPage tabs', () => {
    it('renders History, Scheduled Jobs, and Event Triggers tabs', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /history/i })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /scheduled jobs/i })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /event triggers/i })).toBeInTheDocument();
    });

    it('History tab is selected by default', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const executionsTab = screen.getByRole('tab', { name: /history/i });
        expect(executionsTab).toHaveAttribute('data-state', 'active');
    });

    it('switches to Scheduled Jobs tab when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /scheduled jobs/i });
        await user.click(schedulesTab);

        expect(schedulesTab).toHaveAttribute('data-state', 'active');
    });

    it('does not render count pills on the tabs', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('.tab-count')).not.toBeInTheDocument();
    });
});

describe('JobsPage Executions tab', () => {
    it('renders execution list', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(await screen.findByRole('list', { name: /job history/i })).toBeInTheDocument();
    });

    it('renders execution items', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(8);
        });
    });

    it('does not render status summary pills', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /job history/i })).toBeInTheDocument();
        });

        expect(document.querySelector('[aria-label="Executions by status"]')).not.toBeInTheDocument();
    });

    it('renders the run-a-job button without a page caution', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(
            within(document.querySelector('.jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        ).toBeInTheDocument();
        expect(screen.queryByTestId('jobs-page__run-caution')).not.toBeInTheDocument();
    });

    it('shows the reason on Run a job when no job can run', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );
        await screen.findByRole('list', { name: /job history/i });

        await user.hover(
            within(document.querySelector('.jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        );

        expect(await screen.findByRole('tooltip')).toHaveTextContent('No on-demand jobs are available to run');
    });

    it('RunAJob_ShowsNoReason_WhenTheJobListFailsToLoad', async () => {
        // Arrange
        const queryClient = createTestQueryClient();
        mockApiGet.mockImplementation((url: string) =>
            url === '/jobs/specs' ? Promise.reject(new Error('Request failed')) : defaultApiGetResponse(url),
        );

        // Act
        render(
            <TestWrapper role={Role.Admin} queryClient={queryClient}>
                <JobsPage />
            </TestWrapper>,
        );
        await screen.findByRole('list', { name: /job history/i });
        await waitFor(() => expect(queryClient.getQueryState(['jobs', 'specs', 'all'])?.status).toBe('error'));

        // Assert
        expect(
            within(document.querySelector('.jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        ).toBeDisabled();
        expect(screen.queryByText('No on-demand jobs are available to run')).not.toBeInTheDocument();
    });

    it('RunAJob_ShowsTheRoleReason_WhenTheJobListFailsToLoadForAViewer', async () => {
        // Arrange
        mockApiGet.mockImplementation((url: string) =>
            url === '/jobs/specs' ? Promise.reject(new Error('Request failed')) : defaultApiGetResponse(url),
        );

        // Act
        render(
            <TestWrapper role={Role.Viewer}>
                <JobsPage />
            </TestWrapper>,
        );
        await screen.findByRole('list', { name: /job history/i });

        // Assert
        expect(screen.getByText('Requires Manager role')).toBeInTheDocument();
    });

    it('limits manager manual run options to backend-allowed job scopes', async () => {
        const user = userEvent.setup();
        const scopedSpecs: ApiJobSpec[] = [
            {
                jobType: 'ContentManual',
                displayName: 'Content Manual',
                category: 'Content',
                scope: 'Content' as JobScopeValue,
                enabled: true,
                onDemand: true,
                supportedTriggerModes: ['Manual'],
                parameters: [],
            },
            {
                jobType: 'UserManual',
                displayName: 'User Manual',
                category: 'Content',
                scope: 'User' as JobScopeValue,
                enabled: true,
                onDemand: true,
                supportedTriggerModes: ['Manual'],
                parameters: [],
            },
            {
                jobType: 'AdministrativeManual',
                displayName: 'Administrative Manual',
                category: 'System',
                scope: 'Administrative' as JobScopeValue,
                enabled: true,
                onDemand: true,
                supportedTriggerModes: ['Manual'],
                parameters: [],
            },
            {
                jobType: 'SystemManual',
                displayName: 'System Manual',
                category: 'System',
                scope: 'System' as JobScopeValue,
                enabled: true,
                onDemand: true,
                supportedTriggerModes: ['Manual'],
                parameters: [],
            },
        ];
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(scopedSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve(countByStatus(mockJobs));
            if (url === '/jobs' || url.startsWith('/jobs?')) return Promise.resolve(pageJobs(url, mockJobs));
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(
            within(document.querySelector('.jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);

        expect(screen.getByRole('option', { name: 'Content Manual' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'User Manual' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Administrative Manual' })).not.toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'System Manual' })).not.toBeInTheDocument();
    });

    it('renders filters component', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="job-execution-filters"]')).toBeInTheDocument();
    });

    it('applies data-execution-id to execution items', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const items = document.querySelectorAll('[data-execution-id]');
            expect(items.length).toBe(8);
        });
    });

    it('shows progress bar for running executions', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const progressBars = screen.getAllByTestId('execution-row__progress-bar');
            expect(progressBars.length).toBeGreaterThan(0);
        });
    });

    it('shows execution messages', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Checking feed: NZBgeek')).toBeInTheDocument();
            expect(screen.getByText(/API error: Rate limit exceeded/)).toBeInTheDocument();
        });
    });
});

describe('JobsPage Executions tab - filtering', () => {
    it('filters executions by status', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);

        const failedOption = screen.getByRole('option', { name: 'Failed' });
        await user.click(failedOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('filters executions by template (jobType)', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        await user.click(templateTrigger);

        const rssSyncOption = screen.getByRole('option', { name: 'RSS Sync' });
        await user.click(rssSyncOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(2);
        });
    });

    it('shows empty state when filters match nothing', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        await user.click(templateTrigger);

        const backupOption = screen.getByRole('option', { name: 'Backup Database' });
        await user.click(backupOption);

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);

        const runningOption = screen.getByRole('option', { name: 'Running' });
        await user.click(runningOption);

        await waitFor(() => {
            expect(screen.getByText('No job history found')).toBeInTheDocument();
            expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();
        });
    });
});

describe('JobsPage Executions tab - server paging', () => {
    function makeManyJobs(count: number): ApiJobExecution[] {
        return Array.from({ length: count }, (_, i) => ({
            id: `exec-${i}`,
            jobType: 'RSS Sync',
            scope: 'Content' as JobScopeValue,
            status: 'Completed' as JobExecutionStatusValue,
            progress: 100,
            createdAt: new Date(Date.UTC(2024, 0, 1, 0, 0, count - i)).toISOString(),
            startedAt: '2024-01-01T00:00:00Z',
            completedAt: '2024-01-01T00:00:01Z',
            statusMessage: `Run ${i}`,
        }));
    }

    it('renders only a bounded page of rows regardless of total count', async () => {
        const many = makeManyJobs(130);
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve(countByStatus(many));
            if (url === '/jobs' || url.startsWith('/jobs?')) return Promise.resolve(pageJobs(url, many));
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(EXECUTIONS_PAGE_SIZE);
        });

        expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
    });

    it('advances to the next page and requests the next pageNumber from the server', async () => {
        const user = userEvent.setup();
        const many = makeManyJobs(130);
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve(countByStatus(many));
            if (url === '/jobs' || url.startsWith('/jobs?')) return Promise.resolve(pageJobs(url, many));
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-execution-id="exec-0"]')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /next page/i }));

        await waitFor(() => {
            expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
        });

        await waitFor(() => {
            expect(document.querySelector('[data-execution-id="exec-50"]')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-execution-id="exec-0"]')).not.toBeInTheDocument();

        const lastJobsCall = mockApiGet.mock.calls
            .map((c) => c[0] as string)
            .filter((u) => u.startsWith('/jobs?'))
            .pop();
        expect(lastJobsCall).toContain('pageNumber=2');
    });

    it('does not render the pager when results fit on one page', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });

        expect(document.querySelector('[data-component="executions-pager"]')).not.toBeInTheDocument();
    });

    it('renders first and last page controls in the shared pager', async () => {
        const many = makeManyJobs(130);
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve(countByStatus(many));
            if (url === '/jobs' || url.startsWith('/jobs?')) return Promise.resolve(pageJobs(url, many));
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /first page/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /last page/i })).toBeInTheDocument();
    });
});

describe('JobsPage Executions tab - execution details', () => {
    it('execution rows are expandable', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const executionTriggers = await screen.findAllByTestId('execution-row__trigger');
        expect(executionTriggers.length).toBeGreaterThan(0);
    });

    it('clicking an execution row expands to show details', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const [firstTrigger] = await screen.findAllByTestId('execution-row__trigger');
        await user.click(firstTrigger);

        const [logs] = await screen.findAllByTestId('execution-row__logs');
        expect(logs).toHaveAttribute('data-state', 'open');
    });

    it('shows only the execution linked by the job query parameter, with a way back', async () => {
        const scrollIntoView = vi.spyOn(Element.prototype, 'scrollIntoView');
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/exec-2') {
                return Promise.resolve(mockJobs[1]);
            }
            return defaultApiGetResponse(url);
        });

        render(
            <TestWrapper initialRoute="/?job=exec-2">
                <JobsPage />
            </TestWrapper>,
        );

        const linkedRow = await waitFor(() => {
            const row = document.querySelector('[data-execution-id="exec-2"]');
            expect(row).toBeInTheDocument();
            return row as HTMLElement;
        });

        expect(within(linkedRow).getByTestId('execution-row__trigger')).toHaveAttribute('aria-expanded', 'true');
        expect(within(linkedRow).getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');
        expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' });
        expect(document.querySelectorAll('[data-execution-id]')).toHaveLength(1);
        expect(screen.getByRole('button', { name: 'Back to jobs' })).toBeInTheDocument();
    });

    it('fetches and expands a linked child execution omitted by the roots-only list', async () => {
        const scrollIntoView = vi.spyOn(Element.prototype, 'scrollIntoView');
        const linkedExecutionId = '11111111-1111-4111-8111-111111111111';
        const linkedExecution: ApiJobExecution = {
            id: linkedExecutionId,
            parentExecutionId: '22222222-2222-4222-8222-222222222222',
            jobType: 'DownloadRefresh',
            scope: 'Content',
            status: 'Completed',
            progress: 100,
            createdAt: '2024-01-15T10:31:00Z',
            startedAt: '2024-01-15T10:31:00Z',
            completedAt: '2024-01-15T10:31:05Z',
        };
        mockApiGet.mockImplementation((url: string) => {
            if (url === `/jobs/${linkedExecutionId}`) {
                return Promise.resolve(linkedExecution);
            }
            return defaultApiGetResponse(url);
        });

        render(
            <TestWrapper initialRoute={`/?job=${linkedExecutionId}`}>
                <JobsPage />
            </TestWrapper>,
        );

        const linkedRow = await waitFor(() => {
            const row = document.querySelector(`[data-execution-id="${linkedExecutionId}"]`);
            expect(row).toBeInTheDocument();
            return row as HTMLElement;
        });

        expect(mockApiGet).toHaveBeenCalledWith(`/jobs/${linkedExecutionId}`);
        expect(within(linkedRow).getByTestId('execution-row__trigger')).toHaveAttribute('aria-expanded', 'true');
        expect(within(linkedRow).getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');
        expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' });
    });

    it('clicking expanded execution row collapses it', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const [firstTrigger] = await screen.findAllByTestId('execution-row__trigger');
        await user.click(firstTrigger);

        const [logs] = await screen.findAllByTestId('execution-row__logs');
        expect(logs).toHaveAttribute('data-state', 'open');

        await user.click(firstTrigger);

        expect(logs).toHaveAttribute('data-state', 'closed');
    });

    it('expanded execution row survives a polling refresh that updates the same execution', async () => {
        const user = userEvent.setup();

        let callCount = 0;
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve({});
            if (url === '/jobs' || url.startsWith('/jobs?')) {
                callCount++;
                const items: ApiJobExecution[] = [
                    {
                        id: 'exec-poll',
                        jobType: 'RSS Sync',
                        scope: 'Content' as JobScopeValue,
                        status: (callCount === 1 ? 'Running' : 'Completed') as JobExecutionStatusValue,
                        progress: callCount === 1 ? 30 : 100,
                        createdAt: '2024-01-15T10:00:00Z',
                        startedAt: '2024-01-15T10:00:01Z',
                        completedAt: callCount === 1 ? null : '2024-01-15T10:00:30Z',
                        statusMessage: callCount === 1 ? 'Checking feed' : null,
                    },
                ];
                return Promise.resolve({ items, pageNumber: 1, pageSize: 20, totalItems: 1 });
            }
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        const queryClient = createTestQueryClient();

        render(
            <TestWrapper queryClient={queryClient}>
                <JobsPage />
            </TestWrapper>,
        );

        const [trigger] = await screen.findAllByTestId('execution-row__trigger');
        await user.click(trigger);
        const [logs] = await screen.findAllByTestId('execution-row__logs');
        expect(logs).toHaveAttribute('data-state', 'open');

        await queryClient.invalidateQueries({ queryKey: ['jobs', 'list'] });

        await waitFor(() => {
            const root = document.querySelector('[data-execution-id="exec-poll"]');
            expect(root).toHaveAttribute('data-status', 'completed');
        });

        const [logsAfter] = await screen.findAllByTestId('execution-row__logs');
        expect(logsAfter).toHaveAttribute('data-state', 'open');
        expect(logsAfter).toBe(logs);
    });

    it('renders exactly one row per execution.id even after a poll refresh', async () => {
        let callCount = 0;
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve({});
            if (url === '/jobs' || url.startsWith('/jobs?')) {
                callCount++;
                const items: ApiJobExecution[] = [
                    {
                        id: 'exec-stream',
                        jobType: 'RSS Sync',
                        scope: 'Content' as JobScopeValue,
                        status: 'Running' as JobExecutionStatusValue,
                        progress: callCount * 25,
                        createdAt: '2024-01-15T10:00:00Z',
                        startedAt: '2024-01-15T10:00:01Z',
                        completedAt: null,
                        statusMessage: `Checking feed ${callCount}`,
                    },
                ];
                return Promise.resolve({ items, pageNumber: 1, pageSize: 20, totalItems: 1 });
            }
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        const queryClient = createTestQueryClient();

        render(
            <TestWrapper queryClient={queryClient}>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelectorAll('[data-execution-id="exec-stream"]')).toHaveLength(1);
        });

        await queryClient.invalidateQueries({ queryKey: ['jobs', 'list'] });

        await waitFor(() => {
            expect(screen.getByText('Checking feed 2')).toBeInTheDocument();
        });
        expect(document.querySelectorAll('[data-execution-id="exec-stream"]')).toHaveLength(1);
    });
});

describe('JobsPage Schedules tab', () => {
    it('renders the schedule triggers list when the tab is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(screen.getByRole('list', { name: /schedule triggers/i })).toBeInTheDocument();
    });

    it('renders schedule trigger names', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(screen.getByText('Hourly RSS Sync')).toBeInTheDocument();
            expect(screen.getByText('Daily Metadata Refresh')).toBeInTheDocument();
            expect(screen.getByText('Weekly Database Backup')).toBeInTheDocument();
        });
    });

    it('renders schedule cadence labels with the UTC qualifier', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(screen.getByText('Every hour (UTC)')).toBeInTheDocument();
            expect(screen.getByText('Daily at 6 AM (UTC)')).toBeInTheDocument();
            expect(screen.getByText('Weekly on Sunday (UTC)')).toBeInTheDocument();
        });
    });

    it('applies data-trigger-id to each schedule item', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-trigger-id]').length).toBe(5);
        });
    });

    it('does not render a caution note on the schedules tab', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(screen.queryByTestId('jobs-page__schedule-caution')).not.toBeInTheDocument();
    });
});

describe('JobsPage Events tab', () => {
    it('renders the event triggers list when the tab is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        expect(screen.getByRole('list', { name: /event triggers/i })).toBeInTheDocument();
    });

    it('renders event trigger names', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        await waitFor(() => {
            expect(screen.getByText('Refresh on Download Complete')).toBeInTheDocument();
            expect(screen.getByText('Search on Content Added')).toBeInTheDocument();
            expect(screen.getByText('Sync Plex on Import')).toBeInTheDocument();
        });
    });

    it('applies data-event-trigger-id to each event item', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-event-trigger-id]').length).toBe(3);
        });
    });

    it('counts executable context criteria inside the items wrapper', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        const row = await waitFor(() => {
            const element = document.querySelector('[data-event-trigger-id="event-1"]');
            expect(element).toBeInTheDocument();
            return element as HTMLElement;
        });

        expect(within(row).getByText('1 filter')).toBeInTheDocument();
    });

    it('renders multi-event filter values instead of all events', async () => {
        const user = userEvent.setup();
        const multiEventTrigger: ApiEventTrigger = {
            id: 'event-multi',
            name: 'Cleanup After Download Ends',
            jobType: 'Cleanup Downloads',
            scope: 'Administrative' as JobScopeValue,
            eventFilter: {
                eventName: { operator: 'In', values: ['download.imported', 'download.failed'] },
            },
            enabled: true,
            delaySeconds: 0,
            createdAt: '2024-01-01T00:00:00Z',
        };
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) {
                return Promise.resolve([...mockEventTriggers, multiEventTrigger]);
            }
            return defaultApiGetResponse(url);
        });

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        const row = await waitFor(() => {
            const element = document.querySelector('[data-event-trigger-id="event-multi"]');
            expect(element).toBeInTheDocument();
            return element as HTMLElement;
        });

        expect(within(row).getByText('download.imported, download.failed')).toBeInTheDocument();
        expect(within(row).queryByText('All events')).not.toBeInTheDocument();
    });

    it('does not render a caution note on the events tab', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        expect(screen.queryByTestId('jobs-page__event-caution')).not.toBeInTheDocument();
    });
});

describe('JobsPage roots-only toggle', () => {
    it('defaults the toggle on and requests rootsOnly=true', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const toggle = await screen.findByRole('switch', { name: /show top level only/i });
        expect(toggle).toHaveAttribute('data-state', 'checked');

        await waitFor(() => {
            const jobsCalls = mockApiGet.mock.calls.map((c) => c[0] as string).filter((u) => u.startsWith('/jobs?'));
            expect(jobsCalls.some((u) => u.includes('rootsOnly=true'))).toBe(true);
        });
    });

    it('passes rootsOnly=false to the server when the toggle is turned off', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const toggle = await screen.findByRole('switch', { name: /show top level only/i });
        await user.click(toggle);

        await waitFor(() => {
            const jobsCalls = mockApiGet.mock.calls.map((c) => c[0] as string).filter((u) => u.startsWith('/jobs?'));
            expect(jobsCalls.some((u) => u.includes('rootsOnly=false'))).toBe(true);
        });
    });

    it('restores the toggle off state from the ?rootsOnly=0 search param', async () => {
        render(
            <TestWrapper initialRoute="/?rootsOnly=0">
                <JobsPage />
            </TestWrapper>,
        );

        const toggle = await screen.findByRole('switch', { name: /show top level only/i });
        expect(toggle).toHaveAttribute('data-state', 'unchecked');
    });
});

describe('JobsPage chain filter', () => {
    it('applies the correlationId filter from the URL to the requested and rendered list', async () => {
        render(
            <TestWrapper initialRoute="/?correlationId=corr-alpha">
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(2);
        });

        const jobsCalls = mockApiGet.mock.calls.map((c) => c[0] as string).filter((u) => u.startsWith('/jobs?'));
        expect(jobsCalls.some((u) => u.includes('correlationId=corr-alpha'))).toBe(true);
    });

    it('shows the active chain filter labeled as a chain, not as parent and child', async () => {
        render(
            <TestWrapper initialRoute="/?correlationId=corr-alpha">
                <JobsPage />
            </TestWrapper>,
        );

        const chainFilter = await screen.findByText(/corr-alpha/);
        expect(chainFilter).toHaveTextContent('Chain: corr-alpha');
        expect(screen.getByRole('button', { name: /clear chain filter/i })).toBeInTheDocument();
    });

    it('does not show the chain filter when no correlationId is in the URL', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-control="chain-filter"]')).not.toBeInTheDocument();
    });

    it('clears the chain filter and restores the full list', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper initialRoute="/?correlationId=corr-alpha">
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(2);
        });

        await user.click(screen.getByRole('button', { name: /clear chain filter/i }));

        expect(document.querySelector('[data-control="chain-filter"]')).not.toBeInTheDocument();
        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });
    });
});

describe('JobsPage Schedules tab - Add and Actions', () => {
    it('shows Add Schedule button for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(
            within(document.querySelector('.jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add schedule/i,
            }),
        ).toBeInTheDocument();
    });

    it('shows Add Event Trigger button for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        expect(
            within(document.querySelector('.jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add event trigger/i,
            }),
        ).toBeInTheDocument();
    });

    it('creates an event trigger with wrapped context criteria', async () => {
        const user = userEvent.setup();
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') {
                return Promise.resolve([{ ...mockJobSpecs[0], enabled: true }]);
            }
            return defaultApiGetResponse(url);
        });

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        await user.click(
            within(document.querySelector('.jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add event trigger/i,
            }),
        );
        await user.type(screen.getByRole('textbox', { name: /^name/i }), 'Kind-X metadata refresh');
        await user.click(screen.getByRole('combobox', { name: /job template/i }));
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));
        await user.type(screen.getByRole('textbox', { name: 'Pattern (Regex)' }), 'content\\.metadata\\.refresh');
        await user.click(screen.getByRole('button', { name: 'Add Filter' }));
        await user.type(screen.getByRole('textbox', { name: 'Filter 1 key' }), 'content.type');
        await user.type(screen.getByRole('textbox', { name: 'Filter 1 pattern' }), '^kind-x$');
        await user.click(screen.getByRole('switch', { name: 'Filter 1 negate' }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/event-triggers', {
                name: 'Kind-X metadata refresh',
                jobType: 'ContentMetadataRefreshItem',
                eventFilter: {
                    eventName: { operator: 'regex', value: 'content\\.metadata\\.refresh' },
                    eventContext: {
                        items: {
                            'content.type': { operator: 'regex', value: '^kind-x$', negate: true },
                        },
                    },
                },
                scope: 'Administrative',
                enabled: true,
                delaySeconds: 0,
            });
        });
    });

    it('hides Add buttons for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        expect(screen.queryByRole('button', { name: /add schedule/i })).not.toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        expect(screen.queryByRole('button', { name: /add event trigger/i })).not.toBeInTheDocument();
    });

    it('opens schedule form sheet when Add Schedule is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(
            within(document.querySelector('.jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add schedule/i,
            }),
        );

        expect(screen.getByText('Add Schedule Trigger')).toBeInTheDocument();

        await user.keyboard('{Escape}');
    });

    it('uses non-on-demand catalog specs for job choices on a clean install', async () => {
        const user = userEvent.setup();

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve({});
            if (url === '/jobs' || url.startsWith('/jobs?')) {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 20, totalItems: 0 });
            }
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(
            within(document.querySelector('.jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add schedule/i,
            }),
        );

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);

        const refreshOption = screen.getByRole('option', { name: 'Content metadata refresh item (unavailable)' });
        const releaseSearchOption = screen.getByRole('option', { name: 'Release search item (unavailable)' });

        expect(refreshOption).toBeInTheDocument();
        expect(refreshOption).toHaveAttribute('aria-disabled', 'true');
        expect(releaseSearchOption).toBeInTheDocument();
        expect(releaseSearchOption).toHaveAttribute('aria-disabled', 'true');
        expect(screen.queryByRole('option', { name: /backup database/i })).not.toBeInTheDocument();

        await user.keyboard('{Escape}');
    });

    it('keeps historical trigger job types editable when the catalog spec is unavailable', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        const editButton = await waitFor(() => {
            const btn = document.querySelector('[data-action="edit"][data-target="trigger-2"]');
            expect(btn).toBeInTheDocument();
            return btn as HTMLElement;
        });
        await user.click(editButton);

        expect(screen.getByRole('combobox', { name: /job template/i })).toHaveTextContent(
            'Content metadata refresh item',
        );

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/schedule-triggers/trigger-2', {
                name: { value: 'Daily Metadata Refresh' },
                schedule: { value: '0 6 * * *' },
                enabled: { value: true },
            });
        });

        await user.keyboard('{Escape}');
    });

    it('shows toggle switch for each schedule trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="toggle"]').length).toBe(5);
        });
    });

    it('shows edit and delete buttons for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="edit"]').length).toBe(5);
            expect(document.querySelectorAll('[data-action="delete"]').length).toBe(5);
        });
    });

    it('hides edit and delete buttons for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(document.querySelectorAll('[data-action="edit"]').length).toBe(0);
        expect(document.querySelectorAll('[data-action="delete"]').length).toBe(0);
    });

    it('shows run now button for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="run-now"]').length).toBe(4);
        });
    });

    it('hides run now button when catalog spec is not OnDemand', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            expect(screen.getByText('Daily Metadata Refresh')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-trigger-id="trigger-2"] [data-action="run-now"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-trigger-id="trigger-1"] [data-action="run-now"]')).toBeInTheDocument();
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        const editButton = await waitFor(() => {
            const btn = document.querySelector('[data-action="edit"]');
            expect(btn).toBeInTheDocument();
            return btn as HTMLElement;
        });
        await user.click(editButton);

        expect(screen.getByText('Edit Schedule Trigger')).toBeInTheDocument();
    });

    it('opens delete confirmation dialog when delete button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        const deleteButton = await waitFor(() => {
            const btn = document.querySelector('[data-action="delete"]');
            expect(btn).toBeInTheDocument();
            return btn as HTMLElement;
        });
        await user.click(deleteButton);

        expect(screen.getByText('Delete Schedule Trigger')).toBeInTheDocument();
        expect(screen.getByText(/are you sure/i)).toBeInTheDocument();
    });

    it('calls runJob when run now button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        const runNowButton = await waitFor(() => {
            const btn = document.querySelector('[data-action="run-now"]');
            expect(btn).toBeInTheDocument();
            return btn as HTMLElement;
        });
        await user.click(runNowButton);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/jobs/enqueue', {
                jobType: 'RSS Sync',
                extensionId: undefined,
                parameters: undefined,
            });
        });
    });
});

describe('JobsPage Events tab - Event Trigger Actions', () => {
    it('shows toggle switch for event triggers', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="toggle-event"]').length).toBe(3);
        });
    });

    it('shows edit and delete buttons for event triggers for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="edit-event"]').length).toBe(3);
            expect(document.querySelectorAll('[data-action="delete-event"]').length).toBe(3);
        });
    });

    it('locks event trigger template edits and omits jobType from patch payload', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        const editButton = await waitFor(() => {
            const btn = document.querySelector('[data-action="edit-event"][data-target="event-1"]');
            expect(btn).toBeInTheDocument();
            return btn as HTMLElement;
        });
        await user.click(editButton);

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        expect(templateTrigger).toBeDisabled();
        expect(templateTrigger).toHaveTextContent('Content metadata refresh item');
        expect(
            screen.getByText('Job template cannot be changed after the event trigger is created.'),
        ).toBeInTheDocument();
        expect(screen.getByRole('textbox', { name: 'Filter 1 key' })).toHaveValue('content.type');
        expect(screen.getByRole('textbox', { name: 'Filter 1 pattern' })).toHaveValue('^kind-x$');
        expect(screen.getByRole('switch', { name: 'Filter 1 negate' })).toBeChecked();

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/event-triggers/event-1', expect.anything());
        });

        const patchPayload = mockApiPatch.mock.calls.find(([url]) => url === '/event-triggers/event-1')?.[1];
        expect(patchPayload).toMatchObject({
            name: { value: 'Refresh on Download Complete' },
            eventFilter: {
                value: {
                    eventName: { operator: 'regex', value: 'download\\.completed' },
                    eventContext: {
                        items: {
                            'content.type': { operator: 'Regex', value: '^kind-x$', negate: true },
                        },
                    },
                },
            },
            enabled: { value: true },
        });
        expect(patchPayload).not.toHaveProperty('jobType');
    });

    it('hides edit and delete buttons for event triggers for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        expect(document.querySelectorAll('[data-action="edit-event"]').length).toBe(0);
        expect(document.querySelectorAll('[data-action="delete-event"]').length).toBe(0);
    });
});

describe('JobsPage accessibility', () => {
    it('executions list has accessible label', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(await screen.findByRole('list', { name: /job history/i })).toBeInTheDocument();
    });

    it('roots-only toggle has an accessible label', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(await screen.findByRole('switch', { name: /show top level only/i })).toBeInTheDocument();
    });

    it('progress bars have aria attributes', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const progressBars = document.querySelectorAll('[role="progressbar"]');
            expect(progressBars.length).toBeGreaterThan(0);
        });

        const progressBars = document.querySelectorAll('[role="progressbar"]');
        progressBars.forEach((bar) => {
            expect(bar).toHaveAttribute('aria-valuenow');
            expect(bar).toHaveAttribute('aria-valuemin', '0');
            expect(bar).toHaveAttribute('aria-valuemax', '100');
            expect(bar).toHaveAttribute('aria-label');
        });
    });

    it('toggle switches have aria-labels', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            const switches = document.querySelectorAll('[data-action="toggle"]');
            expect(switches.length).toBe(5);
        });

        const switches = document.querySelectorAll('[data-action="toggle"]');
        switches.forEach((sw) => {
            expect(sw).toHaveAttribute('aria-label');
        });
    });

    it('action buttons have aria-labels', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            expect(editButtons.length).toBe(5);
        });

        const editButtons = document.querySelectorAll('[data-action="edit"]');
        const deleteButtons = document.querySelectorAll('[data-action="delete"]');

        editButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
        deleteButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });
});

describe('JobsPage Executions tab - loading state', () => {
    it('shows a loading indicator during the initial load', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByTestId('jobs-page__loading')).toBeInTheDocument();
        expect(screen.queryByText('No job history found')).not.toBeInTheDocument();
    });

    it('replaces the loading indicator with the execution list once loaded', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            expect(within(list).getAllByRole('listitem').length).toBe(8);
        });

        expect(screen.queryByTestId('jobs-page__loading')).not.toBeInTheDocument();
    });
});

describe('JobsPage empty states', () => {
    it('shows empty state when no jobs', async () => {
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve({});
            if (url === '/jobs' || url.startsWith('/jobs?')) {
                return Promise.resolve({ items: [], pageNumber: 1, pageSize: 20, totalItems: 0 });
            }
            if (url === '/schedule-triggers') return Promise.resolve(mockScheduleTriggers);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?'))
                return Promise.resolve(mockEventTriggers);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(await screen.findByText('No job history found')).toBeInTheDocument();
    });

    it('shows empty states when no triggers', async () => {
        const user = userEvent.setup();

        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve(mockJobSpecs);
            if (url === '/jobs/status-counts') return Promise.resolve({});
            if (url === '/jobs' || url.startsWith('/jobs?')) {
                return Promise.resolve({ items: mockJobs, pageNumber: 1, pageSize: 20, totalItems: mockJobs.length });
            }
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        expect(screen.getByText('No schedule triggers')).toBeInTheDocument();

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        expect(screen.getByText('No event triggers')).toBeInTheDocument();
    });
});

describe('JobsPage URL persistence', () => {
    function UrlObserver() {
        const [params] = useSearchParams();
        return <span data-testid="url-params">{params.toString()}</span>;
    }

    it('restores active tab from ?tab=schedules search param', () => {
        render(
            <TestWrapper initialRoute="/?tab=schedules">
                <JobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /scheduled jobs/i });
        expect(schedulesTab).toHaveAttribute('data-state', 'active');
    });

    it('restores active tab from ?tab=events search param', () => {
        render(
            <TestWrapper initialRoute="/?tab=events">
                <JobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /event triggers/i });
        expect(eventsTab).toHaveAttribute('data-state', 'active');
    });

    it('defaults to executions tab when no ?tab= param', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const executionsTab = screen.getByRole('tab', { name: /history/i });
        expect(executionsTab).toHaveAttribute('data-state', 'active');
    });

    it('falls back to Executions and normalizes the URL when ?tab= is invalid', async () => {
        render(
            <TestWrapper initialRoute="/?tab=bogus&status=failed">
                <JobsPage />
                <UrlObserver />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /history/i })).toHaveAttribute('data-state', 'active');
        await waitFor(() => {
            expect(screen.getByTestId('url-params').textContent).toBe('status=failed');
        });
        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('restores template filter from ?template= search param', async () => {
        render(
            <TestWrapper initialRoute="/?template=RSS%20Sync">
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(2);
        });
    });

    it('restores status filter from ?status= search param', async () => {
        render(
            <TestWrapper initialRoute="/?status=failed">
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });

    it('restores both template and status filters from search params', async () => {
        render(
            <TestWrapper initialRoute="/?template=RSS%20Sync&status=running">
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job history/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });
});

it.each([
    ['Scheduled Jobs', '/schedule-triggers', 'No schedule triggers'],
    ['Event Triggers', '/event-triggers', 'No event triggers'],
])('Triggers_ShowLoadingOutsideList_WhenLoading %s', async (tab, endpoint, emptyTitle) => {
    // Arrange
    let resolveResponse!: (value: never[]) => void;
    const response = new Promise<never[]>((resolve) => {
        resolveResponse = resolve;
    });
    mockApiGet.mockImplementation((url) => (url.startsWith(endpoint) ? response : defaultApiGetResponse(url)));
    const user = userEvent.setup();
    render(<JobsPage />, { wrapper: TestWrapper });

    // Act
    await user.click(screen.getByRole('tab', { name: tab }));

    // Assert
    expect(screen.getByTestId('jobs-page__loading').closest('[role="list"]')).toBeNull();
    expect(screen.queryByText(emptyTitle)).not.toBeInTheDocument();

    // Act
    resolveResponse([]);

    // Assert
    expect((await screen.findByText(emptyTitle)).closest('[role="list"]')).toBeNull();
    expect(screen.queryByTestId('jobs-page__loading')).not.toBeInTheDocument();
});

describe('JobsPage phone actions button', () => {
    it.each([
        [/scheduled jobs/i, 'Add Schedule'],
        [/event triggers/i, 'Add Event Trigger'],
    ])('offers the %s tab action as the floating button', async (tab, label) => {
        const user = userEvent.setup();
        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: tab }));

        expect(document.querySelector('.quick-actions-button__fab')).toHaveAttribute('aria-label', label);
    });

    it('offers Run a job as the floating button on History when a job can run', async () => {
        const onDemandSpec: ApiJobSpec = {
            jobType: 'UserManual',
            displayName: 'User Manual',
            category: 'Content',
            scope: 'User' as JobScopeValue,
            enabled: true,
            onDemand: true,
            supportedTriggerModes: ['Manual'],
            parameters: [],
        };
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/jobs/specs') return Promise.resolve([onDemandSpec]);
            if (url === '/jobs/status-counts') return Promise.resolve(countByStatus(mockJobs));
            if (url === '/jobs' || url.startsWith('/jobs?')) return Promise.resolve(pageJobs(url, mockJobs));
            if (url === '/schedule-triggers') return Promise.resolve([]);
            if (url === '/event-triggers' || url.startsWith('/event-triggers?')) return Promise.resolve([]);
            return Promise.reject(new Error(`Unmocked URL: ${url}`));
        });

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() =>
            expect(document.querySelector('.quick-actions-button__fab')).toHaveAttribute('aria-label', 'Run a job'),
        );
    });

    it('shows no floating button on History when no job can run', async () => {
        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        await screen.findByRole('list', { name: /job history/i });
        expect(document.querySelector('.quick-actions-button')).toBeNull();
    });
});

describe('JobsPage history request failure', () => {
    it('shows an error with retry instead of the empty state when the job history request fails', async () => {
        // Arrange
        mockApiGet.mockImplementation((url: string) =>
            url.startsWith('/jobs?') ? Promise.reject(new Error('Server error')) : defaultApiGetResponse(url),
        );

        // Act
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        // Assert
        expect(await screen.findByText('Failed to load job history')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        expect(screen.queryByText('No job history found')).not.toBeInTheDocument();
    });

    it('loads the job history when Retry succeeds', async () => {
        // Arrange
        const user = userEvent.setup();
        mockApiGet.mockImplementation((url: string) =>
            url.startsWith('/jobs?') ? Promise.reject(new Error('Server error')) : defaultApiGetResponse(url),
        );
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );
        const retry = await screen.findByRole('button', { name: 'Retry' });
        mockApiGet.mockImplementation(defaultApiGetResponse);

        // Act
        await user.click(retry);

        // Assert
        const list = await screen.findByRole('list', { name: /job history/i });
        expect(within(list).getAllByRole('listitem')).toHaveLength(8);
    });
});

describe('JobsPage trigger request failure', () => {
    it.each([
        ['Scheduled Jobs', '/schedule-triggers', 'Failed to load schedule triggers', 'No schedule triggers'],
        ['Event Triggers', '/event-triggers', 'Failed to load event triggers', 'No event triggers'],
    ])(
        'shows an error with retry instead of the empty state when the %s request fails',
        async (tab, endpoint, failedTitle, emptyTitle) => {
            // Arrange
            const user = userEvent.setup();
            mockApiGet.mockImplementation((url: string) =>
                url.startsWith(endpoint) ? Promise.reject(new Error('Server error')) : defaultApiGetResponse(url),
            );
            render(
                <TestWrapper>
                    <JobsPage />
                </TestWrapper>,
            );

            // Act
            await user.click(screen.getByRole('tab', { name: tab }));

            // Assert
            expect(await screen.findByText(failedTitle)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
            expect(screen.queryByText(emptyTitle)).not.toBeInTheDocument();
        },
    );

    it.each([
        ['Scheduled Jobs', '/schedule-triggers', 'Hourly RSS Sync'],
        ['Event Triggers', '/event-triggers', 'Refresh on Download Complete'],
    ])('loads the %s when Retry succeeds', async (tab, endpoint, triggerName) => {
        // Arrange
        const user = userEvent.setup();
        mockApiGet.mockImplementation((url: string) =>
            url.startsWith(endpoint) ? Promise.reject(new Error('Server error')) : defaultApiGetResponse(url),
        );
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );
        await user.click(screen.getByRole('tab', { name: tab }));
        const retry = await screen.findByRole('button', { name: 'Retry' });
        mockApiGet.mockImplementation(defaultApiGetResponse);

        // Act
        await user.click(retry);

        // Assert
        expect(await screen.findByText(triggerName)).toBeInTheDocument();
    });
});
