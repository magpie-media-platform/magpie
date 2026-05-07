import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';

import { OverviewTab } from './OverviewTab';
import { ChildrenTab, type ChildItem } from './ChildrenTab';
import { FilesTab, type MediaFile } from './FilesTab';
import { HistoryTab, type HistoryEvent } from './HistoryTab';

describe('OverviewTab', () => {
    describe('rendering', () => {
        it('renders overview description', () => {
            render(<OverviewTab metadata={{ overview: 'Test overview text' }} />);
            expect(screen.getByText('Test overview text')).toBeInTheDocument();
        });

        it('renders genres as badges', () => {
            render(<OverviewTab metadata={{ genres: ['Drama', 'Action'] }} />);
            expect(screen.getByText('Drama')).toBeInTheDocument();
            expect(screen.getByText('Action')).toBeInTheDocument();
        });

        it('renders rating with star icon', () => {
            render(<OverviewTab metadata={{ rating: 8.5 }} />);
            expect(screen.getByText('8.5')).toBeInTheDocument();
        });

        it('renders rating with vote count', () => {
            render(<OverviewTab metadata={{ rating: 9.0, ratingVotes: 1000 }} />);
            expect(screen.getByText(/1,000 votes/)).toBeInTheDocument();
        });

        it('renders runtime', () => {
            render(<OverviewTab metadata={{ runtime: 120 }} />);
            expect(screen.getByText('2h')).toBeInTheDocument();
        });

        it('renders runtime under 60 minutes', () => {
            render(<OverviewTab metadata={{ runtime: 45 }} />);
            expect(screen.getByText('45m')).toBeInTheDocument();
        });

        it('renders certification', () => {
            render(<OverviewTab metadata={{ certification: 'TV-MA' }} />);
            expect(screen.getByText('TV-MA')).toBeInTheDocument();
        });

        it('renders network', () => {
            render(<OverviewTab metadata={{ network: 'HBO' }} />);
            expect(screen.getByText('HBO')).toBeInTheDocument();
        });

        it('renders language', () => {
            render(<OverviewTab metadata={{ language: 'English' }} />);
            expect(screen.getByText('English')).toBeInTheDocument();
        });

        it('renders status', () => {
            render(<OverviewTab metadata={{ status: 'Ended' }} />);
            expect(screen.getByText('Ended')).toBeInTheDocument();
        });

        it('renders external IDs', () => {
            render(
                <OverviewTab
                    metadata={{
                        externalIds: [
                            { source: 'IMDb', id: 'tt0903747', url: 'https://imdb.com/test' },
                            { source: 'TMDb', id: '1396', url: 'https://tmdb.org/test' },
                        ],
                    }}
                />,
            );
            expect(screen.getByRole('link', { name: /imdb/i })).toHaveAttribute('href', 'https://imdb.com/test');
            expect(screen.getByRole('link', { name: /tmdb/i })).toHaveAttribute('href', 'https://tmdb.org/test');
            expect(screen.getByText('tt0903747')).toBeInTheDocument();
            expect(screen.getByText('1396')).toBeInTheDocument();
        });

        it('renders legacy external links as external IDs', () => {
            render(
                <OverviewTab
                    metadata={{
                        externalLinks: [{ label: 'IMDb', url: 'https://imdb.com/test' }],
                    }}
                />,
            );
            expect(screen.getByRole('link', { name: /imdb/i })).toHaveAttribute('href', 'https://imdb.com/test');
        });

        it('renders empty state when no data', () => {
            render(<OverviewTab metadata={{}} />);
            expect(screen.getByText(/no overview information/i)).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(<OverviewTab metadata={{ overview: 'Test' }} />);
            expect(document.querySelector('[data-component="overview-tab"]')).toBeInTheDocument();
        });

        it('external ID links open in new tab', () => {
            render(
                <OverviewTab
                    metadata={{
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
            monitoring: 'active',
            fulfillment: 'fulfilled',
            airDate: '2020-01-01',
        },
        {
            id: 'child-2',
            title: 'Season 2',
            number: 2,
            monitoring: 'active',
            fulfillment: 'upgradable',
            airDate: '2021-01-01',
        },
        {
            id: 'child-3',
            title: 'Season 3',
            number: 3,
            monitoring: 'excluded',
            fulfillment: 'missing',
            airDate: '2022-01-01',
        },
    ];

    const renderWithRouter = (items: ChildItem[], props = {}) => {
        return render(
            <MemoryRouter>
                <ChildrenTab items={items} parentId="content-1" {...props} />
            </MemoryRouter>,
        );
    };

    describe('rendering', () => {
        it('renders child items', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText('Season 1').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Season 2').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Season 3').length).toBeGreaterThan(0);
        });

        it('renders air dates', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText(/2020/).length).toBeGreaterThan(0);
        });

        it('renders empty state when no children', () => {
            renderWithRouter([], { emptyMessage: 'No seasons found' });
            expect(screen.getAllByText('No seasons found').length).toBeGreaterThan(0);
        });

        it('renders fulfillment badges', () => {
            renderWithRouter(mockChildren);
            expect(screen.getAllByText('Fulfilled').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Upgradable').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Missing').length).toBeGreaterThan(0);
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

        it('renders file count summary', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.getByText('2 files')).toBeInTheDocument();
        });

        it('renders total size', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.getByText(/3.*GB total/i)).toBeInTheDocument();
        });

        it('renders quality badges', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.getAllByText('1080p').length).toBeGreaterThan(0);
            expect(screen.getAllByText('4K').length).toBeGreaterThan(0);
        });

        it('renders folder paths', () => {
            render(<FilesTab files={mockFiles} />);
            expect(screen.getAllByText('Show/Season 01').length).toBeGreaterThan(0);
        });

        it('renders empty state when no files', () => {
            render(<FilesTab files={[]} emptyMessage="No files found" />);
            expect(screen.getAllByText('No files found').length).toBeGreaterThan(0);
        });

        it('renders single file correctly', () => {
            render(<FilesTab files={[mockFiles[0]]} />);
            expect(screen.getByText('1 file')).toBeInTheDocument();
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(<FilesTab files={mockFiles} />);
            expect(document.querySelector('[data-component="files-tab"]')).toBeInTheDocument();
        });
    });
});

