import { forwardRef, type ComponentPropsWithoutRef, useCallback } from 'react';
import { SortAscending, SortDescending, ArrowsDownUp } from '@phosphor-icons/react';

import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import { Button } from '../Button';

import './ContentSorting.css';

export type ContentSortField = 'title' | 'dateAdded' | 'lastSearched';

export type ContentSortDirection = 'asc' | 'desc';

export interface ContentSortState {
    field: ContentSortField;

    direction: ContentSortDirection;
}

export interface ContentSortingProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    sort: ContentSortState;

    onChange: (sort: ContentSortState) => void;

    disabled?: boolean;
}

function formatSortFieldLabel(field: ContentSortField): string {
    const labels: Record<ContentSortField, string> = {
        title: 'Title',
        dateAdded: 'Date Added',
        lastSearched: 'Last Searched',
    };
    return labels[field];
}

export const ContentSorting = forwardRef<HTMLDivElement, ContentSortingProps>(
    ({ sort, onChange, disabled = false, className, ...props }, ref) => {
        const handleFieldChange = useCallback(
            (value: string) => {
                onChange({
                    ...sort,
                    field: value as ContentSortField,
                });
            },
            [sort, onChange],
        );

        const handleDirectionToggle = useCallback(() => {
            onChange({
                ...sort,
                direction: sort.direction === 'asc' ? 'desc' : 'asc',
            });
        }, [sort, onChange]);

        const classes = ['content-sorting', className].filter(Boolean).join(' ');

        const DirectionIcon = sort.direction === 'asc' ? SortAscending : SortDescending;
        const directionLabel = sort.direction === 'asc' ? 'Ascending' : 'Descending';
        const toggleLabel = `Sort ${sort.direction === 'asc' ? 'descending' : 'ascending'}`;

        return (
            <div ref={ref} className={classes} data-component="content-sorting" {...props}>
                <div className="content-sorting__label">
                    <ArrowsDownUp weight="duotone" aria-hidden="true" />
                    <span>Sort</span>
                </div>

                <div className="content-sorting__controls">
                    <Select value={sort.field} onValueChange={handleFieldChange} disabled={disabled}>
                        <SelectTrigger size="sm" aria-label="Sort by field" data-sort="field">
                            {formatSortFieldLabel(sort.field)}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value="title">Title</SelectItem>
                            <SelectItem value="dateAdded">Date Added</SelectItem>
                            <SelectItem value="lastSearched">Last Searched</SelectItem>
                        </SelectContent>
                    </Select>

                    <Button
                        variant="ghost"
                        size="sm"
                        onClick={handleDirectionToggle}
                        disabled={disabled}
                        aria-label={toggleLabel}
                        data-sort="direction"
                        data-direction={sort.direction}
                        className="content-sorting__direction"
                    >
                        <DirectionIcon weight="bold" aria-hidden="true" />
                        <span className="content-sorting__direction-label">{directionLabel}</span>
                    </Button>
                </div>
            </div>
        );
    },
);

ContentSorting.displayName = 'ContentSorting';

export default ContentSorting;
