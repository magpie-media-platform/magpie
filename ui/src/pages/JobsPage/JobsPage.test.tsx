import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import type { ApiJobExecution, JobExecutionStatusValue, JobScopeValue } from '@/hooks/useJobs';
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

import { apiGet, apiPost } from '@/lib/api';

import { JobsPage } from './JobsPage';

const mockApiGet = vi.mocked(apiGet);
const mockApiPost = vi.mocked(apiPost);

const mockJobs: ApiJobExecution[] = [
    {
        id: 'exec-1',
        jobType: 'RSS Sync',
        scope: 2 as JobScopeValue,
        status: 2 as JobExecutionStatusValue,
        progress: 45,
        createdAt: '2024-01-15T10:30:00Z',
        startedAt: '2024-01-15T10:30:00Z',
        completedAt: null,
        statusMessage: 'Checking feed: NZBgeek',
    },
    {
        id: 'exec-2',
        jobType: 'Refresh Metadata',
        scope: 2 as JobScopeValue,
        status: 2 as JobExecutionStatusValue,
        progress: 78,
        createdAt: '2024-01-15T10:28:00Z',
        startedAt: '2024-01-15T10:28:00Z',
        completedAt: null,
        statusMessage: 'Processing: Breaking Bad S05E16',
    },
    {
        id: 'exec-3',
        jobType: 'Search Missing',
        scope: 2 as JobScopeValue,
        status: 1 as JobExecutionStatusValue,
        progress: 0,
        createdAt: '2024-01-15T10:32:00Z',
        startedAt: null,
        completedAt: null,
        statusMessage: 'Waiting in queue',
    },
    {
        id: 'exec-4',
        jobType: 'Cleanup Downloads',
        scope: 2 as JobScopeValue,
        status: 3 as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T10:00:00Z',
        startedAt: '2024-01-15T10:00:00Z',
        completedAt: '2024-01-15T10:02:30Z',
        statusMessage: 'Removed 5 completed downloads',
    },
    {
        id: 'exec-5',
        jobType: 'Sync Plex Library',
        scope: 2 as JobScopeValue,
        status: 3 as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T09:45:00Z',
        startedAt: '2024-01-15T09:45:00Z',
        completedAt: '2024-01-15T09:46:15Z',
        statusMessage: 'Synced 1,234 items',
    },
    {
        id: 'exec-6',
        jobType: 'Backup Database',
        scope: 1 as JobScopeValue,
        status: 3 as JobExecutionStatusValue,
        progress: 100,
        createdAt: '2024-01-15T09:00:00Z',
        startedAt: '2024-01-15T09:00:00Z',
        completedAt: '2024-01-15T09:00:45Z',
        statusMessage: 'Backup created: magpie_20240115_090000.db',
    },
    {
        id: 'exec-7',
        jobType: 'Import Trakt List',
        scope: 2 as JobScopeValue,
        status: 4 as JobExecutionStatusValue,
        progress: 34,
        createdAt: '2024-01-15T08:30:00Z',
        startedAt: '2024-01-15T08:30:00Z',
        completedAt: '2024-01-15T08:31:12Z',
        statusMessage: 'API error: Rate limit exceeded',
    },
    {
        id: 'exec-8',
        jobType: 'RSS Sync',
        scope: 2 as JobScopeValue,
        status: 5 as JobExecutionStatusValue,
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
        scope: 2 as JobScopeValue,
        schedule: '0 * * * *',
        executionMode: 0,
        enabled: true,
        nextRunAt: '2024-01-15T12:00:00Z',
        lastRunAt: '2024-01-15T11:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-2',
        name: 'Daily Metadata Refresh',
        jobType: 'Refresh Metadata',
        scope: 2 as JobScopeValue,
        schedule: '0 6 * * *',
        executionMode: 0,
        enabled: true,
        nextRunAt: '2024-01-16T06:00:00Z',
        lastRunAt: '2024-01-15T06:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-3',
        name: 'Weekly Database Backup',
        jobType: 'Backup Database',
        scope: 1 as JobScopeValue,
        schedule: '0 0 * * 0',
        executionMode: 0,
        enabled: true,
        nextRunAt: '2024-01-21T00:00:00Z',
        lastRunAt: '2024-01-14T00:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-4',
        name: 'Nightly Missing Search',
        jobType: 'Search Missing',
        scope: 2 as JobScopeValue,
        schedule: '0 2 * * *',
        executionMode: 0,
        enabled: false,
        nextRunAt: null,
        lastRunAt: '2024-01-14T02:00:00Z',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'trigger-5',
        name: 'Plex Library Sync',
        jobType: 'Sync Plex Library',
        scope: 2 as JobScopeValue,
        schedule: '0 */6 * * *',
        executionMode: 0,
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
        jobType: 'Refresh Metadata',
        scope: 2 as JobScopeValue,
        executionMode: 0,
        eventFilter: {
            eventName: { value: 'download\\.completed' },
        },
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-2',
        name: 'Search on Content Added',
        jobType: 'Search Missing',
        scope: 2 as JobScopeValue,
        executionMode: 0,
        eventFilter: {
            eventName: { value: 'content\\.added' },
            eventContext: {
                contentType: { value: 'episode' },
            },
        },
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'event-3',
        name: 'Sync Plex on Import',
        jobType: 'Sync Plex Library',
        scope: 2 as JobScopeValue,
        executionMode: 1,
        eventFilter: {
            eventName: { value: 'file\\.imported' },
        },
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
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
}: {
    children: ReactNode;
    role?: Role;
    initialRoute?: string;
}) {
    const mockUser = { id: '1', username: 'test', role };
    const queryClient = createTestQueryClient();

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

function setupDefaultMocks() {
    mockApiGet.mockImplementation((url: string) => {
        if (url === '/jobs' || url.startsWith('/jobs?')) {
            return Promise.resolve({
                items: mockJobs,
                pageNumber: 1,
                pageSize: 20,
                totalItems: mockJobs.length,
            });
        }
        if (url === '/schedule-triggers') {
            return Promise.resolve(mockScheduleTriggers);
        }
        if (url === '/event-triggers' || url.startsWith('/event-triggers?')) {
            return Promise.resolve(mockEventTriggers);
        }
        return Promise.reject(new Error(`Unmocked URL: ${url}`));
    });
    mockApiPost.mockResolvedValue('new-job-id');
}

beforeEach(() => {
    vi.clearAllMocks();
    setupDefaultMocks();
});

afterEach(() => {
    vi.restoreAllMocks();
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

        expect(screen.getByRole('heading', { name: 'Jobs', level: 1 })).toBeInTheDocument();
    });

    it('renders the job count for default executions tab', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const count = screen.getByTestId('jobs-page__count');
            expect(count).toHaveTextContent('8');
        });
    });

    it('title badge updates to combined triggers count on Triggers tab', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const count = screen.getByTestId('jobs-page__count');
            expect(count).toHaveTextContent('8');
        });
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
    it('renders Executions and Triggers tabs', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /executions/i })).toBeInTheDocument();
        expect(screen.getByRole('tab', { name: /triggers/i })).toBeInTheDocument();
    });

    it('Executions tab is selected by default', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const executionsTab = screen.getByRole('tab', { name: /executions/i });
        expect(executionsTab).toHaveAttribute('data-state', 'active');
    });

    it('switches to Triggers tab when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        expect(triggersTab).toHaveAttribute('data-state', 'active');
    });

    it('shows combined trigger count in Triggers tab', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const triggersTab = screen.getByRole('tab', { name: /triggers/i });
            expect(within(triggersTab).getByText('8')).toBeInTheDocument();
        });
    });
});

