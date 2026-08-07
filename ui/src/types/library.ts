export interface LibraryMetadataFieldDefinition {
    key: string;
    displayName?: string | null;
    dataType?: string | null;
    format?: string | null;
}

export type LibraryMetadataValueKind = 'text' | 'date' | 'duration' | 'number' | 'collection';

export interface LibraryStandardMetadataFieldDefinition extends LibraryMetadataFieldDefinition {
    field: string;
    valueKind: LibraryMetadataValueKind;
    editable: boolean;
}

export interface LibraryLevelDefinition {
    slug: string;
    displayName: string;
    parentSlug: string | null;
    iconName?: string | null;
    supportedMetadataFields?: number;
    customMetadataFields?: LibraryMetadataFieldDefinition[];
    standardMetadataFields?: LibraryStandardMetadataFieldDefinition[];
    standardFactFields: LibraryMetadataFieldDefinition[];
    acceptedFileTypes?: string[];
    effectiveAcceptedMimeTypes?: string[];
    acceptedSidecarTypes?: string[];
    subfolder?: string | null;
}

export interface LibraryTypeDefinition {
    id: string;
    name: string;
    pluralName: string;
    slug: string;
    iconName?: string | null;
    levels: LibraryLevelDefinition[];
    supportedMetadataFields?: number;
    customMetadataFields?: LibraryMetadataFieldDefinition[];
    standardMetadataFields?: LibraryStandardMetadataFieldDefinition[];
}
