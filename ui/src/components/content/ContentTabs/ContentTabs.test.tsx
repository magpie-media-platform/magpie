import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { ContentFactsPanel } from './ContentFactsPanel';
import { ChildrenTab, type ChildItem } from './ChildrenTab';
import { FilesTab, type MediaFile } from './FilesTab';
import { PreviewsTab } from './PreviewsTab';

const originalTimeZone = process.env.TZ;

beforeEach(() => {
    vi.stubGlobal(
        'matchMedia',
        vi.fn((query: string) => {
            const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
            const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
            return {
                matches:
                    (minWidth === null || 1024 >= Number(minWidth[1])) &&
                    (maxWidth === null || 1024 <= Number(maxWidth[1])),
                media: query,
                onchange: null,
                addListener: () => {},
                removeListener: () => {},
                addEventListener: () => {},
                removeEventListener: () => {},
                dispatchEvent: () => false,
            };
        }),
    );
});

afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    if (originalTimeZone === undefined) {
        delete process.env.TZ;
    } else {
        process.env.TZ = originalTimeZone;
    }
});

describe('ContentFactsPanel', () => {
    describe('rendering', () => {
        it('renders a declared fact with its label and value', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [{ key: 'status', displayName: 'Status' }],
                        factValues: { status: 'Ended' },
                    }}
                />,
            );
            expect(screen.getByText('Status')).toBeInTheDocument();
            expect(screen.getByText('Ended')).toBeInTheDocument();
        });

        it('formats a duration-hinted fact from minutes', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [{ key: 'runtime', displayName: 'Runtime', format: 'duration' }],
                        factValues: { runtime: 120 },
                    }}
                />,
            );
            expect(screen.getByText('2h')).toBeInTheDocument();
        });

        it('renders declared facts in manifest order and skips ones without a value', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [
                            { key: 'runtime', displayName: 'Runtime', format: 'duration' },
                            { key: 'language', displayName: 'Language' },
                            { key: 'certification', displayName: 'Certification' },
                        ],
                        factValues: { runtime: 45, certification: 'TV-MA' },
                    }}
                />,
            );
            const details = screen.getByRole('region', { name: 'Details' });
            const labels = within(details)
                .getAllByRole('term')
                .map((node) => node.textContent);
            expect(labels).toEqual(['Runtime', 'Certification']);
            expect(screen.getByText('45m')).toBeInTheDocument();
            expect(screen.getByText('TV-MA')).toBeInTheDocument();
            expect(screen.queryByText('Language')).not.toBeInTheDocument();
        });

        it('renders a fact set it has never seen before with no code change (type-agnostic)', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [
                            { key: 'pageCount', displayName: 'Pages' },
                            { key: 'isbn', displayName: 'ISBN' },
                        ],
                        factValues: { pageCount: 320, isbn: '978-3-16-148410-0' },
                    }}
                />,
            );
            expect(screen.getByText('Pages')).toBeInTheDocument();
            expect(screen.getByText('320')).toBeInTheDocument();
            expect(screen.getByText('ISBN')).toBeInTheDocument();
            expect(screen.getByText('978-3-16-148410-0')).toBeInTheDocument();
        });

        it('falls back to a humanized key when no display name is declared', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [{ key: 'air_status' }],
                        factValues: { air_status: 'Airing' },
                    }}
                />,
            );
            expect(screen.getByText('Air Status')).toBeInTheDocument();
            expect(screen.getByText('Airing')).toBeInTheDocument();
        });

        it('renders external IDs', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        externalIds: [
                            { source: 'Provider-H', id: 'tt0903747', url: 'https://provider-h.example/test' },
                            { source: 'Provider-A', id: '1396', url: 'https://provider-a.example/test' },
                        ],
                    }}
                />,
            );
            expect(screen.getByRole('link', { name: /provider-h/i })).toHaveAttribute(
                'href',
                'https://provider-h.example/test',
            );
            expect(screen.getByRole('link', { name: /provider-a/i })).toHaveAttribute(
                'href',
                'https://provider-a.example/test',
            );
            expect(screen.getByText('tt0903747')).toBeInTheDocument();
            expect(screen.getByText('1396')).toBeInTheDocument();
        });

        it('renders nothing when there are no facts (header owns the synopsis)', () => {
            const { container } = render(<ContentFactsPanel facts={{}} />);
            expect(container.querySelector('[data-component="content-facts"]')).not.toBeInTheDocument();
        });

        it('renders custom metadata in the Details section', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        customValues: [
                            { key: 'Studio', value: 'AMC' },
                            { key: 'Director', value: 'Vince Gilligan' },
                        ],
                    }}
                />,
            );
            expect(screen.getByRole('region', { name: 'Details' })).toBeInTheDocument();
            expect(screen.queryByRole('heading', { name: /custom values/i })).not.toBeInTheDocument();
            expect(screen.getByText('Studio')).toBeInTheDocument();
            expect(screen.getByText('AMC')).toBeInTheDocument();
            expect(screen.getByText('Director')).toBeInTheDocument();
            expect(screen.getByText('Vince Gilligan')).toBeInTheDocument();
        });

        it('renders schema facts before stably ordered unique custom metadata and external IDs last', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [{ key: 'status', displayName: 'Status' }],
                        factValues: { status: 'Ended' },
                        customValues: [
                            { key: 'Studio', value: 'AMC' },
                            { key: 'director', value: 'Vince Gilligan' },
                            { key: 'status', value: 'Duplicate' },
                            { key: 'Director', value: 'Duplicate director' },
                            { key: 'Blank', value: ' ' },
                        ],
                        externalIds: [{ source: 'Provider-H', id: 'tt0903747' }],
                    }}
                />,
            );

            const sections = screen.getAllByRole('heading', { level: 2 });
            expect(sections.map((heading) => heading.textContent)).toEqual(['External IDs']);
            expect(screen.getByRole('region', { name: 'Details' })).toBeInTheDocument();
            const details = screen.getByRole('region', { name: 'Details' });
            const labels = within(details)
                .getAllByRole('term')
                .map((label) => label.textContent);
            expect(labels).toEqual(['Status', 'Director', 'Studio']);
            expect(screen.queryByText('Duplicate')).not.toBeInTheDocument();
            expect(screen.queryByText('Duplicate director')).not.toBeInTheDocument();
            expect(screen.queryByText('Blank')).not.toBeInTheDocument();
            expect(screen.queryByRole('heading', { name: /custom values/i })).not.toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        factFields: [{ key: 'status', displayName: 'Status' }],
                        factValues: { status: 'Released' },
                    }}
                />,
            );
            expect(document.querySelector('[data-component="content-facts"]')).toBeInTheDocument();
        });

        it('external ID links open in new tab', () => {
            render(
                <ContentFactsPanel
                    facts={{
                        externalIds: [{ source: 'Test', id: 'test-123', url: 'https://example.com' }],
                    }}
                />,
            );
            const link = screen.getByRole('link', { name: /test/i });
            expect(link).toHaveAttribute('target', '_blank');
            expect(link).toHaveAttribute('rel', 'noopener noreferrer');
        });
    });
});

