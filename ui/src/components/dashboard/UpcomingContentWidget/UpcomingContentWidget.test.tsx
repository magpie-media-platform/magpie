import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { UpcomingContentWidget, type UpcomingContentItem } from './UpcomingContentWidget';

const renderWithRouter = (ui: React.ReactElement) => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>{ui}</MemoryRouter>
        </QueryClientProvider>,
    );
};

const mockItems: UpcomingContentItem[] = [
    {
        id: '1',
        contentId: 'content-1',
        title: 'Shogun',
        subtitle: 'S01E10 - A Dream of a Dream',
        date: new Date().toISOString(),
        posterUrl: 'https://example.com/poster1.jpg',
        contentType: 'tv',
        levelLabel: 'Episode',
        fulfillment: 'missing',
    },
    {
        id: '2',
        contentId: 'content-2',
        title: 'Fallout',
        subtitle: 'S01E08 - The Beginning',
        date: new Date(Date.now() + 86400000).toISOString(),
        posterUrl: 'https://example.com/poster2.jpg',
        contentType: 'tv',
        levelLabel: 'Episode',
        fulfillment: 'fulfilled',
    },
    {
        id: '3',
        contentId: 'content-3',
        title: 'Dune: Part Two',
        date: new Date(Date.now() + 86400000 * 2).toISOString(),
        contentType: 'movie',
        levelLabel: 'Movie',
        fulfillment: 'missing',
    },
    {
        id: '4',
        contentId: 'content-4',
        title: '3 Body Problem',
        subtitle: 'S01E01 - Countdown',
        date: new Date(Date.now() + 86400000 * 3).toISOString(),
        contentType: 'tv',
        levelLabel: 'Episode',
        fulfillment: 'upgradable',
    },
    {
        id: '5',
        contentId: 'content-5',
        title: 'The Gentlemen',
        subtitle: 'S01E05 - Episode 5',
        date: new Date(Date.now() + 86400000 * 7).toISOString(),
        contentType: 'tv',
        levelLabel: 'Episode',
        fulfillment: 'fulfilledExternally',
    },
];

describe('UpcomingContentWidget', () => {
    it('renders with default heading', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);
        expect(screen.getByRole('heading', { name: 'Upcoming' })).toBeInTheDocument();
    });

    it('renders with custom heading', () => {
        renderWithRouter(<UpcomingContentWidget heading="Coming Soon" items={mockItems} />);
        expect(screen.getByRole('heading', { name: 'Coming Soon' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        renderWithRouter(<UpcomingContentWidget icon={<TestIcon />} items={mockItems} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays content titles', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        expect(screen.getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
        expect(screen.getByText('Fallout', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
        expect(
            screen.getByText('Dune: Part Two', { selector: '.content-lineage-title__segment--leaf' }),
        ).toBeInTheDocument();
    });

    it('displays subtitles when provided', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        expect(screen.getByText('S01E10 - A Dream of a Dream')).toBeInTheDocument();
        expect(screen.getByText('S01E08 - The Beginning')).toBeInTheDocument();
    });

    it('displays content kinds', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        expect(screen.getAllByText('Episode').length).toBeGreaterThanOrEqual(1);
        expect(screen.getByText('Movie')).toBeInTheDocument();
    });

    it('applies data-component attribute', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveAttribute('data-component', 'upcoming-content-widget');
    });

    it('applies custom className', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} className="custom-class" data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveClass('upcoming-content-widget', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<UpcomingContentWidget ref={ref} items={mockItems} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} data-testid="my-widget" />);
        expect(screen.getByTestId('my-widget')).toBeInTheDocument();
    });

    it('tries a new poster URL after the previous URL fails', () => {
        const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        const renderWidget = (items: UpcomingContentItem[]) => (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <UpcomingContentWidget items={items} />
                </MemoryRouter>
            </QueryClientProvider>
        );
        const view = render(renderWidget([mockItems[0]]));
        const failedImage = screen.getByRole('presentation');
        fireEvent.error(failedImage);
        expect(screen.queryByRole('presentation')).not.toBeInTheDocument();

        view.rerender(renderWidget([{ ...mockItems[0], posterUrl: 'https://example.com/replacement-poster.jpg' }]));

        expect(screen.getByRole('presentation')).toHaveAttribute('src', 'https://example.com/replacement-poster.jpg');
    });
});

