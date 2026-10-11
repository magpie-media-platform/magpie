import { act, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { settingsQueryKeys } from '@/hooks/useSettings';

import { GeneralPage } from './GeneralPage';

const mockApiGet = vi.fn();
const mockApiPatch = vi.fn();

vi.mock('@/hooks/useThemeCatalog', () => ({
    useThemeCatalog: () => ({ isPending: true }),
    themeCatalogQueryKey: ['themes'],
}));

vi.mock('@/lib/api', () => ({
    apiGet: (...args: unknown[]) => mockApiGet(...args),
    apiPatch: (...args: unknown[]) => mockApiPatch(...args),
}));

const mockSettingsGroup = {
    id: 'sg-1',
    name: 'general',
    extensionId: null,
    createdAt: '2025-01-01T00:00:00Z',
    settings: [
        {
            id: 's-1',
            settingsGroupId: 'sg-1',
            key: 'instanceName',
            value: 'My Magpie',
            createdAt: '2025-01-01T00:00:00Z',
        },
        {
            id: 's-2',
            settingsGroupId: 'sg-1',
            key: 'baseUrl',
            value: 'https://magpie.local',
            createdAt: '2025-01-01T00:00:00Z',
        },
    ],
};

function createWrapper(queryClient?: QueryClient) {
    const client =
        queryClient ??
        new QueryClient({
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
            <QueryClientProvider client={client}>
                <MemoryRouter>
                    <ToastProvider>
                        <ToastStateProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

function createTestQueryClient() {
    return new QueryClient({
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
}

describe('GeneralPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue([mockSettingsGroup]);
        mockApiPatch.mockResolvedValue(undefined);
    });

    it('renders with correct data-page attribute', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-page="general"]')).toBeInTheDocument();
        });
    });

    it('renders the page title', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Advanced', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders the page icon as decorative', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const icon = screen.getByTestId('general-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('renders the Instance Settings section heading', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Instance Settings', level: 2 })).toBeInTheDocument();
        });
    });

    it('has data-component attribute on settings section', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-component="general-settings"]')).toBeInTheDocument();
        });
    });

    it('settings section has accessible heading', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const section = screen.getByRole('region', { name: /instance settings/i });
            expect(section).toHaveAttribute('aria-labelledby', 'general-settings-title');
        });
    });
});

describe('GeneralPage form fields', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue([mockSettingsGroup]);
        mockApiPatch.mockResolvedValue(undefined);
    });

    it('renders Instance Name field with loaded value', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toBeInTheDocument();
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });
    });

    it('renders Base URL field with loaded value', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Base URL')).toBeInTheDocument();
            expect(screen.getByLabelText('Base URL')).toHaveValue('https://magpie.local');
        });
    });

    it('renders Base URL field with placeholder', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByPlaceholderText('https://magpie.example.com')).toBeInTheDocument();
        });
    });

    it('applies data-setting attributes to inputs', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-setting="instanceName"]')).toBeInTheDocument();
            expect(document.querySelector('[data-setting="baseUrl"]')).toBeInTheDocument();
        });
    });

    it('fetches settings from the API on mount', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/settings/groups');
        });
    });
});

