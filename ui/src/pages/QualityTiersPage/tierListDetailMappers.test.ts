import { describe, expect, it } from 'vitest';

import type {
    ApiQualityTierList,
    ApiQualityTierRule,
    CriteriaValueOperator,
    QualityTierCriteria,
} from '@/hooks/useQualityTierLists';

import type { Criterion } from './TierForm';
import { operatorsForField } from './criterionOperators';
import {
    mapCriteriaToApi,
    mapCriteriaToDisplay,
    mapListRulesToApi,
    mapTierListToDisplay,
    mapTierRulesToApi,
} from './tierListDetailMappers';

function criterion(overrides: Partial<Criterion> = {}): Criterion {
    return {
        id: 'c1',
        field: 'source',
        operator: 'Equals',
        value: 'Bluray',
        ...overrides,
    };
}

describe('mapCriteriaToApi set operators', () => {
    it('persists an In rule to Values, not Value', () => {
        const criteria = mapCriteriaToApi([criterion({ operator: 'In', value: 'Bluray, WebDL, Remux' })]);

        const item = criteria.and?.[0]?.items?.source;
        expect(item?.operator).toBe('In');
        expect(item?.values).toEqual(['Bluray', 'WebDL', 'Remux']);
        expect(item?.value).toBeUndefined();
    });

    it('persists a Not In rule to Values', () => {
        const criteria = mapCriteriaToApi([criterion({ operator: 'NotIn', value: 'CAM, TS' })]);

        const item = criteria.and?.[0]?.items?.source;
        expect(item?.operator).toBe('NotIn');
        expect(item?.values).toEqual(['CAM', 'TS']);
    });

    it('trims and drops empty entries when splitting a set value', () => {
        const criteria = mapCriteriaToApi([criterion({ operator: 'In', value: ' a ,, b , ' })]);

        expect(criteria.and?.[0]?.items?.source?.values).toEqual(['a', 'b']);
    });

    it('round-trips an In set so a reload shows the joined set in one row', () => {
        const api = mapCriteriaToApi([criterion({ operator: 'In', value: 'Bluray, WebDL' })]);
        const display = mapCriteriaToDisplay(api, 'tier-1');

        expect(display).toHaveLength(1);
        expect(display[0].operator).toBe('In');
        expect(display[0].value).toBe('Bluray, WebDL');
    });
});

describe('mapCriteriaToApi single-value operators', () => {
    it('persists a Regex rule pattern to Value, not Values', () => {
        const criteria = mapCriteriaToApi([criterion({ operator: 'Regex', value: '^S\\d+E\\d+' })]);

        const item = criteria.and?.[0]?.items?.source;
        expect(item?.operator).toBe('Regex');
        expect(item?.value).toBe('^S\\d+E\\d+');
        expect(item?.values).toBeUndefined();
    });

    it('round-trips a Regex pattern unchanged', () => {
        const api = mapCriteriaToApi([criterion({ operator: 'Regex', value: '.*1080p.*' })]);
        const display = mapCriteriaToDisplay(api, 'tier-1');

        expect(display[0].operator).toBe('Regex');
        expect(display[0].value).toBe('.*1080p.*');
    });

    it('maps Ends With to the EndsWithString enum value', () => {
        const criteria = mapCriteriaToApi([criterion({ operator: 'EndsWith', value: 'Remux' })]);

        expect(criteria.and?.[0]?.items?.source?.operator).toBe('EndsWithString');
    });

    it('reads an EndsWithString criterion back as Ends With', () => {
        const display = mapCriteriaToDisplay(
            { and: [{ items: { source: { operator: 'EndsWithString', value: 'Remux' } } }] },
            'tier-1',
        );

        expect(display[0].operator).toBe('EndsWith');
        expect(display[0].value).toBe('Remux');
    });
});

describe('mapCriteria ContainsWord (data-corruption guard)', () => {
    it('reads a stored ContainsWord criterion back as ContainsWord, not Equals', () => {
        const display = mapCriteriaToDisplay(
            { and: [{ items: { source: { operator: 'ContainsWord', value: 'Remux' } } }] },
            'tier-1',
        );

        expect(display[0].operator).toBe('ContainsWord');
        expect(display[0].value).toBe('Remux');
    });

    it('persists a ContainsWord rule to Value, not Values', () => {
        const criteria = mapCriteriaToApi([criterion({ operator: 'ContainsWord', value: 'Remux' })]);

        const item = criteria.and?.[0]?.items?.source;
        expect(item?.operator).toBe('ContainsWord');
        expect(item?.value).toBe('Remux');
        expect(item?.values).toBeUndefined();
    });

    it('round-trips a stored ContainsWord criterion through load and save without degrading to Equals', () => {
        const stored = { and: [{ items: { source: { operator: 'ContainsWord' as const, value: 'Remux' } } }] };

        const display = mapCriteriaToDisplay(stored, 'tier-1');
        expect(display[0].operator).toBe('ContainsWord');

        const resaved = mapCriteriaToApi(display);
        const item = resaved.and?.[0]?.items?.source;
        expect(item?.operator).toBe('ContainsWord');
        expect(item?.operator).not.toBe('Equals');
        expect(item?.value).toBe('Remux');
    });
});

