import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import type { ChangeEvent } from 'react';

import { FilterBar } from './FilterBar';

vi.mock('@/editor/FilterQueryEditor', () => ({
    FilterQueryEditor: ({
        value,
        onChange,
        ...props
    }: {
        value: string;
        onChange: (v: string) => void;
        [key: string]: unknown;
    }) => (
        <div data-testid="filter-query-editor" data-value={value} {...props}>
            <input
                data-testid="query-input"
                value={value}
                onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
            />
        </div>
    ),
}));

vi.mock('../SimpleFilterBuilder', () => ({
    SimpleFilterBuilder: ({
        value,
        onChange: _onChange,
        ...props
    }: {
        value: string;
        onChange: (v: string) => void;
        [key: string]: unknown;
    }) => <div data-testid="simple-filter-builder" data-value={value} {...props} />,
    isSimpleQuery: (query: string) => {
        if (!query.trim()) return true;
        return !query.includes(' or ') && !query.includes('>=') && !query.includes('<=');
    },
}));

describe('FilterBar', () => {
    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    describe('rendering', () => {
        it('renders with data-component="filter-bar" attribute', () => {
            render(<FilterBar value="" onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
        });

        it('starts collapsed by default', () => {
            render(<FilterBar value="" onChange={mockOnChange} />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'false');
        });

        it('expands when toggle button is clicked', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('shows "Active" badge when value is non-empty', () => {
            render(<FilterBar value='genre = "Action"' onChange={mockOnChange} />);

            expect(screen.getByText('Active')).toBeInTheDocument();
        });
    });

    describe('mode switch - progressive disclosure', () => {
        it('defaults to simple mode when expanded', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            expect(screen.getByTestId('simple-filter-builder')).toBeInTheDocument();
        });

        it('shows "Switch to advanced editor" link in simple mode', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchLink = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            expect(switchLink).toBeInTheDocument();
            expect(switchLink).toHaveTextContent('Switch to advanced editor');
        });

        it('does NOT render a SegmentedControl / tablist', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
        });

        it('clicking "Switch to advanced editor" shows the advanced editor', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchLink = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            await user.click(switchLink);

            expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();
            expect(screen.queryByTestId('simple-filter-builder')).not.toBeInTheDocument();
        });

        it('in advanced mode, shows "Switch to simple mode" link', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToAdvanced = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            await user.click(switchToAdvanced);

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toBeInTheDocument();
            expect(switchToSimple).toHaveTextContent('Switch to simple mode');
        });

        it('"Switch to simple mode" link is disabled when query cannot be represented in simple mode', async () => {
            const user = userEvent.setup();

            render(<FilterBar value='genre = "Action" or year >= 2020' onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toBeInTheDocument();
            expect(switchToSimple).toBeDisabled();
        });

        it('"Switch to simple mode" link has title text when disabled explaining why', async () => {
            const user = userEvent.setup();
            render(<FilterBar value='genre = "Action" or year >= 2020' onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toHaveAttribute('title', 'Current query cannot be edited in simple mode');
        });

        it('switch to simple mode works when query is simple-compatible', async () => {
            const user = userEvent.setup();
            render(<FilterBar value='genre = "Action"' onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToAdvanced = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            await user.click(switchToAdvanced);

            expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).not.toBeDisabled();
            await user.click(switchToSimple);

            expect(screen.getByTestId('simple-filter-builder')).toBeInTheDocument();
            expect(screen.queryByTestId('filter-query-editor')).not.toBeInTheDocument();
        });
    });

    describe('header actions', () => {
        it('shows saved filters dropdown when savedFilters prop is provided', () => {
            const savedFilters = [
                { id: '1', name: 'Action Movies', query: 'genre = "Action"' },
                { id: '2', name: 'Recent', query: 'year >= 2020' },
            ];

            render(<FilterBar value="" onChange={mockOnChange} savedFilters={savedFilters} />);

            const savedTrigger = document.querySelector('[data-action="apply-saved-filter"]');
            expect(savedTrigger).toBeInTheDocument();
        });

        it('shows clear button when value is non-empty', () => {
            render(<FilterBar value='genre = "Action"' onChange={mockOnChange} />);

            const clearButton = document.querySelector('[data-action="clear-filter"]');
            expect(clearButton).toBeInTheDocument();
        });

        it('shows save button when showSave is true and value is non-empty', () => {
            render(<FilterBar value='genre = "Action"' onChange={mockOnChange} showSave onSave={vi.fn()} />);

            const saveButton = document.querySelector('[data-action="save-filter"]');
            expect(saveButton).toBeInTheDocument();
        });
    });

    describe('expand/collapse', () => {
        it('starts expanded when defaultExpanded is true', () => {
            render(<FilterBar value="" onChange={mockOnChange} defaultExpanded />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('calls onExpandedChange when toggled', async () => {
            const user = userEvent.setup();
            const mockOnExpandedChange = vi.fn();

            render(<FilterBar value="" onChange={mockOnChange} onExpandedChange={mockOnExpandedChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            expect(mockOnExpandedChange).toHaveBeenCalledWith(true);
        });
    });
});
