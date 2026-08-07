import { ArrowClockwiseIcon, CheckCircleIcon, HourglassIcon } from '@phosphor-icons/react';

import { usePendingRefresh } from '@/hooks/useContent';

import './RefreshStatusBadge.css';

interface RefreshStatusBadgeProps {
    contentId: string | undefined;
}

export function RefreshStatusBadge({ contentId }: RefreshStatusBadgeProps) {
    const { data } = usePendingRefresh(contentId);

    if (!data || data.state === 'None') {
        return null;
    }

    if (data.state === 'Queued') {
        return (
            <span
                className="refresh-status-badge"
                data-tone="queued"
                data-component="refresh-status-badge"
                data-testid="refresh-status-badge"
            >
                <HourglassIcon weight="bold" aria-hidden="true" />
                <span>Refresh queued</span>
            </span>
        );
    }

    if (data.state === 'Running') {
        return (
            <span
                className="refresh-status-badge"
                data-tone="running"
                data-component="refresh-status-badge"
                data-testid="refresh-status-badge"
            >
                <ArrowClockwiseIcon weight="bold" aria-hidden="true" className="refresh-status-badge__spinner" />
                <span>Refreshing</span>
            </span>
        );
    }

    return (
        <span
            className="refresh-status-badge"
            data-tone="recently-completed"
            data-component="refresh-status-badge"
            data-testid="refresh-status-badge"
        >
            <CheckCircleIcon weight="bold" aria-hidden="true" />
            <span>Refresh complete</span>
        </span>
    );
}

RefreshStatusBadge.displayName = 'RefreshStatusBadge';

export default RefreshStatusBadge;