describe('mapOperator preserves operators the value-bearing editor cannot represent', () => {
    it('reads a stored IsNull criterion back as IsNull, not Equals', () => {
        const display = mapCriteriaToDisplay({ and: [{ items: { releaseDate: { operator: 'IsNull' } } }] }, 'tier-1');

        expect(display[0].operator).toBe('IsNull');
        expect(display[0].operator).not.toBe('Equals');
    });

    it('reads a stored IsNotNull criterion back as IsNotNull, not Equals', () => {
        const display = mapCriteriaToDisplay(
            { and: [{ items: { releaseDate: { operator: 'IsNotNull' } } }] },
            'tier-1',
        );

        expect(display[0].operator).toBe('IsNotNull');
        expect(display[0].operator).not.toBe('Equals');
    });

    it('round-trips a stored IsNull criterion through load and save unchanged', () => {
        const stored = { and: [{ items: { releaseDate: { operator: 'IsNull' as const } } }] };

        const display = mapCriteriaToDisplay(stored, 'tier-1');
        expect(display[0].operator).toBe('IsNull');

        const resaved = mapCriteriaToApi(display);
        const item = resaved.and?.[0]?.items?.releaseDate;
        expect(item?.operator).toBe('IsNull');
        expect(item?.operator).not.toBe('Equals');
    });

    it('round-trips a stored IsNotNull criterion through load and save unchanged', () => {
        const stored = { and: [{ items: { releaseDate: { operator: 'IsNotNull' as const } } }] };

        const resaved = mapCriteriaToApi(mapCriteriaToDisplay(stored, 'tier-1'));

        expect(resaved.and?.[0]?.items?.releaseDate?.operator).toBe('IsNotNull');
    });

    it('preserves an unknown wire operator instead of silently coercing it to Equals', () => {
        const stored = {
            and: [{ items: { source: { operator: 'SomeFutureOperator' as CriteriaValueOperator, value: 'x' } } }],
        };

        const display = mapCriteriaToDisplay(stored, 'tier-1');
        expect(display[0].operator).toBe('SomeFutureOperator');

        const resaved = mapCriteriaToApi(display);
        const item = resaved.and?.[0]?.items?.source;
        expect(item?.operator).toBe('SomeFutureOperator');
        expect(item?.operator).not.toBe('Equals');
    });
});

describe('value-bearing editor scoping', () => {
    it('never offers the unary null-check operators as selectable operators', () => {
        const fields = ['source', 'resolution', 'Title', 'group', 'size', 'age'];

        for (const field of fields) {
            const offered = operatorsForField(field).map((option) => option.value);
            expect(offered).not.toContain('IsNull');
            expect(offered).not.toContain('IsNotNull');
        }
    });
});

function matches(criteria: QualityTierCriteria, properties: Record<string, string>): boolean {
    return (criteria.and ?? []).every((group) =>
        Object.entries(group.items ?? {}).every(([field, value]) => properties[field] === value.value),
    );
}

function releaseOutcome(
    listRules: ApiQualityTierRule[],
    tierCriteria: QualityTierCriteria,
    tierRules: ApiQualityTierRule[],
    properties: Record<string, string>,
): string {
    if (listRules.some((rule) => rule.kind === 'Exclude' && matches(rule.criteria, properties))) {
        return 'list exclusion';
    }
    if (listRules.some((rule) => rule.kind === 'Accept' && !matches(rule.criteria, properties))) {
        return 'list criteria miss';
    }
    if (tierRules.some((rule) => rule.kind === 'Exclude' && matches(rule.criteria, properties))) {
        return 'tier exclusion';
    }
    if (tierRules.some((rule) => rule.kind === 'Accept' && !matches(rule.criteria, properties))) {
        return 'no tier match';
    }
    return matches(tierCriteria, properties) ? 'tier match' : 'no tier match';
}

