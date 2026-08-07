export interface PropertyCriterion<T> {
    operator?: string;

    value?: T;

    values?: T[];

    negate?: boolean;
}

export interface DictionaryCriteria<TCriterion> {
    items: Record<string, TCriterion>;
}

export interface ApiEventFilter {
    eventName?: PropertyCriterion<string>;

    eventContext?: DictionaryCriteria<PropertyCriterion<string>>;
}

export interface EditableEventContextCriterion {
    key: string;

    pattern: string | null;

    operator?: string | null;

    values?: string[] | null;

    negate: boolean;
}

export function eventContextToEditableCriteria(
    eventContext: DictionaryCriteria<PropertyCriterion<string>> | undefined,
): EditableEventContextCriterion[] {
    return Object.entries(eventContext?.items ?? {}).map(([key, criterion]) => ({
        key,
        pattern: criterion.value ?? null,
        operator: criterion.operator ?? null,
        values: criterion.values === undefined ? null : [...criterion.values],
        negate: criterion.negate ?? false,
    }));
}

export function editableCriteriaToEventContext(
    criteria: EditableEventContextCriterion[],
): DictionaryCriteria<PropertyCriterion<string>> | undefined {
    if (criteria.length === 0) {
        return undefined;
    }

    return {
        items: Object.fromEntries(
            criteria.map((criterion) => {
                const wireCriterion: PropertyCriterion<string> = {
                    negate: criterion.negate,
                };

                if (criterion.operator !== null) {
                    wireCriterion.operator = criterion.operator ?? 'regex';
                }
                if (criterion.pattern !== null) {
                    wireCriterion.value = criterion.pattern;
                }
                if (criterion.values !== null && criterion.values !== undefined) {
                    wireCriterion.values = [...criterion.values];
                }

                return [criterion.key, wireCriterion];
            }),
        ),
    };
}

export function countExecutableEventContextCriteria(
    eventContext: DictionaryCriteria<PropertyCriterion<string>> | undefined,
): number {
    return Object.keys(eventContext?.items ?? {}).filter((key) => key.trim() !== '').length;
}
