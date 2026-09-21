import { HourglassIcon } from '@phosphor-icons/react';

import { usePendingRefresh } from '@/hooks/useContent';

import './RefreshStatusBadge.css';

interface RefreshStatusBadgeProps {
    contentId: string | undefined;
}

export function RefreshStatusBadge({ contentId }: RefreshStatusBadgeProps) {
    const { data: pendingMetadataRefresh } = usePendingRefresh(contentId);

    if (!pendingMetadataRefresh) {
        return null;
    }

    return (
        <span
            className="refresh-status-badge"
            data-tone="queued"
            data-component="refresh-status-badge"
            data-testid="refresh-status-badge"
        >
            <HourglassIcon weight="bold" aria-hidden="true" />
            <span>Refresh pending</span>
        </span>
    );
}

RefreshStatusBadge.displayName = 'RefreshStatusBadge';

export default RefreshStatusBadge;
