import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { testEffectivePermissions } from '@/test/permissions';

import { MyJobsPage } from './MyJobsPage';
import type { ApiJobExecution, ApiJobSpec, JobExecutionStatusValue, JobScopeValue } from '@/hooks/useJobs';
import type { ApiScheduleTrigger } from '@/hooks/useScheduleTriggers';
import type { ApiEventTrigger } from '@/hooks/useEventTriggers';

vi.mock('@/hooks/useJobs', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useJobs')>();
    return {
        ...actual,
        useJobExecution: vi.fn(),
        useJobs: vi.fn(),
        useJobSpecs: vi.fn(),
    };
});

vi.mock('@/hooks/useScheduleTriggers', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useScheduleTriggers')>();
    return {
        ...actual,
        useScheduleTriggers: vi.fn(),
    };
});

vi.mock('@/hooks/useEventTriggers', () => ({
    useEventTriggers: vi.fn(),
}));

const mockUserJobs: ApiJobExecution[] = [
    {
        id: 'exec-1',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'User' as JobScopeValue,
        status: 'Running' as JobExecutionStatusValue,
        progress: 45,
        createdAt: '2024-01-15T10:30:00Z',
        startedAt: '2024-01-15T10:30:00Z',
        completedAt: null,
        statusMessage: 'Processing items...',
    },
    {
        id: 'exec-2',
        jobType: 'SearchMissing',
        scope: 'User' as JobScopeValue,
        status: 'Completed' as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T09:00:00Z',
        startedAt: '2024-01-15T09:00:00Z',
        completedAt: '2024-01-15T09:15:00Z',
        statusMessage: 'Found 5 items',
    },
    {
        id: 'exec-3',
        jobType: 'WishlistSearch',
        scope: 'System' as JobScopeValue,
        status: 'Completed' as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T08:00:00Z',
        startedAt: '2024-01-15T08:00:00Z',
        completedAt: '2024-01-15T08:05:00Z',
        createdByUserId: '1',
        statusMessage: 'Manual run',
    },
    {
        id: 'exec-4',
        parentExecutionId: 'exec-1',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'User' as JobScopeValue,
        status: 'Completed' as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T10:31:00Z',
        startedAt: '2024-01-15T10:31:00Z',
        completedAt: '2024-01-15T10:32:00Z',
        statusMessage: 'Child execution',
    },
];

const mockScheduleTriggers: ApiScheduleTrigger[] = [
    {
        id: 'trigger-1',
        name: 'Hourly RSS Sync',
        jobType: 'RSSSync',
        scope: 'User' as JobScopeValue,
        schedule: '0 * * * *',
        executionMode: 'Queue',
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: '1',
        nextRunAt: '2024-01-15T12:00:00Z',
    },
];

const mockEventTriggers: ApiEventTrigger[] = [
    {
        id: 'event-1',
        name: 'Refresh on Download',
        jobType: 'ContentMetadataRefreshItem',
        scope: 'User' as JobScopeValue,
        eventFilter: {
            eventName: { value: 'download\\.completed' },
        },
        enabled: true,
        delaySeconds: 0,
        createdAt: '2024-01-01T00:00:00Z',
        createdByUserId: '1',
    },
];

const mockScheduleSpecs: ApiJobSpec[] = [
    {
        jobType: 'RSSSync',
        displayName: 'RSS Sync',
        category: 'Content',
        scope: 'User' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        requesterTriggerModes: ['Schedule'],
        defaultSchedule: '0 * * * *',
        parameters: [],
    },
    {
        jobType: 'ContentMetadataRefreshItem',
        displayName: 'Content metadata refresh item',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        requesterTriggerModes: ['Schedule'],
        parameters: [],
    },
    {
        jobType: 'ReleaseSearchScan',
        displayName: 'Release search scan',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        parameters: [],
    },
    {
        jobType: 'IndexerFeedSyncScan',
        displayName: 'Indexer feed sync scan',
        category: 'System',
        scope: 'Administrative' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        parameters: [],
    },
];

const mockEventSpecs: ApiJobSpec[] = [
    {
        jobType: 'ContentMetadataRefreshItem',
        displayName: 'Content metadata refresh item',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Event'],
        requesterTriggerModes: ['Event'],
        parameters: [],
    },
    {
        jobType: 'ReleaseAutoGrabScan',
        displayName: 'Release auto-grab scan',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Event'],
        parameters: [],
    },
];

