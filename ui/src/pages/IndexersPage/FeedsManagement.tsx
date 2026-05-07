import { useState, useMemo, useCallback } from 'react';
import { Rss, Trash, PencilSimple, Clock, Plus, CircleNotch, Power } from '@phosphor-icons/react';

import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';

import { useIndexers, type IndexerFeedItem } from '@/hooks/useIndexers';
import { useToggle } from '@/hooks/useToggle';
import { formatRelativeTime } from '@/lib/formatTime';

import { EmptyState } from '@/components/ui/EmptyState';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { IndexerFeedForm, type IndexerFeedFormValues } from './IndexerFeedForm';

export function FeedsManagement() {
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingFeed, setEditingFeed] = useState<IndexerFeedFormValues | null>(null);
    const [deletingFeed, setDeletingFeed] = useState<IndexerFeedItem | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const {
        feeds,
        isLoading,
        createFeed: apiCreateFeed,
        updateFeed: apiUpdateFeed,
        toggleFeedEnabled,
        deleteFeed: apiDeleteFeed,
        isCreatingFeed,
        isUpdatingFeed,
    } = useIndexers();

    const handleToggleEnabled = useCallback(
        async (id: string, enabled: boolean) => {
            try {
                await toggleFeedEnabled(id, enabled);
            } catch (error) {
                toast({
                    title: 'Error',
                    description: error instanceof Error ? error.message : 'Failed to toggle feed',
                    variant: 'error',
                });
            }
        },
        [toggleFeedEnabled, toast],
    );

    const handleAdd = useCallback(() => {
        setEditingFeed(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (id: string) => {
            const feed = feeds.find((f) => f.id === id);
            if (feed) {
                setEditingFeed({
                    id: feed.id,
                    name: feed.name,
                    description: feed.description ?? '',
                    provider: feed.provider,
                    enabled: feed.enabled,
                    settings: feed.settings,
                });
                openForm();
            }
        },
        [feeds, openForm],
    );

    const handleDelete = useCallback(
        (id: string) => {
            const feed = feeds.find((f) => f.id === id);
            if (!feed) {
                return;
            }
            setDeletingFeed(feed);
        },
        [feeds],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingFeed) {
            return;
        }
        setIsDeleting(true);

        try {
            await apiDeleteFeed(deletingFeed.id);
            toast({
                title: 'Feed deleted',
                description: `"${deletingFeed.name}" has been removed`,
                variant: 'success',
            });
        } catch (error) {
            toast({
                title: 'Error',
                description: error instanceof Error ? error.message : 'Failed to delete feed',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingFeed(null);
        }
    }, [deletingFeed, apiDeleteFeed, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingFeed(null);
    }, []);

    const handleFormSubmit = useCallback(
        async (values: IndexerFeedFormValues) => {
            try {
                if (values.id) {
                    await apiUpdateFeed(values.id, {
                        name: values.name,
                        description: values.description || undefined,
                        providerType: values.provider,
                        enabled: values.enabled,
                        settings: values.settings,
                    });
                    toast({
                        title: 'Feed updated',
                        description: `${values.name} has been updated`,
                        variant: 'success',
                    });
                } else {
                    await apiCreateFeed({
                        name: values.name,
                        description: values.description || undefined,
                        providerType: values.provider,
                        enabled: values.enabled,
                        settings: values.settings,
                    });
                    toast({
                        title: 'Feed created',
                        description: `${values.name} has been added`,
                        variant: 'success',
                    });
                }
                closeForm();
            } catch (error) {
                toast({
                    title: 'Error',
                    description: error instanceof Error ? error.message : 'Failed to save feed',
                    variant: 'error',
                });
            }
        },
        [apiCreateFeed, apiUpdateFeed, toast, closeForm],
    );

    const addActions: QuickAction[] = useMemo(
        () => [{ id: 'add-feed', label: 'Add Feed', icon: <Plus weight="bold" />, onClick: handleAdd }],
        [handleAdd],
    );

    return (
        <>
            <p className="indexers-page__feeds-description">
                Feeds provide indexer definitions from external sources like Prowlarr or Jackett.
            </p>

            <div className="indexers-page__list" role="list" aria-label="Indexer feeds">
                {isLoading ? (
                    <div className="indexers-page__loading">
                        <CircleNotch weight="bold" className="indexers-page__spinner" aria-hidden="true" />
                        <p>Loading feeds...</p>
                    </div>
                ) : feeds.length === 0 ? (
                    <EmptyState
                        icon={<Rss weight="duotone" />}
                        title="No feeds configured"
                        description="Add a feed to automatically sync indexer definitions"
                        action={
                            <Button variant="primary" size="sm" onClick={handleAdd} data-action="add-feed-empty">
                                <Plus weight="bold" aria-hidden="true" />
                                <span>Add Feed</span>
                            </Button>
                        }
                    />
                ) : (
                    feeds.map((feed) => (
                        <FeedRow
                            key={feed.id}
                            feed={feed}
                            onToggleEnabled={handleToggleEnabled}
                            onEdit={handleEdit}
                            onDelete={handleDelete}
                        />
                    ))
                )}
            </div>

            <QuickActionsButton actions={addActions} title="Add Feed" />

            <IndexerFeedForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) {
                        closeForm();
                    }
                }}
                initialValues={editingFeed}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreatingFeed || isUpdatingFeed}
            />

            <DeleteConfirmDialog
                open={!!deletingFeed}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingFeed?.name ?? ''}
                itemType="Indexer Feed"
                isDeleting={isDeleting}
            />
        </>
    );
}

