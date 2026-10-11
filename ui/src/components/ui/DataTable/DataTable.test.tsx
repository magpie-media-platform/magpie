import { useMemo, useState } from 'react';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { type ColumnDef } from '@tanstack/react-table';

import type { BulkSelectionState } from '@/types/bulkSelection';

import { DataTable } from './DataTable';
import { selectColumn } from './selectColumn';

interface TestData {
    id: number;
    name: string;
    status: string;
}

const testData: TestData[] = [
    { id: 1, name: 'Item One', status: 'active' },
    { id: 2, name: 'Item Two', status: 'inactive' },
    { id: 3, name: 'Item Three', status: 'active' },
    { id: 4, name: 'Item Four', status: 'pending' },
    { id: 5, name: 'Item Five', status: 'active' },
];

const testColumns: ColumnDef<TestData, unknown>[] = [
    {
        accessorKey: 'id',
        header: 'ID',
    },
    {
        accessorKey: 'name',
        header: 'Name',
    },
    {
        accessorKey: 'status',
        header: 'Status',
    },
];

describe('DataTable', () => {
    it('renders a table with data', () => {
        render(<DataTable columns={testColumns} data={testData} />);

        expect(screen.getByRole('grid')).toBeInTheDocument();
        expect(screen.getByText('Item One')).toBeInTheDocument();
        expect(screen.getByText('Item Two')).toBeInTheDocument();
    });

    it('renders column headers', () => {
        render(<DataTable columns={testColumns} data={testData} />);

        expect(screen.getByText('ID')).toBeInTheDocument();
        expect(screen.getByText('Name')).toBeInTheDocument();
        expect(screen.getByText('Status')).toBeInTheDocument();
    });

    it('hides columns marked not visible via columnVisibility', () => {
        render(<DataTable columns={testColumns} data={testData} columnVisibility={{ status: false }} />);

        expect(screen.getByText('Name')).toBeInTheDocument();
        expect(screen.queryByText('Status')).not.toBeInTheDocument();
        expect(document.querySelector('[data-column="status"]')).not.toBeInTheDocument();
    });

    it('applies size classes', () => {
        const { rerender, container } = render(<DataTable columns={testColumns} data={testData} size="sm" />);
        expect(container.querySelector('.data-table')).toHaveAttribute('data-size', 'sm');

        rerender(<DataTable columns={testColumns} data={testData} size="md" />);
        expect(container.querySelector('.data-table')).toHaveAttribute('data-size', 'md');
    });

    it('defaults to md size', () => {
        const { container } = render(<DataTable columns={testColumns} data={testData} />);
        expect(container.querySelector('.data-table')).toHaveAttribute('data-size', 'md');
    });

    it('shows empty state when no data', () => {
        render(<DataTable columns={testColumns} data={[]} />);
        expect(screen.getByText('No data available')).toBeInTheDocument();
    });

    it('renders custom empty state', () => {
        render(<DataTable columns={testColumns} data={[]} emptyState={<div>Custom empty message</div>} />);
        expect(screen.getByText('Custom empty message')).toBeInTheDocument();
    });

    it('renders the empty state outside the horizontally scrolling wrapper', () => {
        const { container } = render(
            <DataTable
                columns={testColumns}
                data={[]}
                keepHeaderWhenEmpty
                emptyState={<div>Custom empty message</div>}
            />,
        );

        expect(container.querySelector('.data-table__wrapper')).not.toContainElement(
            screen.getByText('Custom empty message'),
        );
    });

    it('hides the column header row on a cold-start empty', () => {
        render(<DataTable columns={testColumns} data={[]} emptyState={<div>Custom empty message</div>} />);

        expect(screen.queryByText('ID')).not.toBeInTheDocument();
        expect(screen.queryByText('Name')).not.toBeInTheDocument();
        expect(screen.queryByText('Status')).not.toBeInTheDocument();
        expect(screen.getByText('Custom empty message')).toBeInTheDocument();
    });

    it('keeps the column header row when keepHeaderWhenEmpty is set and there is no data', () => {
        render(
            <DataTable
                columns={testColumns}
                data={[]}
                keepHeaderWhenEmpty
                emptyState={<div>Custom empty message</div>}
            />,
        );

        expect(screen.getByText('ID')).toBeInTheDocument();
        expect(screen.getByText('Name')).toBeInTheDocument();
        expect(screen.getByText('Status')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /sort by name/i })).toBeInTheDocument();
        expect(screen.getByText('Custom empty message')).toBeInTheDocument();
    });

    it('renders filter actions in a filter bar above the table', () => {
        const { container } = render(
            <DataTable columns={testColumns} data={testData} filterActions={<button type="button">Clear</button>} />,
        );

        const filterBar = container.querySelector('[data-component="data-table-filter-bar"]') as HTMLElement;
        expect(within(filterBar).getByRole('button', { name: 'Clear' })).toBeInTheDocument();
        expect(filterBar.nextElementSibling).toHaveClass('data-table__wrapper');
        expect(container.querySelector('.data-table')).toHaveClass('data-table--with-filter-bar');
    });

    it('renders no filter bar when filterActions is omitted', () => {
        const { container } = render(<DataTable columns={testColumns} data={testData} />);

        expect(container.querySelector('[data-component="data-table-filter-bar"]')).not.toBeInTheDocument();
        expect(container.querySelector('.data-table')).not.toHaveClass('data-table--with-filter-bar');
    });

    it('shows loading state', () => {
        const { container } = render(<DataTable columns={testColumns} data={testData} loading />);
        expect(container.querySelector('.data-table')).toHaveAttribute('data-loading', 'true');
        expect(container.querySelector('.data-table__loading-overlay')).toBeInTheDocument();
    });

    it('handles row click', async () => {
        const user = userEvent.setup();
        const handleRowClick = vi.fn();
        render(<DataTable columns={testColumns} data={testData} onRowClick={handleRowClick} />);

        const rows = screen.getAllByRole('row').slice(1);
        await user.click(rows[0]);

        expect(handleRowClick).toHaveBeenCalledWith(testData[0]);
    });

    it('applies clickable class when onRowClick is provided', () => {
        const { container } = render(<DataTable columns={testColumns} data={testData} onRowClick={() => {}} />);
        expect(container.querySelector('.data-table__row--clickable')).toBeInTheDocument();
    });

    it('applies custom className', () => {
        const { container } = render(<DataTable columns={testColumns} data={testData} className="custom-class" />);
        expect(container.querySelector('.data-table')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<DataTable columns={testColumns} data={testData} data-testid="my-table" />);
        expect(screen.getByTestId('my-table')).toBeInTheDocument();
    });

    describe('sorting', () => {
        it('enables sorting by default', () => {
            render(<DataTable columns={testColumns} data={testData} />);
            const sortableHeaders = document.querySelectorAll('.data-table__header-cell--sortable');
            expect(sortableHeaders.length).toBeGreaterThan(0);
        });

        it('disables sorting when enableSorting is false', () => {
            render(<DataTable columns={testColumns} data={testData} enableSorting={false} />);
            const sortableHeaders = document.querySelectorAll('.data-table__header-cell--sortable');
            expect(sortableHeaders.length).toBe(0);
        });

        it('sorts data when clicking header', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} />);

            const nameHeader = screen.getByRole('button', { name: /sort by name/i });
            await user.click(nameHeader);

            const sortedHeader = document.querySelector('.data-table__header-cell--sorted');
            expect(sortedHeader).toBeInTheDocument();
        });

        it('toggles sort direction on repeated clicks', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} />);

            const nameHeader = screen.getByRole('button', { name: /sort by name/i });

            await user.click(nameHeader);
            expect(nameHeader.closest('th')).toHaveAttribute('aria-sort', 'ascending');

            await user.click(nameHeader);
            expect(nameHeader.closest('th')).toHaveAttribute('aria-sort', 'descending');
        });

        it('supports keyboard sorting with Enter', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} />);

            const nameHeader = screen.getByRole('button', { name: /sort by name/i });
            nameHeader.focus();
            await user.keyboard('{Enter}');

            expect(nameHeader.closest('th')).toHaveAttribute('aria-sort', 'ascending');
        });

        it('supports keyboard sorting with Space', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} />);

            const nameHeader = screen.getByRole('button', { name: /sort by name/i });
            nameHeader.focus();
            await user.keyboard(' ');

            expect(nameHeader.closest('th')).toHaveAttribute('aria-sort', 'ascending');
        });

        it('supports controlled sorting', () => {
            const handleSortingChange = vi.fn();
            render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    sorting={[{ id: 'name', desc: false }]}
                    onSortingChange={handleSortingChange}
                />,
            );

            const nameHeader = screen.getByText('Name').closest('th');
            expect(nameHeader).toHaveAttribute('aria-sort', 'ascending');
        });
    });

    describe('pagination', () => {
        it('Pagination_HidesSinglePageControls_WhenExplicitPaginationIsEnabled', () => {
            // Arrange
            render(<DataTable columns={testColumns} data={testData.slice(0, 2)} enablePagination pageSize={5} />);

            // Act
            const pager = screen.queryByRole('navigation', { name: 'Pagination' });

            // Assert
            expect(pager).not.toBeInTheDocument();
        });

        it('hides pagination by default', () => {
            render(<DataTable columns={testColumns} data={testData} />);
            expect(screen.queryByText(/of/i)).not.toBeInTheDocument();
        });

        it('shows pagination when enabled', () => {
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);
            expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
        });

        it('navigates to next page', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            expect(screen.getByText('Item One')).toBeInTheDocument();
            expect(screen.queryByText('Item Three')).not.toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: /next/i }));

            expect(screen.queryByText('Item One')).not.toBeInTheDocument();
            expect(screen.getByText('Item Three')).toBeInTheDocument();
        });

        it('navigates to previous page', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            await user.click(screen.getByRole('button', { name: /next/i }));
            expect(screen.getByText('Item Three')).toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: /previous/i }));
            expect(screen.getByText('Item One')).toBeInTheDocument();
        });

        it('navigates to first page', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            await user.click(screen.getByRole('button', { name: /last/i }));
            expect(screen.getByText('Item Five')).toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: /first/i }));
            expect(screen.getByText('Item One')).toBeInTheDocument();
        });

        it('disables previous/first buttons on first page', () => {
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            expect(screen.getByRole('button', { name: /first/i })).toBeDisabled();
            expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();
        });

        it('disables next/last buttons on last page', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            await user.click(screen.getByRole('button', { name: /last/i }));

            expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
            expect(screen.getByRole('button', { name: /last/i })).toBeDisabled();
        });

        it('shows correct page info', () => {
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
        });

        it('keeps the current page when the data is replaced by an equal copy', async () => {
            const user = userEvent.setup();
            const { rerender } = render(
                <DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />,
            );
            await user.click(screen.getByRole('button', { name: /next/i }));

            rerender(<DataTable columns={testColumns} data={[...testData]} enablePagination pageSize={2} />);
            await act(async () => {});

            expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
            expect(screen.getByText('Item Three')).toBeInTheDocument();
        });

        it('clamps the current page to the last page when the data shrinks', async () => {
            const user = userEvent.setup();
            const { rerender } = render(
                <DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />,
            );
            await user.click(screen.getByRole('button', { name: /last/i }));

            rerender(<DataTable columns={testColumns} data={testData.slice(0, 3)} enablePagination pageSize={2} />);

            expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
            expect(screen.getByText('Item Three')).toBeInTheDocument();

            await user.click(screen.getByRole('button', { name: /previous/i }));

            expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
        });

        it('returns to the first page when the sorting changes', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);
            await user.click(screen.getByRole('button', { name: /next/i }));

            await user.click(screen.getByRole('button', { name: /sort by name/i }));

            expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
        });

        it('returns to the first page when the controlled sorting changes', async () => {
            const user = userEvent.setup();
            const { rerender } = render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enablePagination
                    pageSize={2}
                    sorting={[{ id: 'name', desc: false }]}
                />,
            );
            await user.click(screen.getByRole('button', { name: /next/i }));

            rerender(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enablePagination
                    pageSize={2}
                    sorting={[{ id: 'name', desc: true }]}
                />,
            );

            expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument();
        });

        it('keeps the current page when the controlled sorting is replaced by an equal copy', async () => {
            const user = userEvent.setup();
            const { rerender } = render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enablePagination
                    pageSize={2}
                    sorting={[{ id: 'name', desc: false }]}
                />,
            );
            await user.click(screen.getByRole('button', { name: /next/i }));

            rerender(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enablePagination
                    pageSize={2}
                    sorting={[{ id: 'name', desc: false }]}
                />,
            );
            await act(async () => {});

            expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
        });

        it('supports controlled pagination', () => {
            const handlePaginationChange = vi.fn();
            render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enablePagination
                    pagination={{ pageIndex: 1, pageSize: 2 }}
                    onPaginationChange={handlePaginationChange}
                />,
            );

            expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
            expect(screen.getByText('Item Three')).toBeInTheDocument();
        });

        it('supports manual pagination with rowCount', () => {
            render(
                <DataTable
                    columns={testColumns}
                    data={testData.slice(0, 2)}
                    enablePagination
                    manualPagination
                    rowCount={100}
                    pageSize={2}
                />,
            );

            expect(screen.getByText('Page 1 of 50')).toBeInTheDocument();
        });
    });

    describe('auto-pagination', () => {
        it('auto-enables pagination for large datasets', () => {
            const largeData = Array.from({ length: 60 }, (_, i) => ({
                id: i + 1,
                name: `Item ${i + 1}`,
                status: 'active',
            }));

            render(<DataTable columns={testColumns} data={largeData} />);

            expect(screen.getByRole('button', { name: 'Go to next page' })).toBeInTheDocument();
            const rows = screen.getAllByRole('row').slice(1);
            expect(rows.length).toBe(10);
        });

        it('does not auto-enable pagination for small datasets', () => {
            render(<DataTable columns={testColumns} data={testData} />);

            expect(screen.queryByRole('button', { name: 'Go to next page' })).not.toBeInTheDocument();
            const rows = screen.getAllByRole('row').slice(1);
            expect(rows.length).toBe(5);
        });
    });

    describe('row selection', () => {
        it('does not enable selection by default', () => {
            render(<DataTable columns={testColumns} data={testData} />);
            const rows = screen.getAllByRole('row').slice(1);
            rows.forEach((row) => {
                expect(row).not.toHaveAttribute('aria-selected');
            });
        });

        it('supports controlled row selection', () => {
            const handleSelectionChange = vi.fn();
            render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enableRowSelection
                    rowSelection={{ '1': true }}
                    onRowSelectionChange={handleSelectionChange}
                    getRowId={(row) => String(row.id)}
                />,
            );

            const rows = screen.getAllByRole('row').slice(1);
            expect(rows[0]).toHaveAttribute('data-state', 'selected');
        });
    });

    describe('select column', () => {
        const selection: BulkSelectionState = {
            allPages: false,
            hasSelection: false,
            headerChecked: false,
            selectedCount: 0,
            pageSelectedCount: 0,
            totalCount: 5,
            showAcrossPagesPrompt: false,
            isRowChecked: () => false,
            toggleRow: vi.fn(),
            toggleHeader: vi.fn(),
            selectAllAcrossPages: vi.fn(),
            clear: vi.fn(),
        };

        it('is wide enough for the checkbox and the cell padding', () => {
            const columns = [
                selectColumn<TestData>({
                    selection,
                    getId: (row) => String(row.id),
                    headerLabel: 'Select all',
                    rowLabel: (row) => row.name,
                }),
                ...testColumns,
            ];

            render(<DataTable columns={columns} data={testData} />);

            expect(document.querySelector('th[data-column="select"]')).toHaveStyle({ width: '52px' });
        });
    });

    describe('accessibility', () => {
        it('has role="grid" on table', () => {
            render(<DataTable columns={testColumns} data={testData} />);
            expect(screen.getByRole('grid')).toBeInTheDocument();
        });

        it('has scope="col" on header cells', () => {
            render(<DataTable columns={testColumns} data={testData} />);
            const headerCells = screen.getAllByRole('columnheader');
            headerCells.forEach((cell) => {
                expect(cell).toHaveAttribute('scope', 'col');
            });
        });

        it('has aria-sort on sortable columns', async () => {
            const user = userEvent.setup();
            render(<DataTable columns={testColumns} data={testData} />);

            const nameHeader = screen.getByText('Name').closest('th');
            expect(nameHeader).not.toHaveAttribute('aria-sort');

            const sortButton = within(nameHeader!).getByRole('button');
            await user.click(sortButton);

            expect(nameHeader).toHaveAttribute('aria-sort', 'ascending');
        });

        it('has aria-busy and aria-live on loading overlay', () => {
            const { container } = render(<DataTable columns={testColumns} data={testData} loading />);
            const overlay = container.querySelector('.data-table__loading-overlay');
            expect(overlay).toHaveAttribute('aria-live', 'polite');
            expect(overlay).toHaveAttribute('aria-busy', 'true');
        });

        it('has aria-label on pagination buttons', () => {
            render(<DataTable columns={testColumns} data={testData} enablePagination pageSize={2} />);

            expect(screen.getByRole('button', { name: 'Go to first page' })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Go to previous page' })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Go to next page' })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Go to last page' })).toBeInTheDocument();
        });

        it('has aria-selected on selected rows', () => {
            render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    enableRowSelection
                    rowSelection={{ '1': true }}
                    getRowId={(row) => String(row.id)}
                />,
            );

            const rows = screen.getAllByRole('row').slice(1);
            expect(rows[0]).toHaveAttribute('aria-selected', 'true');
        });

        it('has data-row-id attribute on rows', () => {
            render(<DataTable columns={testColumns} data={testData} getRowId={(row) => String(row.id)} />);

            const rows = screen.getAllByRole('row').slice(1);
            expect(rows[0]).toHaveAttribute('data-row-id', '1');
            expect(rows[1]).toHaveAttribute('data-row-id', '2');
            expect(rows[2]).toHaveAttribute('data-row-id', '3');
        });

        it('has data-label attribute on cells with string headers', () => {
            render(<DataTable columns={testColumns} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[0]).toHaveAttribute('data-label', 'ID');
            expect(cells[1]).toHaveAttribute('data-label', 'Name');
            expect(cells[2]).toHaveAttribute('data-label', 'Status');
        });

        it('omits data-label attribute on cells with empty or non-string headers', () => {
            const columnsWithEmpty: ColumnDef<TestData, unknown>[] = [
                {
                    accessorKey: 'id',
                    header: '',
                    enableSorting: false,
                },
                {
                    accessorKey: 'name',
                    header: 'Name',
                },
            ];

            render(<DataTable columns={columnsWithEmpty} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[0]).not.toHaveAttribute('data-label');
            expect(cells[1]).toHaveAttribute('data-label', 'Name');
        });

        it('derives the card-collapse data-label from meta.headerLabel for function headers', () => {
            const columnsWithMeta: ColumnDef<TestData, unknown>[] = [
                {
                    accessorKey: 'status',
                    header: () => <button type="button">filter</button>,
                    meta: { headerLabel: 'Status' },
                    enableSorting: false,
                },
                {
                    accessorKey: 'name',
                    header: 'Name',
                },
            ];

            render(<DataTable columns={columnsWithMeta} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[0]).toHaveAttribute('data-label', 'Status');
        });
    });

    describe('mobile card collapse roles', () => {
        it('infers field role for labeled cells and actions role for unlabeled cells', () => {
            const columns: ColumnDef<TestData, unknown>[] = [
                { id: 'actions', header: '', cell: () => <button type="button">act</button> },
                { accessorKey: 'name', header: 'Name' },
            ];

            render(<DataTable columns={columns} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[0]).toHaveAttribute('data-mobile-role', 'actions');
            expect(cells[1]).toHaveAttribute('data-mobile-role', 'field');
        });

        it('honors an explicit meta.mobileRole over the default inference', () => {
            const columns: ColumnDef<TestData, unknown>[] = [
                { accessorKey: 'name', header: 'Name', meta: { mobileRole: 'header' } },
                { accessorKey: 'status', header: 'Status', meta: { mobileRole: 'actions' } },
            ];

            render(<DataTable columns={columns} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[0]).toHaveAttribute('data-mobile-role', 'header');
            expect(cells[1]).toHaveAttribute('data-mobile-role', 'actions');
        });

        it('honors an explicit meta.mobileRole of hidden, for a value already shown elsewhere on the card', () => {
            const columns: ColumnDef<TestData, unknown>[] = [
                { accessorKey: 'name', header: 'Name', meta: { mobileRole: 'header' } },
                { accessorKey: 'status', header: 'Status', meta: { mobileRole: 'hidden' } },
            ];

            render(<DataTable columns={columns} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[1]).toHaveAttribute('data-mobile-role', 'hidden');
        });

        it('keeps a later header-role cell marked header even with a field-role cell between it and the first header cell', () => {
            const columns: ColumnDef<TestData, unknown>[] = [
                { accessorKey: 'name', header: 'Name', meta: { mobileRole: 'header' } },
                { accessorKey: 'id', header: 'Provider' },
                { accessorKey: 'status', header: 'Status', meta: { mobileRole: 'header' } },
            ];

            render(<DataTable columns={columns} data={testData} />);

            const firstRow = screen.getAllByRole('row')[1];
            const cells = within(firstRow).getAllByRole('gridcell');

            expect(cells[0]).toHaveAttribute('data-mobile-role', 'header');
            expect(cells[1]).toHaveAttribute('data-mobile-role', 'field');
            expect(cells[2]).toHaveAttribute('data-mobile-role', 'header');
        });

        it('omits data-mobile-selection-active when the prop is not supplied', () => {
            const { container } = render(<DataTable columns={testColumns} data={testData} />);
            expect(container.querySelector('.data-table')).not.toHaveAttribute('data-mobile-selection-active');
        });

        it('reflects mobileSelectionActive on the root element', () => {
            const { container, rerender } = render(
                <DataTable columns={testColumns} data={testData} mobileSelectionActive={false} />,
            );
            expect(container.querySelector('.data-table')).toHaveAttribute('data-mobile-selection-active', 'false');

            rerender(<DataTable columns={testColumns} data={testData} mobileSelectionActive={true} />);
            expect(container.querySelector('.data-table')).toHaveAttribute('data-mobile-selection-active', 'true');
        });

        it('keeps cell positions equal across rows when only some rows are selectable', () => {
            render(
                <DataTable
                    columns={testColumns}
                    data={testData}
                    mobileSelectionActive
                    mobileSelectionOverlay={{
                        isSelected: () => false,
                        onToggle: vi.fn(),
                        canSelect: (row) => row.status === 'active',
                        rowLabel: (row) => row.name,
                    }}
                />,
            );

            const cellLayouts = screen
                .getAllByRole('row')
                .slice(1)
                .map((row) =>
                    within(row)
                        .getAllByRole('gridcell')
                        .map((cell) => cell.getAttribute('data-column') ?? cell.className)
                        .join('|'),
                );

            expect(cellLayouts).toEqual(Array(5).fill('data-table__mobile-select-cell|id|name|status'));
            expect(screen.getAllByRole('button', { name: /^Select / })).toHaveLength(3);
            expect(screen.queryByRole('button', { name: 'Select Item Two' })).not.toBeInTheDocument();
        });
    });
});

