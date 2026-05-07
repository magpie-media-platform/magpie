import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';

import { MyJobsPage } from './MyJobsPage';
import type { ApiJobExecution, JobExecutionStatusValue, JobScopeValue } from '@/hooks/useJobs';
import type { ApiScheduleTrigger } from '@/hooks/useScheduleTriggers';
import type { ApiEventTrigger } from '@/hooks/useEventTriggers';

vi.mock('@/hooks/useJobs', () => ({
    useJobs: vi.fn(),
    jobExecutionStatusLabels: {
        0: 'Created',
        1: 'Queued',
        2: 'Running',
        3: 'Completed',
        4: 'Failed',
        5: 'Cancelled',
        6: 'Cancelling',
    },
}));

vi.mock('@/hooks/useScheduleTriggers', () => ({
    useScheduleTriggers: vi.fn(),
}));

vi.mock('@/hooks/useEventTriggers', () => ({
    useEventTriggers: vi.fn(),
}));

const mockUserJobs: ApiJobExecution[] = [
    {
        id: 'exec-1',
        jobType: 'RefreshMetadata',
        scope: 3 as JobScopeValue,
        status: 2 as JobExecutionStatusValue,
        progress: 45,
        createdAt: '2024-01-15T10:30:00Z',
        startedAt: '2024-01-15T10:30:00Z',
        completedAt: null,
        statusMessage: 'Processing items...',
    },
    {
        id: 'exec-2',
        jobType: 'SearchMissing',
        scope: 3 as JobScopeValue,
        status: 3 as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T09:00:00Z',
        startedAt: '2024-01-15T09:00:00Z',
        completedAt: '2024-01-15T09:15:00Z',
        statusMessage: 'Found 5 items',
    },
    {
        id: 'exec-3',
        jobType: 'SystemBackup',
        scope: 1 as JobScopeValue,
        status: 3 as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T08:00:00Z',
        startedAt: '2024-01-15T08:00:00Z',
        completedAt: '2024-01-15T08:05:00Z',
        statusMessage: null,
    },
];

const mockScheduleTriggers: ApiScheduleTrigger[] = [
    {
        id: 'trigger-1',
        name: 'Hourly RSS Sync',
        jobType: 'RSSSync',
        scope: 3 as JobScopeValue,
        schedule: '0 * * * *',
        executionMode: 0,
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
        nextRunAt: '2024-01-15T12:00:00Z',
    },
    {
        id: 'trigger-2',
        name: 'System Maintenance',
        jobType: 'Maintenance',
        scope: 0 as JobScopeValue,
        schedule: '0 0 * * *',
        executionMode: 0,
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
];

const mockEventTriggers: ApiEventTrigger[] = [
    {
        id: 'event-1',
        name: 'Refresh on Download',
        jobType: 'RefreshMetadata',
        scope: 3 as JobScopeValue,
        executionMode: 0,
        eventFilter: {
            eventName: { value: 'download\\.completed' },
        },
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-2',
        name: 'System Event',
        jobType: 'SystemCheck',
        scope: 0 as JobScopeValue,
        executionMode: 0,
        eventFilter: {},
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
];

import { useJobs } from '@/hooks/useJobs';
import { useScheduleTriggers } from '@/hooks/useScheduleTriggers';
import { useEventTriggers } from '@/hooks/useEventTriggers';

const mockUseJobs = useJobs as ReturnType<typeof vi.fn>;
const mockUseScheduleTriggers = useScheduleTriggers as ReturnType<typeof vi.fn>;
const mockUseEventTriggers = useEventTriggers as ReturnType<typeof vi.fn>;

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
                <AuthProvider initialUser={mockUser}>
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

beforeEach(() => {
    vi.clearAllMocks();

    mockUseJobs.mockReturnValue({
        jobs: mockUserJobs,
        runJob: vi.fn().mockResolvedValue('job-id'),
        isLoading: false,
        error: null,
        refetch: vi.fn(),
    });

    mockUseScheduleTriggers.mockReturnValue({
        triggers: mockScheduleTriggers,
        isLoading: false,
        error: null,
    });

    mockUseEventTriggers.mockReturnValue({
        triggers: mockEventTriggers,
        isLoading: false,
        error: null,
    });
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

    it('renders job count for user-scoped jobs only', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const count = screen.getByTestId('my-jobs-page__count');
        expect(count).toBeInTheDocument();
        expect(count).toHaveTextContent('2');
    });

    it('renders the page icon as decorative', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const icon = screen.getByTestId('my-jobs-page__icon');
        expect(icon).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('MyJobsPage tabs', () => {
    it('renders Executions, Schedules, and Events tabs', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /executions/i })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /schedules/i })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /events/i })).toBeInTheDocument();
    });

    it('Executions tab is selected by default', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const executionsTab = screen.getByRole('tab', { name: /executions/i });
        expect(executionsTab).toHaveAttribute('data-state', 'active');
    });

    it('switches to Schedules tab when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        expect(schedulesTab).toHaveAttribute('data-state', 'active');
    });

    it('switches to Events tab when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        expect(eventsTab).toHaveAttribute('data-state', 'active');
    });
});

