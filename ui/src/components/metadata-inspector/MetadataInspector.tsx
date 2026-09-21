import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { CaretDownIcon, CaretRightIcon, PencilSimpleIcon, PlusIcon, TrashIcon } from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Skeleton } from '@/components/ui/Skeleton';
import { SortableListControls, sortableListItemClassNames, useSortableList } from '@/components/ui/SortableList';
import { Tooltip, TooltipProvider } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import {
    useContentDetail,
    useMetadataInspector,
    usePatchMetadataInspector,
    useReorderMetadataContributions,
} from '@/hooks/useContent';
import {
    getStandardMetadataField,
    STANDARD_METADATA_FIELDS,
    useLibraryTypes,
    type LibraryMetadataFieldDefinition,
    type LibraryMetadataValueKind,
    type LibraryStandardMetadataFieldDefinition,
    type LibraryTypeDefinition,
} from '@/hooks/useLibraryTypes';
import { useSubject } from '@/hooks/useSubjects';
import {
    useSubjectMetadataInspector,
    usePatchSubjectMetadataInspector,
    useReorderSubjectMetadataContributions,
} from '@/hooks/useSubjectMetadataInspector';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import { humanizeCustomKey } from '@/lib/contentTypeDisplay';
import { formatShortDate } from '@/lib/dateFormatting';
import { getErrorMessage } from '@/lib/errors';
import type {
    MetadataAlternateTextUpdate,
    MetadataAwardUpdate,
    MetadataCollectionPatch,
    MetadataContainerPatch,
    MetadataContentRatingUpdate,
    MetadataContribution,
    MetadataExternalIdUpdate,
    MetadataImageUpdate,
    MetadataInspectorField,
    MetadataPreviewUpdate,
    MetadataReferenceUpdate,
    MetadataRelationshipAdd,
    MetadataRelationshipUpdate,
    MetadataSubItemKey,
    SubItemPayload,
    SubjectRef,
} from '@/types';

import { SubItemFormDialog } from './SubItemFormDialog';
import type { SubjectSelection } from './SubjectPicker';

import './MetadataInspector.css';

const MANAGER_REQUIRED_MESSAGE = 'Requires Manager role to edit metadata.';
const USER_BUFFER_ID = 'user-buffer';

type MetadataInspectorOwner = { kind: 'content' | 'subject'; id: string | undefined };

interface MetadataInspectorProps {
    contentId?: string | undefined;
    owner?: MetadataInspectorOwner;
}

interface InspectorGenre {
    itemId: string;
    name: string;
}

interface InspectorImage {
    itemId: string;
    type: string;
    url: string;
    language?: string | null;
    mimeType?: string | null;
    width?: number | null;
    height?: number | null;
}

interface InspectorRelationship {
    itemId: string;
    kind: string;
    qualifier?: string | null;
    relatedMetadataContainerId?: string | null;
    subjectId?: string | null;
    name?: string | null;
}

interface InspectorExternalId {
    itemId: string;
    value: string;
    source?: string | null;
}

interface InspectorPreview {
    itemId: string;
    kind: string;
    url: string;
    title?: string | null;
    mimeType?: string | null;
    releaseDate?: string | null;
    language?: string | null;
}

interface InspectorAlternateText {
    itemId: string;
    kind: string;
    text: string;
    language?: string | null;
}

interface InspectorAward {
    itemId: string;
    body: string;
    name: string;
    category?: string | null;
    status?: string | null;
    year?: number | null;
    language?: string | null;
    country?: string | null;
}

interface InspectorContentRating {
    itemId: string;
    body: string;
    rating: string;
}

interface InspectorCustomValue {
    itemId: string;
    key: string;
    value?: string | null;
}

interface InspectorReference {
    itemId: string;
    kind: string;
    source: string;
    url: string;
    displayName?: string | null;
    language?: string | null;
}

interface InspectorContribution {
    id: string;
    providerType: string;
    ordinal: number;
    isUserContribution: boolean;
    source: string | null;
    dateLastRefreshed: string | null;
    fieldValues: Record<string, string>;
    images: InspectorImage[];
    genres: InspectorGenre[];
    relationships: InspectorRelationship[];
    externalIds: InspectorExternalId[];
    previews: InspectorPreview[];
    alternateTexts: InspectorAlternateText[];
    awards: InspectorAward[];
    contentRatings: InspectorContentRating[];
    customValues: InspectorCustomValue[];
    references: InspectorReference[];
}

type BufferRelationship = InspectorRelationship & { subjectName?: string; subjectKind?: string };

const SCALAR_PATCH_KEYS: Record<string, keyof MetadataContainerPatch> = {
    Title: 'title',
    OriginalTitle: 'originalTitle',
    SortTitle: 'sortTitle',
    Language: 'language',
    Overview: 'overview',
    PublishDate: 'publishDate',
    Duration: 'duration',
    SequenceNumber: 'sequenceNumber',
    ProductionStatus: 'productionStatus',
    Established: 'established',
    Dissolved: 'dissolved',
    BirthDate: 'birthDate',
    DeathDate: 'deathDate',
};

const INTERNAL_SOURCE_PREFIX = 'internal.';

function useInspectorHooks(owner: MetadataInspectorOwner) {
    const isContent = owner.kind === 'content';
    const id = owner.id;

    const contentData = useMetadataInspector(id, { enabled: isContent });
    const subjectData = useSubjectMetadataInspector(id, { enabled: !isContent });
    const contentPatch = usePatchMetadataInspector(id);
    const subjectPatch = usePatchSubjectMetadataInspector(id);
    const contentReorder = useReorderMetadataContributions(id);
    const subjectReorder = useReorderSubjectMetadataContributions(id);

    return {
        data: isContent ? contentData : subjectData,
        patch: isContent ? contentPatch : subjectPatch,
        reorder: isContent ? contentReorder : subjectReorder,
    };
}

function emptyUserContribution(ordinal: number): InspectorContribution {
    return {
        id: USER_BUFFER_ID,
        providerType: 'user',
        ordinal,
        isUserContribution: true,
        source: 'user',
        dateLastRefreshed: null,
        fieldValues: {},
        images: [],
        genres: [],
        relationships: [],
        externalIds: [],
        previews: [],
        alternateTexts: [],
        awards: [],
        contentRatings: [],
        customValues: [],
        references: [],
    };
}

type ScalarMetadataSource = Pick<
    MetadataContribution,
    | 'title'
    | 'originalTitle'
    | 'sortTitle'
    | 'language'
    | 'overview'
    | 'publishDate'
    | 'duration'
    | 'sequenceNumber'
    | 'productionStatus'
    | 'established'
    | 'dissolved'
    | 'birthDate'
    | 'deathDate'
>;

function scalarFieldValues(source: ScalarMetadataSource): Record<string, string> {
    const values: Record<string, string> = {};
    const set = (key: string, value: string | number | null | undefined) => {
        if (value !== null && value !== undefined && value !== '') {
            values[key] = String(value);
        }
    };
    set('Title', source.title);
    set('OriginalTitle', source.originalTitle);
    set('SortTitle', source.sortTitle);
    set('Language', source.language);
    set('Overview', source.overview);
    set('PublishDate', source.publishDate);
    set('Duration', source.duration);
    set('SequenceNumber', source.sequenceNumber);
    set('ProductionStatus', source.productionStatus);
    set('Established', source.established);
    set('Dissolved', source.dissolved);
    set('BirthDate', source.birthDate);
    set('DeathDate', source.deathDate);
    return values;
}

function byOrdinal<T extends { ordinal?: number }>(items: T[] | undefined): T[] {
    return [...(items ?? [])].sort((a, b) => (a.ordinal ?? 0) - (b.ordinal ?? 0));
}