describe('DataTable header reconciliation', () => {
    it('preserves a function header subtree when the column defs are rebuilt', async () => {
        const user = userEvent.setup();

        function StatefulHeader() {
            const [open, setOpen] = useState(false);
            return (
                <button type="button" data-testid="hdr" onClick={() => setOpen((o) => !o)}>
                    {open ? 'open' : 'closed'}
                </button>
            );
        }

        function Harness() {
            const [bump, setBump] = useState(0);
            const columns = useMemo<ColumnDef<TestData, unknown>[]>(
                () => [{ accessorKey: 'name', header: () => <StatefulHeader />, enableSorting: false }],
                // eslint-disable-next-line react-hooks/exhaustive-deps
                [bump],
            );
            return (
                <div>
                    <button type="button" data-testid="bump" onClick={() => setBump((b) => b + 1)}>
                        bump
                    </button>
                    <DataTable columns={columns} data={testData} />
                </div>
            );
        }

        render(<Harness />);

        await user.click(screen.getByTestId('hdr'));
        expect(screen.getByTestId('hdr')).toHaveTextContent('open');

        await user.click(screen.getByTestId('bump'));
        expect(screen.getByTestId('hdr')).toHaveTextContent('open');
    });
});

it('keeps explicit rows and cells when table display changes', () => {
    render(<DataTable columns={testColumns} data={testData} />);
    expect(screen.getAllByRole('row')[1]).toHaveAttribute('role', 'row');
    expect(screen.getAllByRole('gridcell')[0]).toHaveAttribute('role', 'gridcell');
    expect(screen.getAllByRole('columnheader')[0]).toHaveAttribute('role', 'columnheader');
});
