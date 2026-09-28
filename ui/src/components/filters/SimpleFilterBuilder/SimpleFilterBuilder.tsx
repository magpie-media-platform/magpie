import { useCallback, useEffect, useMemo, useState, type ComponentPropsWithoutRef } from 'react';

import {
    useLibraryTypes,
    type LibraryMetadataFieldDefinition,
    type LibraryTypeDefinition,
} from '@/hooks/useLibraryTypes';
import {
    buildQuery,
    EMPTY_FIELDS,
    normalizeYearOps,
    parseQuery,
    YEAR_OPS,
    type FieldState,
    type YearOp,
} from '@/lib/simpleFilterQuery';
import {
    buildContentFilterOptions,
    contentFilterSelectionValue,
    parseContentFilterSelectionValue,
} from '@/lib/contentFilterOptions';
import { readContentFilterSelection } from '@/lib/contentColumnFilters';

import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';

import './SimpleFilterBuilder.css';

export interface SimpleFilterBuilderProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    value: string;

    onChange: (value: string) => void;

    context?: 'content' | 'subject';

    readOnly?: boolean;

    loading?: boolean;

    defaultContentType?: string;

    restrictContentType?: string;

    lockContentLevel?: string;
    includeAllContentLevels?: boolean;

    defaultSubjectKind?: string;

    lockSubjectKind?: boolean;
}

const MIN_YEAR = 1870;
const MAX_YEAR_OFFSET = 5;

const ANY_VALUE = '__any__';

type SimpleMetadataField = 'Title' | 'PublishDate' | 'Genres' | 'Relationships';

interface QueryDraft {
    query: string;
    sourceQuery: string;
    applied?: boolean;
}

interface NeutralYearDraft extends QueryDraft {
    releaseYear: string;
    yearOps: YearOp[];
}

interface EditDraft extends QueryDraft {
    fields: FieldState;
}

function sameQuery(a: string | null | undefined, b: string | null | undefined): boolean {
    return (a ?? '').trim() === (b ?? '').trim();
}

function draftMatches(draft: QueryDraft | null, value: string): boolean {
    return draft !== null && (sameQuery(draft.query, value) || (!draft.applied && sameQuery(draft.sourceQuery, value)));
}

function isNewlyApplied<T extends QueryDraft>(draft: T | null, value: string): draft is T {
    return (
        draft !== null && !draft.applied && !sameQuery(draft.query, draft.sourceQuery) && sameQuery(draft.query, value)
    );
}

const SIMPLE_FIELD_KEYS: Record<SimpleMetadataField, string[]> = {
    Title: ['title'],
    PublishDate: ['publishDate'],
    Genres: ['genre', 'genres'],
    Relationships: ['relationship', 'relationships'],
};

function hasFieldKey(fields: LibraryMetadataFieldDefinition[] | undefined, keys: string[]): boolean {
    const wanted = new Set(keys.map((key) => key.toLowerCase()));
    return (fields ?? []).some((field) => wanted.has(field.key.toLowerCase()));
}

function hasDefinitions(type: LibraryTypeDefinition): boolean {
    return (
        type.standardMetadataFields !== undefined ||
        type.customMetadataFields !== undefined ||
        type.levels.some(
            (level) =>
                level.standardMetadataFields !== undefined ||
                level.standardFactFields !== undefined ||
                level.customMetadataFields !== undefined,
        )
    );
}

function typeSupportsField(type: LibraryTypeDefinition, field: SimpleMetadataField): boolean {
    if (!hasDefinitions(type)) {
        return true;
    }
    const keys = SIMPLE_FIELD_KEYS[field];
    if (
        (type.standardMetadataFields ?? []).some(
            (definition) => definition.field === field || hasFieldKey([definition], keys),
        )
    ) {
        return true;
    }
    if (hasFieldKey(type.customMetadataFields, keys)) {
        return true;
    }
    return type.levels.some(
        (level) =>
            (level.standardMetadataFields ?? []).some(
                (definition) => definition.field === field || hasFieldKey([definition], keys),
            ) ||
            hasFieldKey(level.standardFactFields, keys) ||
            hasFieldKey(level.customMetadataFields, keys),
    );
}

function registrySupportsField(types: LibraryTypeDefinition[], slug: string, field: SimpleMetadataField): boolean {
    if (slug) {
        const type = types.find((t) => t.slug === slug);
        return type ? typeSupportsField(type, field) : true;
    }
    if (types.length === 0) {
        return true;
    }
    return types.some((type) => typeSupportsField(type, field));
}

