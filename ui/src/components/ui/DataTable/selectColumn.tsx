import { type ColumnDef } from '@tanstack/react-table';

import { Checkbox } from '@/components/ui/Checkbox';
import type { BulkSelectionState } from '@/types/bulkSelection';

export interface SelectColumnArgs<TData> {
    selection: BulkSelectionState;

    getId: (row: TData) => string;

    headerLabel: string;

    rowLabel: (row: TData) => string;

    canSelect?: (row: TData) => boolean;
}

/** The leading checkbox column for a bulk-selectable table, whose header checkbox covers only the current page. */
export function selectColumn<TData>({
    selection,
    getId,
    headerLabel,
    rowLabel,
    canSelect,
}: SelectColumnArgs<TData>): ColumnDef<TData> {
    return {
        id: 'select',
        enableSorting: false,
        size: 52,
        meta: { mobileRole: 'hidden' },
        header: () => (
            <Checkbox
                checked={selection.headerChecked}
                onCheckedChange={(checked) => selection.toggleHeader(!!checked)}
                aria-label={headerLabel}
                data-action="select-all"
            />
        ),
        cell: ({ row }) => (
            <Checkbox
                checked={selection.isRowChecked(getId(row.original))}
                disabled={canSelect !== undefined && !canSelect(row.original)}
                onCheckedChange={(checked) => selection.toggleRow(getId(row.original), !!checked)}
                aria-label={`Select ${rowLabel(row.original)}`}
                onClick={(event) => event.stopPropagation()}
                onKeyDown={(event) => event.stopPropagation()}
                data-action="select-row"
            />
        ),
    };
}