const mockManualSpecs: ApiJobSpec[] = [
    {
        jobType: 'ContentMetadataRefreshItem',
        displayName: 'Content metadata refresh item',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        requesterTriggerModes: ['Manual'],
        parameters: [],
    },
    {
        jobType: 'ReleaseSearchScan',
        displayName: 'Release search scan',
        category: 'Content',
        scope: 'Content' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        parameters: [],
    },
    {
        jobType: 'RSSSync',
        displayName: 'RSS Sync',
        category: 'Content',
        scope: 'User' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        requesterTriggerModes: ['Schedule'],
        defaultSchedule: '0 * * * *',
        parameters: [],
    },
    {
        jobType: 'IndexerFeedSyncScan',
        displayName: 'Indexer feed sync scan',
        category: 'System',
        scope: 'Administrative' as JobScopeValue,
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual', 'Schedule'],
        requesterTriggerModes: ['Manual'],
        parameters: [],
    },
];

import { useJobExecution, useJobs, useJobSpecs } from '@/hooks/useJobs';
import { useScheduleTriggers } from '@/hooks/useScheduleTriggers';
import { useEventTriggers } from '@/hooks/useEventTriggers';

const mockUseJobExecution = useJobExecution as ReturnType<typeof vi.fn>;
const mockUseJobs = useJobs as ReturnType<typeof vi.fn>;
const mockUseJobSpecs = useJobSpecs as ReturnType<typeof vi.fn>;
const mockUseScheduleTriggers = useScheduleTriggers as ReturnType<typeof vi.fn>;
const mockUseEventTriggers = useEventTriggers as ReturnType<typeof vi.fn>;

const createScheduleTriggerMock = vi.fn().mockResolvedValue({});
const updateScheduleTriggerMock = vi.fn().mockResolvedValue(undefined);
const deleteScheduleTriggerMock = vi.fn().mockResolvedValue(undefined);
const createEventTriggerMock = vi.fn().mockResolvedValue({});
const updateEventTriggerMock = vi.fn().mockResolvedValue(undefined);
const deleteEventTriggerMock = vi.fn().mockResolvedValue(undefined);

function TestWrapper({
    children,
    role = Role.Viewer,
    initialRoute = '/',
}: {
    children: ReactNode;
    role?: Role;
    initialRoute?: string;
}) {
    const mockUser = { id: '1', username: 'test', role };
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
        },
    });

    return (
        <QueryClientProvider client={queryClient}>
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

function scheduleTriggersReturn(triggers: ApiScheduleTrigger[]) {
    return {
        triggers,
        isLoading: false,
        error: null,
        createTrigger: createScheduleTriggerMock,
        isCreating: false,
        updateTrigger: updateScheduleTriggerMock,
        isUpdating: false,
        deleteTrigger: deleteScheduleTriggerMock,
        isDeleting: false,
        refetch: vi.fn(),
    };
}

function eventTriggersReturn(triggers: ApiEventTrigger[]) {
    return {
        triggers,
        isLoading: false,
        error: null,
        createTrigger: createEventTriggerMock,
        isCreating: false,
        updateTrigger: updateEventTriggerMock,
        isUpdating: false,
        deleteTrigger: deleteEventTriggerMock,
        isDeleting: false,
        refetch: vi.fn(),
    };
}

const EXECUTIONS_PAGE_SIZE = 50;

function pageUserJobs(params: { jobType?: string; rootsOnly?: boolean; pageNumber?: number; pageSize?: number } = {}) {
    let filtered = [...mockUserJobs];
    if (params.rootsOnly) {
        filtered = filtered.filter((job) => !job.parentExecutionId);
    }
    if (params.jobType) {
        filtered = filtered.filter((job) => job.jobType === params.jobType);
    }
    const pageNumber = params.pageNumber ?? 1;
    const pageSize = params.pageSize ?? EXECUTIONS_PAGE_SIZE;
    const start = (pageNumber - 1) * pageSize;
    return {
        jobs: filtered.slice(start, start + pageSize),
        pagination: { pageNumber, pageSize, totalItems: filtered.length },
        runJob: vi.fn().mockResolvedValue('job-id'),
        isLoading: false,
        error: null,
        refetch: vi.fn(),
    };
}

beforeEach(() => {
    vi.clearAllMocks();

    mockUseJobExecution.mockReturnValue({
        job: null,
        isLoading: false,
        error: null,
        refetch: vi.fn(),
    });
    mockUseJobs.mockImplementation(
        (params?: { jobType?: string; rootsOnly?: boolean; pageNumber?: number; pageSize?: number }) =>
            pageUserJobs(params),
    );

    mockUseJobSpecs.mockImplementation((mode?: string) => ({
        specs:
            mode === 'Event'
                ? mockEventSpecs
                : mode === 'Manual'
                  ? mockManualSpecs
                  : mode === 'Schedule'
                    ? mockScheduleSpecs
                    : mockManualSpecs,
        isLoading: false,
        error: null,
        refetch: vi.fn(),
    }));

    mockUseScheduleTriggers.mockReturnValue(scheduleTriggersReturn(mockScheduleTriggers));
    mockUseEventTriggers.mockReturnValue(eventTriggersReturn(mockEventTriggers));
});

describe('MyJobsPage', () => {
    it('renders with correct data-page attribute', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="my-jobs"]')).toBeInTheDocument();
    });

    it('renders the page title', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('heading', { name: 'My Jobs', level: 1 })).toBeInTheDocument();
    });

    it('renders the simplified page description', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText(/your jobs and job triggers/i)).toBeInTheDocument();
    });

    it('does not render a header count pill', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.queryByTestId('my-jobs-page__count')).not.toBeInTheDocument();
    });

    it('does not surface a caution note on the manual-run surface', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.queryByTestId('my-jobs-page__run-caution')).not.toBeInTheDocument();
    });
});

