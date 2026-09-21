import { useEffect, useMemo, useState } from 'react';

import { WarningIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { useSchedulerStatus } from '@/hooks/useSchedulerStatus';

export function SchedulerStallBanner() {
    const { data } = useSchedulerStatus();

    const stalledSince = useMemo(() => {
        if (!data?.stalledSinceUtc) {
            return null;
        }
        const since = new Date(data.stalledSinceUtc);
        if (Number.isNaN(since.getTime())) {
            return null;
        }
        return since;
    }, [data?.stalledSinceUtc]);

    const recoveredAt = useMemo(() => {
        if (!data?.recoveredAtUtc) {
            return null;
        }
        const at = new Date(data.recoveredAtUtc);
        if (Number.isNaN(at.getTime())) {
            return null;
        }
        return at;
    }, [data?.recoveredAtUtc]);

    const [showRecovery, setShowRecovery] = useState(false);
    useEffect(() => {
        const remaining = recoveredAt ? recoveredAt.getTime() + 60_000 - Date.now() : 0;
        setShowRecovery(remaining > 0);
        if (remaining <= 0) {
            return;
        }
        const timer = setTimeout(() => setShowRecovery(false), remaining);
        return () => clearTimeout(timer);
    }, [recoveredAt]);

    if (!data) {
        return null;
    }

    if (data.isStalled) {
        const since = stalledSince ? stalledSince.toLocaleString() : 'unknown';
        return (
            <Alert
                variant="warning"
                icon={<WarningIcon weight="duotone" aria-hidden="true" />}
                title="Scheduler stalled"
                data-testid="scheduler-stall-banner"
            >
                Magpie had no eligible instance after {since}. Queued jobs will start when an eligible instance returns.
                Review the extension status of each instance.
            </Alert>
        );
    }

    if (recoveredAt && showRecovery) {
        return (
            <Alert variant="success" title="Scheduler recovered" data-testid="scheduler-recovered-banner">
                Eligibility returned at {recoveredAt.toLocaleString()}.
            </Alert>
        );
    }

    return null;
}

SchedulerStallBanner.displayName = 'SchedulerStallBanner';
