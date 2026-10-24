import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { ContentStatsWidget, type ContentStatsRow, type RecentContentItem } from './ContentStatsWidget';
import { TooltipProvider } from '@/components/ui/Tooltip';

const renderWithRouter = (ui: React.ReactElement) => {
    return render(<MemoryRouter>{ui}</MemoryRouter>, { wrapper: TooltipProvider });
};

const defaultRows: ContentStatsRow[] = [
    { status: 'fulfilled', count: 42 },
    { status: 'upgradable', count: 3 },
    { status: 'missing', count: 5 },
];

const linkedRows: ContentStatsRow[] = [
    { status: 'fulfilled', count: 42, url: '/library?fulfillment=fulfilled' },
    { status: 'upgradable', count: 3, url: '/library?fulfillment=upgradable' },
    { status: 'missing', count: 5, url: '/library?fulfillment=missing' },
];

const recentItems: RecentContentItem[] = [
    { id: '1', title: 'Breaking Bad', timestamp: '5 min ago', fulfillment: 'fulfilled' },
    { id: '2', title: 'The Office', timestamp: '2h ago', fulfillment: 'missing' },
    { id: '3', title: 'Parks and Recreation', timestamp: '1d ago', fulfillment: 'upgradable' },
];

describe('ContentStatsWidget', () => {
    it('renders with default heading', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);
        expect(screen.getByRole('heading', { name: 'Content' })).toBeInTheDocument();
    });

    it('renders with custom heading', () => {
        renderWithRouter(<ContentStatsWidget heading="Media Library" rows={defaultRows} />);
        expect(screen.getByRole('heading', { name: 'Media Library' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        renderWithRouter(<ContentStatsWidget icon={<TestIcon />} rows={defaultRows} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays total count in header', () => {
        renderWithRouter(<ContentStatsWidget total={58} rows={defaultRows} />);

        expect(screen.getByText('58')).toBeInTheDocument();
    });

    it('ContentStatsWidget_OmitsTheHeaderTotal_WhenNoTotalIsGiven', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);

        // Assert
        expect(document.querySelector('.content-stats-widget__total')).not.toBeInTheDocument();
    });

    it('ContentStatsWidget_ShowsTheRowLabel_WhenARowHasALabel', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={[{ status: 'fulfilled', label: 'Today', count: 4 }]} />);

        // Assert
        expect(screen.getByText('Today').closest('.status-row')).toHaveTextContent('4');
        expect(screen.queryByText('Fulfilled')).not.toBeInTheDocument();
    });

    it('displays count for each status', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);

        expect(screen.getByText('Missing')).toBeInTheDocument();
        expect(screen.getByText('Upgradable')).toBeInTheDocument();
        expect(screen.getByText('Fulfilled')).toBeInTheDocument();

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow).toHaveTextContent('5');

        const upgradableRow = screen.getByText('Upgradable').closest('.status-row');
        expect(upgradableRow).toHaveTextContent('3');

        const fulfilledRow = screen.getByText('Fulfilled').closest('.status-row');
        expect(fulfilledRow).toHaveTextContent('42');
    });

    it('applies data-component attribute', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveAttribute('data-component', 'content-stats-widget');
    });

    it('applies custom className', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} className="custom-class" data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveClass('content-stats-widget', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<ContentStatsWidget ref={ref} rows={defaultRows} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} data-testid="my-widget" />);
        expect(screen.getByTestId('my-widget')).toBeInTheDocument();
    });
});

describe('ContentStatsWidget loading state', () => {
    it('shows skeleton rows when loading', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} loading />);

        const skeletonRows = document.querySelectorAll('.status-row [data-component="skeleton"]');
        expect(skeletonRows.length).toBe(3);
    });

    it('shows skeleton items when loading', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} loading />);

        const skeletonItems = document.querySelectorAll('.content-stats-widget__item[data-component="skeleton"]');
        expect(skeletonItems.length).toBe(2);
    });

    it('shows dash for total when loading', () => {
        renderWithRouter(<ContentStatsWidget total={58} rows={defaultRows} loading />);
        expect(screen.getByText('-')).toBeInTheDocument();
    });

    it('does not show status labels when loading', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} loading />);
        expect(screen.queryByText('Missing')).not.toBeInTheDocument();
    });

    it('does not show view all link when loading', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} loading viewAllUrl="/library" />);
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });
});

