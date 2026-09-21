import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
    CircleNotchIcon,
    CopyIcon,
    PencilSimpleIcon,
    PlusIcon,
    SlidersHorizontalIcon,
    TrashIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { Role, useAuth } from '@/auth';
import { useBulkRun } from '@/hooks/useBulkRun';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { Badge } from '@/components/ui/Badge';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useQualityTierLists, type ApiQualityTierList, type QualityTierListPatch } from '@/hooks/useQualityTierLists';
import { useTags } from '@/hooks/useTags';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { tagsPatch } from '@/lib/tagsPatch';
import { TierListForm, type TierListFormValues } from './TierListForm';

import '@/styles/bulk-actions-toolbar.css';
import '@/styles/card-actions.css';
import './QualityTiersPage.css';

interface TierListDisplay {
    id: string;
    name: string;
    mimeTypes: string[];
    upgradeAllowed: boolean;
}

const UNIVERSAL_FILTER = '__universal__';
const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];
const SORT_KEYS = ['name', 'mimeTypes', 'upgradeAllowed'] as const;
const DEFAULT_FILTERS = { name: '', mime: '' };

function mapTierListToDisplay(apiTierList: ApiQualityTierList): TierListDisplay {
    return {
        id: apiTierList.id,
        name: apiTierList.name,
        mimeTypes: apiTierList.mimeTypes ?? [],
        upgradeAllowed: apiTierList.upgradeAllowed,
    };
}

