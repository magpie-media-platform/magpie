import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import * as apiModule from '@/lib/api';
import { ToastProvider, ToastStateProvider } from '@/components/ui/Toast';

import { FirstRootFolderStep } from './FirstRootFolderStep';

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
                    <FirstRootFolderStep onAdvance={onAdvance} />
                </ToastProvider>
            </ToastStateProvider>
        </QueryClientProvider>,
    );
}

describe('FirstRootFolderStep', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.mocked(apiModule.apiGet).mockResolvedValue([]);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders add and skip affordances', async () => {
        const onAdvance = vi.fn();
        renderStep(onAdvance);

        expect(await screen.findByRole('button', { name: /add a root folder/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /skip for now/i })).toBeInTheDocument();
    });

    it('advances when skip is clicked', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /skip for now/i }));
        expect(onAdvance).toHaveBeenCalled();
    });
});