describe('MyJobsPage Executions tab', () => {
    it('renders execution list when Executions tab is selected', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /job executions/i })).toBeInTheDocument();
    });

    it('only shows user-scoped executions', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /job executions/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(2);
    });

    it('renders status summary badges', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-status="running"]')).toBeInTheDocument();
        expect(document.querySelector('[data-status="completed"]')).toBeInTheDocument();
    });

    it('renders filters component', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="job-execution-filters"]')).toBeInTheDocument();
    });

    it('shows progress bar for running executions', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const progressBars = screen.getAllByTestId('execution-row__progress-bar');
        expect(progressBars.length).toBeGreaterThan(0);
    });

    it('shows execution messages', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText('Processing items...')).toBeInTheDocument();
    });
});

describe('MyJobsPage Executions tab - filtering', () => {
    it('filters executions by status', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);

        const completedOption = screen.getByRole('option', { name: 'Completed' });
        await user.click(completedOption);

        const list = screen.getByRole('list', { name: /job executions/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
    });
});

describe('MyJobsPage Schedules tab', () => {
    it('renders schedule triggers list when Schedules tab is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        expect(screen.getByRole('list', { name: /schedule triggers/i })).toBeInTheDocument();
    });

    it('only shows user-scoped schedule triggers', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        const list = screen.getByRole('list', { name: /schedule triggers/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
    });

    it('renders trigger names', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        expect(screen.getByText('Hourly RSS Sync')).toBeInTheDocument();
    });

    it('shows Run button for schedule triggers', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        const runButton = document.querySelector('[data-action="run"]');
        expect(runButton).toBeInTheDocument();
    });

    it('calls runJob when Run button is clicked', async () => {
        const user = userEvent.setup();
        const runJobMock = vi.fn().mockResolvedValue('job-id');
        mockUseJobs.mockReturnValue({
            jobs: mockUserJobs,
            runJob: runJobMock,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });

        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        const runButton = document.querySelector('[data-action="run"]') as HTMLElement;
        await user.click(runButton);

        await waitFor(() => {
            expect(runJobMock).toHaveBeenCalledWith({
                jobType: 'RSSSync',
                extensionId: undefined,
                parameters: undefined,
            });
        });
    });
});

describe('MyJobsPage Events tab', () => {
    it('renders event triggers list when Events tab is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        expect(screen.getByRole('list', { name: /event triggers/i })).toBeInTheDocument();
    });

    it('only shows user-scoped event triggers', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        const list = screen.getByRole('list', { name: /event triggers/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
    });

    it('renders event trigger names', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        expect(screen.getByText('Refresh on Download')).toBeInTheDocument();
    });

    it('shows event pattern description', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        expect(screen.getByText('Download completed')).toBeInTheDocument();
    });
});

describe('MyJobsPage accessibility', () => {
    it('executions list has accessible label', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /job executions/i })).toBeInTheDocument();
    });

    it('summary section has accessible label', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Executions by status"]');
        expect(summary).toBeInTheDocument();
    });

    it('progress bars have aria attributes', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const progressBars = document.querySelectorAll('[role="progressbar"]');
        progressBars.forEach((bar) => {
            expect(bar).toHaveAttribute('aria-valuenow');
            expect(bar).toHaveAttribute('aria-valuemin', '0');
            expect(bar).toHaveAttribute('aria-valuemax', '100');
            expect(bar).toHaveAttribute('aria-label');
        });
    });

    it('run buttons have aria-labels', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        const runButtons = document.querySelectorAll('[data-action="run"]');
        runButtons.forEach((btn) => {
            expect(btn).toHaveAttribute('aria-label');
        });
    });
});

