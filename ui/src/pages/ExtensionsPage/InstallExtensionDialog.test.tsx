import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { InstallExtensionDialog, type InstallExtensionDialogProps } from './InstallExtensionDialog';

function renderDialog(overrides: Partial<InstallExtensionDialogProps> = {}) {
    const props: InstallExtensionDialogProps = {
        open: true,
        onOpenChange: vi.fn(),
        onInstall: vi.fn().mockResolvedValue(undefined),
        onInstallFromFile: vi.fn().mockResolvedValue(undefined),
        isInstalling: false,
        isInstallingFromFile: false,
        ...overrides,
    };

    render(<InstallExtensionDialog {...props} />);
    return props;
}

describe('InstallExtensionDialog', () => {
    it('keeps a failed package install open with a recoverable error', async () => {
        const user = userEvent.setup();
        const onInstall = vi.fn().mockRejectedValue(new Error('Package Magpie.Missing was not found.'));
        renderDialog({ onInstall });

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
        renderDialog({ onInstall, onOpenChange });

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
        renderDialog({ onInstallFromFile });
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
