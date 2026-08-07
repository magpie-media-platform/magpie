import { useState, useCallback, useMemo, useRef } from 'react';
import { Link } from 'react-router-dom';
import {
    PuzzlePieceIcon,
    PowerIcon,
    CircleNotchIcon,
    CaretRightIcon,
    WarningIcon,
    WarningCircleIcon,
    PlusIcon,
    ArrowClockwiseIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { Switch } from '@/components/ui/Switch';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/Tabs';
import { useToast } from '@/components/ui/Toast';
import { AdminExtensionsSlot } from '@/components/ui/Slot';
import { EmptyState } from '@/components/ui/EmptyState';
import {
    SortableListControls,
    sortableListItemClassNames,
    useSortableList,
    type SortableDropPosition,
    type SortableMoveDirection,
} from '@/components/ui/SortableList';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import {
    useExtensionActions,
    useExtensionManagement,
    type ExtensionItem,
    type ExtensionUpdateInfo,
} from '@/hooks/useExtensionManagement';
import { getErrorMessage } from '@/lib/errors';
import { InstallExtensionDialog } from './InstallExtensionDialog';
import { NuGetFeedsSection } from './NuGetFeedsSection';
import { getStatusVariant, getStatusLabel } from './extensionStatus';

import './ExtensionsPage.css';
import '@/styles/list-toolbar.css';

function comparePackageIdDescending(a: string | null | undefined, b: string | null | undefined): number {
    const left = a?.toUpperCase() ?? '';
    const right = b?.toUpperCase() ?? '';
    if (left < right) {
        return 1;
    }
    if (left > right) {
        return -1;
    }
    return 0;
}

export function ExtensionsPage() {
    useDocumentTitle('Extensions');
    const { extensions, isLoading, error, toggleEnabled, reorderExtensions, refetch } = useExtensionManagement();
    const actions = useExtensionActions();

    const orderedExtensions = useMemo(
        () =>
            [...extensions].sort(
                (a, b) => b.loadOrder - a.loadOrder || comparePackageIdDescending(a.packageId, b.packageId),
            ),
        [extensions],
    );

    const [togglingIds, setTogglingIds] = useState<Set<string>>(new Set());
    const [installOpen, setInstallOpen] = useState(false);
    const [updateInfo, setUpdateInfo] = useState<ExtensionUpdateInfo[] | null>(null);
    const { toast } = useToast();
    const sortable = useSortableList({
        items: orderedExtensions,
        getLabel: (extension) => extension.name,
        onReorder: reorderExtensions,
        onReorderSuccess: ({ item, position, total }) => {
            toast({
                title: 'Load Order Updated',
                description: `${item.name} moved to position ${position + 1} of ${total}. The new order takes effect after the next restart.`,
                variant: 'success',
            });
        },
        onReorderError: (error) => {
            toast({
                title: 'Failed to Reorder',
                description: getErrorMessage(error),
                variant: 'error',
            });
        },
    });
    const displayExtensions = sortable.items;

    const runToggle = useCallback(
        async (ext: ExtensionItem, enabled: boolean) => {
            setTogglingIds((prev) => new Set([...prev, ext.id]));
            try {
                await toggleEnabled(ext.id, enabled);
                toast({
                    title: enabled ? 'Extension Enabled' : 'Extension Disabled',
                    description: `${ext.name} will be ${enabled ? 'enabled' : 'disabled'} after the next restart.`,
                    variant: 'success',
                });
                return true;
            } catch (err) {
                toast({
                    title: enabled ? 'Failed to Enable' : 'Failed to Disable',
                    description:
                        err instanceof Error
                            ? err.message
                            : `Failed to ${enabled ? 'enable' : 'disable'} the extension.`,
                    variant: 'error',
                });
                return false;
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

    const handleToggleEnabled = useCallback(
        async (ext: ExtensionItem, enabled: boolean) => {
            if (ext.disabledReason) {
                return;
            }
            await runToggle(ext, enabled);
        },
        [runToggle],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleInstall = useCallback(
        async (packageId: string, version: string) => {
            const result = await actions.installExtension({ packageId, version: version || undefined });
            toast({
                title: 'Extension Installed',
                description: result.restartRequired
                    ? `${result.extension.name} v${result.extension.version} installed. Restart Magpie to load it.`
                    : `${result.extension.name} v${result.extension.version} installed.`,
                variant: 'success',
            });
        },
        [actions, toast],
    );

    const handleInstallFromFile = useCallback(
        async (file: File) => {
            const result = await actions.installExtensionFromFile(file);
            toast({
                title: 'Extension Installed',
                description: result.restartRequired
                    ? `${result.extension.name} installed from ${file.name}. Restart Magpie to load it.`
                    : `${result.extension.name} installed from ${file.name}.`,
                variant: 'success',
            });
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

    const extensionsDescription =
        'Extensions can be installed from NuGet or uploaded packages, then enabled, disabled, and updated here.';

    if (isLoading && extensions.length === 0) {
        return (
            <div className="extensions-page" data-page="extensions">
                <PageHeader
                    className="extensions-page__header"
                    title="Extensions"
                    description={extensionsDescription}
                    icon={<PuzzlePieceIcon weight="duotone" aria-hidden="true" data-testid="extensions-page__icon" />}
                />
                <div className="extensions-page__loading">
                    <CircleNotchIcon weight="bold" className="extensions-page__spinner" aria-hidden="true" />
                    <p>Loading extensions...</p>
                </div>
            </div>
        );
    }

    if (error && extensions.length === 0) {
        return (
            <div className="extensions-page" data-page="extensions">
                <AdminExtensionsSlot>
                    <PageHeader
                        className="extensions-page__header"
                        title="Extensions"
                        description={extensionsDescription}
                        icon={
                            <PuzzlePieceIcon weight="duotone" aria-hidden="true" data-testid="extensions-page__icon" />
                        }
                    />
                    <EmptyState
                        icon={<WarningCircleIcon weight="duotone" />}
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
                    <PageHeader
                        className="extensions-page__header"
                        title="Extensions"
                        description={extensionsDescription}
                        icon={
                            <PuzzlePieceIcon weight="duotone" aria-hidden="true" data-testid="extensions-page__icon" />
                        }
                        actions={
                            <Button
                                variant="primary"
                                size="sm"
                                onClick={() => setInstallOpen(true)}
                                data-action="add-extension"
                            >
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="extensions-page__add-label">Add</span>
                            </Button>
                        }
                    />

                    <Tabs defaultValue="installed" className="extensions-page__tabs">
                        <TabsList aria-label="Extension sections">
                            <TabsTrigger value="installed" data-tab="installed">
                                Installed
                            </TabsTrigger>
                            <TabsTrigger value="feeds" data-tab="feeds">
                                NuGet Feeds
                            </TabsTrigger>
                        </TabsList>

                        <TabsContent value="installed" forceMount>
                            <div className="list-toolbar__summary">
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    className="extensions-page__check-updates"
                                    onClick={handleCheckUpdates}
                                    disabled={actions.isCheckingUpdates}
                                    data-action="check-updates"
                                >
                                    {actions.isCheckingUpdates ? (
                                        <CircleNotchIcon
                                            weight="bold"
                                            className="extensions-page__spinner"
                                            aria-hidden="true"
                                        />
                                    ) : (
                                        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                                    )}
                                    <span className="extensions-page__button-label">Check for Updates</span>
                                </Button>
                            </div>

                            <div className="extensions-page__list" role="list" aria-label="Installed extensions">
                                {displayExtensions.length === 0 ? (
                                    <EmptyState
                                        icon={<PuzzlePieceIcon weight="duotone" />}
                                        title="No extensions installed"
                                        description="No extensions are currently installed."
                                    />
                                ) : (
                                    <>
                                        <p
                                            className="extensions-page__list-hint"
                                            data-testid="extensions-page__order-hint"
                                        >
                                            Some extensions can override or conflict with one another. Extensions higher
                                            in the list take precedence. Extensions can be reordered by dragging or
                                            using the move buttons. Changes take effect after the next restart.
                                        </p>
                                        <p className="sr-only" aria-live="polite" aria-atomic="true">
                                            {sortable.announcement}
                                        </p>
                                        {displayExtensions.map((ext, index) => (
                                            <ExtensionCard
                                                key={ext.id}
                                                extension={ext}
                                                position={index}
                                                extensionCount={displayExtensions.length}
                                                isToggling={togglingIds.has(ext.id)}
                                                isMoving={sortable.movingId === ext.id}
                                                isReorderDisabled={sortable.isMoving}
                                                isDragging={sortable.draggedId === ext.id}
                                                isDropTarget={sortable.dropTargetId === ext.id}
                                                dropPosition={
                                                    sortable.dropTargetId === ext.id ? sortable.dropPosition : null
                                                }
                                                onToggleEnabled={handleToggleEnabled}
                                                onDragStart={sortable.handleDragStart}
                                                onDragEnd={sortable.handleDragEnd}
                                                onDragOver={sortable.handleDragOver}
                                                onDragLeave={sortable.handleDragLeave}
                                                onDrop={sortable.handleDrop}
                                                onTouchStart={sortable.handleTouchStart}
                                                onTouchMove={sortable.handleTouchMove}
                                                onTouchEnd={sortable.handleTouchEnd}
                                                onTouchCancel={sortable.handleTouchCancel}
                                                onMove={sortable.handleMove}
                                                focusDirection={
                                                    sortable.keyboardFocusTarget?.id === ext.id
                                                        ? sortable.keyboardFocusTarget.direction
                                                        : null
                                                }
                                                onFocusRestored={sortable.handleFocusRestored}
                                                updateInfo={updateInfoByExtensionId.get(ext.id) ?? null}
                                            />
                                        ))}
                                        <div
                                            ref={sortable.dragImageRef}
                                            className="sortable-list__drag-image extensions-page__drag-image"
                                            aria-hidden="true"
                                        />
                                    </>
                                )}
                            </div>
                        </TabsContent>

                        <TabsContent value="feeds" forceMount>
                            <NuGetFeedsSection />
                        </TabsContent>
                    </Tabs>
                </PullToRefresh>
            </AdminExtensionsSlot>

            <InstallExtensionDialog
                open={installOpen}
                onOpenChange={setInstallOpen}
                onInstall={handleInstall}
                onInstallFromFile={handleInstallFromFile}
                isInstalling={actions.isInstalling}
                isInstallingFromFile={actions.isInstallingFromFile}
            />
        </div>
    );
}

ExtensionsPage.displayName = 'ExtensionsPage';

interface ExtensionCardProps {
    extension: ExtensionItem;
    position: number;
    extensionCount: number;
    isToggling: boolean;
    isMoving: boolean;
    isReorderDisabled: boolean;
    isDragging: boolean;
    isDropTarget: boolean;
    dropPosition: SortableDropPosition | null;
    onToggleEnabled: (ext: ExtensionItem, enabled: boolean) => void;
    onDragStart: (ext: ExtensionItem, event: React.DragEvent) => void;
    onDragEnd: () => void;
    onDragOver: (id: string, event: React.DragEvent) => void;
    onDragLeave: () => void;
    onDrop: (id: string, event: React.DragEvent) => void;
    onTouchStart: (ext: ExtensionItem, event: React.TouchEvent) => void;
    onTouchMove: (event: React.TouchEvent) => void;
    onTouchEnd: () => void;
    onTouchCancel: () => void;
    onMove: (ext: ExtensionItem, direction: SortableMoveDirection) => void;
    focusDirection: SortableMoveDirection | null;
    onFocusRestored: () => void;
    updateInfo: ExtensionUpdateInfo | null;
}

function ExtensionCard({
    extension,
    position,
    extensionCount,
    isToggling,
    isMoving,
    isReorderDisabled,
    isDragging,
    isDropTarget,
    dropPosition,
    onToggleEnabled,
    onDragStart,
    onDragEnd,
    onDragOver,
    onDragLeave,
    onDrop,
    onTouchStart,
    onTouchMove,
    onTouchEnd,
    onTouchCancel,
    onMove,
    focusDirection,
    onFocusRestored,
    updateInfo,
}: ExtensionCardProps) {
    const statusVariant = getStatusVariant(extension);
    const statusLabel = getStatusLabel(extension);
    const canToggle = !extension.disabledReason;
    const cardLinkRef = useRef<HTMLAnchorElement>(null);

    const classes = [
        'extensions-page__card',
        sortableListItemClassNames(isDragging, isDropTarget, dropPosition),
        !extension.enabled && 'extensions-page__card--disabled',
        extension.disabledReason && 'extensions-page__card--error',
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <div
            className={classes}
            role="listitem"
            data-magpie-extension-id={extension.id}
            data-sortable-list-item-id={extension.id}
            data-dragging={isDragging || undefined}
            onDragOver={(e) => onDragOver(extension.id, e)}
            onDragLeave={onDragLeave}
            onDrop={(e) => onDrop(extension.id, e)}
        >
            <SortableListControls
                label={extension.name}
                position={position}
                itemCount={extensionCount}
                moving={isMoving}
                disabled={isReorderDisabled}
                focusDirection={focusDirection}
                focusFallbackRef={cardLinkRef}
                dragHandleClassName="extensions-page__drag-handle"
                orderControlsClassName="extensions-page__order-controls"
                orderButtonClassName="extensions-page__order-button"
                spinnerClassName="extensions-page__spinner"
                dragHandleTestId="extensions-page__drag-handle"
                actionName="extension"
                onDragStart={(event) => onDragStart(extension, event)}
                onDragEnd={onDragEnd}
                onTouchStart={(event) => onTouchStart(extension, event)}
                onTouchMove={onTouchMove}
                onTouchEnd={onTouchEnd}
                onTouchCancel={onTouchCancel}
                onMove={(direction) => onMove(extension, direction)}
                onFocusRestored={onFocusRestored}
            />
            <Link ref={cardLinkRef} to={`/admin/extensions/${extension.id}`} className="extensions-page__card-link">
                <div className="extensions-page__card-info">
                    <span className="extensions-page__card-title-row">
                        <span className="extensions-page__card-name">{extension.name}</span>
                        <span className="extensions-page__card-version">v{extension.version}</span>
                    </span>
                    {extension.disabledReason && (
                        <span className="extensions-page__card-reason">{extension.disabledReason}</span>
                    )}
                </div>
                <div className="extensions-page__card-badges">
                    <Badge variant={statusVariant} size="sm" dot>
                        {statusLabel}
                    </Badge>
                    {extension.managed && (
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
                        <WarningIcon weight="bold" className="extensions-page__card-warning" aria-label="Has errors" />
                    )}
                </div>
                <CaretRightIcon weight="bold" className="extensions-page__card-chevron" aria-hidden="true" />
            </Link>
            {canToggle && (
                <div className="extensions-page__card-toggle" onClick={(e) => e.stopPropagation()}>
                    {isToggling ? (
                        <CircleNotchIcon weight="bold" className="extensions-page__spinner" aria-hidden="true" />
                    ) : (
                        <PowerIcon weight="bold" className="extensions-page__toggle-icon" aria-hidden="true" />
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
