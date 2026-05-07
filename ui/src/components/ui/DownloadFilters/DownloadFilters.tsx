import { forwardRef, type ComponentPropsWithoutRef, useCallback } from 'react';

import { Select, SelectTrigger, SelectContent, SelectItem } from '../Select';
import { Button } from '../Button';
import { type DownloadClientOption, type StateFilterOption, type DownloadFilterState } from '@/types';

import '@/styles/list-toolbar.css';

const ALL_VALUE = '__all__';

export interface DownloadFiltersProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    filters: DownloadFilterState;

    clients: DownloadClientOption[];

    stateOptions: StateFilterOption[];

    onChange: (filters: DownloadFilterState) => void;

    loading?: boolean;
}

export const DownloadFilters = forwardRef<HTMLDivElement, DownloadFiltersProps>(
    ({ filters, clients, stateOptions, onChange, loading = false, className, ...props }, ref) => {
        const hasActiveFilters = !!(filters.client || filters.state);

        const handleClientChange = useCallback(
            (value: string) => {
                onChange({
                    ...filters,
                    client: value === ALL_VALUE ? '' : value,
                });
            },
            [filters, onChange],
        );

        const handleStateChange = useCallback(
            (value: string) => {
                onChange({
                    ...filters,
                    state: value === ALL_VALUE ? '' : value,
                });
            },
            [filters, onChange],
        );

        const handleClearAll = useCallback(() => {
            onChange({ client: '', state: '' });
        }, [onChange]);

        const classes = ['list-toolbar__filters', className].filter(Boolean).join(' ');

        const clientLabel = filters.client
            ? (clients.find((c) => c.id === filters.client)?.name ?? 'Unknown')
            : 'All Clients';

        const stateLabel = filters.state
            ? (stateOptions.find((o) => String(o.value) === filters.state)?.label ?? 'Unknown')
            : 'All States';

        return (
            <div ref={ref} className={classes} data-component="download-filters" {...props}>
                <div className="list-toolbar__filters-controls">
                    <Select value={filters.client || ALL_VALUE} onValueChange={handleClientChange} disabled={loading}>
                        <SelectTrigger
                            size="sm"
                            placeholder="All Clients"
                            aria-label="Filter by client"
                            data-filter="client"
                        >
                            {clientLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All Clients</SelectItem>
                            {clients.map((client) => (
                                <SelectItem key={client.id} value={client.id}>
                                    {client.name}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>

                    <Select value={filters.state || ALL_VALUE} onValueChange={handleStateChange} disabled={loading}>
                        <SelectTrigger
                            size="sm"
                            placeholder="All States"
                            aria-label="Filter by state"
                            data-filter="state"
                        >
                            {stateLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All States</SelectItem>
                            {stateOptions
                                .filter((o) => o.value !== 'all')
                                .map((opt) => (
                                    <SelectItem key={String(opt.value)} value={String(opt.value)}>
                                        {opt.label}
                                    </SelectItem>
                                ))}
                        </SelectContent>
                    </Select>

                    {hasActiveFilters && (
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={handleClearAll}
                            className="list-toolbar__clear-filters"
                            data-action="clear-filters"
                        >
                            Clear
                        </Button>
                    )}
                </div>
            </div>
        );
    },
);

DownloadFilters.displayName = 'DownloadFilters';

export default DownloadFilters;