function toInspectorContribution(
    contribution: MetadataContribution,
    subjects: Record<string, SubjectRef>,
): InspectorContribution {
    return {
        id: contribution.id,
        providerType: contribution.providerType,
        ordinal: contribution.ordinal,
        isUserContribution: contribution.providerType.toLowerCase() === 'user',
        source: contribution.source ?? null,
        dateLastRefreshed: contribution.dateLastRefreshed ?? null,
        fieldValues: scalarFieldValues(contribution),
        genres: byOrdinal(contribution.genres).map((g) => ({ itemId: g.id, name: g.name })),
        images: byOrdinal(contribution.images).map((i) => ({
            itemId: i.id,
            type: i.type,
            url: i.url,
            language: i.language ?? null,
            mimeType: i.mimeType ?? null,
            width: i.width ?? null,
            height: i.height ?? null,
        })),
        relationships: byOrdinal(contribution.relationships).map((r) => {
            const related = r.relatedMetadataContainerId ? subjects[r.relatedMetadataContainerId] : undefined;
            return {
                itemId: r.id,
                kind: r.kind,
                qualifier: r.qualifier ?? null,
                relatedMetadataContainerId: r.relatedMetadataContainerId ?? null,
                subjectId: related?.subjectId ?? null,
                name: r.relatedMetadataContainerId ? (related?.name ?? 'Unknown') : null,
            };
        }),
        externalIds: byOrdinal(contribution.externalIds)
            .filter((e) => !e.source?.startsWith(INTERNAL_SOURCE_PREFIX))
            .map((e) => ({ itemId: e.id, value: e.value, source: e.source ?? null })),
        previews: byOrdinal(contribution.previews).map((p) => ({
            itemId: p.id,
            kind: p.kind,
            url: p.url,
            title: p.title ?? null,
            mimeType: p.mimeType ?? null,
            releaseDate: p.releaseDate ?? null,
            language: p.language ?? null,
        })),
        alternateTexts: byOrdinal(contribution.alternateTexts).map((a) => ({
            itemId: a.id,
            kind: a.kind,
            text: a.text,
            language: a.language ?? null,
        })),
        awards: byOrdinal(contribution.awards).map((a) => ({
            itemId: a.id,
            body: a.body,
            name: a.name,
            category: a.category ?? null,
            status: a.status ?? null,
            year: a.year ?? null,
            language: a.language ?? null,
            country: a.country ?? null,
        })),
        contentRatings: byOrdinal(contribution.contentRatings).map((r) => ({
            itemId: r.id,
            body: r.body,
            rating: r.rating,
        })),
        customValues: (contribution.customMetadata ?? []).map((v) => ({
            itemId: v.id,
            key: v.key,
            value: v.value ?? null,
        })),
        references: byOrdinal(contribution.references).map((r) => ({
            itemId: r.id,
            kind: r.kind,
            source: r.source,
            url: r.url,
            displayName: r.displayName ?? null,
            language: r.language ?? null,
        })),
    };
}

function cloneUserContribution(contribution: InspectorContribution): InspectorContribution {
    return JSON.parse(JSON.stringify(contribution)) as InspectorContribution;
}

function userContributionHasContent(contribution: InspectorContribution): boolean {
    if (Object.keys(contribution.fieldValues ?? {}).length > 0) {
        return true;
    }
    return SUBITEM_RENDERERS.some((renderer) => (contribution[renderer.key]?.length ?? 0) > 0);
}

function mergeFields(
    contributions: InspectorContribution[],
    values: Record<string, string>,
): Record<string, MetadataInspectorField> {
    const provenance = resolveFields(contributions);
    const result: Record<string, MetadataInspectorField> = {};
    for (const key of new Set([...Object.keys(values), ...Object.keys(provenance)])) {
        const winner = provenance[key];
        result[key] = {
            value: winner?.value ?? values[key] ?? null,
            contributionId: winner?.contributionId ?? '',
            providerType: winner?.providerType ?? '',
            ordinal: winner?.ordinal ?? 0,
        };
    }
    return result;
}

function resolveFields(contributions: InspectorContribution[]): Record<string, MetadataInspectorField> {
    const result: Record<string, MetadataInspectorField> = {};
    for (const contribution of contributions) {
        const fieldValues = contribution.fieldValues ?? {};
        for (const [key, value] of Object.entries(fieldValues)) {
            const existing = result[key];
            if (!existing || contribution.ordinal < existing.ordinal) {
                result[key] = {
                    value,
                    contributionId: contribution.id,
                    providerType: contribution.providerType,
                    ordinal: contribution.ordinal,
                };
            }
        }
    }
    return result;
}

function realItemId(itemId: string | undefined): string | undefined {
    return itemId && !itemId.startsWith('new:') ? itemId : undefined;
}

function scalarPatchValue(fieldKey: string, raw: string | undefined): string | number | null {
    if (raw === undefined || raw === '') {
        return null;
    }
    if (fieldKey === 'SequenceNumber') {
        const parsed = Number.parseInt(raw, 10);
        return Number.isNaN(parsed) ? null : parsed;
    }
    return raw;
}

function diffFields<TView extends { itemId: string }>(
    before: TView,
    after: TView,
    keys: (keyof TView & string)[],
): (Record<string, unknown> & { id: string }) | null {
    const changes: Record<string, unknown> = {};
    let dirty = false;
    for (const key of keys) {
        const b = (before[key] ?? null) as unknown;
        const a = (after[key] ?? null) as unknown;
        if (b !== a) {
            changes[key] = { value: a };
            dirty = true;
        }
    }
    return dirty ? { id: after.itemId, ...changes } : null;
}

function diffCollection<TView extends { itemId: string }, TAdd, TUpdate extends { id: string }>(
    baseline: TView[],
    working: TView[],
    toAdd: (item: TView) => TAdd,
    toUpdate: (before: TView, after: TView) => TUpdate | null,
): MetadataCollectionPatch<TAdd, TUpdate> | undefined {
    const baseById = new Map(baseline.map((item) => [item.itemId, item]));
    const add: TAdd[] = [];
    const update: TUpdate[] = [];
    const seen = new Set<string>();
    for (const item of working) {
        const before = realItemId(item.itemId) ? baseById.get(item.itemId) : undefined;
        if (!before) {
            add.push(toAdd(item));
            continue;
        }
        seen.add(item.itemId);
        const itemUpdate = toUpdate(before, item);
        if (itemUpdate) {
            update.push(itemUpdate);
        }
    }
    const remove = [...baseById.keys()].filter((id) => !seen.has(id));
    if (add.length === 0 && remove.length === 0 && update.length === 0) {
        return undefined;
    }
    const patch: MetadataCollectionPatch<TAdd, TUpdate> = {};
    if (add.length > 0) {
        patch.add = add;
    }
    if (remove.length > 0) {
        patch.remove = remove;
    }
    if (update.length > 0) {
        patch.update = update;
    }
    return patch;
}

function customValuesMap(values: InspectorCustomValue[]): Record<string, string | null> {
    const map: Record<string, string | null> = {};
    for (const value of values) {
        map[value.key] = value.value ?? null;
    }
    return map;
}

function recordsEqual(a: Record<string, string | null>, b: Record<string, string | null>): boolean {
    const aKeys = Object.keys(a);
    return aKeys.length === Object.keys(b).length && aKeys.every((key) => key in b && a[key] === b[key]);
}

function relationshipAdd(relationship: InspectorRelationship): MetadataRelationshipAdd {
    const rel = relationship as BufferRelationship;
    const add: MetadataRelationshipAdd = { kind: { value: rel.kind } };
    if (rel.qualifier) {
        add.qualifier = { value: rel.qualifier };
    }
    if (rel.subjectId) {
        add.subjectId = rel.subjectId;
    } else if (rel.subjectName) {
        add.subjectName = rel.subjectName;
        if (rel.subjectKind) {
            add.subjectKind = rel.subjectKind;
        }
    }
    return add;
}

function relationshipUpdate(
    before: InspectorRelationship,
    after: InspectorRelationship,
): MetadataRelationshipUpdate | null {
    const update: MetadataRelationshipUpdate = { id: after.itemId };
    let dirty = false;
    if (before.kind !== after.kind) {
        update.kind = { value: after.kind };
        dirty = true;
    }
    if ((before.qualifier ?? null) !== (after.qualifier ?? null)) {
        update.qualifier = { value: after.qualifier ?? null };
        dirty = true;
    }
    const afterBuffer = after as BufferRelationship;
    if (after.subjectId && after.subjectId !== before.subjectId) {
        update.subjectId = after.subjectId;
        dirty = true;
    } else if (!after.subjectId && afterBuffer.subjectName) {
        update.subjectName = afterBuffer.subjectName;
        if (afterBuffer.subjectKind) {
            update.subjectKind = afterBuffer.subjectKind;
        }
        dirty = true;
    }
    return dirty ? update : null;
}

