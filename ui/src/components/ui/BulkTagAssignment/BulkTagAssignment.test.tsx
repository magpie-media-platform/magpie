import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { BulkTagAssignment, type BulkTagItem, type ContentWithTags } from './BulkTagAssignment';

const mockTags: BulkTagItem[] = [
    { id: 'tag-1', name: 'Favorites', color: '#f7768e' },
    { id: 'tag-2', name: 'Watch Later', color: '#7aa2f7' },
    { id: 'tag-3', name: 'Family', color: '#9ece6a' },
];

const mockSelectedItems: ContentWithTags[] = [
    { id: 'content-1', title: 'Item 1', tags: ['tag-1'] },
    { id: 'content-2', title: 'Item 2', tags: ['tag-2'] },
    { id: 'content-3', title: 'Item 3', tags: [] },
];

describe('BulkTagAssignment', () => {
    const defaultProps = {
        open: true,
        onOpenChange: vi.fn(),
        availableTags: mockTags,
        selectedItems: mockSelectedItems,
        onApply: vi.fn(),
    };

    beforeEach(() => {
        vi.clearAllMocks();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(document.querySelector('[data-component="bulk-tag-assignment"]')).toBeInTheDocument();
        });

        it('renders title "Assign Tags"', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByText('Assign Tags')).toBeInTheDocument();
        });

        it('renders description with item count for multiple items', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByText(/Modify tags for 3 selected items/)).toBeInTheDocument();
        });

        it('renders description with item title for single item', () => {
            render(<BulkTagAssignment {...defaultProps} selectedItems={[{ id: '1', title: 'Test Item', tags: [] }]} />);

            expect(document.querySelector('.sheet-description')).toHaveTextContent(/^Modify tags for "Test Item"$/);
        });

        it('renders all available tags', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByText('Favorites')).toBeInTheDocument();
            expect(screen.getByText('Watch Later')).toBeInTheDocument();
            expect(screen.getByText('Family')).toBeInTheDocument();
        });

        it('renders empty state when no tags available', () => {
            render(<BulkTagAssignment {...defaultProps} availableTags={[]} />);

            expect(screen.getByText('No tags available')).toBeInTheDocument();
            expect(screen.getByText("Add tags from an item's detail page to use them here")).toBeInTheDocument();
        });

        it('renders Cancel and Apply buttons', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByRole('button', { name: /Cancel/ })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /Apply/ })).toBeInTheDocument();
        });
    });

    describe('tag states', () => {
        it('shows tag as checked when all selected items have it', () => {
            const items: ContentWithTags[] = [
                { id: '1', title: 'Item 1', tags: ['tag-1'] },
                { id: '2', title: 'Item 2', tags: ['tag-1'] },
            ];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            const favoritesButton = screen.getByRole('button', { name: /Favorites/i });
            expect(favoritesButton).toHaveAttribute('aria-pressed', 'true');
        });

        it('shows tag as unchecked when no selected items have it', () => {
            const items: ContentWithTags[] = [
                { id: '1', title: 'Item 1', tags: [] },
                { id: '2', title: 'Item 2', tags: [] },
            ];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            const favoritesButton = screen.getByRole('button', { name: /Favorites/i });
            expect(favoritesButton).toHaveAttribute('aria-pressed', 'false');
        });

        it('shows indeterminate state when some items have the tag', () => {
            const items: ContentWithTags[] = [
                { id: '1', title: 'Item 1', tags: ['tag-1'] },
                { id: '2', title: 'Item 2', tags: [] },
            ];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            const favoritesButton = screen.getByRole('button', { name: /Favorites/i });
            expect(favoritesButton).toHaveAttribute('aria-pressed', 'false');
        });
    });

    describe('tag interactions', () => {
        it('toggles tag to "add" when clicking an unchecked tag', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: [] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            const favoritesButton = screen.getByRole('button', { name: /Favorites/i });
            await user.click(favoritesButton);

            expect(screen.getByText('Add')).toBeInTheDocument();
            expect(favoritesButton).toHaveAttribute('aria-pressed', 'true');
        });

        it('toggles tag to "remove" when clicking a checked tag', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: ['tag-1'] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            const favoritesButton = screen.getByRole('button', { name: /Favorites/i });
            await user.click(favoritesButton);

            expect(screen.getByText('Remove')).toBeInTheDocument();
            expect(favoritesButton).toHaveAttribute('aria-pressed', 'false');
        });

        it('cycles through states: add -> remove -> unchanged', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: [] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            const favoritesButton = screen.getByRole('button', { name: /Favorites/i });

            expect(favoritesButton).toHaveAttribute('aria-pressed', 'false');

            await user.click(favoritesButton);
            expect(screen.getByText('Add')).toBeInTheDocument();

            await user.click(favoritesButton);
            expect(screen.getByText('Remove')).toBeInTheDocument();

            await user.click(favoritesButton);
            expect(screen.queryByText('Add')).not.toBeInTheDocument();
            expect(screen.queryByText('Remove')).not.toBeInTheDocument();
        });
    });

    describe('changes summary', () => {
        it('does not show summary when no changes', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.queryByText('Changes:')).not.toBeInTheDocument();
        });

        it('shows summary when tags to add', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: [] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            await user.click(screen.getByRole('button', { name: /Favorites/i }));

            expect(screen.getByText('Changes:')).toBeInTheDocument();
            expect(screen.getByText(/1 tag to add/)).toBeInTheDocument();
        });

        it('shows summary when tags to remove', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: ['tag-1'] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            await user.click(screen.getByRole('button', { name: /Favorites/i }));

            expect(screen.getByText('Changes:')).toBeInTheDocument();
            expect(screen.getByText(/1 tag to remove/)).toBeInTheDocument();
        });

        it('shows summary with both adds and removes', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: ['tag-1'] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            await user.click(screen.getByRole('button', { name: /Watch Later/i }));

            await user.click(screen.getByRole('button', { name: /Favorites/i }));

            expect(screen.getByText('Changes:')).toBeInTheDocument();
            expect(screen.getByText(/1 tag to add/)).toBeInTheDocument();
            expect(screen.getByText(/1 tag to remove/)).toBeInTheDocument();
        });
    });

    describe('apply button', () => {
        it('is disabled when no changes', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByRole('button', { name: /Apply/ })).toBeDisabled();
        });

        it('is enabled when there are changes', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: [] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            await user.click(screen.getByRole('button', { name: /Favorites/i }));

            expect(screen.getByRole('button', { name: /Apply/ })).toBeEnabled();
        });

        it('calls onApply with correct result when clicked', async () => {
            const user = userEvent.setup();
            const onApply = vi.fn();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: ['tag-1'] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} onApply={onApply} />);

            await user.click(screen.getByRole('button', { name: /Watch Later/i }));

            await user.click(screen.getByRole('button', { name: /Favorites/i }));

            await user.click(screen.getByRole('button', { name: /Apply/ }));

            expect(onApply).toHaveBeenCalledWith({
                tagsToAdd: ['tag-2'],
                tagsToRemove: ['tag-1'],
            });
        });

        it('shows "Applying..." when isApplying is true', () => {
            render(<BulkTagAssignment {...defaultProps} isApplying />);

            expect(screen.getByRole('button', { name: /Applying/ })).toBeInTheDocument();
        });

        it('is disabled when isApplying is true', () => {
            render(<BulkTagAssignment {...defaultProps} isApplying />);

            expect(screen.getByRole('button', { name: /Applying/ })).toBeDisabled();
        });
    });

    describe('cancel button', () => {
        it('calls onOpenChange with false when clicked', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(<BulkTagAssignment {...defaultProps} onOpenChange={onOpenChange} />);

            await user.click(screen.getByRole('button', { name: /Cancel/ }));

            await waitFor(() => {
                expect(onOpenChange).toHaveBeenCalledWith(false);
            });
        });

        it('is disabled when isApplying is true', () => {
            render(<BulkTagAssignment {...defaultProps} isApplying />);

            expect(screen.getByRole('button', { name: /Cancel/ })).toBeDisabled();
        });
    });

    describe('accessibility', () => {
        it('has proper role for tag group', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByRole('group', { name: 'Available tags' })).toBeInTheDocument();
        });

        it('tag buttons have proper aria-pressed state', () => {
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: ['tag-1'] }];

            render(<BulkTagAssignment {...defaultProps} selectedItems={items} />);

            expect(screen.getByRole('button', { name: /Favorites/i })).toHaveAttribute('aria-pressed', 'true');

            expect(screen.getByRole('button', { name: /Watch Later/i })).toHaveAttribute('aria-pressed', 'false');
        });

        it('disables all tag buttons when isApplying', () => {
            render(<BulkTagAssignment {...defaultProps} isApplying />);

            const tagButtons = screen.getAllByRole('button').filter((btn) => btn.getAttribute('data-tag-id'));

            tagButtons.forEach((button) => {
                expect(button).toBeDisabled();
            });
        });
    });

    describe('search', () => {
        it('renders search input when tags are available', () => {
            render(<BulkTagAssignment {...defaultProps} />);

            expect(screen.getByPlaceholderText('Search tags...')).toBeInTheDocument();
        });

        it('does not render search input when no tags available', () => {
            render(<BulkTagAssignment {...defaultProps} availableTags={[]} />);

            expect(screen.queryByPlaceholderText('Search tags...')).not.toBeInTheDocument();
        });

        it('filters tags by search query', async () => {
            const user = userEvent.setup();
            render(<BulkTagAssignment {...defaultProps} />);

            await user.type(screen.getByPlaceholderText('Search tags...'), 'fav');

            expect(screen.getByText('Favorites')).toBeInTheDocument();
            expect(screen.queryByText('Watch Later')).not.toBeInTheDocument();
            expect(screen.queryByText('Family')).not.toBeInTheDocument();
        });

        it('shows no matching tags message when search has no results', async () => {
            const user = userEvent.setup();
            render(<BulkTagAssignment {...defaultProps} />);

            await user.type(screen.getByPlaceholderText('Search tags...'), 'xyz');

            expect(screen.getByText('No matching tags')).toBeInTheDocument();
            expect(screen.getByText('Try a different search term')).toBeInTheDocument();
        });

        it('clears search when clear button is clicked', async () => {
            const user = userEvent.setup();
            render(<BulkTagAssignment {...defaultProps} />);

            const searchInput = screen.getByPlaceholderText('Search tags...');
            await user.type(searchInput, 'fav');

            expect(screen.queryByText('Watch Later')).not.toBeInTheDocument();

            await user.click(screen.getByLabelText('Clear search'));

            expect(screen.getByText('Favorites')).toBeInTheDocument();
            expect(screen.getByText('Watch Later')).toBeInTheDocument();
            expect(screen.getByText('Family')).toBeInTheDocument();
        });

        it('is case insensitive', async () => {
            const user = userEvent.setup();
            render(<BulkTagAssignment {...defaultProps} />);

            await user.type(screen.getByPlaceholderText('Search tags...'), 'FAVORITES');

            expect(screen.getByText('Favorites')).toBeInTheDocument();
        });

        it('resets search when sheet reopens', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: [] }];

            const { rerender } = render(<BulkTagAssignment {...defaultProps} selectedItems={items} open={true} />);

            await user.type(screen.getByPlaceholderText('Search tags...'), 'fav');
            expect(screen.queryByText('Watch Later')).not.toBeInTheDocument();

            rerender(<BulkTagAssignment {...defaultProps} selectedItems={items} open={false} />);

            rerender(<BulkTagAssignment {...defaultProps} selectedItems={items} open={true} />);

            expect(screen.getByPlaceholderText('Search tags...')).toHaveValue('');
            expect(screen.getByText('Favorites')).toBeInTheDocument();
            expect(screen.getByText('Watch Later')).toBeInTheDocument();
        });
    });

    describe('state reset', () => {
        it('resets state when sheet opens', async () => {
            const user = userEvent.setup();
            const items: ContentWithTags[] = [{ id: '1', title: 'Item 1', tags: [] }];

            const { rerender } = render(<BulkTagAssignment {...defaultProps} selectedItems={items} open={true} />);

            await user.click(screen.getByRole('button', { name: /Favorites/i }));
            expect(screen.getByText('Add')).toBeInTheDocument();

            rerender(<BulkTagAssignment {...defaultProps} selectedItems={items} open={false} />);

            rerender(<BulkTagAssignment {...defaultProps} selectedItems={items} open={true} />);

            expect(screen.queryByText('Add')).not.toBeInTheDocument();
        });

        it('keeps pending choices and the search when the selection refetches while open', async () => {
            // Arrange
            const user = userEvent.setup();
            const onApply = vi.fn();
            const { rerender } = render(<BulkTagAssignment {...defaultProps} onApply={onApply} />);
            await user.click(screen.getByRole('button', { name: /Favorites/i }));
            await user.click(screen.getByRole('button', { name: /Watch Later/i }));
            await user.click(screen.getByRole('button', { name: /Watch Later/i }));
            await user.type(screen.getByPlaceholderText('Search tags...'), 'fav');

            // Act
            rerender(
                <BulkTagAssignment
                    {...defaultProps}
                    onApply={onApply}
                    availableTags={[...mockTags]}
                    selectedItems={[...mockSelectedItems]}
                />,
            );
            await user.click(screen.getByRole('button', { name: /Apply/ }));

            // Assert
            expect(screen.getByPlaceholderText('Search tags...')).toHaveValue('fav');
            expect(onApply).toHaveBeenCalledWith({ tagsToAdd: ['tag-1'], tagsToRemove: ['tag-2'] });
        });
    });
});
