import { useState, useCallback, useMemo } from 'react';
import {
    ArrowRightIcon,
    ArrowsLeftRightIcon,
    CircleNotchIcon,
    FlaskIcon,
    TrashIcon,
    PencilSimpleIcon,
    PlusIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { useBulkRun } from '@/hooks/useBulkRun';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { Badge } from '@/components/ui/Badge';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { SelectionCircleToggle } from '@/components/ui/SelectionCircleToggle';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useRemotePathMappings, type ApiRemotePathMapping } from '@/hooks/useRemotePathMappings';
import { useToggle } from '@/hooks/useToggle';
import { useInstanceName } from '@/hooks/useInstanceName';

import { EmptyState } from '@/components/ui/EmptyState';
import { Tooltip } from '@/components/ui/Tooltip';

import { RemotePathMappingForm, type RemotePathMappingFormValues } from './RemotePathMappingForm';

import './RemotePathMappingsPage.css';
import '@/styles/bulk-actions-toolbar.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

function toRemotePathMappingFormValues(mapping: ApiRemotePathMapping): RemotePathMappingFormValues {
    return {
        id: mapping.id,
        host: mapping.host,
        remotePath: mapping.remotePath,
        localPath: mapping.localPath,
    };
}

export function RemotePathMappingsPage() {
    const instanceName = useInstanceName();
    useDocumentTitle('Remote Path Mappings');
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingMapping, setEditingMapping] = useState<RemotePathMappingFormValues | null>(null);
    const [deletingMapping, setDeletingMapping] = useState<ApiRemotePathMapping | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [testingMappingId, setTestingMappingId] = useState<string | null>(null);
    const [isBulkDeleteOpen, setIsBulkDeleteOpen] = useState(false);
    const [isBulkDeleting, setIsBulkDeleting] = useState(false);
    const { toast } = useToast();
    const runBulk = useBulkRun();

    const {
        mappings,
        isLoading,
        error,
        createMapping,
        isCreating,
        updateMapping,
        isUpdating,
        deleteMapping,
        testLocalPath,
        isTestingLocalPath,
        refetch,
    } = useRemotePathMappings();
    const currentEditingMapping = useMemo(() => {
        if (!editingMapping?.id) {
            return editingMapping;
        }
        const mapping = mappings.find((candidate) => candidate.id === editingMapping.id);
        return mapping ? toRemotePathMappingFormValues(mapping) : editingMapping;
    }, [editingMapping, mappings]);

    const isSubmitting = isCreating || isUpdating;

    const handleAddMapping = useCallback(() => {
        setEditingMapping(null);
        openForm();
    }, [openForm]);

    const addMappingActions: QuickAction[] = useMemo(
        () => [
            { id: 'add-mapping', label: 'Add Mapping', icon: <PlusIcon weight="bold" />, onClick: handleAddMapping },
        ],
        [handleAddMapping],
    );

    const handleEditMapping = useCallback(
        (id: string) => {
            const mapping = mappings.find((m) => m.id === id);
            if (mapping) {
                setEditingMapping(toRemotePathMappingFormValues(mapping));
                openForm();
            }
        },
        [mappings, openForm],
    );

    const handleFormSubmit = useCallback(
        async (values: RemotePathMappingFormValues) => {
            try {
                if (values.id) {
                    await updateMapping(values.id, {
                        host: { value: values.host },
                        remotePath: { value: values.remotePath },
                        localPath: { value: values.localPath },
                    });
                    toast({
                        title: values.host,
                        description: 'Remote path mapping updated.',
                        variant: 'success',
                    });
                } else {
                    await createMapping({
                        host: values.host,
                        remotePath: values.remotePath,
                        localPath: values.localPath,
                    });
                    toast({
                        title: values.host,
                        description: 'Remote path mapping added.',
                        variant: 'success',
                    });
                }

                closeForm();
                setEditingMapping(null);
            } catch (error) {
                toast({
                    title: values.host,
                    description: 'Failed to save remote path mapping',
                    variant: 'error',
                });
                throw error;
            }
        },
        [toast, createMapping, updateMapping, closeForm],
    );

    const handleFormClose = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
                setEditingMapping(null);
            }
        },
        [closeForm],
    );

    const handleDeleteMapping = useCallback(
        (id: string) => {
            const mapping = mappings.find((m) => m.id === id);
            if (!mapping) {
                return;
            }
            setDeletingMapping(mapping);
        },
        [mappings],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingMapping) {
            return;
        }

        setIsDeleting(true);

        try {
            await deleteMapping(deletingMapping.id);
            toast({
                title: deletingMapping.host,
                description: 'Remote path mapping deleted.',
                variant: 'success',
            });
        } catch {
            toast({
                title: deletingMapping.host,
                description: 'Failed to delete remote path mapping',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingMapping(null);
        }
    }, [deletingMapping, deleteMapping, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingMapping(null);
    }, []);

    const mappingRowIds = useMemo(() => mappings.map((m) => m.id), [mappings]);
    const selection = useBulkSelection({ pageRowIds: mappingRowIds, totalCount: mappings.length });
    const selectedMappings = useMemo(() => mappings.filter((m) => selection.isRowChecked(m.id)), [mappings, selection]);

    const handleBulkDelete = useCallback(async () => {
        setIsBulkDeleting(true);
        try {
            await runBulk(selectedMappings, selection, {
                action: (mapping) => deleteMapping(mapping.id),
                verb: 'delete',
                itemNounPlural: 'remote path mappings',
            });
        } finally {
            setIsBulkDeleting(false);
            setIsBulkDeleteOpen(false);
        }
    }, [deleteMapping, runBulk, selectedMappings, selection]);

    const handleTestLocalPath = useCallback(
        async (localPath: string, mappingId?: string) => {
            if (mappingId) {
                setTestingMappingId(mappingId);
            }

            try {
                const result = await testLocalPath({ localPath });
                toast({
                    title: localPath,
                    description: result.message,
                    variant: result.success ? 'success' : 'warning',
                });
            } catch {
                toast({
                    title: localPath,
                    description: 'Failed to test local path',
                    variant: 'error',
                });
            } finally {
                if (mappingId) {
                    setTestingMappingId(null);
                }
            }
        },
        [testLocalPath, toast],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    return (
        <div className="remote-path-mappings-page" data-page="remote-path-mappings">
            <PullToRefresh onRefresh={handleRefresh} className="remote-path-mappings-page__pull-to-refresh">
                <PageHeader
                    className="remote-path-mappings-page__header"
                    title="Remote Path Mappings"
                    description={`Translate paths reported by remote download clients into paths ${instanceName} can read at import time.`}
                    icon={
                        <ArrowsLeftRightIcon
                            weight="duotone"
                            aria-hidden="true"
                            data-testid="remote-path-mappings-page__icon"
                        />
                    }
                    actions={
                        <div className="remote-path-mappings-page__header-actions">
                            <SelectModeToggle
                                mobileOnly={false}
                                active={selection.selectionMode}
                                onToggle={selection.toggleSelectionMode}
                            />
                            <Button variant="primary" size="sm" data-action="add-mapping" onClick={handleAddMapping}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="remote-path-mappings-page__add-label">Add Mapping</span>
                            </Button>
                        </div>
                    }
                />

                <BulkActionsToolbar
                    className="remote-path-mappings-page__bulk-toolbar"
                    selection={selection}
                    label="Remote path mapping selection actions"
                    itemNounPlural="remote path mappings"
                >
                    <Button
                        variant="destructive"
                        size="sm"
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected remote path mappings"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Delete</span>
                    </Button>
                </BulkActionsToolbar>

                <div className="remote-path-mappings-page__list" role="list" aria-label="Remote path mappings">
                    {isLoading ? (
                        <EmptyState title="Loading..." size="sm" />
                    ) : error ? (
                        <EmptyState
                            icon={<WarningCircleIcon weight="duotone" />}
                            title="Failed to load remote path mappings"
                            onRetry={refetch}
                        />
                    ) : mappings.length === 0 ? (
                        <EmptyState
                            icon={<ArrowsLeftRightIcon weight="duotone" />}
                            title="No remote path mappings configured"
                            description="A mapping is needed when a download client runs on another machine."
                        />
                    ) : (
                        mappings.map((mapping) => (
                            <MappingRow
                                key={mapping.id}
                                mapping={mapping}
                                onEdit={handleEditMapping}
                                onDelete={handleDeleteMapping}
                                onTest={handleTestLocalPath}
                                isTesting={testingMappingId === mapping.id}
                                selectionMode={selection.selectionMode}
                                isSelected={selection.isRowChecked(mapping.id)}
                                onToggleSelected={(checked) => selection.toggleRow(mapping.id, checked)}
                            />
                        ))
                    )}
                </div>
            </PullToRefresh>

            <QuickActionsButton actions={addMappingActions} title="Add Mapping" />

            <DeleteConfirmDialog
                open={!!deletingMapping}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingMapping?.host ?? ''}
                itemType="Remote Path Mapping"
                isDeleting={isDeleting}
            />

            <DeleteConfirmDialog
                open={isBulkDeleteOpen}
                onConfirm={handleBulkDelete}
                onCancel={() => setIsBulkDeleteOpen(false)}
                itemName={`${selection.selectedCount} selected`}
                itemType="Remote Path Mappings"
                isDeleting={isBulkDeleting}
            />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="remote-path-mappings-mobile-selection-bar"
                    selection={selection}
                    itemNounPlural="remote path mappings"
                >
                    <Button
                        variant="destructive"
                        size="sm"
                        disabled={!selection.hasSelection}
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected remote path mappings"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        Delete
                    </Button>
                </MobileSelectionBar>
            )}

            <RemotePathMappingForm
                open={formOpen}
                onOpenChange={handleFormClose}
                initialValues={currentEditingMapping}
                onSubmit={handleFormSubmit}
                onTestLocalPath={(localPath) => handleTestLocalPath(localPath)}
                isSubmitting={isSubmitting}
                isTestingLocalPath={isTestingLocalPath && testingMappingId === null}
            />
        </div>
    );
}

