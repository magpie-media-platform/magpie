import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ListFilterToolbar, type ListFilterField } from './ListFilterToolbar';

const emptyFields: ListFilterField[] = [
    { id: 'status', label: 'Status', kind: 'facet', value: '', options: [{ value: 'Ready', label: 'Ready' }] },
    {
        id: 'provider',
        label: 'Provider',
        kind: 'multi',
        value: [],
        options: [
            { value: 'alpha', label: 'Alpha' },
            { value: 'beta', label: 'Beta' },
        ],
    },
    { id: 'name', label: 'Name', kind: 'text', value: '' },
    { id: 'size', label: 'Size', kind: 'numberRange', value: { min: '', max: '' }, unit: 'GB' },
];

describe('ListFilterToolbar', () => {
    beforeEach(() => vi.stubGlobal('innerWidth', 390));

    afterEach(() => {
        vi.restoreAllMocks();
        vi.unstubAllGlobals();
    });

    it('TableLayout_RendersNoFilterOrSortControls_WhenTableHeadersProvideThem', () => {
        // Arrange
        vi.stubGlobal('innerWidth', 1280);
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 1200 } as DOMRect);

        // Act
        render(
            <ListFilterToolbar
                fields={emptyFields}
                onCommit={vi.fn()}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
                selection={{ active: false, onToggle: vi.fn() }}
            />,
        );

        // Assert
        expect(screen.queryByRole('group', { name: 'List filters' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /^Filters/ })).not.toBeInTheDocument();
        expect(screen.queryByRole('combobox', { name: 'Sort by field' })).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="mobile-list-toolbar"]')).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="list-filter-toolbar"]')).toBeEmptyDOMElement();
    });

    it('TableLayout_KeepsActiveFilterChips_WhenTableHeadersProvideTheControls', () => {
        // Arrange
        vi.stubGlobal('innerWidth', 1280);

        // Act
        render(
            <ListFilterToolbar
                fields={[
                    {
                        id: 'status',
                        label: 'Status',
                        kind: 'facet',
                        value: 'Ready',
                        options: [{ value: 'Ready', label: 'Ready' }],
                    },
                ]}
                onCommit={vi.fn()}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );

        // Assert
        expect(screen.queryByRole('combobox', { name: 'Sort by field' })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Remove Status: Ready filter' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Clear all' })).toBeInTheDocument();
    });

    it('TableLayout_ClearsAllFilters_WhenClearAllIsClicked', async () => {
        // Arrange
        vi.stubGlobal('innerWidth', 1280);
        const user = userEvent.setup();
        const onCommit = vi.fn();
        render(
            <ListFilterToolbar
                fields={[
                    {
                        id: 'status',
                        label: 'Status',
                        kind: 'facet',
                        value: 'Ready',
                        options: [{ value: 'Ready', label: 'Ready' }],
                    },
                ]}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Clear all' }));

        // Assert
        expect(onCommit).toHaveBeenCalledWith({ status: '' });
    });

    it('FilterModal_StaysClosed_WhenTheLayoutReturnsToCards', async () => {
        // Arrange
        const user = userEvent.setup();
        const mediaListeners: ((event: { matches: boolean }) => void)[] = [];
        vi.stubGlobal('matchMedia', (query: string) => ({
            matches: true,
            media: query,
            addEventListener: (_type: string, listener: (event: { matches: boolean }) => void) =>
                mediaListeners.push(listener),
            removeEventListener: () => {},
        }));
        render(
            <ListFilterToolbar
                fields={emptyFields}
                onCommit={vi.fn()}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );
        await user.click(screen.getByRole('button', { name: 'Filters, 0 active' }));

        // Act
        act(() => mediaListeners.forEach((listener) => listener({ matches: false })));
        act(() => mediaListeners.forEach((listener) => listener({ matches: true })));

        // Assert
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('FilterModal_CommitsDraftOnlyOnApply_WhenDismissedAndReopened', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        render(
            <ListFilterToolbar
                fields={emptyFields}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Filters, 0 active' }));
        const dialog = screen.getByRole('dialog');
        await user.selectOptions(within(dialog).getByLabelText('Status'), 'Ready');
        await user.click(within(dialog).getByRole('checkbox', { name: 'Alpha' }));
        await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

        // Assert
        expect(onCommit).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Filters, 0 active' })).toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: 'Filters, 0 active' }));
        const reopened = screen.getByRole('dialog');
        await user.selectOptions(within(reopened).getByLabelText('Status'), 'Ready');
        await user.click(within(reopened).getByRole('checkbox', { name: 'Alpha' }));
        await user.type(within(reopened).getByLabelText('Name'), 'Orbit');
        await user.type(within(reopened).getByRole('spinbutton', { name: 'Size minimum' }), '2');
        await user.click(within(reopened).getByRole('button', { name: 'Apply' }));

        // Assert
        expect(onCommit).toHaveBeenCalledWith({
            status: 'Ready',
            provider: ['alpha'],
            name: 'Orbit',
            size: { min: '2', max: '' },
        });
    });

    it('FilterModal_ClearsEveryFilterFromThePillField_WhenClearAllAndPillsAreOn', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        render(
            <ListFilterToolbar
                fields={[
                    {
                        id: 'provider',
                        label: 'Provider',
                        kind: 'multi',
                        value: ['alpha'],
                        options: [
                            { value: 'alpha', label: 'Alpha' },
                            { value: 'beta', label: 'Beta' },
                        ],
                    },
                ]}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
                dialogClearAll
                pillMultiSelect
            />,
        );
        await user.click(screen.getByRole('button', { name: 'Filters, 1 active' }));
        const dialog = screen.getByRole('dialog');

        // Act
        await user.click(within(dialog).getByRole('button', { name: 'Remove Alpha' }));
        await user.click(within(dialog).getByRole('button', { name: 'Clear all' }));

        // Assert
        expect(onCommit).toHaveBeenCalledExactlyOnceWith({ provider: [] });
    });

    it('FilterModal_CommitsFractionalMinimum_WhenFocusedInputSubmitsWithEnter', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        render(
            <ListFilterToolbar
                fields={emptyFields}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Filters, 0 active' }));
        const minimum = within(screen.getByRole('dialog')).getByRole('spinbutton', {
            name: 'Size minimum',
        }) as HTMLInputElement;
        await user.type(minimum, '1.5');

        // Assert
        expect(minimum).toHaveFocus();

        // Act
        await user.keyboard('{Enter}');

        // Assert
        expect(onCommit).toHaveBeenCalledWith({
            status: '',
            provider: [],
            name: '',
            size: { min: '1.5', max: '' },
        });
        expect(minimum.validity.stepMismatch).toBe(false);
    });

    it('FilterModal_CommitsFractionalMaximum_WhenFocusedInputSubmitsWithEnter', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        render(
            <ListFilterToolbar
                fields={emptyFields}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Filters, 0 active' }));
        const maximum = within(screen.getByRole('dialog')).getByRole('spinbutton', {
            name: 'Size maximum',
        }) as HTMLInputElement;
        await user.type(maximum, '2.5');

        // Assert
        expect(maximum).toHaveFocus();

        // Act
        await user.keyboard('{Enter}');

        // Assert
        expect(onCommit).toHaveBeenCalledWith({
            status: '',
            provider: [],
            name: '',
            size: { min: '', max: '2.5' },
        });
        expect(maximum.validity.stepMismatch).toBe(false);
    });

    it('FiltersButton_KeepsCountOutsideLabel_WhenFiltersAreActive', () => {
        // Arrange
        const fields: ListFilterField[] = [
            { id: 'provider', label: 'Provider', kind: 'multi', value: ['alpha'], options: [] },
            { id: 'size', label: 'Size', kind: 'numberRange', value: { min: '2', max: '8' }, unit: 'GB' },
        ];

        // Act
        render(
            <ListFilterToolbar
                fields={fields}
                onCommit={vi.fn()}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );
        const button = screen.getByRole('button', { name: 'Filters, 2 active' });

        // Assert
        expect(button).toHaveTextContent('Filters (2)');
        expect(within(button).getByText('Filters')).toHaveClass('list-filter-toolbar__compact-label');
    });

    it('ActiveFilters_RemovesOneValue_WhenMultiSelectAndRangeAreActive', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        const fields: ListFilterField[] = [
            {
                id: 'provider',
                label: 'Provider',
                kind: 'multi',
                value: ['alpha', 'beta'],
                options: [
                    { value: 'alpha', label: 'Alpha' },
                    { value: 'beta', label: 'Beta' },
                ],
            },
            { id: 'size', label: 'Size', kind: 'numberRange', value: { min: '2', max: '8' }, unit: 'GB' },
        ];
        render(
            <ListFilterToolbar
                fields={fields}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: false }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={vi.fn()}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Remove Provider: Alpha filter' }));

        // Assert
        expect(onCommit).toHaveBeenCalledWith({ provider: ['beta'], size: { min: '2', max: '8' } });
        expect(screen.getByRole('button', { name: 'Filters, 2 active' })).toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: 'Clear all' }));

        // Assert
        expect(onCommit).toHaveBeenCalledWith({ provider: [], size: { min: '', max: '' } });
    });

    it('Toolbar_PreservesSort_WhenActiveFilterIsRemoved', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        const onSortingChange = vi.fn();
        render(
            <ListFilterToolbar
                fields={[
                    {
                        id: 'status',
                        label: 'Status',
                        kind: 'facet',
                        value: 'Ready',
                        options: [{ value: 'Ready', label: 'Ready' }],
                    },
                ]}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: true }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={onSortingChange}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Remove Status: Ready filter' }));

        // Assert
        expect(onCommit).toHaveBeenCalledWith({ status: '' });
        expect(onSortingChange).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Sort descending' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('LayoutSwitch_PreservesCommittedFiltersAndSort_WhenModalDraftIsOpen', async () => {
        // Arrange
        const user = userEvent.setup();
        const onCommit = vi.fn();
        const onSortingChange = vi.fn();
        const rect = vi
            .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
            .mockReturnValue({ width: 320 } as DOMRect);
        render(
            <ListFilterToolbar
                fields={[
                    {
                        id: 'status',
                        label: 'Status',
                        kind: 'facet',
                        value: 'Ready',
                        options: [{ value: 'Ready', label: 'Ready' }],
                    },
                ]}
                onCommit={onCommit}
                sorting={[{ id: 'name', desc: true }]}
                sortOptions={[{ id: 'name', label: 'Name' }]}
                onSortingChange={onSortingChange}
            />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Filters, 1 active' }));
        await user.selectOptions(within(screen.getByRole('dialog')).getByLabelText('Status'), '');
        rect.mockReturnValue({ width: 1200 } as DOMRect);
        act(() => {
            window.dispatchEvent(new Event('resize'));
        });

        // Assert
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Status filtered, edit filter' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Filters, 1 active' })).not.toBeInTheDocument();
        expect(onCommit).not.toHaveBeenCalled();
        expect(onSortingChange).not.toHaveBeenCalled();

        // Act
        rect.mockReturnValue({ width: 320 } as DOMRect);
        act(() => {
            window.dispatchEvent(new Event('resize'));
        });
        await user.click(screen.getByRole('button', { name: 'Filters, 1 active' }));

        // Assert
        expect(within(screen.getByRole('dialog')).getByLabelText('Status')).toHaveValue('Ready');

        // Act
        await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));

        // Assert
        expect(screen.getByRole('button', { name: 'Sort descending' })).toHaveAttribute('aria-pressed', 'true');
        expect(onCommit).not.toHaveBeenCalled();
    });
});
