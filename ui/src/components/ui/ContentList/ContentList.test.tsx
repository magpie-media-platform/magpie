import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ContentList, type ContentItem } from './ContentList';

const mockItems: ContentItem[] = [
    {
        id: '1',
        title: 'Breaking Bad',
        posterUrl: 'https://example.com/poster1.jpg',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        year: 2008,
        dateAdded: '2024-01-15T10:00:00Z',
    },
    {
        id: '2',
        title: 'Dune: Part Two',
        posterUrl: 'https://example.com/poster2.jpg',
        kind: 'movie',
        monitoring: 'excluded',
        fulfillment: 'missing',
        year: 2024,
        dateAdded: '2024-06-01T12:00:00Z',
    },
    {
        id: '3',
        title: 'The Office',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'upgradable',
        year: 2005,
        dateAdded: '2023-03-01T08:00:00Z',
    },
    {
        id: '4',
        title: 'Poor Things',
        posterUrl: 'https://example.com/poster4.jpg',
        kind: 'movie',
        monitoring: 'excluded',
        fulfillment: 'fulfilledExternally',
        year: 2023,
    },
];

describe('ContentList', () => {
    it('renders with correct data-component attribute', () => {
        render(<ContentList items={mockItems} />);

        expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();
    });

    it('renders content items with titles', () => {
        render(<ContentList items={mockItems} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
        expect(screen.getByText('The Office')).toBeInTheDocument();
        expect(screen.getByText('Poor Things')).toBeInTheDocument();
    });

    it('renders year when provided', () => {
        render(<ContentList items={mockItems} />);

        expect(screen.getByText('2008')).toBeInTheDocument();
        expect(screen.getByText('2024')).toBeInTheDocument();
        expect(screen.getByText('2005')).toBeInTheDocument();
        expect(screen.getByText('2023')).toBeInTheDocument();
    });

    it('renders year 0 instead of suppressing it', () => {
        const items: ContentItem[] = [
            {
                id: 'zero-year',
                title: 'Zero Year Item',
                kind: 'movie',
                monitoring: 'active',
                fulfillment: 'missing',
                year: 0,
            },
        ];

        render(<ContentList items={items} />);

        expect(screen.getByText('0')).toBeInTheDocument();
    });

    it('does not render year span when year is undefined', () => {
        const items: ContentItem[] = [
            {
                id: 'no-year',
                title: 'No Year Item',
                kind: 'movie',
                monitoring: 'active',
                fulfillment: 'missing',
            },
        ];

        render(<ContentList items={items} />);

        const yearSpan = document.querySelector('.content-list__year');
        expect(yearSpan).not.toBeInTheDocument();
    });

    it('renders kind badges', () => {
        render(<ContentList items={mockItems} />);

        const episodeBadges = screen.getAllByText('Episode');
        const movieBadges = screen.getAllByText('Movie');

        expect(episodeBadges).toHaveLength(2);
        expect(movieBadges).toHaveLength(2);
    });

    it('renders fulfillment status badges', () => {
        render(<ContentList items={mockItems} />);

        expect(screen.getByText('Fulfilled')).toBeInTheDocument();
        expect(screen.getByText('Missing')).toBeInTheDocument();
        expect(screen.getByText('Upgradable')).toBeInTheDocument();
        expect(screen.getByText('External')).toBeInTheDocument();
    });

    it('renders monitoring status badges', () => {
        render(<ContentList items={mockItems} />);

        const monitoredBadges = screen.getAllByText('Monitored');
        const excludedBadges = screen.getAllByText('Excluded');

        expect(monitoredBadges).toHaveLength(2);
        expect(excludedBadges).toHaveLength(2);
    });

    describe('poster column', () => {
        it('renders poster images when posterUrl is provided', () => {
            render(<ContentList items={mockItems} />);

            const posterImages = screen.getAllByRole('img');
            expect(posterImages.length).toBeGreaterThan(0);
        });

        it('renders placeholder when posterUrl is not provided', () => {
            const itemWithoutPoster: ContentItem[] = [
                {
                    id: 'no-poster',
                    title: 'No Poster Show',
                    kind: 'episode',
                    monitoring: 'active',
                    fulfillment: 'missing',
                },
            ];

            render(<ContentList items={itemWithoutPoster} />);

            const placeholder = document.querySelector('.content-list__poster--placeholder');
            expect(placeholder).toBeInTheDocument();
        });
    });

    describe('click handling', () => {
        it('calls onContentClick when row is clicked', async () => {
            const user = userEvent.setup();
            const handleClick = vi.fn();

            render(<ContentList items={mockItems} onContentClick={handleClick} />);

            const row = screen.getByText('Breaking Bad').closest('tr');
            expect(row).toBeInTheDocument();

            await user.click(row!);

            expect(handleClick).toHaveBeenCalledTimes(1);
            expect(handleClick).toHaveBeenCalledWith(mockItems[0]);
        });

        it('does not add click handler when onContentClick is not provided', () => {
            render(<ContentList items={mockItems} />);

            const row = screen.getByText('Breaking Bad').closest('tr');
            expect(row).not.toHaveAttribute('data-clickable');
        });
    });

    describe('empty state', () => {
        it('renders default empty state when no items', () => {
            render(<ContentList items={[]} />);

            expect(screen.getByText('No content found')).toBeInTheDocument();
        });

        it('renders custom empty state when provided', () => {
            render(<ContentList items={[]} emptyState={<div>Custom empty message</div>} />);

            expect(screen.getByText('Custom empty message')).toBeInTheDocument();
        });
    });

    describe('loading state', () => {
        it('shows loading overlay when loading is true', () => {
            render(<ContentList items={mockItems} loading />);

            const loadingOverlay = document.querySelector('.data-table__loading-overlay');
            expect(loadingOverlay).toBeInTheDocument();
        });
    });

    describe('sorting', () => {
        it('enables sorting by default', () => {
            render(<ContentList items={mockItems} />);

            const titleHeader = screen.getByRole('button', { name: /sort by title/i });
            expect(titleHeader).toBeInTheDocument();
        });

        it('can disable sorting', () => {
            render(<ContentList items={mockItems} enableSorting={false} />);

            const titleHeader = screen.queryByRole('button', { name: /sort by title/i });
            expect(titleHeader).not.toBeInTheDocument();
        });
    });

    describe('pagination', () => {
        it('does not show pagination by default', () => {
            render(<ContentList items={mockItems} />);

            const pagination = document.querySelector('.data-table__pagination');
            expect(pagination).not.toBeInTheDocument();
        });

        it('shows pagination when enabled', () => {
            const manyItems: ContentItem[] = Array.from({ length: 30 }, (_, i) => ({
                id: String(i),
                title: `Item ${i}`,
                kind: 'movie' as const,
                monitoring: 'active' as const,
                fulfillment: 'fulfilled' as const,
            }));

            render(<ContentList items={manyItems} enablePagination pageSize={10} />);

            const pagination = document.querySelector('.data-table__pagination');
            expect(pagination).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('renders table with grid role', () => {
            render(<ContentList items={mockItems} />);

            expect(screen.getByRole('grid')).toBeInTheDocument();
        });

        it('has column headers with scope', () => {
            render(<ContentList items={mockItems} />);

            const headers = screen.getAllByRole('columnheader');
            headers.forEach((header) => {
                expect(header).toHaveAttribute('scope', 'col');
            });
        });

        it('poster images have alt text', () => {
            render(<ContentList items={mockItems} />);

            const images = screen.getAllByRole('img');
            images.forEach((img) => {
                expect(img).toHaveAttribute('alt');
                expect(img.getAttribute('alt')).toMatch(/poster/i);
            });
        });
    });

    describe('className prop', () => {
        it('applies custom className', () => {
            render(<ContentList items={mockItems} className="custom-class" />);

            const list = document.querySelector('.content-list');
            expect(list).toHaveClass('custom-class');
        });
    });

    describe('multi-select', () => {
        it('does not show checkboxes when enableSelection is false (default)', () => {
            render(<ContentList items={mockItems} />);

            const checkboxes = screen.queryAllByRole('checkbox');
            expect(checkboxes).toHaveLength(0);
        });

        it('shows checkboxes when enableSelection is true', () => {
            render(<ContentList items={mockItems} enableSelection />);

            const checkboxes = screen.getAllByRole('checkbox');
            expect(checkboxes).toHaveLength(mockItems.length + 1);
        });

        it('sets data-selection-enabled attribute when selection is enabled', () => {
            render(<ContentList items={mockItems} enableSelection />);

            const list = document.querySelector('[data-selection-enabled="true"]');
            expect(list).toBeInTheDocument();
        });

        it('does not set data-selection-enabled attribute when selection is disabled', () => {
            render(<ContentList items={mockItems} />);

            const list = document.querySelector('[data-selection-enabled]');
            expect(list).not.toBeInTheDocument();
        });

        it('calls onRowSelectionChange when a row is selected', async () => {
            const user = userEvent.setup();
            const handleSelectionChange = vi.fn();

            render(
                <ContentList
                    items={mockItems}
                    enableSelection
                    rowSelection={{}}
                    onRowSelectionChange={handleSelectionChange}
                />,
            );

            const checkboxes = screen.getAllByRole('checkbox');
            await user.click(checkboxes[1]);

            expect(handleSelectionChange).toHaveBeenCalled();
        });

        it('shows select-all checkbox in header', () => {
            render(<ContentList items={mockItems} enableSelection />);

            const selectAllCheckbox = screen.getByLabelText('Select all');
            expect(selectAllCheckbox).toBeInTheDocument();
        });

        it('selects all rows when select-all is clicked', async () => {
            const user = userEvent.setup();
            const handleSelectionChange = vi.fn();

            render(
                <ContentList
                    items={mockItems}
                    enableSelection
                    rowSelection={{}}
                    onRowSelectionChange={handleSelectionChange}
                />,
            );

            const selectAllCheckbox = screen.getByLabelText('Select all');
            await user.click(selectAllCheckbox);

            expect(handleSelectionChange).toHaveBeenCalled();
        });

        it('checkbox has accessible label with item title', () => {
            render(<ContentList items={mockItems} enableSelection />);

            expect(screen.getByLabelText('Select Breaking Bad')).toBeInTheDocument();
            expect(screen.getByLabelText('Select Dune: Part Two')).toBeInTheDocument();
            expect(screen.getByLabelText('Select The Office')).toBeInTheDocument();
            expect(screen.getByLabelText('Select Poor Things')).toBeInTheDocument();
        });

        it('reflects controlled selection state', () => {
            render(
                <ContentList
                    items={mockItems}
                    enableSelection
                    rowSelection={{ '1': true, '3': true }}
                    onRowSelectionChange={vi.fn()}
                />,
            );

            const breakingBadCheckbox = screen.getByLabelText('Select Breaking Bad');
            const officeCheckbox = screen.getByLabelText('Select The Office');
            const duneCheckbox = screen.getByLabelText('Select Dune: Part Two');

            expect(breakingBadCheckbox).toBeChecked();
            expect(officeCheckbox).toBeChecked();
            expect(duneCheckbox).not.toBeChecked();
        });

        it('checkbox click does not trigger row click handler', async () => {
            const user = userEvent.setup();
            const handleClick = vi.fn();
            const handleSelectionChange = vi.fn();

            render(
                <ContentList
                    items={mockItems}
                    enableSelection
                    rowSelection={{}}
                    onRowSelectionChange={handleSelectionChange}
                    onContentClick={handleClick}
                />,
            );

            const checkbox = screen.getByLabelText('Select Breaking Bad');
            await user.click(checkbox);

            expect(handleSelectionChange).toHaveBeenCalled();
            expect(handleClick).not.toHaveBeenCalled();
        });
    });

    describe('date added column', () => {
        it('renders Added column header', () => {
            render(<ContentList items={mockItems} />);
            const header = screen.getByText('Added');
            expect(header).toBeInTheDocument();
        });
    });
});