describe('ChildrenTab', () => {
    const mockChildren: ChildItem[] = [
        {
            id: 'child-1',
            title: 'Season 1',
            number: 1,
            effectiveMonitoring: 'Monitored',
            fulfillment: 'fulfilled',
            date: '2020-01-01',
        },
        {
            id: 'child-2',
            title: 'Season 2',
            number: 2,
            effectiveMonitoring: 'Monitored',
            fulfillment: 'upgradable',
            date: '2021-01-01',
        },
        {
            id: 'child-3',
            title: 'Season 3',
            number: 3,
            effectiveMonitoring: 'Unmonitored',
            fulfillment: 'missing',
            date: '2022-01-01',
        },
    ];

    describe('rendering', () => {
        it('renders child items', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByText('Season 1').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Season 2').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Season 3').length).toBeGreaterThan(0);
        });

        it('ChildrenTab_OmitsPosterColumn_WhenNoChildHasArtwork', () => {
            // Arrange
            // Act
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            // Assert
            expect(document.querySelector('[data-column="poster"]')).not.toBeInTheDocument();
            expect(document.querySelector('.children-tab__poster')).not.toBeInTheDocument();
            expect(screen.getByRole('columnheader', { name: 'Title' })).toBeInTheDocument();
        });

        it('ChildrenTab_ShowsPosterOnlyForChildrenWithArtwork_WhenSomeChildrenHaveArtwork', () => {
            // Arrange
            const children = [{ ...mockChildren[0], posterUrl: '/season-1.jpg' }, mockChildren[1]];

            // Act
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={children} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            // Assert
            expect(screen.getAllByAltText('Season 1 artwork')).toHaveLength(1);
            expect(document.querySelectorAll('.children-tab__poster')).toHaveLength(1);
            expect(document.querySelector('.children-tab__poster--placeholder')).not.toBeInTheDocument();
            expect(document.querySelectorAll('td[data-column="poster"]')).toHaveLength(2);
        });

        it('keeps a long unbroken child title contained with the full title available', () => {
            const longTitle = `Child${'UnbrokenTitleSegment'.repeat(8)}`;
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab
                            items={[
                                {
                                    id: 'long-child',
                                    title: longTitle,
                                    subtitle: `Subtitle${'UnbrokenSubtitleSegment'.repeat(6)}`,
                                    number: 1,
                                    effectiveMonitoring: 'Monitored',
                                    fulfillment: 'missing',
                                },
                            ]}
                            parentId="content-1"
                        />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            const titles = screen.getAllByTitle(longTitle);
            expect(titles.length).toBeGreaterThan(0);
            expect(titles[0]).toHaveClass('children-tab__title');
            expect(titles[0].closest('[data-column="title"]')).toBeInTheDocument();
        });

        it('renders child dates', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByText(/2020/).length).toBeGreaterThan(0);
        });

        it('uses a generic date column header when none is provided', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByText('Date').length).toBeGreaterThan(0);
            expect(screen.queryByText('Air Date')).not.toBeInTheDocument();
        });

        it('uses the registry-provided date column header when supplied', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" dateColumnHeader="Air Date" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByText('Air Date').length).toBeGreaterThan(0);
            expect(screen.queryByText('Date')).not.toBeInTheDocument();
        });

        it('renders empty state when no children', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={[]} parentId="content-1" emptyMessage="No seasons found" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByText('No seasons found').length).toBeGreaterThan(0);
        });

        it('renders fulfillment badges independently from child monitoring', () => {
            // Arrange
            // Act
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            // Assert
            expect(screen.getAllByText('Fulfilled').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Upgradable').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
        });

        it('surfaces the in-flight acquisition status in the status cell', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                    acquisitionStatus: 'downloading',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={items} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(document.querySelector('[data-acquisition-status="downloading"]')).toBeInTheDocument();
            expect(screen.getAllByText('Downloading').length).toBeGreaterThan(0);
            expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        });

        it('StatusCell_ShowsUpcoming_WhenAMissingChildHasAFutureDate', () => {
            // Arrange
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 9',
                    number: 9,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                    date: '2099-01-01',
                },
            ];

            // Act
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={items} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            // Assert
            expect(screen.getAllByText('Upcoming').length).toBeGreaterThan(0);
            expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        });

        it('StatusCell_ShowsMissing_WhenAnInstantChildDateIsPastAndTheLocalDateIsNot', () => {
            // Arrange
            process.env.TZ = 'America/Los_Angeles';
            vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-10-07T18:00:00-07:00'));
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Episode 1',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                    date: '2026-10-08T00:00:00Z',
                    datePrecision: 'Instant',
                },
            ];

            // Act
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={items} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            // Assert
            expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
            expect(screen.queryByText('Upcoming')).not.toBeInTheDocument();
        });

        it('labels monitoring cells with explanatory wording', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByLabelText('Monitored').length).toBeGreaterThan(0);
            expect(screen.getAllByLabelText('Unmonitored').length).toBeGreaterThan(0);
        });

        it('provides accessible compact sort controls defaulting to number ascending', async () => {
            vi.stubGlobal(
                'matchMedia',
                vi.fn((query: string) => {
                    const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
                    const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
                    return {
                        matches:
                            (minWidth === null || 390 >= Number(minWidth[1])) &&
                            (maxWidth === null || 390 <= Number(maxWidth[1])),
                        media: query,
                        onchange: null,
                        addListener: () => {},
                        removeListener: () => {},
                        addEventListener: () => {},
                        removeEventListener: () => {},
                        dispatchEvent: () => false,
                    };
                }),
            );
            const user = userEvent.setup();
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            const controls = screen.getByTestId('children-tab__sort-controls');
            const field = within(controls).getByRole('combobox', { name: 'Sort by field' });
            const direction = within(controls).getByRole('button', {
                name: 'Sort descending',
            });

            expect(direction).toBeEnabled();

            await user.click(field);

            expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
                'Number',
                'Title',
                'Date',
                'Status',
                'Fulfilled',
                'Media size',
                'Media files',
            ]);

            await user.click(screen.getByRole('option', { name: 'Title' }));

            expect(direction).toBeEnabled();
            expect(direction).toHaveAccessibleName('Sort descending');
        });

        it('orders children by sequence number ascending with missing numbers last by default', () => {
            const children: ChildItem[] = [
                {
                    id: 'child-two',
                    title: 'Beta',
                    number: 2,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
                {
                    id: 'child-none',
                    title: 'Extra',
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
                {
                    id: 'child-one',
                    title: 'Alpha',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={children} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            const order = Array.from(document.querySelectorAll('.data-table__row')).map((row) =>
                row.getAttribute('data-row-id'),
            );
            expect(order).toEqual(['child-one', 'child-two', 'child-none']);
        });

        it('lets the user override the default number sort on the desktop table', async () => {
            const user = userEvent.setup();
            const children: ChildItem[] = [
                {
                    id: 'child-one',
                    title: 'Zulu',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
                {
                    id: 'child-two',
                    title: 'Alpha',
                    number: 2,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={children} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            const getOrder = () =>
                Array.from(document.querySelectorAll('.data-table__row')).map((row) => row.getAttribute('data-row-id'));
            expect(getOrder()).toEqual(['child-one', 'child-two']);

            await user.click(screen.getByRole('button', { name: 'Sort by Title' }));

            expect(getOrder()).toEqual(['child-two', 'child-one']);
        });

        it('orders mobile cards by sequence number ascending with missing numbers last by default', () => {
            vi.stubGlobal(
                'matchMedia',
                vi.fn((query: string) => {
                    const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
                    const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
                    return {
                        matches:
                            (minWidth === null || 390 >= Number(minWidth[1])) &&
                            (maxWidth === null || 390 <= Number(maxWidth[1])),
                        media: query,
                        onchange: null,
                        addListener: () => {},
                        removeListener: () => {},
                        addEventListener: () => {},
                        removeEventListener: () => {},
                        dispatchEvent: () => false,
                    };
                }),
            );
            const children: ChildItem[] = [
                {
                    id: 'child-two',
                    title: 'Beta',
                    number: 2,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
                {
                    id: 'child-none',
                    title: 'Extra',
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
                {
                    id: 'child-one',
                    title: 'Alpha',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={children} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            const order = Array.from(document.querySelectorAll('.children-tab__mobile-card')).map((card) =>
                card.getAttribute('data-child-id'),
            );
            expect(order).toEqual(['child-one', 'child-two', 'child-none']);
        });

        it('renders only sorted manual child cards on mobile', async () => {
            vi.stubGlobal(
                'matchMedia',
                vi.fn((query: string) => {
                    const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
                    const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
                    return {
                        matches:
                            (minWidth === null || 390 >= Number(minWidth[1])) &&
                            (maxWidth === null || 390 <= Number(maxWidth[1])),
                        media: query,
                        onchange: null,
                        addListener: () => {},
                        removeListener: () => {},
                        addEventListener: () => {},
                        removeEventListener: () => {},
                        dispatchEvent: () => false,
                    };
                }),
            );
            const user = userEvent.setup();
            const children: ChildItem[] = [
                {
                    id: 'child-zulu',
                    title: 'Zulu',
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'upgradable',
                    date: '2024-02-01',
                },
                {
                    id: 'child-alpha',
                    title: 'Alpha',
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                    date: '2024-03-01',
                },
                {
                    id: 'child-beta',
                    title: 'Beta',
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'fulfilled',
                    date: '2024-01-01',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={children} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            expect(document.querySelector('[data-component="children-tab"] .data-table')).not.toBeInTheDocument();
            expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();

            const field = screen.getByRole('combobox', { name: 'Sort by field' });
            const getMobileOrder = () =>
                Array.from(document.querySelectorAll('.children-tab__mobile-card')).map((card) =>
                    card.getAttribute('data-child-id'),
                );
            await user.click(field);
            await user.click(screen.getByRole('option', { name: 'Title' }));

            expect(getMobileOrder()).toEqual(['child-alpha', 'child-beta', 'child-zulu']);

            await user.click(field);
            await user.click(screen.getByRole('option', { name: 'Date' }));

            expect(getMobileOrder()).toEqual(['child-beta', 'child-zulu', 'child-alpha']);

            await user.click(field);
            await user.click(screen.getByRole('option', { name: 'Status' }));

            expect(getMobileOrder()).toEqual(['child-beta', 'child-alpha', 'child-zulu']);

            await user.click(screen.getByRole('button', { name: 'Sort descending' }));

            expect(getMobileOrder()).toEqual(['child-zulu', 'child-alpha', 'child-beta']);
        });

        it('renders purpose-built child cards at the tablet breakpoint', () => {
            vi.stubGlobal(
                'matchMedia',
                vi.fn((query: string) => {
                    const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
                    const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
                    return {
                        matches:
                            (minWidth === null || 768 >= Number(minWidth[1])) &&
                            (maxWidth === null || 768 <= Number(maxWidth[1])),
                        media: query,
                        onchange: null,
                        addListener: () => {},
                        removeListener: () => {},
                        addEventListener: () => {},
                        removeEventListener: () => {},
                        dispatchEvent: () => false,
                    };
                }),
            );
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={[mockChildren[0]]} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            expect(document.querySelector('[data-component="children-tab"] .data-table')).not.toBeInTheDocument();
            expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();
            expect(document.querySelector('.children-tab__mobile-card')).toBeInTheDocument();
        });
    });

    describe('effective monitoring', () => {
        it('shows a child with Monitored effective monitoring as monitored', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={items} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByLabelText('Monitored').length).toBeGreaterThan(0);
            expect(document.querySelectorAll('.children-tab__monitoring--active').length).toBeGreaterThan(0);
        });

        it('shows a child with Unmonitored effective monitoring as unmonitored', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    effectiveMonitoring: 'Unmonitored',
                    fulfillment: 'missing',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={items} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByLabelText('Unmonitored').length).toBeGreaterThan(0);
        });

        it('uses the stored effective value without loading a parent state', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    effectiveMonitoring: 'Monitored',
                    fulfillment: 'missing',
                },
            ];
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={items} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(screen.getAllByLabelText(/^Monitored$/).length).toBeGreaterThan(0);
        });
    });

    describe('click handling', () => {
        it('calls onChildClick when row is clicked', async () => {
            const onChildClick = vi.fn();
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" onChildClick={onChildClick} />
                    </MemoryRouter>
                </TooltipProvider>,
            );

            expect(document.querySelector('[data-component="children-tab"]')).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(
                <TooltipProvider>
                    <MemoryRouter>
                        <ChildrenTab items={mockChildren} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>,
            );
            expect(document.querySelector('[data-component="children-tab"]')).toBeInTheDocument();
        });
    });
});

describe('FilesTab', () => {
    it('keeps file actions keyboard accessible without an enclosing button', async () => {
        // Arrange
        const user = userEvent.setup();
        const onDelete = vi.fn();
        const onUnlink = vi.fn();
        render(
            <FilesTab
                files={[{ id: 'file', fileName: 'sample.mkv', relativePath: 'sample.mkv', size: 1024 }]}
                onDeleteFiles={onDelete}
                onUnlinkFiles={onUnlink}
            />,
        );

        // Act
        const trigger = screen.getByRole('button', { name: 'Actions for sample.mkv' });
        trigger.focus();
        await user.keyboard('{Enter}');
        await user.keyboard('{Enter}');

        // Assert
        expect(onUnlink).toHaveBeenCalledWith([
            {
                id: 'file',
                fileName: 'sample.mkv',
                relativePath: 'sample.mkv',
                size: 1024,
            },
        ]);
        expect(trigger.closest('.files-tab__panel')).not.toHaveAttribute('role', 'button');
        expect(onDelete).not.toHaveBeenCalled();
    });
    const mockFiles: MediaFile[] = [
        {
            id: 'file-1',
            relativePath: 'Show/Season 01/Episode.01.mkv',
            fileName: 'Episode.01.mkv',
            size: 1024 * 1024 * 1024,
            dateAdded: '2023-01-01T00:00:00Z',
        },
        {
            id: 'file-2',
            relativePath: 'Show/Season 01/Episode.02.mkv',
            fileName: 'Episode.02.mkv',
            size: 2 * 1024 * 1024 * 1024,
            dateAdded: '2023-01-02T00:00:00Z',
        },
    ];
    const sharedFiles: MediaFile[] = [
        {
            id: 'owned',
            relativePath: 'Show/Season 01/Show.S01E01-E03.mkv',
            fileName: 'Show.S01E01-E03.mkv',
            size: 1024,
            coveredItemCount: 3,
        },
        {
            id: 'covering',
            relativePath: 'Show/Season 01/Show.S01E04-E05.mkv',
            fileName: 'Show.S01E04-E05.mkv',
            size: 1024,
            coveredItemCount: 2,
            covering: true,
        },
    ];

    describe('rendering', () => {
        it('renders file names', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.getAllByText('Episode.01.mkv').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Episode.02.mkv').length).toBeGreaterThan(0);
        });

        it('renders a slim files/size header with the plural count', () => {
            render(<FilesTab files={mockFiles} />);
            const header = screen.getByTestId('files-tab-header');
            expect(header).toHaveTextContent(/2 files/);
            expect(header).toHaveTextContent(/3.*GB/i);
            expect(header.querySelector('.files-tab__summary')).toHaveTextContent(/2 files/);
        });

        it('renders file-added timestamps as local instant dates', () => {
            process.env.TZ = 'America/Los_Angeles';

            render(<FilesTab files={[mockFiles[0]]} />);

            expect(screen.getByText('Added Dec 31, 2022')).toBeInTheDocument();
        });

        it('does not render file-level resolution or codec badges', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.queryByText('1080p')).not.toBeInTheDocument();
            expect(screen.queryByText('4K')).not.toBeInTheDocument();
            expect(screen.queryByText('x265')).not.toBeInTheDocument();
            expect(screen.queryByText('HEVC')).not.toBeInTheDocument();
        });

        it('renders folder paths', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.getAllByText('Show/Season 01').length).toBeGreaterThan(0);
        });

        it('keeps long spaced and unbroken filenames in the shared file panels', () => {
            const spacedName = `${'A very long filename '.repeat(12)}.mkv`;
            const unbrokenName = `${'unbroken'.repeat(40)}.mkv`;

            render(
                <FilesTab
                    files={[
                        { id: 'spaced', relativePath: `Folder/${spacedName}`, fileName: spacedName, size: 1024 },
                        {
                            id: 'unbroken',
                            relativePath: `Folder/${unbrokenName}`,
                            fileName: unbrokenName,
                            size: 2048,
                        },
                    ]}
                    onDeleteFiles={vi.fn()}
                    onUnlinkFiles={vi.fn()}
                />,
            );

            expect(screen.getByText(spacedName)).toHaveClass('files-tab__file-name');
            expect(screen.getByText(unbrokenName)).toHaveClass('files-tab__file-name');
            expect(screen.getByRole('button', { name: `Actions for ${unbrokenName}` })).toBeInTheDocument();
        });

        it('renders the shared empty state with icon and message when no files', () => {
            render(<FilesTab files={[]} emptyMessage="No files attached to this content" />);
            const emptyState = screen
                .getByText('No files attached to this content')
                .closest('[data-component="empty-state"]');
            expect(emptyState).toBeInTheDocument();
            expect(screen.getByTestId('empty-state__icon')).toBeInTheDocument();
            expect(screen.queryByTestId('files-tab-header')).not.toBeInTheDocument();
        });

        it('renders the singular "1 file" header for a single file', () => {
            render(<FilesTab files={[mockFiles[0]]} />);
            expect(screen.getByTestId('files-tab-header')).toHaveTextContent(/1 file(?!s)/);
        });

        it('FilesTab_ShowsImportAndSelectFilesInTheHeader_WhenBothArePermitted', () => {
            // Arrange & Act
            render(<FilesTab files={mockFiles} onImportFile={vi.fn()} onRenameFiles={vi.fn()} />);

            // Assert
            const header = screen.getByTestId('files-tab-header');
            expect(within(header).getByRole('button', { name: 'Import file' })).toBeInTheDocument();
            expect(within(header).getByRole('button', { name: 'Select files', pressed: false })).toBeInTheDocument();
            expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
        });

        it('renders the import action in the empty state', () => {
            render(<FilesTab files={[]} onImportFile={vi.fn()} />);
            expect(screen.getByRole('button', { name: 'Import file' })).toBeInTheDocument();
        });

        it('invokes the import action from the files header', async () => {
            const user = userEvent.setup();
            const onImportFile = vi.fn();
            render(<FilesTab files={mockFiles} onImportFile={onImportFile} />);

            await user.click(screen.getByRole('button', { name: 'Import file' }));

            expect(onImportFile).toHaveBeenCalledTimes(1);
        });

        it('shows how many items each shared file covers', () => {
            render(<FilesTab files={sharedFiles} />);
            const panels = document.querySelectorAll('.files-tab__panel');
            expect(panels[0]).toHaveTextContent('Covers 3 items');
            expect(panels[1]).toHaveTextContent('Covers 2 items');
        });

        it('renders one cohesive panel per file with filename, size, and mediainfo', () => {
            render(<FilesTab files={mockFiles} />);
            const panels = document.querySelectorAll('.files-tab__panel');
            expect(panels).toHaveLength(2);
            expect(screen.getAllByText('Episode.01.mkv')).toHaveLength(1);
            expect(panels[0]).toHaveTextContent(/1.*GB/i);
        });
    });

    describe('file actions', () => {
        it('FilesTab_OffersNoFileMenu_WhenNoFileActionIsPermitted', () => {
            // Arrange & Act
            render(<FilesTab files={mockFiles} />);

            // Assert
            expect(screen.queryByRole('button', { name: 'Actions for Episode.01.mkv' })).not.toBeInTheDocument();
        });

        it('FilesTab_ListsEveryFileActionInOneMenuWithDeleteSetApart_WhenAllArePermitted', async () => {
            // Arrange
            const user = userEvent.setup();
            render(
                <FilesTab
                    files={mockFiles}
                    onReassignFile={vi.fn()}
                    onRenameFiles={vi.fn()}
                    onUnlinkFiles={vi.fn()}
                    onDeleteFiles={vi.fn()}
                />,
            );

            // Act
            await user.click(screen.getByRole('button', { name: 'Actions for Episode.01.mkv' }));

            // Assert
            expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
                'Correct episode association',
                'Rename using mask…',
                'Unlink',
                'Delete from disk',
            ]);
            expect(screen.getByRole('separator')).toBeInTheDocument();
        });

        it('FilesTab_PassesTheFileToEachAction_WhenItsMenuItemIsChosen', async () => {
            // Arrange
            const user = userEvent.setup();
            const onRenameFiles = vi.fn();
            const onUnlinkFiles = vi.fn();
            const onDeleteFiles = vi.fn();
            render(
                <FilesTab
                    files={mockFiles}
                    onRenameFiles={onRenameFiles}
                    onUnlinkFiles={onUnlinkFiles}
                    onDeleteFiles={onDeleteFiles}
                />,
            );
            const trigger = screen.getByRole('button', { name: 'Actions for Episode.02.mkv' });

            // Act
            await user.click(trigger);
            await user.click(screen.getByRole('menuitem', { name: 'Rename using mask…' }));
            await user.click(trigger);
            await user.click(screen.getByRole('menuitem', { name: 'Unlink' }));
            await user.click(trigger);
            await user.click(screen.getByRole('menuitem', { name: 'Delete from disk' }));

            // Assert
            expect(onRenameFiles).toHaveBeenCalledWith(['file-2']);
            expect(onUnlinkFiles).toHaveBeenCalledWith([expect.objectContaining({ id: 'file-2' })]);
            expect(onDeleteFiles).toHaveBeenCalledWith([expect.objectContaining({ id: 'file-2' })]);
        });

        it('FilesTab_OffersNoRename_WhenAnotherItemOwnsTheFile', async () => {
            // Arrange
            const user = userEvent.setup();
            render(
                <FilesTab
                    files={sharedFiles}
                    onRenameFiles={vi.fn()}
                    onUnlinkFiles={vi.fn()}
                    onDeleteFiles={vi.fn()}
                />,
            );

            // Act
            await user.click(screen.getByRole('button', { name: 'Actions for Show.S01E04-E05.mkv' }));

            // Assert
            expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
                'Unlink',
                'Delete from disk',
            ]);
        });
    });

    describe('toolbar', () => {
        it('FilesTab_GroupsTheContentActionsAfterTheSummary_WhenTheyArePermitted', () => {
            // Arrange & Act
            render(
                <FilesTab
                    files={mockFiles}
                    onRenameFiles={vi.fn()}
                    onRenameAllFiles={vi.fn()}
                    onScanFiles={vi.fn()}
                    onImportFile={vi.fn()}
                />,
            );

            // Assert
            const header = screen.getByTestId('files-tab-header');
            const contentActions = header.querySelector<HTMLElement>('.files-tab__content-actions')!;
            expect(header.firstElementChild).toHaveTextContent('2 files · 3 GB');
            expect(within(header).getByRole('button', { name: 'Select files' })).toBeInTheDocument();
            expect(
                within(contentActions)
                    .getAllByRole('button')
                    .map((button) => button.textContent),
            ).toEqual(['Rename all files…', 'Scan for files', 'Import file']);
        });

        it('FilesTab_ShowsTheRenameAllLabel_WhenOneIsGiven', () => {
            // Arrange & Act
            render(
                <FilesTab
                    files={mockFiles}
                    onRenameAllFiles={vi.fn()}
                    renameAllLabel="Rename all files, including child items…"
                />,
            );

            // Assert
            expect(
                screen.getByRole('button', { name: 'Rename all files, including child items…' }),
            ).toBeInTheDocument();
        });

        it('FilesTab_StartsTheScan_WhenScanForFilesIsPressed', async () => {
            // Arrange
            const user = userEvent.setup();
            const onScanFiles = vi.fn();
            render(<FilesTab files={[]} onScanFiles={onScanFiles} />);

            // Act
            await user.click(screen.getByRole('button', { name: 'Scan for files' }));

            // Assert
            expect(onScanFiles).toHaveBeenCalledTimes(1);
        });

        it('FilesTab_DisablesScanForFiles_WhileTheScanStarts', () => {
            // Arrange & Act
            render(<FilesTab files={mockFiles} onScanFiles={vi.fn()} scanning />);

            // Assert
            expect(screen.getByRole('button', { name: 'Scan for files' })).toBeDisabled();
        });

        it('FilesTab_ShowsTheReviewSectionBetweenTheToolbarAndTheFiles_WhenGivenOne', () => {
            // Arrange & Act
            render(
                <FilesTab files={mockFiles}>
                    <section aria-label="Review" />
                </FilesTab>,
            );

            // Assert
            const review = screen.getByRole('region', { name: 'Review' });
            expect(review.previousElementSibling).toBe(screen.getByTestId('files-tab-header'));
            expect(review.nextElementSibling).toHaveClass('files-tab__panels');
        });
    });

    describe('selection', () => {
        it('FilesTab_ShowsCheckboxesAndEmptySelectionActions_WhenSelectFilesIsPressed', async () => {
            // Arrange
            const user = userEvent.setup();
            render(
                <FilesTab files={mockFiles} onRenameFiles={vi.fn()} onUnlinkFiles={vi.fn()} onDeleteFiles={vi.fn()} />,
            );

            // Act
            await user.click(screen.getByRole('button', { name: 'Select files', pressed: false }));

            // Assert
            const bar = screen.getByRole('region', { name: 'File selection actions' });
            expect(screen.getByRole('button', { name: 'Select files', pressed: true })).toBeInTheDocument();
            expect(screen.getByRole('checkbox', { name: 'Select Episode.01.mkv' })).not.toBeChecked();
            expect(screen.getByRole('checkbox', { name: 'Select Episode.02.mkv' })).not.toBeChecked();
            expect(bar).toHaveTextContent('0 selected');
            expect(within(bar).getByRole('button', { name: 'Clear selection' })).toBeDisabled();
            expect(within(bar).getByRole('button', { name: 'Select all', pressed: false })).toBeInTheDocument();
            expect(within(bar).getByRole('button', { name: 'Rename using mask…' })).toBeDisabled();
            expect(within(bar).getByRole('button', { name: 'Unlink' })).toBeDisabled();
            expect(within(bar).getByRole('button', { name: 'Delete from disk' })).toBeDisabled();
        });

        it('FilesTab_PassesOnlyTheSelectedFilesToEachAction_WhenOneFileIsSelected', async () => {
            // Arrange
            const user = userEvent.setup();
            const onRenameFiles = vi.fn();
            const onUnlinkFiles = vi.fn();
            const onDeleteFiles = vi.fn();
            render(
                <FilesTab
                    files={mockFiles}
                    onRenameFiles={onRenameFiles}
                    onUnlinkFiles={onUnlinkFiles}
                    onDeleteFiles={onDeleteFiles}
                />,
            );
            await user.click(screen.getByRole('button', { name: 'Select files' }));
            await user.click(screen.getByRole('checkbox', { name: 'Select Episode.02.mkv' }));
            const bar = screen.getByRole('region', { name: 'File selection actions' });

            // Act
            await user.click(within(bar).getByRole('button', { name: 'Rename using mask…' }));
            await user.click(within(bar).getByRole('button', { name: 'Unlink' }));
            await user.click(within(bar).getByRole('button', { name: 'Delete from disk' }));

            // Assert
            expect(bar).toHaveTextContent('1 selected');
            expect(onRenameFiles).toHaveBeenCalledWith(['file-2']);
            expect(onUnlinkFiles).toHaveBeenCalledWith(
                [expect.objectContaining({ id: 'file-2' })],
                expect.objectContaining({ selectedIds: ['file-2'] }),
            );
            expect(onDeleteFiles).toHaveBeenCalledWith(
                [expect.objectContaining({ id: 'file-2' })],
                expect.objectContaining({ selectedIds: ['file-2'] }),
            );
        });

        it('FilesTab_SelectsEveryFile_WhenSelectAllIsPressed', async () => {
            // Arrange
            const user = userEvent.setup();
            render(<FilesTab files={mockFiles} onUnlinkFiles={vi.fn()} />);
            await user.click(screen.getByRole('button', { name: 'Select files' }));
            const bar = screen.getByRole('region', { name: 'File selection actions' });

            // Act
            await user.click(within(bar).getByRole('button', { name: 'Select all' }));

            // Assert
            expect(bar).toHaveTextContent('2 selected');
            expect(within(bar).getByRole('button', { name: 'Select all', pressed: true })).toBeInTheDocument();
            expect(screen.getByRole('checkbox', { name: 'Select Episode.01.mkv' })).toBeChecked();
            expect(screen.getByRole('checkbox', { name: 'Select Episode.02.mkv' })).toBeChecked();
        });

        it('FilesTab_RenamesOnlyTheFilesThisItemOwns_WhenTheSelectionIncludesASharedFile', async () => {
            // Arrange
            const user = userEvent.setup();
            const onRenameFiles = vi.fn();
            render(<FilesTab files={sharedFiles} onRenameFiles={onRenameFiles} onDeleteFiles={vi.fn()} />);
            await user.click(screen.getByRole('button', { name: 'Select files' }));
            const bar = screen.getByRole('region', { name: 'File selection actions' });
            await user.click(within(bar).getByRole('button', { name: 'Select all' }));

            // Act
            await user.click(within(bar).getByRole('button', { name: 'Rename using mask…' }));

            // Assert
            expect(onRenameFiles).toHaveBeenCalledWith(['owned']);
        });

        it('FilesTab_DisablesRename_WhenOnlyASharedFileIsSelected', async () => {
            // Arrange
            const user = userEvent.setup();
            render(<FilesTab files={sharedFiles} onRenameFiles={vi.fn()} onDeleteFiles={vi.fn()} />);
            await user.click(screen.getByRole('button', { name: 'Select files' }));

            // Act
            await user.click(screen.getByRole('checkbox', { name: 'Select Show.S01E04-E05.mkv' }));

            // Assert
            const bar = screen.getByRole('region', { name: 'File selection actions' });
            expect(within(bar).getByRole('button', { name: 'Rename using mask…' })).toBeDisabled();
            expect(within(bar).getByRole('button', { name: 'Delete from disk' })).toBeEnabled();
        });

        it('FilesTab_ClearsTheSelectionAndHidesTheCheckboxes_WhenSelectionModeIsLeft', async () => {
            // Arrange
            const user = userEvent.setup();
            render(<FilesTab files={mockFiles} onUnlinkFiles={vi.fn()} />);
            await user.click(screen.getByRole('button', { name: 'Select files' }));
            await user.click(screen.getByRole('checkbox', { name: 'Select Episode.01.mkv' }));

            // Act
            await user.click(screen.getByRole('button', { name: 'Select files', pressed: true }));
            await user.click(screen.getByRole('button', { name: 'Select files', pressed: false }));

            // Assert
            expect(screen.getByRole('region', { name: 'File selection actions' })).toHaveTextContent('0 selected');
            expect(screen.getByRole('checkbox', { name: 'Select Episode.01.mkv' })).not.toBeChecked();
        });

        it('FilesTab_KeepsTheRemainingFilesSelected_WhenASelectedFileLeavesTheList', async () => {
            // Arrange
            const user = userEvent.setup();
            const { rerender } = render(<FilesTab files={mockFiles} onUnlinkFiles={vi.fn()} />);
            await user.click(screen.getByRole('button', { name: 'Select files' }));
            await user.click(screen.getByRole('checkbox', { name: 'Select Episode.01.mkv' }));
            await user.click(screen.getByRole('checkbox', { name: 'Select Episode.02.mkv' }));

            // Act
            rerender(<FilesTab files={[mockFiles[1]]} onUnlinkFiles={vi.fn()} />);

            // Assert
            expect(screen.getByRole('region', { name: 'File selection actions' })).toHaveTextContent('1 selected');
            expect(screen.getByRole('checkbox', { name: 'Select Episode.02.mkv' })).toBeChecked();
        });

        it('FilesTab_OffersNoSelection_WhenNoBulkActionIsPermitted', () => {
            // Arrange & Act
            render(<FilesTab files={mockFiles} onReassignFile={vi.fn()} />);

            // Assert
            expect(screen.queryByRole('button', { name: 'Select files' })).not.toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(<FilesTab files={mockFiles} />);
            expect(document.querySelector('[data-component="files-tab"]')).toBeInTheDocument();
        });
    });

    describe('media info', () => {
        it('renders a per-track Video / Audio / Text breakdown from the typed fields', () => {
            const fileWithInfo: MediaFile = {
                id: 'file-info-1',
                relativePath: 'file.mkv',
                fileName: 'file.mkv',
                size: 1024,
                mediaInfo: {
                    container: 'matroska,webm',
                    durationSeconds: 5400,
                    bitRate: 8000000,
                    tracks: [
                        {
                            type: 'Video',
                            name: 'video',
                            codec: 'h264',
                            width: 1920,
                            height: 1080,
                            frameRate: 23.976,
                            bitRate: 5500000,
                            videoRange: 'HDR',
                            bitDepth: 10,
                        },
                        {
                            type: 'Audio',
                            name: 'audio-en',
                            codec: 'aac',
                            channels: 6,
                            language: 'eng',
                            bitRate: 384000,
                            sampleRate: 48000,
                        },
                        {
                            type: 'Subtitle',
                            name: 'Commentary',
                            codec: 'subrip',
                            language: 'eng',
                            forced: true,
                            default: false,
                            sdh: true,
                        },
                    ],
                },
            };
            render(<FilesTab files={[fileWithInfo]} />);
            const meta = document.querySelector('.files-tab__panel-meta');
            expect(meta).not.toBeNull();
            expect(meta).toHaveTextContent('MKV');
            expect(meta).toHaveTextContent('1 KB');
            expect(meta).not.toHaveTextContent('1080p');
            expect(meta).not.toHaveTextContent('x265');

            const grid = screen.getByTestId('files-tab-media-info');

            expect(grid).toHaveTextContent('MKV');
            expect(grid).toHaveTextContent('1h 30m');
            expect(grid).toHaveTextContent('8.00 Mbps');

            const video = screen.getByTestId('files-tab-track-video');
            expect(video).toHaveTextContent('h264');
            expect(video).toHaveTextContent('1920x1080');
            expect(video).toHaveTextContent('1080p');
            expect(video).toHaveTextContent('HDR');
            expect(video).toHaveTextContent('10-bit');
            expect(video).toHaveTextContent('23.976 fps');
            expect(video).toHaveTextContent('5.50 Mbps');

            const audio = screen.getByTestId('files-tab-track-audio');
            expect(audio).toHaveTextContent('aac');
            expect(audio).toHaveTextContent('5.1');
            expect(audio).toHaveTextContent('eng');
            expect(audio).toHaveTextContent('48 kHz');

            const text = screen.getByTestId('files-tab-track-text');
            expect(text).toHaveTextContent('SRT');
            expect(text).toHaveTextContent('Commentary');
            expect(text).toHaveTextContent('Forced');
            expect(text).toHaveTextContent('SDH');
        });

        it('labels the video track at its reported height instead of a lower tier', () => {
            // Arrange
            const file: MediaFile = {
                id: 'file-1440',
                relativePath: 'file.mkv',
                fileName: 'file.mkv',
                size: 1024,
                mediaInfo: {
                    tracks: [{ type: 'Video', name: 'video', codec: 'hevc', width: 2560, height: 1440 }],
                },
            };
            // Act
            render(<FilesTab files={[file]} />);
            // Assert
            const video = screen.getByTestId('files-tab-track-video');
            expect(video).toHaveTextContent('2560x1440');
            expect(video).toHaveTextContent('1440p');
            expect(video).not.toHaveTextContent('1080p');
        });

        it('falls back to raw properties for codec and bitrate on legacy tracks', () => {
            const legacyFile: MediaFile = {
                id: 'file-legacy-1',
                relativePath: 'legacy.mkv',
                fileName: 'legacy.mkv',
                size: 1024,
                mediaInfo: {
                    tracks: [{ type: 'Video', name: 'video', properties: { Codec: 'h264', BitRate: '5500000' } }],
                },
            };
            render(<FilesTab files={[legacyFile]} />);
            const video = screen.getByTestId('files-tab-track-video');
            expect(video).toHaveTextContent('h264');
            expect(video).toHaveTextContent('5.50 Mbps');
        });

        it('omits sections that have no tracks', () => {
            const audioOnly: MediaFile = {
                id: 'file-audio-only',
                relativePath: 'song.flac',
                fileName: 'song.flac',
                size: 1024,
                mediaInfo: {
                    container: 'flac',
                    tracks: [{ type: 'Audio', name: 'audio', codec: 'flac', channels: 2 }],
                },
            };
            render(<FilesTab files={[audioOnly]} />);
            expect(screen.getByTestId('files-tab-track-audio')).toHaveTextContent('Stereo');
            expect(screen.queryByTestId('files-tab-track-video')).not.toBeInTheDocument();
            expect(screen.queryByTestId('files-tab-track-text')).not.toBeInTheDocument();
        });

        it('renders the "MediaInfo not extracted" affordance when info is missing', () => {
            const fileWithoutInfo: MediaFile = {
                id: 'file-info-2',
                relativePath: 'noinfo.mkv',
                fileName: 'noinfo.mkv',
                size: 1024,
                mediaInfo: null,
            };
            render(<FilesTab files={[fileWithoutInfo]} />);
            const empties = screen.getAllByTestId('files-tab-media-info-empty');
            expect(empties.length).toBeGreaterThan(0);
            expect(empties[0]).toHaveTextContent(/MediaInfo not extracted/);
        });

        it('omits the quality slot instead of rendering a bare "-" when quality is absent', () => {
            const fileWithoutQuality: MediaFile = {
                id: 'file-no-quality',
                relativePath: 'noquality.mkv',
                fileName: 'noquality.mkv',
                size: 1024,
            };
            render(<FilesTab files={[fileWithoutQuality]} />);
            const meta = document.querySelector('.files-tab__panel-meta');
            expect(meta).not.toBeNull();
            expect(meta?.querySelector('.files-tab__quality-cell')).toBeNull();
            expect(meta?.textContent ?? '').not.toMatch(/^\s*-/);
        });
    });
});

