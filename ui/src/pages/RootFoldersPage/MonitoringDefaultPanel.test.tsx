import { render, screen, waitFor, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

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

function createTestWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
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

describe('MonitoringDefaultPanel', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPatch = vi.mocked(apiPatch);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        cleanup();
        vi.restoreAllMocks();
    });

    it('defaults to Active when the settings group is not yet created', async () => {
        mockApiGet.mockResolvedValue([]);

        render(<MonitoringDefaultPanel />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: 'System default monitoring' })).toBeChecked();
        });
        expect(screen.getByTestId('monitoring-default-info')).toBeInTheDocument();
    });

    it('reflects the stored system default', async () => {
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
                        value: 'false',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);

        render(<MonitoringDefaultPanel />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('switch', { name: 'System default monitoring' })).not.toBeChecked();
        });
    });

    it('saves the picked value through the system settings PATCH', async () => {
        mockApiGet.mockResolvedValue([]);
        mockApiPatch.mockResolvedValue(undefined);
        const user = userEvent.setup();

        render(<MonitoringDefaultPanel />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-setting="system-default-monitoring"]')).toBeInTheDocument();
        });

        const toggle = screen.getByRole('switch', { name: 'System default monitoring' });
        await user.click(toggle);

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith(
                '/settings/groups/system/monitoring',
                expect.objectContaining({
                    settings: expect.objectContaining({ add: { 'system-default': 'false' } }),
                }),
            );
        });
    });

    it('keeps a newer toggle change dirty when the pending save succeeds', async () => {
        mockApiGet.mockResolvedValue([]);
        let resolvePatch: (() => void) | undefined;
        mockApiPatch.mockReturnValue(
            new Promise<void>((resolve) => {
                resolvePatch = resolve;
            }),
        );
        const user = userEvent.setup();

        render(<MonitoringDefaultPanel />, { wrapper: createTestWrapper() });

        const toggle = await screen.findByRole('switch', { name: 'System default monitoring' });
        await user.click(toggle);
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledTimes(1);
        });

        await user.click(toggle);
        expect(toggle).toBeChecked();

        resolvePatch?.();

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /save/i })).not.toBeDisabled();
        });
    });
});
