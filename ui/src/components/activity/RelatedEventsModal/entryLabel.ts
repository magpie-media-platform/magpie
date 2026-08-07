import { MagpieActivityEntryKind, type MagpieActivityEntry } from '@/hooks/useMagpieActivity';
import { humanizeCustomKey } from '@/lib/contentTypeDisplay';
import { humanizeJobType } from '@/lib/jobExecution';

export function entryLabel(entry: MagpieActivityEntry): string {
    if (entry.kind === MagpieActivityEntryKind.JobExecution) {
        return humanizeJobType(entry.label);
    }
    return humanizeCustomKey(entry.label) || entry.label;
}