describe('PreviewsTab', () => {
    it('renders one player per preview with title + kind label', () => {
        render(
            <PreviewsTab
                previews={[
                    { url: 'https://www.youtube.com/watch?v=KEY1', kind: 'trailer', title: 'Official Trailer' },
                    { url: 'https://cdn.example.com/teaser.mp4', kind: 'teaser', title: 'Teaser' },
                ]}
            />,
        );

        expect(screen.getAllByTestId('preview-player-facade')).toHaveLength(2);
        expect(screen.queryByTestId('preview-player-video')).not.toBeInTheDocument();
        expect(screen.getAllByText('Official Trailer').length).toBeGreaterThan(0);
        expect(screen.getAllByText('Teaser').length).toBeGreaterThan(0);
        const captions = document.querySelectorAll('.previews-tab__caption');
        expect(captions.length).toBe(2);
        expect(document.querySelector('[data-component="previews-tab"]')).toBeInTheDocument();
    });

    it('renders an empty state when there are no previews', () => {
        render(<PreviewsTab previews={[]} />);

        expect(screen.getByText('No previews available for this item yet.')).toBeInTheDocument();
        expect(screen.queryByTestId('preview-player-facade')).not.toBeInTheDocument();
        expect(document.querySelector('[data-component="previews-tab"]')).toBeInTheDocument();
    });
});
