import { useState, useMemo, useCallback } from 'react';
import {
    Database,
    Trash,
    CloudArrowUp,
    ClockCounterClockwise,
    CircleNotch,
    Calendar,
    HardDrives,
    Gear,
    FloppyDisk,
    WarningCircle,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import '@/styles/card-actions.css';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
} from '@/components/ui/Dialog';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';
import { Switch } from '@/components/ui/Switch';
import { Label } from '@/components/ui/Label';
import { Input } from '@/components/ui/Input';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { AdminBackupsSlot } from '@/components/ui/Slot';
import { EmptyState } from '@/components/ui/EmptyState';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { useBackups, type BackupItem, type BackupType } from '@/hooks/useBackups';
import { useBackupSettings, type BackupInterval, type BackupSettings } from '@/hooks/useBackupSettings';
import { formatRelativeTime, isRelativeTimeString } from '@/lib/formatTime';
import { getErrorMessage } from '@/lib/errors';
import { formatSize } from './formatSize';

import './BackupsPage.css';

const intervalLabels: Record<BackupInterval, string> = {
    daily: 'Daily',
    weekly: 'Weekly',
    biweekly: 'Every 2 weeks',
    monthly: 'Monthly',
};

function formatDate(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
    });
}

function formatDateTime(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
    });
}

const typeVariantMap: Record<BackupType, 'healthy' | 'unknown'> = {
    manual: 'healthy',
    scheduled: 'unknown',
};

const typeLabels: Record<BackupType, string> = {
    manual: 'Manual',
    scheduled: 'Scheduled',
};

