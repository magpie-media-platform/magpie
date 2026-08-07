import type { JobExecutionModeValue } from '@/lib/jobActions';

import type { ContentListItem } from './content';

export type AcquisitionBlockReason = 'None' | 'NoRootFolder';

export interface AcquisitionEligibility {
    canAutoGrab: boolean;
    blockReason: AcquisitionBlockReason;
    blockReasonDetail?: string | null;
}

export type RootFolderResolutionSource = 'Override' | 'Inherited' | 'ContentTypeDefault' | 'None';

export interface EffectiveRootFolder {
    rootFolderId?: string | null;
    rootFolderName?: string | null;
    rootFolderPath?: string | null;
    source: RootFolderResolutionSource;
    resolvingHierarchyItemId?: string | null;
    isOverriddenOnItem: boolean;
}

export interface SetContentRootFolderVariables {
    contentHierarchyItemId: string;
    rootFolderId: string | null;
}

export type QualityTierListResolutionSource = 'ExplicitOnItem' | 'Inherited' | 'ContentTypeDefaultRootFolder' | 'None';

export interface EffectiveQualityTierList {
    qualityTierListId?: string | null;
    qualityTierListName?: string | null;
    source: QualityTierListResolutionSource;
    resolvingHierarchyItemId?: string | null;
    resolvingHierarchyItemName?: string | null;
    ownQualityTierListId?: string | null;
}

export interface SetQualityTierListVariables {
    contentHierarchyItemId: string;
    qualityTierListId: string | null;
}

export interface MediaServerDeepLink {
    mediaServerId: string;
    serverName: string;
    providerType: string;
    url: string;
}

export interface SearchContentReleasesParams {
    id: string;

    mode?: JobExecutionModeValue;
}

export interface ContentListResult {
    items: ContentListItem[];
    pageNumber: number;
    pageSize: number;
    totalItems: number;
}

export interface ContentRelationship {
    id: string;
    kind: string;
    qualifier?: string | null;
    ordinal: number;
    subjectId?: string | null;
    name: string;
    imageUrl?: string | null;
}

export type PendingRefreshState = 'None' | 'Queued' | 'Running' | 'RecentlyCompleted';

export interface PendingRefreshResponse {
    state: PendingRefreshState;
    lastCompletedAt?: string | null;
}

export interface UpdateContentRequest {
    id: string;
    monitoring?: boolean | null;
    qualityTierListId?: string | null;
    rootFolderId?: string | null;
    tagsToAdd?: string[];
    tagsToRemove?: string[];
}

export interface MetadataInspectorField {
    value: string | null;
    contributionId: string;
    providerType: string;
    ordinal: number;
}

export interface MetadataGenre {
    id: string;
    metadataContributionId?: string;
    name: string;
    ordinal?: number;
}

export interface MetadataImage {
    id: string;
    metadataContributionId?: string;
    ordinal?: number;
    type: string;
    url: string;
    source?: string | null;
    language?: string | null;
    mimeType?: string | null;
    width?: number | null;
    height?: number | null;
    isPrimary?: boolean;
}

export interface MetadataRelationship {
    id: string;
    metadataContributionId?: string;
    relatedMetadataContainerId?: string | null;
    ordinal?: number;
    kind: string;
    qualifier?: string | null;
}

export interface MetadataExternalId {
    id: string;
    metadataContributionId?: string | null;
    releaseId?: string | null;
    ordinal?: number;
    value: string;
    source?: string | null;
}

export interface MetadataPreview {
    id: string;
    metadataContributionId?: string;
    ordinal?: number;
    kind: string;
    url: string;
    source?: string | null;
    embedKind?: string | null;
    title?: string | null;
    mimeType?: string | null;
    releaseDate?: string | null;
    language?: string | null;
}

export interface MetadataAlternateText {
    id: string;
    metadataContributionId?: string;
    ordinal?: number;
    kind: string;
    text: string;
    language?: string | null;
}

export interface MetadataAward {
    id: string;
    metadataContributionId?: string;
    ordinal?: number;
    body: string;
    name: string;
    category?: string | null;
    status?: string | null;
    year?: number | null;
    language?: string | null;
    country?: string | null;
}

export interface MetadataContentRating {
    id: string;
    metadataContributionId?: string;
    ordinal?: number;
    body: string;
    rating: string;
}

export interface MetadataCustomValue {
    id: string;
    metadataContributionId?: string;
    key: string;
    value?: string | null;
}

export interface MetadataReference {
    id: string;
    metadataContributionId?: string;
    ordinal?: number;
    kind: string;
    source: string;
    url: string;
    displayName?: string | null;
    language?: string | null;
}

