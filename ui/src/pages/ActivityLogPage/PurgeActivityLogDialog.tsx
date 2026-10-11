import { useId, useMemo, useState } from 'react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useJobSpecs } from '@/hooks/useJobs';
import { type AuditHistoryPurgeCounts, usePurgeAuditHistory } from '@/hooks/useMagpieActivity';
import { useScheduleTriggers } from '@/hooks/useScheduleTriggers';
import { getErrorMessage } from '@/lib/errors';

const RETENTION_CLEANUP_JOB_TYPE = 'RetentionCleanup';

const PURGE_AGES = [
    { days: 7, label: '7 days' },
    { days: 30, label: '30 days' },
    { days: 90, label: '90 days' },
    { days: 365, label: '1 year' },
];

interface PurgeActivityLogDialogProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
}

export function PurgeActivityLogDialog({ open, onOpenChange }: PurgeActivityLogDialogProps) {
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent size="alert" data-component="purge-activity-log-dialog">
                <PurgeActivityLogForm onDone={() => onOpenChange(false)} />
            </DialogContent>
        </Dialog>
    );
}

function PurgeActivityLogForm({ onDone }: { onDone: () => void }) {
    const selectId = useId();
    const [olderThanDays, setOlderThanDays] = useState(30);
    const { toast } = useToast();
    const { purge, isPurging, error: purgeError } = usePurgeAuditHistory();
    const { triggers } = useScheduleTriggers();
    const { specs } = useJobSpecs();

    const onPurged = (counts: AuditHistoryPurgeCounts) => {
        toast({
            title: 'Activity log purged',
            description: `Deleted ${counts.auditRecords.toLocaleString()} audit entries and ${counts.jobExecutions.toLocaleString()} job runs.`,
            variant: 'success',
        });
        onDone();
    };

    const retention = useMemo(() => {
        const trigger = triggers.find((item) => item.jobType === RETENTION_CLEANUP_JOB_TYPE && item.enabled);
        const spec = specs.find((item) => item.jobType === RETENTION_CLEANUP_JOB_TYPE);
        if (!trigger || !spec) {
            return null;
        }
        const days = (name: string) =>
            trigger.parameters?.[name] ?? spec.parameters?.find((parameter) => parameter.name === name)?.defaultValue;
        return { events: days('auditRetentionDays'), jobRuns: days('jobExecutionRetentionDays') };
    }, [triggers, specs]);

    const ageLabel = PURGE_AGES.find((age) => age.days === olderThanDays)?.label;

    return (
        <>
            <DialogHeader>
                <DialogTitle>Purge activity log</DialogTitle>
            </DialogHeader>
            <DialogBody className="purge-activity-log">
                <div className="purge-activity-log__field">
                    <Label htmlFor={selectId}>Purge everything older than</Label>
                    <Select value={String(olderThanDays)} onValueChange={(value) => setOlderThanDays(Number(value))}>
                        <SelectTrigger id={selectId} />
                        <SelectContent>
                            {PURGE_AGES.map((age) => (
                                <SelectItem key={age.days} value={String(age.days)}>
                                    {age.label}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                </div>
                <DialogDescription>{`Delete activity older than ${ageLabel}.`}</DialogDescription>
                {retention && (
                    <p className="purge-activity-log__retention">
                        {`Magpie automatically purges events older than ${retention.events} days and job runs older than ${retention.jobRuns} days.`}
                    </p>
                )}
                {purgeError && <Alert variant="error">{getErrorMessage(purgeError)}</Alert>}
            </DialogBody>
            <DialogFooter>
                <Button variant="secondary" onClick={onDone} disabled={isPurging}>
                    Cancel
                </Button>
                <Button
                    variant="destructive"
                    loading={isPurging}
                    onClick={() => void purge(olderThanDays).then(onPurged, () => undefined)}
                    data-action="confirm-purge"
                >
                    Purge
                </Button>
            </DialogFooter>
        </>
    );
}
