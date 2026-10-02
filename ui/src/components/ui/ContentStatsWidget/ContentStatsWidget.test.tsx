import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import {
    ContentStatsWidget,
    type FulfillmentCounts,
    type FulfillmentStatus,
    type RecentContentItem,
} from './ContentStatsWidget';
import { TooltipProvider } from '@/components/ui/Tooltip';

const renderWithRouter = (ui: React.ReactElement) => {
    return render(<MemoryRouter>{ui}</MemoryRouter>, { wrapper: TooltipProvider });
};

const defaultCounts: FulfillmentCounts = {
    total: 58,
    missing: 5,
    upgradable: 3,
    fulfilled: 42,
    fulfilledExternally: 8,
    partial: 4,
};

const recentItems: RecentContentItem[] = [
    { id: '1', title: 'Breaking Bad', addedAt: '5 min ago', fulfillment: 'fulfilled' },
    { id: '2', title: 'The Office', addedAt: '2h ago', fulfillment: 'missing' },
    { id: '3', title: 'Parks and Recreation', addedAt: '1d ago', fulfillment: 'upgradable' },
];

describe('ContentStatsWidget', () => {
    it('renders with default heading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);
        expect(screen.getByRole('heading', { name: 'Content' })).toBeInTheDocument();
    });

    it('renders with custom heading', () => {
        renderWithRouter(<ContentStatsWidget heading="Media Library" counts={defaultCounts} />);
        expect(screen.getByRole('heading', { name: 'Media Library' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        renderWithRouter(<ContentStatsWidget icon={<TestIcon />} counts={defaultCounts} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays total count in header', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);

        expect(screen.getByText('58')).toBeInTheDocument();
    });

    it('displays count for each status', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);

        expect(screen.getByText('Missing')).toBeInTheDocument();
        expect(screen.getByText('Upgradable')).toBeInTheDocument();
        expect(screen.getByText('Fulfilled')).toBeInTheDocument();
        expect(screen.getByText('External')).toBeInTheDocument();

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow).toHaveTextContent('5');

        const upgradableRow = screen.getByText('Upgradable').closest('.status-row');
        expect(upgradableRow).toHaveTextContent('3');

        const fulfilledRow = screen.getByText('Fulfilled').closest('.status-row');
        expect(fulfilledRow).toHaveTextContent('42');

        const externalRow = screen.getByText('External').closest('.status-row');
        expect(externalRow).toHaveTextContent('8');
    });

    it('applies data-component attribute', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveAttribute('data-component', 'content-stats-widget');
    });

    it('applies custom className', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} className="custom-class" data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveClass('content-stats-widget', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<ContentStatsWidget ref={ref} counts={defaultCounts} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} data-testid="my-widget" />);
        expect(screen.getByTestId('my-widget')).toBeInTheDocument();
    });
});

describe('ContentStatsWidget loading state', () => {
    it('shows skeleton rows when loading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} loading />);

        const skeletonRows = document.querySelectorAll('.status-row [data-component="skeleton"]');
        expect(skeletonRows.length).toBe(5);
    });

    it('shows skeleton items when loading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} loading />);

        const skeletonItems = document.querySelectorAll('.content-stats-widget__item[data-component="skeleton"]');
        expect(skeletonItems.length).toBe(2);
    });

    it('shows dash for total when loading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} loading />);
        expect(screen.getByText('-')).toBeInTheDocument();
    });

    it('does not show status labels when loading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} loading />);
        expect(screen.queryByText('Missing')).not.toBeInTheDocument();
    });

    it('does not show view all link when loading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} loading viewAllUrl="/library" />);
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });
});

