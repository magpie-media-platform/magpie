import { Badge } from '../Badge';
import type { AcquisitionStatus } from '@/types';

const ACQUISITION_LABELS: Record<AcquisitionStatus, string> = {
    grabbed: 'Grabbed',
    queued: 'Queued',
    downloading: 'Downloading',
    paused: 'Paused',
    stalled: 'Stalled',
    downloaded: 'Downloaded',
};

export function AcquisitionBadge({ status }: { status: AcquisitionStatus }) {
    return (
        <Badge variant={status} size="sm" dot data-acquisition-status={status}>
            {ACQUISITION_LABELS[status]}
        </Badge>
    );
}

AcquisitionBadge.displayName = 'AcquisitionBadge';

export default AcquisitionBadge;