describe('MyJobsPage Executions tab', () => {
    it('fetches executions scoped to the current user (mine=true)', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(mockUseJobs).toHaveBeenCalledWith(
            expect.objectContaining({ mine: true, rootsOnly: true, pageNumber: 1, pageSize: EXECUTIONS_PAGE_SIZE }),
        );
    });

    it('renders every run the user triggered, including manual System-scoped runs', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /job history/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(3);
        expect(screen.getByText('Manual run')).toBeInTheDocument();
    });

    it('shows top level executions only by default', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('switch', { name: /show top level only/i })).toHaveAttribute('data-state', 'checked');
        expect(screen.queryByText('Child execution')).not.toBeInTheDocument();
    });

    it('requests and renders child executions when the roots-only toggle is off', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('switch', { name: /show top level only/i }));

        await waitFor(() => {
            expect(mockUseJobs).toHaveBeenCalledWith(expect.objectContaining({ mine: true, rootsOnly: false }));
        });
        expect(screen.getByText('Child execution')).toBeInTheDocument();
    });

    it('shows only the execution linked by the job query parameter, with a way back', () => {
        const scrollIntoView = vi.spyOn(Element.prototype, 'scrollIntoView');
        const linked = mockUserJobs.find((job) => job.id === 'exec-2');
        mockUseJobExecution.mockReturnValue({
            job: linked,
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper initialRoute="/?job=exec-2">
                <MyJobsPage />
            </TestWrapper>,
        );

        const linkedRow = document.querySelector('[data-execution-id="exec-2"]') as HTMLElement;
        expect(within(linkedRow).getByTestId('execution-row__trigger')).toHaveAttribute('aria-expanded', 'true');
        expect(within(linkedRow).getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');
        expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' });
        expect(document.querySelectorAll('[data-execution-id]')).toHaveLength(1);
        expect(screen.getByRole('button', { name: 'Back to jobs' })).toBeInTheDocument();
    });

    it('shows the loading skeleton, not the empty state, while the linked execution loads', () => {
        mockUseJobExecution.mockReturnValue({
            job: null,
            isLoading: true,
            error: null,
            refetch: vi.fn(),
        });

        render(
            <TestWrapper initialRoute="/?job=exec-2">
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByTestId('my-jobs-page__loading')).toBeInTheDocument();
        expect(screen.queryByText('No job history found')).not.toBeInTheDocument();
    });

    it('fetches and expands a linked execution omitted from the current page', () => {
        const scrollIntoView = vi.spyOn(Element.prototype, 'scrollIntoView');
        const linkedExecutionId = '11111111-1111-4111-8111-111111111111';
        const linkedExecution: ApiJobExecution = {
            id: linkedExecutionId,
            parentExecutionId: '22222222-2222-4222-8222-222222222222',
            jobType: 'DownloadRefresh',
            scope: 'User',
            status: 'Completed',
            progress: 100,
            createdAt: '2024-01-15T10:31:00Z',
            startedAt: '2024-01-15T10:31:00Z',
            completedAt: '2024-01-15T10:31:05Z',
        };
        mockUseJobExecution.mockReturnValue({
            job: linkedExecution,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });

        render(
            <TestWrapper initialRoute={`/?job=${linkedExecutionId}`}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const linkedRow = document.querySelector(`[data-execution-id="${linkedExecutionId}"]`) as HTMLElement;
        expect(mockUseJobExecution).toHaveBeenCalledWith(linkedExecutionId, true);
        expect(within(linkedRow).getByTestId('execution-row__trigger')).toHaveAttribute('aria-expanded', 'true');
        expect(within(linkedRow).getByTestId('execution-row__logs')).toHaveAttribute('data-state', 'open');
        expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' });
    });

    it('filters executions by template (job type)', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'WishlistSearch' }));

        const list = screen.getByRole('list', { name: /job history/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
        expect(screen.getByText('Manual run')).toBeInTheDocument();
    });
});