describe('UpcomingContentWidget today badge', () => {
    it('shows today badge when items are airing today', () => {
        const todayItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show 1',
                date: new Date().toISOString(),
                fulfillment: 'missing',
            },
            {
                id: '2',
                contentId: 'content-2',
                title: 'Show 2',
                date: new Date().toISOString(),
                fulfillment: 'fulfilled',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={todayItems} />);
        expect(screen.getByText('2 today')).toBeInTheDocument();
    });

    it('does not show today badge when no items air today', () => {
        const futureItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show 1',
                date: new Date(Date.now() + 86400000).toISOString(),
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={futureItems} />);
        expect(screen.queryByText(/today/)).not.toBeInTheDocument();
    });

    it('does not show today badge when loading', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} loading />);
        expect(screen.queryByText(/today/)).not.toBeInTheDocument();
    });

    it('has aria-label for today badge', () => {
        const todayItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show 1',
                date: new Date().toISOString(),
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={todayItems} />);
        const badge = screen.getByText('1 today');
        expect(badge).toHaveAttribute('aria-label', '1 airing today');
    });
});

describe('UpcomingContentWidget maxItems', () => {
    it('limits displayed items to maxItems', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} maxItems={3} />);

        expect(screen.getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
        expect(screen.getByText('Fallout', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
        expect(
            screen.getByText('Dune: Part Two', { selector: '.content-lineage-title__segment--leaf' }),
        ).toBeInTheDocument();
        expect(screen.queryByText('3 Body Problem')).not.toBeInTheDocument();
        expect(screen.queryByText('The Gentlemen')).not.toBeInTheDocument();
    });

    it('defaults to 5 items', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        expect(screen.getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
        expect(
            screen.getByText('The Gentlemen', { selector: '.content-lineage-title__segment--leaf' }),
        ).toBeInTheDocument();
    });

    it('shows all items when fewer than maxItems', () => {
        const fewItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show 1',
                date: new Date().toISOString(),
                fulfillment: 'missing',
            },
            {
                id: '2',
                contentId: 'content-2',
                title: 'Show 2',
                date: new Date(Date.now() + 86400000).toISOString(),
                fulfillment: 'fulfilled',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={fewItems} maxItems={5} />);

        expect(screen.getByText('Show 1', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
        expect(screen.getByText('Show 2', { selector: '.content-lineage-title__segment--leaf' })).toBeInTheDocument();
    });
});

describe('UpcomingContentWidget loading state', () => {
    it('shows skeleton items when loading', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} loading />);

        const skeletonItems = document.querySelectorAll('.upcoming-content-widget__item--skeleton');
        expect(skeletonItems.length).toBe(3);
    });

    it('does not show content titles when loading', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} loading />);
        expect(screen.queryByText('Shogun')).not.toBeInTheDocument();
    });

    it('does not show view calendar link when loading', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} loading viewCalendarUrl="/calendar" />);
        expect(screen.queryByText('View calendar')).not.toBeInTheDocument();
    });
});

describe('UpcomingContentWidget empty state', () => {
    it('shows empty message when no items', () => {
        renderWithRouter(<UpcomingContentWidget items={[]} />);
        expect(screen.getByText('No upcoming content')).toBeInTheDocument();
    });

    it('does not show view calendar link when empty', () => {
        renderWithRouter(<UpcomingContentWidget items={[]} viewCalendarUrl="/calendar" />);
        expect(screen.queryByText('View calendar')).not.toBeInTheDocument();
    });
});

