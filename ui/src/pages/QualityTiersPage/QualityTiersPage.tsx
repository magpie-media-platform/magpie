import { useState, useCallback, useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
    SlidersHorizontal,
    Plus,
    Trash,
    Copy,
    CircleNotch,
    CheckFat,
    CaretRight,
    Tag,
    WarningCircle,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Button } from '@/components/ui/Button';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Tooltip } from '@/components/ui/Tooltip';
import { EmptyState } from '@/components/ui/EmptyState';
import { useToast } from '@/components/ui/Toast';
import { useQualityTierLists, type ApiQualityTierList } from '@/hooks/useQualityTierLists';
import { useTags } from '@/hooks/useTags';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useAuth, Role } from '@/auth';
import { TierListForm, type TierListFormValues } from './TierListForm';

import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';
import './QualityTiersPage.css';

interface TierPreview {
    id: string;
    name: string;
    isCutoff: boolean;
}

interface TierListDisplay {
    id: string;
    name: string;
    upgradeAllowed: boolean;
    tiers: TierPreview[];
    createdAt: string;
}

function mapTierListToDisplay(apiTierList: ApiQualityTierList): TierListDisplay {
    const sortedRanks = [...apiTierList.qualityTierRanks].sort((a, b) => a.ordinal - b.ordinal);

    return {
        id: apiTierList.id,
        name: apiTierList.name,
        upgradeAllowed: apiTierList.upgradeAllowed,
        tiers: sortedRanks.map((rank) => ({
            id: rank.qualityTierId,
            name: rank.qualityTier?.name ?? 'Unknown Tier',
            isCutoff: rank.isCutoff,
        })),
        createdAt: apiTierList.createdAt,
    };
}

