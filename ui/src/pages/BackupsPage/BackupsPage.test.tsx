import { act, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { apiDelete, apiGet, apiPost } from '@/lib/api';

import { BackupsPage } from './BackupsPage';

function createMockBackups() {
    return [
        {
            id: 'backup-1',
            name: 'backup-1',
            createdAt: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
            sizeBytes: 15728640,
        },
        {
            id: '20240115_000000',
            name: 'Scheduled 20240115_000000',
            createdAt: new Date(Date.now() - 12 * 60 * 60 * 1000).toISOString(),
            sizeBytes: 15466496,
            kind: 'Scheduled',
        },
        {
            id: 'backup-3',
            name: 'backup-3',
            createdAt: new Date(Date.now() - 36 * 60 * 60 * 1000).toISOString(),
            sizeBytes: 15204352,
        },
        {
            id: 'backup-4',
            name: 'backup-4',
            createdAt: new Date(Date.now() - 60 * 60 * 60 * 1000).toISOString(),
            sizeBytes: 14942208,
        },
        {
            id: 'backup-5',
            name: 'backup-5',
            createdAt: new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString(),
            sizeBytes: 12582912,
        },
        {
            id: 'backup-6',
            name: 'backup-6',
            createdAt: new Date(Date.now() - 8 * 24 * 60 * 60 * 1000).toISOString(),
            sizeBytes: 12320768,
        },
    ];
}

let mockApiBackups = createMockBackups();

function defaultApiGet(url: string) {
    if (url === '/database') {
        return Promise.resolve({
            provider: 'SQLite',
            backupRestoreSupported: true,
            optimizeSupported: true,
        });
    }
    if (url === '/database/backups') {
        return Promise.resolve([...mockApiBackups]);
    }
    return Promise.resolve([]);
}

function defaultApiPost(url: string) {
    if (url === '/jobs/enqueue') {
        return Promise.resolve('execution-1');
    }
    return Promise.resolve(undefined);
}

function defaultApiDelete(url: string) {
    const match = url.match(/\/backups\/(.+)$/);
    if (match) {
        const id = decodeURIComponent(match[1]);
        mockApiBackups = mockApiBackups.filter((b) => b.id !== id);
    }
    return Promise.resolve(undefined);
}

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn().mockImplementation(defaultApiGet),
    apiPost: vi.fn().mockImplementation(defaultApiPost),
    apiDelete: vi.fn().mockImplementation(defaultApiDelete),
    isApiError: vi.fn(() => false),
}));

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

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

const originalTimeZone = process.env.TZ;

beforeEach(() => {
    vi.clearAllMocks();
    mockApiBackups = createMockBackups();
    vi.mocked(apiGet).mockImplementation(defaultApiGet);
    vi.mocked(apiPost).mockImplementation(defaultApiPost);
    vi.mocked(apiDelete).mockImplementation(defaultApiDelete);
});

afterEach(() => {
    if (originalTimeZone === undefined) {
        delete process.env.TZ;
    } else {
        process.env.TZ = originalTimeZone;
    }
});