describe('MyJobsPage attempts filter', () => {
    it('requests only the retry family while preserving the user scope and clears it on demand', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialRoute="/?originalExecutionId=exec-original">
                <MyJobsPage />
            </TestWrapper>,
        );

        // Assert
        expect(mockUseJobs).toHaveBeenLastCalledWith(
            expect.objectContaining({
                mine: true,
                rootsOnly: false,
                originalExecutionId: 'exec-original',
            }),
        );
        expect(screen.getByText('All attempts')).toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Show top level only' })).toBeDisabled();

        // Act
        await user.click(screen.getByRole('button', { name: 'Clear attempts filter' }));

        // Assert
        expect(screen.queryByText('All attempts')).not.toBeInTheDocument();
        expect(mockUseJobs.mock.calls.at(-1)?.[0]?.originalExecutionId).toBeUndefined();
    });

    it('clears the attempts filter when leaving history', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialRoute="/?originalExecutionId=exec-original">
                <MyJobsPage />
            </TestWrapper>,
        );

        // Act
        await user.click(screen.getByRole('tab', { name: 'Scheduled Jobs' }));
        await user.click(screen.getByRole('tab', { name: 'History' }));

        // Assert
        expect(screen.queryByText('All attempts')).not.toBeInTheDocument();
        expect(mockUseJobs.mock.calls.at(-1)?.[0]?.originalExecutionId).toBeUndefined();
    });
});

describe('MyJobsPage chain filter', () => {
    it('requests the correlationId filter from the URL', () => {
        render(
            <TestWrapper initialRoute="/?correlationId=corr-alpha">
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(mockUseJobs).toHaveBeenCalledWith(expect.objectContaining({ correlationId: 'corr-alpha' }));
    });

    it('shows the active chain filter labeled as a chain, not as parent and child', () => {
        render(
            <TestWrapper initialRoute="/?correlationId=corr-alpha">
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText('Chain: corr-alpha')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /clear chain filter/i })).toBeInTheDocument();
    });

    it('does not show the chain filter when no correlationId is in the URL', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-control="chain-filter"]')).not.toBeInTheDocument();
    });

    it('clears the chain filter when the clear button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper initialRoute="/?correlationId=corr-alpha">
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /clear chain filter/i }));

        expect(document.querySelector('[data-control="chain-filter"]')).not.toBeInTheDocument();
        await waitFor(() => {
            const lastCallParams = mockUseJobs.mock.calls.at(-1)?.[0];
            expect(lastCallParams?.correlationId).toBeUndefined();
        });
    });
});

describe('MyJobsPage status filter', () => {
    it('sends the Pending effective-status name to the server when filtering by Pending', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);
        await user.click(screen.getByRole('option', { name: 'Pending' }));

        await waitFor(() => {
            expect(mockUseJobs).toHaveBeenCalledWith(expect.objectContaining({ status: ['Pending'] }));
        });
    });

    it('sends Queued when filtering by Queued (eligible now)', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);
        await user.click(screen.getByRole('option', { name: 'Queued' }));

        await waitFor(() => {
            expect(mockUseJobs).toHaveBeenCalledWith(expect.objectContaining({ status: ['Queued'] }));
        });
    });
});

