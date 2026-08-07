import type {
    ApiQualityTierList,
    ApiQualityTierListRank,
    ApiQualityTierRule,
    CriteriaValueOperator,
    QualityTierCriteria,
    StringPropertyCriterion,
} from '@/hooks/useQualityTierLists';

import type { Criterion } from './TierForm';
import { isSetOperator } from './criterionOperators';
import type { ExclusionDraft } from './RulesEditor';
import { normalizeExclusions } from './rulesEditorUtils';

type TierCriterion = Criterion;

export interface TierWithCriteria {
    id: string;
    name: string;
    isCutoff: boolean;
    criteriaCount: number;
    criteria: TierCriterion[];
    exclusions: ExclusionDraft[];
    sourceRules: ApiQualityTierRule[];
}

interface TierListDetail {
    id: string;
    name: string;
    mimeTypes: string[];
    upgradeAllowed: boolean;
    tiers: TierWithCriteria[];
    criteria: TierCriterion[];
    exclusions: ExclusionDraft[];
    sourceRules: ApiQualityTierRule[];
    createdAt: string;
}

const operatorNames: Partial<Record<CriteriaValueOperator, TierCriterion['operator']>> = {
    Equals: 'Equals',
    NotEquals: 'NotEquals',
    In: 'In',
    NotIn: 'NotIn',
    ContainsString: 'Contains',
    NotContainsString: 'NotContains',
    StartsWithString: 'StartsWith',
    NotStartsWithString: 'NotStartsWith',
    EndsWithString: 'EndsWith',
    NotEndsWithString: 'NotEndsWith',
    ContainsWord: 'ContainsWord',
    Regex: 'Regex',
    GreaterThan: 'GreaterThan',
    GreaterThanOrEqual: 'GreaterThanOrEqual',
    LessThan: 'LessThan',
    LessThanOrEqual: 'LessThanOrEqual',
};

const operatorValues: Record<string, CriteriaValueOperator> = {
    Equals: 'Equals',
    NotEquals: 'NotEquals',
    In: 'In',
    NotIn: 'NotIn',
    Contains: 'ContainsString',
    NotContains: 'NotContainsString',
    StartsWith: 'StartsWithString',
    NotStartsWith: 'NotStartsWithString',
    EndsWith: 'EndsWithString',
    NotEndsWith: 'NotEndsWithString',
    ContainsWord: 'ContainsWord',
    Regex: 'Regex',
    GreaterThan: 'GreaterThan',
    GreaterThanOrEqual: 'GreaterThanOrEqual',
    LessThan: 'LessThan',
    LessThanOrEqual: 'LessThanOrEqual',
};

function mapOperator(op: CriteriaValueOperator | undefined): TierCriterion['operator'] {
    if (op === undefined) {
        return 'Equals';
    }
    return operatorNames[op] ?? op;
}

function splitSetValues(value: string): string[] {
    return value
        .split(',')
        .map((part) => part.trim())
        .filter((part) => part !== '');
}

export function mapCriteriaToApi(criteria: Criterion[]): QualityTierCriteria {
    if (criteria.length === 0) {
        return { and: [] };
    }

    const and: { items: Record<string, StringPropertyCriterion> }[] = [];
    let items: Record<string, StringPropertyCriterion> = {};

    for (const c of criteria) {
        if (Object.prototype.hasOwnProperty.call(items, c.field)) {
            and.push({ items });
            items = {};
        }

        const operator: CriteriaValueOperator = operatorValues[c.operator] ?? (c.operator as CriteriaValueOperator);
        items[c.field] = isSetOperator(c.operator)
            ? { operator, values: splitSetValues(c.value) }
            : { operator, value: c.value };
    }

    and.push({ items });

    return { and };
}

export function mapCriteriaToDisplay(criteria: QualityTierCriteria, tierId: string): TierCriterion[] {
    const result: TierCriterion[] = [];
    let index = 0;

    if (!criteria.and) {
        return result;
    }

    for (const dict of criteria.and) {
        if (!dict.items) {
            continue;
        }

        for (const [field, criterion] of Object.entries(dict.items)) {
            const op = mapOperator(criterion.operator);

            if (isSetOperator(op)) {
                result.push({
                    id: `${tierId}-c-${index++}`,
                    field,
                    operator: op,
                    value: (criterion.values ?? []).join(', '),
                });
            } else if (criterion.values && criterion.values.length > 0) {
                for (const v of criterion.values) {
                    result.push({
                        id: `${tierId}-c-${index++}`,
                        field,
                        operator: op,
                        value: v,
                    });
                }
            } else {
                result.push({
                    id: `${tierId}-c-${index++}`,
                    field,
                    operator: op,
                    value: criterion.value ?? '',
                });
            }
        }
    }

    return result;
}

function combineCriteria(...criteriaSets: QualityTierCriteria[]): QualityTierCriteria {
    return {
        and: criteriaSets.flatMap((criteria) => criteria.and ?? []),
    };
}

function acceptCriteria(rules: readonly ApiQualityTierRule[]): QualityTierCriteria {
    return combineCriteria(...rules.filter((rule) => rule.kind === 'Accept').map((rule) => rule.criteria));
}

function mapExclusionsToDisplay(rules: ApiQualityTierRule[] | undefined): ExclusionDraft[] {
    if (!rules) {
        return [];
    }

    return rules
        .filter((rule) => rule.kind === 'Exclude')
        .map((rule) => ({
            id: rule.id || crypto.randomUUID(),
            name: rule.name ?? '',
            criteria: mapCriteriaToDisplay(rule.criteria, rule.id || 'exclusion'),
        }));
}

