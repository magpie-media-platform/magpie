export type FieldType = 'string' | 'number' | 'long' | 'date' | 'timespan' | 'guid' | 'boolean' | 'enum';

interface ScalarFieldDef {
    name: string;
    type: FieldType;
    enumValues?: string[];
    description?: string;
}

export interface CollectionFieldDef {
    name: string;
    aliases: string[];
    primaryField: string;
    itemFields: { name: string; type: FieldType }[];
}

export interface FieldRegistry {
    scalarFields: ScalarFieldDef[];
    collectionFields: CollectionFieldDef[];
}

export const contentFieldRegistry: FieldRegistry = {
    scalarFields: [
        { name: 'name', type: 'string', description: 'Content name' },
        { name: 'slug', type: 'string', description: 'URL slug' },
        { name: 'path', type: 'string', description: 'File path' },
        { name: 'contentType', type: 'string', description: 'Registered content type' },
        { name: 'level', type: 'string', description: 'Registered hierarchy level' },
        {
            name: 'monitoring',
            type: 'boolean',
            description: 'Whether content is effectively monitored',
        },
        { name: 'rootFolderId', type: 'guid', description: 'Root folder ID' },
        { name: 'parentId', type: 'guid', description: 'Parent content ID' },
        { name: 'title', type: 'string', description: 'Metadata title' },
        { name: 'originalTitle', type: 'string', description: 'Original language title' },
        { name: 'overview', type: 'string', description: 'Description' },
        { name: 'language', type: 'string', description: 'Language code' },
        { name: 'publishDate', type: 'date', description: 'Publish/release date' },
        { name: 'duration', type: 'timespan', description: 'Duration: "1h30m" or "02:00:00"' },
        { name: 'productionStatus', type: 'string', description: 'Production status' },
        { name: 'fileKind', type: 'string', description: 'Media file kind' },
        { name: 'mimeType', type: 'string', description: 'File MIME type' },
        { name: 'filePath', type: 'string', description: 'Media file path' },
        { name: 'fileSize', type: 'long', description: 'File size in bytes' },
    ],
    collectionFields: [
        {
            name: 'genre',
            aliases: ['genres'],
            primaryField: 'name',
            itemFields: [{ name: 'name', type: 'string' }],
        },
        {
            name: 'tag',
            aliases: ['tags'],
            primaryField: 'name',
            itemFields: [{ name: 'name', type: 'string' }],
        },
        {
            name: 'award',
            aliases: ['awards'],
            primaryField: 'name',
            itemFields: [
                { name: 'body', type: 'string' },
                { name: 'name', type: 'string' },
                { name: 'category', type: 'string' },
                { name: 'status', type: 'string' },
                { name: 'year', type: 'number' },
                { name: 'language', type: 'string' },
                { name: 'country', type: 'string' },
            ],
        },
        {
            name: 'externalId',
            aliases: ['externalIds'],
            primaryField: 'value',
            itemFields: [
                { name: 'source', type: 'string' },
                { name: 'value', type: 'string' },
            ],
        },
        {
            name: 'contentRating',
            aliases: ['contentRatings'],
            primaryField: 'rating',
            itemFields: [
                { name: 'body', type: 'string' },
                { name: 'rating', type: 'string' },
            ],
        },
        {
            name: 'image',
            aliases: ['images'],
            primaryField: 'type',
            itemFields: [
                { name: 'type', type: 'string' },
                { name: 'mimeType', type: 'string' },
                { name: 'url', type: 'string' },
                { name: 'source', type: 'string' },
                { name: 'language', type: 'string' },
                { name: 'width', type: 'number' },
                { name: 'height', type: 'number' },
            ],
        },
        {
            name: 'relationship',
            aliases: ['relationships'],
            primaryField: 'kind',
            itemFields: [
                { name: 'kind', type: 'string' },
                { name: 'qualifier', type: 'string' },
                { name: 'subject', type: 'string' },
                { name: 'name', type: 'string' },
            ],
        },
    ],
};

export const subjectFieldRegistry: FieldRegistry = {
    scalarFields: [
        { name: 'kind', type: 'string', description: 'Subject kind' },
        { name: 'title', type: 'string', description: 'Name / title' },
    ],
    collectionFields: [
        {
            name: 'tag',
            aliases: ['tags'],
            primaryField: 'name',
            itemFields: [{ name: 'name', type: 'string' }],
        },
    ],
};

export function getAllFieldNames(registry: FieldRegistry): string[] {
    const names: string[] = [];
    for (const f of registry.scalarFields) {
        names.push(f.name);
    }
    for (const f of registry.collectionFields) {
        names.push(f.name);
        for (const alias of f.aliases) {
            names.push(alias);
        }
    }
    return names;
}

export function getScalarField(registry: FieldRegistry, name: string): ScalarFieldDef | undefined {
    return registry.scalarFields.find((f) => f.name.toLowerCase() === name.toLowerCase());
}

export function getCollectionField(registry: FieldRegistry, name: string): CollectionFieldDef | undefined {
    const lower = name.toLowerCase();
    return registry.collectionFields.find(
        (f) => f.name.toLowerCase() === lower || f.aliases.some((a) => a.toLowerCase() === lower),
    );
}

export function isCollectionField(registry: FieldRegistry, name: string): boolean {
    return getCollectionField(registry, name) !== undefined;
}

export function getOperatorsForType(type: FieldType): string[] {
    switch (type) {
        case 'string':
            return [
                '=',
                '!=',
                'contains',
                '!contains',
                'startswith',
                '!startswith',
                'endswith',
                '!endswith',
                'in',
                '!in',
                'null',
                '!null',
            ];
        case 'number':
        case 'long':
            return ['=', '!=', '>', '>=', '<', '<=', 'in', '!in', 'null', '!null'];
        case 'date':
        case 'timespan':
            return ['=', '!=', '>', '>=', '<', '<=', 'null', '!null'];
        case 'guid':
            return ['=', '!=', 'null', '!null'];
        case 'boolean':
            return ['=', '!=', 'null', '!null'];
        case 'enum':
            return ['=', '!=', 'in', '!in', 'null', '!null'];
        default:
            return ['=', '!='];
    }
}
