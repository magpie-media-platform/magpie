import { describe, expect, it } from 'vitest';
import { EditorState } from '@codemirror/state';
import { CompletionContext } from '@codemirror/autocomplete';
import { LRLanguage, LanguageSupport, ensureSyntaxTree } from '@codemirror/language';

import { parser } from './filter-query-parser.gen';
import { filterQueryHighlight } from './highlight';
import { filterQueryAutocomplete } from './autocomplete';
import {
    type FieldRegistry,
    contentFieldRegistry,
    subjectFieldRegistry,
    getOperatorsForType,
    getScalarField,
    getCollectionField,
    isCollectionField,
    getAllFieldNames,
} from './field-registry';

const parserWithHighlight = parser.configure({
    props: [filterQueryHighlight],
});

const filterQueryLanguage = LRLanguage.define({
    name: 'filter-query',
    parser: parserWithHighlight,
    languageData: {},
});

function createEditorState(query: string, registry: FieldRegistry): EditorState {
    const languageSupport = new LanguageSupport(filterQueryLanguage, [
        filterQueryLanguage.data.of({
            autocomplete: filterQueryAutocomplete(registry),
        }),
    ]);

    return EditorState.create({
        doc: query,
        extensions: [languageSupport],
    });
}

function createContext(
    queryWithCursor: string,
    registry: FieldRegistry = contentFieldRegistry,
    explicit = true,
): CompletionContext {
    const cursorPos = queryWithCursor.indexOf('|');
    if (cursorPos === -1) {
        throw new Error('Query must contain | to indicate cursor position');
    }

    const actualQuery = queryWithCursor.replace('|', '');
    const state = createEditorState(actualQuery, registry);

    ensureSyntaxTree(state, actualQuery.length, 1000);

    return new CompletionContext(state, cursorPos, explicit);
}

function getCompletions(queryWithCursor: string, registry: FieldRegistry = contentFieldRegistry) {
    const context = createContext(queryWithCursor, registry);
    const autocomplete = filterQueryAutocomplete(registry);
    return autocomplete(context);
}

function getLabels(result: ReturnType<typeof getCompletions>): string[] {
    return result?.options.map((o) => o.label) ?? [];
}

