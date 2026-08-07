import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

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
        loading: _loading,
        defaultContentType: _defaultContentType,
        defaultSubjectKind: _defaultSubjectKind,
        lockSubjectKind: _lockSubjectKind,
        ...props
    }: {
        value: string;
        onChange: (v: string) => void;
        [key: string]: unknown;
    }) => <div data-testid="simple-filter-builder" data-value={value} {...props} />,
}));

vi.mock('@/lib/simpleFilterQuery', () => ({
    isSimpleQuery: (query: string) => {
        if (!query.trim()) return true;
        return !query.includes(' or ') && !query.includes('>=') && !query.includes('<=');
    },
}));

function createMatchMediaController(initialWidth: number) {
    let width = initialWidth;
    const listeners = new Set<(event: MediaQueryListEvent) => void>();
    const matches = (query: string) => {
        const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
        return minWidth !== null && width >= Number(minWidth[1]);
    };
    const matchMedia = vi.fn((query: string) => ({
        get matches() {
            return matches(query);
        },
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => listeners.add(listener),
        removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) =>
            listeners.delete(listener),
        dispatchEvent: () => false,
    }));

    return {
        matchMedia,
        setWidth(nextWidth: number) {
            width = nextWidth;
            const event = {
                matches: matches('(min-width: 1024px)'),
                media: '(min-width: 1024px)',
            } as MediaQueryListEvent;
            listeners.forEach((listener) => listener(event));
        },
    };
}

describe('FilterBar', () => {
    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
        vi.stubGlobal('matchMedia', createMatchMediaController(390).matchMedia);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    describe('rendering', () => {
        it('renders with data-component="filter-bar" attribute', () => {
            render(<FilterBar value="" onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="filter-bar"]')).toBeInTheDocument();
        });

        it('starts collapsed by default in the card layout', () => {
            render(<FilterBar value="" onChange={mockOnChange} tableLayoutQuery="(min-width: 1024px)" />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'false');
        });

        it('starts expanded by default in the library table layout', () => {
            vi.stubGlobal('matchMedia', createMatchMediaController(1280).matchMedia);

            render(<FilterBar value="" onChange={mockOnChange} tableLayoutQuery="(min-width: 1024px)" />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
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

        it('shows "Advanced filter" control in simple mode', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchControl = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            expect(switchControl).toBeInTheDocument();
            expect(switchControl).toHaveTextContent('Advanced filter');
        });

        it('renders the mode toggle in the header actions for both modes, never inside the builder', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const builder = screen.getByTestId('simple-filter-builder');
            const switchToAdvanced = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            expect(builder).not.toContainElement(switchToAdvanced);
            expect(switchToAdvanced.closest('.filter-bar__actions')).not.toBeNull();
            expect(switchToAdvanced.closest('.filter-bar__content')).toBeNull();

            await user.click(switchToAdvanced);
            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple.closest('.filter-bar__actions')).not.toBeNull();
        });

        it('renders the mode toggle as a real button affordance, not a plain link', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchControl = document.querySelector('[data-action="switch-to-advanced"]') as HTMLButtonElement;
            expect(switchControl.tagName).toBe('BUTTON');
            expect(switchControl).toHaveClass('button', 'button--ghost', 'button--sm');
            expect(switchControl).toHaveTextContent('Advanced filter');
        });

        it('the mode switch flips state on click (round trip)', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            await user.click(document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement);
            expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLButtonElement;
            expect(switchToSimple.tagName).toBe('BUTTON');
            expect(switchToSimple).toHaveClass('button', 'button--ghost', 'button--sm');
            expect(switchToSimple).toHaveTextContent('Simple filter');

            await user.click(switchToSimple);
            expect(screen.queryByTestId('filter-query-editor')).not.toBeInTheDocument();
            expect(document.querySelector('[data-action="switch-to-advanced"]')).toBeInTheDocument();
        });

        it('does NOT render a tablist', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
        });

        it('clicking "Advanced filter" shows the advanced editor', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchLink = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            await user.click(switchLink);

            expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();
            expect(screen.queryByTestId('simple-filter-builder')).not.toBeInTheDocument();
        });

        it('in advanced mode, shows "Simple filter" link', async () => {
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToAdvanced = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
            await user.click(switchToAdvanced);

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toBeInTheDocument();
            expect(switchToSimple).toHaveTextContent('Simple filter');
        });

        it('"Simple filter" link is disabled when query cannot be represented in simple mode', async () => {
            const user = userEvent.setup();

            render(<FilterBar value='genre = "Action" or year >= 2020' onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toBeInTheDocument();
            expect(switchToSimple).toBeDisabled();
        });

        it('"Simple filter" link has title text when disabled explaining why', async () => {
            const user = userEvent.setup();
            render(<FilterBar value='genre = "Action" or year >= 2020' onChange={mockOnChange} />);

            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toHaveAttribute('title', 'This query cannot be edited in simple mode');
        });

        it('renders advanced mode when the query cannot be represented in simple mode', () => {
            render(<FilterBar value='genre = "Action" or year >= 2020' onChange={mockOnChange} defaultExpanded />);

            expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();
            expect(screen.queryByTestId('simple-filter-builder')).not.toBeInTheDocument();

            const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
            expect(switchToSimple).toBeInTheDocument();
            expect(switchToSimple).toBeDisabled();
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

        it('updates the responsive default when the layout changes before interaction', () => {
            const viewport = createMatchMediaController(390);
            vi.stubGlobal('matchMedia', viewport.matchMedia);
            render(<FilterBar value="" onChange={mockOnChange} tableLayoutQuery="(min-width: 1024px)" />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'false');

            act(() => viewport.setWidth(1280));

            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });

        it('preserves a manual choice when the layout changes', async () => {
            const viewport = createMatchMediaController(1280);
            vi.stubGlobal('matchMedia', viewport.matchMedia);
            const user = userEvent.setup();
            render(<FilterBar value="" onChange={mockOnChange} tableLayoutQuery="(min-width: 1024px)" />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            const toggle = document.querySelector('[data-action="toggle-filter"]') as HTMLElement;
            await user.click(toggle);
            expect(filterBar).toHaveAttribute('data-expanded', 'false');

            act(() => viewport.setWidth(390));
            act(() => viewport.setWidth(1280));

            expect(filterBar).toHaveAttribute('data-expanded', 'false');
        });
    });

    describe('non-collapsible (live filter) context', () => {
        it('renders no collapse toggle when collapsible is false', () => {
            render(<FilterBar value="" onChange={mockOnChange} collapsible={false} />);

            expect(document.querySelector('[data-action="toggle-filter"]')).not.toBeInTheDocument();
        });

        it('renders a static heading in place of the toggle', () => {
            render(<FilterBar value="" onChange={mockOnChange} collapsible={false} />);

            const heading = document.querySelector('[data-component="filter-bar-heading"]');
            expect(heading).toBeInTheDocument();
            expect(heading).toHaveTextContent('Filters');
        });

        it('stays expanded with the panel always rendered', () => {
            render(<FilterBar value="" onChange={mockOnChange} collapsible={false} />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
            expect(screen.getByTestId('simple-filter-builder')).toBeInTheDocument();
        });

        it('cannot be collapsed even when an expanded value is passed', () => {
            render(<FilterBar value="" onChange={mockOnChange} collapsible={false} expanded={false} />);

            const filterBar = document.querySelector('[data-component="filter-bar"]');
            expect(filterBar).toHaveAttribute('data-expanded', 'true');
        });
    });
});
