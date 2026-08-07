import { forwardRef, type ComponentPropsWithoutRef, useCallback } from 'react';

import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import { ClearFiltersButton } from '../ClearFiltersButton';

import '@/styles/list-toolbar.css';

const ALL_VALUE = '__all__';

export type JobExecutionStatus = 'pending' | 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface TemplateOption {
    id: string;

    name: string;
}

export interface StatusFilterOption {
    value: JobExecutionStatus | 'all';

    label: string;
}

export interface JobExecutionFilterState {
    template: string;

    status: string;
}

export interface JobExecutionFiltersProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    filters: JobExecutionFilterState;

    templates: TemplateOption[];

    statusOptions: StatusFilterOption[];

    onChange: (filters: JobExecutionFilterState) => void;

    loading?: boolean;
}

export const JobExecutionFilters = forwardRef<HTMLDivElement, JobExecutionFiltersProps>(
    ({ filters, templates, statusOptions, onChange, loading = false, className, ...props }, ref) => {
        const hasActiveFilters = !!(filters.template || filters.status);

        const handleTemplateChange = useCallback(
            (value: string) => {
                onChange({
                    ...filters,
                    template: value === ALL_VALUE ? '' : value,
                });
            },
            [filters, onChange],
        );

        const handleStatusChange = useCallback(
            (value: string) => {
                onChange({
                    ...filters,
                    status: value === ALL_VALUE ? '' : value,
                });
            },
            [filters, onChange],
        );

        const handleClearAll = useCallback(() => {
            onChange({ template: '', status: '' });
        }, [onChange]);

        const classes = ['list-toolbar__filters', className].filter(Boolean).join(' ');

        const templateLabel = filters.template
            ? (templates.find((t) => t.id === filters.template)?.name ?? 'Unknown')
            : 'All Templates';

        const statusLabel = filters.status
            ? (statusOptions.find((o) => o.value === filters.status)?.label ?? 'Unknown')
            : 'All Statuses';

        return (
            <div ref={ref} className={classes} data-component="job-execution-filters" {...props}>
                <div className="list-toolbar__filters-controls">
                    <Select
                        value={filters.template || ALL_VALUE}
                        onValueChange={handleTemplateChange}
                        disabled={loading}
                    >
                        <SelectTrigger
                            size="sm"
                            placeholder="All Templates"
                            aria-label="Filter by template"
                            data-filter="template"
                        >
                            {templateLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All Templates</SelectItem>
                            {templates.map((template) => (
                                <SelectItem key={template.id} value={template.id}>
                                    {template.name}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>

                    <Select value={filters.status || ALL_VALUE} onValueChange={handleStatusChange} disabled={loading}>
                        <SelectTrigger
                            size="sm"
                            placeholder="All Statuses"
                            aria-label="Filter by status"
                            data-filter="status"
                        >
                            {statusLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All Statuses</SelectItem>
                            {statusOptions
                                .filter((o) => o.value !== 'all')
                                .map((opt) => (
                                    <SelectItem key={String(opt.value)} value={String(opt.value)}>
                                        {opt.label}
                                    </SelectItem>
                                ))}
                        </SelectContent>
                    </Select>

                    {hasActiveFilters && (
                        <ClearFiltersButton onClick={handleClearAll} className="list-toolbar__clear-filters" />
                    )}
                </div>
            </div>
        );
    },
);

JobExecutionFilters.displayName = 'JobExecutionFilters';

export default JobExecutionFilters;