describe('JobsPage Executions tab', () => {
    it('renders execution list', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /job executions/i })).toBeInTheDocument();
    });

    it('renders execution items', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job executions/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(8);
        });
    });

    it('renders status summary badges', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-status="running"]')).toBeInTheDocument();
            expect(document.querySelector('[data-status="pending"]')).toBeInTheDocument();
            expect(document.querySelector('[data-status="completed"]')).toBeInTheDocument();
            expect(document.querySelector('[data-status="failed"]')).toBeInTheDocument();
            expect(document.querySelector('[data-status="cancelled"]')).toBeInTheDocument();
        });
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
            const list = screen.getByRole('list', { name: /job executions/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });

        const statusTrigger = screen.getByRole('combobox', { name: /filter by status/i });
        await user.click(statusTrigger);

        const failedOption = screen.getByRole('option', { name: 'Failed' });
        await user.click(failedOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job executions/i });
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
            const list = screen.getByRole('list', { name: /job executions/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(8);
        });

        const templateTrigger = screen.getByRole('combobox', { name: /filter by template/i });
        await user.click(templateTrigger);

        const rssSyncOption = screen.getByRole('option', { name: 'RSS Sync' });
        await user.click(rssSyncOption);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job executions/i });
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
            const list = screen.getByRole('list', { name: /job executions/i });
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
            expect(screen.getByText('No job executions found')).toBeInTheDocument();
            expect(screen.getByText('Try adjusting your filters')).toBeInTheDocument();
        });
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
});

