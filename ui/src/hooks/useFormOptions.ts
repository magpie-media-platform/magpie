import { useMemo } from 'react';

import { useContentFilters } from '@/hooks/useFilters';
import { useQualityTierLists } from '@/hooks/useQualityTierLists';
import { useTags } from '@/hooks/useTags';
import type { ContentFilterOption, QualityTierListOption, TagOption } from '@/types';

export interface FormOptions {
    filterOptions: ContentFilterOption[];
    qualityTierListOptions: QualityTierListOption[];
    tagOptions: TagOption[];
}

export function useFormOptions(): FormOptions {
    const { filters: contentFilters } = useContentFilters();
    const { tierLists: qualityTierLists } = useQualityTierLists();
    const { tags: apiTags } = useTags();

    const filterOptions: ContentFilterOption[] = useMemo(
        () => contentFilters.map((f) => ({ id: f.id, name: f.name })),
        [contentFilters],
    );

    const qualityTierListOptions: QualityTierListOption[] = useMemo(
        () => qualityTierLists.map((q) => ({ id: q.id, name: q.name })),
        [qualityTierLists],
    );

    const tagOptions: TagOption[] = useMemo(() => apiTags.map((t) => ({ id: t.name, name: t.name })), [apiTags]);

    return { filterOptions, qualityTierListOptions, tagOptions };
}
