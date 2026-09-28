import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';

import * as apiModule from '@/lib/api';
import { ToastProvider, ToastStateProvider } from '@/components/ui/Toast';

import { FirstIndexerStep } from './FirstIndexerStep';

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

vi.mock('@/components/configuration/IndexerForm', () => ({
    IndexerForm: ({
        open,
        onSubmit,
    }: {
        open: boolean;
        onSubmit: (values: {
            name: string;
            providerType: string;
            settings: Record<string, string>;
        }) => void | Promise<void>;
    }) =>
        open ? (
            <button
                type="button"
                onClick={() => void onSubmit({ name: 'Test Indexer', providerType: 'newznab', settings: {} })}
            >
                stub save
            </button>
        ) : null,
}));

vi.mock('@/components/configuration/ConfigureInstanceDialog', () => ({
    ConfigureInstanceDialog: ({ open, onSaved }: { open: boolean; onSaved?: () => void }) =>
        open ? (
            <button type="button" onClick={onSaved}>
                stub configure save
            </button>
        ) : null,
}));

const directProvider = { kind: 'indexer', providerType: 'newznab', displayName: 'Newznab' };

const catalogDefinition = {
    id: 'idx-catalog',
    providerType: 'cardigann',
    name: 'Catalog Tracker',
    availability: 'Public',
    createdAt: '2024-01-01T00:00:00Z',
    connectionCount: 0,
};

function mockApi(responses: { providers?: unknown[]; indexers?: unknown[] }) {
    vi.mocked(apiModule.apiGet).mockImplementation(async (path: string) => {
        if (path === '/providers') {
            return (responses.providers ?? []) as never;
        }
        if (path === '/indexers') {
            return (responses.indexers ?? []) as never;
        }
        return [] as never;
    });
}

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
        <MemoryRouter>
            <QueryClientProvider client={queryClient}>
                <ToastStateProvider>
                    <ToastProvider>
                        <FirstIndexerStep onAdvance={onAdvance} />
                    </ToastProvider>
                </ToastStateProvider>
            </QueryClientProvider>
        </MemoryRouter>,
    );
}

describe('FirstIndexerStep', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        mockApi({ providers: [directProvider] });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders add and skip affordances', async () => {
        const onAdvance = vi.fn();
        renderStep(onAdvance);

        expect(await screen.findByRole('button', { name: /add an indexer/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /skip for now/i })).toBeInTheDocument();
    });

    it('does not treat available catalog rows as already configured', async () => {
        const onAdvance = vi.fn();
        mockApi({ providers: [directProvider], indexers: [catalogDefinition] });

        renderStep(onAdvance);

        expect(await screen.findByRole('button', { name: /add an indexer/i })).toBeInTheDocument();
        await waitFor(() => {
            expect(apiModule.apiGet).toHaveBeenCalledWith('/indexers');
        });
        expect(screen.queryByText(/already configured/i)).not.toBeInTheDocument();
    });

    it('offers the catalog when only catalog definitions are installed', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        mockApi({ indexers: [catalogDefinition] });
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /configure/i }));
        await user.click(await screen.findByRole('button', { name: /stub configure save/i }));

        expect(await screen.findByRole('button', { name: /add another indexer/i })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /add an indexer/i })).not.toBeInTheDocument();
        expect(screen.queryByText(/no indexer integrations are installed/i)).not.toBeInTheDocument();
        expect(onAdvance).not.toHaveBeenCalled();
    });

    it('says nothing is installed only when there is neither a provider nor a catalog', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        mockApi({});
        renderStep(onAdvance);

        expect(await screen.findByText(/no indexer integrations are installed/i)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /add an indexer/i })).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /skip for now/i }));
        expect(onAdvance).toHaveBeenCalledWith(true);
    });

    it('advances without creating when skip is clicked', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /skip for now/i }));
        expect(onAdvance).toHaveBeenCalled();
        expect(apiModule.apiPost).not.toHaveBeenCalled();
    });

    it('shows the added state and does not advance after a successful create', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /add an indexer/i }));
        await user.click(await screen.findByRole('button', { name: /stub save/i }));

        expect(await screen.findByRole('button', { name: /add another indexer/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
        expect(onAdvance).not.toHaveBeenCalled();
        expect(screen.queryByRole('button', { name: /skip for now/i })).not.toBeInTheDocument();
    });

    it('add another reopens a fresh form and a second create works', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /add an indexer/i }));
        await user.click(await screen.findByRole('button', { name: /stub save/i }));
        await user.click(await screen.findByRole('button', { name: /add another indexer/i }));

        expect(await screen.findByRole('button', { name: /stub save/i })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /add another indexer/i })).not.toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /stub save/i }));
        expect(await screen.findByRole('button', { name: /add another indexer/i })).toBeInTheDocument();
        expect(onAdvance).not.toHaveBeenCalled();
    });

    it('advances when continue is clicked after an add', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        renderStep(onAdvance);

        await user.click(await screen.findByRole('button', { name: /add an indexer/i }));
        await user.click(await screen.findByRole('button', { name: /stub save/i }));
        await user.click(await screen.findByRole('button', { name: 'Continue' }));

        expect(onAdvance).toHaveBeenCalledTimes(1);
    });
});
