import { useState, useMemo, useCallback, useRef } from 'react';
import {
    DatabaseIcon,
    TrashIcon,
    CloudArrowUpIcon,
    ClockCounterClockwiseIcon,
    CircleNotchIcon,
    CalendarIcon,
    HardDrivesIcon,
    WarningCircleIcon,
    WrenchIcon,
    DownloadSimpleIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import '@/styles/card-actions.css';
import '@/styles/list-item-card.css';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
} from '@/components/ui/Dialog';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';
import { AdminBackupsSlot } from '@/components/ui/Slot';
import { EmptyState } from '@/components/ui/EmptyState';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useBackups, type BackupItem, type BackupType } from '@/hooks/useBackups';
import { formatUtcDate, formatUtcDateTime } from '@/lib/dateFormatting';
import { formatRelativeTime, isRelativeTimeString } from '@/lib/formatTime';
import { getErrorMessage } from '@/lib/errors';
import { formatBackupSize } from '@/lib/formatSize';

import './BackupsPage.css';

const typeVariantMap: Record<BackupType, 'healthy' | 'unknown'> = {
    manual: 'healthy',
    scheduled: 'unknown',
};

const typeLabels: Record<BackupType, string> = {
    manual: 'Manual',
    scheduled: 'Scheduled',
};

