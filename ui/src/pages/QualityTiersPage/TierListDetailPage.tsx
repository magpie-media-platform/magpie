import { useMemo, useCallback, useState, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
    ArrowLeft,
    SlidersHorizontal,
    Plus,
    Trash,
    PencilSimple,
    CheckFat,
    DotsSixVertical,
    DotsThreeVertical,
    ListNumbers,
    CircleNotch,
    WarningCircle,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { Button } from '@/components/ui/Button';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent } from '@/components/ui/DropdownMenu';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import {
    useQualityTierList,
    useQualityTierLists,
    useQualityTiers,
    type ApiQualityTierList,
    type ApiQualityTierListRank,
    type QualityTierCriteria,
    type QualityTierListPatch,
    type QualityTierListRankPatch,
    type CriteriaValueOperator,
} from '@/hooks/useQualityTierLists';
import { useAuth, Role } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { getErrorMessage } from '@/lib/errors';
import { TierForm, type TierFormValues, type Criterion } from './TierForm';

import '@/styles/card-actions.css';
import './TierListDetailPage.css';

export type TierCriterion = Criterion;

export interface TierWithCriteria {
    id: string;
    name: string;
    isCutoff: boolean;
    criteriaCount: number;
    criteria: TierCriterion[];
}

export interface TierListDetail {
    id: string;
    name: string;
    upgradeAllowed: boolean;
    tiers: TierWithCriteria[];
    createdAt: string;
}

const operatorNames: Record<number, TierCriterion['operator']> = {
    0: 'Equals',
    1: 'NotEquals',
    10: 'Contains',
    11: 'NotContains',
    12: 'StartsWith',
    13: 'NotStartsWith',
};

function mapOperator(op: CriteriaValueOperator | undefined): TierCriterion['operator'] {
    return operatorNames[op ?? 0] ?? 'Equals';
}

const operatorValues: Record<string, CriteriaValueOperator> = {
    Equals: 0,
    NotEquals: 1,
    Contains: 10,
    NotContains: 11,
    StartsWith: 12,
    NotStartsWith: 13,
};

function mapCriteriaToApi(criteria: Criterion[]): QualityTierCriteria {
    if (criteria.length === 0) {
        return { and: [] };
    }

    const items: Record<string, { operator: CriteriaValueOperator; value: string }> = {};
    for (const c of criteria) {
        items[c.field] = {
            operator: operatorValues[c.operator] ?? 0,
            value: c.value,
        };
    }

    return { and: [{ items }] };
}

function mapCriteriaToDisplay(criteria: QualityTierCriteria, tierId: string): TierCriterion[] {
    const result: TierCriterion[] = [];
    let index = 0;

    if (!criteria.and) {
        return result;
    }

    for (const dict of criteria.and) {
        if (!dict.items) {
            continue;
        }

        for (const [field, criterion] of Object.entries(dict.items)) {
            const op = mapOperator(criterion.operator);

            if (criterion.values && criterion.values.length > 0) {
                for (const v of criterion.values) {
                    result.push({
                        id: `${tierId}-c-${index++}`,
                        field,
                        operator: op,
                        value: v,
                    });
                }
            } else {
                result.push({
                    id: `${tierId}-c-${index++}`,
                    field,
                    operator: op,
                    value: criterion.value ?? '',
                });
            }
        }
    }

    return result;
}

function mapTierRankToDisplay(rank: ApiQualityTierListRank): TierWithCriteria {
    const tier = rank.qualityTier;
    const criteria = tier?.criteria ? mapCriteriaToDisplay(tier.criteria, rank.qualityTierId) : [];

    return {
        id: rank.qualityTierId,
        name: tier?.name ?? 'Unknown Tier',
        isCutoff: rank.isCutoff,
        criteriaCount: criteria.length,
        criteria,
    };
}

function mapTierListToDisplay(apiTierList: ApiQualityTierList): TierListDetail {
    const sortedRanks = [...apiTierList.qualityTierRanks].sort((a, b) => a.ordinal - b.ordinal);

    return {
        id: apiTierList.id,
        name: apiTierList.name,
        upgradeAllowed: apiTierList.upgradeAllowed,
        tiers: sortedRanks.map(mapTierRankToDisplay),
        createdAt: apiTierList.createdAt,
    };
}

