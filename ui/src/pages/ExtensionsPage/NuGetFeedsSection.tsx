import { forwardRef, useState, useCallback, useImperativeHandle, useMemo } from 'react';
import {
    PlusIcon,
    CloudIcon,
    PencilSimpleIcon,
    TrashIcon,
    CircleNotchIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { Tooltip } from '@/components/ui/Tooltip';
import { EmptyState } from '@/components/ui/EmptyState';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { useToast } from '@/components/ui/Toast';
import { usePackageSources, type PackageSource } from '@/hooks/usePackageSources';
import { getErrorMessage } from '@/lib/errors';
import { PackageSourceForm, type PackageSourceFormValues } from './PackageSourceForm';

import '@/styles/card-actions.css';
import './NuGetFeedsSection.css';

function toFormValues(source: PackageSource): PackageSourceFormValues {
    return {
        id: source.id,
        name: source.name,
        sourceUrl: source.sourceUrl,
        enabled: source.enabled,
        searchFilterKind: source.searchFilterKind,
        searchFilterValue: source.searchFilterValue ?? '',
        credentialKind: source.credentialKind,
        username: source.username ?? '',
        secret: '',
        secretReference: source.secretReference ?? '',
        hasSecret: source.hasSecret,
    };
}

/** Lets the Extensions page open the Add Feed form from its phone actions button. */
export interface NuGetFeedsSectionHandle {
    add: () => void;
}

export const NuGetFeedsSection = forwardRef<NuGetFeedsSectionHandle>(function NuGetFeedsSection(_props, ref) {
    const {
        sources,
        isLoading,
        error,
        refetch,
        createSource,
        isCreating,
        patchSource,
        isPatching,
        setEnabled,
        deleteSource,
        isDeleting,
    } = usePackageSources();
    const { toast } = useToast();

    const [formOpen, setFormOpen] = useState(false);
    const [editing, setEditing] = useState<PackageSourceFormValues | null>(null);
    const [deletingSource, setDeletingSource] = useState<PackageSource | null>(null);
    const [togglingIds, setTogglingIds] = useState<Set<string>>(new Set());
    const currentEditing = useMemo(() => {
        if (!editing?.id) {
            return editing;
        }
        const source = sources.find((candidate) => candidate.id === editing.id);
        return source ? toFormValues(source) : editing;
    }, [editing, sources]);

    const handleAdd = useCallback(() => {
        setEditing(null);
        setFormOpen(true);
    }, []);
    useImperativeHandle(ref, () => ({ add: handleAdd }), [handleAdd]);

    const handleEdit = useCallback((source: PackageSource) => {
        setEditing(toFormValues(source));
        setFormOpen(true);
    }, []);

    const handleFormOpenChange = useCallback((open: boolean) => {
        setFormOpen(open);
        if (!open) {
            setEditing(null);
        }
    }, []);

    const handleSubmit = useCallback(
        async (values: PackageSourceFormValues) => {
            try {
                if (values.id) {
                    await patchSource(values.id, {
                        name: { value: values.name.trim() },
                        sourceUrl: { value: values.sourceUrl.trim() },
                        enabled: { value: values.enabled },
                        searchFilterKind: { value: values.searchFilterKind },
                        searchFilterValue: {
                            value: values.searchFilterKind === 'None' ? null : values.searchFilterValue.trim(),
                        },
                        credentialKind: { value: values.credentialKind },
                        username: { value: values.credentialKind === 'Basic' ? values.username.trim() || null : null },
                        ...(values.credentialKind === 'Basic' && values.secret
                            ? { encryptedSecret: { value: values.secret } }
                            : {}),
                        secretReference: {
                            value:
                                values.credentialKind === 'SecretReference'
                                    ? values.secretReference.trim() || null
                                    : null,
                        },
                    });
                    toast({ title: 'Feed Updated', description: `Updated "${values.name}".`, variant: 'success' });
                } else {
                    await createSource({
                        name: values.name.trim(),
                        sourceUrl: values.sourceUrl.trim(),
                        enabled: values.enabled,
                        searchFilterKind: values.searchFilterKind,
                        searchFilterValue: values.searchFilterKind === 'None' ? null : values.searchFilterValue.trim(),
                        credentialKind: values.credentialKind,
                        username: values.credentialKind === 'Basic' ? values.username.trim() || null : null,
                        secret: values.credentialKind === 'Basic' ? values.secret || null : null,
                        secretReference:
                            values.credentialKind === 'SecretReference' ? values.secretReference.trim() || null : null,
                    });
                    toast({ title: 'Feed Added', description: `Added "${values.name}".`, variant: 'success' });
                }
                setFormOpen(false);
                setEditing(null);
            } catch (err) {
                toast({ title: 'Save Failed', description: getErrorMessage(err), variant: 'error' });
                throw err;
            }
        },
        [createSource, patchSource, toast],
    );

    const handleToggle = useCallback(
        async (source: PackageSource, enabled: boolean) => {
            setTogglingIds((prev) => new Set([...prev, source.id]));
            try {
                await setEnabled(source.id, enabled);
            } catch (err) {
                toast({ title: 'Update Failed', description: getErrorMessage(err), variant: 'error' });
            } finally {
                setTogglingIds((prev) => {
                    const next = new Set(prev);
                    next.delete(source.id);
                    return next;
                });
            }
        },
        [setEnabled, toast],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingSource) {
            return;
        }
        try {
            await deleteSource(deletingSource.id);
            toast({ title: 'Feed Removed', description: `Removed "${deletingSource.name}".`, variant: 'success' });
        } catch (err) {
            toast({ title: 'Delete Failed', description: getErrorMessage(err), variant: 'error' });
        } finally {
            setDeletingSource(null);
        }
    }, [deletingSource, deleteSource, toast]);

    return (
        <section className="nuget-feeds" data-section="nuget-feeds">
            <header className="nuget-feeds__header">
                <div className="nuget-feeds__heading">
                    <h2 className="nuget-feeds__title">NuGet Feeds</h2>
                    <p className="nuget-feeds__description">
                        Package sources used to discover and install extensions. The first enabled feed is the default.
                    </p>
                </div>
                <Button variant="primary" size="sm" onClick={handleAdd} data-action="add-feed">
                    <PlusIcon weight="bold" aria-hidden="true" />
                    <span>Add Feed</span>
                </Button>
            </header>

            {isLoading && sources.length === 0 ? (
                <div className="nuget-feeds__loading">
                    <CircleNotchIcon weight="bold" className="nuget-feeds__spinner" aria-hidden="true" />
                    <p>Loading feeds...</p>
                </div>
            ) : error && sources.length === 0 ? (
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Failed to load feeds"
                    description={getErrorMessage(error)}
                    onRetry={refetch}
                />
            ) : sources.length === 0 ? (
                <EmptyState
                    icon={<CloudIcon weight="duotone" />}
                    title="No feeds configured"
                    description="Add a NuGet feed to install extensions from."
                    action={
                        <Button variant="primary" size="sm" onClick={handleAdd}>
                            <PlusIcon weight="bold" aria-hidden="true" />
                            Add Feed
                        </Button>
                    }
                />
            ) : (
                <div className="nuget-feeds__list" role="list" aria-label="NuGet feeds">
                    {sources.map((source) => (
                        <div
                            key={source.id}
                            className={`nuget-feeds__card${source.enabled ? '' : ' nuget-feeds__card--disabled'}`}
                            role="listitem"
                            data-feed-id={source.id}
                        >
                            <div className="nuget-feeds__card-info">
                                <div className="nuget-feeds__card-title-row">
                                    <span className="nuget-feeds__card-name">{source.name}</span>
                                    {source.credentialKind !== 'None' && (
                                        <Badge variant="info" size="sm" badgeStyle="outline">
                                            Authenticated
                                        </Badge>
                                    )}
                                </div>
                                <span className="nuget-feeds__card-url">{source.sourceUrl}</span>
                            </div>
                            <div className="nuget-feeds__card-toggle" onClick={(e) => e.stopPropagation()}>
                                <Switch
                                    checked={source.enabled}
                                    onCheckedChange={(checked) => handleToggle(source, checked)}
                                    aria-label={`${source.enabled ? 'Disable' : 'Enable'} ${source.name}`}
                                    data-action="toggle-feed"
                                    data-target={source.id}
                                    disabled={togglingIds.has(source.id)}
                                />
                            </div>
                            <div className="nuget-feeds__card-actions card-actions">
                                <Tooltip content="Edit">
                                    <button
                                        type="button"
                                        className="card-actions__btn"
                                        onClick={() => handleEdit(source)}
                                        aria-label={`Edit ${source.name}`}
                                        data-action="edit-feed"
                                        data-target={source.id}
                                    >
                                        <PencilSimpleIcon weight="bold" />
                                    </button>
                                </Tooltip>
                                <Tooltip content="Delete">
                                    <button
                                        type="button"
                                        className="card-actions__btn card-actions__btn--danger"
                                        onClick={() => setDeletingSource(source)}
                                        aria-label={`Delete ${source.name}`}
                                        data-action="delete-feed"
                                        data-target={source.id}
                                    >
                                        <TrashIcon weight="bold" />
                                    </button>
                                </Tooltip>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            <PackageSourceForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={currentEditing}
                onSubmit={handleSubmit}
                isSubmitting={isCreating || isPatching}
            />

            <DeleteConfirmDialog
                open={!!deletingSource}
                onConfirm={handleConfirmDelete}
                onCancel={() => setDeletingSource(null)}
                itemName={deletingSource?.name ?? ''}
                itemType="Feed"
                isDeleting={isDeleting}
            />
        </section>
    );
});

NuGetFeedsSection.displayName = 'NuGetFeedsSection';

export default NuGetFeedsSection;
