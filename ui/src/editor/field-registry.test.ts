import { describe, expect, it } from 'vitest';

import contentFieldCatalog from './content-field-catalog.txt?raw';
import {
    contentFieldRegistry,
    getCollectionField,
    getOperatorsForType,
    getScalarField,
    isCollectionField,
    subjectFieldRegistry,
    type FieldType,
} from './field-registry';

describe('Field Registry', () => {
    it('matches the backend content field catalog', () => {
        const describeType = (field: { type: FieldType; enumValues?: string[] }) =>
            field.enumValues ? `enum ${[...field.enumValues].sort().join('|')}` : field.type;
        const scalars = contentFieldRegistry.scalarFields.map((field) => `scalar ${field.name} ${describeType(field)}`);
        const collections = contentFieldRegistry.collectionFields.flatMap((field) =>
            [field.name, ...field.aliases].map((name) =>
                [`collection ${name}`, ...field.itemFields.map((item) => `${item.name}:${item.type}`).sort()].join(' '),
            ),
        );

        expect([...scalars, ...collections].sort()).toEqual(contentFieldCatalog.trim().split(/\r?\n/));
    });

    describe('Content Scalar Fields', () => {
        const expectedContentScalarFields: Record<string, FieldType> = {
            name: 'string',
            slug: 'string',
            path: 'string',
            contentType: 'string',
            level: 'string',
            monitoring: 'enum',
            fulfillment: 'enum',
            activeAcquisitionStatus: 'enum',
            qualityStatus: 'enum',
            pendingMetadataRefresh: 'boolean',
            hasSearchableTitle: 'boolean',
            importPending: 'boolean',
            primaryMediaSize: 'long',
            primaryMediaFileCount: 'number',
            rootFolderId: 'guid',
            parentId: 'guid',
            title: 'string',
            originalTitle: 'string',
            language: 'string',
            publishDate: 'date',
            duration: 'timespan',
            productionStatus: 'string',
        };

        it.each(Object.entries(expectedContentScalarFields))(
            'has the content scalar field %s with type %s',
            (fieldName, expectedType) => {
                const field = getScalarField(contentFieldRegistry, fieldName);
                expect(field?.type).toBe(expectedType);
            },
        );

        it('has no extra content scalar fields', () => {
            const expectedNames = Object.keys(expectedContentScalarFields);
            const actualNames = contentFieldRegistry.scalarFields.map((f) => f.name);
            expect(actualNames.sort()).toEqual(expectedNames.sort());
        });

        it('monitoring is an enum of monitoring modes', () => {
            const field = getScalarField(contentFieldRegistry, 'monitoring');
            expect(field?.enumValues).toEqual(['Unmonitored', 'Monitored', 'Shotgun']);
        });

        it('advertises the supported fulfillment values in the existing catalog', () => {
            expect(getScalarField(contentFieldRegistry, 'Fulfillment')?.enumValues).toEqual([
                'Missing',
                'Partial',
                'Upgradable',
                'Fulfilled',
                'FulfilledExternally',
            ]);
        });
    });

    describe('Content Collection Fields', () => {
        const expectedContentCollectionFields: Array<{
            name: string;
            aliases: string[];
            primaryField: string;
            itemFieldNames: string[];
            itemFieldTypes: Record<string, FieldType>;
        }> = [
            {
                name: 'genre',
                aliases: ['genres'],
                primaryField: 'name',
                itemFieldNames: ['name'],
                itemFieldTypes: { name: 'string' },
            },
            {
                name: 'tag',
                aliases: ['tags'],
                primaryField: 'name',
                itemFieldNames: ['name'],
                itemFieldTypes: { name: 'string' },
            },
            {
                name: 'award',
                aliases: ['awards'],
                primaryField: 'name',
                itemFieldNames: ['body', 'name', 'category', 'status', 'year', 'language', 'country'],
                itemFieldTypes: {
                    body: 'string',
                    name: 'string',
                    category: 'string',
                    status: 'string',
                    year: 'number',
                    language: 'string',
                    country: 'string',
                },
            },
            {
                name: 'externalId',
                aliases: ['externalIds'],
                primaryField: 'value',
                itemFieldNames: ['source', 'value'],
                itemFieldTypes: { source: 'string', value: 'string' },
            },
            {
                name: 'contentRating',
                aliases: ['contentRatings'],
                primaryField: 'rating',
                itemFieldNames: ['body', 'rating'],
                itemFieldTypes: { body: 'string', rating: 'string' },
            },
            {
                name: 'image',
                aliases: ['images'],
                primaryField: 'type',
                itemFieldNames: ['type', 'mimeType', 'url', 'source', 'language', 'width', 'height'],
                itemFieldTypes: {
                    type: 'string',
                    mimeType: 'string',
                    url: 'string',
                    source: 'string',
                    language: 'string',
                    width: 'number',
                    height: 'number',
                },
            },
            {
                name: 'relationship',
                aliases: ['relationships'],
                primaryField: 'kind',
                itemFieldNames: [
                    'kind',
                    'type',
                    'qualifier',
                    'subject',
                    'name',
                    'relatedMetadataContainerId',
                    'externalId',
                    'externalIdSource',
                ],
                itemFieldTypes: {
                    kind: 'string',
                    type: 'string',
                    qualifier: 'string',
                    subject: 'string',
                    name: 'string',
                    relatedMetadataContainerId: 'guid',
                    externalId: 'string',
                    externalIdSource: 'string',
                },
            },
            {
                name: 'mediaFile',
                aliases: ['mediaFiles'],
                primaryField: 'fileKind',
                itemFieldNames: ['fileKind', 'mimeType', 'filePath', 'fileSize'],
                itemFieldTypes: {
                    fileKind: 'string',
                    mimeType: 'string',
                    filePath: 'string',
                    fileSize: 'long',
                },
            },
            {
                name: 'subtree',
                aliases: [],
                primaryField: 'monitoring',
                itemFieldNames: ['monitoring', 'fulfillment'],
                itemFieldTypes: {
                    monitoring: 'enum',
                    fulfillment: 'enum',
                },
            },
        ];

        it.each(expectedContentCollectionFields)('$name has the expected aliases', (expected) => {
            const field = getCollectionField(contentFieldRegistry, expected.name);
            expect(field?.aliases).toEqual(expected.aliases);
        });

        it.each(expectedContentCollectionFields)('$name has the expected primary field', (expected) => {
            const field = getCollectionField(contentFieldRegistry, expected.name);
            expect(field?.primaryField).toBe(expected.primaryField);
        });

        it.each(expectedContentCollectionFields)('$name has the expected item fields', (expected) => {
            const field = getCollectionField(contentFieldRegistry, expected.name);
            const actualNames = field?.itemFields.map((f) => f.name).sort();
            expect(actualNames).toEqual([...expected.itemFieldNames].sort());
        });

        it.each(expectedContentCollectionFields)('$name has the expected item field types', (expected) => {
            const field = getCollectionField(contentFieldRegistry, expected.name);
            expect(Object.fromEntries((field?.itemFields ?? []).map((item) => [item.name, item.type]))).toEqual(
                expected.itemFieldTypes,
            );
        });

        it.each(expectedContentCollectionFields)('$name resolves from each of its aliases', (expected) => {
            const resolved = expected.aliases.map((alias) => getCollectionField(contentFieldRegistry, alias)?.name);
            expect(resolved).toEqual(expected.aliases.map(() => expected.name));
        });

        it('has no extra content collection fields', () => {
            const expectedNames = expectedContentCollectionFields.map((f) => f.name);
            const actualNames = contentFieldRegistry.collectionFields.map((f) => f.name);
            expect(actualNames.sort()).toEqual(expectedNames.sort());
        });
    });

    describe('Subject Scalar Fields', () => {
        const expectedSubjectScalarFields: Record<string, FieldType> = {
            kind: 'string',
            title: 'string',
        };

        it.each(Object.entries(expectedSubjectScalarFields))(
            'has the subject scalar field %s with type %s',
            (fieldName, expectedType) => {
                const field = getScalarField(subjectFieldRegistry, fieldName);
                expect(field?.type).toBe(expectedType);
            },
        );

        it('has no extra subject scalar fields', () => {
            const expectedNames = Object.keys(expectedSubjectScalarFields);
            const actualNames = subjectFieldRegistry.scalarFields.map((f) => f.name);
            expect(actualNames.sort()).toEqual(expectedNames.sort());
        });
    });

    describe('Subject Collection Fields', () => {
        const expectedSubjectCollectionFields: Array<{
            name: string;
            aliases: string[];
            primaryField: string;
            itemFieldNames: string[];
            itemFieldTypes: Record<string, FieldType>;
        }> = [
            {
                name: 'tag',
                aliases: ['tags'],
                primaryField: 'name',
                itemFieldNames: ['name'],
                itemFieldTypes: { name: 'string' },
            },
        ];

        it('advertises tag but not genre or contentRating collections', () => {
            expect(getCollectionField(subjectFieldRegistry, 'tag')).toBeDefined();
            expect(getCollectionField(subjectFieldRegistry, 'tags')).toBeDefined();
            expect(getCollectionField(subjectFieldRegistry, 'genre')).toBeUndefined();
            expect(getCollectionField(subjectFieldRegistry, 'genres')).toBeUndefined();
            expect(getCollectionField(subjectFieldRegistry, 'contentRating')).toBeUndefined();
            expect(getCollectionField(subjectFieldRegistry, 'contentRatings')).toBeUndefined();
        });

        it.each(expectedSubjectCollectionFields)('$name has the expected aliases', (expected) => {
            const field = getCollectionField(subjectFieldRegistry, expected.name);
            expect(field?.aliases).toEqual(expected.aliases);
        });

        it.each(expectedSubjectCollectionFields)('$name has the expected primary field', (expected) => {
            const field = getCollectionField(subjectFieldRegistry, expected.name);
            expect(field?.primaryField).toBe(expected.primaryField);
        });

        it.each(expectedSubjectCollectionFields)('$name has the expected item fields', (expected) => {
            const field = getCollectionField(subjectFieldRegistry, expected.name);
            const actualNames = field?.itemFields.map((f) => f.name).sort();
            expect(actualNames).toEqual([...expected.itemFieldNames].sort());
        });

        it.each(expectedSubjectCollectionFields)('$name has the expected item field types', (expected) => {
            const field = getCollectionField(subjectFieldRegistry, expected.name);
            expect(Object.fromEntries((field?.itemFields ?? []).map((item) => [item.name, item.type]))).toEqual(
                expected.itemFieldTypes,
            );
        });

        it('has no extra subject collection fields', () => {
            const expectedNames = expectedSubjectCollectionFields.map((f) => f.name);
            const actualNames = subjectFieldRegistry.collectionFields.map((f) => f.name);
            expect(actualNames.sort()).toEqual(expectedNames.sort());
        });
    });

    describe('Case-Insensitive Lookup', () => {
        it('getScalarField is case-insensitive', () => {
            expect(getScalarField(contentFieldRegistry, 'Name')?.name).toBe('name');
            expect(getScalarField(contentFieldRegistry, 'NAME')?.name).toBe('name');
            expect(getScalarField(contentFieldRegistry, 'nAmE')?.name).toBe('name');
            expect(getScalarField(contentFieldRegistry, 'rootFolderId')?.name).toBe('rootFolderId');
            expect(getScalarField(contentFieldRegistry, 'ROOTFOLDERID')?.name).toBe('rootFolderId');
        });

        it('getCollectionField is case-insensitive for names', () => {
            expect(getCollectionField(contentFieldRegistry, 'Genre')?.name).toBe('genre');
            expect(getCollectionField(contentFieldRegistry, 'GENRE')?.name).toBe('genre');
            expect(getCollectionField(contentFieldRegistry, 'Award')?.name).toBe('award');
        });

        it('getCollectionField is case-insensitive for aliases', () => {
            expect(getCollectionField(contentFieldRegistry, 'Genres')?.name).toBe('genre');
            expect(getCollectionField(contentFieldRegistry, 'GENRES')?.name).toBe('genre');
            expect(getCollectionField(contentFieldRegistry, 'Awards')?.name).toBe('award');
            expect(getCollectionField(contentFieldRegistry, 'AWARDS')?.name).toBe('award');
        });
    });

    describe('isCollectionField', () => {
        it('returns true for collection field names', () => {
            expect(isCollectionField(contentFieldRegistry, 'genre')).toBe(true);
            expect(isCollectionField(contentFieldRegistry, 'tag')).toBe(true);
            expect(isCollectionField(contentFieldRegistry, 'award')).toBe(true);
        });

        it('returns true for collection field aliases', () => {
            expect(isCollectionField(contentFieldRegistry, 'genres')).toBe(true);
            expect(isCollectionField(contentFieldRegistry, 'tags')).toBe(true);
            expect(isCollectionField(contentFieldRegistry, 'awards')).toBe(true);
        });

        it('returns false for scalar fields', () => {
            expect(isCollectionField(contentFieldRegistry, 'name')).toBe(false);
            expect(isCollectionField(contentFieldRegistry, 'title')).toBe(false);
            expect(isCollectionField(contentFieldRegistry, 'fileSize')).toBe(false);
        });

        it('returns false for unknown fields', () => {
            expect(isCollectionField(contentFieldRegistry, 'unknown')).toBe(false);
            expect(isCollectionField(contentFieldRegistry, 'notAField')).toBe(false);
        });

        it('recognizes relationship and its alias so they are no longer flagged', () => {
            expect(isCollectionField(contentFieldRegistry, 'relationship')).toBe(true);
            expect(isCollectionField(contentFieldRegistry, 'relationships')).toBe(true);
            expect(isCollectionField(contentFieldRegistry, 'Relationship')).toBe(true);

            const field = getCollectionField(contentFieldRegistry, 'relationship');
            const subFieldNames = field?.itemFields.map((f) => f.name) ?? [];
            expect(subFieldNames).toContain('kind');
            expect(subFieldNames).toContain('qualifier');
            expect(subFieldNames).toContain('subject');
        });
    });

    describe('getOperatorsForType', () => {
        it('returns string operators', () => {
            const ops = getOperatorsForType('string');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('contains');
            expect(ops).toContain('!contains');
            expect(ops).toContain('startswith');
            expect(ops).toContain('!startswith');
            expect(ops).toContain('endswith');
            expect(ops).toContain('!endswith');
            expect(ops).toContain('in');
            expect(ops).toContain('!in');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');
        });

        it('returns number operators', () => {
            const ops = getOperatorsForType('number');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('>');
            expect(ops).toContain('>=');
            expect(ops).toContain('<');
            expect(ops).toContain('<=');
            expect(ops).toContain('in');
            expect(ops).toContain('!in');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');
        });

        it('returns long operators (same as number)', () => {
            const ops = getOperatorsForType('long');
            expect(ops).toEqual(getOperatorsForType('number'));
        });

        it('returns date operators', () => {
            const ops = getOperatorsForType('date');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('>');
            expect(ops).toContain('>=');
            expect(ops).toContain('<');
            expect(ops).toContain('<=');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');
            expect(ops).not.toContain('in');
            expect(ops).not.toContain('!in');
        });

        it('returns timespan operators (same as date)', () => {
            const ops = getOperatorsForType('timespan');
            expect(ops).toEqual(getOperatorsForType('date'));
        });

        it('returns guid operators', () => {
            const ops = getOperatorsForType('guid');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');
            expect(ops).not.toContain('>');
            expect(ops).not.toContain('contains');
        });

        it('returns equality and has-no-value operators for boolean fields', () => {
            expect(getOperatorsForType('boolean')).toEqual(['=', '!=', 'null', '!null']);
        });

        it('returns enum operators', () => {
            const ops = getOperatorsForType('enum');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('in');
            expect(ops).toContain('!in');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');
            expect(ops).not.toContain('>');
            expect(ops).not.toContain('contains');
        });
    });

    describe('Type Mapping Validation', () => {
        it.each(['monitoring', 'fulfillment', 'activeAcquisitionStatus', 'qualityStatus'])(
            'enum field %s has a non-empty enumValues array',
            (fieldName) => {
                expect(getScalarField(contentFieldRegistry, fieldName)?.enumValues?.length).toBeGreaterThan(0);
            },
        );
    });
});