export function BackupsPage() {
    useDocumentTitle('Backups');
    const {
        backups: apiBackups,
        isLoading,
        error,
        createBackup,
        isCreating,
        deleteBackup,
        isDeleting,
        restoreBackup,
        isRestoring,
        refetch,
    } = useBackups();

    const [restoreDialogOpen, setRestoreDialogOpen] = useState(false);
    const [backupToRestore, setBackupToRestore] = useState<BackupItem | null>(null);
    const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
    const [backupToDelete, setBackupToDelete] = useState<BackupItem | null>(null);

    const { settings, saveSettings, isSaving: isSavingSettings } = useBackupSettings();

    const [localSettings, setLocalSettings] = useState<BackupSettings | null>(null);
    const [settingsChanged, setSettingsChanged] = useState(false);

    useUnsavedChangesGuard(settingsChanged);

    const currentSettings = localSettings ?? settings;

    const { toast } = useToast();

    const totalBackups = apiBackups.length;
    const totalSize = useMemo(() => apiBackups.reduce((sum, b) => sum + b.sizeBytes, 0), [apiBackups]);

    const handleCreateBackup = useCallback(async () => {
        try {
            await createBackup({});

            toast({
                title: 'Backup Created',
                description: 'Successfully created backup',
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({
                title: 'Backup Failed',
                description: message,
                variant: 'error',
            });
        }
    }, [createBackup, toast]);

    const handleRestoreClick = useCallback((backup: BackupItem) => {
        setBackupToRestore(backup);
        setRestoreDialogOpen(true);
    }, []);

    const handleRestoreConfirm = useCallback(async () => {
        if (!backupToRestore) return;

        try {
            await restoreBackup(backupToRestore.id);

            toast({
                title: 'Restore Initiated',
                description: `Database will be restored from "${backupToRestore.name}". The application will restart.`,
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({
                title: 'Restore Failed',
                description: message,
                variant: 'error',
            });
        } finally {
            setRestoreDialogOpen(false);
            setBackupToRestore(null);
        }
    }, [backupToRestore, restoreBackup, toast]);

    const handleDeleteClick = useCallback((backup: BackupItem) => {
        setBackupToDelete(backup);
        setDeleteDialogOpen(true);
    }, []);

    const handleDeleteConfirm = useCallback(async () => {
        if (!backupToDelete) return;

        try {
            await deleteBackup(backupToDelete.id);

            toast({
                title: 'Backup Deleted',
                description: `Successfully deleted "${backupToDelete.name}"`,
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({
                title: 'Delete Failed',
                description: message,
                variant: 'error',
            });
        } finally {
            setDeleteDialogOpen(false);
            setBackupToDelete(null);
        }
    }, [backupToDelete, deleteBackup, toast]);

    const handleSettingsChange = useCallback(
        <K extends keyof BackupSettings>(key: K, value: BackupSettings[K]) => {
            setLocalSettings((prev) => ({ ...(prev ?? settings), [key]: value }));
            setSettingsChanged(true);
        },
        [settings],
    );

    const handleSaveSettings = useCallback(async () => {
        if (!localSettings) return;

        try {
            await saveSettings(localSettings);

            setLocalSettings(null);
            setSettingsChanged(false);

            toast({
                title: 'Settings Saved',
                description: 'Backup settings have been updated successfully.',
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'An unexpected error occurred';
            toast({
                title: 'Save Failed',
                description: message,
                variant: 'error',
            });
        }
    }, [localSettings, saveSettings, toast]);

    const createBackupActions: QuickAction[] = useMemo(
        () => [
            {
                id: 'create-backup',
                label: 'Create Backup',
                icon: <CloudArrowUp weight="bold" />,
                onClick: handleCreateBackup,
            },
        ],
        [handleCreateBackup],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    if (error && apiBackups.length === 0) {
        return (
            <div className="backups-page" data-page="backups">
                <header className="backups-page__header">
                    <div className="backups-page__header-left">
                        <Database
                            weight="duotone"
                            className="backups-page__icon"
                            aria-hidden="true"
                            data-testid="backups-page__icon"
                        />
                        <h1 className="backups-page__title">Backups</h1>
                    </div>
                </header>
                <EmptyState
                    icon={<WarningCircle weight="duotone" />}
                    title="Failed to load backups"
                    description={getErrorMessage(error)}
                    action={
                        <Button variant="secondary" size="sm" onClick={() => refetch()}>
                            Retry
                        </Button>
                    }
                />
            </div>
        );
    }

    return (
        <div className="backups-page" data-page="backups">
            <AdminBackupsSlot>
                <PullToRefresh onRefresh={handleRefresh} className="backups-page__pull-to-refresh">
                    <header className="backups-page__header">
                        <div className="backups-page__header-left">
                            <Database
                                weight="duotone"
                                className="backups-page__icon"
                                aria-hidden="true"
                                data-testid="backups-page__icon"
                            />
                            <h1 className="backups-page__title">Backups</h1>
                            <span className="page-count backups-page__count" data-testid="backups-page__count">
                                {totalBackups}
                            </span>
                        </div>
                        <div className="backups-page__header-actions">
                            <Button
                                variant="primary"
                                size="sm"
                                data-action="create-backup"
                                onClick={handleCreateBackup}
                                disabled={isCreating}
                            >
                                {isCreating ? (
                                    <CircleNotch weight="bold" className="backups-page__spinner" aria-hidden="true" />
                                ) : (
                                    <CloudArrowUp weight="bold" aria-hidden="true" />
                                )}
                                <span className="backups-page__create-label">
                                    {isCreating ? 'Creating...' : 'Create Backup'}
                                </span>
                            </Button>
                        </div>
                    </header>

                    <div className="backups-page__summary">
                        <div className="backups-page__summary-item">
                            <HardDrives weight="duotone" className="backups-page__summary-icon" aria-hidden="true" />
                            <span className="backups-page__summary-label">Total Size</span>
                            <span className="backups-page__summary-value" data-testid="backups-page__summary-value">
                                {formatSize(totalSize)}
                            </span>
                        </div>
                    </div>

                    <section
                        className="backups-page__settings"
                        aria-labelledby="backup-settings-title"
                        data-component="backup-settings"
                    >
                        <header className="backups-page__settings-header">
                            <Gear weight="duotone" className="backups-page__settings-icon" aria-hidden="true" />
                            <h2 id="backup-settings-title" className="backups-page__settings-title">
                                Schedule Settings
                            </h2>
                        </header>

                        <div className="backups-page__settings-content">
                            <div className="backups-page__settings-row">
                                <div className="backups-page__settings-field">
                                    <Label htmlFor="backup-enabled">Automatic Backups</Label>
                                    <p id="backup-enabled-hint" className="backups-page__settings-hint">
                                        Enable automatic database backups on a schedule
                                    </p>
                                </div>
                                <Switch
                                    id="backup-enabled"
                                    checked={currentSettings.enabled}
                                    onCheckedChange={(checked) => handleSettingsChange('enabled', checked)}
                                    aria-describedby="backup-enabled-hint"
                                    data-setting="enabled"
                                />
                            </div>

                            <div className="backups-page__settings-row">
                                <div className="backups-page__settings-field">
                                    <Label htmlFor="backup-interval">Backup Interval</Label>
                                    <p className="backups-page__settings-hint">How often to create automatic backups</p>
                                </div>
                                <Select
                                    value={currentSettings.interval}
                                    onValueChange={(value) => handleSettingsChange('interval', value as BackupInterval)}
                                    disabled={!currentSettings.enabled}
                                >
                                    <SelectTrigger
                                        id="backup-interval"
                                        className="backups-page__settings-select"
                                        aria-label="Select backup interval"
                                        data-setting="interval"
                                    >
                                        {intervalLabels[currentSettings.interval]}
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value="daily">Daily</SelectItem>
                                        <SelectItem value="weekly">Weekly</SelectItem>
                                        <SelectItem value="biweekly">Every 2 weeks</SelectItem>
                                        <SelectItem value="monthly">Monthly</SelectItem>
                                    </SelectContent>
                                </Select>
                            </div>

                            <div className="backups-page__settings-row">
                                <div className="backups-page__settings-field">
                                    <Label htmlFor="backup-retention">Keep Last</Label>
                                    <p className="backups-page__settings-hint">Number of automatic backups to retain</p>
                                </div>
                                <div className="backups-page__settings-input-group">
                                    <Input
                                        id="backup-retention"
                                        type="number"
                                        min={1}
                                        max={365}
                                        value={currentSettings.retention}
                                        onChange={(e) =>
                                            handleSettingsChange(
                                                'retention',
                                                Math.max(1, parseInt(e.target.value, 10) || 1),
                                            )
                                        }
                                        disabled={!currentSettings.enabled}
                                        className="backups-page__settings-number"
                                        data-setting="retention"
                                    />
                                    <span className="backups-page__settings-suffix">backups</span>
                                </div>
                            </div>

                            <div className="backups-page__settings-actions">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={handleSaveSettings}
                                    disabled={!settingsChanged || isSavingSettings}
                                    data-action="save-settings"
                                >
                                    {isSavingSettings ? (
                                        <>
                                            <CircleNotch
                                                weight="bold"
                                                className="backups-page__spinner"
                                                aria-hidden="true"
                                            />
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
                        </div>
                    </section>

                    <div className="backups-page__list" role="list" aria-label="Backups">
                        {isLoading && apiBackups.length === 0 ? (
                            <div className="backups-page__loading">
                                <CircleNotch weight="bold" className="backups-page__spinner" aria-hidden="true" />
                                <p>Loading backups...</p>
                            </div>
                        ) : apiBackups.length === 0 ? (
                            <EmptyState
                                icon={<Database weight="duotone" />}
                                title="No backups available"
                                description="Create a backup to protect your data"
                            />
                        ) : (
                            apiBackups.map((backup) => (
                                <BackupRow
                                    key={backup.id}
                                    backup={backup}
                                    onRestore={handleRestoreClick}
                                    onDelete={handleDeleteClick}
                                />
                            ))
                        )}
                    </div>
                </PullToRefresh>

                <QuickActionsButton actions={createBackupActions} title="Create Backup" />

                <Dialog open={restoreDialogOpen} onOpenChange={setRestoreDialogOpen}>
                    <DialogContent>
                        <DialogHeader>
                            <DialogTitle>Restore Backup</DialogTitle>
                            <DialogDescription>
                                Are you sure you want to restore the database from this backup? This will replace all
                                current data and the application will restart.
                            </DialogDescription>
                        </DialogHeader>
                        {backupToRestore && (
                            <div className="backups-page__dialog-info">
                                <div className="backups-page__dialog-row">
                                    <span className="backups-page__dialog-label">Backup:</span>
                                    <span className="backups-page__dialog-value">{backupToRestore.name}</span>
                                </div>
                                <div className="backups-page__dialog-row">
                                    <span className="backups-page__dialog-label">Created:</span>
                                    <span className="backups-page__dialog-value">
                                        {formatDateTime(backupToRestore.createdAt)}
                                    </span>
                                </div>
                                <div className="backups-page__dialog-row">
                                    <span className="backups-page__dialog-label">Size:</span>
                                    <span className="backups-page__dialog-value">
                                        {formatSize(backupToRestore.sizeBytes)}
                                    </span>
                                </div>
                            </div>
                        )}
                        <DialogFooter>
                            <Button
                                variant="ghost"
                                onClick={() => setRestoreDialogOpen(false)}
                                disabled={isRestoring}
                                data-action="cancel"
                            >
                                Cancel
                            </Button>
                            <Button
                                variant="destructive"
                                onClick={handleRestoreConfirm}
                                disabled={isRestoring}
                                data-action="confirm-restore"
                            >
                                {isRestoring ? (
                                    <>
                                        <CircleNotch
                                            weight="bold"
                                            className="backups-page__spinner"
                                            aria-hidden="true"
                                        />
                                        Restoring...
                                    </>
                                ) : (
                                    <>
                                        <ClockCounterClockwise weight="bold" aria-hidden="true" />
                                        Restore
                                    </>
                                )}
                            </Button>
                        </DialogFooter>
                    </DialogContent>
                </Dialog>

                <Dialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
                    <DialogContent>
                        <DialogHeader>
                            <DialogTitle>Delete Backup</DialogTitle>
                            <DialogDescription>
                                Are you sure you want to delete this backup? This action cannot be undone.
                            </DialogDescription>
                        </DialogHeader>
                        {backupToDelete && (
                            <div className="backups-page__dialog-info">
                                <div className="backups-page__dialog-row">
                                    <span className="backups-page__dialog-label">Backup:</span>
                                    <span className="backups-page__dialog-value">{backupToDelete.name}</span>
                                </div>
                                <div className="backups-page__dialog-row">
                                    <span className="backups-page__dialog-label">Created:</span>
                                    <span className="backups-page__dialog-value">
                                        {formatDateTime(backupToDelete.createdAt)}
                                    </span>
                                </div>
                            </div>
                        )}
                        <DialogFooter>
                            <Button
                                variant="ghost"
                                onClick={() => setDeleteDialogOpen(false)}
                                disabled={isDeleting}
                                data-action="cancel"
                            >
                                Cancel
                            </Button>
                            <Button
                                variant="destructive"
                                onClick={handleDeleteConfirm}
                                disabled={isDeleting}
                                data-action="confirm-delete"
                            >
                                {isDeleting ? (
                                    <>
                                        <CircleNotch
                                            weight="bold"
                                            className="backups-page__spinner"
                                            aria-hidden="true"
                                        />
                                        Deleting...
                                    </>
                                ) : (
                                    <>
                                        <Trash weight="bold" aria-hidden="true" />
                                        Delete
                                    </>
                                )}
                            </Button>
                        </DialogFooter>
                    </DialogContent>
                </Dialog>
            </AdminBackupsSlot>
        </div>
    );
}

BackupsPage.displayName = 'BackupsPage';

interface BackupRowProps {
    backup: BackupItem;
    onRestore: (backup: BackupItem) => void;
    onDelete: (backup: BackupItem) => void;
}

function BackupRow({ backup, onRestore, onDelete }: BackupRowProps) {
    return (
        <div className="backups-page__item" role="listitem" data-backup-id={backup.id}>
            <div className="backups-page__item-main">
                <div className="backups-page__item-info">
                    <div className="backups-page__item-header">
                        <span className="backups-page__item-name">{backup.name}</span>
                        <Badge variant={typeVariantMap[backup.type]}>{typeLabels[backup.type]}</Badge>
                    </div>
                    <div className="backups-page__item-meta">
                        <span
                            className="backups-page__item-date"
                            data-testid="backups-page__item-date"
                            title={formatDateTime(backup.createdAt)}
                        >
                            <Calendar weight="duotone" aria-hidden="true" />
                            <span className="backups-page__item-date-full">{formatDate(backup.createdAt)}</span>
                            {isRelativeTimeString(formatRelativeTime(backup.createdAt)) && (
                                <span className="backups-page__item-date-relative">
                                    ({formatRelativeTime(backup.createdAt)})
                                </span>
                            )}
                        </span>
                        <span className="backups-page__item-size" data-testid="backups-page__item-size">
                            <HardDrives weight="duotone" aria-hidden="true" />
                            {formatSize(backup.sizeBytes)}
                        </span>
                    </div>
                </div>
                <div className="backups-page__item-actions card-actions">
                    <Tooltip content="Restore">
                        <button
                            type="button"
                            className="backups-page__action-btn card-actions__btn"
                            data-testid="backups-page__action-btn"
                            onClick={() => onRestore(backup)}
                            aria-label={`Restore from ${backup.name}`}
                            data-action="restore"
                            data-target={backup.id}
                        >
                            <ClockCounterClockwise weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="backups-page__action-btn backups-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="backups-page__action-btn"
                            onClick={() => onDelete(backup)}
                            aria-label={`Delete ${backup.name}`}
                            data-action="delete"
                            data-target={backup.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

export default BackupsPage;
