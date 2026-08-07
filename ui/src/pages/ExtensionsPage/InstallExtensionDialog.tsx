import { useState, useCallback, useRef, type ReactNode } from 'react';
import { CircleNotchIcon, DownloadSimpleIcon, UploadIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogBody,
    DialogFooter,
} from '@/components/ui/Dialog';
import { getErrorMessage } from '@/lib/errors';

import './InstallExtensionDialog.css';

export interface InstallExtensionDialogProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    onInstall: (packageId: string, version: string) => Promise<void>;
    onInstallFromFile: (file: File) => Promise<void>;
    isInstalling: boolean;
    isInstallingFromFile: boolean;
}

export function InstallExtensionDialog({
    open,
    onOpenChange,
    onInstall,
    onInstallFromFile,
    isInstalling,
    isInstallingFromFile,
}: InstallExtensionDialogProps): ReactNode {
    const [packageId, setPackageId] = useState('');
    const [packageVersion, setPackageVersion] = useState('');
    const [installError, setInstallError] = useState<string | null>(null);
    const fileInputRef = useRef<HTMLInputElement | null>(null);

    const reset = useCallback(() => {
        setPackageId('');
        setPackageVersion('');
        setInstallError(null);
    }, []);

    const handleOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen) {
                reset();
            }
            onOpenChange(nextOpen);
        },
        [onOpenChange, reset],
    );

    const handleInstall = useCallback(async () => {
        const trimmedId = packageId.trim();
        if (!trimmedId) {
            return;
        }
        setInstallError(null);
        try {
            await onInstall(trimmedId, packageVersion.trim());
            reset();
            onOpenChange(false);
        } catch (error) {
            const detail = getErrorMessage(error, 'Check the package details and try again.');
            setInstallError(`Failed to install the package. ${detail}`);
        }
    }, [packageId, packageVersion, onInstall, reset, onOpenChange]);

    const handleFile = useCallback(
        async (file: File) => {
            setInstallError(null);
            try {
                await onInstallFromFile(file);
                reset();
                onOpenChange(false);
            } catch (error) {
                const detail = getErrorMessage(error, 'Check the package archive and try again.');
                setInstallError(`Failed to install the package. ${detail}`);
            }
        },
        [onInstallFromFile, onOpenChange, reset],
    );

    return (
        <Dialog open={open} onOpenChange={handleOpenChange}>
            <DialogContent data-component="install-extension-dialog">
                <DialogHeader>
                    <DialogTitle>Add Extension</DialogTitle>
                    <DialogDescription>
                        An extension can be installed from NuGet or a local package. The extension loads after the next
                        Magpie restart.
                    </DialogDescription>
                </DialogHeader>

                <DialogBody>
                    {installError && (
                        <Alert
                            variant="error"
                            title="Installation failed"
                            icon={<WarningCircleIcon weight="fill" />}
                            data-testid="install-extension-dialog__error"
                        >
                            {installError}
                        </Alert>
                    )}

                    <div className="install-extension-dialog__fields">
                        <div className="install-extension-dialog__field">
                            <Label htmlFor="install-package-id">NuGet Package ID</Label>
                            <Input
                                id="install-package-id"
                                type="text"
                                value={packageId}
                                placeholder="Magpie.Extensions.Foo"
                                onChange={(e) => setPackageId(e.target.value)}
                                data-testid="install-extension-dialog__package-id"
                            />
                        </div>
                        <div className="install-extension-dialog__field install-extension-dialog__field--version">
                            <Label htmlFor="install-package-version">Version</Label>
                            <Input
                                id="install-package-version"
                                type="text"
                                value={packageVersion}
                                placeholder="Optional"
                                onChange={(e) => setPackageVersion(e.target.value)}
                                data-testid="install-extension-dialog__package-version"
                            />
                        </div>
                    </div>

                    <input
                        ref={fileInputRef}
                        type="file"
                        accept=".nupkg,.zip"
                        hidden
                        onChange={(e) => {
                            const file = e.target.files?.[0];
                            if (file) {
                                void handleFile(file);
                            }
                            e.target.value = '';
                        }}
                        data-testid="install-extension-dialog__file-input"
                    />
                </DialogBody>

                <DialogFooter>
                    <Button
                        variant="secondary"
                        onClick={() => fileInputRef.current?.click()}
                        disabled={isInstallingFromFile || isInstalling}
                        data-action="install-file"
                    >
                        {isInstallingFromFile ? (
                            <CircleNotchIcon
                                weight="bold"
                                className="install-extension-dialog__spinner"
                                aria-hidden="true"
                            />
                        ) : (
                            <UploadIcon weight="bold" aria-hidden="true" />
                        )}
                        <span>{isInstallingFromFile ? 'Uploading...' : 'Upload .nupkg'}</span>
                    </Button>
                    <Button
                        variant="primary"
                        onClick={handleInstall}
                        disabled={!packageId.trim() || isInstalling || isInstallingFromFile}
                        data-action="install"
                    >
                        {isInstalling ? (
                            <CircleNotchIcon
                                weight="bold"
                                className="install-extension-dialog__spinner"
                                aria-hidden="true"
                            />
                        ) : (
                            <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                        )}
                        <span>{isInstalling ? 'Installing...' : 'Install Package'}</span>
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

InstallExtensionDialog.displayName = 'InstallExtensionDialog';

export default InstallExtensionDialog;