describe('UpcomingContentWidget navigation', () => {
    it('renders items as links when getItemUrl is provided', () => {
        const getItemUrl = (item: UpcomingContentItem) => `/library/${item.contentId}`;

        renderWithRouter(<UpcomingContentWidget items={mockItems} getItemUrl={getItemUrl} />);

        const link = screen.getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' }).closest('a');
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/library/content-1');
    });

    it('renders items as divs when getItemUrl is not provided', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        const item = screen
            .getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item?.tagName).toBe('DIV');
    });

    it('makes item clickable when getItemUrl is provided', () => {
        const getItemUrl = (item: UpcomingContentItem) => `/library/${item.contentId}`;

        renderWithRouter(<UpcomingContentWidget items={mockItems} getItemUrl={getItemUrl} />);

        const item = screen
            .getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item).toHaveAttribute('data-clickable');
    });

    it('renders view calendar as link when viewCalendarUrl is provided', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} viewCalendarUrl="/calendar" />);

        const link = screen.getByText('View calendar');
        expect(link.tagName).toBe('A');
        expect(link).toHaveAttribute('href', '/calendar');
    });

    it('links have correct URLs for each item', () => {
        const getItemUrl = (item: UpcomingContentItem) => `/library/${item.contentId}`;

        renderWithRouter(<UpcomingContentWidget items={mockItems} getItemUrl={getItemUrl} />);

        const shogunLink = screen
            .getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })
            .closest('a');
        const falloutLink = screen
            .getByText('Fallout', { selector: '.content-lineage-title__segment--leaf' })
            .closest('a');
        const duneLink = screen
            .getByText('Dune: Part Two', { selector: '.content-lineage-title__segment--leaf' })
            .closest('a');

        expect(shogunLink).toHaveAttribute('href', '/library/content-1');
        expect(falloutLink).toHaveAttribute('href', '/library/content-2');
        expect(duneLink).toHaveAttribute('href', '/library/content-3');
    });
});

describe('UpcomingContentWidget view all link', () => {
    it('shows view calendar link when there are more items than maxItems', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} maxItems={3} viewCalendarUrl="/calendar" />);
        expect(screen.getByText('View calendar')).toBeInTheDocument();
    });

    it('shows view calendar link when items exist and URL provided', () => {
        const fewItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show 1',
                date: new Date().toISOString(),
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={fewItems} viewCalendarUrl="/calendar" />);
        expect(screen.getByText('View calendar')).toBeInTheDocument();
    });

    it('hides view calendar link when no URL provided', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);
        expect(screen.queryByText('View calendar')).not.toBeInTheDocument();
    });

    it('applies data-action attribute to view calendar link', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} viewCalendarUrl="/calendar" />);
        expect(screen.getByText('View calendar')).toHaveAttribute('data-action', 'view-calendar');
    });
});

describe('UpcomingContentWidget data attributes', () => {
    it('applies data-content-id to each item', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        const items = document.querySelectorAll('[data-content-id]');
        expect(items[0]).toHaveAttribute('data-content-id', 'content-1');
        expect(items[1]).toHaveAttribute('data-content-id', 'content-2');
        expect(items[2]).toHaveAttribute('data-content-id', 'content-3');
    });

    it('applies data-fulfillment to each item', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        const items = document.querySelectorAll(
            '.upcoming-content-widget__item:not(.upcoming-content-widget__item--skeleton)',
        );
        expect(items[0]).toHaveAttribute('data-fulfillment', 'missing');
        expect(items[1]).toHaveAttribute('data-fulfillment', 'fulfilled');
        expect(items[2]).toHaveAttribute('data-fulfillment', 'missing');
        expect(items[3]).toHaveAttribute('data-fulfillment', 'upgradable');
    });

    it('applies data-content-type to each item', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        const items = document.querySelectorAll(
            '.upcoming-content-widget__item:not(.upcoming-content-widget__item--skeleton)',
        );
        expect(items[0]).toHaveAttribute('data-content-type', 'tv');
        expect(items[1]).toHaveAttribute('data-content-type', 'tv');
        expect(items[2]).toHaveAttribute('data-content-type', 'movie');
    });
});

