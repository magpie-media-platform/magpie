import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';

import { GeneralPage } from './GeneralPage';

const mockApiGet = vi.fn();
const mockApiPatch = vi.fn();
const mockIsApiError = vi.fn(() => false);

vi.mock('@/lib/api', () => ({
    apiGet: (...args: unknown[]) => mockApiGet(...args),
    apiPatch: (...args: unknown[]) => mockApiPatch(...args),
    isApiError: (...args: unknown[]) => mockIsApiError(...args),
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

describe('GeneralPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockSettingsGroup);
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
            expect(screen.getByRole('heading', { name: 'General', level: 1 })).toBeInTheDocument();
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
        mockApiGet.mockResolvedValue(mockSettingsGroup);
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
            expect(mockApiGet).toHaveBeenCalledWith('/settings/groups/system/general');
        });
    });
});

describe('GeneralPage save', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockSettingsGroup);
        mockApiPatch.mockResolvedValue(undefined);
    });

    it('renders save button initially disabled', async () => {
        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            const saveButton = screen.getByRole('button', { name: /save settings/i });
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
            expect(screen.getByRole('button', { name: /save settings/i })).not.toBeDisabled();
        });
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

        const saveButton = screen.getByRole('button', { name: /save settings/i });
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

        const saveButton = screen.getByRole('button', { name: /save settings/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByText('Save Failed')).toBeInTheDocument();
            expect(screen.getByText('Server error')).toBeInTheDocument();
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

        const saveButton = screen.getByRole('button', { name: /save settings/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /save settings/i })).toBeDisabled();
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

    it('shows form with defaults when API returns 404', async () => {
        const notFoundError = { status: 404, message: 'Not found' };
        mockIsApiError.mockImplementation((v: unknown) => v === notFoundError);
        mockApiGet.mockRejectedValue(notFoundError);

        render(<GeneralPage />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-testid="settings-error"]')).not.toBeInTheDocument();
            expect(screen.getByLabelText('Instance Name')).toHaveValue('');
            expect(screen.getByLabelText('Base URL')).toHaveValue('');
            expect(screen.getByText('Settings will be created on first save.')).toBeInTheDocument();
        });
    });
});