describe('BackupsPage', () => {
    it('shows Last Optimized as Never when no optimization ran', async () => {
        // Arrange
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        expect(await screen.findByText('Last Optimized')).toBeInTheDocument();
        expect(screen.getByTestId('backups-page__last-optimized')).toHaveTextContent('Never');
        expect(screen.queryByText('Scheduled Optimization')).not.toBeInTheDocument();
    });

    it('LastOptimized_ShowsTheUtcTime_WhenTheLocalTimeZoneIsNotUtc', async () => {
        // Arrange
        process.env.TZ = 'America/Los_Angeles';
        vi.mocked(apiGet).mockImplementation((url: string) =>
            url === '/database'
                ? Promise.resolve({
                      provider: 'SQLite',
                      backupRestoreSupported: true,
                      optimizeSupported: true,
                      lastOptimizedAt: '2026-10-06T23:05:00Z',
                  })
                : defaultApiGet(url),
        );
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        expect(await screen.findByText('Oct 6, 2026, 11:05 PM')).toBeInTheDocument();
    });

    it('BackupRow_ShowsTheUtcDate_WhenTheLocalTimeZoneIsNotUtc', async () => {
        // Arrange
        process.env.TZ = 'America/Los_Angeles';
        vi.mocked(apiGet).mockImplementation((url: string) =>
            url === '/database/backups'
                ? Promise.resolve([
                      {
                          id: '20261007_002600',
                          name: 'Manual 20261007_002600',
                          createdAt: '2026-10-07T00:26:00Z',
                          sizeBytes: 15728640,
                      },
                  ])
                : defaultApiGet(url),
        );
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        const date = await screen.findByTestId('backups-page__item-date');
        expect(date).toHaveTextContent('Oct 7, 2026');
        expect(date).toHaveAttribute('title', 'Oct 7, 2026, 12:26 AM');
    });

    it.each(['Restore from Manual 20261007_002600', 'Delete Manual 20261007_002600'])(
        'BackupDialog_ShowsTheUtcCreatedTime_WhenTheLocalTimeZoneIsNotUtc (%s)',
        async (button) => {
            // Arrange
            process.env.TZ = 'America/Los_Angeles';
            const user = userEvent.setup();
            vi.mocked(apiGet).mockImplementation((url: string) =>
                url === '/database/backups'
                    ? Promise.resolve([
                          {
                              id: '20261007_002600',
                              name: 'Manual 20261007_002600',
                              createdAt: '2026-10-07T00:26:00Z',
                              sizeBytes: 15728640,
                          },
                      ])
                    : defaultApiGet(url),
            );
            render(<BackupsPage />, { wrapper: createWrapper() });
            // Act
            await user.click(await screen.findByRole('button', { name: button }));
            // Assert
            expect(await screen.findByRole('dialog')).toHaveTextContent('Oct 7, 2026, 12:26 AM');
        },
    );

    it('shows no download action on backup rows', async () => {
        // Arrange
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        expect(await screen.findByRole('button', { name: 'Restore from backup-1' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /download/i })).not.toBeInTheDocument();
    });

    it('RestoreAndDelete_AreDisabledWithTheReason_WhenTheLoginCannotRestoreOrDelete', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((url: string) =>
            url === '/database'
                ? Promise.resolve({
                      provider: 'MSSQL',
                      backupRestoreSupported: true,
                      optimizeSupported: true,
                      restoreDeniedReason: 'Restore needs the database owner, dbcreator or sysadmin role on SQL Server',
                      deleteDeniedReason: 'Delete needs the sysadmin role on SQL Server',
                  })
                : defaultApiGet(url),
        );
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        const restoreButton = await screen.findByRole('button', { name: 'Restore from backup-1' });
        const deleteButton = screen.getByRole('button', { name: 'Delete backup-1' });
        expect(restoreButton).toBeDisabled();
        expect(restoreButton.parentElement).toHaveAccessibleDescription(
            'Restore needs the database owner, dbcreator or sysadmin role on SQL Server',
        );
        expect(deleteButton).toBeDisabled();
        expect(deleteButton.parentElement).toHaveAccessibleDescription('Delete needs the sysadmin role on SQL Server');
        expect(screen.getAllByRole('button', { name: 'Create Backup' })[0]).toBeEnabled();
    });

    it('CreateBackup_IsDisabledWithTheReason_WhenTheProcessCannotCreateABackup', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((url: string) =>
            url === '/database'
                ? Promise.resolve({
                      provider: 'SQLite',
                      backupRestoreSupported: true,
                      optimizeSupported: true,
                      backupDeniedReason: 'Create needs write access to the backup directory',
                  })
                : defaultApiGet(url),
        );
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        expect(await screen.findByRole('button', { name: 'Restore from backup-1' })).toBeEnabled();
        const createButton = screen.getAllByRole('button', { name: 'Create Backup' })[0];
        expect(createButton).toBeDisabled();
        expect(createButton.parentElement).toHaveAccessibleDescription(
            'Create needs write access to the backup directory',
        );
    });

    it('EmptyState_ShowsTheCreateReason_WhenTheProcessCannotCreateABackup', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((url: string) =>
            Promise.resolve(
                url === '/database'
                    ? {
                          provider: 'SQLite',
                          backupRestoreSupported: true,
                          optimizeSupported: true,
                          backupDeniedReason: 'Create needs write access to the backup directory',
                      }
                    : [],
            ),
        );
        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });
        // Assert
        expect(await screen.findByText('No backups available')).toBeInTheDocument();
        expect(screen.getByRole('list', { name: 'Backups' })).toHaveTextContent(
            'Create needs write access to the backup directory',
        );
        expect(screen.queryByText('Create a backup to protect your data')).not.toBeInTheDocument();
    });

    it('renders with correct data-page attribute', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-page="backups"]')).toBeInTheDocument();
        });
    });

    it('renders the page title', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Database', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders the page icon as decorative', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const icon = screen.getByTestId('backups-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('renders a Create Backup button', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const createButtons = screen.getAllByRole('button', { name: /create backup/i });
            expect(createButtons.length).toBeGreaterThanOrEqual(1);
        });
    });

    it('renders total size summary', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Total Size')).toBeInTheDocument();

            const summaryValue = screen.getByTestId('backups-page__summary-value');
            expect(summaryValue).toBeInTheDocument();
            expect(summaryValue?.textContent).toMatch(/\d+(\.\d+)?\s*MB/);
        });
    });

    it('renders unsupported state when backups are unavailable for the provider', async () => {
        const { apiGet } = await import('@/lib/api');
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database') {
                return Promise.resolve({
                    provider: 'PostgreSQL',
                    backupRestoreSupported: false,
                    optimizeSupported: false,
                    unsupportedReason: 'Database backup and restore are not implemented for PostgreSQL deployments.',
                });
            }
            return Promise.resolve([]);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Database backups unavailable')).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /create backup/i })).not.toBeInTheDocument();
            expect(screen.queryByText('Schedule Settings')).not.toBeInTheDocument();
        });
    });

    it('does not fetch or render legacy automatic backup settings', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /backups/i })).toBeInTheDocument();
        });

        expect(apiGet).not.toHaveBeenCalledWith('/settings/groups/system/backups');
        expect(screen.queryByText('Schedule Settings')).not.toBeInTheDocument();
        expect(screen.queryByText('Automatic Backups')).not.toBeInTheDocument();
    });
});

