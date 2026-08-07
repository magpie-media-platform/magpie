import { forwardRef, type ComponentPropsWithoutRef } from 'react';

import { DetailsPanel } from '@/components/ui/DetailsPanel';
import { ExternalIdDetails, type ExternalId } from '@/components/ui/ExternalIds';
import { humanizeCustomKey } from '@/lib/contentTypeDisplay';
import { formatDurationFromMinutes } from '@/lib/formatTime';
import type { CustomMetadataValue } from '@/lib/metadata';

import './ContentTabs.css';

interface ContentFactField {
    key: string;

    displayName?: string | null;

    dataType?: string | null;

    format?: string | null;
}

export interface ContentFacts {
    factFields?: ContentFactField[];

    factValues?: Record<string, string | number | null | undefined>;

    externalIds?: ExternalId[];

    customValues?: CustomMetadataValue[];
}

export interface ContentFactsPanelProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    facts: ContentFacts;
}

function formatFactValue(value: string | number | null | undefined, field: ContentFactField): string | null {
    if (value === null || value === undefined || value === '') {
        return null;
    }
    if (field.format === 'duration') {
        const minutes = typeof value === 'number' ? value : Number(value);
        return Number.isFinite(minutes) ? formatDurationFromMinutes(minutes) : null;
    }
    return String(value);
}

export const ContentFactsPanel = forwardRef<HTMLDivElement, ContentFactsPanelProps>(
    ({ facts, className, ...props }, ref) => {
        const classes = ['content-facts', 'details-panel-group', className].filter(Boolean).join(' ');

        const detailRows = (facts.factFields ?? [])
            .map((field) => ({
                key: field.key,
                label: field.displayName?.trim() || humanizeCustomKey(field.key),
                value: formatFactValue(facts.factValues?.[field.key], field),
            }))
            .filter((row): row is { key: string; label: string; value: string } => row.value !== null);
        const detailKeys = new Set(detailRows.map((row) => row.key.trim().toLocaleLowerCase()));
        const customRows = (facts.customValues ?? [])
            .map((entry) => ({
                key: entry.key.trim(),
                label: humanizeCustomKey(entry.key.trim()),
                value: entry.value.trim(),
            }))
            .filter((row) => row.key && row.value && !detailKeys.has(row.key.toLocaleLowerCase()))
            .sort((a, b) => a.label.localeCompare(b.label, 'en', { sensitivity: 'base' }));
        const seenKeys = new Set<string>();
        const mergedRows = [...detailRows, ...customRows].filter((row) => {
            const key = row.key.toLocaleLowerCase();
            if (seenKeys.has(key)) {
                return false;
            }
            seenKeys.add(key);
            return true;
        });
        const externalIds = facts.externalIds;
        const hasExternalIds = !!externalIds?.length;

        if (mergedRows.length === 0 && !hasExternalIds) {
            return null;
        }

        return (
            <div ref={ref} className={classes} data-component="content-facts" {...props}>
                <DetailsPanel title="Details" rows={mergedRows} />
                {hasExternalIds && <ExternalIdDetails ids={externalIds!} />}
            </div>
        );
    },
);

ContentFactsPanel.displayName = 'ContentFactsPanel';

export default ContentFactsPanel;