describe('ContentStatsWidget recent items', () => {
    it('renders recent content items', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('The Office')).toBeInTheDocument();
        expect(screen.getByText('Parks and Recreation')).toBeInTheDocument();
    });

    it('displays recent additions header', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);
        expect(screen.getByText('Recent Additions')).toBeInTheDocument();
    });

    it('displays item metadata', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        expect(screen.getByText('5 min ago')).toBeInTheDocument();
        expect(screen.getByText('2h ago')).toBeInTheDocument();
    });

    it('limits displayed items to maxItems', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} maxItems={2} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('The Office')).toBeInTheDocument();
        expect(screen.queryByText('Parks and Recreation')).not.toBeInTheDocument();
    });

    it('applies data-content-id to items', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        const items = document.querySelectorAll('[data-content-id]');
        expect(items[0]).toHaveAttribute('data-content-id', '1');
        expect(items[1]).toHaveAttribute('data-content-id', '2');
        expect(items[2]).toHaveAttribute('data-content-id', '3');
    });

    it('applies data-status to items', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        const items = document.querySelectorAll(
            '.content-stats-widget__item:not(.content-stats-widget__item[data-component="skeleton"])',
        );
        expect(items[0]).toHaveAttribute('data-status', 'fulfilled');
        expect(items[1]).toHaveAttribute('data-status', 'missing');
        expect(items[2]).toHaveAttribute('data-status', 'upgradable');
    });

    it('does not show recent section when no items and not loading', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={[]} />);
        expect(screen.queryByText('Recent Additions')).not.toBeInTheDocument();
    });

    it('renders the per-item fulfillment status for a monitored recent item', () => {
        const items: RecentContentItem[] = [
            {
                id: 'mon1',
                title: 'Monitored Show',
                addedAt: '1h ago',
                fulfillment: 'upgradable',
                effectiveMonitoring: 'Monitored',
            },
        ];
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        const item = document.querySelector('[data-content-id="mon1"]');
        expect(item?.querySelector('.content-stats-widget__item-status')?.textContent).toBe('Upgradable');
    });

    it('shows explicit unmonitored and fulfillment states separately for a recent item', () => {
        // Arrange
        const items: RecentContentItem[] = [
            {
                id: 'ex1',
                title: 'Unmonitored Show',
                addedAt: '1h ago',
                fulfillment: 'missing',
                monitoring: 'Unmonitored',
                effectiveMonitoring: 'Unmonitored',
            },
        ];

        // Act
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        // Assert
        const item = document.querySelector('[data-content-id="ex1"]');
        expect(item).toBeInTheDocument();
        expect(item?.querySelector('.content-stats-widget__item-status')).toHaveTextContent('Missing');
        expect(item).toHaveAttribute('data-status', 'missing');
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
    });

    it('shows inherited unmonitored and fulfillment states separately for a recent item', () => {
        // Arrange
        const items: RecentContentItem[] = [
            {
                id: 'inh1',
                title: 'Inherited Excluded',
                addedAt: '1h ago',
                fulfillment: 'missing',
                monitoring: null,
                effectiveMonitoring: 'Unmonitored',
            },
        ];

        // Act
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        // Assert
        const item = document.querySelector('[data-content-id="inh1"]');
        expect(item?.querySelector('.content-stats-widget__item-status')).toHaveTextContent('Missing');
        expect(screen.getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
    });
});

describe('ContentStatsWidget navigation', () => {
    it('renders status rows as links when getStatusUrl is provided', () => {
        const getStatusUrl = (status: FulfillmentStatus) => `/library?fulfillment=${status}`;

        renderWithRouter(<ContentStatsWidget counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow?.tagName).toBe('A');
        expect(missingRow).toHaveAttribute('href', '/library?fulfillment=missing');
    });

    it('renders status rows as divs when getStatusUrl is not provided', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow?.tagName).toBe('DIV');
    });

    it('does not make row clickable when count is 0', () => {
        const getStatusUrl = (status: FulfillmentStatus) => `/library?fulfillment=${status}`;

        renderWithRouter(
            <ContentStatsWidget
                counts={{ total: 10, missing: 0, upgradable: 0, fulfilled: 10, fulfilledExternally: 0, partial: 0 }}
                getStatusUrl={getStatusUrl}
            />,
        );

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow?.tagName).toBe('DIV');
        expect(missingRow).not.toHaveAttribute('data-clickable');
    });

    it('makes row clickable when count is greater than 0 and URL provided', () => {
        const getStatusUrl = (status: FulfillmentStatus) => `/library?fulfillment=${status}`;

        renderWithRouter(<ContentStatsWidget counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow).toHaveAttribute('data-clickable');
    });

    it('renders view all as link when viewAllUrl is provided', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} viewAllUrl="/library" />);

        const link = screen.getByText('View all content');
        expect(link.tagName).toBe('A');
        expect(link).toHaveAttribute('href', '/library');
    });

    it('renders content items as links when getItemUrl is provided', () => {
        const getItemUrl = (item: RecentContentItem) => `/library/${item.id}`;

        renderWithRouter(
            <ContentStatsWidget counts={defaultCounts} recentItems={recentItems} getItemUrl={getItemUrl} />,
        );

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item?.tagName).toBe('A');
        expect(item).toHaveAttribute('href', '/library/1');
    });

    it('renders content items as divs when getItemUrl is not provided', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item?.tagName).toBe('DIV');
    });

    it('makes content item clickable when getItemUrl is provided', () => {
        const getItemUrl = (item: RecentContentItem) => `/library/${item.id}`;

        renderWithRouter(
            <ContentStatsWidget counts={defaultCounts} recentItems={recentItems} getItemUrl={getItemUrl} />,
        );

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-clickable');
    });

    it('links have correct URLs for each status', () => {
        const getStatusUrl = (status: FulfillmentStatus) => `/library?fulfillment=${status}`;

        renderWithRouter(<ContentStatsWidget counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        expect(screen.getByText('Missing').closest('a')).toHaveAttribute('href', '/library?fulfillment=missing');
        expect(screen.getByText('Upgradable').closest('a')).toHaveAttribute('href', '/library?fulfillment=upgradable');
        expect(screen.getByText('Partial').closest('a')).toHaveAttribute('href', '/library?fulfillment=partial');
        expect(screen.getByText('Fulfilled').closest('a')).toHaveAttribute('href', '/library?fulfillment=fulfilled');
        expect(screen.getByText('External').closest('a')).toHaveAttribute(
            'href',
            '/library?fulfillment=fulfilledExternally',
        );
    });
});