export interface MetadataContribution {
    id: string;
    metadataContainerId?: string;
    ordinal: number;
    extensionId?: string | null;
    providerType: string;
    source?: string | null;
    title?: string | null;
    originalTitle?: string | null;
    sortTitle?: string | null;
    language?: string | null;
    overview?: string | null;
    publishDate?: string | null;
    duration?: string | null;
    sequenceNumber?: number | null;
    productionStatus?: string | null;
    established?: string | null;
    dissolved?: string | null;
    birthDate?: string | null;
    deathDate?: string | null;
    createdAt?: string;
    createdByUserId?: string | null;
    modifiedAt?: string | null;
    modifiedByUserId?: string | null;
    dateLastRefreshed?: string | null;
    alternateTexts?: MetadataAlternateText[];
    awards?: MetadataAward[];
    contentRatings?: MetadataContentRating[];
    customMetadata?: MetadataCustomValue[];
    externalIds?: MetadataExternalId[];
    genres?: MetadataGenre[];
    images?: MetadataImage[];
    previews?: MetadataPreview[];
    references?: MetadataReference[];
    relationships?: MetadataRelationship[];
}

export interface MaterializedMetadata {
    containerId: string;
    contentHierarchyItemId?: string | null;
    subjectId?: string | null;
    title?: string | null;
    originalTitle?: string | null;
    sortTitle?: string | null;
    language?: string | null;
    overview?: string | null;
    publishDate?: string | null;
    duration?: string | null;
    sequenceNumber?: number | null;
    productionStatus?: string | null;
    established?: string | null;
    dissolved?: string | null;
    birthDate?: string | null;
    deathDate?: string | null;
    customMetadata?: MetadataCustomValue[] | null;
    externalIds?: MetadataExternalId[] | null;
    references?: MetadataReference[] | null;
    relationships?: MetadataRelationship[] | null;
    images?: MetadataImage[] | null;
    previews?: MetadataPreview[] | null;
    alternateTexts?: MetadataAlternateText[] | null;
    contentRatings?: MetadataContentRating[] | null;
    awards?: MetadataAward[] | null;
    genres?: MetadataGenre[] | null;
}

export interface SubjectRef {
    subjectId: string;
    name: string;
    imageUrl?: string | null;
}

export interface MetadataInspectorResponse {
    materialized: MaterializedMetadata;
    contributions: MetadataContribution[];
    subjects: Record<string, SubjectRef>;
}

export type MetadataSubItemKey =
    | 'genres'
    | 'images'
    | 'relationships'
    | 'externalIds'
    | 'previews'
    | 'alternateTexts'
    | 'awards'
    | 'contentRatings'
    | 'customValues'
    | 'references';

export interface RelationshipSubItemPayload {
    kind: string;
    qualifier?: string;
    ordinal?: number;
    subjectId?: string;
    subjectName?: string;
    subjectKind?: string;
}

export interface GenreSubItemPayload {
    name: string;
}

export interface ImageSubItemPayload {
    type: string;
    url: string;
    mimeType?: string;
    language?: string;
    width?: number;
    height?: number;
}

export interface PreviewSubItemPayload {
    kind: string;
    url: string;
    mimeType?: string;
    title?: string;
    releaseDate?: string;
    language?: string;
}

export interface ExternalIdSubItemPayload {
    value: string;
    source?: string;
}

export interface AlternateTextSubItemPayload {
    kind: string;
    text: string;
    language?: string;
}

export interface AwardSubItemPayload {
    body: string;
    name: string;
    category?: string;
    status?: string;
    year?: number;
    language?: string;
    country?: string;
}

export interface ContentRatingSubItemPayload {
    body: string;
    rating: string;
}

export interface CustomMetadataValueSubItemPayload {
    key: string;
    value?: string;
}

export interface ReferenceSubItemPayload {
    kind: string;
    source: string;
    url: string;
    displayName?: string;
    language?: string;
}

export interface SubItemPayloadByKey {
    genres: GenreSubItemPayload;
    images: ImageSubItemPayload;
    relationships: RelationshipSubItemPayload;
    externalIds: ExternalIdSubItemPayload;
    previews: PreviewSubItemPayload;
    alternateTexts: AlternateTextSubItemPayload;
    awards: AwardSubItemPayload;
    contentRatings: ContentRatingSubItemPayload;
    customValues: CustomMetadataValueSubItemPayload;
    references: ReferenceSubItemPayload;
}

export type SubItemPayload = SubItemPayloadByKey[MetadataSubItemKey];

export interface ScalarPatch<T> {
    value: T;
}

export interface MetadataCollectionPatch<TAdd, TUpdate> {
    add?: TAdd[];
    remove?: string[];
    update?: TUpdate[];
}

export interface MetadataSetPatch {
    add?: string[];
    remove?: string[];
}

export interface MetadataAlternateTextUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    kind?: ScalarPatch<string>;
    language?: ScalarPatch<string | null>;
    text?: ScalarPatch<string>;
}

export interface MetadataAwardUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    body?: ScalarPatch<string>;
    name?: ScalarPatch<string>;
    category?: ScalarPatch<string | null>;
    status?: ScalarPatch<string | null>;
    year?: ScalarPatch<number | null>;
    language?: ScalarPatch<string | null>;
    country?: ScalarPatch<string | null>;
}