describe('BackupsPage list items', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('renders backup items in the list', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /backups/i });
            const items = within(list).getAllByRole('listitem');

            expect(items.length).toBe(6);
        });
    });

    it('renders backup names', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('backup-1')).toBeInTheDocument();
            expect(screen.getByText('backup-5')).toBeInTheDocument();
        });
    });

    it('renders backup type badges', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const manualBadges = screen.getAllByText('Manual');
            expect(manualBadges.length).toBeGreaterThan(0);

            const scheduledBadges = screen.getAllByText('Scheduled');
            expect(scheduledBadges.length).toBeGreaterThan(0);
        });
    });

    it('renders backup sizes', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const items = screen.getAllByTestId('backups-page__item-size');
            expect(items.length).toBe(6);
            items.forEach((item) => {
                expect(item.textContent).toMatch(/\d+(\.\d+)?\s*(B|KB|MB|GB)/);
            });
        });
    });

    it('renders backup dates', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const dateElements = screen.getAllByTestId('backups-page__item-date');
            expect(dateElements.length).toBe(6);
        });
    });

    it('applies data-backup-id to items', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const items = document.querySelectorAll('[data-backup-id]');
            expect(items.length).toBe(6);
            expect(items[0]).toHaveAttribute('data-backup-id', 'backup-1');
        });
    });
});