function buildDeltaPatch(baseline: InspectorContribution, working: InspectorContribution): MetadataContainerPatch {
    const patch: MetadataContainerPatch = {};

    for (const fieldKey of Object.keys(SCALAR_PATCH_KEYS)) {
        const before = baseline.fieldValues[fieldKey] ?? '';
        const after = working.fieldValues[fieldKey] ?? '';
        if (before !== after) {
            patch[SCALAR_PATCH_KEYS[fieldKey]] = { value: scalarPatchValue(fieldKey, after) } as never;
        }
    }

    const beforeGenres = baseline.genres.map((g) => g.name);
    const afterGenres = working.genres.map((g) => g.name);
    const addedGenres = afterGenres.filter((name) => !beforeGenres.includes(name));
    const removedGenres = beforeGenres.filter((name) => !afterGenres.includes(name));
    if (addedGenres.length > 0 || removedGenres.length > 0) {
        patch.genres = {};
        if (addedGenres.length > 0) {
            patch.genres.add = addedGenres;
        }
        if (removedGenres.length > 0) {
            patch.genres.remove = removedGenres;
        }
    }

    const beforeCustom = customValuesMap(baseline.customValues);
    const afterCustom = customValuesMap(working.customValues);
    if (!recordsEqual(beforeCustom, afterCustom)) {
        patch.customMetadata = { value: afterCustom };
    }

    const images = diffCollection(
        baseline.images,
        working.images,
        (i) => ({
            type: i.type,
            url: i.url,
            mimeType: i.mimeType ?? null,
            language: i.language ?? null,
            width: i.width ?? null,
            height: i.height ?? null,
        }),
        (before, after) =>
            diffFields(before, after, [
                'type',
                'url',
                'mimeType',
                'language',
                'width',
                'height',
            ]) as MetadataImageUpdate | null,
    );
    if (images) {
        patch.images = images;
    }

    const previews = diffCollection(
        baseline.previews,
        working.previews,
        (p) => ({
            kind: p.kind,
            url: p.url,
            mimeType: p.mimeType ?? null,
            title: p.title ?? null,
            releaseDate: p.releaseDate ?? null,
            language: p.language ?? null,
        }),
        (before, after) =>
            diffFields(before, after, [
                'kind',
                'url',
                'mimeType',
                'title',
                'releaseDate',
                'language',
            ]) as MetadataPreviewUpdate | null,
    );
    if (previews) {
        patch.previews = previews;
    }

    const externalIds = diffCollection(
        baseline.externalIds,
        working.externalIds,
        (e) => ({ value: e.value, source: e.source ?? null }),
        (before, after) => diffFields(before, after, ['value', 'source']) as MetadataExternalIdUpdate | null,
    );
    if (externalIds) {
        patch.externalIds = externalIds;
    }

    const alternateTexts = diffCollection(
        baseline.alternateTexts,
        working.alternateTexts,
        (a) => ({ kind: a.kind, text: a.text, language: a.language ?? null }),
        (before, after) =>
            diffFields(before, after, ['kind', 'text', 'language']) as MetadataAlternateTextUpdate | null,
    );
    if (alternateTexts) {
        patch.alternateTexts = alternateTexts;
    }

    const awards = diffCollection(
        baseline.awards,
        working.awards,
        (a) => ({
            body: a.body,
            name: a.name,
            category: a.category ?? null,
            status: a.status ?? null,
            year: a.year ?? null,
            language: a.language ?? null,
            country: a.country ?? null,
        }),
        (before, after) =>
            diffFields(before, after, [
                'body',
                'name',
                'category',
                'status',
                'year',
                'language',
                'country',
            ]) as MetadataAwardUpdate | null,
    );
    if (awards) {
        patch.awards = awards;
    }

    const contentRatings = diffCollection(
        baseline.contentRatings,
        working.contentRatings,
        (r) => ({ body: r.body, rating: r.rating }),
        (before, after) => diffFields(before, after, ['body', 'rating']) as MetadataContentRatingUpdate | null,
    );
    if (contentRatings) {
        patch.contentRatings = contentRatings;
    }

    const references = diffCollection(
        baseline.references,
        working.references,
        (r) => ({
            kind: r.kind,
            source: r.source,
            url: r.url,
            displayName: r.displayName ?? null,
            language: r.language ?? null,
        }),
        (before, after) =>
            diffFields(before, after, [
                'kind',
                'source',
                'url',
                'displayName',
                'language',
            ]) as MetadataReferenceUpdate | null,
    );
    if (references) {
        patch.references = references;
    }

    const relationships = diffCollection(
        baseline.relationships,
        working.relationships,
        relationshipAdd,
        relationshipUpdate,
    );
    if (relationships) {
        patch.relationships = relationships;
    }

    return patch;
}

let bufferItemCounter = 0;

function nextBufferItemId(): string {
    bufferItemCounter += 1;
    return `new:${bufferItemCounter}`;
}

function payloadToElement(kind: MetadataSubItemKey, payload: SubItemPayload, itemId: string): Record<string, unknown> {
    switch (kind) {
        case 'relationships': {
            const p = payload as import('@/types').RelationshipSubItemPayload;
            const element: BufferRelationship = { itemId, kind: p.kind, qualifier: p.qualifier ?? null };
            if (p.subjectId) {
                element.subjectId = p.subjectId;
            } else if (p.subjectName) {
                element.subjectName = p.subjectName;
                element.subjectKind = p.subjectKind;
                element.name = p.subjectName;
            }
            return element as unknown as Record<string, unknown>;
        }
        case 'genres': {
            const p = payload as import('@/types').GenreSubItemPayload;
            return { itemId, name: p.name };
        }
        case 'images': {
            const p = payload as import('@/types').ImageSubItemPayload;
            return {
                itemId,
                type: p.type,
                url: p.url,
                mimeType: p.mimeType ?? null,
                language: p.language ?? null,
                width: p.width ?? null,
                height: p.height ?? null,
            };
        }
        case 'previews': {
            const p = payload as import('@/types').PreviewSubItemPayload;
            return {
                itemId,
                kind: p.kind,
                url: p.url,
                mimeType: p.mimeType ?? null,
                title: p.title ?? null,
                releaseDate: p.releaseDate ?? null,
                language: p.language ?? null,
            };
        }
        case 'externalIds': {
            const p = payload as import('@/types').ExternalIdSubItemPayload;
            return { itemId, value: p.value, source: p.source ?? null };
        }
        case 'alternateTexts': {
            const p = payload as import('@/types').AlternateTextSubItemPayload;
            return { itemId, kind: p.kind, text: p.text, language: p.language ?? null };
        }
        case 'awards': {
            const p = payload as import('@/types').AwardSubItemPayload;
            return {
                itemId,
                body: p.body,
                name: p.name,
                category: p.category ?? null,
                status: p.status ?? null,
                year: p.year ?? null,
                language: p.language ?? null,
                country: p.country ?? null,
            };
        }
        case 'contentRatings': {
            const p = payload as import('@/types').ContentRatingSubItemPayload;
            return { itemId, body: p.body, rating: p.rating };
        }
        case 'customValues': {
            const p = payload as import('@/types').CustomMetadataValueSubItemPayload;
            return { itemId, key: p.key, value: p.value ?? null };
        }
        case 'references': {
            const p = payload as import('@/types').ReferenceSubItemPayload;
            return {
                itemId,
                kind: p.kind,
                source: p.source,
                url: p.url,
                displayName: p.displayName ?? null,
                language: p.language ?? null,
            };
        }
    }
}

interface InspectorFieldDefinition {
    key: string;
    label: string;
    valueKind: LibraryMetadataValueKind;
    dataType?: string | null;
    format?: string | null;
    editable: boolean;
}

interface OwnerMetadataDefinitions {
    standardFields: LibraryStandardMetadataFieldDefinition[];
    customFields: LibraryMetadataFieldDefinition[];
}

const FALLBACK_SCALAR_FIELDS: InspectorFieldDefinition[] = STANDARD_METADATA_FIELDS.filter(
    (field) => field.valueKind !== 'collection',
).map(toInspectorField);

function definitionLabel(definition: LibraryMetadataFieldDefinition): string {
    return definition.displayName?.trim() || humanizeCustomKey(definition.key);
}

function toInspectorField(definition: LibraryStandardMetadataFieldDefinition): InspectorFieldDefinition {
    return {
        key: definition.field,
        label: definitionLabel(definition),
        valueKind: definition.valueKind,
        dataType: definition.dataType,
        format: definition.format,
        editable: definition.editable,
    };
}

function formatFieldValue(field: InspectorFieldDefinition, value: string | null | undefined): string {
    if (value === null || value === undefined || value === '') {
        return '';
    }
    if (field.valueKind === 'date' || field.dataType?.toLowerCase() === 'date') {
        return formatShortDate(value, value);
    }
    return value;
}

interface SubItemRow {
    itemId: string;
    text: string;
    editValues: Record<string, string>;
    subject?: SubjectSelection | null;
}

interface SubItemRenderContext {
    customMetadataByKey: Map<string, LibraryMetadataFieldDefinition>;
}

function str(value: string | number | null | undefined): string {
    return value === null || value === undefined ? '' : String(value);
}

function relationshipText(r: InspectorRelationship): string {
    const detail = [r.kind, r.qualifier].filter(Boolean).join(' • ');
    if (r.name) {
        return detail ? `${r.name} (${detail})` : r.name;
    }
    return detail;
}