export interface MetadataContentRatingUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    body?: ScalarPatch<string>;
    rating?: ScalarPatch<string>;
}

export interface MetadataExternalIdUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    value?: ScalarPatch<string>;
    source?: ScalarPatch<string | null>;
}

export interface MetadataImageUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    type?: ScalarPatch<string>;
    mimeType?: ScalarPatch<string | null>;
    url?: ScalarPatch<string>;
    source?: ScalarPatch<string | null>;
    language?: ScalarPatch<string | null>;
    width?: ScalarPatch<number | null>;
    height?: ScalarPatch<number | null>;
}

export interface MetadataPreviewUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    kind?: ScalarPatch<string>;
    mimeType?: ScalarPatch<string | null>;
    url?: ScalarPatch<string>;
    source?: ScalarPatch<string | null>;
    title?: ScalarPatch<string | null>;
    releaseDate?: ScalarPatch<string | null>;
    language?: ScalarPatch<string | null>;
}

export interface MetadataReferenceUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    kind?: ScalarPatch<string>;
    source?: ScalarPatch<string>;
    url?: ScalarPatch<string>;
    displayName?: ScalarPatch<string | null>;
    language?: ScalarPatch<string | null>;
}

export interface MetadataRelationshipAdd {
    id?: string;
    ordinal?: ScalarPatch<number>;
    kind: ScalarPatch<string>;
    qualifier?: ScalarPatch<string | null>;
    subjectId?: string;
    subjectName?: string;
    subjectKind?: string;
}

export interface MetadataRelationshipUpdate {
    id: string;
    ordinal?: ScalarPatch<number>;
    kind?: ScalarPatch<string>;
    qualifier?: ScalarPatch<string | null>;
    subjectId?: string;
    subjectName?: string;
    subjectKind?: string;
}

export type MetadataAlternateTextAdd = Omit<MetadataAlternateText, 'id' | 'metadataContributionId'>;
export type MetadataAwardAdd = Omit<MetadataAward, 'id' | 'metadataContributionId'>;
export type MetadataContentRatingAdd = Omit<MetadataContentRating, 'id' | 'metadataContributionId'>;
export type MetadataExternalIdAdd = Omit<MetadataExternalId, 'id' | 'metadataContributionId' | 'releaseId'>;
export type MetadataImageAdd = Omit<MetadataImage, 'id' | 'metadataContributionId' | 'isPrimary'>;
export type MetadataPreviewAdd = Omit<MetadataPreview, 'id' | 'metadataContributionId'>;
export type MetadataReferenceAdd = Omit<MetadataReference, 'id' | 'metadataContributionId'>;

export interface MetadataContainerPatch {
    title?: ScalarPatch<string | null>;
    originalTitle?: ScalarPatch<string | null>;
    sortTitle?: ScalarPatch<string | null>;
    language?: ScalarPatch<string | null>;
    overview?: ScalarPatch<string | null>;
    publishDate?: ScalarPatch<string | null>;
    duration?: ScalarPatch<string | null>;
    sequenceNumber?: ScalarPatch<number | null>;
    productionStatus?: ScalarPatch<string | null>;
    established?: ScalarPatch<string | null>;
    dissolved?: ScalarPatch<string | null>;
    birthDate?: ScalarPatch<string | null>;
    deathDate?: ScalarPatch<string | null>;
    alternateTexts?: MetadataCollectionPatch<MetadataAlternateTextAdd, MetadataAlternateTextUpdate>;
    awards?: MetadataCollectionPatch<MetadataAwardAdd, MetadataAwardUpdate>;
    contentRatings?: MetadataCollectionPatch<MetadataContentRatingAdd, MetadataContentRatingUpdate>;
    customMetadata?: ScalarPatch<Record<string, string | null>>;
    externalIds?: MetadataCollectionPatch<MetadataExternalIdAdd, MetadataExternalIdUpdate>;
    genres?: MetadataSetPatch;
    images?: MetadataCollectionPatch<MetadataImageAdd, MetadataImageUpdate>;
    previews?: MetadataCollectionPatch<MetadataPreviewAdd, MetadataPreviewUpdate>;
    references?: MetadataCollectionPatch<MetadataReferenceAdd, MetadataReferenceUpdate>;
    relationships?: MetadataCollectionPatch<MetadataRelationshipAdd, MetadataRelationshipUpdate>;
    contributionIds?: string[];
}

export interface BulkResult {
    count: number;
    error: string | null;
}

export interface ContentHierarchyItemPatch {
    monitoring?: ScalarPatch<boolean>;
    rootFolderId?: ScalarPatch<string | null>;
    qualityTierListId?: ScalarPatch<string | null>;
    path?: ScalarPatch<string | null>;
    tags?: { add?: string[]; remove?: string[] };
}

export interface BulkContentPatchRequest {
    ids?: string[];
    filter?: string;
    patch: ContentHierarchyItemPatch;
}

export interface BulkContentDeleteRequest {
    ids?: string[];
    filter?: string;
}