export function QualityTiersPage() {
    useDocumentTitle('Quality Tiers');
    const { hasRole } = useAuth();
    const canManage = hasRole(Role.Manager);
    const [searchParams, setSearchParams] = useSearchParams();
    const [cloningIds, setCloningIds] = useState<Set<string>>(new Set());
    const [formOpen, , openForm, closeForm] = useToggle();
    const [deletingTierList, setDeletingTierList] = useState<TierListDisplay | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const { toast } = useToast();

    const selectedTagName = searchParams.get('tag') ?? '';

    const {
        tierLists: apiTierLists,
        isLoading,
        error,
        createTierList,
        deleteTierList,
        refetch,
    } = useQualityTierLists(selectedTagName ? { tag: selectedTagName } : undefined);

    const { tags } = useTags();

    const tierLists = useMemo(() => {
        return apiTierLists.map(mapTierListToDisplay);
    }, [apiTierLists]);

    const totalTierLists = tierLists.length;

    const upgradeCounts = useMemo(() => {
        let upgrade = 0;
        let noUpgrade = 0;
        for (const tl of tierLists) {
            if (tl.upgradeAllowed) {
                upgrade++;
            } else {
                noUpgrade++;
            }
        }
        return { upgrade, noUpgrade };
    }, [tierLists]);

    const handleTagFilterChange = useCallback(
        (tagName: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tagName === '__all__') {
                        next.delete('tag');
                    } else {
                        next.set('tag', tagName);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const handleClearTagFilter = useCallback(() => {
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev);
                next.delete('tag');
                return next;
            },
            { replace: true },
        );
    }, [setSearchParams]);

    const handleAdd = useCallback(() => {
        openForm();
    }, [openForm]);

    const addTierActions: QuickAction[] = useMemo(
        () => [{ id: 'add-tier', label: 'Add Tier List', icon: <Plus weight="bold" />, onClick: handleAdd }],
        [handleAdd],
    );

    const handleDelete = useCallback(
        (id: string) => {
            const tierList = tierLists.find((t) => t.id === id);
            if (!tierList) {
                return;
            }
            setDeletingTierList(tierList);
        },
        [tierLists],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingTierList) {
            return;
        }

        setIsDeleting(true);

        try {
            await deleteTierList(deletingTierList.id);
            toast({
                title: 'Deleted',
                description: `Deleted "${deletingTierList.name}"`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete failed',
                description: 'Could not delete quality tier list',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingTierList(null);
        }
    }, [deletingTierList, deleteTierList, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingTierList(null);
    }, []);

    const handleClone = useCallback(
        async (id: string) => {
            const tierList = tierLists.find((t) => t.id === id);
            if (!tierList) {
                return;
            }

            setCloningIds((prev) => new Set([...prev, id]));

            try {
                const apiTierList = apiTierLists.find((t) => t.id === id);
                if (!apiTierList) {
                    throw new Error('Tier list not found');
                }

                await createTierList({
                    name: `${tierList.name} (Copy)`,
                    upgradeAllowed: tierList.upgradeAllowed,
                    qualityTierRanks: apiTierList.qualityTierRanks.map((rank) => ({
                        qualityTierId: rank.qualityTierId,
                        ordinal: rank.ordinal,
                        isCutoff: rank.isCutoff,
                    })),
                });
                toast({
                    title: 'Cloned',
                    description: `Created copy of "${tierList.name}"`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Clone failed',
                    description: 'Could not clone quality tier list',
                    variant: 'error',
                });
            } finally {
                setCloningIds((prev) => {
                    const next = new Set(prev);
                    next.delete(id);
                    return next;
                });
            }
        },
        [tierLists, apiTierLists, createTierList, toast],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleFormSubmit = useCallback(
        async (values: TierListFormValues) => {
            setIsSubmitting(true);
            try {
                await createTierList({
                    name: values.name,
                    upgradeAllowed: values.upgradeAllowed,
                });
                toast({
                    title: 'Created',
                    description: `Created "${values.name}"`,
                    variant: 'success',
                });
                closeForm();
            } catch {
                toast({
                    title: 'Error',
                    description: 'Failed to create tier list',
                    variant: 'error',
                });
            } finally {
                setIsSubmitting(false);
            }
        },
        [createTierList, toast, closeForm],
    );

    const handleFormOpenChange = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
            }
        },
        [closeForm],
    );

    return (
        <div className="quality-tiers-page" data-page="quality-tiers">
            <PullToRefresh onRefresh={handleRefresh} className="quality-tiers-page__pull-to-refresh">
                <header className="quality-tiers-page__header">
                    <div className="quality-tiers-page__header-left">
                        <SlidersHorizontal
                            weight="duotone"
                            className="quality-tiers-page__icon"
                            aria-hidden="true"
                            data-testid="quality-tiers-page__icon"
                        />
                        <h1 className="quality-tiers-page__title">Quality Tiers</h1>
                        <span className="page-count quality-tiers-page__count" data-testid="quality-tiers-page__count">
                            {totalTierLists}
                        </span>
                    </div>
                    {canManage && (
                        <div className="quality-tiers-page__header-actions">
                            <Button variant="primary" size="sm" data-action="add-tier-list" onClick={handleAdd}>
                                <Plus weight="bold" aria-hidden="true" />
                                <span className="quality-tiers-page__add-label">Add Tier List</span>
                            </Button>
                        </div>
                    )}
                </header>

                {isLoading && tierLists.length === 0 ? (
                    <div className="quality-tiers-page__loading" data-testid="quality-tiers-page__loading">
                        <CircleNotch weight="bold" className="quality-tiers-page__spinner" aria-hidden="true" />
                        <p>Loading quality tier lists...</p>
                    </div>
                ) : error && tierLists.length === 0 ? (
                    <EmptyState
                        icon={<WarningCircle weight="duotone" />}
                        title="Failed to load quality tier lists"
                        description="Something went wrong. Please try again."
                        action={
                            <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                Retry
                            </Button>
                        }
                    />
                ) : (
                    <>
                        <div className="list-toolbar__summary" aria-label="Upgrade summary">
                            <Badge variant="fulfilled" size="sm" badgeStyle="outline" dot count={upgradeCounts.upgrade}>
                                Upgrade
                            </Badge>
                            <Badge variant="default" size="sm" badgeStyle="outline" dot count={upgradeCounts.noUpgrade}>
                                No Upgrade
                            </Badge>
                        </div>

                        {tags.length > 0 && (
                            <div className="list-toolbar__filters" data-component="tier-filters">
                                <div className="list-toolbar__filters-controls">
                                    <Select value={selectedTagName || '__all__'} onValueChange={handleTagFilterChange}>
                                        <SelectTrigger
                                            size="sm"
                                            placeholder="All Tags"
                                            aria-label="Filter by tag"
                                            data-filter="tag"
                                        >
                                            <span className="quality-tiers-page__tag-trigger">
                                                <Tag weight="regular" aria-hidden="true" />
                                                {selectedTagName || 'All Tags'}
                                            </span>
                                        </SelectTrigger>
                                        <SelectContent>
                                            <SelectItem value="__all__">All Tags</SelectItem>
                                            {tags.map((tag) => (
                                                <SelectItem key={tag.name} value={tag.name}>
                                                    {tag.name}
                                                </SelectItem>
                                            ))}
                                        </SelectContent>
                                    </Select>
                                    {selectedTagName && (
                                        <Button
                                            variant="ghost"
                                            size="sm"
                                            onClick={handleClearTagFilter}
                                            className="list-toolbar__clear-filters"
                                            data-action="clear-tag-filter"
                                            aria-label="Clear tag filter"
                                        >
                                            Clear
                                        </Button>
                                    )}
                                </div>
                            </div>
                        )}

                        <div className="quality-tiers-page__list" role="list" aria-label="Quality tier lists">
                            {tierLists.length === 0 ? (
                                <EmptyState
                                    icon={<SlidersHorizontal weight="duotone" />}
                                    title={
                                        selectedTagName
                                            ? 'No quality tier lists match the selected tag'
                                            : 'No quality tier lists configured'
                                    }
                                    description={
                                        selectedTagName
                                            ? 'Try selecting a different tag or clear the filter'
                                            : 'Add a tier list to define quality preferences'
                                    }
                                />
                            ) : (
                                tierLists.map((tierList) => (
                                    <TierListCard
                                        key={tierList.id}
                                        tierList={tierList}
                                        isCloning={cloningIds.has(tierList.id)}
                                        canManage={canManage}
                                        onDelete={handleDelete}
                                        onClone={handleClone}
                                    />
                                ))
                            )}
                        </div>
                    </>
                )}
            </PullToRefresh>

            {canManage && <QuickActionsButton actions={addTierActions} title="Add Tier List" />}

            <TierListForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={null}
                tags={tags}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
            />

            <DeleteConfirmDialog
                open={!!deletingTierList}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingTierList?.name ?? ''}
                itemType="Quality Tier List"
                isDeleting={isDeleting}
            />
        </div>
    );
}

