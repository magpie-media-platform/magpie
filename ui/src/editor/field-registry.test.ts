import { describe, expect, it } from 'vitest';

import contentFieldCatalog from './content-field-catalog.txt?raw';
import {
    contentFieldRegistry,
    getAllFieldNames,
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
            overview: 'string',
            language: 'string',
            publishDate: 'date',
            duration: 'timespan',
            productionStatus: 'string',
        };

        it('has all expected content scalar fields', () => {
            for (const fieldName of Object.keys(expectedContentScalarFields)) {
                const field = getScalarField(contentFieldRegistry, fieldName);
                expect(field, `Missing scalar field: ${fieldName}`).toBeDefined();
            }
        });

        it('has correct types for content scalar fields', () => {
            for (const [fieldName, expectedType] of Object.entries(expectedContentScalarFields)) {
                const field = getScalarField(contentFieldRegistry, fieldName);
                expect(field?.type, `Field ${fieldName} has wrong type`).toBe(expectedType);
            }
        });

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
            expect(getAllFieldNames(contentFieldRegistry)).toContain('fulfillment');
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
        ];

        it('has all expected content collection fields', () => {
            for (const expected of expectedContentCollectionFields) {
                const field = getCollectionField(contentFieldRegistry, expected.name);
                expect(field, `Missing collection field: ${expected.name}`).toBeDefined();
            }
        });

        it('collection fields have correct aliases', () => {
            for (const expected of expectedContentCollectionFields) {
                const field = getCollectionField(contentFieldRegistry, expected.name);
                expect(field?.aliases, `Field ${expected.name} has wrong aliases`).toEqual(expected.aliases);
            }
        });

        it('collection fields have correct primary field', () => {
            for (const expected of expectedContentCollectionFields) {
                const field = getCollectionField(contentFieldRegistry, expected.name);
                expect(field?.primaryField, `Field ${expected.name} has wrong primaryField`).toBe(
                    expected.primaryField,
                );
            }
        });

        it('collection fields have correct item fields', () => {
            for (const expected of expectedContentCollectionFields) {
                const field = getCollectionField(contentFieldRegistry, expected.name);
                const actualNames = field?.itemFields.map((f) => f.name).sort();
                expect(actualNames, `Field ${expected.name} has wrong item fields`).toEqual(
                    expected.itemFieldNames.sort(),
                );
            }
        });

        it('collection item fields have correct types', () => {
            for (const expected of expectedContentCollectionFields) {
                const field = getCollectionField(contentFieldRegistry, expected.name);
                for (const itemField of field?.itemFields ?? []) {
                    const expectedType = expected.itemFieldTypes[itemField.name];
                    expect(itemField.type, `Field ${expected.name}.${itemField.name} has wrong type`).toBe(
                        expectedType,
                    );
                }
            }
        });

        it('aliases resolve to correct collection fields', () => {
            for (const expected of expectedContentCollectionFields) {
                for (const alias of expected.aliases) {
                    const field = getCollectionField(contentFieldRegistry, alias);
                    expect(field?.name, `Alias ${alias} resolves to wrong field`).toBe(expected.name);
                }
            }
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

        it('has all expected subject scalar fields', () => {
            for (const fieldName of Object.keys(expectedSubjectScalarFields)) {
                const field = getScalarField(subjectFieldRegistry, fieldName);
                expect(field, `Missing scalar field: ${fieldName}`).toBeDefined();
            }
        });

        it('has correct types for subject scalar fields', () => {
            for (const [fieldName, expectedType] of Object.entries(expectedSubjectScalarFields)) {
                const field = getScalarField(subjectFieldRegistry, fieldName);
                expect(field?.type, `Field ${fieldName} has wrong type`).toBe(expectedType);
            }
        });

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

        it('has all expected subject collection fields', () => {
            for (const expected of expectedSubjectCollectionFields) {
                const field = getCollectionField(subjectFieldRegistry, expected.name);
                expect(field, `Missing collection field: ${expected.name}`).toBeDefined();
            }
        });

        it('advertises tag but not genre or contentRating collections', () => {
            expect(getCollectionField(subjectFieldRegistry, 'tag')).toBeDefined();
            expect(getCollectionField(subjectFieldRegistry, 'tags')).toBeDefined();
            expect(getCollectionField(subjectFieldRegistry, 'genre')).toBeUndefined();
            expect(getCollectionField(subjectFieldRegistry, 'genres')).toBeUndefined();
            expect(getCollectionField(subjectFieldRegistry, 'contentRating')).toBeUndefined();
            expect(getCollectionField(subjectFieldRegistry, 'contentRatings')).toBeUndefined();
        });

        it('collection fields have correct aliases', () => {
            for (const expected of expectedSubjectCollectionFields) {
                const field = getCollectionField(subjectFieldRegistry, expected.name);
                expect(field?.aliases, `Field ${expected.name} has wrong aliases`).toEqual(expected.aliases);
            }
        });

        it('collection fields have correct primary field', () => {
            for (const expected of expectedSubjectCollectionFields) {
                const field = getCollectionField(subjectFieldRegistry, expected.name);
                expect(field?.primaryField, `Field ${expected.name} has wrong primaryField`).toBe(
                    expected.primaryField,
                );
            }
        });

        it('collection fields have correct item fields', () => {
            for (const expected of expectedSubjectCollectionFields) {
                const field = getCollectionField(subjectFieldRegistry, expected.name);
                const actualNames = field?.itemFields.map((f) => f.name).sort();
                expect(actualNames, `Field ${expected.name} has wrong item fields`).toEqual(
                    expected.itemFieldNames.sort(),
                );
            }
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

    describe('getAllFieldNames', () => {
        it('returns all scalar and collection field names for content', () => {
            const names = getAllFieldNames(contentFieldRegistry);
            expect(names).toContain('name');
            expect(names).not.toContain('fileSize');
            expect(names).toContain('monitoring');
            expect(names).toContain('genre');
            expect(names).toContain('tag');
            expect(names).toContain('award');
            expect(names).toContain('genres');
            expect(names).toContain('tags');
            expect(names).toContain('awards');
            expect(names).toContain('relationship');
            expect(names).toContain('relationships');
        });

        it('returns correct count for content registry', () => {
            const names = getAllFieldNames(contentFieldRegistry);
            expect(names.length).toBe(39);
        });

        it('returns all scalar and collection field names for subject', () => {
            const names = getAllFieldNames(subjectFieldRegistry);
            expect(names).toContain('kind');
            expect(names).toContain('title');
            expect(names).toContain('tag');
            expect(names).toContain('tags');
        });

        it('returns correct count for subject registry', () => {
            const names = getAllFieldNames(subjectFieldRegistry);
            expect(names.length).toBe(4);
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

    describe('Registry Completeness Cross-Check', () => {
        it('content registry supports the advertised query fields', () => {
            const queriedContentFields = [
                'name',
                'monitoring',
                'contentType',
                'title',
                'publishDate',
                'duration',
                'overview',
                'parentId',
                'genre',
                'genres',
                'tag',
                'tags',
                'award',
                'awards',
                'externalId',
                'externalIds',
                'image',
                'images',
                'contentRating',
                'contentRatings',
            ];

            for (const fieldName of queriedContentFields) {
                const scalar = getScalarField(contentFieldRegistry, fieldName);
                const collection = getCollectionField(contentFieldRegistry, fieldName);
                expect(
                    scalar !== undefined || collection !== undefined,
                    `Field ${fieldName} used in grammar tests but missing from content registry`,
                ).toBe(true);
            }
        });

        it('subject registry supports all curated fields advertised on the people page', () => {
            const queriedSubjectFields = ['kind', 'title', 'tag', 'tags'];

            for (const fieldName of queriedSubjectFields) {
                const scalar = getScalarField(subjectFieldRegistry, fieldName);
                const collection = getCollectionField(subjectFieldRegistry, fieldName);
                expect(
                    scalar !== undefined || collection !== undefined,
                    `Field ${fieldName} used in grammar tests but missing from subject registry`,
                ).toBe(true);
            }
        });
    });

    describe('Type Mapping Validation', () => {
        it('all scalar fields have valid types', () => {
            const validTypes: FieldType[] = ['string', 'number', 'long', 'date', 'timespan', 'guid', 'boolean', 'enum'];

            for (const field of contentFieldRegistry.scalarFields) {
                expect(validTypes, `Field ${field.name} has invalid type ${field.type}`).toContain(field.type);
            }

            for (const field of subjectFieldRegistry.scalarFields) {
                expect(validTypes, `Field ${field.name} has invalid type ${field.type}`).toContain(field.type);
            }
        });

        it('all collection item fields have valid types', () => {
            const validTypes: FieldType[] = ['string', 'number', 'long', 'date', 'timespan', 'guid', 'boolean', 'enum'];

            for (const collection of contentFieldRegistry.collectionFields) {
                for (const field of collection.itemFields) {
                    expect(
                        validTypes,
                        `Collection ${collection.name}.${field.name} has invalid type ${field.type}`,
                    ).toContain(field.type);
                }
            }

            for (const collection of subjectFieldRegistry.collectionFields) {
                for (const field of collection.itemFields) {
                    expect(
                        validTypes,
                        `Collection ${collection.name}.${field.name} has invalid type ${field.type}`,
                    ).toContain(field.type);
                }
            }
        });

        it('enum fields have enumValues array', () => {
            for (const field of contentFieldRegistry.scalarFields) {
                if (field.type === 'enum') {
                    expect(field.enumValues, `Enum field ${field.name} missing enumValues`).toBeDefined();
                    expect(field.enumValues?.length, `Enum field ${field.name} has empty enumValues`).toBeGreaterThan(
                        0,
                    );
                }
            }
        });
    });
});
