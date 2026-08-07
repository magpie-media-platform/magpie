import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ApiError } from '@/lib/api';
import * as apiModule from '@/lib/api';
import { ToastProvider, ToastStateProvider } from '@/components/ui/Toast';

import { MetadataProviderStep } from './MetadataProviderStep';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPatch: vi.fn(),
    };
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });
}

function renderStep(onAdvance: () => void) {
    const queryClient = createTestQueryClient();
    return render(
        <QueryClientProvider client={queryClient}>
            <ToastStateProvider>
                <ToastProvider>
                    <MetadataProviderStep onAdvance={onAdvance} />
                </ToastProvider>
            </ToastStateProvider>
        </QueryClientProvider>,
    );
}

describe('MetadataProviderStep', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders the skip affordance even when the operator is unauthenticated', async () => {
        const onAdvance = vi.fn();
        vi.mocked(apiModule.apiGet).mockRejectedValue(new ApiError(401, 'Unauthenticated'));

        renderStep(onAdvance);

        const skipButton = await screen.findByRole('button', { name: /skip for now/i });
        expect(skipButton).toBeInTheDocument();

        await waitFor(() => {
            expect(screen.getByText(/signed in yet/i)).toBeInTheDocument();
        });
    });

    it('advances when skip is clicked', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        vi.mocked(apiModule.apiGet).mockResolvedValue([]);

        renderStep(onAdvance);

        const skip = await screen.findByRole('button', { name: /skip for now/i });
        await user.click(skip);

        expect(onAdvance).toHaveBeenCalled();
    });

    it('shows an editable card per extension settings group', async () => {
        const onAdvance = vi.fn();
        vi.mocked(apiModule.apiGet).mockResolvedValue([
            {
                id: 'group-1',
                name: 'Provider A',
                extensionId: 'ext-1',
                createdAt: '2024-01-01T00:00:00Z',
                settings: [
                    {
                        id: 'set-1',
                        settingsGroupId: 'group-1',
                        key: 'apiKey',
                        value: '',
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                ],
            },
        ]);

        renderStep(onAdvance);

        expect(await screen.findByText('Provider A')).toBeInTheDocument();
        expect(screen.getByLabelText(/apiKey/i)).toBeInTheDocument();
    });

    it('hides extension groups with no settings keys', async () => {
        const onAdvance = vi.fn();
        vi.mocked(apiModule.apiGet).mockResolvedValue([
            {
                id: 'group-empty',
                name: 'Empty',
                extensionId: 'ext-1',
                createdAt: '2024-01-01T00:00:00Z',
                settings: [],
            },
        ]);

        renderStep(onAdvance);

        await screen.findByRole('button', { name: /skip for now/i });
        expect(screen.queryByText('Empty')).not.toBeInTheDocument();
    });
});
