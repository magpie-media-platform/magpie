import type { ReactNode } from 'react';
import { ArrowCounterClockwiseIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';

export interface LoadErrorAlertProps {
    onRetry?: () => void;
}

export function LoadErrorAlert({ onRetry }: LoadErrorAlertProps): ReactNode {
    return (
        <Alert variant="error" icon={<WarningCircleIcon weight="duotone" />} title="Failed to load">
            {onRetry && (
                <Button variant="ghost" size="sm" onClick={onRetry}>
                    <ArrowCounterClockwiseIcon weight="bold" aria-hidden="true" />
                    Try again
                </Button>
            )}
        </Alert>
    );
}

LoadErrorAlert.displayName = 'LoadErrorAlert';

export default LoadErrorAlert;