describe('criteria and exclusions compatibility mapping', () => {
    const listRules: ApiQualityTierRule[] = [
        {
            id: 'list-source',
            name: 'Source gate',
            kind: 'Accept',
            criteria: { and: [{ items: { source: { operator: 'Equals', value: 'Bluray' } } }] },
        },
        {
            id: 'list-group',
            name: 'Blocked group',
            kind: 'Exclude',
            criteria: { and: [{ items: { group: { operator: 'Equals', value: 'YTS' } } }] },
        },
        {
            id: 'list-language',
            name: 'Language gate',
            kind: 'Accept',
            criteria: { and: [{ items: { language: { operator: 'Equals', value: 'English' } } }] },
        },
    ];
    const tierRules: ApiQualityTierRule[] = [
        {
            id: 'tier-codec',
            name: 'Codec gate',
            kind: 'Accept',
            criteria: { and: [{ items: { codec: { operator: 'Equals', value: 'x265' } } }] },
        },
        {
            id: 'tier-group',
            name: 'Tier blocked group',
            kind: 'Exclude',
            criteria: { and: [{ items: { group: { operator: 'Equals', value: 'EVO' } } }] },
        },
        {
            id: 'tier-hdr',
            name: 'HDR gate',
            kind: 'Accept',
            criteria: { and: [{ items: { hdr: { operator: 'Equals', value: 'true' } } }] },
        },
    ];
    const tierCriteria: QualityTierCriteria = {
        and: [{ items: { resolution: { operator: 'Equals', value: '1080p' } } }],
    };
    const apiTierList: ApiQualityTierList = {
        id: 'list-1',
        name: 'Compatibility list',
        mimeTypes: ['video/*'],
        upgradeAllowed: true,
        createdAt: '2026-07-23T00:00:00Z',
        rules: listRules,
        qualityTierRanks: [
            {
                qualityTierListId: 'list-1',
                qualityTierId: 'tier-1',
                ordinal: 0,
                isCutoff: false,
                createdAt: '2026-07-23T00:00:00Z',
                qualityTier: {
                    id: 'tier-1',
                    name: 'Compatibility tier',
                    criteria: tierCriteria,
                    rules: tierRules,
                    createdAt: '2026-07-23T00:00:00Z',
                },
            },
        ],
    };

    it('round-trips untouched list rules without changing IDs, names, criteria, or order', () => {
        const display = mapTierListToDisplay(apiTierList);

        const saved = mapListRulesToApi({
            criteria: display.criteria,
            exclusions: display.exclusions,
            sourceRules: display.sourceRules,
            criteriaChanged: false,
            exclusionsChanged: false,
        });

        expect(saved).toEqual(listRules);
    });

    it('normalizes edited list criteria to one rule while preserving the exclusion in place', () => {
        const display = mapTierListToDisplay(apiTierList);

        const saved = mapListRulesToApi({
            criteria: display.criteria,
            exclusions: display.exclusions,
            sourceRules: display.sourceRules,
            criteriaChanged: true,
            exclusionsChanged: false,
        });

        expect(saved).toEqual([
            {
                id: 'list-source',
                name: null,
                kind: 'Accept',
                criteria: {
                    and: [
                        {
                            items: {
                                source: { operator: 'Equals', value: 'Bluray' },
                                language: { operator: 'Equals', value: 'English' },
                            },
                        },
                    ],
                },
            },
            listRules[1],
        ]);
    });

    it('preserves release-score outcomes after list and tier UI round-trips with multiple legacy gates', () => {
        const display = mapTierListToDisplay(apiTierList);
        const displayedTier = display.tiers[0];
        const savedListRules = mapListRulesToApi({
            criteria: display.criteria,
            exclusions: display.exclusions,
            sourceRules: display.sourceRules,
            criteriaChanged: true,
            exclusionsChanged: false,
        });
        const savedTierCriteria = mapCriteriaToApi(displayedTier.criteria);
        const savedTierRules = mapTierRulesToApi(displayedTier.exclusions, displayedTier.sourceRules);
        const releases = [
            {
                source: 'Bluray',
                language: 'English',
                resolution: '1080p',
                codec: 'x265',
                hdr: 'true',
                group: 'PTP',
            },
            {
                source: 'WEB',
                language: 'English',
                resolution: '1080p',
                codec: 'x265',
                hdr: 'true',
                group: 'PTP',
            },
            {
                source: 'Bluray',
                language: 'French',
                resolution: '1080p',
                codec: 'x265',
                hdr: 'true',
                group: 'PTP',
            },
            {
                source: 'Bluray',
                language: 'English',
                resolution: '1080p',
                codec: 'x265',
                hdr: 'true',
                group: 'YTS',
            },
            {
                source: 'Bluray',
                language: 'English',
                resolution: '1080p',
                codec: 'x265',
                hdr: 'true',
                group: 'EVO',
            },
            {
                source: 'Bluray',
                language: 'English',
                resolution: '1080p',
                codec: 'x264',
                hdr: 'true',
                group: 'PTP',
            },
            {
                source: 'Bluray',
                language: 'English',
                resolution: '720p',
                codec: 'x265',
                hdr: 'true',
                group: 'PTP',
            },
        ];
        const expected = [
            'tier match',
            'list criteria miss',
            'list criteria miss',
            'list exclusion',
            'tier exclusion',
            'no tier match',
            'no tier match',
        ];

        const before = releases.map((release) => releaseOutcome(listRules, tierCriteria, tierRules, release));
        const after = releases.map((release) =>
            releaseOutcome(savedListRules, savedTierCriteria, savedTierRules, release),
        );

        expect(before).toEqual(expected);
        expect(after).toEqual(expected);
        expect(after).toEqual(before);
        expect(savedTierRules).toEqual([tierRules[1]]);
        expect(savedTierCriteria.and).toEqual([
            {
                items: {
                    resolution: { operator: 'Equals', value: '1080p' },
                    codec: { operator: 'Equals', value: 'x265' },
                    hdr: { operator: 'Equals', value: 'true' },
                },
            },
        ]);
    });

    it('maps empty criteria and exclusions to no additional eligibility gate', () => {
        const saved = mapListRulesToApi({
            criteria: [],
            exclusions: [],
            sourceRules: listRules,
            criteriaChanged: true,
            exclusionsChanged: true,
        });

        expect(saved).toEqual([]);
    });
});
