import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { ContentListItem } from '@/types';

import { SubjectContentTab } from './SubjectContentTab';
import type { SubjectContentController } from './useSubjectContent';
import { TooltipProvider } from '@/components/ui/Tooltip';

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
        const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
        const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
        return {
            matches:
                (minWidth === null || width >= Number(minWidth[1])) &&
                (maxWidth === null || width <= Number(maxWidth[1])),
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        };
    });
}

const SAMPLE_ITEMS: ContentListItem[] = [
    {
        id: 'c1',
        title: 'Breaking Bad',
        level: 'tv-show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: 'Monitored',
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled',
        publishDate: '2008-01-20T00:00:00Z',
        posterUrl: 'https://example.com/p1.jpg',
        year: 2008,
        dateAdded: '2024-01-01T00:00:00Z',
    },
    {
        id: 'c2',
        title: 'Better Call Saul',
        level: 'tv-show',
        levelLabel: 'Show',
        contentType: 'tv',
        monitoring: null,
        effectiveMonitoring: 'Unmonitored',
        fulfillment: 'fulfilledExternally',
        publishDate: '2015-02-08T00:00:00Z',
        posterUrl: 'https://example.com/p2.jpg',
        year: 2015,
        dateAdded: '2024-02-01T00:00:00Z',
    },
];

const noop = () => {};

const baseController: SubjectContentController = {
    contentItems: SAMPLE_ITEMS,
    totalItems: SAMPLE_ITEMS.length,
    isLoading: false,
    error: null,
    sorting: [{ id: 'publishDate', desc: true }],
    onSortingChange: noop,
    pagination: { pageIndex: 0, pageSize: 25 },
    onPaginationChange: noop,
    headerFilters: {
        contentTypeOptions: [],
        contentType: '',
        monitoring: '',
        fulfillment: '',
        title: '',
        publishDateFrom: '',
        publishDateTo: '',
    },
    headerFilterHandlers: {
        onContentTypeChange: noop,
        onMonitoringChange: noop,
        onFulfillmentChange: noop,
        onTitleChange: noop,
        onPublishDateChange: noop,
    },
    pageCount: 1,
    currentPage: 1,
    goToPage: noop,
    hasActiveFilters: false,
};

function renderTab(overrides: Partial<SubjectContentController> = {}) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <SubjectContentTab {...baseController} {...overrides} />
            </MemoryRouter>
        </QueryClientProvider>,
        { wrapper: TooltipProvider },
    );
}

afterEach(() => {
    vi.restoreAllMocks();
});

describe('SubjectContentTab desktop', () => {
    beforeEach(() => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
    });

    it('renders monitoring and fulfillment as separate columns', () => {
        // Arrange
        // Act
        renderTab();

        // Assert
        expect(document.querySelector('.data-table__header-cell[data-column="title"]')).toBeInTheDocument();
        expect(document.querySelector('.data-table__header-cell[data-column="contentType"]')).toBeInTheDocument();
        expect(document.querySelector('.data-table__header-cell[data-column="fulfillment"]')).toBeInTheDocument();
        expect(document.querySelector('.data-table__header-cell[data-column="publishDate"]')).toBeInTheDocument();

        expect(document.querySelector('.data-table__header-cell[data-column="monitoring"]')).toBeInTheDocument();
        expect(document.querySelector('.data-table__header-cell[data-column="dateAdded"]')).not.toBeInTheDocument();
        expect(screen.queryByText('Added')).not.toBeInTheDocument();
    });

    it('renders the title with the normal content-list treatment, not an accent link', () => {
        renderTab();

        const title = screen.getByTitle('Breaking Bad');
        expect(title.tagName).toBe('SPAN');
        expect(title).toHaveClass('content-list__title');
        expect(title.closest('a')).toBeNull();

        const row = title.closest('tr');
        expect(row).toHaveAttribute('data-clickable', 'true');
    });

    it('renders no filter panel', () => {
        // Arrange
        // Act
        renderTab({ hasActiveFilters: true });

        // Assert
        expect(document.querySelector('[data-component="filter-bar"]')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Advanced filter' })).not.toBeInTheDocument();
    });

    it('renders header filters only for Title, Type, and Status', () => {
        renderTab();

        expect(screen.getByRole('button', { name: /filter by title/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by type/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filter by status/i })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /filter by published/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /filter by monitoring/i })).not.toBeInTheDocument();
    });

    it('provides sorting through sortable column headers with no separate sort box', () => {
        renderTab();

        expect(screen.getByRole('button', { name: /sort by title/i })).toBeInTheDocument();
        expect(document.querySelector('[data-component="mobile-list-toolbar"]')).not.toBeInTheDocument();
    });

    it('shows the empty state when there is no content and no filters', () => {
        renderTab({ contentItems: [], totalItems: 0 });

        expect(screen.getByText('No content')).toBeInTheDocument();
    });

    it('shows the no-results state when filters exclude everything', () => {
        renderTab({ contentItems: [], totalItems: 0, hasActiveFilters: true });

        expect(screen.getByText('No results found')).toBeInTheDocument();
    });
});

describe('SubjectContentTab mobile', () => {
    beforeEach(() => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
    });

    it('renders the responsive sort toolbar and cards instead of the table', () => {
        renderTab();

        const toolbar = document.querySelector('[data-component="mobile-list-toolbar"]');
        expect(toolbar).toBeInTheDocument();
        expect(toolbar?.querySelector('[data-sort="field"]')).toBeInTheDocument();
        expect(toolbar?.querySelector('[data-sort="ascending"]')).toBeInTheDocument();
        expect(toolbar?.querySelector('[data-sort="descending"]')).toBeInTheDocument();

        expect(screen.queryByRole('grid')).not.toBeInTheDocument();
        expect(document.querySelector('[data-content-id="c1"]')).toBeInTheDocument();
    });

    it('renders no filter panel', () => {
        // Arrange
        // Act
        renderTab({ hasActiveFilters: true });

        // Assert
        expect(document.querySelector('[data-component="filter-bar"]')).not.toBeInTheDocument();
    });

    it('renders the status badge inside the card meta row', () => {
        // Arrange
        // Act
        renderTab();

        // Assert
        const badge = screen.getByText('Fulfilled');
        expect(badge.closest('.mobile-media-card__meta')).not.toBeNull();
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
        expect(screen.getByText('External')).toBeInTheDocument();
    });
});

describe('SubjectContentTab states', () => {
    beforeEach(() => {
        vi.stubGlobal('matchMedia', createMatchMedia(1440));
    });

    it('renders a loading skeleton while content is loading', () => {
        renderTab({ isLoading: true });

        expect(screen.getByTestId('subject-content-tab__loading')).toBeInTheDocument();
    });

    it('renders an error state when the query fails', () => {
        renderTab({ error: new Error('boom') });

        expect(screen.getByTestId('subject-content-tab__error')).toBeInTheDocument();
    });
});
