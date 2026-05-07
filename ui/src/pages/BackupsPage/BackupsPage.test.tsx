import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

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

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn().mockImplementation((url: string) => {
        if (url === '/backups') {
            return Promise.resolve([...mockApiBackups]);
        }
        if (url === '/settings/groups/system/backups') {
            return Promise.resolve({
                id: 'system-backups',
                name: 'backups',
                extensionId: null,
                settings: [
                    { id: '1', settingsGroupId: 'system-backups', key: 'enabled', value: 'false' },
                    { id: '2', settingsGroupId: 'system-backups', key: 'interval', value: 'daily' },
                    { id: '3', settingsGroupId: 'system-backups', key: 'retention', value: '7' },
                ],
            });
        }
        return Promise.resolve([]);
    }),
    apiPost: vi.fn().mockImplementation((url: string) => {
        if (url === '/backups') {
            const newBackup = {
                id: 'new-backup',
                name: 'new-backup',
                createdAt: new Date().toISOString(),
                sizeBytes: 15728640,
            };
            mockApiBackups = [newBackup, ...mockApiBackups];
            return Promise.resolve({ id: newBackup.id });
        }
        return Promise.resolve(undefined);
    }),
    apiPatch: vi.fn().mockResolvedValue(undefined),
    apiDelete: vi.fn().mockImplementation((url: string) => {
        const match = url.match(/\/backups\/(.+)$/);
        if (match) {
            const id = decodeURIComponent(match[1]);
            mockApiBackups = mockApiBackups.filter((b) => b.id !== id);
        }
        return Promise.resolve(undefined);
    }),
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

describe('BackupsPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
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
            expect(screen.getByRole('heading', { name: 'Backups', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders the backup count', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const count = screen.getByTestId('backups-page__count');
            expect(count).toBeInTheDocument();
            expect(count).toHaveTextContent('6');
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
            expect(screen.getByText('backup-1.db')).toBeInTheDocument();
            expect(screen.getByText('backup-5.db')).toBeInTheDocument();
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

    it('creates a new backup when create button is clicked', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByTestId('backups-page__count')).toHaveTextContent('6');
        });

        const createButton = screen.getAllByRole('button', { name: /create backup/i })[0];
        await user.click(createButton);

        await waitFor(
            () => {
                const count = screen.getByTestId('backups-page__count');
                expect(count).toHaveTextContent('7');
            },
            { timeout: 3000 },
        );
    }, 5000);
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

            expect(within(dialog).getByText('backup-1.db')).toBeInTheDocument();

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
            expect(within(dialog).getByText('backup-1.db')).toBeInTheDocument();
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