FeedsManagement.displayName = 'FeedsManagement';

interface FeedRowProps {
    feed: IndexerFeedItem;
    onToggleEnabled: (id: string, enabled: boolean) => void;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
}

function FeedRow({ feed, onToggleEnabled, onEdit, onDelete }: FeedRowProps) {
    return (
        <div
            className={`indexers-page__item${feed.enabled ? '' : ' indexers-page__item--disabled'}`}
            role="listitem"
            data-testid="indexers-page__item"
            data-feed-id={feed.id}
            data-enabled={feed.enabled}
        >
            <div className="indexers-page__item-main">
                <div className="indexers-page__item-info">
                    <div className="indexers-page__item-header">
                        <span className="indexers-page__item-name">{feed.name}</span>
                        <Badge variant={feed.enabled ? 'healthy' : 'unknown'} size="sm">
                            {feed.enabled ? 'Enabled' : 'Disabled'}
                        </Badge>
                    </div>
                    <div className="indexers-page__item-meta">
                        <span className="indexers-page__item-definition">{feed.provider}</span>
                        {feed.description && (
                            <span className="indexers-page__item-description">{feed.description}</span>
                        )}
                        <span className="indexers-page__item-checked">
                            <Clock weight="regular" aria-hidden="true" />
                            Synced {formatRelativeTime(feed.lastSync)}
                        </span>
                    </div>
                </div>
                <div className="indexers-page__item-actions card-actions">
                    <Tooltip content={feed.enabled ? 'Disable' : 'Enable'}>
                        <div className="indexers-page__item-toggle">
                            <Power weight="bold" className="indexers-page__toggle-icon" aria-hidden="true" />
                            <Switch
                                checked={feed.enabled}
                                onCheckedChange={(checked) => onToggleEnabled(feed.id, checked)}
                                aria-label={`${feed.enabled ? 'Disable' : 'Enable'} ${feed.name}`}
                                data-action="toggle-enabled"
                                data-target={feed.id}
                            />
                        </div>
                    </Tooltip>
                    <Tooltip content="Edit">
                        <button
                            type="button"
                            className="indexers-page__action-btn card-actions__btn"
                            data-testid="indexers-page__action-btn"
                            onClick={() => onEdit(feed.id)}
                            aria-label={`Edit ${feed.name}`}
                            data-action="edit"
                            data-target={feed.id}
                        >
                            <PencilSimple weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="indexers-page__action-btn indexers-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="indexers-page__action-btn"
                            onClick={() => onDelete(feed.id)}
                            aria-label={`Delete ${feed.name}`}
                            data-action="delete"
                            data-target={feed.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}
