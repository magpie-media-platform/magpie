import { Link } from 'react-router-dom';
import { WarningCircleIcon, ArrowCounterClockwiseIcon, HouseIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

import './RouteErrorState.css';

export interface RouteErrorStateProps {
    message?: string;

    onRetry?: () => void;
}

export function RouteErrorState({ message = 'Try again or go to the dashboard.', onRetry }: RouteErrorStateProps) {
    return (
        <div className="route-error" data-component="route-error">
            <EmptyState
                icon={<WarningCircleIcon weight="duotone" />}
                title="Failed to load page"
                description={message}
                action={
                    <div className="route-error__actions">
                        {onRetry && (
                            <Button variant="primary" onClick={onRetry}>
                                <ArrowCounterClockwiseIcon weight="bold" aria-hidden="true" />
                                Retry
                            </Button>
                        )}
                        <Button asChild variant="secondary">
                            <Link to="/">
                                <HouseIcon weight="bold" aria-hidden="true" />
                                Go to Dashboard
                            </Link>
                        </Button>
                    </div>
                }
            />
        </div>
    );
}

RouteErrorState.displayName = 'RouteErrorState';