describe('MyJobsPage manual trigger', () => {
    it('shows an enabled "Run a job" affordance for a Manager with runnable jobs', () => {
        render(
            <TestWrapper role={Role.Manager}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const runButton = within(document.querySelector('.my-jobs-page__executions-toolbar') as HTMLElement).getByRole(
            'button',
            { name: /run a job/i },
        );
        expect(runButton).toBeInTheDocument();
        expect(runButton).toBeEnabled();
    });

    it('opens the run-job dialog and triggers the selected job', async () => {
        const user = userEvent.setup();
        const runJobMock = vi.fn().mockResolvedValue('job-id');
        mockUseJobs.mockImplementation(() => ({ ...pageUserJobs(), runJob: runJobMock }));

        render(
            <TestWrapper role={Role.Manager}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(
            within(document.querySelector('.my-jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        expect(dialog).toBeInTheDocument();

        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

        await user.click(within(dialog).getByRole('button', { name: /run job/i }));

        await waitFor(() => {
            expect(runJobMock).toHaveBeenCalledWith(expect.objectContaining({ jobType: 'ContentMetadataRefreshItem' }));
        });
    });

    it('limits the manual run picker to jobs the manager can run under scope rules', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(
            within(document.querySelector('.my-jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);

        expect(screen.getByRole('option', { name: 'Content metadata refresh item' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Release search scan' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'RSS Sync' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Indexer feed sync scan' })).not.toBeInTheDocument();
    });

    it('shows requester-facing manual options only for user-scoped jobs', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(
            within(document.querySelector('.my-jobs-page__executions-toolbar') as HTMLElement).getByRole('button', {
                name: /run a job/i,
            }),
        );

        const dialog = document.querySelector('[data-component="run-job-dialog"]') as HTMLElement;
        const jobSelect = dialog.querySelector('[data-field="jobType"]') as HTMLElement;
        await user.click(jobSelect);

        expect(screen.getByRole('option', { name: 'RSS Sync' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Content metadata refresh item' })).not.toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Release search scan' })).not.toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Indexer feed sync scan' })).not.toBeInTheDocument();
    });

    it('hides the "Run a job" affordance for a Viewer who may not run jobs', () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.queryByRole('button', { name: /run a job/i })).not.toBeInTheDocument();
    });

    it('hides the "Run a job" affordance for a Requester with no user-scoped manual jobs', () => {
        mockUseJobSpecs.mockImplementation((mode?: string) => ({
            specs:
                mode === 'Event'
                    ? mockEventSpecs
                    : mode === 'Manual'
                      ? mockManualSpecs
                      : mode === 'Schedule'
                        ? mockScheduleSpecs
                        : mockManualSpecs.filter((spec) => spec.scope !== 'User'),
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        }));

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.queryByRole('button', { name: /run a job/i })).not.toBeInTheDocument();
    });
});

describe('MyJobsPage executions empty state', () => {
    beforeEach(() => {
        mockUseJobs.mockImplementation((params?: { jobType?: string; pageNumber?: number; pageSize?: number }) => ({
            ...pageUserJobs(params),
            jobs: [],
            pagination: { pageNumber: 1, pageSize: EXECUTIONS_PAGE_SIZE, totalItems: 0 },
        }));
    });

    it('points an admin to the Admin > System Jobs page', () => {
        render(
            <TestWrapper role={Role.Admin}>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText(/Admin > System Jobs/)).toBeInTheDocument();
    });

    it('drops the Admin > System Jobs cross-link for a non-admin', () => {
        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText(/This list shows only jobs you ran\./)).toBeInTheDocument();
        expect(screen.queryByText(/Admin > System Jobs/)).not.toBeInTheDocument();
    });

    it('drops the run-a-job invitation for a role that cannot run jobs', () => {
        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.queryByText(/Run a job to see your execution history/)).not.toBeInTheDocument();
    });

    it('shows an error with retry instead of the empty state when the job history request fails', async () => {
        // Arrange
        const user = userEvent.setup();
        const refetch = vi.fn();
        mockUseJobs.mockReturnValue({
            jobs: [],
            pagination: null,
            runJob: vi.fn(),
            isLoading: false,
            error: new Error('Server error'),
            refetch,
        });
        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Retry' }));

        // Assert
        expect(screen.getByText('Failed to load job history')).toBeInTheDocument();
        expect(screen.queryByText('No job history found')).not.toBeInTheDocument();
        expect(refetch).toHaveBeenCalledTimes(1);
    });
});

describe('MyJobsPage Schedules tab', () => {
    it('lists the user own schedule triggers via mine=true', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(mockUseScheduleTriggers).toHaveBeenCalledWith({ mine: true });
        expect(screen.getByText('Hourly RSS Sync')).toBeInTheDocument();
    });

    it('limits the schedule type picker to user-eligible types', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(
            within(document.querySelector('.my-jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add schedule/i,
            }),
        );

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);

        expect(screen.getByRole('option', { name: 'RSS Sync' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Content metadata refresh item' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Release search scan' })).not.toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Indexer feed sync scan' })).not.toBeInTheDocument();
    });

    it('creates a user-scoped schedule trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(
            within(document.querySelector('.my-jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add schedule/i,
            }),
        );

        const nameInput = document.querySelector('[data-field="name"]') as HTMLInputElement;
        await user.type(nameInput, 'My Sync');

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'RSS Sync' }));

        await user.click(screen.getByRole('button', { name: /^save$/i }));

        await waitFor(() => {
            expect(createScheduleTriggerMock).toHaveBeenCalledWith(
                expect.objectContaining({ jobType: 'RSSSync', scope: 'User', name: 'My Sync' }),
            );
        });
    });

    it('edits an existing schedule trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(document.querySelector('[data-action="edit"]') as HTMLElement);

        const nameInput = document.querySelector('[data-field="name"]') as HTMLInputElement;
        await user.clear(nameInput);
        await user.type(nameInput, 'Renamed Sync');

        await user.click(screen.getByRole('button', { name: /^save$/i }));

        await waitFor(() => {
            expect(updateScheduleTriggerMock).toHaveBeenCalledWith(
                expect.objectContaining({
                    id: 'trigger-1',
                    patch: expect.objectContaining({ name: { value: 'Renamed Sync' } }),
                }),
            );
        });
    });

    it('deletes a schedule trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(document.querySelector('[data-action="delete"]') as HTMLElement);
        await user.click(screen.getByRole('button', { name: /^delete$/i }));

        await waitFor(() => {
            expect(deleteScheduleTriggerMock).toHaveBeenCalledWith('trigger-1');
        });
    });

    it('shows empty state with create guidance for a role that can schedule', async () => {
        const user = userEvent.setup();
        mockUseScheduleTriggers.mockReturnValue(scheduleTriggersReturn([]));

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(screen.getByText('No schedule triggers')).toBeInTheDocument();
        expect(screen.getByText(/create a schedule/i)).toBeInTheDocument();
    });

    it('drops the create-schedule guidance for a role that cannot schedule', async () => {
        const user = userEvent.setup();
        mockUseScheduleTriggers.mockReturnValue(scheduleTriggersReturn([]));

        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(screen.getByText('No schedule triggers')).toBeInTheDocument();
        expect(screen.queryByText(/create a schedule/i)).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /add schedule/i })).not.toBeInTheDocument();
    });
});

