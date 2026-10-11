import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowClockwiseIcon, ArrowRightIcon, PlusIcon } from '@phosphor-icons/react';

import { InstallExtensionDialog } from '@/components/configuration/InstallExtensionDialog';
import { Button } from '@/components/ui/Button';
import { useRestartRequired } from '@/hooks/ui/useRestartRequired';
import { useExtensionActions } from '@/hooks/useExtensionManagement';
import { useHostRestart } from '@/hooks/useHostRestart';
import { useInstanceName } from '@/hooks/useInstanceName';
import { useSystemInfo } from '@/hooks/useSystemStatus';
import { getErrorMessage } from '@/lib/errors';

interface ExtensionsStepProps {
    onAdvance: () => void | Promise<void>;
}

export function ExtensionsStep({ onAdvance }: ExtensionsStepProps): ReactNode {
    const instanceName = useInstanceName();
    const queryClient = useQueryClient();
    const systemInfo = useSystemInfo();
    const actions = useExtensionActions();
    const { restartRequired, setRestartRequired } = useRestartRequired();
    const { capabilityLoading, canRestart, phase, error: restartError, requestRestart, reset } = useHostRestart();

    const [installOpen, setInstallOpen] = useState(false);
    const [isContinuing, setIsContinuing] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const isRestarting = phase === 'restarting' || phase === 'reconnecting';

    const handleRestarted = useCallback(() => {
        setRestartRequired(false);
        void queryClient.invalidateQueries();
    }, [setRestartRequired, queryClient]);

    useEffect(() => {
        if (phase === 'recovered') {
            reset();
            handleRestarted();
        }
    }, [phase, reset, handleRestarted]);

    const handleRestart = useCallback(() => {
        setError(null);
        void requestRestart();
    }, [requestRestart]);

    const handleRecheck = useCallback(async () => {
        const startedAt = systemInfo.data?.startedAt;
        const result = await systemInfo.refetch();
        if (result.data?.startedAt !== startedAt) {
            handleRestarted();
        }
    }, [systemInfo, handleRestarted]);

    const handleInstall = useCallback(
        async (packageId: string, version: string) => {
            await actions.installExtension({ packageId, version: version || undefined });
        },
        [actions],
    );

    const handleInstallFromFile = useCallback(
        async (file: File) => {
            await actions.installExtensionFromFile(file);
        },
        [actions],
    );

    const handleContinue = useCallback(async () => {
        setError(null);
        setIsContinuing(true);
        try {
            await onAdvance();
        } catch (err) {
            setError(getErrorMessage(err));
        } finally {
            setIsContinuing(false);
        }
    }, [onAdvance]);

    const displayedError = error ?? restartError;

    return (
        <div className="setup-page__form" data-step="extensions">
            <Button
                type="button"
                variant={restartRequired ? 'secondary' : 'primary'}
                size="lg"
                onClick={() => setInstallOpen(true)}
                disabled={isRestarting}
                className="setup-page__submit"
                data-action="add-extension"
            >
                <PlusIcon weight="bold" aria-hidden="true" />
                Add extension
            </Button>

            {restartRequired && (
                <>
                    <p className="setup-page__subtitle" data-state="restart-required">
                        Restart {instanceName} to apply pending extension changes.
                    </p>
                    {canRestart ? (
                        <Button
                            type="button"
                            variant="primary"
                            size="lg"
                            onClick={handleRestart}
                            loading={isRestarting}
                            className="setup-page__submit"
                            data-action="restart-host"
                        >
                            <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                            Restart
                        </Button>
                    ) : (
                        !capabilityLoading && (
                            <Button
                                type="button"
                                variant="primary"
                                size="lg"
                                onClick={handleRecheck}
                                loading={systemInfo.isFetching}
                                className="setup-page__submit"
                                data-action="recheck-extensions"
                            >
                                <ArrowClockwiseIcon weight="bold" aria-hidden="true" />I have restarted {instanceName}
                            </Button>
                        )
                    )}
                </>
            )}

            {displayedError && (
                <p className="setup-page__error" role="alert" data-testid="setup-error">
                    {displayedError}
                </p>
            )}

            <Button
                type="button"
                variant="secondary"
                size="lg"
                onClick={handleContinue}
                loading={isContinuing}
                disabled={isRestarting}
                className="setup-page__submit"
                data-action="continue-extensions"
            >
                Continue
                <ArrowRightIcon weight="bold" aria-hidden="true" />
            </Button>

            <InstallExtensionDialog
                open={installOpen}
                onOpenChange={setInstallOpen}
                onInstall={handleInstall}
                onInstallFromFile={handleInstallFromFile}
                isInstalling={actions.isInstalling}
                isInstallingFromFile={actions.isInstallingFromFile}
            />
        </div>
    );
}

ExtensionsStep.displayName = 'ExtensionsStep';