describe('BackupsPage actions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('renders restore button for each backup', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const restoreButtons = document.querySelectorAll('[data-action="restore"]');
            expect(restoreButtons.length).toBe(6);
        });
    });

    it('renders delete button for each backup', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(6);
        });
    });

    it('restore button has correct aria-label', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const restoreButton = screen.getByRole('button', { name: /restore from backup-1/i });
            expect(restoreButton).toHaveAttribute('data-action', 'restore');
            expect(restoreButton).toHaveAttribute('data-target', 'backup-1');
        });
    });

    it('delete button has correct aria-label', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const deleteButton = screen.getByRole('button', { name: /delete backup-1/i });
            expect(deleteButton).toHaveAttribute('data-action', 'delete');
            expect(deleteButton).toHaveAttribute('data-target', 'backup-1');
        });
    });

    it('opens restore dialog when restore button is clicked', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /restore from backup-1/i })).toBeInTheDocument();
        });

        const restoreButton = screen.getByRole('button', { name: /restore from backup-1/i });
        await user.click(restoreButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
            expect(screen.getByText('Restore Backup')).toBeInTheDocument();
        });
    });

    it('opens delete dialog when delete button is clicked', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /delete backup-1/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /delete backup-1/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
            expect(screen.getByText('Delete Backup')).toBeInTheDocument();
        });
    });

    it('queues a backup and acknowledges it when the create button is clicked', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /backups/i });
            expect(within(list).getAllByRole('listitem')).toHaveLength(6);
        });

        const createButton = screen.getAllByRole('button', { name: /create backup/i })[0];
        await user.click(createButton);

        expect(await screen.findByText('Backup Queued')).toBeInTheDocument();
        expect(apiPost).toHaveBeenCalledWith('/jobs/enqueue', {
            jobType: 'DatabaseBackup',
            parameters: undefined,
        });
        expect(within(screen.getByRole('list', { name: /backups/i })).getAllByRole('listitem')).toHaveLength(6);
    });

    it('acknowledges an accepted backup enqueue without an execution id and refreshes the list', async () => {
        const user = userEvent.setup();
        let resolveEnqueue: (() => void) | undefined;
        const enqueuePromise = new Promise<void>((resolve) => {
            resolveEnqueue = resolve;
        });
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database') {
                return Promise.resolve({
                    provider: 'SQLite',
                    backupRestoreSupported: true,
                    optimizeSupported: true,
                });
            }
            if (url === '/database/backups') {
                return Promise.resolve([]);
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPost).mockImplementation((url: string) => {
            return url === '/jobs/enqueue' ? enqueuePromise : Promise.resolve(undefined);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: 'Create Backup' }).length).toBeGreaterThan(0);
        });

        await user.click(screen.getAllByRole('button', { name: 'Create Backup' })[0]);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Queueing...' })).toBeDisabled();
        });
        expect(apiPost).toHaveBeenCalledWith('/jobs/enqueue', {
            jobType: 'DatabaseBackup',
            parameters: undefined,
        });

        await act(async () => {
            resolveEnqueue?.();
        });

        expect(await screen.findByText('Backup Queued')).toBeInTheDocument();
        await waitFor(() => {
            const backupRequests = vi.mocked(apiGet).mock.calls.filter(([url]) => url === '/database/backups');
            expect(backupRequests).toHaveLength(2);
        });
    });
});

describe('BackupsPage restore dialog', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('shows backup details in restore dialog', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /restore from backup-1/i })).toBeInTheDocument();
        });

        const restoreButton = screen.getByRole('button', { name: /restore from backup-1/i });
        await user.click(restoreButton);

        await waitFor(() => {
            const dialog = screen.getByRole('dialog');
            expect(dialog).toBeInTheDocument();

            expect(within(dialog).getByText('backup-1')).toBeInTheDocument();

            expect(dialog).toHaveTextContent(/MB/);
        });
    });

    it('can cancel restore', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /restore from backup-1/i })).toBeInTheDocument();
        });

        const restoreButton = screen.getByRole('button', { name: /restore from backup-1/i });
        await user.click(restoreButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });
});

describe('BackupsPage delete dialog', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('shows backup name in delete dialog', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /delete backup-1/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /delete backup-1/i });
        await user.click(deleteButton);

        await waitFor(() => {
            const dialog = screen.getByRole('dialog');
            expect(within(dialog).getByText('backup-1')).toBeInTheDocument();
        });
    });

    it('can cancel delete', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /delete backup-1/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /delete backup-1/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    });

    it('deletes backup when confirmed', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-backup-id]').length).toBe(6);
        });

        const deleteButton = screen.getByRole('button', { name: /delete backup-1/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const confirmButton = within(screen.getByRole('dialog')).getByRole('button', { name: /delete/i });
        await user.click(confirmButton);

        await waitFor(
            () => {
                expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
            },
            { timeout: 2000 },
        );

        await waitFor(() => {
            expect(document.querySelectorAll('[data-backup-id]').length).toBe(5);
        });
    }, 5000);

    it('submits delete once and disables confirm while deletion is pending', async () => {
        const user = userEvent.setup();
        let resolveDelete: (() => void) | undefined;
        vi.mocked(apiDelete).mockImplementation((url: string) => {
            void defaultApiDelete(url);

            return new Promise<void>((resolve) => {
                resolveDelete = resolve;
            });
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /delete backup-1/i })).toBeInTheDocument();
        });

        const deleteButton = screen.getByRole('button', { name: /delete backup-1/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const confirmButton = within(screen.getByRole('dialog')).getByRole('button', { name: /delete/i });
        await user.dblClick(confirmButton);

        expect(apiDelete).toHaveBeenCalledTimes(1);
        expect(apiDelete).toHaveBeenCalledWith('/database/backups/backup-1');

        await waitFor(() => {
            expect(within(screen.getByRole('dialog')).getByRole('button', { name: /deleting/i })).toBeDisabled();
        });

        resolveDelete?.();

        await waitFor(() => {
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        });
    }, 5000);

    it('shows empty state after deleting the last backup', async () => {
        mockApiBackups = [
            {
                id: 'only-backup',
                name: 'only-backup',
                createdAt: new Date().toISOString(),
                sizeBytes: 1024,
            },
        ];

        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelectorAll('[data-backup-id]').length).toBe(1);
        });

        const deleteButton = screen.getByRole('button', { name: /delete only-backup/i });
        await user.click(deleteButton);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const confirmButton = within(screen.getByRole('dialog')).getByRole('button', { name: /delete/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(screen.getByText('No backups available')).toBeInTheDocument();
        });

        expect(document.querySelectorAll('[data-backup-id]').length).toBe(0);
    }, 5000);
});

