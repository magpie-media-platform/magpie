import { Badge } from '@/components/ui/Badge';
import { downloadStatusLabels, downloadStatusVariants, isFailedDownloadStatus } from '@/hooks/useDownloads';
import type { DownloadStatusValue } from '@/types';

export interface ReleaseStatusBadgeProps {
    status: DownloadStatusValue;

    /** Shown as the badge title when the status is a failure. */
    failureReason?: string | null;
}

function humanizeFailureReason(reason?: string | null): string | undefined {
    if (!reason) {
        return undefined;
    }
    return reason.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function ReleaseStatusBadge({ status, failureReason }: ReleaseStatusBadgeProps) {
    return (
        <Badge
            variant={downloadStatusVariants[status]}
            size="sm"
            dot
            title={isFailedDownloadStatus(status) ? humanizeFailureReason(failureReason) : undefined}
            data-acquisition-status={status}
        >
            {downloadStatusLabels[status]}
        </Badge>
    );
}
