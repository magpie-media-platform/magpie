import { act, render, screen, waitFor, within, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { settingsQueryKeys } from '@/hooks/useSettings';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPatch } from '@/lib/api';

import { MonitoringDefaultPanel } from './MonitoringDefaultPanel';

let queryClient: QueryClient;

function TestWrapper({ children }: { children: ReactNode }) {
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
}

describe('MonitoringDefaultPanel', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
        queryClient = new QueryClient({
            defaultOptions: {
                queries: { retry: false, gcTime: 0 },
                mutations: { retry: false },
            },
        });
    });

    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it('Picker_SelectsMonitoredAndShowsTheInfo_WhenTheSettingsGroupIsNotCreated', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);

        // Act
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });

        // Assert
        await waitFor(() => {
            expect(screen.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true');
        });
        expect(screen.getByTestId('monitoring-default-info')).toHaveTextContent(
            'Monitored is used until a default is saved.',
        );
    });

    it('Picker_OffersTheRootFolderModes_WhenLoaded', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);

        // Act
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });

        // Assert
        const modeGroup = await screen.findByRole('radiogroup', { name: 'Default monitoring' });
        expect(
            within(modeGroup)
                .getAllByRole('radio')
                .map((mode) => mode.textContent),
        ).toEqual(['Unmonitored', 'Monitored', 'Shotgun']);
    });

    it.each([
        ['false', /^Unmonitored:/],
        ['0', /^Unmonitored:/],
        ['true', /^Monitored:/],
        ['Unmonitored', /^Unmonitored:/],
        ['Monitored', /^Monitored:/],
        ['Shotgun', /^Shotgun:/],
    ])('Picker_SelectsTheStoredMode_WhenTheSystemDefaultIs (%s)', async (stored, expected) => {
        // Arrange
        mockApiGet.mockResolvedValue([
            {
                id: 'g1',
                name: 'monitoring',
                extensionId: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 's1',
                        settingsGroupId: 'g1',
                        key: 'system-default',
                        value: stored,
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);

        // Act
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });

        // Assert
        await waitFor(() => {
            expect(screen.getByRole('radio', { name: expected })).toHaveAttribute('aria-checked', 'true');
        });
    });

    it.each([[/^Unmonitored:/], [/^Monitored:/]])(
        'MaxSpread_IsHidden_WhenTheChosenModeIsNotShotgun (%s)',
        async (mode) => {
            // Arrange
            mockApiGet.mockResolvedValue([]);
            const user = userEvent.setup();
            render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });

            // Act
            await user.click(await screen.findByRole('radio', { name: mode }));

            // Assert
            expect(screen.queryByRole('spinbutton', { name: 'Max spread' })).not.toBeInTheDocument();
            expect(screen.queryByText('How many to grab at once per item. 0 means all.')).not.toBeInTheDocument();
        },
    );

    it('MaxSpread_Shows_WhenShotgunIsChosen', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });

        // Act
        await user.click(await screen.findByRole('radio', { name: /^Shotgun:/ }));

        // Assert
        expect(screen.getByRole('spinbutton', { name: 'Max spread' })).toBeInTheDocument();
        expect(screen.getByText('How many to grab at once per item. 0 means all.')).toBeInTheDocument();
    });

    it('Save_SendsTheChosenModeWithoutTheMaxSpread_WhenTheModeIsNotShotgun', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);
        mockApiPatch.mockResolvedValue(undefined);
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await user.click(await screen.findByRole('radio', { name: /^Unmonitored:/ }));

        // Act
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/monitoring', {
                settings: { add: { 'system-default': 'Unmonitored' } },
            });
        });
    });

    it('Save_SendsShotgunWithTheMaxSpread_WhenShotgunIsChosen', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);
        mockApiPatch.mockResolvedValue(undefined);
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await user.click(await screen.findByRole('radio', { name: /^Shotgun:/ }));
        const maxSpread = screen.getByRole('spinbutton', { name: 'Max spread' });
        await user.clear(maxSpread);
        await user.type(maxSpread, '2');

        // Act
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/monitoring', {
                settings: { add: { 'system-default': 'Shotgun', 'max-spread': '2' } },
            });
        });
    });

    it('Save_IsDisabled_WhenTheShotgunMaxSpreadIsNotAWholeNumberOfZeroOrMore', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await user.click(await screen.findByRole('radio', { name: /^Shotgun:/ }));
        const maxSpread = screen.getByRole('spinbutton', { name: 'Max spread' });

        // Act
        await user.clear(maxSpread);
        await user.type(maxSpread, '-1');

        // Assert
        expect(screen.getByText('Max spread must be a whole number, 0 or more.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
    });

    it.each([
        ['Monitored', /^Monitored:/, '5'],
        ['Unmonitored', /^Unmonitored:/, '-1'],
    ])(
        'Save_IsDisabled_WhenTheStoredModeIsChosenAgainAfterAMaxSpreadEdit (%s)',
        async (stored, storedMode, maxSpreadEdit) => {
            // Arrange
            mockApiGet.mockResolvedValue([
                {
                    id: 'g1',
                    name: 'monitoring',
                    extensionId: null,
                    createdAt: '2024-01-01T00:00:00Z',
                    settings: [
                        {
                            id: 's1',
                            settingsGroupId: 'g1',
                            key: 'system-default',
                            value: stored,
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                        {
                            id: 's2',
                            settingsGroupId: 'g1',
                            key: 'max-spread',
                            value: '2',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                    ],
                },
            ]);
            const user = userEvent.setup();
            render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
            await waitFor(() => {
                expect(screen.getByRole('radio', { name: storedMode })).toHaveAttribute('aria-checked', 'true');
            });
            await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
            const maxSpread = screen.getByRole('spinbutton', { name: 'Max spread' });
            await user.clear(maxSpread);
            await user.type(maxSpread, maxSpreadEdit);

            // Act
            await user.click(screen.getByRole('radio', { name: storedMode }));

            // Assert
            expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
        },
    );

    it('Save_KeepsANewerModeChangeDirty_WhenThePendingSaveSucceeds', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([]);
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await user.click(await screen.findByRole('radio', { name: /^Unmonitored:/ }));
        await user.click(screen.getByRole('button', { name: /save/i }));
        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledTimes(1);
        });
        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));

        // Act
        resolvePatch?.();

        // Assert
        await waitFor(() => {
            expect(screen.getByRole('button', { name: /save/i })).not.toBeDisabled();
        });
        expect(screen.getByRole('radio', { name: /^Shotgun:/ })).toHaveAttribute('aria-checked', 'true');
    });

    it('MaxSpread_ShowsTheSavedValue_WhenShotgunIsChosenAgainAfterAPendingSave', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([
            {
                id: 'g1',
                name: 'monitoring',
                extensionId: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 's1',
                        settingsGroupId: 'g1',
                        key: 'system-default',
                        value: 'Monitored',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                    {
                        id: 's2',
                        settingsGroupId: 'g1',
                        key: 'max-spread',
                        value: '2',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await waitFor(() => {
            expect(screen.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true');
        });
        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
        await user.clear(screen.getByRole('spinbutton', { name: 'Max spread' }));
        await user.type(screen.getByRole('spinbutton', { name: 'Max spread' }), '5');
        await user.click(screen.getByRole('button', { name: /save/i }));
        await user.click(screen.getByRole('radio', { name: /^Monitored:/ }));
        mockApiGet.mockResolvedValue([
            {
                id: 'g1',
                name: 'monitoring',
                extensionId: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 's1',
                        settingsGroupId: 'g1',
                        key: 'system-default',
                        value: 'Shotgun',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                    {
                        id: 's2',
                        settingsGroupId: 'g1',
                        key: 'max-spread',
                        value: '5',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);
        resolvePatch?.();
        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledTimes(2);
        });

        // Act
        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));

        // Assert
        await waitFor(() => {
            expect(screen.getByRole('spinbutton', { name: 'Max spread' })).toHaveValue(5);
        });
        expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
    });

    it('MaxSpread_ShowsTheSavedValue_WhenShotgunIsChosenAgainBeforeTheRefetchAfterAPendingSave', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([
            {
                id: 'g1',
                name: 'monitoring',
                extensionId: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 's1',
                        settingsGroupId: 'g1',
                        key: 'system-default',
                        value: 'Monitored',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                    {
                        id: 's2',
                        settingsGroupId: 'g1',
                        key: 'max-spread',
                        value: '2',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await waitFor(() => {
            expect(screen.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true');
        });
        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
        await user.clear(screen.getByRole('spinbutton', { name: 'Max spread' }));
        await user.type(screen.getByRole('spinbutton', { name: 'Max spread' }), '5');
        await user.click(screen.getByRole('button', { name: /save/i }));
        await user.click(screen.getByRole('radio', { name: /^Monitored:/ }));
        let resolveRefetch: ((groups: unknown) => void) | undefined;
        mockApiGet.mockReturnValue(
            new Promise((resolve) => {
                resolveRefetch = resolve;
            }),
        );
        resolvePatch?.();
        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledTimes(2);
        });
        await waitFor(() => {
            expect(screen.getByRole('button', { name: /save/i })).toHaveTextContent('Save');
        });

        // Act
        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
        act(() => {
            resolveRefetch?.([
                {
                    id: 'g1',
                    name: 'monitoring',
                    extensionId: null,
                    createdAt: '2024-01-01T00:00:00Z',
                    settings: [
                        {
                            id: 's1',
                            settingsGroupId: 'g1',
                            key: 'system-default',
                            value: 'Shotgun',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                        {
                            id: 's2',
                            settingsGroupId: 'g1',
                            key: 'max-spread',
                            value: '5',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                    ],
                },
            ]);
        });

        // Assert
        await waitFor(() => {
            expect(screen.getByRole('spinbutton', { name: 'Max spread' })).toHaveValue(5);
        });
        expect(screen.getByRole('radio', { name: /^Shotgun:/ })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
    });

    it('Picker_ShowsTheNewerStoredSettings_WhenTheLoadedModeIsChosenAgainAfterAnExternalChange', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([
            {
                id: 'g1',
                name: 'monitoring',
                extensionId: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 's1',
                        settingsGroupId: 'g1',
                        key: 'system-default',
                        value: 'Monitored',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                    {
                        id: 's2',
                        settingsGroupId: 'g1',
                        key: 'max-spread',
                        value: '2',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);
        queryClient = new QueryClient();
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await waitFor(() => {
            expect(screen.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true');
        });
        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
        await user.clear(screen.getByRole('spinbutton', { name: 'Max spread' }));
        await user.type(screen.getByRole('spinbutton', { name: 'Max spread' }), '5');
        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    id: 'g1',
                    name: 'monitoring',
                    extensionId: null,
                    createdAt: '2024-01-01T00:00:00Z',
                    settings: [
                        {
                            id: 's1',
                            settingsGroupId: 'g1',
                            key: 'system-default',
                            value: 'Shotgun',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                        {
                            id: 's2',
                            settingsGroupId: 'g1',
                            key: 'max-spread',
                            value: '7',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                    ],
                },
            ]);
        });
        await screen.findByText('Settings changed elsewhere');

        // Act
        await user.click(screen.getByRole('radio', { name: /^Monitored:/ }));

        // Assert
        await waitFor(() => {
            expect(screen.getByRole('radio', { name: /^Shotgun:/ })).toHaveAttribute('aria-checked', 'true');
        });
        expect(screen.getByRole('spinbutton', { name: 'Max spread' })).toHaveValue(7);
        expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
        expect(screen.queryByText('Settings changed elsewhere')).not.toBeInTheDocument();
    });

    it('Save_AsksForConfirmation_WhenAnotherModeIsChosenAfterAnExternalChange', async () => {
        // Arrange
        mockApiGet.mockResolvedValue([
            {
                id: 'g1',
                name: 'monitoring',
                extensionId: null,
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 's1',
                        settingsGroupId: 'g1',
                        key: 'system-default',
                        value: 'Shotgun',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                    {
                        id: 's2',
                        settingsGroupId: 'g1',
                        key: 'max-spread',
                        value: '2',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);
        queryClient = new QueryClient();
        const user = userEvent.setup();
        render(<MonitoringDefaultPanel />, { wrapper: TestWrapper });
        await user.clear(await screen.findByRole('spinbutton', { name: 'Max spread' }));
        await user.type(screen.getByRole('spinbutton', { name: 'Max spread' }), '5');
        act(() => {
            queryClient.setQueryData(settingsQueryKeys.groups, [
                {
                    id: 'g1',
                    name: 'monitoring',
                    extensionId: null,
                    createdAt: '2024-01-01T00:00:00Z',
                    settings: [
                        {
                            id: 's1',
                            settingsGroupId: 'g1',
                            key: 'system-default',
                            value: 'Shotgun',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                        {
                            id: 's2',
                            settingsGroupId: 'g1',
                            key: 'max-spread',
                            value: '7',
                            createdAt: '2024-01-01T00:00:00Z',
                        },
                    ],
                },
            ]);
        });
        await screen.findByText('Settings changed elsewhere');
        await user.click(screen.getByRole('radio', { name: /^Monitored:/ }));

        // Act
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        expect(await screen.findByText('Save over newer changes?')).toBeInTheDocument();
        expect(mockApiPatch).not.toHaveBeenCalled();
    });
});