RemotePathMappingsPage.displayName = 'RemotePathMappingsPage';

interface MappingRowProps {
    mapping: ApiRemotePathMapping;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
    onTest: (localPath: string, mappingId: string) => void;
    isTesting: boolean;
    selectionMode: boolean;
    isSelected: boolean;
    onToggleSelected: (checked: boolean) => void;
}

function MappingRow({
    mapping,
    onEdit,
    onDelete,
    onTest,
    isTesting,
    selectionMode,
    isSelected,
    onToggleSelected,
}: MappingRowProps) {
    return (
        <div className="remote-path-mappings-page__item" role="listitem" data-mapping-id={mapping.id}>
            {selectionMode && (
                <SelectionCircleToggle
                    selected={isSelected}
                    onToggle={() => onToggleSelected(!isSelected)}
                    aria-label={`Select mapping for ${mapping.host}`}
                    data-action="select-row"
                />
            )}
            <div className="remote-path-mappings-page__item-main">
                <div className="remote-path-mappings-page__item-info">
                    <div className="remote-path-mappings-page__item-header">
                        <Badge variant="info" size="sm">
                            {mapping.host}
                        </Badge>
                    </div>
                    <div className="remote-path-mappings-page__item-meta">
                        <span className="remote-path-mappings-page__item-path" data-path="remote">
                            {mapping.remotePath}
                        </span>
                        <ArrowRightIcon
                            weight="bold"
                            className="remote-path-mappings-page__item-arrow"
                            aria-hidden="true"
                        />
                        <span className="remote-path-mappings-page__item-path" data-path="local">
                            {mapping.localPath}
                        </span>
                    </div>
                </div>
                <div className="remote-path-mappings-page__item-actions card-actions">
                    <Tooltip content="Test local path">
                        <button
                            type="button"
                            className="remote-path-mappings-page__action-btn card-actions__btn"
                            data-testid="remote-path-mappings-page__action-btn"
                            onClick={() => onTest(mapping.localPath, mapping.id)}
                            disabled={isTesting}
                            aria-label={`Test local path for ${mapping.host}`}
                            data-action="test-local-path"
                            data-target={mapping.id}
                        >
                            {isTesting ? (
                                <CircleNotchIcon weight="bold" className="card-actions__spinner" />
                            ) : (
                                <FlaskIcon weight="bold" />
                            )}
                        </button>
                    </Tooltip>
                    <Tooltip content="Edit">
                        <button
                            type="button"
                            className="remote-path-mappings-page__action-btn card-actions__btn"
                            data-testid="remote-path-mappings-page__action-btn"
                            onClick={() => onEdit(mapping.id)}
                            aria-label={`Edit mapping for ${mapping.host}`}
                            data-action="edit"
                            data-target={mapping.id}
                        >
                            <PencilSimpleIcon weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="remote-path-mappings-page__action-btn remote-path-mappings-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="remote-path-mappings-page__action-btn"
                            onClick={() => onDelete(mapping.id)}
                            aria-label={`Delete mapping for ${mapping.host}`}
                            data-action="delete"
                            data-target={mapping.id}
                        >
                            <TrashIcon weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

export default RemotePathMappingsPage;
