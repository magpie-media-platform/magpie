import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { SubjectFilters, type SubjectFilterState, type TagItem } from './SubjectFilters';

describe('SubjectFilters', () => {
    const defaultFilters: SubjectFilterState = {
        kind: '',
        tags: [],
    };

    const mockTags: TagItem[] = [
        { id: 'tag-1', name: 'Favorites' },
        { id: 'tag-2', name: 'Watch Later' },
        { id: 'tag-3', name: 'Family' },
    ];

    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="subject-filters"]')).toBeInTheDocument();
        });

        it('renders filter title', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            expect(screen.getByText('Filters')).toBeInTheDocument();
        });

        it('renders kind filter dropdown', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            expect(document.querySelector('[data-filter="kind"]')).toBeInTheDocument();
        });

        it('renders tags filter when tags are provided', () => {
            render(<SubjectFilters filters={defaultFilters} availableTags={mockTags} onChange={mockOnChange} />);

            expect(document.querySelector('[data-filter="tags"]')).toBeInTheDocument();
        });

        it('does not render tags filter when no tags are provided', () => {
            render(<SubjectFilters filters={defaultFilters} availableTags={[]} onChange={mockOnChange} />);

            expect(document.querySelector('[data-filter="tags"]')).not.toBeInTheDocument();
        });
    });

    describe('kind filter', () => {
        it('displays "All Kinds" when no kind is selected', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]');
            expect(kindTrigger).toHaveTextContent('All Kinds');
        });

        it('displays selected kind label', () => {
            render(<SubjectFilters filters={{ ...defaultFilters, kind: 'movie' }} onChange={mockOnChange} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]');
            expect(kindTrigger).toHaveTextContent('Movies');
        });

        it('displays "People" for person kind', () => {
            render(<SubjectFilters filters={{ ...defaultFilters, kind: 'person' }} onChange={mockOnChange} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]');
            expect(kindTrigger).toHaveTextContent('People');
        });

        it('calls onChange when kind is selected', async () => {
            const user = userEvent.setup();
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]') as HTMLElement;
            await user.click(kindTrigger);

            const movieOption = await screen.findByRole('option', { name: 'Movies' });
            await user.click(movieOption);

            expect(mockOnChange).toHaveBeenCalledWith(expect.objectContaining({ kind: 'movie' }));
        });

        it('calls onChange with empty kind when "All Kinds" is selected', async () => {
            const user = userEvent.setup();
            render(<SubjectFilters filters={{ ...defaultFilters, kind: 'movie' }} onChange={mockOnChange} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]') as HTMLElement;
            await user.click(kindTrigger);

            const allOption = await screen.findByRole('option', { name: 'All Kinds' });
            await user.click(allOption);

            expect(mockOnChange).toHaveBeenCalledWith(expect.objectContaining({ kind: '' }));
        });
    });

    describe('tags filter', () => {
        it('displays placeholder text when no tags are selected', () => {
            render(<SubjectFilters filters={defaultFilters} availableTags={mockTags} onChange={mockOnChange} />);

            const tagsTrigger = document.querySelector('[data-filter="tags"]');
            expect(tagsTrigger).toHaveTextContent('Tags');
        });

        it('displays selection count when tags are selected', () => {
            render(
                <SubjectFilters
                    filters={{ ...defaultFilters, tags: ['tag-1', 'tag-2'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            const tagsTrigger = document.querySelector('[data-filter="tags"]');
            expect(tagsTrigger).toHaveTextContent('2 selected');
        });

        it('renders selected tags as badges', () => {
            render(
                <SubjectFilters
                    filters={{ ...defaultFilters, tags: ['tag-1'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            expect(screen.getByText('Favorites')).toBeInTheDocument();
        });

        it('calls onChange to add a tag when selected', async () => {
            const user = userEvent.setup();
            render(<SubjectFilters filters={defaultFilters} availableTags={mockTags} onChange={mockOnChange} />);

            const tagsTrigger = document.querySelector('[data-filter="tags"]') as HTMLElement;
            await user.click(tagsTrigger);

            const favoriteOption = await screen.findByRole('option', { name: /Favorites/ });
            await user.click(favoriteOption);

            expect(mockOnChange).toHaveBeenCalledWith(expect.objectContaining({ tags: ['tag-1'] }));
        });

        it('calls onChange to remove a tag when badge remove button is clicked', async () => {
            const user = userEvent.setup();
            render(
                <SubjectFilters
                    filters={{ ...defaultFilters, tags: ['tag-1'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            const removeButton = screen.getByRole('button', { name: 'Remove Favorites filter' });
            await user.click(removeButton);

            expect(mockOnChange).toHaveBeenCalledWith(expect.objectContaining({ tags: [] }));
        });
    });

    describe('active filter count', () => {
        it('does not show count badge when no filters are active', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            const header = document.querySelector('.subject-filters__header');
            expect(within(header!).queryByText('0')).not.toBeInTheDocument();
        });

        it('shows count badge when kind filter is active', () => {
            render(<SubjectFilters filters={{ ...defaultFilters, kind: 'movie' }} onChange={mockOnChange} />);

            expect(screen.getByText('1')).toBeInTheDocument();
        });

        it('includes tags in active filter count', () => {
            render(
                <SubjectFilters
                    filters={{ ...defaultFilters, tags: ['tag-1', 'tag-2'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            expect(screen.getByText('2')).toBeInTheDocument();
        });

        it('counts kind and tags together', () => {
            render(
                <SubjectFilters
                    filters={{ kind: 'show', tags: ['tag-1'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            expect(screen.getByText('2')).toBeInTheDocument();
        });

        it('hides count badge when showActiveCount is false', () => {
            render(
                <SubjectFilters
                    filters={{ ...defaultFilters, kind: 'movie' }}
                    onChange={mockOnChange}
                    showActiveCount={false}
                />,
            );

            const header = document.querySelector('.subject-filters__header');
            expect(within(header!).queryByText('1')).not.toBeInTheDocument();
        });
    });

    describe('clear all button', () => {
        it('does not show clear button when no filters are active', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            expect(screen.queryByText('Clear all')).not.toBeInTheDocument();
        });

        it('shows clear button when filters are active', () => {
            render(<SubjectFilters filters={{ ...defaultFilters, kind: 'movie' }} onChange={mockOnChange} />);

            expect(screen.getByText('Clear all')).toBeInTheDocument();
        });

        it('calls onChange to clear all filters when clicked', async () => {
            const user = userEvent.setup();
            render(
                <SubjectFilters
                    filters={{ kind: 'movie', tags: ['tag-1'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            await user.click(screen.getByText('Clear all'));

            expect(mockOnChange).toHaveBeenCalledWith({
                kind: '',
                tags: [],
            });
        });
    });

    describe('loading state', () => {
        it('disables filter controls when loading', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} loading={true} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]');
            expect(kindTrigger).toHaveAttribute('data-disabled');
        });
    });

    describe('accessibility', () => {
        it('has aria-label on kind filter', () => {
            render(<SubjectFilters filters={defaultFilters} onChange={mockOnChange} />);

            const kindTrigger = document.querySelector('[data-filter="kind"]');
            expect(kindTrigger).toHaveAttribute('aria-label', 'Filter by subject kind');
        });

        it('has aria-label on tags filter', () => {
            render(<SubjectFilters filters={defaultFilters} availableTags={mockTags} onChange={mockOnChange} />);

            const tagsTrigger = document.querySelector('[data-filter="tags"]');
            expect(tagsTrigger).toHaveAttribute('aria-label', 'Filter by tags');
        });

        it('has aria-label on tag remove buttons', () => {
            render(
                <SubjectFilters
                    filters={{ ...defaultFilters, tags: ['tag-1'] }}
                    availableTags={mockTags}
                    onChange={mockOnChange}
                />,
            );

            expect(screen.getByRole('button', { name: 'Remove Favorites filter' })).toBeInTheDocument();
        });

        it('has data-action on clear button', () => {
            render(<SubjectFilters filters={{ ...defaultFilters, kind: 'movie' }} onChange={mockOnChange} />);

            expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
        });
    });
});