function mapExclusionsToApi(exclusions: ExclusionDraft[]): ApiQualityTierRule[] {
    return normalizeExclusions(exclusions).map((exclusion) => ({
        id: exclusion.id,
        name: exclusion.name.trim() ? exclusion.name.trim() : null,
        kind: 'Exclude',
        criteria: mapCriteriaToApi(exclusion.criteria),
    }));
}

function sameExclusions(exclusions: ExclusionDraft[], sourceRules: ApiQualityTierRule[]): boolean {
    return JSON.stringify(exclusions) === JSON.stringify(mapExclusionsToDisplay(sourceRules));
}

function mergeRules(
    sourceRules: readonly ApiQualityTierRule[],
    acceptRules: readonly ApiQualityTierRule[],
    exclusionRules: readonly ApiQualityTierRule[],
): ApiQualityTierRule[] {
    const merged: ApiQualityTierRule[] = [];
    let acceptsInserted = false;
    let exclusionIndex = 0;

    for (const sourceRule of sourceRules) {
        if (sourceRule.kind === 'Accept') {
            if (!acceptsInserted) {
                merged.push(...acceptRules);
                acceptsInserted = true;
            }
            continue;
        }

        if (sourceRule.kind === 'Exclude') {
            const exclusion = exclusionRules[exclusionIndex++];
            if (exclusion) {
                merged.push(exclusion);
            }
            continue;
        }

        merged.push(sourceRule);
    }

    if (!acceptsInserted) {
        merged.unshift(...acceptRules);
    }
    merged.push(...exclusionRules.slice(exclusionIndex));

    return merged;
}

export function mapTierRulesToApi(
    exclusions: ExclusionDraft[],
    sourceRules: ApiQualityTierRule[],
): ApiQualityTierRule[] {
    const exclusionRules = sameExclusions(exclusions, sourceRules)
        ? sourceRules.filter((rule) => rule.kind === 'Exclude')
        : mapExclusionsToApi(exclusions);

    return mergeRules(sourceRules, [], exclusionRules);
}

interface MapListRulesToApiOptions {
    criteria: Criterion[];
    exclusions: ExclusionDraft[];
    sourceRules: ApiQualityTierRule[];
    criteriaChanged: boolean;
    exclusionsChanged: boolean;
}

export function mapListRulesToApi({
    criteria,
    exclusions,
    sourceRules,
    criteriaChanged,
    exclusionsChanged,
}: MapListRulesToApiOptions): ApiQualityTierRule[] {
    if (!criteriaChanged && !exclusionsChanged) {
        return sourceRules;
    }

    const existingAcceptRules = sourceRules.filter((rule) => rule.kind === 'Accept');
    const acceptRules: ApiQualityTierRule[] = criteriaChanged
        ? criteria.length === 0
            ? []
            : [
                  {
                      id: existingAcceptRules[0]?.id || crypto.randomUUID(),
                      name: null,
                      kind: 'Accept',
                      criteria: mapCriteriaToApi(criteria),
                  },
              ]
        : existingAcceptRules;
    const exclusionRules = exclusionsChanged
        ? mapExclusionsToApi(exclusions)
        : sourceRules.filter((rule) => rule.kind === 'Exclude');

    return mergeRules(sourceRules, acceptRules, exclusionRules);
}

function mapTierRankToDisplay(rank: ApiQualityTierListRank): TierWithCriteria {
    const tier = rank.qualityTier;
    const sourceRules = tier?.rules ?? [];
    const combinedCriteria = combineCriteria(tier?.criteria ?? { and: [] }, acceptCriteria(sourceRules));
    const criteria = mapCriteriaToDisplay(combinedCriteria, rank.qualityTierId);

    return {
        id: rank.qualityTierId,
        name: tier?.name ?? 'Unknown Tier',
        isCutoff: rank.isCutoff,
        criteriaCount: criteria.length,
        criteria,
        exclusions: mapExclusionsToDisplay(sourceRules),
        sourceRules,
    };
}

export function mapTierListToDisplay(apiTierList: ApiQualityTierList): TierListDetail {
    const sortedRanks = [...apiTierList.qualityTierRanks].sort((a, b) => a.ordinal - b.ordinal);
    const sourceRules = apiTierList.rules ?? [];

    return {
        id: apiTierList.id,
        name: apiTierList.name,
        mimeTypes: apiTierList.mimeTypes ?? [],
        upgradeAllowed: apiTierList.upgradeAllowed,
        tiers: sortedRanks.map(mapTierRankToDisplay),
        criteria: mapCriteriaToDisplay(acceptCriteria(sourceRules), `${apiTierList.id}-list`),
        exclusions: mapExclusionsToDisplay(sourceRules),
        sourceRules,
        createdAt: apiTierList.createdAt,
    };
}

export function formatOperator(operator: TierCriterion['operator']): string {
    switch (operator) {
        case 'Equals':
            return '=';
        case 'NotEquals':
            return '≠';
        case 'In':
            return 'in';
        case 'NotIn':
            return 'not in';
        case 'Contains':
            return 'contains';
        case 'NotContains':
        case 'NotContainsString':
            return 'not contains';
        case 'StartsWith':
        case 'StartsWithString':
            return 'starts with';
        case 'NotStartsWith':
        case 'NotStartsWithString':
            return 'not starts with';
        case 'EndsWith':
        case 'EndsWithString':
            return 'ends with';
        case 'NotEndsWith':
        case 'NotEndsWithString':
            return 'not ends with';
        case 'ContainsString':
            return 'contains';
        case 'IsNull':
            return 'is null';
        case 'IsNotNull':
            return 'is not null';
        case 'ContainsWord':
            return 'contains word';
        case 'Regex':
            return 'matches';
        case 'GreaterThan':
            return '>';
        case 'GreaterThanOrEqual':
            return '≥';
        case 'LessThan':
            return '<';
        case 'LessThanOrEqual':
            return '≤';
    }
}
