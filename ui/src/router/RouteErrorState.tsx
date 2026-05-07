import { Link } from 'react-router-dom';
import { WarningCircle, ArrowCounterClockwise, House } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

import './RouteErrorState.css';

export interface RouteErrorStateProps {
    message?: string;

    onRetry?: () => void;
}

export function RouteErrorState({
    message = 'Something went wrong while loading this page.',
    onRetry,
}: RouteErrorStateProps) {
    return (
        <div className="route-error" data-component="route-error">
            <EmptyState
                icon={<WarningCircle weight="duotone" />}
                title="Failed to load page"
                description={message}
                action={
                    <div className="route-error__actions">
                        {onRetry && (
                            <Button variant="primary" onClick={onRetry}>
                                <ArrowCounterClockwise weight="bold" aria-hidden="true" />
                                Retry
                            </Button>
                        )}
                        <Button asChild variant="secondary">
                            <Link to="/">
                                <House weight="bold" aria-hidden="true" />
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

export default RouteErrorState;