function formatOperator(operator: TierCriterion['operator']): string {
    switch (operator) {
        case 'Equals':
            return '=';
        case 'NotEquals':
            return '≠';
        case 'Contains':
            return 'contains';
        case 'NotContains':
            return 'not contains';
        case 'StartsWith':
            return 'starts with';
        case 'NotStartsWith':
            return 'not starts with';
        default:
            return operator;
    }
}

export function TierListDetailPage() {
    useDocumentTitle('Tier List');
    const { tierListId } = useParams<{ tierListId: string }>();
    const { hasRole } = useAuth();
    const canManage = hasRole(Role.Manager);
    const { toast } = useToast();
    const [deletingTier, setDeletingTier] = useState<{ id: string; name: string } | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);

    const [formOpen, setFormOpen] = useState(false);
    const [editingTier, setEditingTier] = useState<TierFormValues | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);

    const [draggedTierId, setDraggedTierId] = useState<string | null>(null);
    const [dropTargetId, setDropTargetId] = useState<string | null>(null);
    const [dropPosition, setDropPosition] = useState<'before' | 'after' | null>(null);
    const [orderedTiers, setOrderedTiers] = useState<TierWithCriteria[] | null>(null);
    const dragImageRef = useRef<HTMLDivElement>(null);

    const { data: apiTierList, isLoading, error, refetch } = useQualityTierList(tierListId);
    const { updateTierList } = useQualityTierLists();
    const { createTier, updateTier } = useQualityTiers();

    const tierList = useMemo(() => {
        if (!apiTierList) {
            return null;
        }
        return mapTierListToDisplay(apiTierList);
    }, [apiTierList]);

    const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
        const items: BreadcrumbItem[] = [{ id: 'quality-tiers', label: 'Quality Tiers', to: '/admin/quality-tiers' }];

        if (tierList) {
            items.push({
                id: tierList.id,
                label: tierList.name,
                to: `/admin/quality-tiers/${tierList.id}`,
            });
        }

        return items;
    }, [tierList]);

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleAddTier = useCallback(() => {
        setEditingTier(null);
        setFormOpen(true);
    }, []);

    const addTierActions: QuickAction[] = useMemo(
        () => [{ id: 'add-tier', label: 'Add Tier', icon: <Plus weight="bold" />, onClick: handleAddTier }],
        [handleAddTier],
    );

    const handleEditTier = useCallback(
        (tierId: string) => {
            if (!tierList) {
                return;
            }
            const tier = tierList.tiers.find((t) => t.id === tierId);
            if (tier) {
                setEditingTier({
                    id: tier.id,
                    name: tier.name,
                    isCutoff: tier.isCutoff,
                    criteria: tier.criteria,
                });
                setFormOpen(true);
            }
        },
        [tierList],
    );

    const handleFormOpenChange = useCallback((open: boolean) => {
        if (!open) {
            setFormOpen(false);
            setEditingTier(null);
        }
    }, []);

    const handleFormSubmit = useCallback(
        async (values: TierFormValues) => {
            if (!tierListId || !tierList) {
                return;
            }

            setIsSubmitting(true);
            const criteria = mapCriteriaToApi(values.criteria);

            try {
                if (values.id) {
                    await updateTier(values.id, {
                        name: { value: values.name },
                        criteria: { value: criteria },
                    });

                    const existing = tierList.tiers.find((t) => t.id === values.id);
                    if (existing && existing.isCutoff !== values.isCutoff) {
                        const update: QualityTierListRankPatch[] = [];
                        if (values.isCutoff) {
                            for (const t of tierList.tiers) {
                                if (t.id === values.id) {
                                    update.push({ qualityTierId: { value: t.id }, isCutoff: { value: true } });
                                } else if (t.isCutoff) {
                                    update.push({ qualityTierId: { value: t.id }, isCutoff: { value: false } });
                                }
                            }
                        } else {
                            update.push({ qualityTierId: { value: values.id }, isCutoff: { value: false } });
                        }
                        if (update.length > 0) {
                            await updateTierList(tierListId, { qualityTierRanks: { update } });
                        }
                    }

                    await refetch();
                    toast({
                        title: 'Updated',
                        description: `Updated tier "${values.name}"`,
                        variant: 'success',
                    });
                } else {
                    const newTier = await createTier({
                        name: values.name,
                        criteria,
                    });

                    const nextOrdinal = tierList.tiers.length;
                    const patch: QualityTierListPatch = {
                        qualityTierRanks: {
                            add: [
                                {
                                    qualityTierId: newTier.id,
                                    ordinal: nextOrdinal,
                                    isCutoff: values.isCutoff,
                                },
                            ],
                        },
                    };

                    if (values.isCutoff) {
                        const update: QualityTierListRankPatch[] = [];
                        for (const t of tierList.tiers) {
                            if (t.isCutoff) {
                                update.push({ qualityTierId: { value: t.id }, isCutoff: { value: false } });
                            }
                        }
                        if (update.length > 0) {
                            patch.qualityTierRanks!.update = update;
                        }
                    }

                    await updateTierList(tierListId, patch);
                    await refetch();
                    toast({
                        title: 'Created',
                        description: `Added tier "${values.name}"`,
                        variant: 'success',
                    });
                }

                setFormOpen(false);
                setEditingTier(null);
            } catch {
                toast({
                    title: 'Error',
                    description: `Failed to ${values.id ? 'update' : 'create'} tier`,
                    variant: 'error',
                });
            } finally {
                setIsSubmitting(false);
            }
        },
        [tierListId, tierList, createTier, updateTier, updateTierList, refetch, toast],
    );

    const handleDeleteTier = useCallback((tierId: string, tierName: string) => {
        setDeletingTier({ id: tierId, name: tierName });
    }, []);

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingTier || !tierListId) {
            return;
        }

        setIsDeleting(true);

        try {
            const patch: QualityTierListPatch = {
                qualityTierRanks: {
                    remove: [deletingTier.id],
                },
            };
            await updateTierList(tierListId, patch);
            await refetch();

            toast({
                title: 'Deleted',
                description: `Removed tier "${deletingTier.name}"`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete failed',
                description: 'Could not delete tier',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingTier(null);
        }
    }, [deletingTier, tierListId, updateTierList, refetch, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingTier(null);
    }, []);

    const handleSetCutoff = useCallback(
        async (tierId: string) => {
            if (!tierListId || !tierList) {
                return;
            }

            try {
                const update: QualityTierListRankPatch[] = [];
                for (const tier of tierList.tiers) {
                    if (tier.id === tierId) {
                        update.push({ qualityTierId: { value: tier.id }, isCutoff: { value: true } });
                    } else if (tier.isCutoff) {
                        update.push({ qualityTierId: { value: tier.id }, isCutoff: { value: false } });
                    }
                }

                const patch: QualityTierListPatch = {
                    qualityTierRanks: { update },
                };
                await updateTierList(tierListId, patch);
                await refetch();

                const tier = tierList.tiers.find((t) => t.id === tierId);
                toast({
                    title: 'Cutoff Updated',
                    description: `Set "${tier?.name ?? 'tier'}" as cutoff`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Update failed',
                    description: 'Could not update cutoff',
                    variant: 'error',
                });
            }
        },
        [tierListId, tierList, updateTierList, refetch, toast],
    );

    const displayTiers = useMemo(() => orderedTiers ?? tierList?.tiers ?? [], [orderedTiers, tierList?.tiers]);

    const handleDragStart = useCallback(
        (tierId: string, event: React.DragEvent) => {
            setDraggedTierId(tierId);

            if (dragImageRef.current) {
                const tier = displayTiers.find((t) => t.id === tierId);
                if (tier) {
                    dragImageRef.current.textContent = tier.name;
                    dragImageRef.current.style.display = 'block';
                    event.dataTransfer.setDragImage(dragImageRef.current, 0, 0);
                    requestAnimationFrame(() => {
                        if (dragImageRef.current) {
                            dragImageRef.current.style.display = 'none';
                        }
                    });
                }
            }

            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', tierId);
        },
        [displayTiers],
    );

    const handleDragEnd = useCallback(() => {
        setDraggedTierId(null);
        setDropTargetId(null);
        setDropPosition(null);
    }, []);

    const handleDragOver = useCallback(
        (tierId: string, event: React.DragEvent) => {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';

            if (!draggedTierId || draggedTierId === tierId) {
                return;
            }

            const rect = event.currentTarget.getBoundingClientRect();
            const midY = rect.top + rect.height / 2;
            const position: 'before' | 'after' = event.clientY < midY ? 'before' : 'after';

            setDropTargetId(tierId);
            setDropPosition(position);
        },
        [draggedTierId],
    );

    const handleDragLeave = useCallback(() => {
        setDropTargetId(null);
        setDropPosition(null);
    }, []);

    const handleDrop = useCallback(
        async (targetTierId: string, event: React.DragEvent) => {
            event.preventDefault();

            const sourceTierId = event.dataTransfer.getData('text/plain');
            if (!sourceTierId || sourceTierId === targetTierId || !dropPosition || !tierListId) {
                handleDragEnd();
                return;
            }

            const currentTiers = orderedTiers ?? tierList?.tiers ?? [];
            const sourceIndex = currentTiers.findIndex((t) => t.id === sourceTierId);
            const targetIndex = currentTiers.findIndex((t) => t.id === targetTierId);

            if (sourceIndex === -1 || targetIndex === -1) {
                handleDragEnd();
                return;
            }

            const newTiers = [...currentTiers];
            const [removed] = newTiers.splice(sourceIndex, 1);

            let insertIndex = targetIndex;
            if (dropPosition === 'after') {
                insertIndex += 1;
            }
            if (sourceIndex < targetIndex) {
                insertIndex -= 1;
            }

            newTiers.splice(insertIndex, 0, removed);
            setOrderedTiers(newTiers);
            handleDragEnd();

            try {
                const update: QualityTierListRankPatch[] = newTiers.map((tier, index) => ({
                    qualityTierId: { value: tier.id },
                    ordinal: { value: index },
                }));

                const patch: QualityTierListPatch = {
                    qualityTierRanks: { update },
                };
                await updateTierList(tierListId, patch);
                await refetch();

                toast({
                    title: 'Reordered',
                    description: `Moved "${removed.name}" to priority ${insertIndex + 1}`,
                    variant: 'success',
                });
            } catch {
                setOrderedTiers(null);
                toast({
                    title: 'Reorder failed',
                    description: 'Could not save new tier order',
                    variant: 'error',
                });
            }
        },
        [tierListId, tierList, orderedTiers, dropPosition, handleDragEnd, updateTierList, refetch, toast],
    );

    if (isLoading) {
        return (
            <div className="tier-list-detail-page" data-page="tier-list-detail">
                <div className="tier-list-detail-page__loading">
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--breadcrumb" />
                    <div className="tier-list-detail-page__loading-header">
                        <Skeleton
                            className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--icon"
                            variant="circle"
                        />
                        <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--title" />
                        <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--badge" />
                    </div>
                    <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--description" />
                    <div className="tier-list-detail-page__loading-tiers">
                        <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--tier-card" />
                        <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--tier-card" />
                        <Skeleton className="tier-list-detail-page__skeleton tier-list-detail-page__skeleton--tier-card" />
                    </div>
                </div>
            </div>
        );
    }

    const baseBreadcrumbs: BreadcrumbItem[] = [
        { id: 'quality-tiers', label: 'Quality Tiers', to: '/admin/quality-tiers' },
    ];

    if (error && !tierList) {
        return (
            <div className="tier-list-detail-page" data-page="tier-list-detail">
                <PullToRefresh onRefresh={handleRefresh} className="tier-list-detail-page__pull-to-refresh">
                    <Breadcrumb items={baseBreadcrumbs} className="tier-list-detail-page__breadcrumb" />
                    <header className="tier-list-detail-page__header" data-component="tier-list-detail-header">
                        <div className="tier-list-detail-page__header-main">
                            <SlidersHorizontal
                                weight="duotone"
                                className="tier-list-detail-page__icon"
                                aria-hidden="true"
                            />
                            <div className="tier-list-detail-page__title-row">
                                <h1 className="tier-list-detail-page__title">Quality Tier List</h1>
                            </div>
                        </div>
                    </header>
                    <div className="tier-list-detail-page__not-found">
                        <WarningCircle
                            weight="duotone"
                            className="tier-list-detail-page__not-found-icon"
                            aria-hidden="true"
                        />
                        <h2 className="tier-list-detail-page__not-found-title">Failed to Load Tier List</h2>
                        <p>{getErrorMessage(error)}</p>
                        <div className="tier-list-detail-page__error-actions">
                            <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                Retry
                            </Button>
                            <Link to="/admin/quality-tiers" className="tier-list-detail-page__back-link">
                                <ArrowLeft weight="bold" aria-hidden="true" />
                                Back to Quality Tiers
                            </Link>
                        </div>
                    </div>
                </PullToRefresh>
            </div>
        );
    }

    if (!tierList) {
        return (
            <div className="tier-list-detail-page" data-page="tier-list-detail">
                <PullToRefresh onRefresh={handleRefresh} className="tier-list-detail-page__pull-to-refresh">
                    <Breadcrumb items={baseBreadcrumbs} className="tier-list-detail-page__breadcrumb" />
                    <header className="tier-list-detail-page__header" data-component="tier-list-detail-header">
                        <div className="tier-list-detail-page__header-main">
                            <SlidersHorizontal
                                weight="duotone"
                                className="tier-list-detail-page__icon"
                                aria-hidden="true"
                            />
                            <div className="tier-list-detail-page__title-row">
                                <h1 className="tier-list-detail-page__title">Quality Tier List</h1>
                            </div>
                        </div>
                    </header>
                    <div className="tier-list-detail-page__not-found">
                        <SlidersHorizontal
                            weight="duotone"
                            className="tier-list-detail-page__not-found-icon"
                            aria-hidden="true"
                        />
                        <h2 className="tier-list-detail-page__not-found-title">Tier List Not Found</h2>
                        <p>The tier list you are looking for does not exist.</p>
                        <Link to="/admin/quality-tiers" className="tier-list-detail-page__back-link">
                            <ArrowLeft weight="bold" aria-hidden="true" />
                            Back to Quality Tiers
                        </Link>
                    </div>
                </PullToRefresh>
            </div>
        );
    }

    return (
        <div className="tier-list-detail-page" data-page="tier-list-detail">
            <PullToRefresh onRefresh={handleRefresh} className="tier-list-detail-page__pull-to-refresh">
                <Breadcrumb items={breadcrumbs} className="tier-list-detail-page__breadcrumb" />

                <header
                    className="tier-list-detail-page__header"
                    data-component="tier-list-detail-header"
                    data-tier-list-id={tierList.id}
                >
                    <div className="tier-list-detail-page__header-main">
                        <SlidersHorizontal
                            weight="duotone"
                            className="tier-list-detail-page__icon"
                            aria-hidden="true"
                        />
                        <div className="tier-list-detail-page__title-row">
                            <h1 className="tier-list-detail-page__title">{tierList.name}</h1>
                            {tierList.upgradeAllowed ? (
                                <Badge variant="fulfilled" size="sm">
                                    Upgrade
                                </Badge>
                            ) : (
                                <Badge variant="default" size="sm">
                                    No Upgrade
                                </Badge>
                            )}
                        </div>
                    </div>
                    {canManage && (
                        <div className="tier-list-detail-page__header-actions">
                            <Button variant="primary" size="sm" onClick={handleAddTier} data-action="add-tier">
                                <Plus weight="bold" aria-hidden="true" />
                                <span className="tier-list-detail-page__add-label">Add Tier</span>
                            </Button>
                        </div>
                    )}
                    {canManage && (
                        <div className="tier-list-detail-page__mobile-actions">
                            <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        aria-label="Actions"
                                        data-testid="tier-list-detail-page__mobile-menu"
                                    >
                                        <DotsThreeVertical weight="bold" aria-hidden="true" />
                                    </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end" sideOffset={4}>
                                    <div className="tier-list-detail-page__mobile-actions-content">
                                        <Button
                                            variant="primary"
                                            size="sm"
                                            onClick={handleAddTier}
                                            data-action="add-tier"
                                        >
                                            <Plus weight="bold" aria-hidden="true" />
                                            Add Tier
                                        </Button>
                                    </div>
                                </DropdownMenuContent>
                            </DropdownMenu>
                        </div>
                    )}
                </header>

                <p className="tier-list-detail-page__description">
                    Tiers are listed in priority order. Higher priority tiers are preferred during quality selection.
                    The cutoff tier is the minimum acceptable quality level.
                    {canManage && ' Drag tiers to reorder.'}
                </p>

                <div className="tier-list-detail-page__tiers" role="list" aria-label="Quality tiers">
                    {displayTiers.length === 0 ? (
                        <div className="tier-list-detail-page__empty">
                            <ListNumbers
                                weight="duotone"
                                className="tier-list-detail-page__empty-icon"
                                aria-hidden="true"
                            />
                            <p className="tier-list-detail-page__empty-text">No tiers configured</p>
                            <p className="tier-list-detail-page__empty-hint">Add tiers to define quality preferences</p>
                        </div>
                    ) : (
                        displayTiers.map((tier, index) => (
                            <TierCard
                                key={tier.id}
                                tier={tier}
                                priority={index + 1}
                                totalTiers={displayTiers.length}
                                isDeleting={isDeleting && deletingTier?.id === tier.id}
                                canManage={canManage}
                                isDragging={draggedTierId === tier.id}
                                isDropTarget={dropTargetId === tier.id}
                                dropPosition={dropTargetId === tier.id ? dropPosition : null}
                                onEdit={handleEditTier}
                                onDelete={handleDeleteTier}
                                onSetCutoff={handleSetCutoff}
                                onDragStart={handleDragStart}
                                onDragEnd={handleDragEnd}
                                onDragOver={handleDragOver}
                                onDragLeave={handleDragLeave}
                                onDrop={handleDrop}
                            />
                        ))
                    )}
                </div>

                <div ref={dragImageRef} className="tier-list-detail-page__drag-image" aria-hidden="true" />
            </PullToRefresh>

            {canManage && <QuickActionsButton actions={addTierActions} title="Add Tier" />}

            <TierForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={editingTier}
                tierListName={tierList.name}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
            />

            <DeleteConfirmDialog
                open={!!deletingTier}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingTier?.name ?? ''}
                itemType="Tier"
                isDeleting={isDeleting}
            />
        </div>
    );
}

