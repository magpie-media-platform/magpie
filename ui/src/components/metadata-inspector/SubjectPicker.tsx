import { useId, useMemo, useState } from 'react';
import { MagnifyingGlassIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { useSubjects, useSubject, type ApiSubject } from '@/hooks/useSubjects';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { extractTitle } from '@/lib/metadata';
import { magpieQlString } from '@/lib/magpieQl';

const RESULT_LIMIT = 20;

export type SubjectSelection =
    | { mode: 'existing'; subjectId: string }
    | { mode: 'new'; subjectName: string; subjectKind: string };

interface SubjectPickerProps {
    value: SubjectSelection | null;
    onChange: (value: SubjectSelection | null) => void;
    error?: boolean;
}

function subjectDisplayName(subject: ApiSubject): string {
    const title = extractTitle(subject.metadataContainer);
    if (title) {
        return title;
    }
    return `${subject.kind} ${subject.id.slice(0, 8)}`;
}

export function SubjectPicker({ value, onChange, error }: SubjectPickerProps) {
    const mode = value?.mode ?? 'existing';
    const searchInputId = useId();
    const newNameId = useId();

    const { subjectTypes } = useLibraryTypes();

    const [query, setQuery] = useState('');
    const debouncedQuery = useDebouncedValue(query, 300);

    const subjectFilter = useMemo(() => {
        const trimmed = debouncedQuery.trim();
        if (!trimmed) {
            return undefined;
        }
        return `title contains ${magpieQlString(trimmed)}`;
    }, [debouncedQuery]);

    const { subjects, isLoading } = useSubjects({
        pageSize: RESULT_LIMIT,
        sortBy: 'title',
        sortDirection: 'Ascending',
        ...(subjectFilter ? { filter: subjectFilter } : {}),
    });

    const matches = useMemo(() => subjects.map((s) => ({ subject: s, name: subjectDisplayName(s) })), [subjects]);

    const selectedSubjectId = value?.mode === 'existing' ? value.subjectId : '';
    const { subject: selectedSubject } = useSubject(selectedSubjectId, Boolean(selectedSubjectId));

    const selectedExistingName = useMemo(() => {
        if (value?.mode !== 'existing') {
            return null;
        }
        const fromResults = matches.find((entry) => entry.subject.id === value.subjectId)?.name;
        if (fromResults) {
            return fromResults;
        }
        return selectedSubject ? subjectDisplayName(selectedSubject) : null;
    }, [matches, selectedSubject, value]);

    const setMode = (next: 'existing' | 'new') => {
        if (next === mode) {
            return;
        }
        onChange(next === 'existing' ? null : { mode: 'new', subjectName: '', subjectKind: '' });
        if (next === 'existing') {
            setQuery('');
        }
    };

    return (
        <div className="metadata-inspector__subject-picker" data-component="subject-picker">
            <div className="metadata-inspector__subject-mode" role="radiogroup" aria-label="Subject source">
                <Button
                    type="button"
                    size="sm"
                    variant={mode === 'existing' ? 'primary' : 'ghost'}
                    role="radio"
                    aria-checked={mode === 'existing'}
                    onClick={() => setMode('existing')}
                    data-action="subject-mode-existing"
                >
                    Existing
                </Button>
                <Button
                    type="button"
                    size="sm"
                    variant={mode === 'new' ? 'primary' : 'ghost'}
                    role="radio"
                    aria-checked={mode === 'new'}
                    onClick={() => setMode('new')}
                    data-action="subject-mode-new"
                >
                    Create new
                </Button>
            </div>

            {mode === 'existing' ? (
                <div className="metadata-inspector__subject-existing">
                    <Label htmlFor={searchInputId}>Find subject</Label>
                    <Input
                        id={searchInputId}
                        type="search"
                        value={query}
                        onChange={(e) => setQuery(e.target.value)}
                        placeholder="Search by name..."
                        leadingIcon={<MagnifyingGlassIcon weight="regular" />}
                        error={error}
                        aria-label="Search subjects by name"
                        data-search="subject"
                    />
                    {selectedExistingName && (
                        <p className="metadata-inspector__subject-selected" data-testid="subject-selected">
                            Selected: {selectedExistingName}
                        </p>
                    )}
                    <ul className="metadata-inspector__subject-results" aria-label="Subjects" role="list">
                        {isLoading ? (
                            <li className="metadata-inspector__empty">Loading...</li>
                        ) : matches.length === 0 ? (
                            <li className="metadata-inspector__empty">No matches found.</li>
                        ) : (
                            matches.map((entry) => {
                                const selected = value?.mode === 'existing' && value.subjectId === entry.subject.id;
                                return (
                                    <li key={entry.subject.id}>
                                        <button
                                            type="button"
                                            aria-pressed={selected}
                                            className="metadata-inspector__subject-result"
                                            data-selected={selected || undefined}
                                            onClick={() => onChange({ mode: 'existing', subjectId: entry.subject.id })}
                                        >
                                            <span className="metadata-inspector__subject-result-name">
                                                {entry.name}
                                            </span>
                                            <span className="metadata-inspector__subject-result-kind">
                                                {entry.subject.kind}
                                            </span>
                                        </button>
                                    </li>
                                );
                            })
                        )}
                    </ul>
                </div>
            ) : (
                <div className="metadata-inspector__subject-new">
                    <div className="metadata-inspector__dialog-field">
                        <Label htmlFor={newNameId}>
                            Name <span aria-hidden="true">*</span>
                        </Label>
                        <Input
                            id={newNameId}
                            value={value?.mode === 'new' ? value.subjectName : ''}
                            onChange={(e) =>
                                onChange({
                                    mode: 'new',
                                    subjectName: e.target.value,
                                    subjectKind: value?.mode === 'new' ? value.subjectKind : '',
                                })
                            }
                            placeholder="Jane Doe"
                            error={error}
                            aria-label="New subject name"
                        />
                    </div>
                    <div className="metadata-inspector__dialog-field">
                        <Label>
                            Kind <span aria-hidden="true">*</span>
                        </Label>
                        <Select
                            value={value?.mode === 'new' ? value.subjectKind || undefined : undefined}
                            onValueChange={(kind) =>
                                onChange({
                                    mode: 'new',
                                    subjectName: value?.mode === 'new' ? value.subjectName : '',
                                    subjectKind: kind,
                                })
                            }
                        >
                            <SelectTrigger error={error} aria-label="New subject kind" data-testid="subject-kind">
                                <SelectValue placeholder="Select a kind..." />
                            </SelectTrigger>
                            <SelectContent>
                                {subjectTypes.map((type) => (
                                    <SelectItem key={type.slug} value={type.slug}>
                                        {type.name}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    </div>
                </div>
            )}
        </div>
    );
}