function customValueText(value: { key: string; value?: string | null }, context: SubItemRenderContext): string {
    const label = definitionLabel(context.customMetadataByKey.get(value.key.toLowerCase()) ?? { key: value.key });
    return [label, value.value].filter((x) => x !== null && x !== undefined && x !== '').join(': ');
}

const SUBITEM_RENDERERS: ReadonlyArray<{
    key: MetadataSubItemKey;
    standardField?: string;
    fallbackLabel: string;
    lines: (c: InspectorContribution, context: SubItemRenderContext) => string[];
    rows: (c: InspectorContribution, context: SubItemRenderContext) => SubItemRow[];
}> = [
    {
        key: 'genres',
        standardField: 'Genres',
        fallbackLabel: 'Genres',
        lines: (c) => (c.genres ?? []).map((g) => g.name),
        rows: (c) =>
            (c.genres ?? []).map((g) => ({
                itemId: g.itemId,
                text: g.name,
                editValues: { name: g.name },
            })),
    },
    {
        key: 'images',
        standardField: 'Images',
        fallbackLabel: 'Images',
        lines: (c) => (c.images ?? []).map((i) => [i.type, i.url, i.language].filter(Boolean).join(' • ')),
        rows: (c) =>
            (c.images ?? []).map((i) => ({
                itemId: i.itemId,
                text: [i.type, i.url, i.language].filter(Boolean).join(' • '),
                editValues: {
                    type: str(i.type),
                    url: str(i.url),
                    mimeType: str(i.mimeType),
                    language: str(i.language),
                    width: str(i.width),
                    height: str(i.height),
                },
            })),
    },
    {
        key: 'relationships',
        standardField: 'Relationships',
        fallbackLabel: 'Relationships',
        lines: (c) => (c.relationships ?? []).map((r) => relationshipText(r)),
        rows: (c) =>
            (c.relationships ?? []).map((r) => {
                const rel = r as BufferRelationship;
                const subject: SubjectSelection | null = rel.subjectId
                    ? { mode: 'existing', subjectId: rel.subjectId }
                    : rel.subjectName
                      ? { mode: 'new', subjectName: rel.subjectName, subjectKind: rel.subjectKind ?? '' }
                      : null;
                return {
                    itemId: r.itemId,
                    text: relationshipText(r),
                    editValues: { kind: str(r.kind), qualifier: str(r.qualifier) },
                    subject,
                };
            }),
    },
    {
        key: 'externalIds',
        standardField: 'ExternalIds',
        fallbackLabel: 'External IDs',
        lines: (c) => (c.externalIds ?? []).map((e) => [e.source, e.value].filter(Boolean).join(': ')),
        rows: (c) =>
            (c.externalIds ?? []).map((e) => ({
                itemId: e.itemId,
                text: [e.source, e.value].filter(Boolean).join(': '),
                editValues: { value: str(e.value), source: str(e.source) },
            })),
    },
    {
        key: 'previews',
        standardField: 'Previews',
        fallbackLabel: 'Previews',
        lines: (c) => (c.previews ?? []).map((p) => [p.kind, p.title ?? p.url].filter(Boolean).join(' • ')),
        rows: (c) =>
            (c.previews ?? []).map((p) => ({
                itemId: p.itemId,
                text: [p.kind, p.title ?? p.url].filter(Boolean).join(' • '),
                editValues: {
                    kind: str(p.kind),
                    url: str(p.url),
                    mimeType: str(p.mimeType),
                    title: str(p.title),
                    releaseDate: str(p.releaseDate),
                    language: str(p.language),
                },
            })),
    },
    {
        key: 'alternateTexts',
        standardField: 'AlternateTexts',
        fallbackLabel: 'Alternate texts',
        lines: (c) => (c.alternateTexts ?? []).map((a) => [a.kind, a.text, a.language].filter(Boolean).join(' • ')),
        rows: (c) =>
            (c.alternateTexts ?? []).map((a) => ({
                itemId: a.itemId,
                text: [a.kind, a.text, a.language].filter(Boolean).join(' • '),
                editValues: { kind: str(a.kind), text: str(a.text), language: str(a.language) },
            })),
    },
    {
        key: 'awards',
        standardField: 'Awards',
        fallbackLabel: 'Awards',
        lines: (c) =>
            (c.awards ?? []).map((a) =>
                [a.body, a.name, a.category, a.status, a.year?.toString()].filter(Boolean).join(' • '),
            ),
        rows: (c) =>
            (c.awards ?? []).map((a) => ({
                itemId: a.itemId,
                text: [a.body, a.name, a.category, a.status, a.year?.toString()].filter(Boolean).join(' • '),
                editValues: {
                    body: str(a.body),
                    name: str(a.name),
                    category: str(a.category),
                    status: str(a.status),
                    year: str(a.year),
                    language: str(a.language),
                    country: str(a.country),
                },
            })),
    },
    {
        key: 'contentRatings',
        standardField: 'ContentRatings',
        fallbackLabel: 'Content ratings',
        lines: (c) => (c.contentRatings ?? []).map((r) => `${r.body}: ${r.rating}`),
        rows: (c) =>
            (c.contentRatings ?? []).map((r) => ({
                itemId: r.itemId,
                text: `${r.body}: ${r.rating}`,
                editValues: { body: str(r.body), rating: str(r.rating) },
            })),
    },
    {
        key: 'customValues',
        fallbackLabel: 'Custom values',
        lines: (c, context) => (c.customValues ?? []).map((v) => customValueText(v, context)),
        rows: (c, context) =>
            (c.customValues ?? []).map((v) => ({
                itemId: v.itemId,
                text: customValueText(v, context),
                editValues: { key: str(v.key), value: str(v.value) },
            })),
    },
    {
        key: 'references',
        standardField: 'References',
        fallbackLabel: 'References',
        lines: (c) =>
            (c.references ?? []).map((r) => [r.kind, r.displayName ?? r.url, r.source].filter(Boolean).join(' • ')),
        rows: (c) =>
            (c.references ?? []).map((r) => ({
                itemId: r.itemId,
                text: [r.kind, r.displayName ?? r.url, r.source].filter(Boolean).join(' • '),
                editValues: {
                    kind: str(r.kind),
                    source: str(r.source),
                    url: str(r.url),
                    displayName: str(r.displayName),
                    language: str(r.language),
                },
            })),
    },
];

interface SubItemDefinition {
    key: MetadataSubItemKey;
    label: string;
    lines: (c: InspectorContribution, context: SubItemRenderContext) => string[];
    rows: (c: InspectorContribution, context: SubItemRenderContext) => SubItemRow[];
}

function hasSubItemRows(renderer: (typeof SUBITEM_RENDERERS)[number], contributions: InspectorContribution[]) {
    const emptyContext: SubItemRenderContext = { customMetadataByKey: new Map() };
    return contributions.some((contribution) => renderer.rows(contribution, emptyContext).length > 0);
}

function buildSubItemDefinitions(
    ownerDefinitions: OwnerMetadataDefinitions | null,
    contributions: InspectorContribution[],
): SubItemDefinition[] {
    if (!ownerDefinitions) {
        return SUBITEM_RENDERERS.map((renderer) => ({ ...renderer, label: renderer.fallbackLabel }));
    }

    return SUBITEM_RENDERERS.flatMap((renderer) => {
        if (renderer.key === 'customValues') {
            if (ownerDefinitions.customFields.length === 0 && !hasSubItemRows(renderer, contributions)) {
                return [];
            }
            return [{ ...renderer, label: renderer.fallbackLabel }];
        }

        if (!renderer.standardField) {
            return [];
        }

        const field = getStandardMetadataField(
            { standardMetadataFields: ownerDefinitions.standardFields },
            renderer.standardField,
        );
        return field ? [{ ...renderer, label: definitionLabel(field) }] : [];
    });
}

function buildScalarFields(ownerDefinitions: OwnerMetadataDefinitions | null): InspectorFieldDefinition[] {
    if (!ownerDefinitions) {
        return FALLBACK_SCALAR_FIELDS;
    }
    return ownerDefinitions.standardFields.filter((field) => field.valueKind !== 'collection').map(toInspectorField);
}

function buildSubItemContext(ownerDefinitions: OwnerMetadataDefinitions | null): SubItemRenderContext {
    const customMetadataByKey = new Map<string, LibraryMetadataFieldDefinition>();
    for (const field of ownerDefinitions?.customFields ?? []) {
        customMetadataByKey.set(field.key.toLowerCase(), field);
    }
    return { customMetadataByKey };
}

