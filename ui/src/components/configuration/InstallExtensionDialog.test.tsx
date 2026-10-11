import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet } from '@/lib/api';

import { InstallExtensionDialog, type InstallExtensionDialogProps } from './InstallExtensionDialog';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
}));

let queryClient: QueryClient;
let props: InstallExtensionDialogProps;

beforeEach(() => {
    queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    props = {
        open: true,
        onOpenChange: vi.fn(),
        onInstall: vi.fn().mockResolvedValue(undefined),
        onInstallFromFile: vi.fn().mockResolvedValue(undefined),
        isInstalling: false,
        isInstallingFromFile: false,
    };
    vi.mocked(apiGet).mockResolvedValue([
        { id: 'Magpie.Extensions.Tmdb', version: '1.2.0', title: 'TMDB', description: 'Movie metadata' },
        { id: 'Magpie.Extensions.Books', version: '1.1.0', title: null, description: null },
    ]);
});

afterEach(() => {
    vi.useRealTimers();
});

describe('InstallExtensionDialog search', () => {
    it('search_ListsTheFoundExtensions_WhenTheDialogOpens', async () => {
        // Arrange
        // Act
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );

        // Assert
        expect(await screen.findByText('TMDB')).toBeInTheDocument();
        expect(screen.getByText('v1.2.0')).toBeInTheDocument();
        expect(screen.getByText('Movie metadata')).toBeInTheDocument();
        expect(screen.getByText('Magpie.Extensions.Books')).toBeInTheDocument();
        expect(screen.getByText('v1.1.0')).toBeInTheDocument();
        expect(apiGet).toHaveBeenCalledWith('/extensions/search?q=');
    });

    it('search_SendsTheTypedText_WhenTheTextChanges', async () => {
        // Arrange
        vi.useFakeTimers({ shouldAdvanceTime: true });
        const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );
        await screen.findByText('TMDB');

        // Act
        await user.type(screen.getByRole('textbox', { name: 'Search extensions' }), ' tmdb ');
        await vi.advanceTimersByTimeAsync(300);

        // Assert
        await waitFor(() => {
            expect(apiGet).toHaveBeenLastCalledWith('/extensions/search?q=tmdb');
        });
    });

    it('search_ShowsNoResultsMessage_WhenNoExtensionMatches', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([]);

        // Act
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );

        // Assert
        expect(await screen.findByText('No extensions found')).toBeInTheDocument();
    });

    it('search_ShowsFailureMessage_WhenTheRequestFails', async () => {
        // Arrange
        vi.mocked(apiGet).mockRejectedValue(new Error('The feed is unreachable.'));

        // Act
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );

        // Assert
        expect(await screen.findByText('Failed to search extensions')).toBeInTheDocument();
        expect(screen.getByText('The feed is unreachable.')).toBeInTheDocument();
    });

    it('install_InstallsTheVersionOfTheRow_WhenInstallIsClicked', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );
        await screen.findByText('TMDB');

        // Act
        await user.click(screen.getByRole('button', { name: 'Install TMDB' }));

        // Assert
        expect(props.onInstall).toHaveBeenCalledWith('Magpie.Extensions.Tmdb', '1.2.0');
        await waitFor(() => {
            expect(props.onOpenChange).toHaveBeenCalledWith(false);
        });
    });

    it('install_KeepsTheDialogOpenWithAnError_WhenInstallingAResultFails', async () => {
        // Arrange
        const user = userEvent.setup();
        props.onInstall = vi.fn().mockRejectedValue(new Error('Package Magpie.Extensions.Tmdb was not found.'));
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );
        await screen.findByText('TMDB');

        // Act
        await user.click(screen.getByRole('button', { name: 'Install TMDB' }));

        // Assert
        const error = await screen.findByTestId('install-extension-dialog__error');
        expect(error).toHaveTextContent('Failed to install the package. Package Magpie.Extensions.Tmdb was not found.');
        expect(props.onOpenChange).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Install TMDB' })).toBeEnabled();
    });

    it('install_MarksOnlyTheClickedRowBusy_WhenResultsShareAnId', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([
            { id: 'Magpie.Extensions.Books', version: '1.0.1', title: null, description: null },
            { id: 'Magpie.Extensions.Books', version: '1.0.0', title: null, description: null },
        ]);
        const user = userEvent.setup();
        props.onInstall = vi.fn().mockReturnValue(new Promise(() => {}));
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );
        const buttons = await screen.findAllByRole('button', { name: 'Install Magpie.Extensions.Books' });

        // Act
        await user.click(buttons[0]);

        // Assert
        expect(buttons[0]).toHaveAttribute('aria-busy', 'true');
        expect(buttons[1]).toHaveAttribute('aria-busy', 'false');
    });

    it('install_InstallsTheVersionOfTheClickedRow_WhenResultsShareAnId', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue([
            { id: 'Magpie.Extensions.Books', version: '1.0.1', title: null, description: null },
            { id: 'Magpie.Extensions.Books', version: '1.0.0', title: null, description: null },
        ]);
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );
        const buttons = await screen.findAllByRole('button', { name: 'Install Magpie.Extensions.Books' });

        // Act
        await user.click(buttons[1]);

        // Assert
        expect(props.onInstall).toHaveBeenCalledWith('Magpie.Extensions.Books', '1.0.0');
    });
});

