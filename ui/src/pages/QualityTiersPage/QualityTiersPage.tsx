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
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useQualityTierLists, type ApiQualityTierList } from '@/hooks/useQualityTierLists';
import { useTags } from '@/hooks/useTags';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { TierListForm, type TierListFormValues } from './TierListForm';

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
    const [formOpen, , openForm, closeForm] = useToggle();
    const [cloningIds, setCloningIds] = useState<Set<string>>(new Set());
    const [deletingTierList, setDeletingTierList] = useState<TierListDisplay | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const {
        tierLists: apiTierLists,
        isLoading,
        error,
        createTierList,
        deleteTierList,
        refetch,
    } = useQualityTierLists();
    const { tags } = useTags();
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

    const columns = useMemo<ColumnDef<TierListDisplay>[]>(
        () => [
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
                meta: { headerLabel: 'Name' },
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
                                    onClick={() => void navigate(`/admin/quality-tiers/${row.original.id}`)}
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
        [canManage, cloningIds, filters, handleClone, mimeOptions, navigate, setFilters],
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
                const created = await createTierList({
                    name: values.name,
                    mimeTypes: values.mimeTypes,
                    upgradeAllowed: values.upgradeAllowed,
                });
                toast({ title: 'Created', description: `Created "${values.name}"`, variant: 'success' });
                closeForm();
                void navigate(`/admin/quality-tiers/${created.id}`);
            } catch {
                toast({ title: 'Create failed', description: 'Failed to create the tier list.', variant: 'error' });
            } finally {
                setIsSubmitting(false);
            }
        },
        [closeForm, createTierList, navigate, toast],
    );

    const addTierActions: QuickAction[] = useMemo(
        () => [{ id: 'add-tier', label: 'Add Tier List', icon: <PlusIcon weight="bold" />, onClick: openForm }],
        [openForm],
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
                            <Button variant="primary" size="sm" data-action="add-tier-list" onClick={openForm}>
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
                    <DataTable<TierListDisplay>
                        columns={columns}
                        data={filteredTierLists}
                        sorting={sorting}
                        onSortingChange={(updater) =>
                            setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                        }
                        getRowId={(tierList) => tierList.id}
                        aria-label="Quality tiers"
                        emptyState={<EmptyState title="No tier lists match the filters" size="sm" />}
                    />
                )}
            </PullToRefresh>

            {canManage && <QuickActionsButton actions={addTierActions} title="Add Tier List" />}

            <TierListForm
                open={formOpen}
                onOpenChange={(open) => !open && closeForm()}
                initialValues={null}
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
        </div>
    );
}

QualityTiersPage.displayName = 'QualityTiersPage';

export default QualityTiersPage;
