import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';

import { JobRetryPolicyPanel } from './JobRetryPolicyPanel';

const mockApiGet = vi.fn();
const mockApiPatch = vi.fn();

vi.mock('@/lib/api', () => ({
    apiGet: (...args: unknown[]) => mockApiGet(...args),
    apiPatch: (...args: unknown[]) => mockApiPatch(...args),
}));

const mockGroup = {
    id: 'sg-job-retry',
    name: 'job-retry',
    extensionId: null,
    createdAt: '2025-01-01T00:00:00Z',
    settings: [
        {
            id: 's-1',
            settingsGroupId: 'sg-job-retry',
            key: 'base-seconds',
            value: '15',
            createdAt: '2025-01-01T00:00:00Z',
        },
        {
            id: 's-2',
            settingsGroupId: 'sg-job-retry',
            key: 'factor',
            value: '3',
            createdAt: '2025-01-01T00:00:00Z',
        },
        {
            id: 's-3',
            settingsGroupId: 'sg-job-retry',
            key: 'cap-seconds',
            value: '600',
            createdAt: '2025-01-01T00:00:00Z',
        },
        {
            id: 's-4',
            settingsGroupId: 'sg-job-retry',
            key: 'max-attempts',
            value: '4',
            createdAt: '2025-01-01T00:00:00Z',
        },
    ],
};

function createWrapper() {
    const client = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
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

describe('JobRetryPolicyPanel', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue([mockGroup]);
        mockApiPatch.mockResolvedValue(undefined);
    });

    it('fetches the job-retry settings group on mount', async () => {
        render(<JobRetryPolicyPanel />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/settings/groups');
        });
    });

    it('renders all four retry inputs with loaded values', async () => {
        render(<JobRetryPolicyPanel />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText(/base delay/i)).toHaveValue(15);
            expect(screen.getByLabelText(/backoff factor/i)).toHaveValue(3);
            expect(screen.getByLabelText(/maximum delay/i)).toHaveValue(600);
            expect(screen.getByLabelText(/maximum attempts/i)).toHaveValue(4);
        });
    });

    it('persists edits via PATCH', async () => {
        const user = userEvent.setup();
        render(<JobRetryPolicyPanel />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByLabelText(/maximum attempts/i)).toHaveValue(4);
        });

        const attempts = screen.getByLabelText(/maximum attempts/i);
        await user.clear(attempts);
        await user.type(attempts, '3');

        const save = screen.getByRole('button', { name: /save settings/i });
        await user.click(save);

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/job-retry', {
                settings: {
                    update: {
                        'base-seconds': '15',
                        factor: '3',
                        'cap-seconds': '600',
                        'max-attempts': '3',
                    },
                },
            });
        });
    });

    it.each([
        [/base delay/i, '', 'Base delay is required.'],
        [/base delay/i, '0', 'Base delay must be at least 1.'],
        [/backoff factor/i, '0.5', 'Backoff factor must be at least 1.'],
        [/maximum delay/i, '0', 'Maximum delay must be at least 1.'],
        [/maximum attempts/i, '2.5', 'Maximum attempts must be a whole number.'],
    ])('rejects an invalid %s value before saving', async (label, value, expectedError) => {
        const user = userEvent.setup();
        render(<JobRetryPolicyPanel />, { wrapper: createWrapper() });

        const input = await screen.findByLabelText(label);
        await user.clear(input);
        if (value) {
            await user.type(input, value);
        }

        await user.click(screen.getByRole('button', { name: /save settings/i }));

        const error = screen.getByText(expectedError);
        expect(error).toHaveAttribute('role', 'alert');
        expect(input).toHaveAttribute('aria-invalid', 'true');
        expect(input).toHaveAccessibleDescription(expectedError);
        expect(mockApiPatch).not.toHaveBeenCalled();
        expect(screen.queryByText('Job retry settings updated.')).not.toBeInTheDocument();
    });

    it('clears a validation error when the value is corrected and saves the corrected value', async () => {
        const user = userEvent.setup();
        render(<JobRetryPolicyPanel />, { wrapper: createWrapper() });

        const attempts = await screen.findByLabelText(/maximum attempts/i);
        await user.clear(attempts);
        await user.type(attempts, '2.5');
        await user.click(screen.getByRole('button', { name: /save settings/i }));

        expect(screen.getByText('Maximum attempts must be a whole number.')).toBeInTheDocument();

        await user.clear(attempts);
        await user.type(attempts, '3');

        expect(screen.queryByText('Maximum attempts must be a whole number.')).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /save settings/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/settings/groups/system/job-retry', {
                settings: {
                    update: {
                        'base-seconds': '15',
                        factor: '3',
                        'cap-seconds': '600',
                        'max-attempts': '3',
                    },
                },
            });
        });
    });
});
