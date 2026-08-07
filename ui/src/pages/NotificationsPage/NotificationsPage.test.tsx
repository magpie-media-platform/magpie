import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { AuthProvider, Role } from '@/auth';
import { SchemaDataType } from '@/hooks/useProviderSchema';
import { testEffectivePermissions } from '@/test/permissions';

import { NotificationsPage } from './NotificationsPage';

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

vi.mock('@/hooks/useUsers', () => ({
    useUsers: vi.fn(() => ({
        users: [{ id: 'user-1', username: 'alice' }],
        isLoading: false,
        error: null,
    })),
}));

import { apiGet, apiPatch, apiPost, apiDelete, ApiError } from '@/lib/api';

const mockRules = [
    {
        id: 'rule-1',
        name: 'Failures Webhook',
        ownerUserId: 'user-1',
        eventFilter: {
            eventName: { operator: 'regex', value: 'download\\.failed' },
            eventContext: {
                items: {
                    'content.type': { operator: 'Regex', value: '^kind-x$', negate: true },
                },
            },
        },
        providerType: 'webhook',
        enabled: true,
        createdAt: '2024-01-01T00:00:00Z',
        modifiedAt: null,
        createdByUserId: 'user-1',
        modifiedByUserId: null,
    },
    {
        id: 'rule-2',
        name: 'System Alerts',
        ownerUserId: null,
        eventFilter: { eventName: undefined, eventContext: undefined },
        providerType: 'slack',
        enabled: false,
        createdAt: '2024-01-02T00:00:00Z',
        modifiedAt: null,
        createdByUserId: null,
        modifiedByUserId: null,
    },
];

const notificationProviders = [
    {
        kind: 'notification',
        providerType: 'webhook',
        schema: [{ key: 'url', type: SchemaDataType.String, displayName: 'Webhook URL', required: true }],
    },
    {
        kind: 'notification',
        providerType: 'slack',
        schema: [],
    },
];

function setupApiMocks() {
    vi.mocked(apiGet).mockImplementation((url: string) => {
        if (url === '/notification-rules') {
            return Promise.resolve(mockRules);
        }
        if (url === '/providers') {
            return Promise.resolve(notificationProviders);
        }
        return Promise.resolve([]);
    });
    vi.mocked(apiPost).mockResolvedValue({});
    vi.mocked(apiPatch).mockResolvedValue(undefined);
    vi.mocked(apiDelete).mockResolvedValue(undefined);
}

beforeEach(() => {
    vi.clearAllMocks();
    setupApiMocks();
});

afterEach(() => {
    vi.restoreAllMocks();
});