describe('GeneralPage save', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue([mockSettingsGroup]);
        mockApiPatch.mockResolvedValue(undefined);
    });

    it('renders save button initially disabled', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const saveButton = document.querySelector('[data-action="save-settings"]') as HTMLButtonElement;
            expect(saveButton).toBeInTheDocument();
            expect(saveButton).toBeDisabled();
        });
    });

    it('enables save button when a field is changed', async () => {
        const user = userEvent.setup();
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        const input = screen.getByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'New Name');

        await waitFor(() => {
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();
        });
    });

    it('returns to clean when an edit is reverted to the baseline', async () => {
        const user = userEvent.setup();
        render(<GeneralPage />, { wrapper: createWrapper() });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Temporary Name');
        expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();

        await user.clear(input);
        await user.type(input, 'My Magpie');

        expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).toBeDisabled();
    });

    it('calls PATCH endpoint when save is clicked', async () => {
        const user = userEvent.setup();
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        const input = screen.getByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Updated Name');

        const saveButton = document.querySelector('[data-action="save-settings"]') as HTMLButtonElement;
        await user.click(saveButton);

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/general', {
                settings: {
                    update: {
                        instanceName: 'Updated Name',
                        baseUrl: 'https://magpie.local',
                    },
                },
            });
        });
    });

    it('names the saved instance in the success toast', async () => {
        const user = userEvent.setup();
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        const input = screen.getByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Updated Name');
        await user.click(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement);

        expect(await screen.findByText('Updated Name updated the general settings.')).toBeInTheDocument();
    });

    it('shows error toast when save fails', async () => {
        mockApiPatch.mockRejectedValue(new Error('Server error'));
        const user = userEvent.setup();
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        const input = screen.getByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Failing Name');

        const saveButton = document.querySelector('[data-action="save-settings"]') as HTMLButtonElement;
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByText('Save Failed')).toBeInTheDocument();
            expect(screen.getByText('Server error')).toBeInTheDocument();
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();
        });
    });

    it('disables save button after successful save', async () => {
        const user = userEvent.setup();
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        const input = screen.getByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'New');

        const saveButton = document.querySelector('[data-action="save-settings"]') as HTMLButtonElement;
        await user.click(saveButton);

        await waitFor(() => {
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).toBeDisabled();
        });
    });

    it('processes a completed refetch even when it returns the previously observed settings', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Submitted Name');
        await user.click(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement);
        await screen.findByText('Settings Saved');

        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [mockSettingsGroup]);
        });

        await waitFor(() => {
            expect(input).toHaveValue('My Magpie');
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).toBeDisabled();
        });
    });

    it('keeps dirty edits when refetched settings change', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();

        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        const input = screen.getByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Draft Name');

        await waitFor(() => {
            expect(input).toHaveValue('Draft Name');
        });

        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    ...mockSettingsGroup,
                    settings: mockSettingsGroup.settings.map((setting) =>
                        setting.key === 'instanceName' ? { ...setting, value: 'Server Name' } : setting,
                    ),
                },
            ]);
        });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('Draft Name');
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();
            expect(screen.getByText('Settings changed elsewhere')).toBeInTheDocument();
        });
    });

    it('adopts the latest server values when a stale draft is reverted', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();

        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Draft Name');

        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    ...mockSettingsGroup,
                    settings: mockSettingsGroup.settings.map((setting) =>
                        setting.key === 'instanceName' ? { ...setting, value: 'Server Name' } : setting,
                    ),
                },
            ]);
        });
        await screen.findByText('Settings changed elsewhere');

        await user.clear(input);
        await user.type(input, 'My Magpie');

        await waitFor(() => {
            expect(input).toHaveValue('Server Name');
            expect(screen.queryByText('Settings changed elsewhere')).not.toBeInTheDocument();
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).toBeDisabled();
        });
    });

    it('refreshes a clean form when settings change', async () => {
        const queryClient = createTestQueryClient();

        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('My Magpie');
        });

        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    ...mockSettingsGroup,
                    settings: mockSettingsGroup.settings.map((setting) =>
                        setting.key === 'instanceName' ? { ...setting, value: 'Server Name' } : setting,
                    ),
                },
            ]);
        });

        await waitFor(() => {
            expect(screen.getByLabelText('Instance Name')).toHaveValue('Server Name');
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).toBeDisabled();
            expect(screen.queryByText('Settings changed elsewhere')).not.toBeInTheDocument();
        });
    });

    it('requires confirmation before overwriting refetched settings', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        mockApiPatch.mockReturnValue(new Promise(() => {}));

        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Draft Name');

        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    ...mockSettingsGroup,
                    settings: mockSettingsGroup.settings.map((setting) =>
                        setting.key === 'instanceName' ? { ...setting, value: 'Server Name' } : setting,
                    ),
                },
            ]);
        });

        await user.click(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement);

        expect(screen.getByRole('dialog', { name: 'Save over newer changes?' })).toBeInTheDocument();
        expect(mockApiPatch).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: 'Save anyway' }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledTimes(1);
        });
    });

    it('keeps edits made while a save is in flight dirty after it succeeds', async () => {
        const user = userEvent.setup();
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );

        render(<GeneralPage />, { wrapper: createWrapper() });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Submitted Name');
        await user.click(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement);

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledTimes(1);
        });

        await user.clear(input);
        await user.type(input, 'Newer Draft');

        act(() => {
            resolvePatch?.();
        });

        await waitFor(() => {
            expect(input).toHaveValue('Newer Draft');
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();
        });
    });

    it('retains a concurrent external-change warning after a save when newer local edits remain', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );

        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Submitted Name');
        await user.click(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement);
        await waitFor(() => expect(mockApiPatch).toHaveBeenCalledTimes(1));

        await user.clear(input);
        await user.type(input, 'Newer Draft');
        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    ...mockSettingsGroup,
                    settings: mockSettingsGroup.settings.map((setting) =>
                        setting.key === 'instanceName' ? { ...setting, value: 'Other User Change' } : setting,
                    ),
                },
            ]);
        });
        await screen.findByText('Settings changed elsewhere');

        mockApiGet.mockResolvedValue([
            {
                ...mockSettingsGroup,
                settings: mockSettingsGroup.settings.map((setting) =>
                    setting.key === 'instanceName' ? { ...setting, value: 'Other User Change' } : setting,
                ),
            },
        ]);
        act(() => {
            resolvePatch?.();
        });

        await waitFor(() => {
            expect(input).toHaveValue('Newer Draft');
            expect(screen.getByText('Settings changed elsewhere')).toBeInTheDocument();
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();
        });
    });

    it('clears an in-flight notification once refetched settings match the submitted snapshot', async () => {
        const user = userEvent.setup();
        const queryClient = createTestQueryClient();
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );

        render(<GeneralPage />, { wrapper: createWrapper(queryClient) });

        const input = await screen.findByLabelText('Instance Name');
        await user.clear(input);
        await user.type(input, 'Submitted Name');
        await user.click(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement);
        await waitFor(() => expect(mockApiPatch).toHaveBeenCalledTimes(1));

        await user.clear(input);
        await user.type(input, 'Newer Draft');
        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    ...mockSettingsGroup,
                    settings: mockSettingsGroup.settings.map((setting) =>
                        setting.key === 'instanceName' ? { ...setting, value: 'Submitted Name' } : setting,
                    ),
                },
            ]);
        });
        await screen.findByText('Settings changed elsewhere');

        mockApiGet.mockResolvedValue([
            {
                ...mockSettingsGroup,
                settings: mockSettingsGroup.settings.map((setting) =>
                    setting.key === 'instanceName' ? { ...setting, value: 'Submitted Name' } : setting,
                ),
            },
        ]);
        act(() => {
            resolvePatch?.();
        });

        await waitFor(() => {
            expect(input).toHaveValue('Newer Draft');
            expect(screen.queryByText('Settings changed elsewhere')).not.toBeInTheDocument();
            expect(document.querySelector('[data-action="save-settings"]') as HTMLButtonElement).not.toBeDisabled();
        });
    });
});