describe('ContentStatsWidget recent items', () => {
    it('renders recent content items', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('The Office')).toBeInTheDocument();
        expect(screen.getByText('Parks and Recreation')).toBeInTheDocument();
    });

    it('displays recent additions header', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);
        expect(screen.getByText('Recent Additions')).toBeInTheDocument();
    });

    it('displays item metadata', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        expect(screen.getByText('5 min ago')).toBeInTheDocument();
        expect(screen.getByText('2h ago')).toBeInTheDocument();
    });

    it('limits displayed items to maxItems', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} maxItems={2} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('The Office')).toBeInTheDocument();
        expect(screen.queryByText('Parks and Recreation')).not.toBeInTheDocument();
    });

    it('applies data-content-id to items', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        const items = document.querySelectorAll('[data-content-id]');
        expect(items[0]).toHaveAttribute('data-content-id', '1');
        expect(items[1]).toHaveAttribute('data-content-id', '2');
        expect(items[2]).toHaveAttribute('data-content-id', '3');
    });

    it('applies data-status to items', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        const items = document.querySelectorAll(
            '.content-stats-widget__item:not(.content-stats-widget__item[data-component="skeleton"])',
        );
        expect(items[0]).toHaveAttribute('data-status', 'fulfilled');
        expect(items[1]).toHaveAttribute('data-status', 'missing');
        expect(items[2]).toHaveAttribute('data-status', 'upgradable');
    });

    it('ContentStatsWidget_ShowsTheRecentHeading_WhenOneIsGiven', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentHeading="Recent" recentItems={recentItems} />);

        // Assert
        expect(screen.getByText('Recent')).toBeInTheDocument();
        expect(screen.queryByText('Recent Additions')).not.toBeInTheDocument();
    });

    it('ContentStatsWidget_ShowsTheEmptyMessage_WhenThereAreNoRecentItems', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={[]} />);

        // Assert
        expect(screen.getByText('Recent Additions')).toBeInTheDocument();
        expect(screen.getByText('No recent additions')).toBeInTheDocument();
    });

    it('ContentStatsWidget_ShowsTheCustomEmptyMessage_WhenOneIsGiven', () => {
        // Arrange & Act
        renderWithRouter(
            <ContentStatsWidget rows={defaultRows} recentItems={[]} recentEmptyMessage="No fulfilled content" />,
        );

        // Assert
        expect(screen.getByText('No fulfilled content')).toBeInTheDocument();
        expect(screen.queryByText('No recent additions')).not.toBeInTheDocument();
    });

    it('renders the per-item fulfillment status for a monitored recent item', () => {
        const items: RecentContentItem[] = [
            {
                id: 'mon1',
                title: 'Monitored Show',
                timestamp: '1h ago',
                fulfillment: 'upgradable',
                effectiveMonitoring: 'Monitored',
            },
        ];
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        const item = document.querySelector('[data-content-id="mon1"]');
        expect(item?.querySelector('.content-stats-widget__item-status')?.textContent).toBe('Upgradable');
    });

    it('shows unmonitored and fulfillment states separately for a recent item', () => {
        // Arrange
        const items: RecentContentItem[] = [
            {
                id: 'ex1',
                title: 'Unmonitored Show',
                timestamp: '1h ago',
                fulfillment: 'missing',
                effectiveMonitoring: 'Unmonitored',
            },
        ];

        // Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        // Assert
        const item = document.querySelector('[data-content-id="ex1"]');
        expect(item).toBeInTheDocument();
        expect(item?.querySelector('.content-stats-widget__item-status')).toHaveTextContent('Missing');
        expect(item).toHaveAttribute('data-status', 'missing');
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
    });

    it('ContentItem_ShowsUpcoming_WhenAMissingItemHasAFuturePublishDate', () => {
        // Arrange
        const items: RecentContentItem[] = [
            {
                id: 'up1',
                title: 'Future Movie',
                timestamp: '1h ago',
                fulfillment: 'missing',
                publishDate: '2099-01-01',
                publishDatePrecision: 'Day',
            },
        ];

        // Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        // Assert
        const item = document.querySelector('[data-content-id="up1"]');
        expect(item?.querySelector('.content-stats-widget__item-status')).toHaveTextContent('Upcoming');
        expect(item).toHaveAttribute('data-status', 'missing');
        expect(screen.getByText('Missing')).toBeInTheDocument();
    });

    it('ContentStatsWidget_HidesTheItemStatus_WhenShowItemStatusIsFalse', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={[]} recentItems={recentItems} showItemStatus={false} />);

        // Assert
        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(document.querySelector('.content-stats-widget__item-status')).not.toBeInTheDocument();
    });
});