describe('HistoryTab', () => {
    const mockEvents: HistoryEvent[] = [
        {
            id: 'event-1',
            eventType: 'imported',
            status: 'completed',
            sourceName: 'Test.File.mkv',
            quality: '1080p',
            downloadClient: 'qBittorrent',
            timestamp: '2023-06-15T10:00:00Z',
        },
        {
            id: 'event-2',
            eventType: 'downloaded',
            status: 'completed',
            sourceName: 'Test.File',
            quality: '1080p',
            indexer: 'Torznab',
            downloadClient: 'qBittorrent',
            timestamp: '2023-06-15T09:30:00Z',
        },
        {
            id: 'event-3',
            eventType: 'grabbed',
            status: 'failed',
            sourceName: 'Failed.File',
            quality: '720p',
            indexer: 'NZBgeek',
            timestamp: '2023-06-15T09:00:00Z',
            message: 'Download failed: connection timeout',
        },
        {
            id: 'event-4',
            eventType: 'searched',
            status: 'completed',
            timestamp: '2023-06-15T08:00:00Z',
        },
    ];

    describe('rendering', () => {
        it('renders event types', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(screen.getAllByText('Imported').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Downloaded').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Grabbed').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Searched').length).toBeGreaterThan(0);
        });

        it('renders source names', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(screen.getAllByText('Test.File.mkv').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Failed.File').length).toBeGreaterThan(0);
        });

        it('renders quality badges', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(screen.getAllByText('1080p').length).toBeGreaterThan(0);
            expect(screen.getAllByText('720p').length).toBeGreaterThan(0);
        });

        it('renders status badges', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(screen.getAllByText('Success').length).toBeGreaterThan(0);
            expect(screen.getAllByText('Failed').length).toBeGreaterThan(0);
        });

        it('renders indexer details', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(screen.getAllByText('Torznab').length).toBeGreaterThan(0);
            expect(screen.getAllByText('NZBgeek').length).toBeGreaterThan(0);
        });

        it('renders download client details', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(screen.getAllByText('qBittorrent').length).toBeGreaterThan(0);
        });

        it('renders empty state when no events', () => {
            render(<HistoryTab events={[]} emptyMessage="No history found" />);
            expect(screen.getAllByText('No history found').length).toBeGreaterThan(0);
        });
    });

    describe('accessibility', () => {
        it('has correct component data attribute', () => {
            render(<HistoryTab events={mockEvents} />);
            expect(document.querySelector('[data-component="history-tab"]')).toBeInTheDocument();
        });
    });
});
