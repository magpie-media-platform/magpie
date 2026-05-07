import { useState, useCallback, useEffect } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
    PuzzlePiece,
    ArrowLeft,
    CircleNotch,
    Warning,
    Power,
    ArrowClockwise,
    FloppyDisk,
    GearSix,
    Package,
    Trash,
    DownloadSimple,
    WarningCircle,
    ClockCounterClockwise,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { Switch } from '@/components/ui/Switch';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { AdminExtensionSettingsSlot } from '@/components/ui/Slot';
import { EmptyState } from '@/components/ui/EmptyState';
import {
    useExtension,
    useExtensionManagement,
    useExtensionActions,
    useExtensionVersions,
    type ExtensionItem,
} from '@/hooks/useExtensionManagement';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useExtensionSettingsGroups, useUpdateExtensionSettingsGroup, settingsToRecord } from '@/hooks/useSettings';
import { formatRelativeTime } from '@/lib/formatTime';
import { getStatusVariant, getStatusLabel } from './ExtensionsPage';

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
                    description: `${extension.name} has been ${enabled ? 'enabled' : 'disabled'}.`,
                    variant: 'success',
                });
            } catch (err) {
                const message = err instanceof Error ? err.message : 'An unexpected error occurred';
                toast({ title: 'Failed', description: message, variant: 'error' });
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
                description: `${extension.name} has been re-enabled.`,
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({ title: 'Failed to Enable', description: message, variant: 'error' });
        }
    }, [extension, toggleEnabled, toast]);

    if (isLoading) {
        return (
            <div className="extension-detail" data-page="extension-detail">
                <div className="extension-detail__loading">
                    <CircleNotch weight="bold" className="extension-detail__spinner" aria-hidden="true" />
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
                        <ArrowLeft weight="bold" aria-hidden="true" />
                        Extensions
                    </Link>
                </nav>
                <EmptyState
                    icon={<WarningCircle weight="duotone" />}
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

    const canToggle = !extension.builtIn && !extension.disabledReason;
    const statusVariant = getStatusVariant(extension);
    const statusLabel = getStatusLabel(extension);

    return (
        <div className="extension-detail" data-page="extension-detail">
            <PullToRefresh onRefresh={handleRefresh} className="extension-detail__pull-to-refresh">
                <nav className="extension-detail__breadcrumb" aria-label="Breadcrumb">
                    <Link to="/admin/extensions" className="extension-detail__breadcrumb-link">
                        <ArrowLeft weight="bold" aria-hidden="true" />
                        Extensions
                    </Link>
                    <span className="extension-detail__breadcrumb-sep" aria-hidden="true">
                        /
                    </span>
                    <span className="extension-detail__breadcrumb-current">{extension.name}</span>
                </nav>

                <header className="extension-detail__header" data-testid="extension-detail__header">
                    <div className="extension-detail__header-left">
                        <PuzzlePiece weight="duotone" className="extension-detail__icon" aria-hidden="true" />
                        <h1 className="extension-detail__title">{extension.name}</h1>
                        <span className="extension-detail__version">v{extension.version}</span>
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
                    </div>
                    {canToggle && (
                        <div className="extension-detail__header-toggle">
                            {isToggling && (
                                <CircleNotch weight="bold" className="extension-detail__spinner" aria-hidden="true" />
                            )}
                            <Power weight="bold" className="extension-detail__toggle-icon" aria-hidden="true" />
                            <Switch
                                checked={extension.enabled}
                                onCheckedChange={handleToggleEnabled}
                                aria-label={`${extension.enabled ? 'Disable' : 'Enable'} ${extension.name}`}
                                disabled={isToggling}
                            />
                        </div>
                    )}
                </header>

                {extension.disabledReason && (
                    <div className="extension-detail__error-banner">
                        <Warning weight="bold" aria-hidden="true" />
                        <span>{extension.disabledReason}</span>
                        <Button variant="ghost" size="sm" onClick={handleRetryEnable}>
                            <ArrowClockwise weight="bold" aria-hidden="true" />
                            Retry
                        </Button>
                    </div>
                )}

                <OverviewSection extension={extension} />

                {extension.managed && <ManagementSection extension={extension} actions={actions} />}

                <VersionHistorySection extension={extension} />

                {extension.enabled && <SettingsSection extensionId={extension.id} />}
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
                    <dd>{extension.builtIn ? 'Built-in' : extension.managed ? 'Managed (NuGet)' : 'Third-party'}</dd>
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
                const message = err instanceof Error ? err.message : 'An unexpected error occurred';
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
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({ title: 'Update Failed', description: message, variant: 'error' });
        }
    }, [actions, extension.id, extension.name, toast]);

    const handleUninstall = useCallback(async () => {
        try {
            await actions.uninstallExtension(extension.id);
            toast({
                title: 'Extension Uninstalled',
                description: `${extension.name} removed. Restart Magpie to fully unload it.`,
                variant: 'success',
            });
            navigate('/admin/extensions');
        } catch (err) {
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({ title: 'Uninstall Failed', description: message, variant: 'error' });
        }
    }, [actions, extension.id, extension.name, navigate, toast]);

    return (
        <section className="extension-detail__section" data-section="management">
            <h2 className="extension-detail__section-title">
                <Package weight="duotone" aria-hidden="true" />
                Package Management
            </h2>

            <div className="extension-detail__management">
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
                            Replaces the staged assembly. Restart Magpie to load the new version.
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
                                <CircleNotch weight="bold" className="extension-detail__spinner" aria-hidden="true" />
                            ) : (
                                <DownloadSimple weight="bold" aria-hidden="true" />
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
                                <span className="extension-detail__mono">{extension.packageRepository}</span>
                            </div>
                        )}
                    </div>
                )}

                {!extension.builtIn && (
                    <div className="extension-detail__management-danger">
                        <Button
                            variant="destructive"
                            size="sm"
                            onClick={handleUninstall}
                            disabled={actions.isUninstalling}
                            data-action="uninstall"
                        >
                            {actions.isUninstalling ? (
                                <CircleNotch weight="bold" className="extension-detail__spinner" aria-hidden="true" />
                            ) : (
                                <Trash weight="bold" aria-hidden="true" />
                            )}
                            Uninstall Extension
                        </Button>
                        <p className="extension-detail__management-help">
                            Removes the extension from disk and the database. Restart Magpie to fully unload it.
                        </p>
                    </div>
                )}
            </div>
        </section>
    );
}