describe('ContentStatsWidget navigation', () => {
    it('ContentStatsWidget_RendersStatusRowsAsLinks_WhenRowsHaveAUrl', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={linkedRows} />);

        // Assert
        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow?.tagName).toBe('A');
        expect(missingRow).toHaveAttribute('href', '/library?fulfillment=missing');
    });

    it('ContentStatsWidget_RendersStatusRowsAsDivs_WhenRowsHaveNoUrl', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);

        // Assert
        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow?.tagName).toBe('DIV');
    });

    it('does not make row clickable when count is 0', () => {
        renderWithRouter(
            <ContentStatsWidget rows={[{ status: 'missing', count: 0, url: '/library?fulfillment=missing' }]} />,
        );

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow?.tagName).toBe('DIV');
        expect(missingRow).not.toHaveAttribute('data-clickable');
    });

    it('makes row clickable when count is greater than 0 and URL provided', () => {
        renderWithRouter(<ContentStatsWidget rows={linkedRows} />);

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow).toHaveAttribute('data-clickable');
    });

    it('renders view all as link when viewAllUrl is provided', () => {
        renderWithRouter(<ContentStatsWidget total={58} rows={defaultRows} viewAllUrl="/library" />);

        const link = screen.getByText('View all content');
        expect(link.tagName).toBe('A');
        expect(link).toHaveAttribute('href', '/library');
    });

    it('renders content items as links when getItemUrl is provided', () => {
        const getItemUrl = (item: RecentContentItem) => `/library/${item.id}`;

        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} getItemUrl={getItemUrl} />);

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item?.tagName).toBe('A');
        expect(item).toHaveAttribute('href', '/library/1');
    });

    it('renders content items as divs when getItemUrl is not provided', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item?.tagName).toBe('DIV');
    });

    it('makes content item clickable when getItemUrl is provided', () => {
        const getItemUrl = (item: RecentContentItem) => `/library/${item.id}`;

        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} getItemUrl={getItemUrl} />);

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-clickable');
    });

    it('links have correct URLs for each status', () => {
        renderWithRouter(<ContentStatsWidget rows={linkedRows} />);

        expect(screen.getByText('Missing').closest('a')).toHaveAttribute('href', '/library?fulfillment=missing');
        expect(screen.getByText('Upgradable').closest('a')).toHaveAttribute('href', '/library?fulfillment=upgradable');
        expect(screen.getByText('Fulfilled').closest('a')).toHaveAttribute('href', '/library?fulfillment=fulfilled');
    });
});

