import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ToastStateProvider } from '@/components/ui/Toast';
import { settingsQueryKeys } from '@/hooks/useSettings';
import { useThemeCatalog } from '@/hooks/useThemeCatalog';
import { apiGet, apiPatch } from '@/lib/api';
import { ThemeDefaultsPanel } from './ThemeDefaultsPanel';

vi.mock('@/lib/api', () => ({ apiGet: vi.fn(), apiPatch: vi.fn() }));
vi.mock('@/hooks/useThemeCatalog', () => ({ useThemeCatalog: vi.fn() }));

describe('ThemeDefaultsPanel', () => {
    const catalog = {
        themes: [
            { id: 'Magpie.Api/Tokyo Night/Dark', name: 'Tokyo Night', variant: 'Dark' },
            { id: 'Magpie.Api/Ember/Dark', name: 'Ember', variant: 'Dark' },
            { id: 'Magpie.Api/Frost/Light', name: 'Frost', variant: 'Light' },
            { id: 'Magpie.Api/Seafoam/Light', name: 'Seafoam', variant: 'Light' },
        ],
        defaults: { mode: 'Dark', darkTheme: 'Magpie.Api/Tokyo Night/Dark', lightTheme: 'Magpie.Api/Frost/Light' },
    };
    const themeGroup = {
        id: 'sg-theme',
        name: 'theme',
        extensionId: null,
        createdAt: '2026-10-01T00:00:00Z',
        settings: [
            { id: 's-1', settingsGroupId: 'sg-theme', key: 'mode', value: 'System', createdAt: '2026-10-01T00:00:00Z' },
            {
                id: 's-2',
                settingsGroupId: 'sg-theme',
                key: 'darkTheme',
                value: 'Magpie.Api/Ember/Dark',
                createdAt: '2026-10-01T00:00:00Z',
            },
            {
                id: 's-3',
                settingsGroupId: 'sg-theme',
                key: 'lightTheme',
                value: 'Magpie.Api/Seafoam/Light',
                createdAt: '2026-10-01T00:00:00Z',
            },
        ],
    };
    let client: QueryClient;

    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(useThemeCatalog).mockReturnValue({ data: catalog } as ReturnType<typeof useThemeCatalog>);
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
    });

    afterEach(() => {
        client.clear();
    });

    it('ThemeDefaultsPanel_ShowsTheInstanceDefaults_WhenNoThemeSettingsAreSaved', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([]);

        // Act
        render(
            <QueryClientProvider client={client}>
                <ToastStateProvider>
                    <ThemeDefaultsPanel />
                </ToastStateProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(await screen.findByRole('combobox', { name: 'Mode' })).toHaveTextContent('Dark');
        expect(screen.getByRole('combobox', { name: 'Dark theme' })).toHaveTextContent('Tokyo Night');
        expect(screen.getByRole('combobox', { name: 'Light theme' })).toHaveTextContent('Frost');
        expect(screen.getByRole('button', { name: 'Save Settings' })).toBeDisabled();
    });

    it('ThemeDefaultsPanel_ShowsTheSavedSettings_WhenTheThemeGroupExists', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([themeGroup]);

        // Act
        render(
            <QueryClientProvider client={client}>
                <ToastStateProvider>
                    <ThemeDefaultsPanel />
                </ToastStateProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(await screen.findByRole('combobox', { name: 'Mode' })).toHaveTextContent('System');
        expect(screen.getByRole('combobox', { name: 'Dark theme' })).toHaveTextContent('Ember');
        expect(screen.getByRole('combobox', { name: 'Light theme' })).toHaveTextContent('Seafoam');
    });

    it('ThemeDefaultsPanel_OffersDarkLightAndSystem_WhenTheModeListIsOpened', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <ToastStateProvider>
                    <ThemeDefaultsPanel />
                </ToastStateProvider>
            </QueryClientProvider>,
        );

        // Act
        await user.click(await screen.findByRole('combobox', { name: 'Mode' }));

        // Assert
        expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['Dark', 'Light', 'System']);
    });

    it('ThemeDefaultsPanel_SavesTheThemeSettingsGroup_WhenSaveIsClicked', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([]);
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <ToastStateProvider>
                    <ThemeDefaultsPanel />
                </ToastStateProvider>
            </QueryClientProvider>,
        );
        await user.click(await screen.findByRole('combobox', { name: 'Mode' }));
        await user.click(screen.getByRole('option', { name: 'Light' }));

        // Act
        await user.click(screen.getByRole('button', { name: 'Save Settings' }));

        // Assert
        await waitFor(() =>
            expect(apiPatch).toHaveBeenCalledWith('/settings/groups/system/theme', {
                settings: {
                    add: {
                        mode: 'Light',
                        darkTheme: 'Magpie.Api/Tokyo Night/Dark',
                        lightTheme: 'Magpie.Api/Frost/Light',
                    },
                },
            }),
        );
    });

    it('ThemeDefaultsPanel_AsksBeforeSaving_WhenTheSettingsChangedElsewhere', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([themeGroup]);
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <ToastStateProvider>
                    <ThemeDefaultsPanel />
                </ToastStateProvider>
            </QueryClientProvider>,
        );
        await user.click(await screen.findByRole('combobox', { name: 'Mode' }));
        await user.click(screen.getByRole('option', { name: 'Light' }));
        act(() => {
            client.setQueryData(settingsQueryKeys.groups, [
                {
                    ...themeGroup,
                    settings: [
                        {
                            id: 's-1',
                            settingsGroupId: 'sg-theme',
                            key: 'mode',
                            value: 'Dark',
                            createdAt: '2026-10-01T00:00:00Z',
                        },
                    ],
                },
            ]);
        });
        await screen.findByText('Settings changed elsewhere');

        // Act
        await user.click(screen.getByRole('button', { name: 'Save Settings' }));

        // Assert
        expect(screen.getByRole('dialog', { name: 'Save over newer changes?' })).toBeInTheDocument();
        expect(apiPatch).not.toHaveBeenCalled();
    });

    it('ThemeDefaultsPanel_ShowsALoadErrorWithoutSaving_WhenTheCatalogFailsToLoad', () => {
        // Arrange
        vi.mocked(useThemeCatalog).mockReturnValue({ isError: true, refetch: vi.fn() } as unknown as ReturnType<
            typeof useThemeCatalog
        >);

        // Act
        render(
            <QueryClientProvider client={client}>
                <ToastStateProvider>
                    <ThemeDefaultsPanel />
                </ToastStateProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(screen.getByText('Failed to load')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Save Settings' })).not.toBeInTheDocument();
    });
});
