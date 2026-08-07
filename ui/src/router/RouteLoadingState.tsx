import { Skeleton } from '@/components/ui/Skeleton';

import './RouteLoadingState.css';

export function RouteLoadingState() {
    return (
        <div className="route-loading" data-component="route-loading" role="status" aria-label="Loading page">
            <div className="route-loading__header">
                <Skeleton variant="text" width="40%" height={28} />
                <Skeleton variant="text" width="20%" height={20} />
            </div>

            <div className="route-loading__content">
                <div className="route-loading__card">
                    <Skeleton variant="text" width="60%" height={20} />
                    <Skeleton variant="text" width="100%" height={16} />
                    <Skeleton variant="text" width="80%" height={16} />
                </div>
                <div className="route-loading__card">
                    <Skeleton variant="text" width="50%" height={20} />
                    <Skeleton variant="text" width="90%" height={16} />
                    <Skeleton variant="text" width="70%" height={16} />
                </div>
            </div>
        </div>
    );
}

RouteLoadingState.displayName = 'RouteLoadingState';
