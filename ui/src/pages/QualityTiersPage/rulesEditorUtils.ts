import { stripBlankRows } from '@/hooks/useFormDirty';

import type { ExclusionDraft } from './RulesEditor';
import { isBlankCriterion } from './criteriaEditorUtils';

function isBlankExclusion(exclusion: ExclusionDraft): boolean {
    return exclusion.name.trim() === '' && exclusion.criteria.every(isBlankCriterion);
}

export function normalizeExclusions(exclusions: readonly ExclusionDraft[]): ExclusionDraft[] {
    return stripBlankRows(exclusions, isBlankExclusion).map((exclusion) => ({
        ...exclusion,
        criteria: stripBlankRows(exclusion.criteria, isBlankCriterion),
    }));
}
