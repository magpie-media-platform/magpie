import { forwardRef, type ComponentPropsWithoutRef, useCallback } from 'react';
import { Funnel, Tag, X } from '@phosphor-icons/react';

import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import { Badge } from '../Badge';
import { Button } from '../Button';
import { type FilterableSubjectKind, formatSubjectKindFilterLabel } from '@/types';

import './SubjectFilters.css';

export interface TagItem {
    id: string;

    name: string;
}

const ALL_VALUE = '__all__';

export type SubjectKind = FilterableSubjectKind;

export interface SubjectFilterState {
    kind: SubjectKind | '';

    tags: string[];
}

export interface SubjectFiltersProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    filters: SubjectFilterState;

    availableTags?: TagItem[];

    onChange: (filters: SubjectFilterState) => void;

    loading?: boolean;

    showActiveCount?: boolean;
}

function getActiveFilterCount(filters: SubjectFilterState): number {
    let count = 0;
    if (filters.kind) count++;
    count += filters.tags.length;
    return count;
}

export const SubjectFilters = forwardRef<HTMLDivElement, SubjectFiltersProps>(
    ({ filters, availableTags = [], onChange, loading = false, showActiveCount = true, className, ...props }, ref) => {
        const activeCount = getActiveFilterCount(filters);

        const handleKindChange = useCallback(
            (value: string) => {
                onChange({
                    ...filters,
                    kind: value === ALL_VALUE ? '' : (value as SubjectKind),
                });
            },
            [filters, onChange],
        );

        const handleTagToggle = useCallback(
            (tagId: string) => {
                const newTags = filters.tags.includes(tagId)
                    ? filters.tags.filter((id) => id !== tagId)
                    : [...filters.tags, tagId];
                onChange({
                    ...filters,
                    tags: newTags,
                });
            },
            [filters, onChange],
        );

        const handleClearAll = useCallback(() => {
            onChange({
                kind: '',
                tags: [],
            });
        }, [onChange]);

        const handleRemoveTag = useCallback(
            (tagId: string) => {
                onChange({
                    ...filters,
                    tags: filters.tags.filter((id) => id !== tagId),
                });
            },
            [filters, onChange],
        );

        const classes = ['subject-filters', className].filter(Boolean).join(' ');

        return (
            <div ref={ref} className={classes} data-component="subject-filters" {...props}>
                <div className="subject-filters__header">
                    <div className="subject-filters__title">
                        <Funnel weight="duotone" aria-hidden="true" />
                        <span>Filters</span>
                        {showActiveCount && activeCount > 0 && (
                            <Badge variant="default" size="sm">
                                {activeCount}
                            </Badge>
                        )}
                    </div>
                    {activeCount > 0 && (
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={handleClearAll}
                            className="subject-filters__clear"
                            data-action="clear-filters"
                        >
                            Clear all
                        </Button>
                    )}
                </div>

                <div className="subject-filters__controls">
                    <div className="subject-filters__control">
                        <Select value={filters.kind || ALL_VALUE} onValueChange={handleKindChange} disabled={loading}>
                            <SelectTrigger
                                size="sm"
                                placeholder="All Kinds"
                                aria-label="Filter by subject kind"
                                data-filter="kind"
                            >
                                {formatSubjectKindFilterLabel(filters.kind)}
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_VALUE}>All Kinds</SelectItem>
                                <SelectItem value="show">Shows</SelectItem>
                                <SelectItem value="movie">Movies</SelectItem>
                                <SelectItem value="person">People</SelectItem>
                                <SelectItem value="album">Albums</SelectItem>
                                <SelectItem value="book">Books</SelectItem>
                            </SelectContent>
                        </Select>
                    </div>

                    {availableTags.length > 0 && (
                        <div className="subject-filters__control subject-filters__control--tags">
                            <Select value="" onValueChange={handleTagToggle} disabled={loading}>
                                <SelectTrigger
                                    size="sm"
                                    placeholder="Select tags"
                                    aria-label="Filter by tags"
                                    data-filter="tags"
                                >
                                    <span className="subject-filters__tags-trigger">
                                        <Tag weight="regular" aria-hidden="true" />
                                        {filters.tags.length > 0 ? `${filters.tags.length} selected` : 'Tags'}
                                    </span>
                                </SelectTrigger>
                                <SelectContent>
                                    {availableTags.map((tag) => (
                                        <SelectItem
                                            key={tag.id}
                                            value={tag.id}
                                            data-selected={filters.tags.includes(tag.id)}
                                        >
                                            <span className="subject-filters__tag-item">
                                                {tag.name}
                                                {filters.tags.includes(tag.id) && (
                                                    <span className="subject-filters__tag-check">&#10003;</span>
                                                )}
                                            </span>
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>
                    )}
                </div>

                {filters.tags.length > 0 && (
                    <div className="subject-filters__selected-tags">
                        {filters.tags.map((tagId) => {
                            const tag = availableTags.find((t) => t.id === tagId);
                            if (!tag) return null;
                            return (
                                <Badge
                                    key={tag.id}
                                    variant="default"
                                    size="sm"
                                    className="subject-filters__selected-tag"
                                >
                                    {tag.name}
                                    <button
                                        type="button"
                                        className="subject-filters__tag-remove"
                                        onClick={() => handleRemoveTag(tag.id)}
                                        aria-label={`Remove ${tag.name} filter`}
                                    >
                                        <X weight="bold" aria-hidden="true" />
                                    </button>
                                </Badge>
                            );
                        })}
                    </div>
                )}
            </div>
        );
    },
);

SubjectFilters.displayName = 'SubjectFilters';

export default SubjectFilters;
