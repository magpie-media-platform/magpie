import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render as rtlRender, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ContentLineageProvider } from '@/components/content/ContentLineageTitle';

import {
    ContentList,
    type ContentItem,
    type ContentHeaderFilters,
    type ContentHeaderFilterHandlers,
} from './ContentList';

function render(ui: React.ReactElement) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return rtlRender(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const mockItems: ContentItem[] = [
    {
        id: '1',
        title: 'Breaking Bad',
        posterUrl: 'https://example.com/poster1.jpg',
        contentType: 'tv',
        levelLabel: 'Episode',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled',
        year: 2008,
        dateAdded: '2024-01-15T10:00:00Z',
    },
    {
        id: '2',
        title: 'Dune: Part Two',
        posterUrl: 'https://example.com/poster2.jpg',
        contentType: 'movie',
        levelLabel: 'Movie',
        monitoring: false,
        effectiveMonitoring: false,
        fulfillment: 'missing',
        year: 2024,
        dateAdded: '2024-06-01T12:00:00Z',
    },
    {
        id: '3',
        title: 'The Office',
        contentType: 'tv',
        levelLabel: 'Episode',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'upgradable',
        year: 2005,
        dateAdded: '2023-03-01T08:00:00Z',
    },
    {
        id: '4',
        title: 'Poor Things',
        posterUrl: 'https://example.com/poster4.jpg',
        contentType: 'movie',
        levelLabel: 'Movie',
        monitoring: false,
        effectiveMonitoring: false,
        fulfillment: 'fulfilledExternally',
        year: 2023,
    },
];

describe('ContentList', () => {
    it('renders with correct data-component attribute', () => {
        render(<ContentList items={mockItems} />);

        expect(document.querySelector('[data-component="content-list"]')).toBeInTheDocument();
    });

    it('renders extension slots for list actions and columns', () => {
        render(<ContentList items={mockItems} />);

        expect(document.querySelector('[data-slot="content.list.actions"]')).toBeInTheDocument();
        expect(document.querySelector('[data-slot="content.list.columns"]')).toBeInTheDocument();
    });

    it('renders content items with titles', () => {
        render(<ContentList items={mockItems} />);

        expect(screen.getByText('Breaking Bad')).toBeInTheDocument();
        expect(screen.getByText('Dune: Part Two')).toBeInTheDocument();
        expect(screen.getByText('The Office')).toBeInTheDocument();
        expect(screen.getByText('Poor Things')).toBeInTheDocument();
    });

    it('resolves a nested title in the desktop content table', async () => {
        render(
            <ContentLineageProvider
                contentTypes={[]}
                loadQualifiedName={async () => ({ id: 'season', title: 'Season 1' })}
            >
                <ContentList items={[{ ...mockItems[0], id: 'pilot', parentId: 'season', title: 'Pilot' }]} />
            </ContentLineageProvider>,
        );

        expect(await screen.findByTitle('Season 1 > Pilot')).toBeInTheDocument();
        expect(screen.getByText('Season 1')).toBeInTheDocument();
        expect(screen.getByText('Pilot')).toBeInTheDocument();
    });

    it('exposes the full title as a tooltip so a clamped long title stays readable', () => {
        const longTitle = 'A'.repeat(200);
        render(<ContentList items={[{ ...mockItems[0], title: longTitle }]} />);

        const titleSpan = document.querySelector('.content-list__title') as HTMLElement;
        expect(titleSpan).not.toBeNull();
        expect(titleSpan).toHaveTextContent(longTitle);
        expect(titleSpan).toHaveAttribute('title', longTitle);
    });

    it('does not render the year under the title (Published column covers the date)', () => {
        render(<ContentList items={mockItems} />);

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

    it('renders fulfillment status badges for monitored items', () => {
        render(<ContentList items={mockItems} />);

        expect(screen.getByText('Fulfilled')).toBeInTheDocument();
        expect(screen.getByText('Upgradable')).toBeInTheDocument();
    });

    it('renders no fulfillment badge for an excluded item', () => {
        render(<ContentList items={mockItems} />);

        // Dune (excluded/missing) and Poor Things (excluded/external) are the only sources of those labels
        expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        expect(screen.queryByText('External')).not.toBeInTheDocument();
    });

    it('shows the in-flight acquisition as a second pill beside the fulfillment pill', () => {
        render(
            <ContentList
                items={[
                    {
                        id: 'dl',
                        title: 'Downloading Movie',
                        contentType: 'movie',
                        levelLabel: 'Movie',
                        monitoring: true,
                        effectiveMonitoring: true,
                        fulfillment: 'missing',
                        acquisitionStatus: 'downloading',
                    },
                ]}
            />,
        );

        const cell = document.querySelector('.content-list__status-cell');
        expect(cell).toBeInTheDocument();

        expect(within(cell as HTMLElement).getByText('Missing')).toBeInTheDocument();

        const badge = cell?.querySelector('[data-acquisition-status="downloading"]');
        expect(badge).toBeInTheDocument();
        expect(badge?.textContent).toBe('Downloading');

        const pills = cell?.querySelectorAll('.badge');
        expect(pills?.[0]).toHaveTextContent('Missing');
        expect(pills?.[1]).toHaveTextContent('Downloading');
    });

    it('renders a single fulfillment pill with no placeholder when no acquisition is in flight', () => {
        render(
            <ContentList
                items={[
                    {
                        id: 'one',
                        title: 'Settled Movie',
                        monitoring: true,
                        effectiveMonitoring: true,
                        fulfillment: 'fulfilled',
                    },
                ]}
            />,
        );

        const cell = document.querySelector('.content-list__status-cell');
        expect(cell?.querySelectorAll('.badge')).toHaveLength(1);
        expect(cell).toHaveTextContent('Fulfilled');
    });

    it('renders monitoring as a labelled eye glyph instead of a named badge', () => {
        render(<ContentList items={mockItems} />);

        const monitored = screen.getAllByLabelText('Monitored');
        const notMonitored = screen.getAllByLabelText('Not monitored');

        expect(monitored).toHaveLength(2);
        expect(notMonitored).toHaveLength(2);

        const monitoredEye = monitored[0].closest('.content-list__monitor-eye');
        const notMonitoredEye = notMonitored[0].closest('.content-list__monitor-eye');
        expect(monitoredEye).toHaveAttribute('data-monitoring', 'true');
        expect(notMonitoredEye).toHaveAttribute('data-monitoring', 'false');
        expect(monitoredEye).toHaveClass('content-list__monitor-eye--on');
        expect(notMonitoredEye).not.toHaveClass('content-list__monitor-eye--on');
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
                    monitoring: true,
                    effectiveMonitoring: true,
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

            const pagination = document.querySelector('.data-table__pager');
            expect(pagination).not.toBeInTheDocument();
        });

        it('shows pagination when enabled', () => {
            const manyItems: ContentItem[] = Array.from({ length: 30 }, (_, i) => ({
                id: String(i),
                title: `Item ${i}`,
                kind: 'movie' as const,
                monitoring: true,
                effectiveMonitoring: true,
                fulfillment: 'fulfilled' as const,
            }));

            render(<ContentList items={manyItems} enablePagination pageSize={10} />);

            const pagination = document.querySelector('.data-table__pager');
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
                expect(img.getAttribute('alt')).toMatch(/artwork/i);
            });
        });

        it('renders the monitoring column header label with its filter control', () => {
            const headerFilters: ContentHeaderFilters = {
                contentTypeOptions: [{ value: 'tv', label: 'TV' }],
                contentType: '',
                monitoring: '',
                fulfillment: '',
                title: '',
                publishDateFrom: '',
                publishDateTo: '',
            };
            const headerFilterHandlers: ContentHeaderFilterHandlers = {
                onContentTypeChange: vi.fn(),
                onMonitoringChange: vi.fn(),
                onFulfillmentChange: vi.fn(),
                onTitleChange: vi.fn(),
                onPublishDateChange: vi.fn(),
            };

            render(
                <ContentList
                    items={mockItems}
                    headerFilters={headerFilters}
                    headerFilterHandlers={headerFilterHandlers}
                />,
            );

            const header = screen.getByRole('columnheader', { name: /monitoring/i });
            const label = within(header).getByText('Monitoring');

            expect(label).toHaveClass('column-header-filter__label');
            expect(label).not.toHaveClass('sr-only');
            expect(within(header).getByRole('button', { name: /filter by monitoring/i })).toBeInTheDocument();
        });
    });

    describe('column visibility', () => {
        it('hides the monitoring and date-added columns when marked not visible', () => {
            render(<ContentList items={mockItems} columnVisibility={{ monitoring: false, dateAdded: false }} />);

            expect(
                document.querySelector('.data-table__header-cell[data-column="monitoring"]'),
            ).not.toBeInTheDocument();
            expect(document.querySelector('.data-table__header-cell[data-column="dateAdded"]')).not.toBeInTheDocument();
            expect(document.querySelector('.data-table__header-cell[data-column="fulfillment"]')).toBeInTheDocument();
            expect(document.querySelector('.data-table__header-cell[data-column="publishDate"]')).toBeInTheDocument();
            expect(screen.queryByText('Added')).not.toBeInTheDocument();
        });
    });

    describe('header filter columns', () => {
        const headerFilters: ContentHeaderFilters = {
            contentTypeOptions: [{ value: 'tv', label: 'TV' }],
            contentType: '',
            monitoring: '',
            fulfillment: '',
            title: '',
            publishDateFrom: '',
            publishDateTo: '',
        };
        const headerFilterHandlers: ContentHeaderFilterHandlers = {
            onContentTypeChange: vi.fn(),
            onMonitoringChange: vi.fn(),
            onFulfillmentChange: vi.fn(),
            onTitleChange: vi.fn(),
            onPublishDateChange: vi.fn(),
        };

        it('renders filter controls only for the listed columns', () => {
            render(
                <ContentList
                    items={mockItems}
                    headerFilters={headerFilters}
                    headerFilterHandlers={headerFilterHandlers}
                    headerFilterColumns={['title', 'contentType', 'fulfillment']}
                />,
            );

            expect(screen.getByRole('button', { name: /filter by title/i })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /filter by type/i })).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /filter by status/i })).toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /filter by published/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /filter by monitoring/i })).not.toBeInTheDocument();
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

        it('select-all is unchecked when no rows are selected', () => {
            render(<ContentList items={mockItems} enableSelection rowSelection={{}} onRowSelectionChange={vi.fn()} />);

            const selectAllCheckbox = screen.getByLabelText('Select all');
            expect(selectAllCheckbox).not.toBeChecked();
            expect(selectAllCheckbox).not.toBePartiallyChecked();
        });

        it('select-all is indeterminate when only a partial subset is selected', () => {
            render(
                <ContentList
                    items={mockItems}
                    enableSelection
                    rowSelection={{ '1': true, '3': true }}
                    onRowSelectionChange={vi.fn()}
                />,
            );

            const selectAllCheckbox = screen.getByLabelText('Select all');
            expect(selectAllCheckbox).toBePartiallyChecked();
            expect(selectAllCheckbox).not.toBeChecked();
        });

        it('select-all is checked when every row is selected', () => {
            render(
                <ContentList
                    items={mockItems}
                    enableSelection
                    rowSelection={{ '1': true, '2': true, '3': true, '4': true }}
                    onRowSelectionChange={vi.fn()}
                />,
            );

            const selectAllCheckbox = screen.getByLabelText('Select all');
            expect(selectAllCheckbox).toBeChecked();
            expect(selectAllCheckbox).not.toBePartiallyChecked();
        });

        it('tags the select column cells so the mobile CSS rule can hide them', () => {
            render(<ContentList items={mockItems} enableSelection />);

            const header = document.querySelector('.data-table__header-cell[data-column="select"]');
            expect(header).toBeInTheDocument();

            const cells = document.querySelectorAll('.data-table__cell[data-column="select"]');
            expect(cells).toHaveLength(mockItems.length);
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

    describe('published column', () => {
        it('renders Published column header', () => {
            render(<ContentList items={mockItems} />);
            expect(screen.getByText('Published')).toBeInTheDocument();
        });

        it('renders a formatted publish date when provided', () => {
            const items: ContentItem[] = [
                {
                    id: 'pub-1',
                    title: 'Dated Item',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                    publishDate: '2008-01-20T00:00:00Z',
                },
            ];

            render(<ContentList items={items} />);

            expect(screen.getByText('Jan 20, 2008')).toBeInTheDocument();
        });

        it('renders a dash when the publish date is missing', () => {
            const items: ContentItem[] = [
                {
                    id: 'no-pub',
                    title: 'Undated Item',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];

            render(<ContentList items={items} />);

            const cell = document.querySelector('.content-list__published');
            expect(cell).toHaveTextContent('--');
        });
    });
});
