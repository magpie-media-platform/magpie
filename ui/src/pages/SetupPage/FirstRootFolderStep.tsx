import { useCallback, type ReactNode } from 'react';
import { ArrowRightIcon, FolderSimplePlusIcon, SkipForwardIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useRootFolders } from '@/hooks/useRootFolders';
import { RootFolderForm, type RootFolderFormValues } from '@/components/configuration/RootFolderForm';

import { AddAnotherActions } from './AddAnotherActions';
import { useAddAnotherStep } from './useAddAnotherStep';

interface FirstRootFolderStepProps {
    onAdvance: (skipped?: boolean) => void;
}

export function FirstRootFolderStep({ onAdvance }: FirstRootFolderStepProps): ReactNode {
    const { toast } = useToast();
    const { contentTypes } = useLibraryTypes();
    const { createFolder, isCreating, folders } = useRootFolders();
    const { formOpen, setFormOpen, added, markAdded, addAnother, continueToNext } = useAddAnotherStep(onAdvance);

    const handleSubmit = useCallback(
        async (values: RootFolderFormValues) => {
            try {
                await createFolder({
                    name: values.name,
                    path: values.path,
                    contentType: values.contentType,
                    isDefault: values.isDefault,
                    defaultMonitoring: values.defaultMonitoring,
                    defaults: values.defaults,
                });
                toast({
                    title: 'Root folder created',
                    description: `Magpie created ${values.name}.`,
                    variant: 'success',
                });
                markAdded();
            } catch (error) {
                toast({
                    title: 'Failed to create root folder',
                    description: error instanceof Error ? error.message : 'Try again from the Root Folders admin page.',
                    variant: 'error',
                });
                throw error;
            }
        },
        [createFolder, markAdded, toast],
    );

    const configuredRootFolderCountLabel = `${folders.length} root folder${folders.length === 1 ? '' : 's'}`;

    return (
        <div className="setup-page__form" data-step="root">
            <p className="setup-page__hint">
                Root folders tell Magpie where to store imported content. One can be added now, or configured later
                under Admin {'>'} Root Folders.
            </p>
            {folders.length > 0 && (
                <p className="setup-page__hint" data-state="already-added">
                    {configuredRootFolderCountLabel} already configured.
                </p>
            )}
            {added ? (
                <AddAnotherActions
                    label="root folder"
                    action="root-folder"
                    onAddAnother={addAnother}
                    onContinue={continueToNext}
                    disabled={isCreating}
                />
            ) : (
                <>
                    <Button
                        type="button"
                        variant="primary"
                        size="lg"
                        onClick={() => setFormOpen(true)}
                        disabled={isCreating}
                        className="setup-page__submit"
                        data-action="open-root-folder-form"
                    >
                        <FolderSimplePlusIcon weight="bold" aria-hidden="true" />
                        Add a root folder
                    </Button>
                    <Button
                        type="button"
                        variant="secondary"
                        size="lg"
                        onClick={() => onAdvance(true)}
                        className="setup-page__submit"
                        data-action="skip-root-folder"
                    >
                        <SkipForwardIcon weight="bold" aria-hidden="true" />
                        Skip for now
                        <ArrowRightIcon weight="bold" aria-hidden="true" />
                    </Button>
                </>
            )}

            <RootFolderForm
                open={formOpen}
                onOpenChange={setFormOpen}
                initialValues={null}
                contentTypes={contentTypes}
                onSubmit={handleSubmit}
                isSubmitting={isCreating}
            />
        </div>
    );
}

FirstRootFolderStep.displayName = 'FirstRootFolderStep';
