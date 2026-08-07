import { useRef } from 'react';

import { Badge, type BadgeVariant } from '@/components/ui/Badge';
import { InlineEditRow } from '@/components/ui/InlineEditRow';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useEffectiveQualityTierList, useSetQualityTierList } from '@/hooks/useContent';
import { useQualityTierLists } from '@/hooks/useQualityTierLists';
import { QualityStatusState, useQualityStatus, type QualityStatusStateValue } from '@/hooks/useQualityStatus';
import type { FulfillmentStatus } from '@/types/content';

interface QualityTierControlProps {
    contentHierarchyItemId: string;
    contentType?: string;
    level?: string;
    fulfillment?: FulfillmentStatus;
    canEdit?: boolean;
}

const INHERIT_VALUE = '__inherit__';

const describeMimeTypes = (patterns: string[]) => (patterns.length === 0 ? 'Any MIME type' : patterns.join(', '));

interface CutoffBadge {
    token: 'met-cutoff' | 'upgradable' | 'missing' | 'no-cutoff' | 'fulfilled';
    label: string;
    variant: BadgeVariant;
}

function resolveCutoffBadge(
    state: Exclude<QualityStatusStateValue, typeof QualityStatusState.NoTierList>,
    cutoffScore: number | null,
    isFulfilled: boolean,
): CutoffBadge {
    switch (state) {
        case QualityStatusState.MetCutoff:
            return { token: 'met-cutoff', label: 'Met cutoff', variant: 'fulfilled' };
        case QualityStatusState.Upgradable:
            return { token: 'upgradable', label: 'Upgradable', variant: 'upgradable' };
        case QualityStatusState.Missing:
            if (isFulfilled) {
                return { token: 'fulfilled', label: 'Fulfilled', variant: 'fulfilled' };
            }
            if (cutoffScore === null) {
                return { token: 'no-cutoff', label: 'No cutoff', variant: 'default' };
            }
            return { token: 'missing', label: 'Missing', variant: 'missing' };
    }
}

export function QualityTierControl({
    contentHierarchyItemId,
    contentType,
    level,
    fulfillment,
    canEdit = true,
}: QualityTierControlProps) {
    const { toast } = useToast();
    const { data: effective, isLoading } = useEffectiveQualityTierList(contentHierarchyItemId);
    const selectedTierListId = effective?.ownQualityTierListId ?? '';
    const { tierLists, isLoading: tierListsLoading } = useQualityTierLists(
        contentType ? { contentType, level } : undefined,
    );
    const { tierLists: allTierLists, isLoading: allTierListsLoading } = useQualityTierLists({
        enabled: !!contentType && !!selectedTierListId,
    });
    const { data: status } = useQualityStatus(contentHierarchyItemId);
    const setMutation = useSetQualityTierList();
    const pickedRef = useRef(false);
    const selectedTierList = (contentType ? allTierLists : tierLists).find((list) => list.id === selectedTierListId);

    const handleChange = async (value: string, close: () => void) => {
        const nextTierListId = value === INHERIT_VALUE ? null : value;
        if (setMutation.isPending) {
            return;
        }
        if (nextTierListId === (selectedTierListId || null)) {
            close();
            return;
        }
        try {
            await setMutation.mutateAsync({
                contentHierarchyItemId,
                qualityTierListId: nextTierListId,
            });
            toast({
                title: 'Quality updated',
                description: nextTierListId
                    ? 'The item now uses the selected quality.'
                    : 'The override was cleared. The item falls back to its inherited or default quality.',
                variant: 'success',
            });
            close();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'The update failed.';
            toast({ title: 'Update failed', description: message, variant: 'error' });
        }
    };

    const value = isLoading
        ? 'Resolving quality...'
        : effective?.qualityTierListName
          ? effective.qualityTierListName
          : 'No quality';

    const isFulfilled = fulfillment === 'fulfilled' || fulfillment === 'fulfilledExternally';
    const cutoff =
        status && status.status !== QualityStatusState.NoTierList
            ? resolveCutoffBadge(status.status, status.cutoffScore ?? null, isFulfilled)
            : null;

    return (
        <InlineEditRow
            label="Quality"
            data-testid="quality-tier-control"
            canEdit={canEdit}
            editAriaLabel="Edit quality"
            triggerTestId="quality-tier-control__trigger"
            readValue={
                <>
                    <span className="quality-tier-control__value" data-testid="quality-tier-control__label">
                        {value}
                    </span>
                    {cutoff && (
                        <Badge
                            variant={cutoff.variant}
                            size="sm"
                            data-testid="quality-tier-control__cutoff"
                            data-status={cutoff.token}
                        >
                            {cutoff.label}
                        </Badge>
                    )}
                </>
            }
        >
            {({ close }) => (
                <Select
                    defaultOpen
                    value={selectedTierListId || INHERIT_VALUE}
                    onValueChange={(value) => {
                        pickedRef.current = true;
                        void handleChange(value, close);
                    }}
                    onOpenChange={(open) => {
                        if (!open) {
                            queueMicrotask(() => {
                                if (pickedRef.current) {
                                    pickedRef.current = false;
                                } else {
                                    close();
                                }
                            });
                        }
                    }}
                    disabled={tierListsLoading || setMutation.isPending}
                >
                    <SelectTrigger size="sm" aria-label="Quality" data-testid="quality-tier-control__select">
                        {selectedTierListId
                            ? selectedTierList
                                ? `${selectedTierList.name} - ${describeMimeTypes(selectedTierList.mimeTypes)}`
                                : contentType && allTierListsLoading
                                  ? 'Loading...'
                                  : 'Unknown tier list'
                            : tierListsLoading
                              ? 'Loading...'
                              : 'Inherit'}
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value={INHERIT_VALUE}>{tierListsLoading ? 'Loading...' : 'Inherit'}</SelectItem>
                        {tierLists.map((list) => (
                            <SelectItem key={list.id} value={list.id}>
                                {list.name} - {describeMimeTypes(list.mimeTypes)}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>
            )}
        </InlineEditRow>
    );
}

QualityTierControl.displayName = 'QualityTierControl';