TierListDetailPage.displayName = 'TierListDetailPage';

interface TierCardProps {
    tier: TierWithCriteria;
    priority: number;
    totalTiers: number;
    isDeleting: boolean;
    canManage: boolean;
    isDragging: boolean;
    isDropTarget: boolean;
    dropPosition: 'before' | 'after' | null;
    onEdit: (tierId: string) => void;
    onDelete: (tierId: string, tierName: string) => void;
    onSetCutoff: (tierId: string) => void;
    onDragStart: (tierId: string, event: React.DragEvent) => void;
    onDragEnd: () => void;
    onDragOver: (tierId: string, event: React.DragEvent) => void;
    onDragLeave: () => void;
    onDrop: (tierId: string, event: React.DragEvent) => void;
}

function TierCard({
    tier,
    priority,
    totalTiers,
    isDeleting,
    canManage,
    isDragging,
    isDropTarget,
    dropPosition,
    onEdit,
    onDelete,
    onSetCutoff,
    onDragStart,
    onDragEnd,
    onDragOver,
    onDragLeave,
    onDrop,
}: TierCardProps) {
    const tierClasses = [
        'tier-list-detail-page__tier',
        tier.isCutoff && 'tier-list-detail-page__tier--cutoff',
        isDragging && 'tier-list-detail-page__tier--dragging',
        isDropTarget && 'tier-list-detail-page__tier--drop-target',
        isDropTarget && dropPosition === 'before' && 'tier-list-detail-page__tier--drop-before',
        isDropTarget && dropPosition === 'after' && 'tier-list-detail-page__tier--drop-after',
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <div
            className={tierClasses}
            role="listitem"
            data-tier-id={tier.id}
            data-cutoff={tier.isCutoff || undefined}
            data-deleting={isDeleting || undefined}
            data-dragging={isDragging || undefined}
            draggable={canManage}
            onDragStart={(e) => onDragStart(tier.id, e)}
            onDragEnd={onDragEnd}
            onDragOver={(e) => onDragOver(tier.id, e)}
            onDragLeave={onDragLeave}
            onDrop={(e) => onDrop(tier.id, e)}
        >
            <div className="tier-list-detail-page__tier-header">
                {canManage && (
                    <div
                        className="tier-list-detail-page__drag-handle"
                        data-testid="tier-list-detail-page__drag-handle"
                        aria-hidden="true"
                    >
                        <DotsSixVertical weight="bold" />
                    </div>
                )}
                <div className="tier-list-detail-page__tier-priority">
                    <span className="tier-list-detail-page__priority-number">{priority}</span>
                    <span className="tier-list-detail-page__priority-of">of {totalTiers}</span>
                </div>
                <div className="tier-list-detail-page__tier-info">
                    <div className="tier-list-detail-page__tier-title-row">
                        <span className="tier-list-detail-page__tier-name">{tier.name}</span>
                        {tier.isCutoff && (
                            <Badge variant="healthy" size="sm">
                                <CheckFat
                                    weight="fill"
                                    className="tier-list-detail-page__cutoff-icon"
                                    aria-hidden="true"
                                />
                                Cutoff
                            </Badge>
                        )}
                    </div>
                    <div className="tier-list-detail-page__tier-meta">
                        <span className="tier-list-detail-page__criteria-count">
                            {tier.criteria.length} {tier.criteria.length === 1 ? 'criterion' : 'criteria'}
                        </span>
                    </div>
                </div>
                {canManage && (
                    <div className="tier-list-detail-page__tier-actions card-actions">
                        {!tier.isCutoff && (
                            <Tooltip content="Set as cutoff">
                                <button
                                    type="button"
                                    className="card-actions__btn"
                                    onClick={() => onSetCutoff(tier.id)}
                                    aria-label={`Set ${tier.name} as cutoff`}
                                    data-action="set-cutoff"
                                    data-target={tier.id}
                                >
                                    <CheckFat weight="bold" />
                                </button>
                            </Tooltip>
                        )}
                        <Tooltip content="Edit">
                            <button
                                type="button"
                                className="card-actions__btn"
                                onClick={() => onEdit(tier.id)}
                                aria-label={`Edit ${tier.name}`}
                                data-action="edit"
                                data-target={tier.id}
                            >
                                <PencilSimple weight="bold" />
                            </button>
                        </Tooltip>
                        <Tooltip content="Delete">
                            <button
                                type="button"
                                className={`card-actions__btn card-actions__btn--danger${isDeleting ? ' card-actions__btn--loading-danger' : ''}`}
                                onClick={() => onDelete(tier.id, tier.name)}
                                disabled={isDeleting}
                                aria-label={isDeleting ? `Deleting ${tier.name}` : `Delete ${tier.name}`}
                                data-action="delete"
                                data-target={tier.id}
                            >
                                {isDeleting ? (
                                    <CircleNotch weight="bold" className="card-actions__spinner" aria-hidden="true" />
                                ) : (
                                    <Trash weight="bold" />
                                )}
                            </button>
                        </Tooltip>
                    </div>
                )}
            </div>

            {tier.criteria.length > 0 && (
                <div className="tier-list-detail-page__criteria">
                    {tier.criteria.map((criterion) => (
                        <span
                            key={criterion.id}
                            className="tier-list-detail-page__criterion"
                            data-criterion-id={criterion.id}
                        >
                            <span className="tier-list-detail-page__criterion-field">{criterion.field}</span>
                            <span className="tier-list-detail-page__criterion-operator">
                                {formatOperator(criterion.operator)}
                            </span>
                            <span className="tier-list-detail-page__criterion-value">{criterion.value || '(any)'}</span>
                        </span>
                    ))}
                </div>
            )}
        </div>
    );
}

export default TierListDetailPage;
