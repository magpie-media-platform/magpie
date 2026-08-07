import { WarningIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';

import { useRestartRequired } from '@/hooks/ui/useRestartRequired';

import './RestartRequiredBanner.css';

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
                icon={<WarningIcon weight="fill" aria-hidden="true" />}
                dismissible
                onDismiss={dismiss}
            >
                Restart Magpie to apply pending extension changes.
            </Alert>
        </div>
    );
}

export default RestartRequiredBanner;