function mergeStandardFieldHints(
    standardFields: LibraryStandardMetadataFieldDefinition[] | undefined,
    hintFields: LibraryMetadataFieldDefinition[],
): LibraryStandardMetadataFieldDefinition[] {
    const hintsByKey = new Map(hintFields.map((field) => [field.key.toLowerCase(), field]));
    return (standardFields ?? []).map((field) => {
        const hint = hintsByKey.get(field.key.toLowerCase());
        if (!hint) {
            return field;
        }
        return {
            ...field,
            displayName: hint.displayName ?? field.displayName,
            dataType: hint.dataType ?? field.dataType,
            format: hint.format ?? field.format,
        };
    });
}

function resolveContentDefinitions(
    contentTypes: LibraryTypeDefinition[],
    content: { contentType: string; level: string } | null | undefined,
): OwnerMetadataDefinitions | null {
    if (!content) {
        return null;
    }
    const type = contentTypes.find((t) => t.slug === content.contentType);
    const level = type?.levels.find((l) => l.slug === content.level);
    if (level) {
        return {
            standardFields: mergeStandardFieldHints(level.standardMetadataFields, [
                ...(level.standardFactFields ?? []),
                ...(level.customMetadataFields ?? []),
            ]),
            customFields: level.customMetadataFields ?? [],
        };
    }
    if (type) {
        return {
            standardFields: mergeStandardFieldHints(type.standardMetadataFields, [
                ...type.levels.flatMap((l) => l.standardFactFields ?? []),
                ...(type.customMetadataFields ?? []),
            ]),
            customFields: type.customMetadataFields ?? [],
        };
    }
    return null;
}

function resolveSubjectDefinitions(
    subjectTypes: LibraryTypeDefinition[],
    subject: { kind: string } | null | undefined,
): OwnerMetadataDefinitions | null {
    if (!subject) {
        return null;
    }
    const type = subjectTypes.find((t) => t.slug === subject.kind);
    if (!type) {
        return null;
    }
    return {
        standardFields: mergeStandardFieldHints(type.standardMetadataFields, type.customMetadataFields ?? []),
        customFields: type.customMetadataFields ?? [],
    };
}

interface EditState {
    field: string;
    label: string;
    initialValue: string;
}

interface SubItemFormState {
    kind: MetadataSubItemKey;
    label: string;
    mode: 'add' | 'edit';
    itemId?: string;
    initialValues?: Record<string, string>;
    initialSubject?: SubjectSelection | null;
}

