import { ArrowClockwiseIcon, CircleNotchIcon, PowerIcon, WarningIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { useHostRestart } from '@/hooks/useHostRestart';
import { useInstanceName } from '@/hooks/useInstanceName';

import './RestartHostPanel.css';

export function RestartHostPanel() {
    const instanceName = useInstanceName();
    const { capabilityLoading, canRestart, phase, error, openConfirm, cancelConfirm, requestRestart, reset, recover } =
        useHostRestart();

    const isBusy = phase === 'restarting' || phase === 'reconnecting';

    return (
        <Card className="restart-host-panel" data-component="restart-host-panel" data-phase={phase}>
            <div className="restart-host-panel__intro">
                <div className="restart-host-panel__icon" aria-hidden="true">
                    <PowerIcon weight="duotone" />
                </div>
                <div className="restart-host-panel__copy">
                    <h3 className="restart-host-panel__title">Restart service</h3>
                </div>
            </div>

            {!capabilityLoading && !canRestart && (
                <Alert
                    variant="warning"
                    title="Restart unavailable"
                    icon={<WarningIcon weight="fill" aria-hidden="true" />}
                    data-testid="restart-unavailable"
                >
                    This deployment has no supervisor to relaunch {instanceName}, so a restart cannot be performed.
                </Alert>
            )}

            {error && phase === 'error' && (
                <Alert
                    variant="error"
                    title="Restart failed"
                    icon={<WarningIcon weight="fill" aria-hidden="true" />}
                    data-testid="restart-error"
                >
                    {error}
                </Alert>
            )}

            <div className="restart-host-panel__actions">
                <Button
                    variant="secondary"
                    onClick={openConfirm}
                    disabled={capabilityLoading || !canRestart || isBusy}
                    data-action="restart-host"
                    title={
                        !canRestart && !capabilityLoading ? 'Restart is not available in this deployment.' : undefined
                    }
                >
                    <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                    Restart service
                </Button>
            </div>

            <Dialog
                open={phase === 'confirming'}
                onOpenChange={(isOpen) => {
                    if (!isOpen) {
                        cancelConfirm();
                    }
                }}
            >
                <DialogContent size="alert" data-component="restart-confirm-dialog">
                    <DialogHeader>
                        <DialogTitle>Restart {instanceName}?</DialogTitle>
                        <DialogDescription>
                            {instanceName} will shut down and the supervisor will relaunch it. The interface will be
                            briefly unavailable while it reconnects. Running jobs stop. Each job retries or stays
                            failed, according to its recovery policy.
                        </DialogDescription>
                    </DialogHeader>
                    <DialogFooter>
                        <Button variant="ghost" onClick={cancelConfirm} data-action="cancel">
                            Cancel
                        </Button>
                        <Button variant="primary" onClick={requestRestart} data-action="confirm-restart">
                            <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                            Restart
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>

            {(phase === 'restarting' || phase === 'reconnecting' || phase === 'recovered') && (
                <div
                    className="restart-host-panel__overlay"
                    data-component="restart-reconnecting-overlay"
                    data-phase={phase}
                    role="status"
                    aria-live="polite"
                >
                    <div className="restart-host-panel__overlay-card">
                        {phase === 'recovered' ? (
                            <>
                                <ArrowClockwiseIcon
                                    weight="bold"
                                    className="restart-host-panel__overlay-icon"
                                    aria-hidden="true"
                                />
                                <h3 className="restart-host-panel__overlay-title">{instanceName} is back online</h3>
                                <p className="restart-host-panel__overlay-message">
                                    {instanceName} has restarted and is responding again.
                                </p>
                                <div className="restart-host-panel__overlay-actions">
                                    <Button variant="primary" onClick={recover} data-action="reload">
                                        Reload {instanceName}
                                    </Button>
                                    <Button variant="ghost" onClick={reset} data-action="dismiss">
                                        Dismiss
                                    </Button>
                                </div>
                            </>
                        ) : (
                            <>
                                <CircleNotchIcon
                                    weight="bold"
                                    className="restart-host-panel__overlay-icon restart-host-panel__overlay-icon--spin"
                                    aria-hidden="true"
                                />
                                <h3 className="restart-host-panel__overlay-title">Reconnecting...</h3>
                                <p className="restart-host-panel__overlay-message">
                                    {phase === 'restarting'
                                        ? 'Requesting a graceful restart...'
                                        : `Waiting for ${instanceName} to come back online.`}
                                </p>
                            </>
                        )}
                    </div>
                </div>
            )}
        </Card>
    );
}

RestartHostPanel.displayName = 'RestartHostPanel';

export default RestartHostPanel;