function TestWrapper({ children, role = Role.Admin }: { children: ReactNode; role?: Role }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    const user = { id: 'user-1', username: 'alice', role };
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <AuthProvider initialUser={user} initialPermissions={testEffectivePermissions()}>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
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

function dataRows() {
    const table = screen.getByRole('grid');
    return within(table)
        .getAllByRole('row')
        .filter((r) => within(r).queryAllByRole('cell').length > 0);
}

describe('NotificationsPage', () => {
    it('renders the page header in a table', async () => {
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-page="notifications"]')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: /Notifications/i, level: 1 })).toBeInTheDocument();

        await waitFor(() => {
            expect(dataRows().length).toBe(2);
        });
    });

    it('renders the page subtitle as a capitalised, full-stopped sentence', () => {
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        expect(screen.getByTestId('page-header__description')).toHaveTextContent(
            'Magpie events can be sent through configured notification providers.',
        );
    });

    it('does not render enabled/disabled stat pills', async () => {
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(dataRows().length).toBe(2);
        });

        expect(document.querySelector('[aria-label="Notification summary"]')).not.toBeInTheDocument();
    });

    it('lists rules with names and a disabled badge on the disabled rule', async () => {
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Failures Webhook')).toBeInTheDocument();
        });
        expect(screen.getByText('System Alerts')).toBeInTheDocument();

        const disabledRow = screen.getByRole('grid').querySelector('[data-row-id="rule-2"]') as HTMLElement;
        expect(within(disabledRow).getByText('Disabled')).toBeInTheDocument();
    });

    it('keeps long unbroken rule names and patterns contained with the full values available', async () => {
        const longName = `NotificationRule${'UnbrokenNameSegment'.repeat(8)}`;
        const longPattern = `download\\.failed\\.${'unbrokenpatternsegment'.repeat(8)}`;
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.resolve([
                    {
                        ...mockRules[0],
                        id: 'rule-long',
                        name: longName,
                        eventFilter: { eventName: { operator: 'regex', value: longPattern }, eventContext: undefined },
                    },
                ]);
            }
            if (url === '/providers') {
                return Promise.resolve(notificationProviders.filter((provider) => provider.providerType === 'webhook'));
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByTitle(longName)).toHaveClass('notifications-table__name');
            expect(screen.getByTitle(longPattern)).toHaveClass('notifications-table__pattern');
        });
        expect(screen.getByTitle(longName).closest('[data-column="name"]')).toBeInTheDocument();
        expect(screen.getByTitle(longPattern).closest('[data-column="name"]')).toBeInTheDocument();
    });

    it('opens the form when Add Rule is clicked', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByText('Failures Webhook')).toBeInTheDocument();
        });

        await user.click(screen.getAllByRole('button', { name: /Add Rule/i })[0]);

        await waitFor(() => {
            expect(screen.getByText('Add Notification Rule')).toBeInTheDocument();
        });
    });

    it('creates a rule with wrapped context criteria', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.resolve([]);
            }
            if (url === '/providers') {
                return Promise.resolve(notificationProviders.filter((provider) => provider.providerType === 'webhook'));
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await user.click((await screen.findAllByRole('button', { name: /Add Rule/i }))[0]);
        await user.type(screen.getByLabelText(/^Name/), 'Kind-X content webhook');
        await user.type(screen.getByLabelText(/Webhook URL/i), 'https://example.test/webhook');
        await user.click(screen.getByRole('button', { name: 'Add Filter' }));
        await user.type(screen.getByRole('textbox', { name: 'Filter 1 key' }), 'content.type');
        await user.type(screen.getByRole('textbox', { name: 'Filter 1 pattern' }), '^kind-x$');
        await user.click(screen.getByRole('switch', { name: 'Filter 1 negate' }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(apiPost).toHaveBeenCalledWith('/notification-rules', {
                name: 'Kind-X content webhook',
                providerType: 'webhook',
                eventFilter: {
                    eventName: undefined,
                    eventContext: {
                        items: {
                            'content.type': { operator: 'regex', value: '^kind-x$', negate: true },
                        },
                    },
                },
                settings: { url: 'https://example.test/webhook' },
                enabled: true,
            });
        });
    });

    it('shows the provider catalog failure in the form and retries it', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.resolve([]);
            }
            if (url === '/providers') {
                return Promise.reject(new Error('Provider catalog unavailable'));
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await user.click((await screen.findAllByRole('button', { name: /Add Rule/i }))[0]);
        expect(await screen.findByText('Failed to load notification providers.')).toBeInTheDocument();
        expect(screen.getByLabelText('Provider')).toBeDisabled();

        await user.click(screen.getByRole('button', { name: 'Retry notification providers' }));

        await waitFor(() => {
            const calls = vi.mocked(apiGet).mock.calls.filter(([url]) => url === '/providers');
            expect(calls).toHaveLength(2);
        });
    });

    it('excludes non-notification providers from the new rule form', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.resolve([]);
            }
            if (url === '/providers') {
                return Promise.resolve([
                    notificationProviders[0],
                    {
                        kind: 'downloadClient',
                        providerType: 'DownloadClient A',
                        schema: [],
                    },
                ]);
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await user.click((await screen.findAllByRole('button', { name: /Add Rule/i }))[0]);

        expect(await screen.findByLabelText(/Webhook URL/i)).toBeInTheDocument();
        expect(screen.queryByText('DownloadClient A')).not.toBeInTheDocument();
    });

    it('surfaces provider validation issues from create failures in the form', async () => {
        const user = userEvent.setup();
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.resolve([]);
            }
            if (url === '/providers') {
                return Promise.resolve(notificationProviders.filter((provider) => provider.providerType === 'webhook'));
            }
            return Promise.resolve([]);
        });
        vi.mocked(apiPost).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.reject(
                    new ApiError(400, 'Validation failed', {
                        isValid: false,
                        issues: [{ key: 'url', message: 'URL is required.' }],
                    }),
                );
            }
            return Promise.resolve({});
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /Add Rule/i }).length).toBeGreaterThan(0);
        });
        await user.click(screen.getAllByRole('button', { name: /Add Rule/i })[0]);
        await screen.findByLabelText(/Webhook URL/i);
        expect(apiGet).toHaveBeenCalledWith('/providers');
        await user.type(screen.getByLabelText(/^Name/), 'Failures Webhook');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(await screen.findByText('URL is required.')).toBeInTheDocument();
        expect(screen.queryByText('Failed to create the notification rule.')).not.toBeInTheDocument();
    });
});

