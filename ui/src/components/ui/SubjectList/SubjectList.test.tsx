import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { SubjectList, type SubjectItem } from './SubjectList';

const mockItems: SubjectItem[] = [
    {
        id: '1',
        title: 'Breaking Bad',
        imageUrl: 'https://example.com/image1.jpg',
        kind: 'show',
        tags: ['Favorites'],
    },
    {
        id: '2',
        title: 'Dune: Part Two',
        imageUrl: 'https://example.com/image2.jpg',
        kind: 'movie',
        tags: ['Watch Later', 'Family'],
    },
    {
        id: '3',
        title: 'Bryan Cranston',
        kind: 'person',
        kindLabel: 'Cast Member',
    },
    {
        id: '4',
        title: 'Abbey Road',
        imageUrl: 'https://example.com/image4.jpg',
        kind: 'album',
        tags: ['Favorites', 'Watch Later', 'Family', 'Extra'],
    },
];

describe('SubjectList', () => {
    it('renders with correct data-component attribute', () => {
        render(<SubjectList items={mockItems} />);

        expect(document.querySelector('[data-component="subject-list"]')).toBeInTheDocument();
    });

    it('renders subject items with titles', () => {
        render(<SubjectList items={mockItems} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
        expect(screen.getByText('Bryan Cranston')).toBeInTheDocument();
        expect(screen.getByText('Abbey Road')).toBeInTheDocument();
    });

    it('keeps a long unbroken title contained with the full title available', () => {
        const longTitle = `Subject${'UnbrokenTitleSegment'.repeat(8)}`;
        render(<SubjectList items={[{ id: 'long-subject', title: longTitle, kind: 'person' }]} />);

        const title = screen.getByText(longTitle);
        expect(title).toHaveClass('subject-list__title');
        expect(title).toHaveAttribute('title', longTitle);
        expect(title.closest('[data-column="title"]')).toBeInTheDocument();
    });

    it('renders kind badges', () => {
        render(<SubjectList items={mockItems} />);

        expect(screen.getByText('Show')).toBeInTheDocument();
        expect(screen.getByText('Movie')).toBeInTheDocument();
        expect(screen.getByText('Cast Member')).toBeInTheDocument();
        expect(screen.getByText('Album')).toBeInTheDocument();
    });

    it('renders tags', () => {
        render(<SubjectList items={mockItems} />);

        expect(screen.getAllByText('Favorites').length).toBeGreaterThan(0);
        expect(screen.getAllByText('Watch Later').length).toBeGreaterThan(0);
        expect(screen.getAllByText('Family').length).toBeGreaterThan(0);
    });

    it('shows +N indicator when more than 3 tags', () => {
        render(<SubjectList items={mockItems} />);

        expect(screen.getByText('+1')).toBeInTheDocument();
    });

    it('shows no tags indicator when no tags', () => {
        render(<SubjectList items={mockItems} />);

        const noTagsLabels = screen.getAllByText('No tags');
        expect(noTagsLabels.length).toBeGreaterThan(0);
    });

    describe('image column', () => {
        it('renders images when imageUrl is provided', () => {
            render(<SubjectList items={mockItems} />);

            const images = screen.getAllByRole('img');
            expect(images.length).toBeGreaterThan(0);
        });

        it('renders placeholder when imageUrl is not provided', () => {
            const itemWithoutImage: SubjectItem[] = [
                {
                    id: 'no-image',
                    title: 'No Image Subject',
                    kind: 'person',
                },
            ];

            render(<SubjectList items={itemWithoutImage} />);

            const placeholder = document.querySelector('.subject-list__image--placeholder');
            expect(placeholder).toBeInTheDocument();
        });

        it('falls back to the placeholder when an image fails to load', () => {
            const item: SubjectItem[] = [
                {
                    id: 'dead-image',
                    title: 'Dead Poster',
                    imageUrl: 'https://example.com/dead.jpg',
                    kind: 'movie',
                },
            ];

            render(<SubjectList items={item} />);

            const img = screen.getByAltText('Dead Poster image');
            fireEvent.error(img);

            expect(screen.queryByAltText('Dead Poster image')).not.toBeInTheDocument();
            expect(document.querySelector('.subject-list__image--placeholder')).toBeInTheDocument();
        });
    });

    describe('click handling', () => {
        it('calls onSubjectClick when row is clicked', async () => {
            const user = userEvent.setup();
            const handleClick = vi.fn();

            render(<SubjectList items={mockItems} onSubjectClick={handleClick} />);

            const row = screen.getByText('Breaking Bad').closest('tr');
            expect(row).toBeInTheDocument();

            await user.click(row!);

            expect(handleClick).toHaveBeenCalledTimes(1);
            expect(handleClick).toHaveBeenCalledWith(mockItems[0]);
        });

        it('does not add click handler when onSubjectClick is not provided', () => {
            render(<SubjectList items={mockItems} />);

            const row = screen.getByText('Breaking Bad').closest('tr');
            expect(row).not.toHaveAttribute('data-clickable');
        });
    });

    describe('empty state', () => {
        it('renders default empty state when no items', () => {
            render(<SubjectList items={[]} />);

            expect(screen.getByText('No matches found')).toBeInTheDocument();
        });

        it('renders custom empty state when provided', () => {
            render(<SubjectList items={[]} emptyState={<div>Custom empty message</div>} />);

            expect(screen.getByText('Custom empty message')).toBeInTheDocument();
        });
    });

    describe('loading state', () => {
        it('shows loading overlay when loading is true', () => {
            render(<SubjectList items={mockItems} loading />);

            const loadingOverlay = document.querySelector('.data-table__loading-overlay');
            expect(loadingOverlay).toBeInTheDocument();
        });
    });

    describe('sorting', () => {
        it('enables sorting by default', () => {
            render(<SubjectList items={mockItems} />);

            const titleHeader = screen.getByRole('button', { name: /sort by title/i });
            expect(titleHeader).toBeInTheDocument();
        });

        it('can disable sorting', () => {
            render(<SubjectList items={mockItems} enableSorting={false} />);

            const titleHeader = screen.queryByRole('button', { name: /sort by title/i });
            expect(titleHeader).not.toBeInTheDocument();
        });
    });

    describe('pagination', () => {
        it('does not show pagination by default', () => {
            render(<SubjectList items={mockItems} />);

            const pagination = document.querySelector('.data-table__pager');
            expect(pagination).not.toBeInTheDocument();
        });

        it('shows pagination when enabled', () => {
            const manyItems: SubjectItem[] = Array.from({ length: 30 }, (_, i) => ({
                id: String(i),
                title: `Subject ${i}`,
                kind: 'movie' as const,
            }));

            render(<SubjectList items={manyItems} enablePagination pageSize={10} />);

            const pagination = document.querySelector('.data-table__pager');
            expect(pagination).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('renders table with grid role', () => {
            render(<SubjectList items={mockItems} />);

            expect(screen.getByRole('grid')).toBeInTheDocument();
        });

        it('has column headers with scope', () => {
            render(<SubjectList items={mockItems} />);

            const headers = screen.getAllByRole('columnheader');
            headers.forEach((header) => {
                expect(header).toHaveAttribute('scope', 'col');
            });
        });

        it('images have alt text', () => {
            render(<SubjectList items={mockItems} />);

            const images = screen.getAllByRole('img');
            images.forEach((img) => {
                expect(img).toHaveAttribute('alt');
                expect(img.getAttribute('alt')).toMatch(/image/i);
            });
        });
    });

    describe('className prop', () => {
        it('applies custom className', () => {
            render(<SubjectList items={mockItems} className="custom-class" />);

            const list = document.querySelector('.subject-list');
            expect(list).toHaveClass('custom-class');
        });
    });
});
