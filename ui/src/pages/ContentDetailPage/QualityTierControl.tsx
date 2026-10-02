import { InlineEditRow } from '@/components/ui/InlineEditRow';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useEffectiveQualityTierList, useSetQualityTierList } from '@/hooks/useContent';
import { useLookupError } from '@/hooks/useLookupError';
import { useQualityTierLists } from '@/hooks/useQualityTierLists';

interface QualityTierControlProps {
    contentHierarchyItemId: string;
    canEdit?: boolean;
}

const INHERIT_VALUE = '__inherit__';

const describeMimeTypes = (patterns: string[]) => (patterns.length === 0 ? 'Any MIME type' : patterns.join(', '));

export function QualityTierControl({ contentHierarchyItemId, canEdit = true }: QualityTierControlProps) {
    const { toast } = useToast();
    const effectiveQuery = useEffectiveQualityTierList(contentHierarchyItemId);
    const { data: effective, isLoading, isFetching, refetch } = effectiveQuery;
    const lookupError = useLookupError(effectiveQuery);
    const selectedTierListId = effective?.ownQualityTierListId ?? '';
    const { tierLists, isLoading: tierListsLoading } = useQualityTierLists();
    const setMutation = useSetQualityTierList();
    const selectedTierList = tierLists.find((list) => list.id === selectedTierListId);

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

    const value = lookupError
        ? 'Quality could not be loaded'
        : isLoading
          ? 'Resolving quality...'
          : effective?.qualityTierListName
            ? effective.qualityTierListName
            : 'No quality';

    return (
        <InlineEditRow
            label="Quality"
            data-testid="quality-tier-control"
            canEdit={canEdit}
            editAriaLabel="Edit quality"
            triggerTestId="quality-tier-control__trigger"
            error={lookupError}
            onRetry={() => void refetch()}
            retrying={isFetching}
            retryAriaLabel="Retry loading quality"
            readValue={
                <span className="quality-tier-control__value" data-testid="quality-tier-control__label">
                    {value}
                </span>
            }
        >
            {({ close }) => (
                <Select
                    defaultOpen
                    value={selectedTierListId || INHERIT_VALUE}
                    onValueChange={(value) => {
                        void handleChange(value, close);
                    }}
                    onDismiss={close}
                    disabled={tierListsLoading || setMutation.isPending}
                >
                    <SelectTrigger size="sm" aria-label="Quality" data-testid="quality-tier-control__select">
                        {selectedTierListId
                            ? selectedTierList
                                ? `${selectedTierList.name} - ${describeMimeTypes(selectedTierList.mimeTypes)}`
                                : tierListsLoading
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
