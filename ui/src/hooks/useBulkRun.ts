import { useCallback } from 'react';

import { useToast } from '@/components/ui/Toast';

interface BulkRunSelection {
    selectOnly: (ids: string[]) => void;
}

export interface BulkRunOptions<TItem> {
    action: (item: TItem) => Promise<unknown>;

    verb: string;

    itemNounPlural: string;

    /** Skips the run's failure toast, for an action whose per-item result is itself the feedback. */
    suppressToast?: boolean;
}

export interface BulkRunResult<TItem> {
    succeeded: TItem[];

    failed: TItem[];
}

/** Returns a runner for a bulk mutation over a selection, which reports any failures in one toast. */
export function useBulkRun() {
    const { toast } = useToast();

    return useCallback(
        async function run<TItem extends { id: string }>(
            items: readonly TItem[],
            selection: BulkRunSelection,
            { action, verb, itemNounPlural, suppressToast }: BulkRunOptions<TItem>,
        ): Promise<BulkRunResult<TItem>> {
            const succeeded: TItem[] = [];
            const failed: TItem[] = [];

            for (const item of items) {
                try {
                    await action(item);
                    succeeded.push(item);
                } catch {
                    failed.push(item);
                }
            }

            selection.selectOnly(failed.map((item) => item.id));

            if (!suppressToast && failed.length > 0) {
                toast({
                    title:
                        succeeded.length > 0
                            ? `Failed to ${verb} some ${itemNounPlural}`
                            : `Failed to ${verb} ${itemNounPlural}`,
                    description:
                        succeeded.length > 0
                            ? `${succeeded.length} succeeded and ${failed.length} failed. Failed ${itemNounPlural} remain selected.`
                            : `${failed.length} failed. Failed ${itemNounPlural} remain selected.`,
                    variant: succeeded.length > 0 ? 'warning' : 'error',
                });
            }

            return { succeeded, failed };
        },
        [toast],
    );
}