describe('JobsPage Triggers tab', () => {
    it('renders Triggers tab', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('tab', { name: /triggers/i })).toBeInTheDocument();
    });

    it('shows combined triggers count in tab', async () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const triggersTab = screen.getByRole('tab', { name: /triggers/i });
            expect(within(triggersTab).getByText('8')).toBeInTheDocument();
        });
    });

    it('renders triggers list when Triggers tab is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        expect(screen.getByRole('list', { name: /triggers/i })).toBeInTheDocument();
    });

    it('renders both schedule and event trigger items', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /triggers/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(8);
        });
    });

    it('renders schedule trigger names', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            expect(screen.getByText('Hourly RSS Sync')).toBeInTheDocument();
            expect(screen.getByText('Daily Metadata Refresh')).toBeInTheDocument();
            expect(screen.getByText('Weekly Database Backup')).toBeInTheDocument();
        });
    });

    it('renders event trigger names', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            expect(screen.getByText('Refresh on Download Complete')).toBeInTheDocument();
            expect(screen.getByText('Search on Content Added')).toBeInTheDocument();
            expect(screen.getByText('Sync Plex on Import')).toBeInTheDocument();
        });
    });

    it('applies data-trigger-id to trigger items', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const scheduleItems = document.querySelectorAll('[data-trigger-id]');
            const eventItems = document.querySelectorAll('[data-event-trigger-id]');
            expect(scheduleItems.length + eventItems.length).toBe(8);
        });
    });
});

describe('JobsPage Triggers tab - Add Trigger', () => {
    it('shows Add Schedule and Add Event Trigger buttons for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        expect(screen.getByRole('button', { name: /add schedule/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /add event trigger/i })).toBeInTheDocument();
    });

    it('hides Add buttons for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        expect(screen.queryByRole('button', { name: /add schedule/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /add event trigger/i })).not.toBeInTheDocument();
    });

    it('opens schedule form sheet when Add Schedule is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        const addButton = screen.getByRole('button', { name: /add schedule/i });
        await user.click(addButton);

        expect(screen.getByText('Add Schedule Trigger')).toBeInTheDocument();
    });
});

describe('JobsPage Triggers tab - Trigger Actions', () => {
    it('shows toggle switch for each trigger', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const switches = document.querySelectorAll('[data-action="toggle"]');
            expect(switches.length).toBe(5);
        });
    });

    it('shows edit and delete buttons for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(editButtons.length).toBe(5);
            expect(deleteButtons.length).toBe(5);
        });
    });

    it('hides edit and delete buttons for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

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

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const runNowButtons = document.querySelectorAll('[data-action="run-now"]');
            expect(runNowButtons.length).toBe(5);
        });
    });

    it('opens edit form when edit button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

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

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

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

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        const runNowButton = await waitFor(() => {
            const btn = document.querySelector('[data-action="run-now"]');
            expect(btn).toBeInTheDocument();
            return btn as HTMLElement;
        });
        await user.click(runNowButton);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/jobs', expect.anything());
        });
    });
});

describe('JobsPage Triggers tab - Event Trigger Actions', () => {
    it('shows toggle switch for event triggers', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const switches = document.querySelectorAll('[data-action="toggle-event"]');
            expect(switches.length).toBe(3);
        });
    });

    it('shows edit and delete buttons for event triggers for Admin role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Admin}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit-event"]');
            const deleteButtons = document.querySelectorAll('[data-action="delete-event"]');
            expect(editButtons.length).toBe(3);
            expect(deleteButtons.length).toBe(3);
        });
    });

    it('hides edit and delete buttons for event triggers for Manager role', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper role={Role.Manager}>
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        expect(document.querySelectorAll('[data-action="edit-event"]').length).toBe(0);
        expect(document.querySelectorAll('[data-action="delete-event"]').length).toBe(0);
    });
});

describe('JobsPage accessibility', () => {
    it('executions list has accessible label', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        expect(screen.getByRole('list', { name: /job executions/i })).toBeInTheDocument();
    });

    it('summary section has accessible label', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const summary = document.querySelector('[aria-label="Executions by status"]');
        expect(summary).toBeInTheDocument();
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

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

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

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

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

describe('JobsPage empty states', () => {
    it('shows empty state when no jobs', () => {
        mockApiGet.mockImplementation((url: string) => {
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

        expect(screen.getByText('No job executions found')).toBeInTheDocument();
    });

    it('shows empty state when no triggers', async () => {
        const user = userEvent.setup();

        mockApiGet.mockImplementation((url: string) => {
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

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        await user.click(triggersTab);

        expect(screen.getByText('No triggers found')).toBeInTheDocument();
    });
});

describe('JobsPage URL persistence', () => {
    it('restores active tab from ?tab=triggers search param', () => {
        render(
            <TestWrapper initialRoute="/?tab=triggers">
                <JobsPage />
            </TestWrapper>,
        );

        const triggersTab = screen.getByRole('tab', { name: /triggers/i });
        expect(triggersTab).toHaveAttribute('data-state', 'active');
    });

    it('defaults to executions tab when no ?tab= param', () => {
        render(
            <TestWrapper>
                <JobsPage />
            </TestWrapper>,
        );

        const executionsTab = screen.getByRole('tab', { name: /executions/i });
        expect(executionsTab).toHaveAttribute('data-state', 'active');
    });

    it('restores template filter from ?template= search param', async () => {
        render(
            <TestWrapper initialRoute="/?template=RSS%20Sync">
                <JobsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /job executions/i });
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
            const list = screen.getByRole('list', { name: /job executions/i });
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
            const list = screen.getByRole('list', { name: /job executions/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(1);
        });
    });
});
