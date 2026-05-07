/* eslint-disable react-refresh/only-export-components */
import { useState, useCallback, useMemo, useRef } from 'react';
import { Link } from 'react-router-dom';
import {
    PuzzlePiece,
    Power,
    CircleNotch,
    CaretRight,
    Warning,
    WarningCircle,
    DownloadSimple,
    ArrowClockwise,
    Upload,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { Switch } from '@/components/ui/Switch';
import { Input } from '@/components/ui/Input';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { AdminExtensionsSlot } from '@/components/ui/Slot';
import { EmptyState } from '@/components/ui/EmptyState';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import {
    useExtensionActions,
    useExtensionManagement,
    type ExtensionItem,
    type ExtensionUpdateInfo,
} from '@/hooks/useExtensionManagement';
import { getErrorMessage } from '@/lib/errors';

import './ExtensionsPage.css';

export function getStatusVariant(ext: ExtensionItem): BadgeVariant {
    if (ext.disabledReason) {
        return 'unhealthy';
    }
    if (!ext.enabled) {
        return 'unknown';
    }
    return 'healthy';
}

export function getStatusLabel(ext: ExtensionItem): string {
    if (ext.disabledReason) {
        return 'Error';
    }
    if (!ext.enabled) {
        return 'Disabled';
    }
    return 'Enabled';
}

export function ExtensionsPage() {
    useDocumentTitle('Extensions');
    const { extensions, isLoading, error, toggleEnabled, refetch } = useExtensionManagement();
    const actions = useExtensionActions();

    const [togglingIds, setTogglingIds] = useState<Set<string>>(new Set());
    const [packageId, setPackageId] = useState('');
    const [packageVersion, setPackageVersion] = useState('');
    const [updateInfo, setUpdateInfo] = useState<ExtensionUpdateInfo[] | null>(null);
    const fileInputRef = useRef<HTMLInputElement | null>(null);
    const { toast } = useToast();

    const statusCounts = useMemo(() => {
        let enabled = 0;
        let disabled = 0;
        let error = 0;
        for (const ext of extensions) {
            if (ext.disabledReason) {
                error++;
            } else if (!ext.enabled) {
                disabled++;
            } else {
                enabled++;
            }
        }
        return { enabled, disabled, error };
    }, [extensions]);

    const handleToggleEnabled = useCallback(
        async (ext: ExtensionItem, enabled: boolean) => {
            if (ext.builtIn || ext.disabledReason) {
                return;
            }

            setTogglingIds((prev) => new Set([...prev, ext.id]));
            try {
                await toggleEnabled(ext.id, enabled);
                toast({
                    title: enabled ? 'Extension Enabled' : 'Extension Disabled',
                    description: `${ext.name} has been ${enabled ? 'enabled' : 'disabled'}.`,
                    variant: 'success',
                });
            } catch (err) {
                const message = err instanceof Error ? err.message : 'An unexpected error occurred';
                toast({
                    title: enabled ? 'Failed to Enable' : 'Failed to Disable',
                    description: message,
                    variant: 'error',
                });
            } finally {
                setTogglingIds((prev) => {
                    const next = new Set(prev);
                    next.delete(ext.id);
                    return next;
                });
            }
        },
        [toggleEnabled, toast],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleInstall = useCallback(async () => {
        const trimmedId = packageId.trim();
        if (!trimmedId) {
            return;
        }
        try {
            const result = await actions.installExtension({
                packageId: trimmedId,
                version: packageVersion.trim() || undefined,
            });
            toast({
                title: 'Extension Installed',
                description: result.restartRequired
                    ? `${result.extension.name} v${result.extension.version} installed. Restart Magpie to load it.`
                    : `${result.extension.name} v${result.extension.version} installed.`,
                variant: 'success',
            });
            setPackageId('');
            setPackageVersion('');
        } catch (err) {
            toast({
                title: 'Install Failed',
                description: getErrorMessage(err),
                variant: 'error',
            });
        }
    }, [packageId, packageVersion, actions, toast]);

    const handleInstallFromFile = useCallback(
        async (file: File) => {
            try {
                const result = await actions.installExtensionFromFile(file);
                toast({
                    title: 'Extension Installed',
                    description: result.restartRequired
                        ? `${result.extension.name} installed from ${file.name}. Restart Magpie to load it.`
                        : `${result.extension.name} installed from ${file.name}.`,
                    variant: 'success',
                });
            } catch (err) {
                toast({
                    title: 'Install Failed',
                    description: getErrorMessage(err),
                    variant: 'error',
                });
            }
        },
        [actions, toast],
    );

    const handleCheckUpdates = useCallback(async () => {
        try {
            const results = await actions.checkUpdates();
            setUpdateInfo(results);
            const updatesAvailable = results.filter((r) => r.hasUpdate).length;
            toast({
                title: 'Update Check Complete',
                description:
                    updatesAvailable === 0
                        ? 'All managed extensions are up to date.'
                        : `${updatesAvailable} extension${updatesAvailable === 1 ? '' : 's'} have updates available.`,
                variant: 'success',
            });
        } catch (err) {
            toast({
                title: 'Update Check Failed',
                description: getErrorMessage(err),
                variant: 'error',
            });
        }
    }, [actions, toast]);

    const updateInfoByExtensionId = useMemo(() => {
        const map = new Map<string, ExtensionUpdateInfo>();
        if (updateInfo) {
            for (const info of updateInfo) {
                map.set(info.extensionId, info);
            }
        }
        return map;
    }, [updateInfo]);

    if (isLoading && extensions.length === 0) {
        return (
            <div className="extensions-page" data-page="extensions">
                <header className="extensions-page__header">
                    <div className="extensions-page__header-left">
                        <PuzzlePiece
                            weight="duotone"
                            className="extensions-page__icon"
                            aria-hidden="true"
                            data-testid="extensions-page__icon"
                        />
                        <h1 className="extensions-page__title">Extensions</h1>
                    </div>
                </header>
                <div className="extensions-page__loading">
                    <CircleNotch weight="bold" className="extensions-page__spinner" aria-hidden="true" />
                    <p>Loading extensions...</p>
                </div>
            </div>
        );
    }

    if (error && extensions.length === 0) {
        return (
            <div className="extensions-page" data-page="extensions">
                <AdminExtensionsSlot>
                    <header className="extensions-page__header">
                        <div className="extensions-page__header-left">
                            <PuzzlePiece
                                weight="duotone"
                                className="extensions-page__icon"
                                aria-hidden="true"
                                data-testid="extensions-page__icon"
                            />
                            <h1 className="extensions-page__title">Extensions</h1>
                        </div>
                    </header>
                    <EmptyState
                        icon={<WarningCircle weight="duotone" />}
                        title="Failed to load extensions"
                        description={getErrorMessage(error)}
                        action={
                            <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                Retry
                            </Button>
                        }
                    />
                </AdminExtensionsSlot>
            </div>
        );
    }

    return (
        <div className="extensions-page" data-page="extensions">
            <AdminExtensionsSlot>
                <PullToRefresh onRefresh={handleRefresh} className="extensions-page__pull-to-refresh">
                    <header className="extensions-page__header">
                        <div className="extensions-page__header-left">
                            <PuzzlePiece
                                weight="duotone"
                                className="extensions-page__icon"
                                aria-hidden="true"
                                data-testid="extensions-page__icon"
                            />
                            <h1 className="extensions-page__title">Extensions</h1>
                            <span className="page-count extensions-page__count" data-testid="extensions-page__count">
                                {extensions.length}
                            </span>
                        </div>
                    </header>

                    <div className="list-toolbar__summary" aria-label="Extension status summary">
                        <Badge variant="healthy" size="sm" badgeStyle="outline" dot count={statusCounts.enabled}>
                            Enabled
                        </Badge>
                        <Badge variant="unknown" size="sm" badgeStyle="outline" dot count={statusCounts.disabled}>
                            Disabled
                        </Badge>
                        <Badge variant="unhealthy" size="sm" badgeStyle="outline" dot count={statusCounts.error}>
                            Error
                        </Badge>
                    </div>

                    <section className="extensions-page__install" aria-label="Install extension" data-section="install">
                        <h2 className="extensions-page__section-title">
                            <DownloadSimple weight="duotone" aria-hidden="true" />
                            Install Extension
                        </h2>
                        <div className="extensions-page__install-row">
                            <Input
                                type="text"
                                value={packageId}
                                placeholder="NuGet package id (e.g. Magpie.Extensions.Foo)"
                                onChange={(e) => setPackageId(e.target.value)}
                                aria-label="NuGet package id"
                                data-testid="extensions-page__package-id"
                            />
                            <Input
                                type="text"
                                value={packageVersion}
                                placeholder="Version (optional)"
                                onChange={(e) => setPackageVersion(e.target.value)}
                                aria-label="Package version"
                                data-testid="extensions-page__package-version"
                            />
                            <Button
                                variant="primary"
                                size="sm"
                                onClick={handleInstall}
                                disabled={!packageId.trim() || actions.isInstalling}
                                data-action="install"
                            >
                                {actions.isInstalling ? (
                                    <CircleNotch
                                        weight="bold"
                                        className="extensions-page__spinner"
                                        aria-hidden="true"
                                    />
                                ) : (
                                    <DownloadSimple weight="bold" aria-hidden="true" />
                                )}
                                Install
                            </Button>
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => fileInputRef.current?.click()}
                                disabled={actions.isInstallingFromFile}
                                data-action="install-file"
                            >
                                {actions.isInstallingFromFile ? (
                                    <CircleNotch
                                        weight="bold"
                                        className="extensions-page__spinner"
                                        aria-hidden="true"
                                    />
                                ) : (
                                    <Upload weight="bold" aria-hidden="true" />
                                )}
                                Upload .nupkg
                            </Button>
                            <input
                                ref={fileInputRef}
                                type="file"
                                accept=".nupkg,.zip"
                                hidden
                                onChange={(e) => {
                                    const file = e.target.files?.[0];
                                    if (file) {
                                        void handleInstallFromFile(file);
                                    }
                                    e.target.value = '';
                                }}
                                data-testid="extensions-page__file-input"
                            />
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={handleCheckUpdates}
                                disabled={actions.isCheckingUpdates}
                                data-action="check-updates"
                            >
                                {actions.isCheckingUpdates ? (
                                    <CircleNotch
                                        weight="bold"
                                        className="extensions-page__spinner"
                                        aria-hidden="true"
                                    />
                                ) : (
                                    <ArrowClockwise weight="bold" aria-hidden="true" />
                                )}
                                Check for Updates
                            </Button>
                        </div>
                        <p className="extensions-page__install-help">
                            Installs run on the next host restart. Update or uninstall actions also take effect after
                            restart.
                        </p>
                    </section>

                    <div className="extensions-page__list" role="list" aria-label="Installed extensions">
                        {extensions.length === 0 ? (
                            <EmptyState
                                icon={<PuzzlePiece weight="duotone" />}
                                title="No extensions installed"
                                description="No extensions are currently installed."
                            />
                        ) : (
                            extensions.map((ext) => (
                                <ExtensionCard
                                    key={ext.id}
                                    extension={ext}
                                    isToggling={togglingIds.has(ext.id)}
                                    onToggleEnabled={handleToggleEnabled}
                                    updateInfo={updateInfoByExtensionId.get(ext.id) ?? null}
                                />
                            ))
                        )}
                    </div>
                </PullToRefresh>
            </AdminExtensionsSlot>
        </div>
    );
}

ExtensionsPage.displayName = 'ExtensionsPage';

interface ExtensionCardProps {
    extension: ExtensionItem;
    isToggling: boolean;
    onToggleEnabled: (ext: ExtensionItem, enabled: boolean) => void;
    updateInfo: ExtensionUpdateInfo | null;
}

function ExtensionCard({ extension, isToggling, onToggleEnabled, updateInfo }: ExtensionCardProps) {
    const statusVariant = getStatusVariant(extension);
    const statusLabel = getStatusLabel(extension);
    const canToggle = !extension.builtIn && !extension.disabledReason;

    return (
        <div
            className={`extensions-page__card${extension.enabled ? '' : ' extensions-page__card--disabled'}${extension.disabledReason ? ' extensions-page__card--error' : ''}`}
            role="listitem"
            data-magpie-extension-id={extension.id}
        >
            <Link to={`/admin/extensions/${extension.id}`} className="extensions-page__card-link">
                <div className="extensions-page__card-info">
                    <span className="extensions-page__card-name">{extension.name}</span>
                    <span className="extensions-page__card-version">v{extension.version}</span>
                </div>
                <div className="extensions-page__card-badges">
                    <Badge variant={statusVariant} size="sm" dot>
                        {statusLabel}
                    </Badge>
                    {extension.builtIn && (
                        <Badge variant="info" size="sm">
                            Built-in
                        </Badge>
                    )}
                    {!extension.builtIn && extension.managed && (
                        <Badge variant="info" size="sm" badgeStyle="outline">
                            Managed
                        </Badge>
                    )}
                    {updateInfo?.hasUpdate && (
                        <Badge variant="warning" size="sm" data-testid="extensions-page__card-update-badge">
                            Update {updateInfo.latestVersion}
                        </Badge>
                    )}
                    {extension.disabledReason && (
                        <Warning weight="bold" className="extensions-page__card-warning" aria-label="Has errors" />
                    )}
                </div>
                <CaretRight weight="bold" className="extensions-page__card-chevron" aria-hidden="true" />
            </Link>
            {canToggle && (
                <div className="extensions-page__card-toggle" onClick={(e) => e.stopPropagation()}>
                    {isToggling ? (
                        <CircleNotch weight="bold" className="extensions-page__spinner" aria-hidden="true" />
                    ) : (
                        <Power weight="bold" className="extensions-page__toggle-icon" aria-hidden="true" />
                    )}
                    <Switch
                        checked={extension.enabled}
                        onCheckedChange={(checked) => onToggleEnabled(extension, checked)}
                        aria-label={`${extension.enabled ? 'Disable' : 'Enable'} ${extension.name}`}
                        data-action="toggle-enabled"
                        data-target={extension.id}
                        disabled={isToggling}
                    />
                </div>
            )}
        </div>
    );
}

export default ExtensionsPage;
