import { useCallback, type ReactNode } from 'react';
import { ArrowRightIcon, FilmStripIcon, SkipForwardIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { useMediaServers } from '@/hooks/useMediaServers';
import { useInstanceName } from '@/hooks/useInstanceName';
import { MediaServerForm, type MediaServerFormValues } from '@/components/configuration/MediaServerForm';
import { toStringSettings } from '@/lib/integrationSettings';

import { AddAnotherActions } from './AddAnotherActions';
import { useAddAnotherStep } from './useAddAnotherStep';

interface FirstMediaServerStepProps {
    onAdvance: (skipped?: boolean) => void;
}

export function FirstMediaServerStep({ onAdvance }: FirstMediaServerStepProps): ReactNode {
    const instanceName = useInstanceName();
    const { toast } = useToast();
    const { createServer, isCreating, servers } = useMediaServers();
    const { formOpen, setFormOpen, added, markAdded, addAnother, continueToNext } = useAddAnotherStep(onAdvance);

    const handleSubmit = useCallback(
        async (values: MediaServerFormValues) => {
            try {
                await createServer({
                    providerType: values.provider,
                    name: values.name,
                    enabled: values.enabled,
                    settings: toStringSettings(values.settings),
                });
                toast({
                    title: 'Media server created',
                    description: `${instanceName} created ${values.name}.`,
                    variant: 'success',
                });
                markAdded();
            } catch (error) {
                toast({
                    title: 'Failed to create media server',
                    description:
                        error instanceof Error ? error.message : 'Try again from the Media Servers admin page.',
                    variant: 'error',
                });
                throw error;
            }
        },
        [createServer, markAdded, toast, instanceName],
    );

    const configuredMediaServerCountLabel = `${servers.length} media server${servers.length === 1 ? '' : 's'}`;

    return (
        <div className="setup-page__form" data-step="server">
            <p className="setup-page__hint">
                A media server connection lets {instanceName} refresh its library after imports. One can be added now,
                or this step can be skipped.
            </p>
            {servers.length > 0 && (
                <p className="setup-page__hint" data-state="already-added">
                    {configuredMediaServerCountLabel} already configured.
                </p>
            )}
            {added ? (
                <AddAnotherActions
                    label="media server"
                    action="media-server"
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
                        data-action="open-media-server-form"
                    >
                        <FilmStripIcon weight="bold" aria-hidden="true" />
                        Add a media server
                    </Button>
                    <Button
                        type="button"
                        variant="secondary"
                        size="lg"
                        onClick={() => onAdvance(true)}
                        className="setup-page__submit"
                        data-action="skip-media-server"
                    >
                        <SkipForwardIcon weight="bold" aria-hidden="true" />
                        Skip for now
                        <ArrowRightIcon weight="bold" aria-hidden="true" />
                    </Button>
                </>
            )}

            <MediaServerForm
                open={formOpen}
                onOpenChange={setFormOpen}
                initialValues={null}
                onSubmit={handleSubmit}
                isSubmitting={isCreating}
            />
        </div>
    );
}

FirstMediaServerStep.displayName = 'FirstMediaServerStep';