describe('ContentStatsWidget view all link', () => {
    it('shows view all link when there are items and URL provided', () => {
        renderWithRouter(<ContentStatsWidget total={58} rows={defaultRows} viewAllUrl="/library" />);
        expect(screen.getByText('View all content')).toBeInTheDocument();
    });

    it('ContentStatsWidget_ShowsViewAll_WhenThereAreRecentItemsAndNoTotal', () => {
        // Arrange & Act
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} viewAllUrl="/library" />);

        // Assert
        expect(screen.getByText('View all content')).toBeInTheDocument();
    });

    it('ContentStatsWidget_ShowsTheViewAllLabel_WhenOneIsGiven', () => {
        // Arrange & Act
        renderWithRouter(
            <ContentStatsWidget
                rows={defaultRows}
                recentItems={recentItems}
                viewAllUrl="/library"
                viewAllLabel="View all fulfilled"
            />,
        );

        // Assert
        expect(screen.getByRole('link', { name: 'View all fulfilled' })).toHaveAttribute('href', '/library');
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });

    it('hides view all link when no URL provided', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });

    it('hides view all link when no content and no recent items', () => {
        renderWithRouter(<ContentStatsWidget total={0} rows={defaultRows} recentItems={[]} viewAllUrl="/library" />);
        expect(screen.queryByText('View all content')).not.toBeInTheDocument();
    });

    it('applies data-action attribute to view all link', () => {
        renderWithRouter(<ContentStatsWidget total={58} rows={defaultRows} viewAllUrl="/library" />);
        expect(screen.getByText('View all content')).toHaveAttribute('data-action', 'view-all');
    });
});

describe('ContentStatsWidget data attributes', () => {
    it('applies data-status to each status row', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);

        expect(screen.getByText('Missing').closest('.status-row')).toHaveAttribute('data-status', 'missing');
        expect(screen.getByText('Upgradable').closest('.status-row')).toHaveAttribute('data-status', 'upgradable');
        expect(screen.getByText('Fulfilled').closest('.status-row')).toHaveAttribute('data-status', 'fulfilled');
    });
});

describe('ContentStatsWidget accessibility', () => {
    it('has accessible heading structure', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Content' })).toBeInTheDocument();
    });

    it('clickable rows have aria-label', () => {
        renderWithRouter(<ContentStatsWidget rows={linkedRows} />);

        const missingRow = screen.getByText('Missing').closest('.status-row');
        expect(missingRow).toHaveAttribute('aria-label', 'View 5 missing items');
    });

    it('clickable content items expose the content title in their accessible name', () => {
        const getItemUrl = (item: RecentContentItem) => `/library/${item.id}`;
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} getItemUrl={getItemUrl} />);

        const item = screen.getByText('Breaking Bad').closest('.content-stats-widget__item');
        expect(item).not.toHaveAttribute('aria-label');
        expect(item).toHaveAccessibleName('Breaking Bad Status: Fulfilled 5 min ago');
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        renderWithRouter(<ContentStatsWidget icon={<TestIcon />} rows={defaultRows} />);

        const iconWrapper = document.querySelector('.content-stats-widget__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('status indicators are hidden from screen readers', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} />);

        const indicators = document.querySelectorAll('.status-row__indicator');
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('item indicators are hidden from screen readers', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        const indicators = document.querySelectorAll(
            '.content-stats-widget__item-indicator:not(.content-stats-widget__item-indicator--skeleton)',
        );
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('status labels have aria-label', () => {
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={recentItems} />);

        const fulfilledStatus = screen.getAllByText('Fulfilled')[1];
        expect(fulfilledStatus).toHaveAttribute('aria-label', 'Status: Fulfilled');
    });
});

describe('ContentStatsWidget fulfillment variants', () => {
    it('applies missing variant class', () => {
        const items: RecentContentItem[] = [{ id: '1', title: 'Item', timestamp: '5m ago', fulfillment: 'missing' }];
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'missing');
    });

    it('applies upgradable variant class', () => {
        const items: RecentContentItem[] = [{ id: '1', title: 'Item', timestamp: '5m ago', fulfillment: 'upgradable' }];
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'upgradable');
    });

    it('applies fulfilled variant class', () => {
        const items: RecentContentItem[] = [{ id: '1', title: 'Item', timestamp: '5m ago', fulfillment: 'fulfilled' }];
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'fulfilled');
    });

    it('applies fulfilledExternally variant class', () => {
        const items: RecentContentItem[] = [
            { id: '1', title: 'Item', timestamp: '5m ago', fulfillment: 'fulfilledExternally' },
        ];
        renderWithRouter(<ContentStatsWidget rows={defaultRows} recentItems={items} />);

        const item = screen.getByText('Item').closest('.content-stats-widget__item');
        expect(item).toHaveAttribute('data-variant', 'fulfilledExternally');
    });
});