describe('MyJobsPage empty states', () => {
    it('shows empty state when no user-scoped jobs', () => {
        mockUseJobs.mockReturnValue({
            jobs: [],
            runJob: vi.fn(),
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        });

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        expect(screen.getByText('No job executions found')).toBeInTheDocument();
    });

    it('shows empty state when no user-scoped schedule triggers', async () => {
        const user = userEvent.setup();

        mockUseScheduleTriggers.mockReturnValue({
            triggers: [],
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        expect(screen.getByText('No schedule triggers')).toBeInTheDocument();
        expect(screen.getByText('Schedule triggers are managed by administrators.')).toBeInTheDocument();
    });

    it('shows admin link in schedule empty state for admin users', async () => {
        const user = userEvent.setup();

        mockUseScheduleTriggers.mockReturnValue({
            triggers: [],
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper role={Role.Admin}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        const link = screen.getByRole('link', { name: /manage in admin/i });
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/admin/jobs');
    });

    it('does not show admin link in schedule empty state for non-admin users', async () => {
        const user = userEvent.setup();

        mockUseScheduleTriggers.mockReturnValue({
            triggers: [],
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        await user.click(schedulesTab);

        expect(screen.getByText('No schedule triggers')).toBeInTheDocument();
        expect(screen.queryByRole('link', { name: /manage in admin/i })).not.toBeInTheDocument();
    });

    it('shows empty state when no user-scoped event triggers', async () => {
        const user = userEvent.setup();

        mockUseEventTriggers.mockReturnValue({
            triggers: [],
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        expect(screen.getByText('No event triggers')).toBeInTheDocument();
        expect(screen.getByText('Event triggers are managed by administrators.')).toBeInTheDocument();
    });

    it('shows admin link in event empty state for admin users', async () => {
        const user = userEvent.setup();

        mockUseEventTriggers.mockReturnValue({
            triggers: [],
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper role={Role.Admin}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        const link = screen.getByRole('link', { name: /manage in admin/i });
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/admin/jobs');
    });

    it('does not show admin link in event empty state for non-admin users', async () => {
        const user = userEvent.setup();

        mockUseEventTriggers.mockReturnValue({
            triggers: [],
            isLoading: false,
            error: null,
        });

        render(
            <TestWrapper role={Role.Viewer}>
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        await user.click(eventsTab);

        expect(screen.getByText('No event triggers')).toBeInTheDocument();
        expect(screen.queryByRole('link', { name: /manage in admin/i })).not.toBeInTheDocument();
    });
});

describe('MyJobsPage URL persistence', () => {
    it('restores active tab from ?tab= search param', () => {
        render(
            <TestWrapper initialRoute="/?tab=schedules">
                <MyJobsPage />
            </TestWrapper>,
        );

        const schedulesTab = screen.getByRole('tab', { name: /schedules/i });
        expect(schedulesTab).toHaveAttribute('data-state', 'active');
    });

    it('restores events tab from ?tab=events search param', () => {
        render(
            <TestWrapper initialRoute="/?tab=events">
                <MyJobsPage />
            </TestWrapper>,
        );

        const eventsTab = screen.getByRole('tab', { name: /events/i });
        expect(eventsTab).toHaveAttribute('data-state', 'active');
    });

    it('defaults to executions tab when no ?tab= param', () => {
        render(
            <TestWrapper>
                <MyJobsPage />
            </TestWrapper>,
        );

        const executionsTab = screen.getByRole('tab', { name: /executions/i });
        expect(executionsTab).toHaveAttribute('data-state', 'active');
    });

    it('restores template filter from ?template= search param', () => {
        render(
            <TestWrapper initialRoute="/?template=RefreshMetadata">
                <MyJobsPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /job executions/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
    });

    it('restores status filter from ?status= search param', () => {
        render(
            <TestWrapper initialRoute="/?status=completed">
                <MyJobsPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /job executions/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
    });

    it('restores both template and status filters from search params', () => {
        render(
            <TestWrapper initialRoute="/?template=RefreshMetadata&status=running">
                <MyJobsPage />
            </TestWrapper>,
        );

        const list = screen.getByRole('list', { name: /job executions/i });
        const items = within(list).getAllByRole('listitem');

        expect(items.length).toBe(1);
    });
});