describe('Filter Query Autocomplete', () => {
    it.each([
        'genre has "Ac|tion"',
        'genres has (name = "Ac|tion")',
        'title = "Ac|tion"',
        'genre has "Ac|',
        'genre has Ac|',
        'genres has (name = Ac|)',
    ])('does not offer completions inside a quoted value: %s', (query) => {
        expect(getCompletions(query)).toBeNull();
        const context = createContext(query, contentFieldRegistry, false);
        expect(filterQueryAutocomplete(contentFieldRegistry)(context)).toBeNull();
    });
    it('still offers collection fields inside a predicate', () => {
        expect(getLabels(getCompletions('awards has (|)'))).toEqual([
            'body',
            'name',
            'category',
            'status',
            'year',
            'language',
            'country',
            'and',
            'or',
            'not',
        ]);
    });
    it('offers media file fields inside a media file predicate', () => {
        const result = getCompletions('mediaFiles has (|)');

        expect(getLabels(result)).toEqual(['fileKind', 'mimeType', 'filePath', 'fileSize', 'and', 'or', 'not']);
        expect(result?.options.find((option) => option.label === 'fileSize')?.detail).toBe('long');
    });

    describe('Field Registry Functions', () => {
        describe('getScalarField', () => {
            it('finds scalar field by name (case-insensitive)', () => {
                expect(getScalarField(contentFieldRegistry, 'name')).toBeDefined();
                expect(getScalarField(contentFieldRegistry, 'Name')).toBeDefined();
                expect(getScalarField(contentFieldRegistry, 'NAME')).toBeDefined();
            });

            it('returns undefined for non-existent field', () => {
                expect(getScalarField(contentFieldRegistry, 'nonexistent')).toBeUndefined();
            });

            it('returns undefined for collection field names', () => {
                expect(getScalarField(contentFieldRegistry, 'genre')).toBeUndefined();
                expect(getScalarField(contentFieldRegistry, 'genres')).toBeUndefined();
            });
        });

        describe('getCollectionField', () => {
            it('finds collection field by name', () => {
                expect(getCollectionField(contentFieldRegistry, 'genre')).toBeDefined();
                expect(getCollectionField(contentFieldRegistry, 'Genre')).toBeDefined();
            });

            it('finds collection field by alias', () => {
                expect(getCollectionField(contentFieldRegistry, 'genres')).toBeDefined();
                expect(getCollectionField(contentFieldRegistry, 'Genres')).toBeDefined();
            });

            it('returns undefined for non-existent field', () => {
                expect(getCollectionField(contentFieldRegistry, 'nonexistent')).toBeUndefined();
            });
        });

        describe('isCollectionField', () => {
            it('returns true for collection field names', () => {
                expect(isCollectionField(contentFieldRegistry, 'genre')).toBe(true);
                expect(isCollectionField(contentFieldRegistry, 'tag')).toBe(true);
            });

            it('returns true for collection field aliases', () => {
                expect(isCollectionField(contentFieldRegistry, 'genres')).toBe(true);
                expect(isCollectionField(contentFieldRegistry, 'tags')).toBe(true);
            });

            it('returns false for scalar fields', () => {
                expect(isCollectionField(contentFieldRegistry, 'name')).toBe(false);
                expect(isCollectionField(contentFieldRegistry, 'monitoring')).toBe(false);
            });
        });

        describe('getAllFieldNames', () => {
            it('returns all scalar field names', () => {
                const names = getAllFieldNames(contentFieldRegistry);
                expect(names).toContain('name');
                expect(names).toContain('monitoring');
                expect(names).not.toContain('fileSize');
            });

            it('returns all collection field names and aliases', () => {
                const names = getAllFieldNames(contentFieldRegistry);
                expect(names).toContain('genre');
                expect(names).toContain('genres');
                expect(names).toContain('tag');
                expect(names).toContain('tags');
            });
        });
    });

    describe('getOperatorsForType', () => {
        it('returns string operators for string type', () => {
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

        it('returns numeric operators for number type', () => {
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

            expect(ops).not.toContain('contains');
            expect(ops).not.toContain('startswith');
        });

        it('returns numeric operators for long type', () => {
            const ops = getOperatorsForType('long');
            expect(ops).toContain('>');
            expect(ops).toContain('>=');
            expect(ops).toContain('<');
            expect(ops).toContain('<=');
        });

        it('returns date operators for date type', () => {
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
        });

        it('returns timespan operators for timespan type', () => {
            const ops = getOperatorsForType('timespan');
            expect(ops).toContain('=');
            expect(ops).toContain('>');
            expect(ops).toContain('>=');
            expect(ops).toContain('<');
            expect(ops).toContain('<=');
        });

        it('returns guid operators for guid type', () => {
            const ops = getOperatorsForType('guid');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');

            expect(ops).not.toContain('>');
            expect(ops).not.toContain('<');
        });

        it('returns enum operators for enum type', () => {
            const ops = getOperatorsForType('enum');
            expect(ops).toContain('=');
            expect(ops).toContain('!=');
            expect(ops).toContain('in');
            expect(ops).toContain('!in');
            expect(ops).toContain('null');
            expect(ops).toContain('!null');
        });
    });

    describe('Field Suggestions at Empty Position', () => {
        it('suggests fields at start of empty query', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('name');
            expect(labels).toContain('monitoring');
            expect(labels).not.toContain('fileSize');

            expect(labels).toContain('genre');
            expect(labels).toContain('genres');
            expect(labels).toContain('tag');
            expect(labels).toContain('tags');
            expect(labels).toContain('relationship');
            expect(labels).toContain('relationships');

            expect(labels).toContain('and');
            expect(labels).toContain('or');
            expect(labels).toContain('not');
        });

        it('includes all content fields from registry', () => {
            const result = getCompletions('|', contentFieldRegistry);

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toEqual([
                'name',
                'slug',
                'path',
                'contentType',
                'level',
                'fulfillment',
                'monitoring',
                'activeAcquisitionStatus',
                'qualityStatus',
                'pendingMetadataRefresh',
                'hasSearchableTitle',
                'importPending',
                'primaryMediaSize',
                'primaryMediaFileCount',
                'rootFolderId',
                'parentId',
                'title',
                'originalTitle',
                'overview',
                'language',
                'publishDate',
                'duration',
                'productionStatus',
                'genre',
                'genres',
                'tag',
                'tags',
                'award',
                'awards',
                'externalId',
                'externalIds',
                'contentRating',
                'contentRatings',
                'image',
                'images',
                'relationship',
                'relationships',
                'mediaFile',
                'mediaFiles',
                'and',
                'or',
                'not',
            ]);
        });

        it('includes the curated subject fields from registry', () => {
            const result = getCompletions('|', subjectFieldRegistry);

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('kind');
            expect(labels).toContain('title');
            expect(labels).toContain('tag');
            expect(labels).toContain('tags');
        });

        it('provides field type in completion detail', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();

            const monitoringCompletion = result!.options.find((o) => o.label === 'monitoring');
            expect(monitoringCompletion).toBeDefined();
            expect(monitoringCompletion!.detail).toBe('enum');

            const fileSizeCompletion = result!.options.find((o) => o.label === 'fileSize');
            expect(fileSizeCompletion).toBeUndefined();
        });

        it('suggests true and false for a boolean field value', () => {
            const result = getCompletions('importPending = |');

            expect(result).not.toBeNull();
            expect(getLabels(result)).toEqual(expect.arrayContaining(['true', 'false']));
        });

        it('provides field description in completion info', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();

            const nameCompletion = result!.options.find((o) => o.label === 'name');
            expect(nameCompletion).toBeDefined();
            expect(nameCompletion!.info).toBe('Content name');
        });

        it('provides collection field detail', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();

            const genreCompletion = result!.options.find((o) => o.label === 'genre');
            expect(genreCompletion).toBeDefined();
            expect(genreCompletion!.detail).toBe('collection');
            expect(genreCompletion!.info).toContain('Singular form');

            const genresCompletion = result!.options.find((o) => o.label === 'genres');
            expect(genresCompletion).toBeDefined();
            expect(genresCompletion!.detail).toBe('collection');
            expect(genresCompletion!.info).toContain('Plural form');
        });
    });

    describe('Keyword Suggestions', () => {
        it('includes keywords in suggestions', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('and');
            expect(labels).toContain('or');
            expect(labels).toContain('not');
        });

        it('marks keywords with correct completion type', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();

            const andCompletion = result!.options.find((o) => o.label === 'and');
            expect(andCompletion).toBeDefined();
            expect(andCompletion!.type).toBe('keyword');

            const orCompletion = result!.options.find((o) => o.label === 'or');
            expect(orCompletion).toBeDefined();
            expect(orCompletion!.type).toBe('keyword');

            const notCompletion = result!.options.find((o) => o.label === 'not');
            expect(notCompletion).toBeDefined();
            expect(notCompletion!.type).toBe('keyword');
        });
    });

    describe('Field Registry Integration', () => {
        it('uses content registry for content context', () => {
            const result = getCompletions('|', contentFieldRegistry);

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('contentType');
            expect(labels).toContain('level');
            expect(labels).toContain('contentRating');
            expect(labels).toContain('contentRatings');
        });

        it('uses subject registry for subject context', () => {
            const result = getCompletions('|', subjectFieldRegistry);

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('kind');
            expect(labels).toContain('title');
            expect(labels).toContain('tag');

            expect(labels).not.toContain('contentType');
            expect(labels).not.toContain('level');
        });
    });

    describe('Partial Input Filtering', () => {
        it('suggests fields matching partial input', () => {
            const result = getCompletions('mon|');

            expect(result).not.toBeNull();

            const labels = getLabels(result);
            expect(labels).toContain('monitoring');
        });

        it('provides validFor pattern for filtering', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();
            expect(result!.validFor).toBeInstanceOf(RegExp);

            const validFor = result!.validFor as RegExp;
            expect(validFor.test('abc')).toBe(true);
            expect(validFor.test('!')).toBe(true);
            expect(validFor.test('!has')).toBe(true);
            expect(validFor.test(' ')).toBe(false);
        });
    });

    describe('Completion Position (from)', () => {
        it('starts completion from beginning of word', () => {
            const result = getCompletions('mon|');

            expect(result).not.toBeNull();
            expect(result!.from).toBe(0);
        });

        it('starts completion from cursor when no word', () => {
            const result = getCompletions('|');

            expect(result).not.toBeNull();
            expect(result!.from).toBe(0);
        });

        it('handles negation prefix in input', () => {
            const result = getCompletions('!|');

            expect(result).not.toBeNull();

            expect(result!.from).toBe(0);
        });
    });

    describe('Non-Explicit Trigger', () => {
        it('returns null when no word and not explicit', () => {
            const query = '|';
            const cursorPos = query.indexOf('|');
            const actualQuery = query.replace('|', '');

            const state = createEditorState(actualQuery, contentFieldRegistry);

            const context = new CompletionContext(state, cursorPos, false);
            const autocomplete = filterQueryAutocomplete(contentFieldRegistry);
            const result = autocomplete(context);

            expect(result).toBeNull();
        });

        it('returns completions when typing a word (non-explicit)', () => {
            const query = 'mon|';
            const cursorPos = query.indexOf('|');
            const actualQuery = query.replace('|', '');

            const state = createEditorState(actualQuery, contentFieldRegistry);

            const context = new CompletionContext(state, cursorPos, false);
            const autocomplete = filterQueryAutocomplete(contentFieldRegistry);
            const result = autocomplete(context);

            expect(result).not.toBeNull();
            expect(getLabels(result)).toContain('monitoring');
        });
    });

    describe('Context After Keywords', () => {
        it('suggests fields after and keyword', () => {
            const result = getCompletions('name = "test" and |');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('name');
            expect(labels).toContain('monitoring');
            expect(labels).toContain('genre');
        });

        it('suggests fields after or keyword', () => {
            const result = getCompletions('name = "test" or |');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('name');
            expect(labels).toContain('monitoring');
        });

        it('suggests fields after not keyword', () => {
            const result = getCompletions('not |');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('name');
            expect(labels).toContain('genre');
        });
    });

    describe('Parentheses Handling', () => {
        it('suggests fields inside parentheses', () => {
            const result = getCompletions('(|');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('name');
            expect(labels).toContain('monitoring');
        });

        it('suggests fields after opening paren in grouped expression', () => {
            const result = getCompletions('name = "test" and (|');

            expect(result).not.toBeNull();
            const labels = getLabels(result);

            expect(labels).toContain('name');
            expect(labels).toContain('genre');
        });
    });

    describe('Edge Cases', () => {
        it('handles empty string gracefully', () => {
            const result = getCompletions('|');
            expect(result).not.toBeNull();
        });

        it('handles whitespace-only prefix', () => {
            const result = getCompletions('   |');
            expect(result).not.toBeNull();
        });

        it('handles case-insensitive field lookup in registry', () => {
            expect(getScalarField(contentFieldRegistry, 'Monitoring')).toBeDefined();
            expect(getCollectionField(contentFieldRegistry, 'Genre')).toBeDefined();
            expect(isCollectionField(contentFieldRegistry, 'TAGS')).toBe(true);
        });
    });

    describe('Content Field Registry Structure', () => {
        it('has expected scalar fields', () => {
            const fieldNames = contentFieldRegistry.scalarFields.map((f) => f.name);

            expect(fieldNames).toContain('name');
            expect(fieldNames).toContain('slug');
            expect(fieldNames).toContain('path');
            expect(fieldNames).toContain('contentType');
            expect(fieldNames).toContain('level');
            expect(fieldNames).toContain('monitoring');
            expect(fieldNames).toContain('title');
            expect(fieldNames).toContain('overview');
            expect(fieldNames).toContain('publishDate');
            expect(fieldNames).toContain('duration');
            expect(fieldNames).not.toContain('fileSize');
        });

        it('has expected collection fields', () => {
            const fieldNames = contentFieldRegistry.collectionFields.map((f) => f.name);

            expect(fieldNames).toContain('genre');
            expect(fieldNames).toContain('tag');
            expect(fieldNames).toContain('award');
            expect(fieldNames).toContain('externalId');
            expect(fieldNames).toContain('contentRating');
            expect(fieldNames).toContain('image');
        });

        it('monitoring field is an enum of monitoring modes', () => {
            const monitoring = getScalarField(contentFieldRegistry, 'monitoring');

            expect(monitoring).toBeDefined();
            expect(monitoring!.type).toBe('enum');
            expect(monitoring!.enumValues).toEqual(['Unmonitored', 'Monitored', 'Shotgun']);
        });

        it('awards collection has expected item fields', () => {
            const awards = getCollectionField(contentFieldRegistry, 'awards');

            expect(awards).toBeDefined();
            const itemFieldNames = awards!.itemFields.map((f) => f.name);

            expect(itemFieldNames).toContain('body');
            expect(itemFieldNames).toContain('name');
            expect(itemFieldNames).toContain('category');
            expect(itemFieldNames).toContain('status');
            expect(itemFieldNames).toContain('year');
            expect(itemFieldNames).toContain('language');
            expect(itemFieldNames).toContain('country');
        });
    });

    describe('Subject Field Registry Structure', () => {
        it('has expected scalar fields', () => {
            const fieldNames = subjectFieldRegistry.scalarFields.map((f) => f.name);

            expect(fieldNames).toContain('kind');
            expect(fieldNames).toContain('title');
        });

        it('has expected collection fields', () => {
            const fieldNames = subjectFieldRegistry.collectionFields.map((f) => f.name);

            expect(fieldNames).toContain('tag');
        });

        it('subject does not have content-specific fields', () => {
            const fieldNames = subjectFieldRegistry.scalarFields.map((f) => f.name);

            expect(fieldNames).not.toContain('contentType');
            expect(fieldNames).not.toContain('level');
            expect(fieldNames).not.toContain('monitoring');
            expect(fieldNames).not.toContain('fileSize');
        });
    });
});
