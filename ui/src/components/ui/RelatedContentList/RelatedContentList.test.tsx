import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { RelatedContentList, type RelatedContentItem } from './RelatedContentList';

function renderWithRouter(items: RelatedContentItem[], loading = false) {
    return render(
        <MemoryRouter>
            <RelatedContentList items={items} loading={loading} />
        </MemoryRouter>,
    );
}

const mockItems: RelatedContentItem[] = [
    {
        id: 'content-1',
        title: 'Breaking Bad',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'fulfilled',
        posterUrl: 'https://example.com/bb.jpg',
        year: 2008,
    },
    {
        id: 'content-2',
        title: 'El Camino',
        kind: 'movie',
        monitoring: 'excluded',
        fulfillment: 'missing',
        year: 2019,
    },
    {
        id: 'content-3',
        title: 'Better Call Saul',
        kind: 'episode',
        monitoring: 'active',
        fulfillment: 'upgradable',
        posterUrl: 'https://example.com/bcs.jpg',
    },
];

describe('RelatedContentList', () => {
    describe('rendering', () => {
        it('renders with data-component attribute', () => {
            renderWithRouter(mockItems);
            expect(document.querySelector('[data-component="related-content-list"]')).toBeInTheDocument();
        });

        it('renders all content items', () => {
            renderWithRouter(mockItems);
            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
            expect(screen.getByText('El Camino')).toBeInTheDocument();
            expect(screen.getByText('Better Call Saul')).toBeInTheDocument();
        });

        it('renders poster images when provided', () => {
            renderWithRouter(mockItems);
            expect(screen.getByAltText('Breaking Bad poster')).toBeInTheDocument();
            expect(screen.getByAltText('Better Call Saul poster')).toBeInTheDocument();
        });

        it('renders placeholder when poster is missing', () => {
            renderWithRouter(mockItems);
            const placeholders = document.querySelectorAll('.related-content-list__poster--placeholder');
            expect(placeholders.length).toBe(1);
        });

        it('renders year when provided', () => {
            renderWithRouter(mockItems);
            expect(screen.getByText('2008')).toBeInTheDocument();
            expect(screen.getByText('2019')).toBeInTheDocument();
        });
    });

    describe('title links', () => {
        it('renders title as link to library detail page', () => {
            renderWithRouter(mockItems);
            const link = screen.getByRole('link', { name: 'Breaking Bad' });
            expect(link).toHaveAttribute('href', '/library/content-1');
        });

        it('renders all title links with correct paths', () => {
            renderWithRouter(mockItems);
            expect(screen.getByRole('link', { name: 'Breaking Bad' })).toHaveAttribute('href', '/library/content-1');
            expect(screen.getByRole('link', { name: 'El Camino' })).toHaveAttribute('href', '/library/content-2');
            expect(screen.getByRole('link', { name: 'Better Call Saul' })).toHaveAttribute(
                'href',
                '/library/content-3',
            );
        });
    });

    describe('kind badges', () => {
        it('renders kind badges for each item', () => {
            renderWithRouter(mockItems);

            const episodeBadges = screen.getAllByText('Episode');
            expect(episodeBadges.length).toBe(2);
            expect(screen.getByText('Movie')).toBeInTheDocument();
        });
    });

    describe('monitoring status', () => {
        it('renders monitored status with eye icon', () => {
            renderWithRouter(mockItems);
            const monitoredIcons = screen.getAllByLabelText('Monitored');
            expect(monitoredIcons.length).toBe(2);
        });

        it('renders unmonitored status with eye-slash icon', () => {
            renderWithRouter(mockItems);
            expect(screen.getByLabelText('Not monitored')).toBeInTheDocument();
        });
    });

    describe('fulfillment status', () => {
        it('renders fulfillment badges for each item', () => {
            renderWithRouter(mockItems);
            expect(screen.getByText('Fulfilled')).toBeInTheDocument();
            expect(screen.getByText('Missing')).toBeInTheDocument();
            expect(screen.getByText('Upgradable')).toBeInTheDocument();
        });
    });

    describe('empty state', () => {
        it('renders empty state when no items', () => {
            renderWithRouter([]);
            expect(screen.getByText('No related content')).toBeInTheDocument();
            expect(screen.getByText('No content items are linked to this subject.')).toBeInTheDocument();
        });
    });

    describe('table headers', () => {
        it('renders column headers', () => {
            renderWithRouter(mockItems);
            expect(screen.getByText('Title')).toBeInTheDocument();
            expect(screen.getByText('Kind')).toBeInTheDocument();
            expect(screen.getByText('Monitoring')).toBeInTheDocument();
            expect(screen.getByText('Status')).toBeInTheDocument();
        });
    });

    describe('custom className', () => {
        it('applies additional className', () => {
            render(
                <MemoryRouter>
                    <RelatedContentList items={mockItems} className="custom-class" />
                </MemoryRouter>,
            );
            const el = document.querySelector('[data-component="related-content-list"]');
            expect(el?.classList.contains('custom-class')).toBe(true);
        });
    });

    describe('single item', () => {
        it('renders correctly with one item', () => {
            renderWithRouter([mockItems[0]]);
            expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
            expect(screen.getByText('Fulfilled')).toBeInTheDocument();
        });
    });

    describe('all fulfillment variants', () => {
        it('renders external fulfillment status', () => {
            const externalItem: RelatedContentItem = {
                id: 'ext-1',
                title: 'External Movie',
                kind: 'movie',
                monitoring: 'active',
                fulfillment: 'fulfilledExternally',
            };
            renderWithRouter([externalItem]);
            expect(screen.getByText('External')).toBeInTheDocument();
        });
    });

    describe('all kind variants', () => {
        it('renders music and book kinds', () => {
            const items: RelatedContentItem[] = [
                {
                    id: 'music-1',
                    title: 'Album One',
                    kind: 'music',
                    monitoring: 'active',
                    fulfillment: 'fulfilled',
                },
                {
                    id: 'book-1',
                    title: 'Book One',
                    kind: 'book',
                    monitoring: 'excluded',
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(items);
            expect(screen.getByText('Music')).toBeInTheDocument();
            expect(screen.getByText('Book')).toBeInTheDocument();
        });
    });
});
