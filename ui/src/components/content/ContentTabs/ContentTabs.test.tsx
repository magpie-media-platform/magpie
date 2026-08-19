import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { ContentFactsPanel } from './ContentFactsPanel';
import { ChildrenTab, type ChildItem } from './ChildrenTab';
import { FilesTab, type MediaFile } from './FilesTab';
import { PreviewsTab } from './PreviewsTab';

const originalTimeZone = process.env.TZ;

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

beforeEach(() => {
    vi.stubGlobal('matchMedia', createMatchMedia(1024));
});

afterEach(() => {
    vi.unstubAllGlobals();
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
            monitoring: true,
            effectiveMonitoring: true,
            fulfillment: 'fulfilled',
            date: '2020-01-01',
        },
        {
            id: 'child-2',
            title: 'Season 2',
            number: 2,
            monitoring: true,
            effectiveMonitoring: true,
            fulfillment: 'upgradable',
            date: '2021-01-01',
        },
        {
            id: 'child-3',
            title: 'Season 3',
            number: 3,
            monitoring: false,
            effectiveMonitoring: false,
            fulfillment: 'missing',
            date: '2022-01-01',
        },
    ];

    const renderWithRouter = (items: ChildItem[], props = {}) => {
        return render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={items} parentId="content-1" {...props} />
                </MemoryRouter>
            </TooltipProvider>,
        );
    };

    describe('rendering', () => {
        it('renders child items', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText('Season 1').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Season 2').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Season 3').length).toBeGreaterThan(0);
        });

        it('keeps a long unbroken child title contained with the full title available', () => {
            const longTitle = `Child${'UnbrokenTitleSegment'.repeat(8)}`;
            renderWithRouter([
                {
                    id: 'long-child',
                    title: longTitle,
                    subtitle: `Subtitle${'UnbrokenSubtitleSegment'.repeat(6)}`,
                    number: 1,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ]);

            const titles = screen.getAllByTitle(longTitle);
            expect(titles.length).toBeGreaterThan(0);
            expect(titles[0]).toHaveClass('children-tab__title');
            expect(titles[0].closest('[data-column="title"]')).toBeInTheDocument();
        });

        it('renders child dates', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText(/2020/).length).toBeGreaterThan(0);
        });

        it('uses a generic date column header when none is provided', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText('Date').length).toBeGreaterThan(0);
            expect(screen.queryByText('Air Date')).not.toBeInTheDocument();
        });

        it('uses the registry-provided date column header when supplied', () => {
            renderWithRouter(mockChildren, { dateColumnHeader: 'Air Date' });
            expect(screen.getAllByText('Air Date').length).toBeGreaterThan(0);
            expect(screen.queryByText('Date')).not.toBeInTheDocument();
        });

        it('renders empty state when no children', () => {
            renderWithRouter([], { emptyMessage: 'No seasons found' });
            expect(screen.getAllByText('No seasons found').length).toBeGreaterThan(0);
        });

        it('renders fulfillment badges for monitored children', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText('Fulfilled').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Upgradable').length).toBeGreaterThan(0);
        });

        it('suppresses the fulfillment badge for an excluded child', () => {
            renderWithRouter(mockChildren);
            // Season 3 is the only missing child and it is excluded, so no Missing badge shows
            expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        });

        it('surfaces the in-flight acquisition status in the status cell', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                    acquisitionStatus: 'downloading',
                },
            ];
            renderWithRouter(items);
            expect(document.querySelector('[data-acquisition-status="downloading"]')).toBeInTheDocument();
            expect(screen.getAllByText('Downloading').length).toBeGreaterThan(0);
            expect(screen.queryByText('Missing')).not.toBeInTheDocument();
        });

        it('labels monitoring cells with explanatory wording', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByLabelText(/Monitored: Magpie will search/).length).toBeGreaterThan(0);
            expect(screen.getAllByLabelText(/Not monitored: Magpie will not search/).length).toBeGreaterThan(0);
        });

        it('provides accessible compact sort controls defaulting to number ascending', async () => {
            const user = userEvent.setup();
            renderWithRouter(mockChildren);

            const controls = screen.getByTestId('children-tab__sort-controls');
            const field = within(controls).getByRole('combobox', { name: 'Sort children by' });
            const direction = within(controls).getByRole('button', {
                name: 'Sort children by Number descending',
            });

            expect(direction).toBeEnabled();

            await user.click(field);

            expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
                'Number',
                'Title',
                'Date',
                'Status',
            ]);

            await user.click(screen.getByRole('option', { name: 'Title' }));

            expect(direction).toBeEnabled();
            expect(direction).toHaveAccessibleName('Sort children by Title descending');
        });

        it('orders children by sequence number ascending with missing numbers last by default', () => {
            const children: ChildItem[] = [
                {
                    id: 'child-two',
                    title: 'Beta',
                    number: 2,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
                {
                    id: 'child-none',
                    title: 'Extra',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
                {
                    id: 'child-one',
                    title: 'Alpha',
                    number: 1,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(children);

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
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
                {
                    id: 'child-two',
                    title: 'Alpha',
                    number: 2,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(children);

            const getOrder = () =>
                Array.from(document.querySelectorAll('.data-table__row')).map((row) => row.getAttribute('data-row-id'));
            expect(getOrder()).toEqual(['child-one', 'child-two']);

            const field = screen.getByRole('combobox', { name: 'Sort children by' });
            await user.click(field);
            await user.click(screen.getByRole('option', { name: 'Title' }));

            expect(getOrder()).toEqual(['child-two', 'child-one']);
        });

        it('orders mobile cards by sequence number ascending with missing numbers last by default', () => {
            vi.stubGlobal('matchMedia', createMatchMedia(390));
            const children: ChildItem[] = [
                {
                    id: 'child-two',
                    title: 'Beta',
                    number: 2,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
                {
                    id: 'child-none',
                    title: 'Extra',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
                {
                    id: 'child-one',
                    title: 'Alpha',
                    number: 1,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(children);

            const order = Array.from(document.querySelectorAll('.children-tab__mobile-card')).map((card) =>
                card.getAttribute('data-child-id'),
            );
            expect(order).toEqual(['child-one', 'child-two', 'child-none']);
        });

        it('renders only sorted manual child cards on mobile', async () => {
            vi.stubGlobal('matchMedia', createMatchMedia(390));
            const user = userEvent.setup();
            const children: ChildItem[] = [
                {
                    id: 'child-zulu',
                    title: 'Zulu',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'upgradable',
                    date: '2024-02-01',
                },
                {
                    id: 'child-alpha',
                    title: 'Alpha',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                    date: '2024-03-01',
                },
                {
                    id: 'child-beta',
                    title: 'Beta',
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'fulfilled',
                    date: '2024-01-01',
                },
            ];
            renderWithRouter(children);

            expect(document.querySelector('[data-component="children-tab"] .data-table')).not.toBeInTheDocument();
            expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();

            const field = screen.getByRole('combobox', { name: 'Sort children by' });
            const getMobileOrder = () =>
                Array.from(document.querySelectorAll('.children-tab__mobile-card')).map((card) =>
                    card.getAttribute('data-child-id'),
                );
            const expectedAscendingOrder: Record<string, string[]> = {
                Title: ['child-alpha', 'child-beta', 'child-zulu'],
                Date: ['child-beta', 'child-zulu', 'child-alpha'],
                Status: ['child-beta', 'child-alpha', 'child-zulu'],
            };

            for (const [label, expected] of Object.entries(expectedAscendingOrder)) {
                await user.click(field);
                await user.click(screen.getByRole('option', { name: label }));

                expect(getMobileOrder()).toEqual(expected);
            }

            await user.click(screen.getByRole('button', { name: 'Sort children by Status descending' }));

            expect(getMobileOrder()).toEqual(['child-zulu', 'child-alpha', 'child-beta']);
        });

        it('renders purpose-built child cards at the tablet breakpoint', () => {
            vi.stubGlobal('matchMedia', createMatchMedia(768));
            renderWithRouter([mockChildren[0]]);

            expect(document.querySelector('[data-component="children-tab"] .data-table')).not.toBeInTheDocument();
            expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();
            expect(document.querySelector('.children-tab__mobile-card')).toBeInTheDocument();
        });
    });

    describe('effective monitoring', () => {
        it('shows a child with inherited effective monitoring as monitored', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    monitoring: null,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(items);
            expect(screen.getAllByLabelText(/Monitored \(inherited\): Magpie will search/).length).toBeGreaterThan(0);
            expect(document.querySelectorAll('.children-tab__monitoring--inherited').length).toBeGreaterThan(0);
            expect(document.querySelectorAll('.children-tab__monitoring--active').length).toBeGreaterThan(0);
        });

        it('shows a child with explicit disabled monitoring as not monitored', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    monitoring: false,
                    effectiveMonitoring: false,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(items);
            expect(screen.getAllByLabelText(/^Not monitored: Magpie will not search/).length).toBeGreaterThan(0);
            expect(document.querySelectorAll('.children-tab__monitoring--inherited').length).toBe(0);
        });

        it('shows a child with explicit enabled monitoring as monitored', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    monitoring: true,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(items);
            expect(screen.getAllByLabelText(/^Monitored: Magpie will search/).length).toBeGreaterThan(0);
            expect(document.querySelectorAll('.children-tab__monitoring--inherited').length).toBe(0);
        });

        it('shows inherited disabled monitoring as not monitored', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    monitoring: null,
                    effectiveMonitoring: false,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(items);
            expect(
                screen.getAllByLabelText(/^Not monitored \(inherited\): Magpie will not search/).length,
            ).toBeGreaterThan(0);
            expect(document.querySelectorAll('.children-tab__monitoring--inherited').length).toBeGreaterThan(0);
        });

        it('uses the stored effective value without loading a parent state', () => {
            const items: ChildItem[] = [
                {
                    id: 'c1',
                    title: 'Season 1',
                    number: 1,
                    monitoring: null,
                    effectiveMonitoring: true,
                    fulfillment: 'missing',
                },
            ];
            renderWithRouter(items);
            expect(screen.getAllByLabelText(/^Monitored \(inherited\)/).length).toBeGreaterThan(0);
        });
    });

    describe('click handling', () => {
        it('calls onChildClick when row is clicked', async () => {
            const onChildClick = vi.fn();
            renderWithRouter(mockChildren, { onChildClick });

            expect(document.querySelector('[data-component="children-tab"]')).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            renderWithRouter(mockChildren);
            expect(document.querySelector('[data-component="children-tab"]')).toBeInTheDocument();
        });
    });
});

describe('FilesTab', () => {
    const mockFiles: MediaFile[] = [
        {
            id: 'file-1',
            relativePath: 'Show/Season 01/Episode.01.mkv',
            fileName: 'Episode.01.mkv',
            size: 1024 * 1024 * 1024,
            quality: { resolution: '1080p', videoCodec: 'x265' },
            dateAdded: '2023-01-01T00:00:00Z',
        },
        {
            id: 'file-2',
            relativePath: 'Show/Season 01/Episode.02.mkv',
            fileName: 'Episode.02.mkv',
            size: 2 * 1024 * 1024 * 1024,
            quality: { resolution: '4K', videoCodec: 'HEVC' },
            dateAdded: '2023-01-02T00:00:00Z',
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

        it('renders the import action when files already exist', () => {
            render(<FilesTab files={mockFiles} onImportFile={vi.fn()} />);
            expect(screen.getByRole('button', { name: 'Import file' })).toBeInTheDocument();
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

        it('renders one cohesive panel per file with filename, size, and mediainfo', () => {
            render(<FilesTab files={mockFiles} />);
            const panels = document.querySelectorAll('.files-tab__panel');
            expect(panels).toHaveLength(2);
            expect(screen.getAllByText('Episode.01.mkv')).toHaveLength(1);
            expect(panels[0]).toHaveTextContent(/1.*GB/i);
        });
    });

    describe('file actions', () => {
        it('does not render a delete action when onDeleteFile is omitted', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.queryByLabelText('Delete Episode.01.mkv')).not.toBeInTheDocument();
        });

        it('renders a delete action per file when onDeleteFile is provided', () => {
            render(<FilesTab files={mockFiles} onDeleteFile={vi.fn()} />);
            expect(screen.getAllByLabelText('Delete Episode.01.mkv').length).toBeGreaterThan(0);
            expect(screen.getAllByLabelText('Delete Episode.02.mkv').length).toBeGreaterThan(0);
        });

        it('renders a distinct unlink action per file when onUnlinkFile is provided', () => {
            render(<FilesTab files={mockFiles} onUnlinkFile={vi.fn()} />);
            expect(screen.getAllByLabelText('Unlink Episode.01.mkv').length).toBeGreaterThan(0);
            expect(screen.getAllByLabelText('Unlink Episode.02.mkv').length).toBeGreaterThan(0);
        });

        it('invokes onDeleteFile with the file when its action is clicked', () => {
            const onDeleteFile = vi.fn();
            render(<FilesTab files={mockFiles} onDeleteFile={onDeleteFile} />);
            fireEvent.click(screen.getAllByLabelText('Delete Episode.01.mkv')[0]);
            expect(onDeleteFile).toHaveBeenCalledWith(expect.objectContaining({ id: 'file-1' }));
        });

        it('does not invoke a row click when the delete action is clicked', () => {
            const onFileClick = vi.fn();
            const onDeleteFile = vi.fn();
            render(<FilesTab files={mockFiles} onFileClick={onFileClick} onDeleteFile={onDeleteFile} />);
            fireEvent.click(screen.getAllByLabelText('Delete Episode.01.mkv')[0]);
            expect(onDeleteFile).toHaveBeenCalledTimes(1);
            expect(onFileClick).not.toHaveBeenCalled();
        });

        it('invokes unlink without invoking the row click', () => {
            const onFileClick = vi.fn();
            const onUnlinkFile = vi.fn();
            render(<FilesTab files={mockFiles} onFileClick={onFileClick} onUnlinkFile={onUnlinkFile} />);
            fireEvent.click(screen.getAllByLabelText('Unlink Episode.01.mkv')[0]);
            expect(onUnlinkFile).toHaveBeenCalledWith(expect.objectContaining({ id: 'file-1' }));
            expect(onFileClick).not.toHaveBeenCalled();
        });

        it('shows a busy state on the file being deleted', () => {
            render(
                <FilesTab files={mockFiles} onDeleteFile={vi.fn()} onUnlinkFile={vi.fn()} deletingFileId="file-1" />,
            );
            expect(screen.getAllByLabelText('Delete Episode.01.mkv')[0]).toBeDisabled();
            expect(screen.getAllByLabelText('Unlink Episode.01.mkv')[0]).toBeDisabled();
        });

        it('disables both actions while the file is being unlinked', () => {
            render(
                <FilesTab files={mockFiles} onDeleteFile={vi.fn()} onUnlinkFile={vi.fn()} unlinkingFileId="file-1" />,
            );
            expect(screen.getAllByLabelText('Delete Episode.01.mkv')[0]).toBeDisabled();
            expect(screen.getAllByLabelText('Unlink Episode.01.mkv')[0]).toBeDisabled();
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
                quality: { resolution: '1080p', videoCodec: 'x265' },
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
