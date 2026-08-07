import type { Criterion } from './CriteriaEditor';

export function isBlankCriterion(criterion: Criterion): boolean {
    return criterion.value.trim() === '';
}
