import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';

import { AuthProvider, Role } from '@/auth';
import { LocalContentPreviewProvider } from '@/components/content/LocalContentPreview';
import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { testEffectivePermissions } from '@/test/permissions';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
    };
});
vi.mock('@/hooks/useLibraryTypes', () => ({ useLibraryTypes: () => ({ subjectTypes: [] }) }));

import { apiGet, apiPost } from '@/lib/api';

import { ActivityLogPage } from './ActivityLogPage';

const mockApiGet = vi.mocked(apiGet);
const mockApiPost = vi.mocked(apiPost);

const auditHistoryPage = {
    items: [
        {
            id: 'audit-1',
            kind: 'AuditRecord',
            timestamp: '2026-10-07T11:56:00Z',
            label: 'SignIn',
            category: 'Security',
            detail: 'Signed in with a password',
            entityId: 'user-2',
            entityType: 'User',
            entityName: 'alice',
            userId: 'user-2',
        },
        {
            id: 'job-1',
            kind: 'JobExecution',
            timestamp: '2026-10-07T11:30:00Z',
            label: 'RetentionCleanup',
            category: 'Jobs',
            status: 'Failed',
            userId: 'system-user',
        },
        {
            id: 'audit-2',
            kind: 'AuditRecord',
            timestamp: '2026-10-07T11:20:00Z',
            label: 'Create',
            category: 'Jobs',
            detail: 'Created job run for DownloadRefresh.',
            correlationId: 'correlation-1',
            entityId: 'job-2',
            entityType: 'JobExecution',
            entityName: 'DownloadRefresh',
        },
        {
            id: 'audit-3',
            kind: 'AuditRecord',
            timestamp: '2026-10-07T11:10:00Z',
            label: 'Update',
            category: 'Content',
            entityId: 'wishlist-1',
            entityType: 'Wishlist',
            entityName: 'Weekend films',
        },
        {
            id: 'audit-4',
            kind: 'AuditRecord',
            timestamp: '2026-10-07T11:00:00Z',
            label: 'Delete',
            category: 'Integrations',
            entityId: 'client-1',
            entityType: 'DownloadClient',
        },
    ],
    pageNumber: 1,
    pageSize: 25,
    totalItems: 5,
};

const retentionSpec = {
    jobType: 'RetentionCleanup',
    displayName: 'Retention cleanup',
    category: 'System',
    scope: 'Administrative',
    enabled: true,
    onDemand: true,
    supportedTriggerModes: ['Manual', 'Schedule'],
    parameters: [
        { name: 'jobExecutionRetentionDays', type: 'Number', required: false, defaultValue: '30' },
        { name: 'auditRetentionDays', type: 'Number', required: false, defaultValue: '90' },
    ],
};

function LocationProbe() {
    const { search } = useLocation();
    return <output data-testid="activity-log-page__location-search">{search}</output>;
}

function TestWrapper({ children, initialEntry }: { children: ReactNode; initialEntry: string }) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[initialEntry]}>
                <AuthProvider
                    initialUser={{ id: 'user-1', username: 'magpie-admin', role: Role.Admin }}
                    initialPermissions={testEffectivePermissions()}
                >
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                <LocalContentPreviewProvider>{children}</LocalContentPreviewProvider>
                                <LocationProbe />
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