describe('MyJobsPage Events tab', () => {
    it('lists the user own event triggers via mine=true', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        expect(mockUseEventTriggers).toHaveBeenCalledWith({ mine: true });
        expect(screen.getByText('Refresh on Download')).toBeInTheDocument();
    });

    it('limits the event type picker to user-eligible types', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        await user.click(
            within(document.querySelector('.my-jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add event trigger/i,
            }),
        );

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);

        expect(screen.getByRole('option', { name: 'Content metadata refresh item' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Release auto-grab scan' })).not.toBeInTheDocument();
    });

    it('creates a user-scoped event trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        await user.click(
            within(document.querySelector('.my-jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add event trigger/i,
            }),
        );

        const form = document.querySelector('[data-component="event-trigger-form"]') as HTMLElement;
        const nameInput = form.querySelector('[data-field="name"]') as HTMLInputElement;
        await user.type(nameInput, 'On Download');

        const templateTrigger = within(form).getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

        const presetTrigger = within(form).getByRole('combobox', { name: /event pattern/i });
        await user.click(presetTrigger);
        await user.click(screen.getByRole('option', { name: 'Download completed' }));

        await user.click(within(form).getByRole('button', { name: /^save$/i }));

        await waitFor(() => {
            expect(createEventTriggerMock).toHaveBeenCalledWith(
                expect.objectContaining({ jobType: 'ContentMetadataRefreshItem', scope: 'User', name: 'On Download' }),
            );
        });
    });

    it('deletes an event trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Requester}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        const eventsList = screen.getByRole('list', { name: /event triggers/i });
        await user.click(within(eventsList).getByRole('button', { name: /delete refresh on download/i }));
        await user.click(screen.getByRole('button', { name: /confirm delete|^delete$/i }));

        await waitFor(() => {
            expect(deleteEventTriggerMock).toHaveBeenCalledWith('event-1');
        });
    });

    it('shows empty state with create guidance when no events exist', async () => {
        const user = userEvent.setup();
        mockUseEventTriggers.mockReturnValue(eventTriggersReturn([]));

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));

        expect(screen.getByText('No event triggers')).toBeInTheDocument();
        expect(screen.getByText(/create an event trigger/i)).toBeInTheDocument();
    });
});