describe('ContentStatsWidget view all link', () => {
    it('shows view all link when there are items and URL provided', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} viewAllUrl="/library" />);
        expect(screen.getByText('View all content')).toBeInTheDocument();
    });

    it('shows view all link when there are more items than maxItems', () => {
        renderWithRouter(
            <ContentStatsWidget
                counts={{ total: 0, missing: 0, upgradable: 0, fulfilled: 0, fulfilledExternally: 0, partial: 0 }}
                recentItems={recentItems}
                maxItems={2}
                viewAllUrl="/library"
            />,
        );
        expect(screen.getByText('View all content')).toBeInTheDocument();
    });

    it('hides view all link when no URL provided', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });

    it('hides view all link when no content and no recent items', () => {
        renderWithRouter(
            <ContentStatsWidget
                counts={{ total: 0, missing: 0, upgradable: 0, fulfilled: 0, fulfilledExternally: 0, partial: 0 }}
                recentItems={[]}
                viewAllUrl="/library"
            />,
        );
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });

    it('applies data-action attribute to view all link', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} viewAllUrl="/library" />);
        expect(screen.getByText('View all content')).toHaveAttribute('data-action', 'view-all');
    });
});

describe('ContentStatsWidget data attributes', () => {
    it('applies data-status to each status row', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);

        expect(screen.getByText('Missing').closest('.status-row')).toHaveAttribute('data-status', 'missing');
        expect(screen.getByText('Missing').closest('.status-row')).toHaveAttribute('data-status', 'missing');
        expect(screen.getByText('Upgradable').closest('.status-row')).toHaveAttribute('data-status', 'upgradable');
        expect(screen.getByText('Fulfilled').closest('.status-row')).toHaveAttribute('data-status', 'fulfilled');
        expect(screen.getByText('External').closest('.status-row')).toHaveAttribute(
            'data-status',
            'fulfilledExternally',
        );
    });
});

describe('ContentStatsWidget accessibility', () => {
    it('has accessible heading structure', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Content' })).toBeInTheDocument();
    });

    it('clickable rows have aria-label', () => {
        const getStatusUrl = (status: FulfillmentStatus) => `/library?fulfillment=${status}`;
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow).toHaveAttribute('aria-label', 'View 5 missing items');
    });

    it('clickable content items expose the content title in their accessible name', () => {
        const getItemUrl = (item: RecentContentItem) => `/library/${item.id}`;
        renderWithRouter(
            <ContentStatsWidget counts={defaultCounts} recentItems={recentItems} getItemUrl={getItemUrl} />,
        );

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item).not.toHaveAttribute('aria-label');
        expect(item).toHaveAccessibleName('Breaking Bad Status: Fulfilled 5 min ago');
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        renderWithRouter(<ContentStatsWidget icon={<TestIcon />} counts={defaultCounts} />);

        const iconWrapper = document.querySelector('.content-stats-widget__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('status indicators are hidden from screen readers', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} />);

        const indicators = document.querySelectorAll('.status-row__indicator');
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('item indicators are hidden from screen readers', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        const indicators = document.querySelectorAll(
            '.content-stats-widget__item-indicator:not(.content-stats-widget__item-indicator--skeleton)',
        );
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('status labels have aria-label', () => {
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={recentItems} />);

        const fulfilledStatus = screen.getAllByText('Fulfilled')[1];
        expect(fulfilledStatus).toHaveAttribute('aria-label', 'Status: Fulfilled');
    });
});

describe('ContentStatsWidget fulfillment variants', () => {
    it('applies missing variant class', () => {
        const items: RecentContentItem[] = [{ id: '1', title: 'Item', addedAt: '5m ago', fulfillment: 'missing' }];
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'missing');
    });

    it('applies upgradable variant class', () => {
        const items: RecentContentItem[] = [{ id: '1', title: 'Item', addedAt: '5m ago', fulfillment: 'upgradable' }];
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'upgradable');
    });

    it('applies fulfilled variant class', () => {
        const items: RecentContentItem[] = [{ id: '1', title: 'Item', addedAt: '5m ago', fulfillment: 'fulfilled' }];
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'fulfilled');
    });

    it('applies fulfilledExternally variant class', () => {
        const items: RecentContentItem[] = [
            { id: '1', title: 'Item', addedAt: '5m ago', fulfillment: 'fulfilledExternally' },
        ];
        renderWithRouter(<ContentStatsWidget counts={defaultCounts} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'fulfilledExternally');
    });
});
