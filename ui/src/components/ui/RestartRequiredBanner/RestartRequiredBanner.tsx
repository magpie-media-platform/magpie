import { Warning } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';

import { useRestartRequired } from './RestartRequiredContext';

import './RestartRequiredBanner.css';

/** App-wide banner shown when the backend reports a host restart is required. Dismissable; reappears on the next restart-required event. */
export function RestartRequiredBanner() {
    const { restartRequired, dismissed, dismiss } = useRestartRequired();

    if (!restartRequired || dismissed) {
        return null;
    }

    return (
        <div className="restart-required-banner" data-component="restart-required-banner">
            <Alert
                variant="warning"
                title="Restart required"
                icon={<Warning weight="fill" aria-hidden="true" />}
                dismissible
                onDismiss={dismiss}
            >
                Magpie has pending extension changes that will only take effect after the host restarts.
            </Alert>
        </div>
    );
}

export default RestartRequiredBanner;
