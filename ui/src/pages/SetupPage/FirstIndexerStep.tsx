import { useCallback, type ReactNode } from 'react';
import { ArrowRightIcon, DatabaseIcon, SkipForwardIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { countConfiguredIndexerInstances, useIndexers } from '@/hooks/useIndexers';
import { IndexerForm, type IndexerFormValues } from '@/components/configuration/IndexerForm';

import { AddAnotherActions } from './AddAnotherActions';
import { useAddAnotherStep } from './useAddAnotherStep';

interface FirstIndexerStepProps {
    onAdvance: () => void;
}

export function FirstIndexerStep({ onAdvance }: FirstIndexerStepProps): ReactNode {
    const { toast } = useToast();
    const { createIndexer, isCreatingIndexer, indexers } = useIndexers();
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
                    <Button
                        type="button"
                        variant="secondary"
                        size="lg"
                        onClick={onAdvance}
                        className="setup-page__submit"
                        data-action="skip-indexer"
                    >
                        <SkipForwardIcon weight="bold" aria-hidden="true" />
                        Skip for now
                        <ArrowRightIcon weight="bold" aria-hidden="true" />
                    </Button>
                </>
            )}

            <IndexerForm
                open={formOpen}
                onOpenChange={setFormOpen}
                initialValues={null}
                onSubmit={handleSubmit}
                isSubmitting={isCreatingIndexer}
            />
        </div>
    );
}

FirstIndexerStep.displayName = 'FirstIndexerStep';