describe('NotificationsPage row actions', () => {
    it('exposes actions on every rule visible to a manager', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/notification-rules') {
                return Promise.resolve([
                    mockRules[0],
                    { ...mockRules[1], ownerUserId: 'admin-1', createdByUserId: 'admin-1' },
                ]);
            }
            if (url === '/providers') {
                return Promise.resolve(notificationProviders);
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper role={Role.Manager}>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelectorAll('[data-action="test"]').length).toBe(2);
        });
        expect(document.querySelectorAll('[data-action="toggle-enabled"]').length).toBe(2);
        expect(document.querySelectorAll('[data-action="edit"]').length).toBe(2);
        expect(document.querySelectorAll('[data-action="delete"]').length).toBe(2);
    });

    it('opens the delete confirm dialog from the row delete button', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^delete failures webhook$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^delete failures webhook$/i }));

        await waitFor(() => {
            expect(document.querySelector('[data-component="delete-confirm-dialog"]')).toBeInTheDocument();
        });
    });

    it('opens the edit form from the row edit button', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^edit failures webhook$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^edit failures webhook$/i }));

        await waitFor(() => {
            expect(screen.getByText('Edit Notification Rule')).toBeInTheDocument();
        });
        expect(screen.getByRole('textbox', { name: 'Filter 1 key' })).toHaveValue('content.type');
        expect(screen.getByRole('textbox', { name: 'Filter 1 pattern' })).toHaveValue('^kind-x$');
        expect(screen.getByRole('switch', { name: 'Filter 1 negate' })).toBeChecked();

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(apiPatch).toHaveBeenCalledWith(
                '/notification-rules/rule-1',
                expect.objectContaining({
                    name: { value: 'Failures Webhook' },
                    eventFilter: {
                        value: {
                            eventName: { operator: 'regex', value: 'download\\.failed' },
                            eventContext: {
                                items: {
                                    'content.type': { operator: 'Regex', value: '^kind-x$', negate: true },
                                },
                            },
                        },
                    },
                }),
            );
        });
    });

    it('surfaces the failure reason when a Send Test fails', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockImplementation((url: string) => {
            if (url.endsWith('/test')) {
                return Promise.reject(
                    new ApiError(502, 'Notification test delivery failed', {
                        type: 'https://httpstatuses.io/502',
                        title: 'Notification test delivery failed',
                        status: 502,
                        detail: 'Webhook delivery failed due to a transport error.',
                    }),
                );
            }
            return Promise.resolve({});
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /^test failures webhook$/i })).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^test failures webhook$/i }));

        const toast = await screen.findByText(/Webhook delivery failed due to a transport error\./);
        expect(toast).toBeInTheDocument();
        expect(toast.textContent).not.toContain('{');
        expect(toast.textContent).not.toContain('"type"');
        expect(toast.textContent).not.toContain('"status"');
        expect(toast.textContent).not.toContain('502');
    });
});

describe('NotificationsPage column-header filters', () => {
    it('exposes provider and enabled column-header filters', async () => {
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(dataRows().length).toBe(2);
        });

        expect(screen.getByRole('button', { name: /filter by provider/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by enabled/i })).toBeInTheDocument();
    });

    it('filters rows by enabled state', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(dataRows().length).toBe(2);
        });

        await user.click(screen.getByRole('button', { name: /filter by enabled/i }));
        await user.click(await screen.findByRole('menuitemradio', { name: 'Disabled' }));

        await waitFor(() => {
            expect(dataRows().length).toBe(1);
        });
        expect(screen.getByText('System Alerts')).toBeInTheDocument();
    });
});

describe('NotificationsPage empty state', () => {
    it('shows a single add affordance with no duplicate in the empty state', async () => {
        vi.mocked(apiGet).mockImplementation((url: string) => {
            if (url === '/providers') {
                return Promise.resolve(notificationProviders);
            }
            return Promise.resolve([]);
        });

        render(
            <TestWrapper>
                <NotificationsPage />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'No notification rules configured' })).toBeInTheDocument();
        });
        expect(screen.getByText('Create a rule to route matching events through a provider.')).toBeInTheDocument();

        expect(document.querySelector('[data-action="add-rule"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="add-rule-empty"]')).not.toBeInTheDocument();
    });
});