interface VersionHistorySectionProps {
    extension: ExtensionItem;
}

function VersionHistorySection({ extension }: VersionHistorySectionProps) {
    const { versions, isLoading } = useExtensionVersions(extension.name);

    if (isLoading || versions.length === 0) {
        return null;
    }

    return (
        <section className="extension-detail__section" data-section="version-history">
            <h2 className="extension-detail__section-title">
                <ClockCounterClockwise weight="duotone" aria-hidden="true" />
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
        </section>
    );
}

interface SettingsSectionProps {
    extensionId: string;
}

function SettingsSection({ extensionId }: SettingsSectionProps) {
    const { groups, isLoading } = useExtensionSettingsGroups(extensionId);
    const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null);
    const [values, setValues] = useState<Record<string, string>>({});
    const [originalValues, setOriginalValues] = useState<Record<string, string>>({});

    const effectiveGroupId = selectedGroupId ?? groups[0]?.id ?? null;

    useEffect(() => {
        const group = effectiveGroupId ? groups.find((g) => g.id === effectiveGroupId) : undefined;
        if (group) {
            const record = settingsToRecord(group.settings);
            setValues(record);
            setOriginalValues(record);
        }
    }, [groups, effectiveGroupId]);

    const selectedGroup = groups.find((g) => g.id === effectiveGroupId) ?? null;
    const hasChanges = Object.keys(values).some((key) => values[key] !== originalValues[key]);

    if (isLoading) {
        return (
            <section className="extension-detail__section" data-section="settings">
                <h2 className="extension-detail__section-title">
                    <GearSix weight="duotone" aria-hidden="true" />
                    Settings
                </h2>
                <div className="extension-detail__loading">
                    <CircleNotch weight="bold" className="extension-detail__spinner" aria-hidden="true" />
                    <p>Loading settings...</p>
                </div>
            </section>
        );
    }

    if (groups.length === 0) {
        return null;
    }

    return (
        <AdminExtensionSettingsSlot>
            <section className="extension-detail__section" data-section="settings">
                <h2 className="extension-detail__section-title">
                    <GearSix weight="duotone" aria-hidden="true" />
                    Settings
                </h2>

                {groups.length > 1 && (
                    <div className="extension-detail__settings-tabs" role="tablist">
                        {groups.map((group) => (
                            <button
                                key={group.id}
                                type="button"
                                role="tab"
                                id={`settings-tab-${group.id}`}
                                aria-controls={`settings-panel-${group.id}`}
                                className={`extension-detail__settings-tab${effectiveGroupId === group.id ? ' extension-detail__settings-tab--active' : ''}`}
                                aria-selected={effectiveGroupId === group.id}
                                onClick={() => setSelectedGroupId(group.id)}
                            >
                                {group.name}
                            </button>
                        ))}
                    </div>
                )}

                {selectedGroup && (
                    <div
                        className="extension-detail__settings-form"
                        role="tabpanel"
                        id={`settings-panel-${selectedGroup.id}`}
                        aria-labelledby={`settings-tab-${selectedGroup.id}`}
                    >
                        <div className="extension-detail__settings-fields">
                            {selectedGroup.settings.map((setting) => (
                                <div key={setting.key} className="extension-detail__form-field">
                                    <Label htmlFor={`setting-${setting.key}`}>{setting.key}</Label>
                                    <Input
                                        id={`setting-${setting.key}`}
                                        type="text"
                                        value={values[setting.key] ?? ''}
                                        onChange={(e) =>
                                            setValues((prev) => ({ ...prev, [setting.key]: e.target.value }))
                                        }
                                    />
                                </div>
                            ))}
                        </div>
                        <SettingsSaveButton
                            extensionId={extensionId}
                            groupName={selectedGroup.name}
                            values={values}
                            originalValues={originalValues}
                            hasChanges={hasChanges}
                            onSaved={(newValues) => setOriginalValues(newValues)}
                        />
                    </div>
                )}
            </section>
        </AdminExtensionSettingsSlot>
    );
}

