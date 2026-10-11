import { useState, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { RestartRequiredProvider } from '@/hooks/ui/useRestartRequired';
import { apiGet, apiPost } from '@/lib/api';

import { ExtensionsStep } from './ExtensionsStep';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
    apiPost: vi.fn(),
}));

const hostRestart = vi.hoisted(() => ({
    canRestart: true,
    phase: 'idle',
    error: null as string | null,
    requestRestart: vi.fn(),
    reset: vi.fn(),
}));

vi.mock('@/hooks/useHostRestart', () => ({
    useHostRestart: () => ({ capabilityLoading: false, ...hostRestart }),
}));

vi.mock('@/hooks/useInstanceName', () => ({
    useInstanceName: () => 'Magpie',
}));

const systemInfo = vi.hoisted(() => ({
    data: { startedAt: '2026-10-06T00:00:00Z' },
    isFetching: false,
    refetch: vi.fn(),
}));

vi.mock('@/hooks/useSystemStatus', () => ({
    useSystemInfo: () => systemInfo,
}));

const TMDB_EXTENSION = {
    id: 'ext-1',
    packageId: 'Magpie.Extensions.Tmdb',
    version: '1.2.0',
    name: 'TMDB',
    filePath: 'Magpie.Extensions.Tmdb/Magpie.Extensions.Tmdb.dll',
    assemblyName: 'Magpie.Extensions.Tmdb',
    installed: true,
    enabled: true,
    loadOrder: 0,
    managed: true,
    autoUpgrade: false,
    createdAt: '2026-10-06T00:00:00Z',
};

function Providers({ children }: { children: ReactNode }) {
    const [client] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: false } } }));
    return (
        <QueryClientProvider client={client}>
            <RestartRequiredProvider>{children}</RestartRequiredProvider>
        </QueryClientProvider>
    );
}

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGet).mockResolvedValue([]);
    vi.mocked(apiPost).mockResolvedValue({ extension: TMDB_EXTENSION, restartRequired: true });
    systemInfo.refetch.mockResolvedValue({ data: { startedAt: '2026-10-06T00:00:00Z' } });
    hostRestart.canRestart = true;
    hostRestart.phase = 'idle';
    hostRestart.error = null;
});

describe('ExtensionsStep', () => {
    it('install_ShowsTheRestartPrompt_WhenTheInstallRequiresARestart', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });
        await user.click(screen.getByRole('button', { name: 'Add extension' }));
        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Extensions.Tmdb');

        // Act
        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        // Assert
        expect(await screen.findByText('Restart Magpie to apply pending extension changes.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Restart' })).toBeEnabled();
        expect(apiPost).toHaveBeenCalledWith('/extensions', {
            packageId: 'Magpie.Extensions.Tmdb',
            version: undefined,
        });
    });

    it('restart_RequestsTheHostRestart_WhenRestartIsClicked', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });
        await user.click(screen.getByRole('button', { name: 'Add extension' }));
        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Extensions.Tmdb');
        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        // Act
        await user.click(await screen.findByRole('button', { name: 'Restart' }));

        // Assert
        expect(hostRestart.requestRestart).toHaveBeenCalledOnce();
    });

    it('restart_RemovesTheRestartPrompt_WhenTheHostIsBackOnline', async () => {
        // Arrange
        const user = userEvent.setup();
        const { rerender } = render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });
        await user.click(screen.getByRole('button', { name: 'Add extension' }));
        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Extensions.Tmdb');
        await user.click(screen.getByRole('button', { name: 'Install Package' }));
        await screen.findByText('Restart Magpie to apply pending extension changes.');
        hostRestart.phase = 'recovered';

        // Act
        rerender(<ExtensionsStep onAdvance={vi.fn()} />);

        // Assert
        expect(screen.queryByText('Restart Magpie to apply pending extension changes.')).not.toBeInTheDocument();
        expect(hostRestart.reset).toHaveBeenCalledOnce();
    });

    it('restart_ShowsTheHostRestartError_WhenTheRestartFails', async () => {
        // Arrange
        hostRestart.phase = 'error';
        hostRestart.error = 'Magpie did not come back online. Read the host log.';

        // Act
        render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });

        // Assert
        expect(screen.getByTestId('setup-error')).toHaveTextContent(
            'Magpie did not come back online. Read the host log.',
        );
    });

    it('install_ShowsTheManualRestartPath_WhenTheHostCannotRestart', async () => {
        // Arrange
        hostRestart.canRestart = false;
        const user = userEvent.setup();
        render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });
        await user.click(screen.getByRole('button', { name: 'Add extension' }));
        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Extensions.Tmdb');

        // Act
        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        // Assert
        expect(await screen.findByRole('button', { name: 'I have restarted Magpie' })).toBeEnabled();
        expect(screen.queryByRole('button', { name: 'Restart' })).not.toBeInTheDocument();
    });

    it('recheck_RemovesTheRestartPrompt_WhenTheHostHasRestarted', async () => {
        // Arrange
        hostRestart.canRestart = false;
        systemInfo.refetch.mockResolvedValue({ data: { startedAt: '2026-10-06T00:05:00Z' } });
        const user = userEvent.setup();
        render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });
        await user.click(screen.getByRole('button', { name: 'Add extension' }));
        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Extensions.Tmdb');
        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        // Act
        await user.click(await screen.findByRole('button', { name: 'I have restarted Magpie' }));

        // Assert
        expect(screen.queryByText('Restart Magpie to apply pending extension changes.')).not.toBeInTheDocument();
        expect(hostRestart.requestRestart).not.toHaveBeenCalled();
    });

    it('recheck_KeepsTheRestartPrompt_WhenTheHostHasNotRestarted', async () => {
        // Arrange
        hostRestart.canRestart = false;
        const user = userEvent.setup();
        render(<ExtensionsStep onAdvance={vi.fn()} />, { wrapper: Providers });
        await user.click(screen.getByRole('button', { name: 'Add extension' }));
        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Extensions.Tmdb');
        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        // Act
        await user.click(await screen.findByRole('button', { name: 'I have restarted Magpie' }));

        // Assert
        expect(systemInfo.refetch).toHaveBeenCalledOnce();
        expect(screen.getByText('Restart Magpie to apply pending extension changes.')).toBeInTheDocument();
    });

    it('continue_ShowsTheContinueError_WhenARestartFailedBefore', async () => {
        // Arrange
        hostRestart.phase = 'error';
        hostRestart.error = 'Magpie did not come back online. Read the host log.';
        const user = userEvent.setup();
        render(<ExtensionsStep onAdvance={vi.fn().mockRejectedValue(new Error('Temporary settings failure'))} />, {
            wrapper: Providers,
        });

        // Act
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        // Assert
        expect(await screen.findByTestId('setup-error')).toHaveTextContent('Temporary settings failure');
    });

    it('continue_AdvancesTheSetup_WhenContinueIsClicked', async () => {
        // Arrange
        const user = userEvent.setup();
        const onAdvance = vi.fn();
        render(<ExtensionsStep onAdvance={onAdvance} />, { wrapper: Providers });

        // Act
        await user.click(screen.getByRole('button', { name: 'Continue' }));

        // Assert
        expect(onAdvance).toHaveBeenCalledOnce();
    });
});
