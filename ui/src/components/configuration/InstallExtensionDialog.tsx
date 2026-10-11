import { useState, useCallback, useRef, type ReactNode } from 'react';
import { DownloadSimpleIcon, MagnifyingGlassIcon, UploadIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Skeleton } from '@/components/ui/Skeleton';
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
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useExtensionSearch } from '@/hooks/useExtensionManagement';
import { useInstanceName } from '@/hooks/useInstanceName';

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
    const instanceName = useInstanceName();
    const [searchText, setSearchText] = useState('');
    const [packageId, setPackageId] = useState('');
    const [packageVersion, setPackageVersion] = useState('');
    const [installError, setInstallError] = useState<string | null>(null);
    const [installingResult, setInstallingResult] = useState<string | null>(null);
    const fileInputRef = useRef<HTMLInputElement | null>(null);
    const debouncedSearchText = useDebouncedValue(searchText.trim());
    const search = useExtensionSearch(debouncedSearchText, open);
    const results = search.data ?? [];

    const reset = useCallback(() => {
        setSearchText('');
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

    const install = useCallback(
        async (id: string, version: string) => {
            setInstallError(null);
            try {
                await onInstall(id, version);
                reset();
                onOpenChange(false);
            } catch (error) {
                const detail = getErrorMessage(error, 'Check the package details and try again.');
                setInstallError(`Failed to install the package. ${detail}`);
            }
        },
        [onInstall, reset, onOpenChange],
    );

    const installResult = useCallback(
        async (resultKey: string, id: string, version: string) => {
            setInstallingResult(resultKey);
            try {
                await install(id, version);
            } finally {
                setInstallingResult(null);
            }
        },
        [install],
    );

    const handleInstall = useCallback(async () => {
        await install(packageId.trim(), packageVersion.trim());
    }, [packageId, packageVersion, install]);

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

    let searchBody: ReactNode;
    if (search.isLoading) {
        searchBody = (
            <div className="install-extension-dialog__loading">
                <Skeleton />
                <Skeleton />
                <Skeleton />
            </div>
        );
    } else if (search.isError) {
        searchBody = (
            <EmptyState
                icon={<WarningCircleIcon weight="thin" aria-hidden="true" />}
                title="Failed to search extensions"
                description={getErrorMessage(search.error)}
                size="sm"
            />
        );
    } else if (results.length === 0) {
        searchBody = (
            <EmptyState
                icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                title="No extensions found"
                size="sm"
            />
        );
    } else {
        searchBody = (
            <ul className="install-extension-dialog__results" role="list">
                {results.map((result) => {
                    const resultKey = `${result.id}|${result.version}`;
                    const resultName = result.title || result.id;
                    return (
                        <li key={resultKey} className="install-extension-dialog__result">
                            <span className="install-extension-dialog__result-info">
                                <span className="install-extension-dialog__result-title-row">
                                    <span className="install-extension-dialog__result-title">{resultName}</span>
                                    <span className="install-extension-dialog__result-version">v{result.version}</span>
                                </span>
                                {result.description && (
                                    <span className="install-extension-dialog__result-description">
                                        {result.description}
                                    </span>
                                )}
                            </span>
                            <Button
                                className="install-extension-dialog__result-action"
                                variant="secondary"
                                size="sm"
                                aria-label={`Install ${resultName}`}
                                loading={installingResult === resultKey}
                                disabled={isInstalling || isInstallingFromFile}
                                onClick={() => installResult(resultKey, result.id, result.version)}
                                data-action="install-result"
                                data-package-id={result.id}
                            >
                                <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                                Install
                            </Button>
                        </li>
                    );
                })}
            </ul>
        );
    }

    const isInstallingPackage = isInstalling && installingResult === null;

    return (
        <Dialog open={open} onOpenChange={handleOpenChange}>
            <DialogContent data-component="install-extension-dialog">
                <DialogHeader>
                    <DialogTitle>Add Extension</DialogTitle>
                    <DialogDescription>
                        An extension can be installed from NuGet or a local package. The extension loads after the next{' '}
                        {instanceName} restart.
                    </DialogDescription>
                </DialogHeader>

                <DialogBody className="install-extension-dialog__body">
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

                    <div className="install-extension-dialog__search">
                        <Input
                            value={searchText}
                            placeholder="Search extensions"
                            aria-label="Search extensions"
                            leadingIcon={<MagnifyingGlassIcon weight="bold" aria-hidden="true" />}
                            onChange={(e) => setSearchText(e.target.value)}
                            data-testid="install-extension-dialog__search"
                        />
                        {searchBody}
                    </div>

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
                        <div className="install-extension-dialog__field">
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
                        loading={isInstallingFromFile}
                        disabled={isInstallingFromFile || isInstalling}
                        data-action="install-file"
                    >
                        <UploadIcon weight="bold" aria-hidden="true" />
                        Upload .nupkg
                    </Button>
                    <Button
                        variant="primary"
                        onClick={handleInstall}
                        loading={isInstallingPackage}
                        disabled={!packageId.trim() || isInstalling || isInstallingFromFile}
                        data-action="install"
                    >
                        <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                        Install Package
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

InstallExtensionDialog.displayName = 'InstallExtensionDialog';

export default InstallExtensionDialog;
