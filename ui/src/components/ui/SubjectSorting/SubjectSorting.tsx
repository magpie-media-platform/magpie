import { forwardRef, type ComponentPropsWithoutRef, useCallback } from 'react';
import { SortAscending, SortDescending, ArrowsDownUp } from '@phosphor-icons/react';

import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import { Button } from '../Button';

import './SubjectSorting.css';

export type SubjectSortField = 'title' | 'dateAdded';

export type SubjectSortDirection = 'asc' | 'desc';

export interface SubjectSortState {
    field: SubjectSortField;

    direction: SubjectSortDirection;
}

export interface SubjectSortingProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    sort: SubjectSortState;

    onChange: (sort: SubjectSortState) => void;

    disabled?: boolean;
}

function formatSortFieldLabel(field: SubjectSortField): string {
    const labels: Record<SubjectSortField, string> = {
        title: 'Title',
        dateAdded: 'Date Added',
    };
    return labels[field];
}

export const SubjectSorting = forwardRef<HTMLDivElement, SubjectSortingProps>(
    ({ sort, onChange, disabled = false, className, ...props }, ref) => {
        const handleFieldChange = useCallback(
            (value: string) => {
                onChange({
                    ...sort,
                    field: value as SubjectSortField,
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

        const classes = ['subject-sorting', className].filter(Boolean).join(' ');

        const DirectionIcon = sort.direction === 'asc' ? SortAscending : SortDescending;
        const directionLabel = sort.direction === 'asc' ? 'Ascending' : 'Descending';
        const toggleLabel = `Sort ${sort.direction === 'asc' ? 'descending' : 'ascending'}`;

        return (
            <div ref={ref} className={classes} data-component="subject-sorting" {...props}>
                <div className="subject-sorting__label">
                    <ArrowsDownUp weight="duotone" aria-hidden="true" />
                    <span>Sort</span>
                </div>

                <div className="subject-sorting__controls">
                    <Select value={sort.field} onValueChange={handleFieldChange} disabled={disabled}>
                        <SelectTrigger size="sm" aria-label="Sort by field" data-sort="field">
                            {formatSortFieldLabel(sort.field)}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value="title">Title</SelectItem>
                            <SelectItem value="dateAdded">Date Added</SelectItem>
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
                        className="subject-sorting__direction"
                    >
                        <DirectionIcon weight="bold" aria-hidden="true" />
                        <span className="subject-sorting__direction-label">{directionLabel}</span>
                    </Button>
                </div>
            </div>
        );
    },
);

SubjectSorting.displayName = 'SubjectSorting';

export default SubjectSorting;
