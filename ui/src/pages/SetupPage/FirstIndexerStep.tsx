import { useCallback, type ReactNode } from 'react';
import { ArrowRightIcon, DatabaseIcon, SkipForwardIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { countConfiguredIndexerInstances, useIndexers } from '@/hooks/useIndexers';
import { useIndexerProviderTypes } from '@/hooks/useProviderTypes';
import { IndexerForm, type IndexerFormValues } from '@/components/configuration/IndexerForm';
import { AvailableCatalog } from '@/components/configuration/AvailableCatalog';

import { AddAnotherActions } from './AddAnotherActions';
import { useAddAnotherStep } from './useAddAnotherStep';

interface FirstIndexerStepProps {
    onAdvance: (skipped?: boolean) => void;
}

export function FirstIndexerStep({ onAdvance }: FirstIndexerStepProps): ReactNode {
    const { toast } = useToast();
    const { createIndexer, isCreatingIndexer, indexers, isLoadingIndexers } = useIndexers();
    const { types: providerTypes, isLoading: isLoadingProviderTypes } = useIndexerProviderTypes();
    const { formOpen, setFormOpen, added, markAdded, addAnother, continueToNext } = useAddAnotherStep(onAdvance);
    const configuredIndexerCount = countConfiguredIndexerInstances(indexers);

    const handleSubmit = useCallback(
        async (values: IndexerFormValues) => {
            try {
                await createIndexer({
                    name: values.name,
                    providerType: values.providerType,
                    description: values.description,
                    settings: values.settings,
                });
                toast({
                    title: 'Indexer created',
                    description: `Magpie created ${values.name}.`,
                    variant: 'success',
                });
                markAdded();
            } catch (error) {
                toast({
                    title: 'Failed to create indexer',
                    description: error instanceof Error ? error.message : 'Try again from the Indexers admin page.',
                    variant: 'error',
                });
                throw error;
            }
        },
        [createIndexer, markAdded, toast],
    );

    const hasDirectProviders = providerTypes.length > 0;
    const hasCatalog = indexers.length > 0;
    const nothingInstalled = !isLoadingIndexers && !isLoadingProviderTypes && !hasDirectProviders && !hasCatalog;
    const configuredIndexerCountLabel = `${configuredIndexerCount} indexer instance${configuredIndexerCount === 1 ? '' : 's'}`;

    return (
        <div className="setup-page__form" data-step="indexer">
            <p className="setup-page__hint">
                Indexers find releases. One can be added now, or configured later under Admin {'>'} Indexers.
            </p>
            {configuredIndexerCount > 0 && (
                <p className="setup-page__hint" data-state="already-added">
                    {configuredIndexerCountLabel} already configured.
                </p>
            )}
            {added ? (
                <AddAnotherActions
                    label="indexer"
                    action="indexer"
                    onAddAnother={addAnother}
                    onContinue={continueToNext}
                    disabled={isCreatingIndexer}
                />
            ) : (
                <>
                    {hasDirectProviders && (
                        <Button
                            type="button"
                            variant="primary"
                            size="lg"
                            onClick={() => setFormOpen(true)}
                            disabled={isCreatingIndexer}
                            className="setup-page__submit"
                            data-action="open-indexer-form"
                        >
                            <DatabaseIcon weight="bold" aria-hidden="true" />
                            Add an indexer
                        </Button>
                    )}
                    {hasCatalog && <AvailableCatalog onConfigured={markAdded} />}
                    {nothingInstalled && (
                        <p className="setup-page__hint" data-state="no-indexer-integrations">
                            No indexer integrations are installed. Install an extension that provides one, or skip for
                            now.
                        </p>
                    )}
                    <Button
                        type="button"
                        variant="secondary"
                        size="lg"
                        onClick={() => onAdvance(true)}
                        className="setup-page__submit"
                        data-action="skip-indexer"
                    >
                        <SkipForwardIcon weight="bold" aria-hidden="true" />
                        Skip for now
                        <ArrowRightIcon weight="bold" aria-hidden="true" />
                    </Button>
                </>
            )}

            {hasDirectProviders && (
                <IndexerForm
                    open={formOpen}
                    onOpenChange={setFormOpen}
                    onSubmit={handleSubmit}
                    isSubmitting={isCreatingIndexer}
                />
            )}
        </div>
    );
}

FirstIndexerStep.displayName = 'FirstIndexerStep';