export function SimpleFilterBuilder({
    value,
    onChange,
    context = 'content',
    readOnly = false,
    loading = false,
    defaultContentType,
    restrictContentType,
    lockContentLevel,
    includeAllContentLevels = false,
    defaultSubjectKind,
    lockSubjectKind = false,
    className,
    children,
    ...props
}: SimpleFilterBuilderProps) {
    const { contentTypes, subjectTypes, isLoading: registryLoading } = useLibraryTypes();

    const parsedFields = useMemo(() => parseQuery(value, context) ?? { ...EMPTY_FIELDS }, [value, context]);
    const [editDraft, setEditDraft] = useState<EditDraft | null>(null);
    const matchedEditDraft = draftMatches(editDraft, value) ? editDraft : null;
    const currentFields = matchedEditDraft?.fields ?? parsedFields;
    const [neutralYearDraft, setNeutralYearDraft] = useState<NeutralYearDraft | null>(null);
    const draftMatchesValue = context === 'content' && draftMatches(neutralYearDraft, value);
    const fields = useMemo(() => {
        if (!draftMatchesValue || neutralYearDraft === null) {
            return currentFields;
        }
        return {
            ...currentFields,
            releaseYear: neutralYearDraft.releaseYear,
            yearOps: neutralYearDraft.yearOps,
        };
    }, [draftMatchesValue, neutralYearDraft, currentFields]);

    useEffect(() => {
        if (neutralYearDraft && !draftMatchesValue) {
            setNeutralYearDraft(null);
        } else if (isNewlyApplied(neutralYearDraft, value)) {
            setNeutralYearDraft({ ...neutralYearDraft, applied: true });
        }
    }, [neutralYearDraft, draftMatchesValue, value]);

    useEffect(() => {
        if (editDraft && matchedEditDraft === null) {
            setEditDraft(null);
        } else if (isNewlyApplied(editDraft, value)) {
            setEditDraft({ ...editDraft, applied: true });
        }
    }, [editDraft, matchedEditDraft, value]);

    const maxYear = useMemo(() => new Date().getFullYear() + MAX_YEAR_OFFSET, []);

    const contentLevelLocked = context === 'content' && Boolean(restrictContentType) && Boolean(lockContentLevel);
    const contentFilterOptions = useMemo(
        () => buildContentFilterOptions(contentTypes, restrictContentType, includeAllContentLevels),
        [contentTypes, restrictContentType, includeAllContentLevels],
    );
    const queryContentSelection = useMemo(
        () => (context === 'content' ? readContentFilterSelection(value) : null),
        [context, value],
    );
    const scopedDefaultContentType = restrictContentType ?? defaultContentType;
    const effectiveContentSelection = useMemo(() => {
        if (contentLevelLocked) {
            return { contentType: restrictContentType!, level: lockContentLevel! };
        }
        return queryContentSelection ?? (scopedDefaultContentType ? { contentType: scopedDefaultContentType } : null);
    }, [contentLevelLocked, lockContentLevel, queryContentSelection, restrictContentType, scopedDefaultContentType]);
    const effectiveContentFilterValue = effectiveContentSelection
        ? contentFilterSelectionValue(effectiveContentSelection)
        : ANY_VALUE;
    const effectiveContentType = effectiveContentSelection?.contentType ?? '';
    const subjectKindLocked = context === 'subject' && lockSubjectKind && Boolean(defaultSubjectKind);
    const effectiveSubjectKind = subjectKindLocked ? defaultSubjectKind || '' : fields.kind || defaultSubjectKind || '';
    const contentSupports = useCallback(
        (field: SimpleMetadataField) => registrySupportsField(contentTypes, effectiveContentType, field),
        [contentTypes, effectiveContentType],
    );
    const subjectSupports = useCallback(
        (field: SimpleMetadataField) => registrySupportsField(subjectTypes, effectiveSubjectKind, field),
        [subjectTypes, effectiveSubjectKind],
    );

    const contentTypeLabel = useMemo(() => {
        if (!effectiveContentSelection) {
            return 'Any';
        }
        return (
            contentFilterOptions.find((option) => option.value === effectiveContentFilterValue)?.label ??
            contentTypes.find((type) => type.slug === effectiveContentType)?.name ??
            effectiveContentType
        );
    }, [
        contentFilterOptions,
        contentTypes,
        effectiveContentFilterValue,
        effectiveContentSelection,
        effectiveContentType,
    ]);

    const kindLabel = useMemo(() => {
        if (!effectiveSubjectKind) {
            return 'Any';
        }
        return subjectTypes.find((t) => t.slug === effectiveSubjectKind)?.name ?? effectiveSubjectKind;
    }, [subjectTypes, effectiveSubjectKind]);

    const relationshipKindLabel = useMemo(() => {
        if (!fields.relationshipKind) {
            return 'Any';
        }
        return subjectTypes.find((t) => t.slug === fields.relationshipKind)?.name ?? fields.relationshipKind;
    }, [subjectTypes, fields.relationshipKind]);

    const emitFields = useCallback(
        (updated: FieldState) => {
            const query = buildQuery(updated, context);
            const normalizedYearOps = normalizeYearOps(updated.yearOps);
            if (context === 'content' && updated.releaseYear.trim() && normalizedYearOps.length === YEAR_OPS.length) {
                setNeutralYearDraft({
                    query,
                    sourceQuery: value,
                    releaseYear: updated.releaseYear,
                    yearOps: normalizedYearOps,
                });
            } else {
                setNeutralYearDraft(null);
            }
            setEditDraft({ query, sourceQuery: value, fields: parseQuery(query, context) ?? { ...EMPTY_FIELDS } });
            onChange(query);
        },
        [context, onChange, value],
    );

    const updateField = useCallback(
        (field: keyof FieldState, fieldValue: string) => {
            if (field === 'kind' && subjectKindLocked) {
                return;
            }
            const normalizedValue = fieldValue === ANY_VALUE ? '' : fieldValue;
            const editableFields = subjectKindLocked ? { ...fields, kind: '' } : fields;
            const updated = { ...editableFields, [field]: normalizedValue };
            emitFields(updated);
        },
        [emitFields, fields, subjectKindLocked],
    );

    const updateContentFilter = useCallback(
        (optionValue: string) => {
            if (contentLevelLocked) {
                return;
            }
            const selection = optionValue === ANY_VALUE ? null : parseContentFilterSelectionValue(optionValue);
            emitFields({
                ...fields,
                contentType: selection?.contentType ?? '',
                level: selection?.level ?? '',
            });
        },
        [contentLevelLocked, emitFields, fields],
    );

    const updateReleaseYear = useCallback(
        (raw: string) => {
            const digits = raw.replace(/\D/g, '').slice(0, 4);
            const updated = { ...fields, releaseYear: digits };
            emitFields(updated);
        },
        [emitFields, fields],
    );

    const toggleYearOp = useCallback(
        (op: YearOp) => {
            const next = fields.yearOps.includes(op)
                ? fields.yearOps.filter((o) => o !== op)
                : normalizeYearOps([...fields.yearOps, op]);
            const updated = { ...fields, yearOps: next };
            emitFields(updated);
        },
        [emitFields, fields],
    );

    const disabled = readOnly || loading;
    const showContentTitle = contentSupports('Title');
    const showSubjectTitle = subjectSupports('Title');
    const showReleaseYear = contentSupports('PublishDate');
    const showGenre = contentSupports('Genres');
    const showRelationships = subjectTypes.length > 0 && contentSupports('Relationships');

    const classes = [
        'simple-filter-builder',
        `simple-filter-builder--${context}`,
        readOnly && 'simple-filter-builder--readonly',
        className,
    ]
        .filter(Boolean)
        .join(' ');

    const titleField = (
        <div className="simple-filter-builder__field">
            <Label htmlFor="sfb-title">Title</Label>
            <Input
                id="sfb-title"
                size="sm"
                value={fields.title}
                onChange={(e) => updateField('title', e.target.value)}
                disabled={disabled}
                placeholder="Contains..."
            />
        </div>
    );

    const tagField = (
        <div className="simple-filter-builder__field">
            <Label htmlFor="sfb-tag">Tag</Label>
            <Input
                id="sfb-tag"
                size="sm"
                value={fields.tag}
                onChange={(e) => updateField('tag', e.target.value)}
                disabled={disabled}
                placeholder="favorite"
            />
        </div>
    );

    return (
        <div
            className={classes}
            data-component="simple-filter-builder"
            data-readonly={readOnly || undefined}
            {...props}
        >
            {context === 'content' && (
                <>
                    <div className="simple-filter-builder__field">
                        <Label htmlFor="sfb-content-type">Content Type</Label>
                        <Select
                            value={effectiveContentFilterValue}
                            onValueChange={updateContentFilter}
                            disabled={disabled || registryLoading || contentLevelLocked}
                        >
                            <SelectTrigger id="sfb-content-type" size="sm" placeholder="Any">
                                {contentTypeLabel}
                            </SelectTrigger>
                            <SelectContent>
                                {!restrictContentType && <SelectItem value={ANY_VALUE}>Any</SelectItem>}
                                {contentFilterOptions.map((option) => (
                                    <SelectItem key={option.value} value={option.value}>
                                        {option.label}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    </div>

                    {showContentTitle && titleField}

                    {showReleaseYear && (
                        <div className="simple-filter-builder__field simple-filter-builder__field--release-year">
                            <Label htmlFor="sfb-release-year">Release year</Label>
                            <div className="simple-filter-builder__year">
                                <Input
                                    id="sfb-release-year"
                                    className="simple-filter-builder__year-input"
                                    type="number"
                                    inputMode="numeric"
                                    size="sm"
                                    min={MIN_YEAR}
                                    max={maxYear}
                                    value={fields.releaseYear}
                                    onChange={(e) => updateReleaseYear(e.target.value)}
                                    disabled={disabled}
                                    placeholder="1999"
                                />
                                <div
                                    className="simple-filter-builder__year-ops"
                                    role="group"
                                    aria-label="Release year comparison"
                                >
                                    {YEAR_OPS.map((op) => {
                                        const isOn = fields.yearOps.includes(op);
                                        return (
                                            <button
                                                key={op}
                                                type="button"
                                                id={`sfb-release-year-${op}`}
                                                className="simple-filter-builder__year-op"
                                                role="checkbox"
                                                aria-checked={isOn}
                                                aria-label={op}
                                                data-state={isOn ? 'on' : 'off'}
                                                onClick={() => toggleYearOp(op)}
                                                disabled={disabled}
                                            >
                                                {op}
                                            </button>
                                        );
                                    })}
                                </div>
                            </div>
                        </div>
                    )}

                    {showRelationships && (
                        <div className="simple-filter-builder__field">
                            <Label htmlFor="sfb-relationship-name">Related</Label>
                            <div className="simple-filter-builder__subject simple-filter-builder__subject--year-ratio">
                                <Input
                                    id="sfb-relationship-name"
                                    size="sm"
                                    value={fields.relationshipName}
                                    onChange={(e) => updateField('relationshipName', e.target.value)}
                                    disabled={disabled}
                                    placeholder="Related name"
                                />
                                <Select
                                    value={fields.relationshipKind || ANY_VALUE}
                                    onValueChange={(v) => updateField('relationshipKind', v)}
                                    disabled={disabled || registryLoading}
                                >
                                    <SelectTrigger
                                        id="sfb-relationship-kind"
                                        size="sm"
                                        placeholder="Any"
                                        aria-label="Related kind"
                                    >
                                        <span className="simple-filter-builder__subject-kind">
                                            {relationshipKindLabel}
                                        </span>
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value={ANY_VALUE}>Any</SelectItem>
                                        {subjectTypes.map((k) => (
                                            <SelectItem key={k.slug} value={k.slug}>
                                                {k.name}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            </div>
                        </div>
                    )}

                    {showGenre && (
                        <div className="simple-filter-builder__field">
                            <Label htmlFor="sfb-genre">Genre</Label>
                            <Input
                                id="sfb-genre"
                                size="sm"
                                value={fields.genre}
                                onChange={(e) => updateField('genre', e.target.value)}
                                disabled={disabled}
                                placeholder="Action"
                            />
                        </div>
                    )}

                    {tagField}
                </>
            )}

            {context === 'subject' && (
                <>
                    <div className="simple-filter-builder__field">
                        <Label htmlFor="sfb-subject-name">Related</Label>
                        <div className="simple-filter-builder__subject">
                            <Select
                                value={effectiveSubjectKind || ANY_VALUE}
                                onValueChange={(v) => updateField('kind', v)}
                                disabled={disabled || registryLoading || subjectKindLocked}
                            >
                                <SelectTrigger id="sfb-kind" size="sm" placeholder="Any" aria-label="Kind">
                                    <span className="simple-filter-builder__subject-kind">{kindLabel}</span>
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={ANY_VALUE}>Any</SelectItem>
                                    {subjectTypes.map((k) => (
                                        <SelectItem key={k.slug} value={k.slug}>
                                            {k.name}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                            {showSubjectTitle && (
                                <Input
                                    id="sfb-subject-name"
                                    size="sm"
                                    value={fields.title}
                                    onChange={(e) => updateField('title', e.target.value)}
                                    disabled={disabled}
                                    placeholder="Name contains..."
                                />
                            )}
                        </div>
                    </div>

                    {tagField}
                </>
            )}

            {children}
        </div>
    );
}

SimpleFilterBuilder.displayName = 'SimpleFilterBuilder';

export default SimpleFilterBuilder;