describe('BackupsPage accessibility', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('list has accessible label', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('list', { name: /backups/i })).toBeInTheDocument();
        });
    });

    it('action buttons have aria-labels', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const actionButtons = screen.getAllByTestId('backups-page__action-btn');
            expect(actionButtons.length).toBeGreaterThan(0);
            actionButtons.forEach((btn) => {
                expect(btn).toHaveAttribute('aria-label');
            });
        });
    });

    it('dialogs have accessible structure', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /restore from backup-1/i })).toBeInTheDocument();
        });

        const restoreButton = screen.getByRole('button', { name: /restore from backup-1/i });
        await user.click(restoreButton);

        await waitFor(() => {
            const dialog = screen.getByRole('dialog');
            expect(dialog).toBeInTheDocument();

            expect(within(dialog).getByText('Restore Backup')).toBeInTheDocument();
        });
    });
});

describe('BackupsPage layout', () => {
    it('Header_IsInThePageContent_WhenBackupsAreUnavailable', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database') {
                return Promise.resolve({
                    provider: 'SqlServer',
                    backupRestoreSupported: false,
                    optimizeSupported: false,
                    unsupportedReason: 'Database backup and restore are not implemented for SqlServer deployments.',
                });
            }
            return Promise.resolve([]);
        });

        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });

        // Assert
        await screen.findByText('Database backups unavailable');
        expect(
            screen.getByRole('heading', { name: 'Database' }).closest('.backups-page__pull-to-refresh'),
        ).not.toBeNull();
    });

    it('PullToRefresh_IsDisabled_WhenBackupsAreUnavailable', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database') {
                return Promise.resolve({
                    provider: 'SqlServer',
                    backupRestoreSupported: false,
                    optimizeSupported: false,
                    unsupportedReason: 'Database backup and restore are not implemented for SqlServer deployments.',
                });
            }
            return Promise.resolve([]);
        });

        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });

        // Assert
        await screen.findByText('Database backups unavailable');
        expect(
            screen.getByRole('heading', { name: 'Database' }).closest('.backups-page__pull-to-refresh'),
        ).toHaveAttribute('data-disabled', 'true');
    });

    it('Header_IsInThePageContent_WhenTheBackupsFailToLoad', async () => {
        // Arrange
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database/backups') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        // Act
        render(<BackupsPage />, { wrapper: createWrapper() });

        // Assert
        await screen.findByText('Failed to load backups');
        expect(
            screen.getByRole('heading', { name: 'Database' }).closest('.backups-page__pull-to-refresh'),
        ).not.toBeNull();
    });
});

describe('BackupsPage error state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('shows error state when API fails', async () => {
        const { apiGet } = await import('@/lib/api');
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database/backups') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Failed to load backups')).toBeInTheDocument();
        });
    });

    it('shows error message from API', async () => {
        const { apiGet } = await import('@/lib/api');
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database/backups') {
                return Promise.reject(new Error('Server unavailable'));
            }
            return Promise.resolve([]);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Server unavailable')).toBeInTheDocument();
        });
    });

    it('shows retry button in error state', async () => {
        const { apiGet } = await import('@/lib/api');
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database/backups') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
        });
    });

    it('still shows page title in error state', async () => {
        const { apiGet } = await import('@/lib/api');
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/database/backups') {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve([]);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Database', level: 1 })).toBeInTheDocument();
        });
    });
});