export function QualityTiersPage() {
    useDocumentTitle('Quality Tiers');
    const { hasRole } = useAuth();
    const canManage = hasRole(Role.Manager);
    const navigate = useNavigate();
    const { toast } = useToast();
    const runBulk = useBulkRun();
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingTierListId, setEditingTierListId] = useState<string | null>(null);
    const [cloningIds, setCloningIds] = useState<Set<string>>(new Set());
    const [deletingTierList, setDeletingTierList] = useState<TierListDisplay | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const [isBulkDeleteOpen, setIsBulkDeleteOpen] = useState(false);
    const [isBulkDeleting, setIsBulkDeleting] = useState(false);
    const {
        tierLists: apiTierLists,
        isLoading,
        error,
        createTierList,
        updateTierList,
        deleteTierList,
        refetch,
    } = useQualityTierLists();
    const { tags } = useTags();
    const editingTierList = useMemo(
        () => apiTierLists.find((candidate) => candidate.id === editingTierListId) ?? null,
        [apiTierLists, editingTierListId],
    );
    const { tags: editingTierListTags, isLoading: isLoadingEditingTierListTags } = useTags(
        editingTierListId ? { entityId: editingTierListId } : undefined,
    );
    const editingTagsLoaded = !editingTierListId || !isLoadingEditingTierListTags;
    const { sorting, filters, setSorting, setFilters } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
    });

    const tierLists = useMemo(() => apiTierLists.map(mapTierListToDisplay), [apiTierLists]);
    const mimeOptions = useMemo<FacetOption[]>(() => {
        const patterns = new Set(tierLists.flatMap((tierList) => tierList.mimeTypes));
        return [
            { value: UNIVERSAL_FILTER, label: 'Any MIME type' },
            ...[...patterns].sort().map((pattern) => ({ value: pattern, label: pattern })),
        ];
    }, [tierLists]);
    const filteredTierLists = useMemo(() => {
        const nameFilter = filters.name.trim().toLowerCase();
        return tierLists.filter((tierList) => {
            const nameMatches = !nameFilter || tierList.name.toLowerCase().includes(nameFilter);
            const mimeMatches =
                !filters.mime ||
                (filters.mime === UNIVERSAL_FILTER
                    ? tierList.mimeTypes.length === 0
                    : tierList.mimeTypes.includes(filters.mime));
            return nameMatches && mimeMatches;
        });
    }, [filters, tierLists]);
    const selection = useBulkSelection({
        pageRowIds,
        totalCount: filteredTierLists.length,
        resetKey: JSON.stringify(filters),
    });
    const selectedTierLists = useMemo(
        () => filteredTierLists.filter((t) => selection.isRowChecked(t.id)),
        [filteredTierLists, selection],
    );

    const handleBulkDelete = useCallback(async () => {
        setIsBulkDeleting(true);
        try {
            await runBulk(selectedTierLists, selection, {
                action: (tierList) => deleteTierList(tierList.id),
                verb: 'delete',
                itemNounPlural: 'tier lists',
            });
        } finally {
            setIsBulkDeleting(false);
            setIsBulkDeleteOpen(false);
        }
    }, [deleteTierList, runBulk, selectedTierLists, selection]);

    const handleClone = useCallback(
        async (tierList: TierListDisplay) => {
            const source = apiTierLists.find((candidate) => candidate.id === tierList.id);
            if (!source) {
                return;
            }
            setCloningIds((current) => new Set([...current, tierList.id]));
            try {
                await createTierList({
                    name: `${tierList.name} (Copy)`,
                    mimeTypes: tierList.mimeTypes,
                    upgradeAllowed: tierList.upgradeAllowed,
                    rules: source.rules ?? [],
                    qualityTierRanks: source.qualityTierRanks.map((rank) => ({
                        qualityTierId: rank.qualityTierId,
                        ordinal: rank.ordinal,
                        isCutoff: rank.isCutoff,
                    })),
                });
                toast({ title: 'Cloned', description: `Created copy of "${tierList.name}"`, variant: 'success' });
            } catch {
                toast({ title: 'Clone failed', description: 'Failed to clone the tier list.', variant: 'error' });
            } finally {
                setCloningIds((current) => {
                    const next = new Set(current);
                    next.delete(tierList.id);
                    return next;
                });
            }
        },
        [apiTierLists, createTierList, toast],
    );

    const handleAdd = useCallback(() => {
        setEditingTierListId(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (tierListId: string) => {
            setEditingTierListId(tierListId);
            openForm();
        },
        [openForm],
    );

    const columns = useMemo<ColumnDef<TierListDisplay>[]>(
        () => [
            ...(canManage
                ? [
                      selectColumn<TierListDisplay>({
                          selection,
                          getId: (row) => row.id,
                          headerLabel: 'Select all tier lists',
                          rowLabel: (row) => row.name,
                      }),
                  ]
                : []),
            {
                id: 'name',
                accessorKey: 'name',
                header: () => (
                    <ColumnHeaderFilter
                        type="text"
                        label="Name"
                        value={filters.name}
                        active={!!filters.name}
                        onChange={(name) => setFilters({ ...filters, name })}
                        placeholder="Contains..."
                    />
                ),
                meta: { headerLabel: 'Name', mobileRole: 'header' },
                cell: ({ row }) => (
                    <Link className="quality-tiers-page__name-link" to={`/admin/quality-tiers/${row.original.id}`}>
                        {row.original.name}
                    </Link>
                ),
            },
            {
                id: 'mimeTypes',
                accessorFn: (tierList) => tierList.mimeTypes.join(', ') || 'Any',
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="File Types"
                        options={mimeOptions}
                        value={filters.mime}
                        active={!!filters.mime}
                        onChange={(mime) => setFilters({ ...filters, mime })}
                    />
                ),
                meta: { headerLabel: 'File Types' },
                cell: ({ row }) => (
                    <span className="quality-tiers-page__file-types">{String(row.getValue('mimeTypes'))}</span>
                ),
            },
            {
                id: 'upgradeAllowed',
                accessorKey: 'upgradeAllowed',
                header: 'Upgrades',
                cell: ({ row }) => (
                    <Badge variant={row.original.upgradeAllowed ? 'fulfilled' : 'default'} size="sm">
                        {row.original.upgradeAllowed ? 'Allowed' : 'Disabled'}
                    </Badge>
                ),
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                cell: ({ row }) =>
                    canManage ? (
                        <div className="quality-tiers-page__row-actions card-actions">
                            <Tooltip content="Clone">
                                <button
                                    type="button"
                                    className="card-actions__btn"
                                    onClick={() => void handleClone(row.original)}
                                    disabled={cloningIds.has(row.original.id)}
                                    aria-label={
                                        cloningIds.has(row.original.id)
                                            ? `Cloning ${row.original.name}`
                                            : `Clone ${row.original.name}`
                                    }
                                    data-action="clone"
                                >
                                    {cloningIds.has(row.original.id) ? (
                                        <CircleNotchIcon className="quality-tiers-page__spinner" weight="bold" />
                                    ) : (
                                        <CopyIcon weight="bold" />
                                    )}
                                </button>
                            </Tooltip>
                            <Tooltip content="Edit">
                                <button
                                    type="button"
                                    className="card-actions__btn"
                                    onClick={() => handleEdit(row.original.id)}
                                    aria-label={`Edit ${row.original.name}`}
                                    data-action="edit"
                                >
                                    <PencilSimpleIcon weight="bold" />
                                </button>
                            </Tooltip>
                            <Tooltip content="Delete">
                                <button
                                    type="button"
                                    className="card-actions__btn card-actions__btn--danger"
                                    onClick={() => setDeletingTierList(row.original)}
                                    aria-label={`Delete ${row.original.name}`}
                                    data-action="delete"
                                >
                                    <TrashIcon weight="bold" />
                                </button>
                            </Tooltip>
                        </div>
                    ) : null,
            },
        ],
        [canManage, selection, cloningIds, filters, handleClone, handleEdit, mimeOptions, setFilters],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingTierList) {
            return;
        }
        setIsDeleting(true);
        try {
            await deleteTierList(deletingTierList.id);
            toast({ title: 'Deleted', description: `Deleted "${deletingTierList.name}"`, variant: 'success' });
            setDeletingTierList(null);
        } catch {
            toast({ title: 'Delete failed', description: 'Failed to delete the tier list.', variant: 'error' });
        } finally {
            setIsDeleting(false);
        }
    }, [deleteTierList, deletingTierList, toast]);

    const handleFormSubmit = useCallback(
        async (values: TierListFormValues) => {
            setIsSubmitting(true);
            try {
                if (values.id) {
                    const originalTagNames = editingTierListTags.map((tag) => tag.name);
                    const patch: QualityTierListPatch = {
                        name: { value: values.name },
                        mimeTypes: { value: values.mimeTypes },
                        upgradeAllowed: { value: values.upgradeAllowed },
                    };
                    const tags = tagsPatch(values.tags, originalTagNames);
                    if (tags) {
                        patch.tags = tags;
                    }
                    await updateTierList(values.id, patch);
                    toast({ title: 'Updated', description: `Updated "${values.name}"`, variant: 'success' });
                    closeForm();
                    return;
                }

                const created = await createTierList({
                    name: values.name,
                    mimeTypes: values.mimeTypes,
                    upgradeAllowed: values.upgradeAllowed,
                });
                toast({ title: 'Created', description: `Created "${values.name}"`, variant: 'success' });
                if (values.tags.length > 0) {
                    try {
                        await updateTierList(created.id, { tags: { add: values.tags } });
                    } catch {
                        toast({
                            title: 'Tags not saved',
                            description: `Created "${values.name}", but failed to assign its tags.`,
                            variant: 'error',
                        });
                    }
                }
                closeForm();
                void navigate(`/admin/quality-tiers/${created.id}`);
            } catch {
                toast({
                    title: values.id ? 'Update failed' : 'Create failed',
                    description: `Failed to ${values.id ? 'update' : 'create'} the tier list.`,
                    variant: 'error',
                });
            } finally {
                setIsSubmitting(false);
            }
        },
        [closeForm, createTierList, editingTierListTags, navigate, toast, updateTierList],
    );

    const formInitialValues: TierListFormValues | null = useMemo(() => {
        if (!editingTierList) {
            return null;
        }
        return {
            id: editingTierList.id,
            name: editingTierList.name,
            mimeTypes: editingTierList.mimeTypes ?? [],
            upgradeAllowed: editingTierList.upgradeAllowed,
            tags: editingTierListTags.map((tag) => tag.name),
        };
    }, [editingTierList, editingTierListTags]);

    const addTierActions: QuickAction[] = useMemo(
        () => [{ id: 'add-tier', label: 'Add Tier List', icon: <PlusIcon weight="bold" />, onClick: handleAdd }],
        [handleAdd],
    );

    return (
        <div className="quality-tiers-page" data-page="quality-tiers">
            <PullToRefresh
                onRefresh={async () => void (await refetch())}
                className="quality-tiers-page__pull-to-refresh"
            >
                <PageHeader
                    className="quality-tiers-page__header"
                    title="Quality Tiers"
                    description="Ordered quality preferences that determine which releases Magpie grabs and when files are upgraded."
                    icon={<SlidersHorizontalIcon weight="duotone" aria-hidden="true" />}
                    actions={
                        canManage && (
                            <Button variant="primary" size="sm" data-action="add-tier-list" onClick={handleAdd}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                Add Tier List
                            </Button>
                        )
                    }
                />

                {isLoading && tierLists.length === 0 ? (
                    <div className="quality-tiers-page__loading" role="status">
                        <CircleNotchIcon weight="bold" className="quality-tiers-page__spinner" aria-hidden="true" />
                        <p>Loading tier lists...</p>
                    </div>
                ) : error && tierLists.length === 0 ? (
                    <EmptyState
                        icon={<WarningCircleIcon weight="duotone" />}
                        title="Failed to load the tier lists."
                        action={<Button onClick={() => void refetch()}>Retry</Button>}
                    />
                ) : tierLists.length === 0 ? (
                    <EmptyState
                        icon={<SlidersHorizontalIcon weight="duotone" />}
                        title="No tier lists configured"
                        description="A tier list defines preferred release quality."
                    />
                ) : (
                    <>
                        {canManage && (
                            <>
                                <BulkActionsToolbar
                                    selection={selection}
                                    label="Tier list selection actions"
                                    itemNounPlural="tier lists"
                                >
                                    <Button
                                        variant="destructive"
                                        size="sm"
                                        onClick={() => setIsBulkDeleteOpen(true)}
                                        aria-label="Delete selected tier lists"
                                        data-action="bulk-delete"
                                    >
                                        <TrashIcon weight="bold" aria-hidden="true" />
                                        <span className="bulk-actions-toolbar__btn-label">Delete</span>
                                    </Button>
                                </BulkActionsToolbar>

                                <SelectModeToggle
                                    active={selection.selectionMode}
                                    onToggle={selection.toggleSelectionMode}
                                />
                            </>
                        )}

                        <DataTable<TierListDisplay>
                            columns={columns}
                            data={filteredTierLists}
                            sorting={sorting}
                            onSortingChange={(updater) =>
                                setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                            }
                            getRowId={(tierList) => tierList.id}
                            onPageRowIdsChange={setPageRowIds}
                            aria-label="Quality tiers"
                            emptyState={<EmptyState title="No tier lists match the filters" size="sm" />}
                            mobileSelectionActive={canManage ? selection.selectionMode : undefined}
                            mobileSelectionOverlay={{
                                isSelected: (tierList) => selection.isRowChecked(tierList.id),
                                onToggle: (tierList) =>
                                    selection.toggleRow(tierList.id, !selection.isRowChecked(tierList.id)),
                            }}
                        />
                    </>
                )}
            </PullToRefresh>

            {canManage && <QuickActionsButton actions={addTierActions} title="Add Tier List" />}

            <TierListForm
                open={formOpen && editingTagsLoaded}
                onOpenChange={(open) => !open && closeForm()}
                initialValues={formInitialValues}
                tags={tags}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
            />

            <DeleteConfirmDialog
                open={!!deletingTierList}
                onConfirm={handleConfirmDelete}
                onCancel={() => setDeletingTierList(null)}
                itemName={deletingTierList?.name ?? ''}
                itemType="Tier List"
                isDeleting={isDeleting}
            />

            <DeleteConfirmDialog
                open={isBulkDeleteOpen}
                onConfirm={handleBulkDelete}
                onCancel={() => setIsBulkDeleteOpen(false)}
                itemName={`${selection.selectedCount} selected`}
                itemType="Tier Lists"
                isDeleting={isBulkDeleting}
            />

            {canManage && selection.selectionMode && (
                <MobileSelectionBar
                    data-component="quality-tiers-mobile-selection-bar"
                    selection={selection}
                    itemNounPlural="tier lists"
                >
                    <Button
                        variant="destructive"
                        size="sm"
                        disabled={!selection.hasSelection}
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected tier lists"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        Delete
                    </Button>
                </MobileSelectionBar>
            )}
        </div>
    );
}

QualityTiersPage.displayName = 'QualityTiersPage';

export default QualityTiersPage;
