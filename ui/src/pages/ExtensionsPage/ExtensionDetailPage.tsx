import { useState, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
    PuzzlePieceIcon,
    ArrowLeftIcon,
    CircleNotchIcon,
    WarningIcon,
    PowerIcon,
    ArrowClockwiseIcon,
    PackageIcon,
    TrashIcon,
    DownloadSimpleIcon,
    WarningCircleIcon,
    ClockCounterClockwiseIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { Switch } from '@/components/ui/Switch';
import { Label } from '@/components/ui/Label';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { EmptyState } from '@/components/ui/EmptyState';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog';
import {
    useExtension,
    useExtensionManagement,
    useExtensionActions,
    useExtensionVersions,
    type ExtensionItem,
} from '@/hooks/useExtensionManagement';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';
import { getStatusVariant, getStatusLabel } from './extensionStatus';
import { ExtensionSettingsSection } from './ExtensionSettingsSection';

import './ExtensionDetailPage.css';

export function ExtensionDetailPage() {
    useDocumentTitle('Extension');
    const { extensionId } = useParams<{ extensionId: string }>();
    const { extension, isLoading, error, refetch } = useExtension(extensionId);
    const { toggleEnabled } = useExtensionManagement();
    const actions = useExtensionActions();
    const { toast } = useToast();
    const [isToggling, setIsToggling] = useState(false);

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleToggleEnabled = useCallback(
        async (enabled: boolean) => {
            if (!extension) return;
            setIsToggling(true);
            try {
                await toggleEnabled(extension.id, enabled);
                toast({
                    title: enabled ? 'Extension Enabled' : 'Extension Disabled',
                    description: `${extension.name} will be ${enabled ? 'enabled' : 'disabled'} after the next restart.`,
                    variant: 'success',
                });
            } catch (err) {
                const message = getErrorMessage(err, 'Failed to update the extension.');
                toast({
                    title: `Failed to ${enabled ? 'enable' : 'disable'} extension`,
                    description: message,
                    variant: 'error',
                });
            } finally {
                setIsToggling(false);
            }
        },
        [extension, toggleEnabled, toast],
    );

    const handleRetryEnable = useCallback(async () => {
        if (!extension) return;
        try {
            await toggleEnabled(extension.id, true);
            toast({
                title: 'Extension Enabled',
                description: `Magpie enabled ${extension.name} again.`,
                variant: 'success',
            });
        } catch (err) {
            const message = getErrorMessage(err, 'Failed to enable the extension.');
            toast({ title: 'Failed to Enable', description: message, variant: 'error' });
        }
    }, [extension, toggleEnabled, toast]);

    if (isLoading) {
        return (
            <div className="extension-detail" data-page="extension-detail">
                <div className="extension-detail__loading">
                    <CircleNotchIcon weight="bold" className="extension-detail__spinner" aria-hidden="true" />
                    <p>Loading extension...</p>
                </div>
            </div>
        );
    }

    if (error || !extension) {
        return (
            <div className="extension-detail" data-page="extension-detail">
                <nav className="extension-detail__breadcrumb" aria-label="Breadcrumb">
                    <Link to="/admin/extensions" className="extension-detail__breadcrumb-link">
                        <ArrowLeftIcon weight="bold" aria-hidden="true" />
                        Extensions
                    </Link>
                </nav>
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title={error ? 'Failed to load extension' : 'Extension not found'}
                    description={error?.message}
                    action={
                        <Button variant="secondary" size="sm" onClick={() => refetch()}>
                            Retry
                        </Button>
                    }
                />
            </div>
        );
    }

    const canToggle = !extension.disabledReason;
    const statusVariant = getStatusVariant(extension);
    const statusLabel = getStatusLabel(extension);

    return (
        <div className="extension-detail" data-page="extension-detail">
            <PullToRefresh onRefresh={handleRefresh} className="extension-detail__pull-to-refresh">
                <nav className="extension-detail__breadcrumb" aria-label="Breadcrumb">
                    <Link to="/admin/extensions" className="extension-detail__breadcrumb-link">
                        <ArrowLeftIcon weight="bold" aria-hidden="true" />
                        Extensions
                    </Link>
                    <span className="extension-detail__breadcrumb-sep" aria-hidden="true">
                        /
                    </span>
                    <span className="extension-detail__breadcrumb-current">{extension.name}</span>
                </nav>

                <p className="extension-detail__description" data-testid="extension-detail__description">
                    Extension details, status, and version history.
                </p>

                <header className="extension-detail__header" data-testid="extension-detail__header">
                    <div className="extension-detail__header-left">
                        <PuzzlePieceIcon weight="duotone" className="extension-detail__icon" aria-hidden="true" />
                        <h1 className="extension-detail__title">{extension.name}</h1>
                        <span className="extension-detail__version">v{extension.version}</span>
                        <Badge variant={statusVariant} size="sm" dot>
                            {statusLabel}
                        </Badge>
                        {extension.managed && (
                            <Badge variant="info" size="sm" badgeStyle="outline">
                                Managed
                            </Badge>
                        )}
                    </div>
                    {canToggle && (
                        <div className="extension-detail__header-toggle">
                            <div className="extension-detail__header-toggle-control">
                                {isToggling && (
                                    <CircleNotchIcon
                                        weight="bold"
                                        className="extension-detail__spinner"
                                        aria-hidden="true"
                                    />
                                )}
                                <PowerIcon weight="bold" className="extension-detail__toggle-icon" aria-hidden="true" />
                                <Switch
                                    checked={extension.enabled}
                                    onCheckedChange={handleToggleEnabled}
                                    aria-label={`${extension.enabled ? 'Disable' : 'Enable'} ${extension.name}`}
                                    disabled={isToggling}
                                />
                            </div>
                            <p className="extension-detail__header-toggle-hint" data-testid="extension-toggle-hint">
                                Takes effect after restart
                            </p>
                        </div>
                    )}
                </header>

                {extension.disabledReason && (
                    <div className="extension-detail__error-banner">
                        <WarningIcon weight="bold" aria-hidden="true" />
                        <span>{extension.disabledReason}</span>
                        <Button variant="ghost" size="sm" onClick={handleRetryEnable}>
                            <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                            Retry
                        </Button>
                    </div>
                )}

                <OverviewSection extension={extension} />

                <ExtensionSettingsSection extensionId={extension.id} />

                <ManagementSection extension={extension} actions={actions} />

                <VersionHistorySection extension={extension} />
            </PullToRefresh>
        </div>
    );
}

ExtensionDetailPage.displayName = 'ExtensionDetailPage';

interface OverviewSectionProps {
    extension: ExtensionItem;
}

function OverviewSection({ extension }: OverviewSectionProps) {
    return (
        <section className="extension-detail__section" data-section="overview">
            <div className="extension-detail__overview">
                {extension.description && <p className="extension-detail__description">{extension.description}</p>}
                <dl className="extension-detail__fields">
                    {extension.author && (
                        <>
                            <dt>Author</dt>
                            <dd>{extension.author}</dd>
                        </>
                    )}
                    <dt>Type</dt>
                    <dd>{extension.managed ? 'Managed (NuGet)' : 'Third-party'}</dd>
                    {extension.nuGetId && (
                        <>
                            <dt>Package</dt>
                            <dd className="extension-detail__mono">{extension.nuGetId}</dd>
                        </>
                    )}
                    <dt>Assembly</dt>
                    <dd className="extension-detail__mono">{extension.filePath}</dd>
                    <dt>Installed</dt>
                    <dd>
                        {new Date(extension.createdAt).toLocaleDateString(undefined, {
                            year: 'numeric',
                            month: 'long',
                            day: 'numeric',
                        })}
                    </dd>
                    {extension.lastUpdateCheck && (
                        <>
                            <dt>Last Update Check</dt>
                            <dd>{formatRelativeTime(extension.lastUpdateCheck)}</dd>
                        </>
                    )}
                </dl>
            </div>
        </section>
    );
}

interface ManagementSectionProps {
    extension: ExtensionItem;
    actions: ReturnType<typeof useExtensionActions>;
}

function ManagementSection({ extension, actions }: ManagementSectionProps) {
    const { toast } = useToast();
    const navigate = useNavigate();
    const [uninstallConfirmOpen, setUninstallConfirmOpen] = useState(false);

    const handleAutoUpgradeChange = useCallback(
        async (checked: boolean) => {
            try {
                await actions.patchExtension(extension.id, { autoUpgrade: { value: checked } });
                toast({
                    title: checked ? 'Auto-upgrade Enabled' : 'Auto-upgrade Disabled',
                    description: `${extension.name} will ${checked ? 'now' : 'no longer'} auto-upgrade.`,
                    variant: 'success',
                });
            } catch (err) {
                const message = getErrorMessage(err, 'Failed to update automatic upgrades.');
                toast({ title: 'Failed to Update', description: message, variant: 'error' });
            }
        },
        [extension.id, extension.name, actions, toast],
    );

    const handleUpdate = useCallback(async () => {
        try {
            const result = await actions.updateExtension(extension.id);
            toast({
                title: 'Extension Updated',
                description: result.restartRequired
                    ? `${extension.name} updated to v${result.extension.version}. Restart Magpie to load it.`
                    : `${extension.name} updated to v${result.extension.version}.`,
                variant: 'success',
            });
        } catch (err) {
            const message = getErrorMessage(err, 'Failed to update the extension.');
            toast({ title: 'Update Failed', description: message, variant: 'error' });
        }
    }, [actions, extension.id, extension.name, toast]);

    const handleConfirmUninstall = useCallback(async () => {
        try {
            await actions.uninstallExtension(extension.id);
            setUninstallConfirmOpen(false);
            toast({
                title: 'Extension Uninstalled',
                description: `${extension.name} removed. Restart Magpie to fully unload it.`,
                variant: 'success',
            });
            void navigate('/admin/extensions');
        } catch (err) {
            const message = getErrorMessage(err, 'Failed to uninstall the extension.');
            toast({ title: 'Uninstall Failed', description: message, variant: 'error' });
        }
    }, [actions, extension.id, extension.name, navigate, toast]);

    return (
        <>
            <section className="extension-detail__section" data-section="management">
                <h2 className="extension-detail__section-title">
                    <PackageIcon weight="duotone" aria-hidden="true" />
                    Package Management
                </h2>

                <div className="extension-detail__management">
                    {extension.managed && (
                        <>
                            <div className="extension-detail__management-row">
                                <div className="extension-detail__management-label">
                                    <Label>Auto-upgrade</Label>
                                    <p className="extension-detail__management-help">
                                        Automatically install new versions when available
                                    </p>
                                </div>
                                <Switch
                                    checked={extension.autoUpgrade}
                                    onCheckedChange={handleAutoUpgradeChange}
                                    aria-label="Toggle auto-upgrade"
                                />
                            </div>

                            <div className="extension-detail__management-row extension-detail__management-row--updates">
                                <div className="extension-detail__management-label">
                                    <Label>Updates</Label>
                                    <p className="extension-detail__management-help">
                                        Installs the extension update. The new version loads after the next restart.
                                    </p>
                                </div>
                                <div className="extension-detail__management-actions">
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        onClick={handleUpdate}
                                        disabled={actions.isUpdating}
                                        data-action="update"
                                    >
                                        {actions.isUpdating ? (
                                            <CircleNotchIcon
                                                weight="bold"
                                                className="extension-detail__spinner"
                                                aria-hidden="true"
                                            />
                                        ) : (
                                            <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                                        )}
                                        Update Now
                                    </Button>
                                </div>
                            </div>

                            {(extension.nuGetId || extension.packageRepository) && (
                                <div className="extension-detail__nuget-config">
                                    {extension.nuGetId && (
                                        <div className="extension-detail__form-field">
                                            <Label>NuGet Package ID</Label>
                                            <span className="extension-detail__mono">{extension.nuGetId}</span>
                                        </div>
                                    )}
                                    {extension.packageRepository && (
                                        <div className="extension-detail__form-field">
                                            <Label>Package Repository</Label>
                                            <span className="extension-detail__mono">
                                                {extension.packageRepository}
                                            </span>
                                        </div>
                                    )}
                                </div>
                            )}
                        </>
                    )}

                    <div className="extension-detail__management-danger">
                        <Button
                            variant="destructive"
                            size="sm"
                            onClick={() => setUninstallConfirmOpen(true)}
                            disabled={actions.isUninstalling}
                            data-action="uninstall"
                        >
                            {actions.isUninstalling ? (
                                <CircleNotchIcon
                                    weight="bold"
                                    className="extension-detail__spinner"
                                    aria-hidden="true"
                                />
                            ) : (
                                <TrashIcon weight="bold" aria-hidden="true" />
                            )}
                            Uninstall Extension
                        </Button>
                        <p className="extension-detail__management-help">
                            Removes the extension's files and registration immediately. Restart Magpie to unload its
                            currently running code.
                        </p>
                    </div>
                </div>
            </section>

            <DeleteConfirmDialog
                open={uninstallConfirmOpen}
                onConfirm={() => void handleConfirmUninstall()}
                onCancel={() => setUninstallConfirmOpen(false)}
                itemName={extension.name}
                itemType="Extension"
                title="Uninstall Extension"
                description={<>Uninstall &quot;{extension.name}&quot;? Removal takes effect after Magpie restarts.</>}
                isDeleting={actions.isUninstalling}
                confirmLabel="Uninstall"
                confirmBusyLabel="Uninstalling..."
            />
        </>
    );
}

interface VersionHistorySectionProps {
    extension: ExtensionItem;
}

function VersionHistorySection({ extension }: VersionHistorySectionProps) {
    const { versions, isLoading } = useExtensionVersions(extension.packageId);

    return (
        <section className="extension-detail__section" data-section="version-history">
            <h2 className="extension-detail__section-title">
                <ClockCounterClockwiseIcon weight="duotone" aria-hidden="true" />
                Version History
            </h2>
            <div className="extension-detail__versions" role="list">
                <div className="extension-detail__version-row extension-detail__version-row--current" role="listitem">
                    <span className="extension-detail__version-number">
                        v{extension.version}
                        <Badge variant="healthy" size="sm">
                            Current
                        </Badge>
                    </span>
                    <span className="extension-detail__version-date">
                        {new Date(extension.createdAt).toLocaleDateString(undefined, {
                            year: 'numeric',
                            month: 'short',
                            day: 'numeric',
                        })}
                    </span>
                </div>
                {versions.map((v) => (
                    <div key={v.id} className="extension-detail__version-row" role="listitem">
                        <span className="extension-detail__version-number">v{v.version}</span>
                        <span className="extension-detail__version-date">
                            {new Date(v.createdAt).toLocaleDateString(undefined, {
                                year: 'numeric',
                                month: 'short',
                                day: 'numeric',
                            })}
                        </span>
                    </div>
                ))}
            </div>
            {isLoading ? (
                <p className="extension-detail__section-note" role="status">
                    Loading version history...
                </p>
            ) : (
                versions.length === 0 && (
                    <p className="extension-detail__section-note">This is the only installed version.</p>
                )
            )}
        </section>
    );
}

export default ExtensionDetailPage;
