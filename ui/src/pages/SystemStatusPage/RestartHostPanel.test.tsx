import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { ReactNode } from 'react';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { RestartHostPanel } from './RestartHostPanel';

const mockApiGet = vi.fn();
const mockApiPost = vi.fn();
const mockIsApiError = vi.fn((...args: unknown[]) => args[0] instanceof Error);
const mockUseSystemInfo = vi.fn();
let eventHandler: ((event: { name: string; context: Record<string, string> }) => void) | undefined;

vi.mock('@/lib/api', () => ({
    apiGet: (...args: unknown[]) => mockApiGet(...args),
    apiPost: (...args: unknown[]) => mockApiPost(...args),
    isApiError: (...args: unknown[]) => mockIsApiError(...args),
}));

vi.mock('@/hooks/useSystemStatus', () => ({
    useSystemInfo: () => mockUseSystemInfo(),
}));

vi.mock('@/hooks/useEventStream', () => ({
    useEventStream: (handler: typeof eventHandler) => {
        eventHandler = handler;
    },
}));

function LocationProbe() {
    const location = useLocation();
    return <div data-testid="location-pathname">{location.pathname}</div>;
}

function createWrapper(initialPath = '/admin/system') {
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    const wrapper = function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={client}>
                <MemoryRouter initialEntries={[initialPath]}>
                    <Routes>
                        <Route path="*" element={<>{children}</>} />
                    </Routes>
                    <LocationProbe />
                </MemoryRouter>
            </QueryClientProvider>
        );
    };

    return { wrapper, client };
}

function wireApi(options: {
    capability: { canRestart: boolean; reason?: string | null };
    health?: () => { status: string };
    startedAt?: () => string;
}) {
    let systemInfoCalls = 0;
    mockUseSystemInfo.mockReturnValue({
        data: { restartSupported: options.capability.canRestart, startedAt: 'boot-old' },
        isLoading: false,
    });
    mockApiGet.mockImplementation((path: string) => {
        if (path === '/system/health') {
            return Promise.resolve(options.health?.() ?? { status: 'Healthy' });
        }
        if (path === '/system') {
            systemInfoCalls += 1;
            return Promise.resolve({
                startedAt: options.startedAt?.() ?? (systemInfoCalls === 1 ? 'boot-old' : 'boot-new'),
            });
        }
        return Promise.reject(new Error(`unexpected GET ${path}`));
    });
}

