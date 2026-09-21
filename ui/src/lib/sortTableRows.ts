import {
    createTable,
    getCoreRowModel,
    getSortedRowModel,
    type ColumnDef,
    type SortingState,
} from '@tanstack/react-table';

export function sortTableRows<T>(data: T[], columns: ColumnDef<T>[], sorting: SortingState): T[] {
    if (sorting.length === 0) return data;
    const table = createTable({
        data,
        columns,
        state: { sorting },
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: getSortedRowModel(),
        onStateChange: () => {},
        renderFallbackValue: null,
    });
    return table.getSortedRowModel().rows.map((row) => row.original);
}