QualityTiersPage.displayName = 'QualityTiersPage';

interface TierListCardProps {
    tierList: TierListDisplay;
    isCloning: boolean;
    canManage: boolean;
    onDelete: (id: string) => void;
    onClone: (id: string) => void;
}

function TierListCard({ tierList, isCloning, canManage, onDelete, onClone }: TierListCardProps) {
    const tierCount = tierList.tiers.length;
    const cutoffTier = tierList.tiers.find((t) => t.isCutoff);

    return (
        <div
            className="quality-tiers-page__item"
            role="listitem"
            data-tier-list-id={tierList.id}
            data-cloning={isCloning || undefined}
        >
            <div className="quality-tiers-page__item-main">
                <Link
                    to={`/admin/quality-tiers/${tierList.id}`}
                    className="quality-tiers-page__item-link"
                    data-action="view-detail"
                    data-target={tierList.id}
                >
                    <div className="quality-tiers-page__item-info">
                        <div className="quality-tiers-page__item-header">
                            <span className="quality-tiers-page__item-name">{tierList.name}</span>
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
                        <div className="quality-tiers-page__item-meta">
                            <span className="quality-tiers-page__item-tier-count">
                                <SlidersHorizontal weight="regular" aria-hidden="true" />
                                {tierCount} {tierCount === 1 ? 'tier' : 'tiers'}
                            </span>
                            {cutoffTier && (
                                <Tooltip content={`Cutoff: ${cutoffTier.name}`}>
                                    <span
                                        className="quality-tiers-page__item-cutoff"
                                        data-testid="quality-tiers-page__item-cutoff"
                                    >
                                        <CheckFat weight="fill" aria-hidden="true" />
                                        {cutoffTier.name}
                                    </span>
                                </Tooltip>
                            )}
                        </div>
                    </div>
                    <CaretRight weight="bold" className="quality-tiers-page__item-chevron" aria-hidden="true" />
                </Link>
                {canManage && (
                    <div className="quality-tiers-page__item-actions card-actions">
                        <Tooltip content="Clone">
                            <button
                                type="button"
                                className={`card-actions__btn${isCloning ? ' card-actions__btn--loading' : ''}`}
                                onClick={(e) => {
                                    e.preventDefault();
                                    onClone(tierList.id);
                                }}
                                disabled={isCloning}
                                aria-label={isCloning ? `Cloning ${tierList.name}` : `Clone ${tierList.name}`}
                                data-action="clone"
                                data-target={tierList.id}
                            >
                                {isCloning ? (
                                    <CircleNotch weight="bold" className="card-actions__spinner" aria-hidden="true" />
                                ) : (
                                    <Copy weight="bold" />
                                )}
                            </button>
                        </Tooltip>
                        <Tooltip content="Delete">
                            <button
                                type="button"
                                className="card-actions__btn card-actions__btn--danger"
                                onClick={(e) => {
                                    e.preventDefault();
                                    onDelete(tierList.id);
                                }}
                                aria-label={`Delete ${tierList.name}`}
                                data-action="delete"
                                data-target={tierList.id}
                            >
                                <Trash weight="bold" />
                            </button>
                        </Tooltip>
                    </div>
                )}
            </div>

            <div className="quality-tiers-page__tier-preview">
                {tierList.tiers.map((tier) => (
                    <span
                        key={tier.id}
                        className={`quality-tiers-page__tier-chip${tier.isCutoff ? ' quality-tiers-page__tier-chip--cutoff' : ''}`}
                        data-testid={
                            tier.isCutoff ? 'quality-tiers-page__tier-chip--cutoff' : 'quality-tiers-page__tier-chip'
                        }
                        data-tier-id={tier.id}
                        data-cutoff={tier.isCutoff || undefined}
                    >
                        {tier.isCutoff && (
                            <CheckFat weight="fill" className="quality-tiers-page__cutoff-icon" aria-hidden="true" />
                        )}
                        {tier.name}
                    </span>
                ))}
            </div>
        </div>
    );
}

export default QualityTiersPage;