describe('RestartHostPanel', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        eventHandler = undefined;
        mockApiPost.mockResolvedValue(undefined);
    });

    it('disables the control and shows the reason when restart is unsupported', async () => {
        wireApi({ capability: { canRestart: false } });

        render(<RestartHostPanel />, { wrapper: createWrapper().wrapper });

        const button = await screen.findByRole('button', { name: /restart service/i });
        await waitFor(() => expect(button).toBeDisabled());

        expect(screen.getByTestId('restart-unavailable')).toHaveTextContent(
            'This deployment has no supervisor to relaunch Magpie, so a restart cannot be performed.',
        );
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('enables the control when supported', async () => {
        wireApi({ capability: { canRestart: true } });

        render(<RestartHostPanel />, { wrapper: createWrapper().wrapper });

        const button = await screen.findByRole('button', { name: /restart service/i });
        await waitFor(() => expect(button).toBeEnabled());

        expect(screen.queryByTestId('restart-unavailable')).not.toBeInTheDocument();
    });

    it('confirms, calls the endpoint, shows reconnecting, then recovers on a new process', async () => {
        wireApi({ capability: { canRestart: true } });

        const user = userEvent.setup();
        render(<RestartHostPanel />, { wrapper: createWrapper().wrapper });

        const button = await screen.findByRole('button', { name: /restart service/i });
        await waitFor(() => expect(button).toBeEnabled());

        await user.click(button);

        const confirm = await screen.findByRole('button', { name: /^restart$/i });
        await user.click(confirm);

        await waitFor(() => expect(mockApiPost).toHaveBeenCalledWith('/system/restart'));
        await waitFor(() =>
            expect(mockApiGet).toHaveBeenCalledWith('/system/health', {
                skipAuth: true,
                cache: 'no-store',
            }),
        );

        await act(async () => {
            eventHandler?.({ name: 'stream.reconnected', context: {} });
        });

        await waitFor(() => {
            expect(screen.getByText(/magpie is back online/i)).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /reload magpie/i })).toBeInTheDocument();
    });

    it('recovers via spa navigation and cache invalidation, not a full page reload', async () => {
        wireApi({ capability: { canRestart: true } });

        const { wrapper, client } = createWrapper('/admin/system');
        const invalidateSpy = vi.spyOn(client, 'invalidateQueries');
        const reloadSpy = vi.fn();
        const originalLocation = window.location;
        Object.defineProperty(window, 'location', {
            configurable: true,
            value: { ...originalLocation, reload: reloadSpy },
        });

        try {
            const user = userEvent.setup();
            render(<RestartHostPanel />, { wrapper });

            const button = await screen.findByRole('button', { name: /restart service/i });
            await waitFor(() => expect(button).toBeEnabled());
            await user.click(button);

            const confirm = await screen.findByRole('button', { name: /^restart$/i });
            await user.click(confirm);

            await act(async () => {
                eventHandler?.({ name: 'stream.reconnected', context: {} });
            });

            const reloadButton = await screen.findByRole('button', { name: /reload magpie/i });
            expect(screen.getByTestId('location-pathname')).toHaveTextContent('/admin/system');

            await user.click(reloadButton);

            await waitFor(() => expect(invalidateSpy).toHaveBeenCalled());
            expect(screen.getByTestId('location-pathname')).toHaveTextContent('/');
            expect(reloadSpy).not.toHaveBeenCalled();
            expect(screen.queryByText(/magpie is back online/i)).not.toBeInTheDocument();
        } finally {
            Object.defineProperty(window, 'location', {
                configurable: true,
                value: originalLocation,
            });
        }
    });

    it('shows the reconnecting overlay while the host is still down', async () => {
        wireApi({
            capability: { canRestart: true },
            health: () => ({ status: 'Unhealthy' }),
        });

        const user = userEvent.setup();
        render(<RestartHostPanel />, { wrapper: createWrapper().wrapper });

        const button = await screen.findByRole('button', { name: /restart service/i });
        await waitFor(() => expect(button).toBeEnabled());
        await user.click(button);

        const confirm = await screen.findByRole('button', { name: /^restart$/i });
        await user.click(confirm);

        await waitFor(() => {
            expect(screen.getByText(/reconnecting/i)).toBeInTheDocument();
        });
        expect(screen.queryByText(/magpie is back online/i)).not.toBeInTheDocument();
    });

    it('waits for a healthy replacement process instead of accepting the old host', async () => {
        let startedAt = 'boot-old';
        wireApi({
            capability: { canRestart: true },
            startedAt: () => startedAt,
        });

        const user = userEvent.setup();
        render(<RestartHostPanel />, { wrapper: createWrapper().wrapper });

        const button = await screen.findByRole('button', { name: /restart service/i });
        await waitFor(() => expect(button).toBeEnabled());
        await user.click(button);
        await user.click(await screen.findByRole('button', { name: /^restart$/i }));

        await waitFor(() => expect(mockApiGet).toHaveBeenCalledWith('/system', { cache: 'no-store' }));
        expect(screen.getByText(/reconnecting/i)).toBeInTheDocument();

        startedAt = 'boot-new';
        await act(async () => {
            eventHandler?.({ name: 'stream.reconnected', context: {} });
        });

        await waitFor(() => {
            expect(screen.getByText(/magpie is back online/i)).toBeInTheDocument();
        });
    });

    it('surfaces the server reason when the restart request fails', async () => {
        wireApi({ capability: { canRestart: true } });
        mockApiPost.mockRejectedValue(new Error('A restart is already in progress.'));

        const user = userEvent.setup();
        render(<RestartHostPanel />, { wrapper: createWrapper().wrapper });

        const button = await screen.findByRole('button', { name: /restart service/i });
        await waitFor(() => expect(button).toBeEnabled());
        await user.click(button);

        const confirm = await screen.findByRole('button', { name: /^restart$/i });
        await user.click(confirm);

        await waitFor(() => {
            expect(screen.getByTestId('restart-error')).toHaveTextContent('A restart is already in progress.');
        });
    });
});