export function BackupsPage() {
    useDocumentTitle('Database');
    const {
        backups: apiBackups,
        capabilities,
        backupRestoreSupported,
        isCapabilityLoading,
        isLoading,
        error,
        createBackup,
        isCreating,
        deleteBackup,
        isDeleting,
        restoreBackup,
        isRestoring,
        downloadBackup,
        refetch,
    } = useBackups();

    const [restoreDialogOpen, setRestoreDialogOpen] = useState(false);
    const [backupToRestore, setBackupToRestore] = useState<BackupItem | null>(null);
    const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
    const [backupToDelete, setBackupToDelete] = useState<BackupItem | null>(null);
    const deleteSubmitPendingRef = useRef(false);
    const [isDeleteSubmitPending, setIsDeleteSubmitPending] = useState(false);

    const { toast } = useToast();

    const totalSize = useMemo(() => apiBackups.reduce((sum, b) => sum + b.sizeBytes, 0), [apiBackups]);
    const canManageBackups = !isCapabilityLoading && backupRestoreSupported;
    const unsupportedReason = capabilities?.provider
        ? `Database backup and restore are not supported for ${capabilities.provider}.`
        : 'Database backup and restore are not supported for the configured provider.';

    const handleCreateBackup = useCallback(async () => {
        try {
            await createBackup({});

            toast({
                title: 'Backup Queued',
                description: 'The backup will appear here when its job completes.',
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to queue the backup.';
            toast({
                title: 'Unable to Queue Backup',
                description: message,
                variant: 'error',
            });
        }
    }, [createBackup, toast]);

    const handleRestoreClick = useCallback((backup: BackupItem) => {
        setBackupToRestore(backup);
        setRestoreDialogOpen(true);
    }, []);

    const handleDownload = useCallback(
        async (backup: BackupItem) => {
            try {
                await downloadBackup(backup.id);
            } catch (err) {
                toast({
                    title: 'Download Failed',
                    description: getErrorMessage(err),
                    variant: 'error',
                });
            }
        },
        [downloadBackup, toast],
    );

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
            const message = err instanceof Error ? err.message : 'Failed to restore the backup.';
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
        if (!backupToDelete || deleteSubmitPendingRef.current) return;

        deleteSubmitPendingRef.current = true;
        setIsDeleteSubmitPending(true);
        const backup = backupToDelete;

        try {
            await deleteBackup(backup.id);

            toast({
                title: 'Backup Deleted',
                description: `Successfully deleted "${backup.name}"`,
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to delete the backup.';
            toast({
                title: 'Delete Failed',
                description: message,
                variant: 'error',
            });
        } finally {
            deleteSubmitPendingRef.current = false;
            setIsDeleteSubmitPending(false);
            setDeleteDialogOpen(false);
            setBackupToDelete(null);
        }
    }, [backupToDelete, deleteBackup, toast]);

    const createBackupActions: QuickAction[] = useMemo(
        () => [
            {
                id: 'create-backup',
                label: 'Create Backup',
                icon: <CloudArrowUpIcon weight="bold" />,
                onClick: handleCreateBackup,
                disabled: isCreating,
            },
        ],
        [handleCreateBackup, isCreating],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const backupsDescription = 'Database backup and maintenance.';
    const isDeletePending = isDeleting || isDeleteSubmitPending;

    if (error && apiBackups.length === 0) {
        return (
            <div className="backups-page" data-page="backups">
                <PageHeader
                    className="backups-page__header"
                    title="Database"
                    description={backupsDescription}
                    icon={<DatabaseIcon weight="duotone" aria-hidden="true" data-testid="backups-page__icon" />}
                />
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
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

    if (!isCapabilityLoading && !backupRestoreSupported) {
        return (
            <div className="backups-page" data-page="backups">
                <PageHeader
                    className="backups-page__header"
                    title="Database"
                    description={backupsDescription}
                    icon={<DatabaseIcon weight="duotone" aria-hidden="true" data-testid="backups-page__icon" />}
                />
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Database backups unavailable"
                    description={unsupportedReason}
                />
            </div>
        );
    }

    return (
        <div className="backups-page" data-page="backups">
            <AdminBackupsSlot>
                <PullToRefresh onRefresh={handleRefresh} className="backups-page__pull-to-refresh">
                    <PageHeader
                        className="backups-page__header"
                        title="Database"
                        description={backupsDescription}
                        icon={<DatabaseIcon weight="duotone" aria-hidden="true" data-testid="backups-page__icon" />}
                        actions={
                            canManageBackups ? (
                                <>
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        data-action="create-backup"
                                        onClick={handleCreateBackup}
                                        disabled={isCreating}
                                        aria-label={isCreating ? 'Queueing...' : 'Create Backup'}
                                    >
                                        {isCreating ? (
                                            <CircleNotchIcon
                                                weight="bold"
                                                className="backups-page__spinner"
                                                aria-hidden="true"
                                            />
                                        ) : (
                                            <CloudArrowUpIcon weight="bold" aria-hidden="true" />
                                        )}
                                        <span className="backups-page__create-label">
                                            {isCreating ? 'Queueing...' : 'Create Backup'}
                                        </span>
                                    </Button>
                                </>
                            ) : undefined
                        }
                    />

                    <div className="backups-page__summary">
                        <div className="backups-page__summary-item">
                            <HardDrivesIcon
                                weight="duotone"
                                className="backups-page__summary-icon"
                                aria-hidden="true"
                            />
                            <span className="backups-page__summary-label">Total Size</span>
                            <span className="backups-page__summary-value" data-testid="backups-page__summary-value">
                                {formatBackupSize(totalSize)}
                            </span>
                        </div>
                        {capabilities?.optimizeSupported && (
                            <div className="backups-page__summary-item">
                                <WrenchIcon
                                    weight="duotone"
                                    className="backups-page__summary-icon"
                                    aria-hidden="true"
                                />
                                <span className="backups-page__summary-label">Scheduled Optimization</span>
                                <span
                                    className="backups-page__summary-value"
                                    data-testid="backups-page__last-optimized"
                                >
                                    {capabilities.lastOptimizedAt
                                        ? formatUtcDateTime(capabilities.lastOptimizedAt)
                                        : 'Never'}
                                </span>
                            </div>
                        )}
                    </div>

                    <div className="backups-page__list" role="list" aria-label="Backups">
                        {isLoading && apiBackups.length === 0 ? (
                            <div className="backups-page__loading">
                                <CircleNotchIcon weight="bold" className="backups-page__spinner" aria-hidden="true" />
                                <p>Loading backups...</p>
                            </div>
                        ) : apiBackups.length === 0 ? (
                            <EmptyState
                                icon={<DatabaseIcon weight="duotone" />}
                                title="No backups available"
                                description="Create a backup to protect your data"
                            />
                        ) : (
                            apiBackups.map((backup) => (
                                <BackupRow
                                    key={backup.id}
                                    backup={backup}
                                    onDownload={handleDownload}
                                    onRestore={handleRestoreClick}
                                    onDelete={handleDeleteClick}
                                />
                            ))
                        )}
                    </div>
                </PullToRefresh>

                {canManageBackups && <QuickActionsButton actions={createBackupActions} title="Create Backup" />}

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
                                        {formatUtcDateTime(backupToRestore.createdAt)}
                                    </span>
                                </div>
                                <div className="backups-page__dialog-row">
                                    <span className="backups-page__dialog-label">Size:</span>
                                    <span className="backups-page__dialog-value">
                                        {formatBackupSize(backupToRestore.sizeBytes)}
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
                                        <CircleNotchIcon
                                            weight="bold"
                                            className="backups-page__spinner"
                                            aria-hidden="true"
                                        />
                                        Restoring...
                                    </>
                                ) : (
                                    <>
                                        <ClockCounterClockwiseIcon weight="bold" aria-hidden="true" />
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
                                        {formatUtcDateTime(backupToDelete.createdAt)}
                                    </span>
                                </div>
                            </div>
                        )}
                        <DialogFooter>
                            <Button
                                variant="ghost"
                                onClick={() => setDeleteDialogOpen(false)}
                                disabled={isDeletePending}
                                data-action="cancel"
                            >
                                Cancel
                            </Button>
                            <Button
                                variant="destructive"
                                onClick={handleDeleteConfirm}
                                disabled={isDeletePending}
                                data-action="confirm-delete"
                            >
                                {isDeletePending ? (
                                    <>
                                        <CircleNotchIcon
                                            weight="bold"
                                            className="backups-page__spinner"
                                            aria-hidden="true"
                                        />
                                        Deleting...
                                    </>
                                ) : (
                                    <>
                                        <TrashIcon weight="bold" aria-hidden="true" />
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
    onDownload: (backup: BackupItem) => void;
    onDelete: (backup: BackupItem) => void;
}

function BackupRow({ backup, onRestore, onDownload, onDelete }: BackupRowProps) {
    const deleteButtonClassName = [
        'backups-page__action-btn',
        'backups-page__action-btn--danger',
        'card-actions__btn',
        'card-actions__btn--danger',
    ].join(' ');

    return (
        <div className="backups-page__item list-item-card" role="listitem" data-backup-id={backup.id}>
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
                            title={formatUtcDateTime(backup.createdAt)}
                        >
                            <CalendarIcon weight="duotone" aria-hidden="true" />
                            <span className="backups-page__item-date-full">{formatUtcDate(backup.createdAt)}</span>
                            {isRelativeTimeString(formatRelativeTime(backup.createdAt)) && (
                                <span className="backups-page__item-date-relative">
                                    ({formatRelativeTime(backup.createdAt)})
                                </span>
                            )}
                        </span>
                        <span className="backups-page__item-size" data-testid="backups-page__item-size">
                            <HardDrivesIcon weight="duotone" aria-hidden="true" />
                            {formatBackupSize(backup.sizeBytes)}
                        </span>
                    </div>
                </div>
                <div className="backups-page__item-actions card-actions">
                    <Tooltip content="Download">
                        <button
                            type="button"
                            className="backups-page__action-btn card-actions__btn"
                            data-testid="backups-page__action-btn"
                            onClick={() => onDownload(backup)}
                            aria-label={`Download ${backup.name}`}
                            data-action="download"
                            data-target={backup.id}
                        >
                            <DownloadSimpleIcon weight="bold" />
                        </button>
                    </Tooltip>
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
                            <ClockCounterClockwiseIcon weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className={deleteButtonClassName}
                            data-testid="backups-page__action-btn"
                            onClick={() => onDelete(backup)}
                            aria-label={`Delete ${backup.name}`}
                            data-action="delete"
                            data-target={backup.id}
                        >
                            <TrashIcon weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

export default BackupsPage;