export function MetadataInspector({ contentId, owner }: MetadataInspectorProps) {
    const resolvedOwner: MetadataInspectorOwner = owner ?? { kind: 'content', id: contentId };
    const isContentOwner = resolvedOwner.kind === 'content';
    const { data: query, patch, reorder } = useInspectorHooks(resolvedOwner);
    const { data, isLoading, error } = query;
    const contentDetail = useContentDetail(resolvedOwner.id, { enabled: isContentOwner });
    const subjectDetail = useSubject(
        isContentOwner ? '' : (resolvedOwner.id ?? ''),
        !isContentOwner && !!resolvedOwner.id,
    );
    const { contentTypes = [], subjectTypes = [] } = useLibraryTypes();
    const { hasRole } = useAuth();
    const canManage = hasRole(Role.Manager);
    const { toast } = useToast();
    const [editState, setEditState] = useState<EditState | null>(null);
    const [subItemForm, setSubItemForm] = useState<SubItemFormState | null>(null);
    const [expanded, setExpanded] = useState<Set<string>>(() => new Set());

    const inspectorContributions = useMemo(
        () => (data?.contributions ?? []).map((c) => toInspectorContribution(c, data?.subjects ?? {})),
        [data],
    );

    const providerContributions = useMemo(
        () =>
            inspectorContributions
                .filter((contribution) => !contribution.isUserContribution)
                .sort((left, right) => left.ordinal - right.ordinal || left.id.localeCompare(right.id)),
        [inspectorContributions],
    );

    const baselineUser = useMemo<InspectorContribution | null>(() => {
        if (!data) {
            return null;
        }
        const serverUser = inspectorContributions.find((c) => c.isUserContribution);
        if (serverUser) {
            return serverUser;
        }
        const minOrdinal = providerContributions.reduce((min, c) => Math.min(min, c.ordinal), 0);
        return emptyUserContribution(minOrdinal - 1);
    }, [data, inspectorContributions, providerContributions]);

    const [bufferUser, setBufferUser] = useState<InspectorContribution | null>(null);
    const baselineUserRef = useRef<InspectorContribution | null>(null);
    const ownerKey = `${resolvedOwner.kind}:${resolvedOwner.id}`;
    const ownerKeyRef = useRef(ownerKey);

    useEffect(() => {
        const previousBaseline = baselineUserRef.current;
        const ownerChanged = ownerKeyRef.current !== ownerKey;
        setBufferUser((current) => {
            if (!baselineUser) {
                return null;
            }
            if (
                !ownerChanged &&
                current &&
                previousBaseline &&
                Object.keys(buildDeltaPatch(previousBaseline, current)).length > 0
            ) {
                return current;
            }
            return cloneUserContribution(baselineUser);
        });
        baselineUserRef.current = baselineUser;
        ownerKeyRef.current = ownerKey;
    }, [baselineUser, ownerKey]);

    const workingUser = bufferUser ?? baselineUser;

    const changeCount = useMemo(() => {
        if (!workingUser || !baselineUser) {
            return 0;
        }
        return Object.keys(buildDeltaPatch(baselineUser, workingUser)).length;
    }, [workingUser, baselineUser]);
    const dirty = changeCount > 0;

    const pendingFields = useMemo(() => {
        const keys = new Set<string>();
        if (!workingUser || !baselineUser) {
            return keys;
        }
        const before = baselineUser.fieldValues;
        const after = workingUser.fieldValues;
        for (const key of new Set([...Object.keys(before), ...Object.keys(after)])) {
            if ((before[key] ?? '') !== (after[key] ?? '')) {
                keys.add(key);
            }
        }
        return keys;
    }, [workingUser, baselineUser]);

    const {
        state: conflictState,
        beginOwnSave,
        endOwnSave,
        clearConflict,
    } = useExternalChangeConflict({
        dirty: dirty || reorder.isPending,
        entityId: resolvedOwner.id,
        eventNames: isContentOwner
            ? ['content.hierarchyItem.updated', 'content.hierarchyItem.deleted', 'metadata.refreshed']
            : ['subject.updated', 'subject.deleted'],
        deletedEventNames: isContentOwner ? ['content.hierarchyItem.deleted'] : ['subject.deleted'],
        contextKeys: isContentOwner ? ['content.hierarchyItemId'] : ['subject.id'],
    });
    const [showStaleSaveDialog, setShowStaleSaveDialog] = useState(false);

    const providerSortable = useSortableList<InspectorContribution>({
        items: providerContributions,
        getLabel: (contribution) => contribution.providerType,
        onReorder: async (contributionIds) => {
            beginOwnSave();
            try {
                await reorder.mutateAsync(contributionIds);
                endOwnSave(true);
            } catch (reorderError) {
                endOwnSave(false);
                throw reorderError;
            }
        },
        onReorderSuccess: ({ item, position, total }) => {
            toast({
                title: 'Contribution order updated',
                description: `${item.providerType} moved to position ${position + 1} of ${total}.`,
                variant: 'success',
            });
        },
        onReorderError: (reorderError) => {
            toast({
                title: 'Failed to reorder contributions',
                description: getErrorMessage(reorderError),
                variant: 'error',
            });
        },
        disabled: !canManage || reorder.isPending || conflictState !== null,
    });

    const displayProviderContributions = useMemo(
        () =>
            providerSortable.hasOptimisticOrder
                ? providerSortable.items.map((contribution, ordinal) => ({ ...contribution, ordinal }))
                : providerSortable.items,
        [providerSortable.hasOptimisticOrder, providerSortable.items],
    );

    const effectiveContributions = useMemo(() => {
        if (workingUser && userContributionHasContent(workingUser)) {
            return [workingUser, ...displayProviderContributions];
        }
        return displayProviderContributions;
    }, [displayProviderContributions, workingUser]);

    const materializedValues = useMemo(() => (data ? scalarFieldValues(data.materialized) : {}), [data]);

    const effectiveFields = useMemo(
        () => mergeFields(effectiveContributions, { ...materializedValues, ...(workingUser?.fieldValues ?? {}) }),
        [effectiveContributions, materializedValues, workingUser],
    );

    const savedFields = useMemo(() => {
        const saved =
            baselineUser && userContributionHasContent(baselineUser)
                ? [baselineUser, ...displayProviderContributions]
                : displayProviderContributions;
        return mergeFields(saved, materializedValues);
    }, [displayProviderContributions, baselineUser, materializedValues]);

    const ownerDefinitions = useMemo(
        () =>
            isContentOwner
                ? resolveContentDefinitions(contentTypes, contentDetail.data)
                : resolveSubjectDefinitions(subjectTypes, subjectDetail.subject),
        [isContentOwner, contentTypes, contentDetail.data, subjectTypes, subjectDetail.subject],
    );

    const scalarFields = useMemo(() => buildScalarFields(ownerDefinitions), [ownerDefinitions]);
    const subItemContext = useMemo(() => buildSubItemContext(ownerDefinitions), [ownerDefinitions]);
    const subItemDefinitions = useMemo(
        () => buildSubItemDefinitions(ownerDefinitions, effectiveContributions),
        [ownerDefinitions, effectiveContributions],
    );

    const toggleExpanded = (id: string) => {
        setExpanded((prev) => {
            const next = new Set(prev);
            if (next.has(id)) {
                next.delete(id);
            } else {
                next.add(id);
            }
            return next;
        });
    };

    const mutateUser = (mutator: (draft: InspectorContribution) => void) => {
        setBufferUser((prev) => {
            const base = prev ?? (baselineUser ? cloneUserContribution(baselineUser) : null);
            if (!base) {
                return prev;
            }
            const draft = cloneUserContribution(base);
            mutator(draft);
            return draft;
        });
    };

    if (isLoading) {
        return (
            <div className="metadata-inspector" data-component="metadata-inspector">
                <Skeleton className="metadata-inspector__skeleton" />
                <Skeleton className="metadata-inspector__skeleton" />
                <Skeleton className="metadata-inspector__skeleton" />
            </div>
        );
    }

    if (error || !data || !workingUser) {
        return <EmptyState title="Metadata unavailable" />;
    }

    const onEdit = (field: InspectorFieldDefinition, current: MetadataInspectorField | undefined) => {
        setEditState({
            field: field.key,
            label: field.label,
            initialValue: current?.value ?? '',
        });
    };

    const onSaveOverride = (value: string) => {
        if (!editState) {
            return;
        }
        const fieldKey = editState.field;
        mutateUser((draft) => {
            const fieldValues = draft.fieldValues ?? {};
            if (value.trim() === '') {
                delete fieldValues[fieldKey];
            } else {
                fieldValues[fieldKey] = value;
            }
            draft.fieldValues = fieldValues;
        });
        setEditState(null);
    };

    const onSubmitSubItem = (payload: SubItemPayload) => {
        if (!subItemForm) {
            return;
        }
        const { kind, mode, itemId } = subItemForm;
        mutateUser((draft) => {
            const container = draft as unknown as Record<string, Record<string, unknown>[]>;
            const list = [...(container[kind] ?? [])];
            if (mode === 'add') {
                list.push(payloadToElement(kind, payload, nextBufferItemId()));
            } else if (itemId) {
                const index = list.findIndex((row) => (row.itemId as string) === itemId);
                if (index >= 0) {
                    const element = payloadToElement(kind, payload, itemId);
                    const relElement = element as unknown as BufferRelationship;
                    const relExisting = list[index] as unknown as BufferRelationship;
                    if (
                        kind === 'relationships' &&
                        relElement.subjectId &&
                        relElement.subjectId === relExisting.subjectId
                    ) {
                        relElement.name = relExisting.name ?? null;
                    }
                    list[index] = element;
                }
            }
            container[kind] = list;
        });
        setSubItemForm(null);
    };

    const onDeleteSubItem = (kind: MetadataSubItemKey, itemId: string) => {
        mutateUser((draft) => {
            const container = draft as unknown as Record<string, Record<string, unknown>[]>;
            container[kind] = (container[kind] ?? []).filter((row) => (row.itemId as string) !== itemId);
        });
    };

    const onClearUser = () => {
        setBufferUser(emptyUserContribution(workingUser.ordinal));
        toast({
            title: 'User layer cleared',
            description: 'Save to apply your changes. Revert to discard them.',
            variant: 'info',
        });
    };

    const saveChanges = () => {
        if (!baselineUser) {
            return;
        }
        beginOwnSave();
        patch.mutate(buildDeltaPatch(baselineUser, workingUser), {
            onSuccess: () => {
                endOwnSave(true);
                setShowStaleSaveDialog(false);
                toast({ title: 'Metadata saved', variant: 'success' });
            },
            onError: () => {
                endOwnSave(false);
                toast({
                    title: 'Save failed',
                    description: 'The host rejected the changes. Check the values and try again.',
                    variant: 'error',
                });
            },
        });
    };

    const onSave = () => {
        if (conflictState === 'deleted') {
            return;
        }
        if (conflictState === 'stale') {
            setShowStaleSaveDialog(true);
            return;
        }
        saveChanges();
    };

    const onRevert = () => {
        setBufferUser(baselineUser ? cloneUserContribution(baselineUser) : null);
        clearConflict();
        setShowStaleSaveDialog(false);
    };

    return (
        <div className="metadata-inspector" data-component="metadata-inspector">
            <EditToolbar
                changeCount={changeCount}
                canManage={canManage}
                pending={patch.isPending}
                conflictState={conflictState}
                onSave={onSave}
                onRevert={onRevert}
            />

            {conflictState && (
                <ExternalChangeConflictWarning
                    state={conflictState}
                    entityLabel="Metadata"
                    id="metadata-external-change-warning"
                />
            )}

            {data?.subjectsTruncated && (
                <Alert variant="warning" title="Some related names are not resolved">
                    Magpie shows the unresolved names as Unknown.
                </Alert>
            )}

            <ContributionStack
                contributions={effectiveContributions}
                canManage={canManage}
                sortable={providerSortable}
                expanded={expanded}
                scalarFields={scalarFields}
                subItemDefinitions={subItemDefinitions}
                subItemContext={subItemContext}
                onToggle={toggleExpanded}
                onClearUser={onClearUser}
            />

            <ResolvedValues
                fields={effectiveFields}
                savedFields={savedFields}
                pendingFields={pendingFields}
                fieldDefinitions={scalarFields}
                canManage={canManage}
                onEdit={onEdit}
            />

            <EditableSubsections
                contributions={effectiveContributions}
                subItemDefinitions={subItemDefinitions}
                subItemContext={subItemContext}
                canManage={canManage}
                onAdd={(kind, label) => setSubItemForm({ kind, label, mode: 'add' })}
                onEdit={(kind, label, row) =>
                    setSubItemForm({
                        kind,
                        label,
                        mode: 'edit',
                        itemId: row.itemId,
                        initialValues: row.editValues,
                        initialSubject: row.subject ?? null,
                    })
                }
                onDelete={(kind, _label, row) => onDeleteSubItem(kind, row.itemId)}
            />

            {editState && (
                <OverrideDialog state={editState} onCancel={() => setEditState(null)} onSave={onSaveOverride} />
            )}

            {subItemForm && (
                <SubItemFormDialog
                    kind={subItemForm.kind}
                    mode={subItemForm.mode}
                    initialValues={subItemForm.initialValues}
                    initialSubject={subItemForm.initialSubject}
                    pending={false}
                    onCancel={() => setSubItemForm(null)}
                    onSubmit={onSubmitSubItem}
                />
            )}

            <StaleSaveConfirmationDialog
                open={showStaleSaveDialog}
                title="Save metadata changed elsewhere?"
                description="This metadata changed after editing began. Saving anyway will apply your draft over the newer data."
                saving={patch.isPending}
                onCancel={() => setShowStaleSaveDialog(false)}
                onConfirm={saveChanges}
            />
        </div>
    );
}

interface EditToolbarProps {
    changeCount: number;
    canManage: boolean;
    pending: boolean;
    conflictState: 'stale' | 'deleted' | null;
    onSave: () => void;
    onRevert: () => void;
}

function EditToolbar({ changeCount, canManage, pending, conflictState, onSave, onRevert }: EditToolbarProps) {
    const dirty = changeCount > 0;
    return (
        <div className="metadata-inspector__toolbar" data-component="metadata-inspector-toolbar" data-dirty={dirty}>
            <div className="metadata-inspector__toolbar-status">
                {dirty ? (
                    <Badge variant="warning" size="sm" dot data-testid="unsaved-changes">
                        {changeCount} unsaved {changeCount === 1 ? 'change' : 'changes'}
                    </Badge>
                ) : (
                    <span className="metadata-inspector__toolbar-hint" data-testid="no-changes">
                        No unsaved changes
                    </span>
                )}
            </div>
            <div className="metadata-inspector__toolbar-actions">
                <Button
                    variant="ghost"
                    size="sm"
                    onClick={onRevert}
                    disabled={!dirty || pending}
                    data-action="revert-inspector"
                >
                    Revert
                </Button>
                <Button
                    variant="primary"
                    size="sm"
                    onClick={onSave}
                    disabled={!dirty || !canManage || pending || conflictState === 'deleted'}
                    loading={pending}
                    aria-describedby={conflictState ? 'metadata-external-change-warning' : undefined}
                    data-action="save-inspector"
                >
                    Save changes
                </Button>
            </div>
        </div>
    );
}