interface SettingsSaveButtonProps {
    extensionId: string;
    groupName: string;
    values: Record<string, string>;
    originalValues: Record<string, string>;
    hasChanges: boolean;
    onSaved: (values: Record<string, string>) => void;
}

function SettingsSaveButton({
    extensionId,
    groupName,
    values,
    originalValues,
    hasChanges,
    onSaved,
}: SettingsSaveButtonProps) {
    const { updateSettings, isUpdating } = useUpdateExtensionSettingsGroup(extensionId, groupName);

    const handleSave = useCallback(async () => {
        const existingKeys = new Set(Object.keys(originalValues));
        try {
            await updateSettings({ values, existingKeys });
            onSaved({ ...values });
        } catch {
            /* empty */
        }
    }, [updateSettings, values, originalValues, onSaved]);

    return (
        <div className="extension-detail__settings-actions">
            <Button variant="primary" size="sm" onClick={handleSave} disabled={!hasChanges || isUpdating}>
                {isUpdating ? (
                    <>
                        <CircleNotch weight="bold" className="extension-detail__spinner" aria-hidden="true" />
                        Saving...
                    </>
                ) : (
                    <>
                        <FloppyDisk weight="bold" aria-hidden="true" />
                        Save Settings
                    </>
                )}
            </Button>
        </div>
    );
}

export default ExtensionDetailPage;