describe('UpcomingContentWidget accessibility', () => {
    it('has accessible heading structure', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Upcoming' })).toBeInTheDocument();
    });

    it('supports custom heading level', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} headingLevel={3} />);
        expect(screen.getByRole('heading', { level: 3, name: 'Upcoming' })).toBeInTheDocument();
    });

    it('clickable items expose the content title in their accessible name', () => {
        const getItemUrl = (item: UpcomingContentItem) => `/library/${item.contentId}`;
        renderWithRouter(<UpcomingContentWidget items={mockItems} getItemUrl={getItemUrl} />);

        const item = screen
            .getByText('Shogun', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item).not.toHaveAttribute('aria-label');
        expect(item).toHaveAccessibleName(/^Shogun\b/);
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        renderWithRouter(<UpcomingContentWidget icon={<TestIcon />} items={mockItems} />);

        const iconWrapper = document.querySelector('.upcoming-content-widget__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('fulfillment indicators are hidden from screen readers', () => {
        renderWithRouter(<UpcomingContentWidget items={mockItems} />);

        const indicators = document.querySelectorAll('.upcoming-content-widget__item-indicator');
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });
});

describe('UpcomingContentWidget fulfillment variants', () => {
    it('applies missing variant class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show',
                date: new Date().toISOString(),
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const item = screen
            .getByText('Show', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item).toHaveAttribute('data-variant', 'missing');
    });

    it('applies fulfilled variant class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show',
                date: new Date().toISOString(),
                fulfillment: 'fulfilled',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const item = screen
            .getByText('Show', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item).toHaveAttribute('data-variant', 'fulfilled');
    });

    it('applies upgradable variant class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show',
                date: new Date().toISOString(),
                fulfillment: 'upgradable',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const item = screen
            .getByText('Show', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item).toHaveAttribute('data-variant', 'upgradable');
    });

    it('applies fulfilledExternally variant class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show',
                date: new Date().toISOString(),
                fulfillment: 'fulfilledExternally',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const item = screen
            .getByText('Show', { selector: '.content-lineage-title__segment--leaf' })
            .closest('.upcoming-content-widget__item');
        expect(item).toHaveAttribute('data-variant', 'fulfilledExternally');
    });
});

describe('UpcomingContentWidget date formatting', () => {
    it('shows "Today" for items airing today', () => {
        const todayItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show Today',
                date: new Date().toISOString(),
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={todayItems} />);
        expect(screen.getByText('Today')).toBeInTheDocument();
    });

    it('shows "Tomorrow" for items airing tomorrow', () => {
        const tomorrowItems: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show Tomorrow',
                date: new Date(Date.now() + 86400000).toISOString(),
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={tomorrowItems} />);
        expect(screen.getByText('Tomorrow')).toBeInTheDocument();
    });
});

describe('UpcomingContentWidget content kinds', () => {
    it('applies episode kind class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Show',
                date: new Date().toISOString(),
                contentType: 'tv',
                levelLabel: 'Episode',
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const kindLabel = screen.getByText('Episode');
        expect(kindLabel).toHaveAttribute('data-content-type', 'tv');
    });

    it('applies movie kind class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Film',
                date: new Date().toISOString(),
                contentType: 'movie',
                levelLabel: 'Movie',
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const kindLabel = screen.getByText('Movie');
        expect(kindLabel).toHaveAttribute('data-content-type', 'movie');
    });

    it('applies music kind class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Greatest Hits',
                date: new Date().toISOString(),
                contentType: 'music',
                levelLabel: 'Album',
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const kindLabel = screen.getByText('Album');
        expect(kindLabel).toHaveAttribute('data-content-type', 'music');
    });

    it('applies book kind class', () => {
        const items: UpcomingContentItem[] = [
            {
                id: '1',
                contentId: 'content-1',
                title: 'Novel',
                date: new Date().toISOString(),
                contentType: 'books',
                levelLabel: 'Book',
                fulfillment: 'missing',
            },
        ];
        renderWithRouter(<UpcomingContentWidget items={items} />);

        const kindLabel = screen.getByText('Book');
        expect(kindLabel).toHaveAttribute('data-content-type', 'books');
    });
});
