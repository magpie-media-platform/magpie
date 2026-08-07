import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { ApiError } from '@/lib/api';
import * as setupApiModule from '@/lib/setupApi';

import { DbProviderStep } from './DbProviderStep';

vi.mock('@/lib/setupApi', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/setupApi')>();
    return {
        ...actual,
        fetchSetupDatabaseStatus: vi.fn(),
        configureSetupDatabase: vi.fn(),
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
            <DbProviderStep onAdvance={onAdvance} />
        </QueryClientProvider>,
    );
}

describe('DbProviderStep', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('auto-skips when the database is already initialised', async () => {
        const onAdvance = vi.fn();
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: true,
            provider: 'SQLite',
        });

        renderStep(onAdvance);

        await waitFor(() => {
            expect(onAdvance).toHaveBeenCalled();
        });
    });

    it('renders the provider picker when the database is not initialised', async () => {
        const onAdvance = vi.fn();
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: false,
            provider: 'SQLite',
        });

        renderStep(onAdvance);

        const sqliteRadio = await screen.findByLabelText(/sqlite/i);
        expect(sqliteRadio).toBeInTheDocument();
        expect(screen.getByLabelText(/postgresql/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/sql server/i)).toBeInTheDocument();
        expect(onAdvance).not.toHaveBeenCalled();
    });

    it('requires a connection string for postgres before submit', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: false,
            provider: 'SQLite',
        });

        renderStep(onAdvance);

        await user.click(await screen.findByLabelText(/postgresql/i));
        await user.click(screen.getByRole('button', { name: /save and continue/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/connection string is required/i);
        expect(vi.mocked(setupApiModule.configureSetupDatabase)).not.toHaveBeenCalled();
    });

    it('submits and surfaces the restart prompt on success', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: false,
            provider: 'SQLite',
        });
        vi.mocked(setupApiModule.configureSetupDatabase).mockResolvedValue({
            provider: 'postgresql',
            restartRequired: true,
        });

        renderStep(onAdvance);

        await user.click(await screen.findByLabelText(/postgresql/i));
        await user.type(screen.getByLabelText(/connection string/i), 'Host=localhost;Database=magpie');
        await user.click(screen.getByRole('button', { name: /save and continue/i }));

        await waitFor(() => {
            expect(vi.mocked(setupApiModule.configureSetupDatabase)).toHaveBeenCalled();
        });
        expect(vi.mocked(setupApiModule.configureSetupDatabase).mock.calls[0][0]).toEqual({
            provider: 'postgresql',
            connectionString: 'Host=localhost;Database=magpie',
        });

        await screen.findByText(/restart magpie/i);
        expect(screen.getByRole('button', { name: /i have restarted magpie/i })).toBeInTheDocument();
    });

    it('advances after a restart-recheck once the db reports initialised', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus)
            .mockResolvedValueOnce({ initialized: false, provider: 'SQLite' })
            .mockResolvedValueOnce({ initialized: false, provider: 'SQLite' })
            .mockResolvedValueOnce({ initialized: true, provider: 'PostgreSQL' });
        vi.mocked(setupApiModule.configureSetupDatabase).mockResolvedValue({
            provider: 'postgresql',
            restartRequired: true,
        });

        renderStep(onAdvance);

        await user.click(await screen.findByLabelText(/postgresql/i));
        await user.type(screen.getByLabelText(/connection string/i), 'Host=x');
        await user.click(screen.getByRole('button', { name: /save and continue/i }));

        const restartButton = await screen.findByRole('button', { name: /i have restarted magpie/i });
        await user.click(restartButton);

        await waitFor(() => {
            expect(onAdvance).toHaveBeenCalled();
        });
    });

    it('surfaces the api error inline when configure fails', async () => {
        const onAdvance = vi.fn();
        const user = userEvent.setup();
        vi.mocked(setupApiModule.fetchSetupDatabaseStatus).mockResolvedValue({
            initialized: false,
            provider: 'SQLite',
        });
        vi.mocked(setupApiModule.configureSetupDatabase).mockRejectedValue(
            new ApiError(400, 'Connection string is required for the postgresql provider.'),
        );

        renderStep(onAdvance);

        await user.click(await screen.findByLabelText(/postgresql/i));
        await user.type(screen.getByLabelText(/connection string/i), 'something');
        await user.click(screen.getByRole('button', { name: /save and continue/i }));

        const errorEl = await screen.findByTestId('setup-error');
        expect(errorEl).toHaveTextContent(/connection string is required/i);
    });
});
