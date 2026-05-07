/* eslint-disable react-refresh/only-export-components */
import { useCallback, useMemo, type ComponentPropsWithoutRef } from 'react';

import { Input } from '../Input';
import { Label } from '../Label';
import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';

import './SimpleFilterBuilder.css';

export interface SimpleFilterBuilderProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    value: string;

    onChange: (value: string) => void;

    context?: 'content' | 'subject';

    readOnly?: boolean;

    loading?: boolean;
}

interface FieldState {
    contentType: string;
    monitoring: string;
    genre: string;
    tag: string;
    title: string;
    kind: string;
}

const CONTENT_TYPES = ['movie', 'show', 'season', 'episode', 'music', 'book'];
const MONITORING_VALUES = ['None', 'Active', 'Excluded'];
const SUBJECT_KINDS = ['movie', 'show', 'person', 'album', 'book'];

const ANY_VALUE = '__any__';

function buildQuery(fields: FieldState, context: 'content' | 'subject'): string {
    const clauses: string[] = [];

    if (context === 'content') {
        if (fields.contentType) {
            clauses.push(`contentType = "${fields.contentType}"`);
        }
        if (fields.monitoring) {
            clauses.push(`monitoring = ${fields.monitoring}`);
        }
    } else {
        if (fields.kind) {
            clauses.push(`kind = "${fields.kind}"`);
        }
    }

    if (fields.title) {
        clauses.push(`title contains "${fields.title}"`);
    }
    if (fields.genre) {
        clauses.push(`genre has "${fields.genre}"`);
    }
    if (fields.tag) {
        clauses.push(`tag has "${fields.tag}"`);
    }

    return clauses.join(' and ');
}

function parseQuery(query: string, _context: 'content' | 'subject'): FieldState | null {
    const state: FieldState = {
        contentType: '',
        monitoring: '',
        genre: '',
        tag: '',
        title: '',
        kind: '',
    };

    if (!query.trim()) {
        return state;
    }

    const parts = query.split(/\s+and\s+/i);

    for (const part of parts) {
        const trimmed = part.trim();

        const contentTypeMatch = trimmed.match(/^contentType\s*=\s*"([^"]*)"$/i);
        if (contentTypeMatch) {
            state.contentType = contentTypeMatch[1];
            continue;
        }

        const monitoringMatch = trimmed.match(/^monitoring\s*=\s*(\w+)$/i);
        if (monitoringMatch) {
            state.monitoring = monitoringMatch[1];
            continue;
        }

        const kindMatch = trimmed.match(/^kind\s*=\s*"([^"]*)"$/i);
        if (kindMatch) {
            state.kind = kindMatch[1];
            continue;
        }

        const titleMatch = trimmed.match(/^title\s+contains\s+"([^"]*)"$/i);
        if (titleMatch) {
            state.title = titleMatch[1];
            continue;
        }

        const genreMatch = trimmed.match(/^genre\s+has\s+"([^"]*)"$/i);
        if (genreMatch) {
            state.genre = genreMatch[1];
            continue;
        }

        const tagMatch = trimmed.match(/^tag\s+has\s+"([^"]*)"$/i);
        if (tagMatch) {
            state.tag = tagMatch[1];
            continue;
        }

        return null;
    }

    return state;
}

export function SimpleFilterBuilder({
    value,
    onChange,
    context = 'content',
    readOnly = false,
    loading = false,
    className,
    ...props
}: SimpleFilterBuilderProps) {
    const fields = useMemo(
        () =>
            parseQuery(value, context) ?? {
                contentType: '',
                monitoring: '',
                genre: '',
                tag: '',
                title: '',
                kind: '',
            },
        [value, context],
    );

    const updateField = useCallback(
        (field: keyof FieldState, fieldValue: string) => {
            const normalizedValue = fieldValue === ANY_VALUE ? '' : fieldValue;
            const updated = { ...fields, [field]: normalizedValue };
            onChange(buildQuery(updated, context));
        },
        [fields, onChange, context],
    );

    const disabled = readOnly || loading;

    const classes = ['simple-filter-builder', readOnly && 'simple-filter-builder--readonly', className]
        .filter(Boolean)
        .join(' ');

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
                            value={fields.contentType || ANY_VALUE}
                            onValueChange={(v) => updateField('contentType', v)}
                            disabled={disabled}
                        >
                            <SelectTrigger id="sfb-content-type" size="sm" placeholder="Any">
                                {fields.contentType || 'Any'}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ANY_VALUE}>Any</SelectItem>
                                {CONTENT_TYPES.map((t) => (
                                    <SelectItem key={t} value={t}>
                                        {t}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    </div>

                    <div className="simple-filter-builder__field">
                        <Label htmlFor="sfb-monitoring">Monitoring</Label>
                        <Select
                            value={fields.monitoring || ANY_VALUE}
                            onValueChange={(v) => updateField('monitoring', v)}
                            disabled={disabled}
                        >
                            <SelectTrigger id="sfb-monitoring" size="sm" placeholder="Any">
                                {fields.monitoring || 'Any'}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ANY_VALUE}>Any</SelectItem>
                                {MONITORING_VALUES.map((v) => (
                                    <SelectItem key={v} value={v}>
                                        {v}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    </div>
                </>
            )}

            {context === 'subject' && (
                <div className="simple-filter-builder__field">
                    <Label htmlFor="sfb-kind">Kind</Label>
                    <Select
                        value={fields.kind || ANY_VALUE}
                        onValueChange={(v) => updateField('kind', v)}
                        disabled={disabled}
                    >
                        <SelectTrigger id="sfb-kind" size="sm" placeholder="Any">
                            {fields.kind || 'Any'}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ANY_VALUE}>Any</SelectItem>
                            {SUBJECT_KINDS.map((k) => (
                                <SelectItem key={k} value={k}>
                                    {k}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                </div>
            )}

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

            <div className="simple-filter-builder__field">
                <Label htmlFor="sfb-genre">Genre</Label>
                <Input
                    id="sfb-genre"
                    size="sm"
                    value={fields.genre}
                    onChange={(e) => updateField('genre', e.target.value)}
                    disabled={disabled}
                    placeholder="e.g. Action"
                />
            </div>

            <div className="simple-filter-builder__field">
                <Label htmlFor="sfb-tag">Tag</Label>
                <Input
                    id="sfb-tag"
                    size="sm"
                    value={fields.tag}
                    onChange={(e) => updateField('tag', e.target.value)}
                    disabled={disabled}
                    placeholder="e.g. favorite"
                />
            </div>
        </div>
    );
}

export function isSimpleQuery(query: string, context: 'content' | 'subject'): boolean {
    if (!query.trim()) {
        return true;
    }
    return parseQuery(query, context) !== null;
}

SimpleFilterBuilder.displayName = 'SimpleFilterBuilder';

export default SimpleFilterBuilder;
