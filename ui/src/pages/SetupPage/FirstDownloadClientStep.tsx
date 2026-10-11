import { useCallback, type ReactNode } from 'react';
import { ArrowRightIcon, DownloadSimpleIcon, SkipForwardIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { useDownloadClients } from '@/hooks/useDownloadClients';
import { useInstanceName } from '@/hooks/useInstanceName';
import { DownloadClientForm, type DownloadClientFormValues } from '@/components/configuration/DownloadClientForm';
import { toStringSettings } from '@/lib/integrationSettings';

import { AddAnotherActions } from './AddAnotherActions';
import { useAddAnotherStep } from './useAddAnotherStep';

interface FirstDownloadClientStepProps {
    onAdvance: (skipped?: boolean) => void;
}

export function FirstDownloadClientStep({ onAdvance }: FirstDownloadClientStepProps): ReactNode {
    const instanceName = useInstanceName();
    const { toast } = useToast();
    const { createClient, isCreating, clients } = useDownloadClients();
    const { formOpen, setFormOpen, added, markAdded, addAnother, continueToNext } = useAddAnotherStep(onAdvance);

    const handleSubmit = useCallback(
        async (values: DownloadClientFormValues) => {
            try {
                await createClient({
                    providerType: values.provider,
                    name: values.name,
                    enabled: values.enabled,
                    settings: toStringSettings(values.settings),
                });
                toast({
                    title: 'Download client created',
                    description: `${instanceName} created ${values.name}.`,
                    variant: 'success',
                });
                markAdded();
            } catch (error) {
                toast({
                    title: 'Failed to create download client',
                    description:
                        error instanceof Error ? error.message : 'Try again from the Download Clients admin page.',
                    variant: 'error',
                });
                throw error;
            }
        },
        [createClient, markAdded, toast, instanceName],
    );

    const configuredDownloadClientCountLabel = `${clients.length} download client${clients.length === 1 ? '' : 's'}`;

    return (
        <div className="setup-page__form" data-step="download">
            <p className="setup-page__hint">
                Download clients receive releases found by indexers. One can be added now, or configured later under
                Admin {'>'} Download Clients.
            </p>
            {clients.length > 0 && (
                <p className="setup-page__hint" data-state="already-added">
                    {configuredDownloadClientCountLabel} already configured.
                </p>
            )}
            {added ? (
                <AddAnotherActions
                    label="download client"
                    action="download-client"
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
                        data-action="open-download-client-form"
                    >
                        <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                        Add a download client
                    </Button>
                    <Button
                        type="button"
                        variant="secondary"
                        size="lg"
                        onClick={() => onAdvance(true)}
                        className="setup-page__submit"
                        data-action="skip-download-client"
                    >
                        <SkipForwardIcon weight="bold" aria-hidden="true" />
                        Skip for now
                        <ArrowRightIcon weight="bold" aria-hidden="true" />
                    </Button>
                </>
            )}

            <DownloadClientForm
                open={formOpen}
                onOpenChange={setFormOpen}
                initialValues={null}
                onSubmit={handleSubmit}
                isSubmitting={isCreating}
            />
        </div>
    );
}

FirstDownloadClientStep.displayName = 'FirstDownloadClientStep';
