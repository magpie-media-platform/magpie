import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

import { ConfigureInstanceDialog } from './ConfigureInstanceDialog';

const mockApiGet = vi.fn();
const mockApiPost = vi.fn();
const mockApiPatch = vi.fn();
vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: (...args: unknown[]) => mockApiGet(...args),
        apiPost: (...args: unknown[]) => mockApiPost(...args),
        apiPatch: (...args: unknown[]) => mockApiPatch(...args),
    };
});

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
    });
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

function Harness() {
    const [open, setOpen] = useState(true);
    return (
        <>
            <button type="button" data-testid="reopen" onClick={() => setOpen(true)}>
                reopen
            </button>
            <ConfigureInstanceDialog
                open={open}
                onOpenChange={setOpen}
                indexer={{ id: 'idx-1', name: 'NZBgeek', providerType: 'newznab' }}
                onSaved={vi.fn()}
            />
        </>
    );
}

function EditHarness() {
    const [open, setOpen] = useState(true);
    return (
        <ConfigureInstanceDialog
            open={open}
            onOpenChange={setOpen}
            editTarget={{
                indexerId: 'idx-1',
                providerType: 'newznab',
                connection: {
                    id: 'connection-1',
                    indexerId: 'idx-1',
                    name: 'NZBGeek',
                    enabled: true,
                    status: 'Healthy',
                    settings: { host: 'https://nzbgeek.info' },
                    createdAt: '2024-01-01T00:00:00Z',
                },
            }}
        />
    );
}

function firstTestSignal(): AbortSignal {
    const call = mockApiPost.mock.calls.find((c) => c[0] === '/indexers/test');
    return (call?.[2] as { signal: AbortSignal }).signal;
}

describe('ConfigureInstanceDialog test cancellation', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue([]);

        let testCalls = 0;
        mockApiPost.mockImplementation((path: string, _body: unknown, options?: { signal?: AbortSignal }) => {
            if (path === '/indexers/test') {
                testCalls += 1;
                if (testCalls === 1) {
                    return new Promise((_resolve, reject) => {
                        options?.signal?.addEventListener('abort', () => {
                            reject(new DOMException('The operation was aborted.', 'AbortError'));
                        });
                    });
                }
                return Promise.resolve({ success: true, message: 'Connected' });
            }
            return Promise.resolve({});
        });
    });

    it('aborts the in-flight test and suppresses its toast when the dialog closes', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <Harness />
            </TestWrapper>,
        );

        const testButton = await screen.findByRole('button', { name: /^test$/i });
        await user.click(testButton);

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith(
                '/indexers/test',
                expect.objectContaining({ id: 'idx-1', providerType: 'newznab', settings: {} }),
                expect.objectContaining({ signal: expect.any(AbortSignal) }),
            );
        });

        const signal = firstTestSignal();
        expect(signal.aborted).toBe(false);
        await waitFor(() => expect(testButton).toHaveAttribute('aria-busy', 'true'));

        await user.click(screen.getByRole('button', { name: /^close$/i }));

        await waitFor(() => expect(signal.aborted).toBe(true));
        expect(document.querySelector('[data-component="toast"]')).toBeNull();
    });

    it('clears the gate so the next test can run after a cancelled one', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <Harness />
            </TestWrapper>,
        );

        await user.click(await screen.findByRole('button', { name: /^test$/i }));
        await waitFor(() => expect(firstTestSignal().aborted).toBe(false));

        await user.click(screen.getByRole('button', { name: /^close$/i }));
        await waitFor(() => expect(firstTestSignal().aborted).toBe(true));

        await user.click(screen.getByTestId('reopen'));
        await user.click(await screen.findByRole('button', { name: /^test$/i }));

        expect(await screen.findByText('Connected')).toBeInTheDocument();
    });
});

describe('ConfigureInstanceDialog editing', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue([]);
        mockApiPatch.mockResolvedValue(undefined);
    });

    it('wraps scalar and dictionary values for the connection PATCH contract', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <EditHarness />
            </TestWrapper>,
        );

        const nameInput = await screen.findByLabelText('Name');
        await user.clear(nameInput);
        await user.type(nameInput, 'NZBGeek EU');
        await user.click(screen.getByRole('button', { name: /^save$/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/indexers/idx-1/connections/connection-1', {
                name: { value: 'NZBGeek EU' },
                settings: { update: { host: 'https://nzbgeek.info' } },
            });
        });
    });
});

describe('ConfigureInstanceDialog settings schema request', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows a loading state and disables Add while the settings schema loads', async () => {
        // Arrange
        mockApiGet.mockReturnValue(new Promise(() => {}));

        // Act
        render(
            <TestWrapper>
                <Harness />
            </TestWrapper>,
        );

        // Assert
        expect(await screen.findAllByRole('status', { name: 'Loading' })).toHaveLength(3);
        expect(screen.queryByText(/needs no additional configuration/)).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^add$/i })).toBeDisabled();
    });

    it('shows an error with retry and disables Add when the settings schema request fails', async () => {
        // Arrange
        mockApiGet.mockRejectedValue(new Error('Server error'));

        // Act
        render(
            <TestWrapper>
                <Harness />
            </TestWrapper>,
        );

        // Assert
        expect(await screen.findByText('Failed to load settings')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
        expect(screen.queryByText(/needs no additional configuration/)).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^add$/i })).toBeDisabled();
    });
});