describe('MyJobsPage URL persistence', () => {
    it('restores active tab from ?tab= search param', () => {
        render(
            <TestWrapper initialRoute="/?tab=schedules">
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /scheduled jobs/i })).toHaveAttribute('data-state', 'active');
    });

    it('defaults to executions tab when no ?tab= param', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /history/i })).toHaveAttribute('data-state', 'active');
    });
});

describe('MyJobsPage owned triggers outside the eligible catalog', () => {
    const mediaInfoSpec: ApiJobSpec = {
        jobType: 'MediaInfoExtractScan',
        displayName: 'Media info extract scan',
        category: 'Content',
        scope: 'Administrative' as JobScopeValue,
        enabled: true,
        onDemand: false,
        supportedTriggerModes: ['Schedule', 'Event'],
        parameters: [
            { name: 'maxAgeHours', description: 'Maximum age', type: 'Number', required: false, defaultValue: null },
        ],
    };

    beforeEach(() => {
        mockUseJobSpecs.mockImplementation((mode?: string) => ({
            specs:
                mode === 'Event'
                    ? mockEventSpecs
                    : mode === 'Schedule'
                      ? mockScheduleSpecs
                      : [...mockManualSpecs, mediaInfoSpec],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        }));
    });

    it('shows the template and parameters of an owned schedule, then offers only eligible templates on add', async () => {
        const user = userEvent.setup();
        mockUseScheduleTriggers.mockReturnValue(
            scheduleTriggersReturn([
                {
                    id: 'trigger-media',
                    name: 'Media scan',
                    jobType: 'MediaInfoExtractScan',
                    scope: 'Administrative' as JobScopeValue,
                    schedule: '0 * * * *',
                    executionMode: 'Queue',
                    enabled: false,
                    createdAt: '2024-01-01T00:00:00Z',
                    createdByUserId: '1',
                    parameters: { maxAgeHours: '72' },
                },
            ]),
        );

        render(
            <TestWrapper role={Role.Admin}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        await user.click(document.querySelector('[data-action="edit"]') as HTMLElement);

        expect(screen.getByRole('combobox', { name: /job template/i })).toHaveTextContent('Media info extract scan');
        expect(document.querySelector('[data-field="parameter-maxAgeHours"]')).toHaveValue(72);

        await user.click(screen.getByRole('button', { name: /cancel/i }));
        await user.click(
            within(document.querySelector('.my-jobs-page__section-header') as HTMLElement).getByRole('button', {
                name: /add schedule/i,
            }),
        );
        await user.click(screen.getByRole('combobox', { name: /job template/i }));

        expect(screen.getByRole('option', { name: 'RSS Sync' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Media info extract scan' })).not.toBeInTheDocument();
    });

    it('shows the template and parameters of an owned event trigger', async () => {
        const user = userEvent.setup();
        mockUseEventTriggers.mockReturnValue(
            eventTriggersReturn([
                {
                    id: 'event-media',
                    name: 'Media on import',
                    jobType: 'MediaInfoExtractScan',
                    scope: 'Administrative' as JobScopeValue,
                    eventFilter: { eventName: { value: 'content\\.imported' } },
                    enabled: false,
                    delaySeconds: 0,
                    createdAt: '2024-01-01T00:00:00Z',
                    createdByUserId: '1',
                    parameters: { maxAgeHours: '72' },
                },
            ]),
        );

        render(
            <TestWrapper role={Role.Admin}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        await user.click(document.querySelector('[data-action="edit-event"]') as HTMLElement);

        expect(screen.getByRole('combobox', { name: /job template/i })).toHaveTextContent('Media info extract scan');
        expect(document.querySelector('[data-field="parameter-maxAgeHours"]')).toHaveValue(72);
    });
});

describe('MyJobsPage trigger actions follow server authorization', () => {
    it.each([
        [Role.Requester, false],
        [Role.Manager, true],
    ])('shows Run now on a content trigger for role %s: %s', async (role, visible) => {
        const user = userEvent.setup();
        mockUseScheduleTriggers.mockReturnValue(
            scheduleTriggersReturn([
                {
                    id: 'trigger-content',
                    name: 'Refresh item',
                    jobType: 'ContentMetadataRefreshItem',
                    scope: 'User' as JobScopeValue,
                    schedule: '0 0 1 * *',
                    executionMode: 'Queue',
                    enabled: false,
                    createdAt: '2024-01-01T00:00:00Z',
                    createdByUserId: '1',
                },
            ]),
        );

        render(
            <TestWrapper role={role}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));

        expect(document.querySelector('[data-action="run-now"]') !== null).toBe(visible);
    });

    it.each([
        [Role.Manager, false],
        [Role.Admin, true],
    ])('lets role %s manage an owned Administrative-scoped trigger: %s', async (role, manageable) => {
        const user = userEvent.setup();
        mockUseScheduleTriggers.mockReturnValue(
            scheduleTriggersReturn([{ ...mockScheduleTriggers[0], scope: 'Administrative' as JobScopeValue }]),
        );
        mockUseEventTriggers.mockReturnValue(
            eventTriggersReturn([{ ...mockEventTriggers[0], scope: 'Administrative' as JobScopeValue }]),
        );

        render(
            <TestWrapper role={role}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: /scheduled jobs/i }));
        expect(document.querySelector('[data-action="edit"]') !== null).toBe(manageable);
        expect(document.querySelector('[data-action="toggle"]')).toHaveProperty('disabled', !manageable);

        await user.click(screen.getByRole('tab', { name: /event triggers/i }));
        expect(document.querySelector('[data-action="edit-event"]') !== null).toBe(manageable);
    });
});

describe('MyJobsPage phone actions button', () => {
    it.each([
        [/history/i, 'Run a job'],
        [/scheduled jobs/i, 'Add Schedule'],
        [/event triggers/i, 'Add Event Trigger'],
    ])('offers the %s tab action as the floating button', async (tab, label) => {
        const user = userEvent.setup();
        render(
            <TestWrapper role={Role.Manager}>
                <MyJobsPage />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('tab', { name: tab }));

        expect(document.querySelector('.quick-actions-button__fab')).toHaveAttribute('aria-label', label);
    });
});

describe('MyJobsPage trigger request failure', () => {
    it.each([
        ['Scheduled Jobs', 'Failed to load schedule triggers', 'No schedule triggers', 1, 0],
        ['Event Triggers', 'Failed to load event triggers', 'No event triggers', 0, 1],
    ])(
        'shows an error with retry instead of the empty state when the %s request fails',
        async (tab, failedTitle, emptyTitle, scheduleRetries, eventRetries) => {
            // Arrange
            const user = userEvent.setup();
            const refetchSchedules = vi.fn();
            const refetchEvents = vi.fn();
            mockUseScheduleTriggers.mockReturnValue({
                ...scheduleTriggersReturn([]),
                error: new Error('Server error'),
                refetch: refetchSchedules,
            });
            mockUseEventTriggers.mockReturnValue({
                ...eventTriggersReturn([]),
                error: new Error('Server error'),
                refetch: refetchEvents,
            });
            render(
                <TestWrapper role={Role.Requester}>
                    <MyJobsPage />
                </TestWrapper>,
            );
            await user.click(screen.getByRole('tab', { name: tab }));

            // Act
            await user.click(screen.getByRole('button', { name: 'Retry' }));

            // Assert
            expect(screen.getByText(failedTitle)).toBeInTheDocument();
            expect(screen.queryByText(emptyTitle)).not.toBeInTheDocument();
            expect(refetchSchedules).toHaveBeenCalledTimes(scheduleRetries);
            expect(refetchEvents).toHaveBeenCalledTimes(eventRetries);
        },
    );
});