interface ContributionStackProps {
    contributions: InspectorContribution[];
    canManage: boolean;
    sortable: ReturnType<typeof useSortableList<InspectorContribution>>;
    expanded: Set<string>;
    scalarFields: InspectorFieldDefinition[];
    subItemDefinitions: SubItemDefinition[];
    subItemContext: SubItemRenderContext;
    onToggle: (id: string) => void;
    onClearUser: () => void;
}

function ContributionStack({
    contributions,
    canManage,
    sortable,
    expanded,
    scalarFields,
    subItemDefinitions,
    subItemContext,
    onToggle,
    onClearUser,
}: ContributionStackProps) {
    const ordered = useMemo(
        () => [
            ...contributions.filter((contribution) => contribution.isUserContribution),
            ...contributions
                .filter((contribution) => !contribution.isUserContribution)
                .sort((left, right) => left.ordinal - right.ordinal || left.id.localeCompare(right.id)),
        ],
        [contributions],
    );
    const providerPositions = useMemo(
        () =>
            new Map(
                ordered
                    .filter((contribution) => !contribution.isUserContribution)
                    .map((contribution, index) => [contribution.id, index]),
            ),
        [ordered],
    );

    return (
        <section className="metadata-inspector__stack" aria-label="Contribution stack">
            <h3 className="metadata-inspector__stack-title">Contribution stack</h3>
            {ordered.length === 0 ? (
                <p className="metadata-inspector__empty">No contributions yet.</p>
            ) : (
                <>
                    <ol className="metadata-inspector__stack-list">
                        {ordered.map((contribution) => {
                            const fieldValues = contribution.fieldValues ?? {};
                            const fieldRows = scalarFields
                                .filter((field) => fieldValues[field.key] !== undefined)
                                .map((field) => ({ field, value: fieldValues[field.key] }));
                            const subItemGroups = subItemDefinitions
                                .map((kind) => ({
                                    kind,
                                    lines: kind.lines(contribution, subItemContext),
                                }))
                                .filter((g) => g.lines.length > 0);
                            const isExpanded = expanded.has(contribution.id);
                            const isExpandable = fieldRows.length > 0 || subItemGroups.length > 0;
                            const providerPosition = providerPositions.get(contribution.id);
                            const isProvider = providerPosition !== undefined;
                            const isDropTarget = sortable.dropTargetId === contribution.id;
                            const itemClasses = [
                                'metadata-inspector__stack-item',
                                isProvider &&
                                    sortableListItemClassNames(
                                        sortable.draggedId === contribution.id,
                                        isDropTarget,
                                        isDropTarget ? sortable.dropPosition : null,
                                    ),
                            ]
                                .filter(Boolean)
                                .join(' ');
                            return (
                                <li
                                    key={contribution.id}
                                    className={itemClasses}
                                    data-provider-type={contribution.providerType}
                                    data-user-contribution={contribution.isUserContribution || undefined}
                                    data-sortable-list-item-id={isProvider ? contribution.id : undefined}
                                    data-dragging={sortable.draggedId === contribution.id || undefined}
                                    data-expanded={isExpanded || undefined}
                                    onDragOver={
                                        isProvider
                                            ? (event) => sortable.handleDragOver(contribution.id, event)
                                            : undefined
                                    }
                                    onDragLeave={isProvider ? sortable.handleDragLeave : undefined}
                                    onDrop={
                                        isProvider ? (event) => sortable.handleDrop(contribution.id, event) : undefined
                                    }
                                >
                                    <div className="metadata-inspector__stack-row">
                                        {isProvider && canManage && (
                                            <SortableListControls
                                                label={`${contribution.providerType} contribution`}
                                                position={providerPosition}
                                                itemCount={providerPositions.size}
                                                moving={sortable.movingId === contribution.id}
                                                disabled={sortable.disabled || sortable.isMoving}
                                                focusDirection={
                                                    sortable.keyboardFocusTarget?.id === contribution.id
                                                        ? sortable.keyboardFocusTarget.direction
                                                        : null
                                                }
                                                dragHandleClassName="metadata-inspector__stack-drag-handle"
                                                orderControlsClassName="metadata-inspector__stack-order-controls"
                                                orderButtonClassName="metadata-inspector__stack-order-button"
                                                dragHandleTestId="metadata-inspector__stack-drag-handle"
                                                actionName="contribution"
                                                onDragStart={(event) => sortable.handleDragStart(contribution, event)}
                                                onDragEnd={sortable.handleDragEnd}
                                                onTouchStart={(event) => sortable.handleTouchStart(contribution, event)}
                                                onTouchMove={sortable.handleTouchMove}
                                                onTouchEnd={sortable.handleTouchEnd}
                                                onTouchCancel={sortable.handleTouchCancel}
                                                onMove={(direction) => sortable.handleMove(contribution, direction)}
                                                onFocusRestored={sortable.handleFocusRestored}
                                            />
                                        )}
                                        <button
                                            type="button"
                                            className="metadata-inspector__stack-toggle"
                                            onClick={() => onToggle(contribution.id)}
                                            disabled={!isExpandable}
                                            aria-expanded={isExpanded}
                                            aria-label={
                                                isExpanded
                                                    ? `Collapse ${contribution.providerType} contribution`
                                                    : `Expand ${contribution.providerType} contribution`
                                            }
                                            data-action="toggle-contribution"
                                        >
                                            {isExpandable ? (
                                                isExpanded ? (
                                                    <CaretDownIcon size={14} weight="bold" aria-hidden="true" />
                                                ) : (
                                                    <CaretRightIcon size={14} weight="bold" aria-hidden="true" />
                                                )
                                            ) : (
                                                <span className="metadata-inspector__stack-toggle-spacer" />
                                            )}
                                        </button>
                                        {!contribution.isUserContribution && (
                                            <span className="metadata-inspector__stack-ordinal">
                                                #{contribution.ordinal}
                                            </span>
                                        )}
                                        <span className="metadata-inspector__stack-provider">
                                            {contribution.providerType}
                                        </span>
                                        {contribution.isUserContribution && (
                                            <Badge variant="primary" size="sm">
                                                user
                                            </Badge>
                                        )}
                                        {contribution.source && (
                                            <span className="metadata-inspector__stack-source">
                                                {contribution.source}
                                            </span>
                                        )}
                                        {contribution.isUserContribution && (
                                            <GatedButton
                                                canManage={canManage}
                                                variant="ghost"
                                                size="sm"
                                                className="metadata-inspector__stack-delete"
                                                aria-label="Clear user contribution"
                                                data-action="clear-user-contribution"
                                                onClick={onClearUser}
                                            >
                                                <TrashIcon weight="bold" aria-hidden="true" />
                                            </GatedButton>
                                        )}
                                    </div>
                                    {isExpanded && isExpandable && (
                                        <div className="metadata-inspector__stack-detail">
                                            {fieldRows.length > 0 && (
                                                <dl
                                                    className="metadata-inspector__stack-fields"
                                                    data-testid="contribution-fields"
                                                >
                                                    {fieldRows.map(({ field, value }) => (
                                                        <div
                                                            key={field.key}
                                                            className="metadata-inspector__stack-field"
                                                            data-field={field.key}
                                                        >
                                                            <dt className="metadata-inspector__stack-field-label">
                                                                {field.label}
                                                            </dt>
                                                            <dd className="metadata-inspector__stack-field-value">
                                                                {formatFieldValue(field, value)}
                                                            </dd>
                                                        </div>
                                                    ))}
                                                </dl>
                                            )}
                                            {subItemGroups.map(({ kind, lines }) => (
                                                <div
                                                    key={kind.key}
                                                    className="metadata-inspector__stack-group"
                                                    data-subitem={kind.key}
                                                >
                                                    <span className="metadata-inspector__stack-group-label">
                                                        {kind.label}
                                                    </span>
                                                    <ul className="metadata-inspector__stack-group-list">
                                                        {lines.map((line, idx) => (
                                                            <li key={idx}>{line}</li>
                                                        ))}
                                                    </ul>
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </li>
                            );
                        })}
                    </ol>
                    <div ref={sortable.dragImageRef} className="sortable-list__drag-image" aria-hidden="true" />
                </>
            )}
        </section>
    );
}

interface ResolvedValuesProps {
    fields: Record<string, MetadataInspectorField>;
    savedFields: Record<string, MetadataInspectorField>;
    pendingFields: Set<string>;
    fieldDefinitions: InspectorFieldDefinition[];
    canManage: boolean;
    onEdit: (field: InspectorFieldDefinition, current: MetadataInspectorField | undefined) => void;
}

function ResolvedValues({
    fields,
    savedFields,
    pendingFields,
    fieldDefinitions,
    canManage,
    onEdit,
}: ResolvedValuesProps) {
    const editedCount = fieldDefinitions.filter((field) => pendingFields.has(field.key)).length;

    return (
        <section className="metadata-inspector__subsection" aria-label="Resolved values">
            <header className="metadata-inspector__subsection-header">
                <h3 className="metadata-inspector__subsection-label">Properties</h3>
                {editedCount > 0 && (
                    <span className="metadata-inspector__properties-count" data-testid="edited-field-count">
                        {editedCount} edited
                    </span>
                )}
            </header>
            <table className="metadata-inspector__properties-table">
                <thead>
                    <tr>
                        <th scope="col">Name</th>
                        <th scope="col">Value</th>
                        <th scope="col">Source</th>
                        <th scope="col">
                            <span className="sr-only">Actions</span>
                        </th>
                    </tr>
                </thead>
                <tbody>
                    {fieldDefinitions.map((field) => {
                        const entry = fields[field.key];
                        const saved = savedFields[field.key];
                        const pending = pendingFields.has(field.key);
                        return (
                            <tr key={field.key} data-field={field.key} data-pending={pending || undefined}>
                                <th scope="row" className="metadata-inspector__row-name">
                                    {field.label}
                                </th>
                                <td>
                                    <div className="metadata-inspector__row-value">
                                        {entry?.value ? (
                                            formatFieldValue(field, entry.value)
                                        ) : (
                                            <span className="metadata-inspector__empty">No value</span>
                                        )}
                                        {pending &&
                                            (saved?.value ? (
                                                <s className="metadata-inspector__row-previous">
                                                    {formatFieldValue(field, saved.value)}
                                                </s>
                                            ) : (
                                                <span className="metadata-inspector__empty">no value</span>
                                            ))}
                                    </div>
                                </td>
                                <td>
                                    <div className="metadata-inspector__row-source">
                                        {saved?.providerType ? (
                                            <Badge variant="info" size="sm">
                                                {saved.providerType}
                                            </Badge>
                                        ) : (
                                            <span className="metadata-inspector__row-nosource">&mdash;</span>
                                        )}
                                        {pending && (
                                            <Badge variant="warning" size="sm">
                                                unsaved
                                            </Badge>
                                        )}
                                    </div>
                                </td>
                                <td className="metadata-inspector__row-actions">
                                    {field.editable && (
                                        <GatedButton
                                            canManage={canManage}
                                            variant="ghost"
                                            size="sm"
                                            aria-label={`Edit ${field.label}`}
                                            data-action="override-field"
                                            onClick={() => onEdit(field, entry)}
                                        >
                                            <PencilSimpleIcon size={16} weight="bold" aria-hidden="true" />
                                        </GatedButton>
                                    )}
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </section>
    );
}

interface EditableSubsectionsProps {
    contributions: InspectorContribution[];
    subItemDefinitions: SubItemDefinition[];
    subItemContext: SubItemRenderContext;
    canManage: boolean;
    onAdd: (kind: MetadataSubItemKey, label: string) => void;
    onEdit: (kind: MetadataSubItemKey, label: string, row: SubItemRow) => void;
    onDelete: (kind: MetadataSubItemKey, label: string, row: SubItemRow) => void;
}

interface FlatSubItemRow extends SubItemRow {
    providerType: string;
    isUserContribution: boolean;
}

function EditableSubsections({
    contributions,
    subItemDefinitions,
    subItemContext,
    canManage,
    onAdd,
    onEdit,
    onDelete,
}: EditableSubsectionsProps) {
    const ordered = useMemo(() => [...contributions].sort((a, b) => a.ordinal - b.ordinal), [contributions]);

    return (
        <>
            {subItemDefinitions.map((kind) => {
                const rows: FlatSubItemRow[] = ordered.flatMap((c) =>
                    kind.rows(c, subItemContext).map((row) => ({
                        ...row,
                        providerType: c.providerType,
                        isUserContribution: c.isUserContribution,
                    })),
                );
                return (
                    <section
                        key={kind.key}
                        className="metadata-inspector__subsection"
                        aria-label={kind.label}
                        data-subsection={kind.key}
                    >
                        <header className="metadata-inspector__subsection-header">
                            <h3 className="metadata-inspector__subsection-label">{kind.label}</h3>
                            <GatedButton
                                canManage={canManage}
                                variant="ghost"
                                size="sm"
                                aria-label={`Add ${kind.label.toLowerCase()}`}
                                data-action="add-subitem"
                                onClick={() => onAdd(kind.key, kind.label)}
                            >
                                <PlusIcon weight="bold" aria-hidden="true" />
                                Add
                            </GatedButton>
                        </header>
                        {rows.length === 0 ? (
                            <p className="metadata-inspector__empty">No {kind.label.toLowerCase()}.</p>
                        ) : (
                            <ul className="metadata-inspector__subsection-list">
                                {rows.map((row) => (
                                    <li
                                        key={`${row.providerType}-${row.itemId}`}
                                        className="metadata-inspector__subsection-row"
                                        data-subitem-row
                                        data-user-row={row.isUserContribution || undefined}
                                    >
                                        <span className="metadata-inspector__subsection-row-text">{row.text}</span>
                                        <Badge variant="default" size="sm">
                                            {row.providerType}
                                        </Badge>
                                        {row.isUserContribution && (
                                            <span className="metadata-inspector__subsection-row-actions">
                                                <GatedButton
                                                    canManage={canManage}
                                                    variant="ghost"
                                                    size="sm"
                                                    aria-label={`Edit ${kind.label.toLowerCase()}`}
                                                    data-action="edit-subitem"
                                                    onClick={() => onEdit(kind.key, kind.label, row)}
                                                >
                                                    <PencilSimpleIcon size={16} weight="bold" aria-hidden="true" />
                                                </GatedButton>
                                                <GatedButton
                                                    canManage={canManage}
                                                    variant="ghost"
                                                    size="sm"
                                                    aria-label={`Delete ${kind.label.toLowerCase()}`}
                                                    data-action="delete-subitem"
                                                    onClick={() => onDelete(kind.key, kind.label, row)}
                                                >
                                                    <TrashIcon weight="bold" aria-hidden="true" />
                                                </GatedButton>
                                            </span>
                                        )}
                                    </li>
                                ))}
                            </ul>
                        )}
                    </section>
                );
            })}
        </>
    );
}

interface GatedButtonProps {
    canManage: boolean;
    variant: 'ghost' | 'primary';
    size: 'sm' | 'md';
    'aria-label': string;
    'data-action': string;
    className?: string;
    onClick: () => void;
    children: ReactNode;
}

function GatedButton({ canManage, children, onClick, ...rest }: GatedButtonProps) {
    if (canManage) {
        return (
            <Button {...rest} onClick={onClick}>
                {children}
            </Button>
        );
    }
    return (
        <TooltipProvider>
            <Tooltip content={MANAGER_REQUIRED_MESSAGE}>
                <span style={{ display: 'inline-block' }}>
                    <Button {...rest} disabled aria-disabled>
                        {children}
                    </Button>
                </span>
            </Tooltip>
        </TooltipProvider>
    );
}

interface OverrideDialogProps {
    state: EditState;
    onCancel: () => void;
    onSave: (value: string) => void;
}

function OverrideDialog({ state, onCancel, onSave }: OverrideDialogProps) {
    const [value, setValue] = useState(state.initialValue);

    return (
        <Dialog open onOpenChange={(open) => (!open ? onCancel() : undefined)}>
            <DialogContent>
                <DialogHeader>
                    <DialogTitle>Override {state.label}</DialogTitle>
                    <DialogDescription>
                        Adds a manual value that takes priority over provider metadata. Save to apply it.
                    </DialogDescription>
                </DialogHeader>
                <div className="metadata-inspector__dialog-body">
                    <Label htmlFor="metadata-inspector-value">Value</Label>
                    <Input
                        id="metadata-inspector-value"
                        value={value}
                        onChange={(event) => setValue(event.target.value)}
                        autoFocus
                    />
                </div>
                <DialogFooter>
                    <Button variant="ghost" onClick={onCancel}>
                        Cancel
                    </Button>
                    <Button variant="primary" onClick={() => onSave(value)}>
                        Apply
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}