describe('InstallExtensionDialog', () => {
    it('keeps a failed package install open with a recoverable error', async () => {
        const user = userEvent.setup();
        const onInstall = vi.fn().mockRejectedValue(new Error('Package Magpie.Missing was not found.'));
        props.onInstall = onInstall;
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );

        const packageId = screen.getByLabelText('NuGet Package ID');
        await user.type(packageId, 'Magpie.Missing');
        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        const error = await screen.findByTestId('install-extension-dialog__error');
        expect(error).toHaveTextContent('Installation failed');
        expect(error).toHaveTextContent('Failed to install the package. Package Magpie.Missing was not found.');
        expect(screen.getByRole('dialog', { name: 'Add Extension' })).toBeInTheDocument();
        expect(packageId).toHaveValue('Magpie.Missing');
        expect(screen.getByRole('button', { name: 'Install Package' })).toBeEnabled();
        expect(onInstall).toHaveBeenCalledWith('Magpie.Missing', '');
    });

    it('clears the package error when installation is retried', async () => {
        const user = userEvent.setup();
        const onInstall = vi
            .fn()
            .mockRejectedValueOnce(new Error('Package was not found.'))
            .mockResolvedValueOnce(undefined);
        const onOpenChange = vi.fn();
        props.onInstall = onInstall;
        props.onOpenChange = onOpenChange;
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );

        await user.type(screen.getByLabelText('NuGet Package ID'), 'Magpie.Retry');
        await user.click(screen.getByRole('button', { name: 'Install Package' }));
        expect(await screen.findByTestId('install-extension-dialog__error')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Install Package' }));

        await waitFor(() => {
            expect(screen.queryByTestId('install-extension-dialog__error')).not.toBeInTheDocument();
        });
        expect(onInstall).toHaveBeenCalledTimes(2);
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('keeps a failed package upload open with the same recoverable error', async () => {
        const user = userEvent.setup();
        const onInstallFromFile = vi.fn().mockRejectedValue(new Error('The archive is not a valid package.'));
        props.onInstallFromFile = onInstallFromFile;
        render(
            <QueryClientProvider client={queryClient}>
                <InstallExtensionDialog {...props} />
            </QueryClientProvider>,
        );
        const file = new File(['package'], 'Magpie.Local.nupkg', { type: 'application/octet-stream' });

        await user.upload(screen.getByTestId('install-extension-dialog__file-input'), file);

        const error = await screen.findByTestId('install-extension-dialog__error');
        expect(error).toHaveTextContent('Installation failed');
        expect(error).toHaveTextContent('Failed to install the package. The archive is not a valid package.');
        expect(screen.getByRole('dialog', { name: 'Add Extension' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Upload .nupkg' })).toBeEnabled();
        expect(onInstallFromFile).toHaveBeenCalledWith(file);
    });
});