describe('GeneralPage loading state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows loading skeletons while fetching', async () => {
        mockApiGet.mockReturnValue(new Promise(() => {}));

        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-testid="settings-loading"]')).toBeInTheDocument();
        });
    });
});

describe('GeneralPage error state', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows error message when API fails with non-404 error', async () => {
        mockApiGet.mockRejectedValue(new Error('Server error'));

        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-testid="settings-error"]')).toBeInTheDocument();
            expect(screen.getByText(/failed to load settings/i)).toBeInTheDocument();
        });
    });

    it('shows form with defaults when the dynamic group is absent from the list', async () => {
        mockApiGet.mockResolvedValue([]);

        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-testid="settings-error"]')).not.toBeInTheDocument();
            expect(screen.getByLabelText('Instance Name')).toHaveValue('');
            expect(screen.getByLabelText('Base URL')).toHaveValue('');
            expect(screen.getByText('Settings will be created on first save.')).toBeInTheDocument();
        });
    });
});

it('loads settings on retry after the initial request fails', async () => {
    // Arrange
    const user = userEvent.setup();
    mockApiGet.mockRejectedValue(new Error('Unavailable'));
    render(<GeneralPage />, { wrapper: createWrapper() });
    const alert = await screen.findByTestId('settings-error');
    expect(within(alert).getByRole('paragraph')).toHaveTextContent(/^Failed to load settings\.$/);
    expect(alert).not.toHaveTextContent('save the settings');
    expect(screen.queryByLabelText('Instance Name')).not.toBeInTheDocument();
    // Act
    mockApiGet.mockResolvedValue([mockSettingsGroup]);
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByLabelText('Instance Name')).toHaveValue('My Magpie');
    expect(screen.queryByTestId('settings-error')).not.toBeInTheDocument();
});