describe('BackupsPage settings', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiBackups = createMockBackups();
    });

    it('renders schedule settings section', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Schedule Settings')).toBeInTheDocument();
        });
    });

    it('renders automatic backups toggle', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const toggle = document.querySelector('[data-setting="enabled"]');
            expect(toggle).toBeInTheDocument();
            expect(screen.getByText('Automatic Backups')).toBeInTheDocument();
        });
    });

    it('renders backup interval selector', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const intervalSelect = document.querySelector('[data-setting="interval"]');
            expect(intervalSelect).toBeInTheDocument();
            expect(screen.getByText('Backup Interval')).toBeInTheDocument();
        });
    });

    it('renders retention policy input', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const retentionInput = document.querySelector('[data-setting="retention"]');
            expect(retentionInput).toBeInTheDocument();
            expect(screen.getByText('Keep Last')).toBeInTheDocument();
        });
    });

    it('renders save settings button', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const saveButton = screen.getByRole('button', { name: /save settings/i });
            expect(saveButton).toBeInTheDocument();

            expect(saveButton).toBeDisabled();
        });
    });

    it('enables save button when settings are changed', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const saveButton = screen.getByRole('button', { name: /save settings/i });
            expect(saveButton).toBeDisabled();
        });

        const toggle = document.querySelector('[data-setting="enabled"]') as HTMLButtonElement;
        await user.click(toggle);

        const retentionInput = document.querySelector('[data-setting="retention"]') as HTMLInputElement;
        await user.clear(retentionInput);
        await user.type(retentionInput, '14');

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /save settings/i })).not.toBeDisabled();
        });
    });

    it('disables interval and retention when automatic backups are disabled', async () => {
        const user = userEvent.setup();
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-setting="enabled"]')).toBeInTheDocument();
        });

        const toggle = document.querySelector('[data-setting="enabled"]') as HTMLButtonElement;

        await user.click(toggle);

        await waitFor(() => {
            const intervalSelect = document.querySelector('[data-setting="interval"]');
            const retentionInput = document.querySelector('[data-setting="retention"]');
            expect(intervalSelect).not.toBeDisabled();
            expect(retentionInput).not.toBeDisabled();
        });

        await user.click(toggle);

        await waitFor(() => {
            const intervalSelectAfter = document.querySelector('[data-setting="interval"]');
            const retentionInputAfter = document.querySelector('[data-setting="retention"]');
            expect(intervalSelectAfter).toBeDisabled();
            expect(retentionInputAfter).toBeDisabled();
        });
    });

    it('saves settings when save button is clicked', async () => {
        const user = userEvent.setup();

        let resolvePatch: () => void;
        const patchPromise = new Promise<void>((resolve) => {
            resolvePatch = resolve;
        });

        const { apiPatch } = await import('@/lib/api');
        vi.mocked(apiPatch).mockReturnValue(patchPromise);

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-setting="retention"]')).toBeInTheDocument();
        });

        const toggle = document.querySelector('[data-setting="enabled"]') as HTMLButtonElement;
        await user.click(toggle);

        const retentionInput = document.querySelector('[data-setting="retention"]') as HTMLInputElement;
        await user.clear(retentionInput);
        await user.type(retentionInput, '14');

        const saveButton = screen.getByRole('button', { name: /save settings/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByText(/saving/i)).toBeInTheDocument();
        });

        resolvePatch!();

        await waitFor(
            () => {
                expect(screen.getByRole('button', { name: /save settings/i })).toBeDisabled();
            },
            { timeout: 3000 },
        );
    }, 5000);

    it('has data-component attribute on settings section', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const settingsSection = document.querySelector('[data-component="backup-settings"]');
            expect(settingsSection).toBeInTheDocument();
        });
    });

    it('settings section has accessible heading', async () => {
        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const settingsSection = screen.getByRole('region', { name: /schedule settings/i });
            expect(settingsSection).toHaveAttribute('aria-labelledby', 'backup-settings-title');
            expect(screen.getByRole('heading', { name: 'Schedule Settings', level: 2 })).toBeInTheDocument();
        });
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
            if (url === '/backups') {
                return Promise.reject(new Error('Network error'));
            }
            if (url === '/settings/groups/system/backups') {
                return Promise.resolve({
                    id: 'system-backups',
                    name: 'backups',
                    extensionId: null,
                    settings: [],
                });
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
            if (url === '/backups') {
                return Promise.reject(new Error('Server unavailable'));
            }
            if (url === '/settings/groups/system/backups') {
                return Promise.resolve({
                    id: 'system-backups',
                    name: 'backups',
                    extensionId: null,
                    settings: [],
                });
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
            if (url === '/backups') {
                return Promise.reject(new Error('Network error'));
            }
            if (url === '/settings/groups/system/backups') {
                return Promise.resolve({
                    id: 'system-backups',
                    name: 'backups',
                    extensionId: null,
                    settings: [],
                });
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
            if (url === '/backups') {
                return Promise.reject(new Error('Network error'));
            }
            if (url === '/settings/groups/system/backups') {
                return Promise.resolve({
                    id: 'system-backups',
                    name: 'backups',
                    extensionId: null,
                    settings: [],
                });
            }
            return Promise.resolve([]);
        });

        render(<BackupsPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Backups', level: 1 })).toBeInTheDocument();
        });
    });
});