describe('ActivityLogPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-10-07T12:00:00Z'));
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/audit-history')) {
                return Promise.resolve(auditHistoryPage);
            }
            if (url.startsWith('/users')) {
                return Promise.resolve({
                    items: [
                        { id: 'user-2', username: 'alice', role: 'Admin', providerType: 'local' },
                        { id: 'system-user', username: 'system', role: 'Admin', providerType: 'system' },
                    ],
                    totalItems: 2,
                });
            }
            if (url.startsWith('/jobs/specs')) {
                return Promise.resolve([retentionSpec]);
            }
            if (url.startsWith('/schedule-triggers')) {
                return Promise.resolve([
                    { id: 'trigger-1', jobType: 'RetentionCleanup', enabled: true, parameters: {} },
                ]);
            }
            return Promise.resolve(null);
        });
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it.each([
        ['?category=Security%2CSystem', 'category=Security&category=System'],
        ['?search=password', 'search=password'],
        ['?user=magpie', 'hasUser=false'],
        ['?user=user-2', 'userId=user-2'],
        ['?status=Failed', 'status=Failed'],
        ['?time=7d', 'from=2026-09-30T12%3A00%3A00.000Z'],
        ['?sort=time%3Aasc', 'sortDirection=Ascending'],
        ['?page=3', 'pageNumber=3'],
    ])('RequestsTheFilteredFeed_WhenTheUrlHoldsAFilter %s', async (search, expectedParameter) => {
        // Arrange
        // Act
        render(
            <TestWrapper initialEntry={`/admin/activity${search}`}>
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Assert
        await waitFor(() =>
            expect(mockApiGet).toHaveBeenCalledWith(
                expect.stringMatching(new RegExp(`^/audit-history\\?.*${expectedParameter}`)),
            ),
        );
    });

    it('WritesTheCategoryToTheUrl_WhenACategoryIsChosen', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Act
        await user.click(await screen.findByRole('button', { name: 'Filter by Category' }));
        await user.click(screen.getByRole('option', { name: 'Security' }));
        await user.click(screen.getByRole('option', { name: 'System' }));

        // Assert
        expect(screen.getByTestId('activity-log-page__location-search')).toHaveTextContent(
            '?category=Security%2CSystem',
        );
    });

    it('WritesTheEventTextToTheUrl_WhenTheEventFilterIsApplied', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Act
        await user.click(await screen.findByRole('button', { name: 'Filter by Event' }));
        await user.type(screen.getByLabelText('Event'), 'password');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        // Assert
        expect(screen.getByTestId('activity-log-page__location-search')).toHaveTextContent('?search=password');
    });

    it.each([
        ['User', 'Magpie', '?user=magpie'],
        ['User', 'alice', '?user=user-2'],
        ['Status', 'Cancelling', '?status=CancellationRequested'],
        ['Time', 'Last 24 hours', '?time=24h'],
    ])('WritesTheChoiceToTheUrl_WhenAColumnFilterIsChosen %s %s', async (column, choice, expectedSearch) => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );
        await screen.findByRole('gridcell', { name: 'Magpie' });

        // Act
        await user.click(screen.getByRole('button', { name: `Filter by ${column}` }));
        await user.click(screen.getByRole('menuitemradio', { name: choice }));

        // Assert
        expect(screen.getByTestId('activity-log-page__location-search')).toHaveTextContent(expectedSearch);
    });

    it('WritesTheDates_WhenBetweenDatesReplacesAPreset', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialEntry="/admin/activity?time=7d">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Act
        await user.click(await screen.findByRole('button', { name: 'Time filtered, edit filter' }));
        await user.click(screen.getByRole('menuitemradio', { name: 'Between dates' }));
        await user.type(screen.getByLabelText('Time from'), '2026-09-01');
        await user.type(screen.getByLabelText('Time to'), '2026-09-30');
        await user.click(screen.getByRole('button', { name: 'Apply' }));

        // Assert
        expect(screen.getByTestId('activity-log-page__location-search')).toHaveTextContent(
            '?from=2026-09-01&to=2026-09-30',
        );
    });

    it.each([
        [true, {}, 'Magpie automatically purges events older than 90 days and job runs older than 30 days.'],
        [
            true,
            { auditRetentionDays: '60', jobExecutionRetentionDays: '14' },
            'Magpie automatically purges events older than 60 days and job runs older than 14 days.',
        ],
        [false, {}, null],
    ])('PurgeDialog_ShowsTheScheduleRetention_WhenTheScheduleIs %s %o', async (enabled, parameters, expectedLine) => {
        // Arrange
        const user = userEvent.setup();
        mockApiGet.mockImplementation((url: string) => {
            if (url.startsWith('/jobs/specs')) {
                return Promise.resolve([retentionSpec]);
            }
            if (url.startsWith('/schedule-triggers')) {
                return Promise.resolve([{ id: 'trigger-1', jobType: 'RetentionCleanup', enabled, parameters }]);
            }
            if (url.startsWith('/users')) {
                return Promise.resolve({ items: [], totalItems: 0 });
            }
            return Promise.resolve(auditHistoryPage);
        });
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Act
        await user.click((await screen.findAllByRole('button', { name: 'Purge activity log' }))[0]);

        // Assert
        expect(await screen.findByText('Delete activity older than 30 days.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Purge' })).toBeInTheDocument();
        await waitFor(() =>
            expect(screen.queryByText(/^Magpie automatically purges/)?.textContent ?? null).toBe(expectedLine),
        );
    });

    it('PurgeDialog_ReportsTheDeletedCounts_WhenThePurgeCompletes', async () => {
        // Arrange
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({ auditRecords: 21000, jobExecutions: 115 });
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Act
        await user.click((await screen.findAllByRole('button', { name: 'Purge activity log' }))[0]);
        await user.click(await screen.findByRole('button', { name: 'Purge' }));

        // Assert
        expect(await screen.findByText('Deleted 21,000 audit entries and 115 job runs.')).toBeInTheDocument();
        expect(mockApiPost).toHaveBeenCalledWith('/audit-history/purge?olderThanDays=30');
    });

    it('RequestsTheLastPage_WhenTheUrlPageIsPastTheEnd', async () => {
        // Arrange
        // Act
        render(
            <TestWrapper initialEntry="/admin/activity?page=9">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Assert
        await waitFor(() => expect(screen.getByTestId('activity-log-page__location-search')).toHaveTextContent(/^$/));
        expect(mockApiGet).toHaveBeenLastCalledWith(expect.stringMatching(/^\/audit-history\?.*pageNumber=1/));
    });

    it('ShowsMagpie_WhenTheSystemUserStartedTheJobRun', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );
        await screen.findByRole('gridcell', { name: 'Magpie' });

        // Act
        await user.click(screen.getByRole('button', { name: 'Filter by User' }));

        // Assert
        expect(screen.getAllByRole('menuitemradio').map((item) => item.textContent)).toEqual([
            'Any',
            'Magpie',
            'alice',
            'magpie-admin',
        ]);
    });

    it('ShowsTheTargetName_WhenTheEntryHasATarget', async () => {
        // Arrange
        // Act
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Assert
        expect(await screen.findByRole('link', { name: 'Weekend films' })).toHaveAttribute(
            'href',
            '/wishlists/wishlist-1',
        );
        expect(screen.getByRole('link', { name: 'Download Refresh' })).toHaveAttribute('href', '/admin/jobs?job=job-2');
        expect(screen.getByRole('link', { name: 'alice' })).toHaveAttribute('href', '/admin/users');
        expect(screen.getByText('Download Client')).not.toHaveAttribute('href');
    });

    it('RequestsTheCorrelatedActivity_WhenAnEventOpensTheRelatedActivity', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <TestWrapper initialEntry="/admin/activity">
                <ActivityLogPage />
            </TestWrapper>,
        );

        // Act
        await user.click(await screen.findByRole('button', { name: 'Create' }));
        await user.click(screen.getByRole('tab', { name: 'Related activity' }));

        // Assert
        await waitFor(() =>
            expect(mockApiGet).toHaveBeenCalledWith(
                expect.stringMatching(/^\/audit-history\?correlationId=correlation-1/),
            ),
        );
        expect(mockApiGet).toHaveBeenCalledWith(expect.stringMatching(/^\/jobs\/job-2\/execution-tree/));
    });
});
