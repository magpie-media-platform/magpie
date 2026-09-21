import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import {
    DownloadQueueWidget,
    type DownloadStateCounts,
    type DownloadState,
    type ActiveDownloadItem,
} from './DownloadQueueWidget';

const renderWithRouter = (ui: React.ReactElement) => {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
};

const defaultCounts: DownloadStateCounts = {
    downloading: 2,
    queued: 5,
    paused: 2,
    stalled: 1,
};

const activeDownloads: ActiveDownloadItem[] = [
    { id: '1', title: 'Breaking Bad S01E01', progress: 45, state: 'downloading', eta: '5m', size: '1.2 GB' },
    { id: '2', title: 'The Office S05E03', progress: 78, state: 'downloading', eta: '2m', size: '850 MB' },
    { id: '3', title: 'Parks and Recreation S02E01', progress: 0, state: 'queued', size: '920 MB' },
];

describe('DownloadQueueWidget', () => {
    it('renders with default heading', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);
        expect(screen.getByRole('heading', { name: 'Downloads' })).toBeInTheDocument();
    });

    it('renders with custom heading', () => {
        renderWithRouter(<DownloadQueueWidget heading="Active Queue" counts={defaultCounts} />);
        expect(screen.getByRole('heading', { name: 'Active Queue' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        renderWithRouter(<DownloadQueueWidget icon={<TestIcon />} counts={defaultCounts} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays total count in header', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);

        expect(screen.getByText('10')).toBeInTheDocument();
    });

    it('displays count for each state', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);

        expect(screen.getByText('Downloading')).toBeInTheDocument();
        expect(screen.getByText('Queued')).toBeInTheDocument();
        expect(screen.getByText('Paused')).toBeInTheDocument();
        expect(screen.getByText('Stalled')).toBeInTheDocument();

        const downloadingRow = screen.getByText('Downloading').closest('.status-row');
        expect(downloadingRow).toHaveTextContent('2');

        const queuedRow = screen.getByText('Queued').closest('.status-row');
        expect(queuedRow).toHaveTextContent('5');

        const pausedRow = screen.getByText('Paused').closest('.status-row');
        expect(pausedRow).toHaveTextContent('2');

        const stalledRow = screen.getByText('Stalled').closest('.status-row');
        expect(stalledRow).toHaveTextContent('1');
    });

    it('applies data-component attribute', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveAttribute('data-component', 'download-queue-widget');
    });

    it('applies custom className', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} className="custom-class" data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveClass('download-queue-widget', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<DownloadQueueWidget ref={ref} counts={defaultCounts} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} data-testid="my-widget" />);
        expect(screen.getByTestId('my-widget')).toBeInTheDocument();
    });
});

describe('DownloadQueueWidget loading state', () => {
    it('shows skeleton rows when loading', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} loading />);

        const skeletonRows = document.querySelectorAll('.status-row [data-component="skeleton"]');
        expect(skeletonRows.length).toBe(4);
    });

    it('shows skeleton items when loading', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} loading />);

        const skeletonItems = document.querySelectorAll('.download-queue-widget__item[data-component="skeleton"]');
        expect(skeletonItems.length).toBe(2);
    });

    it('shows dash for total when loading', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} loading />);
        expect(screen.getByText('-')).toBeInTheDocument();
    });

    it('does not show state labels when loading', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} loading />);
        expect(screen.queryByText('Downloading')).not.toBeInTheDocument();
    });

    it('does not show view all link when loading', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} loading viewAllUrl="/downloads" />);
        expect(screen.queryByText('View all downloads')).not.toBeInTheDocument();
    });
});

describe('DownloadQueueWidget active downloads', () => {
    it('renders active download items', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        expect(screen.getByText('Breaking Bad S01E01')).toBeInTheDocument();
        expect(screen.getByText('The Office S05E03')).toBeInTheDocument();
        expect(screen.getByText('Parks and Recreation S02E01')).toBeInTheDocument();
    });

    it('links a download title to its content when associated content exists', () => {
        const downloads: ActiveDownloadItem[] = [{ ...activeDownloads[0], contentId: 'content-1' }];

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={downloads} />);

        expect(screen.getByText('Breaking Bad S01E01').closest('a')).toHaveAttribute('href', '/library/content-1');
    });

    it('renders a download title without associated content as plain text', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={[activeDownloads[0]]} />);

        expect(screen.getByText('Breaking Bad S01E01').closest('a')).toBeNull();
    });

    it('displays download metadata', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        expect(screen.getByText('5m')).toBeInTheDocument();
        expect(screen.getByText('1.2 GB')).toBeInTheDocument();
        expect(screen.getByText('45%')).toBeInTheDocument();
    });

    it('omits eta and size when not provided', () => {
        const downloads: ActiveDownloadItem[] = [
            { id: '1', title: 'No Meta Download', progress: 30, state: 'downloading' },
        ];

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={downloads} />);

        const item = screen.getByText('No Meta Download').closest('.download-queue-widget__item');
        expect(item).toBeInTheDocument();

        const etaEl = item?.querySelector('.download-queue-widget__item-eta');
        const sizeEl = item?.querySelector('.download-queue-widget__item-size');
        expect(etaEl).toBeNull();
        expect(sizeEl).toBeNull();
    });

    it('limits displayed items to maxItems', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} maxItems={2} />);

        expect(screen.getByText('Breaking Bad S01E01')).toBeInTheDocument();
        expect(screen.getByText('The Office S05E03')).toBeInTheDocument();
        expect(screen.queryByText('Parks and Recreation S02E01')).not.toBeInTheDocument();
    });

    it('renders progress bars with correct progress', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        const progressBars = document.querySelectorAll('[role="progressbar"]');
        expect(progressBars[0]).toHaveAttribute('aria-valuenow', '45');
        expect(progressBars[1]).toHaveAttribute('aria-valuenow', '78');
        expect(progressBars[2]).toHaveAttribute('aria-valuenow', '0');
    });

    it('applies data-download-id to items', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        const items = document.querySelectorAll('[data-download-id]');
        expect(items[0]).toHaveAttribute('data-download-id', '1');
        expect(items[1]).toHaveAttribute('data-download-id', '2');
        expect(items[2]).toHaveAttribute('data-download-id', '3');
    });

    it('applies data-status to items', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        const items = document.querySelectorAll(
            '.download-queue-widget__item:not(.download-queue-widget__item[data-component="skeleton"])',
        );
        expect(items[0]).toHaveAttribute('data-status', 'downloading');
        expect(items[1]).toHaveAttribute('data-status', 'downloading');
        expect(items[2]).toHaveAttribute('data-status', 'queued');
    });

    it('applies data-progress to items', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        const items = document.querySelectorAll(
            '.download-queue-widget__item:not(.download-queue-widget__item[data-component="skeleton"])',
        );
        expect(items[0]).toHaveAttribute('data-progress', '45');
        expect(items[1]).toHaveAttribute('data-progress', '78');
        expect(items[2]).toHaveAttribute('data-progress', '0');
    });
});

describe('DownloadQueueWidget navigation', () => {
    it('renders status rows as links when getStateUrl is provided', () => {
        const getStateUrl = (state: DownloadState) => `/downloads?state=${state}`;

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} getStateUrl={getStateUrl} />);

        const downloadingRow = screen.getByText('Downloading').closest('.status-row');
        expect(downloadingRow?.tagName).toBe('A');
        expect(downloadingRow).toHaveAttribute('href', '/downloads?state=downloading');
    });

    it('renders status rows as divs when getStateUrl is not provided', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);

        const downloadingRow = screen.getByText('Downloading').closest('.status-row');
        expect(downloadingRow?.tagName).toBe('DIV');
    });

    it('does not make row clickable when count is 0', () => {
        const getStateUrl = (state: DownloadState) => `/downloads?state=${state}`;

        renderWithRouter(
            <DownloadQueueWidget
                counts={{ downloading: 1, queued: 0, paused: 0, stalled: 0 }}
                getStateUrl={getStateUrl}
            />,
        );

        const queuedRow = screen.getByText('Queued').closest('.status-row');
        expect(queuedRow?.tagName).toBe('DIV');
        expect(queuedRow).not.toHaveAttribute('data-clickable');
    });

    it('makes row clickable when count is greater than 0 and URL provided', () => {
        const getStateUrl = (state: DownloadState) => `/downloads?state=${state}`;

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} getStateUrl={getStateUrl} />);

        const downloadingRow = screen.getByText('Downloading').closest('.status-row');
        expect(downloadingRow).toHaveAttribute('data-clickable');
    });

    it('renders view all as link when viewAllUrl is provided', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} viewAllUrl="/downloads" />);

        const link = screen.getByText('View all downloads');
        expect(link.tagName).toBe('A');
        expect(link).toHaveAttribute('href', '/downloads');
    });

    it('renders download items as links when getDownloadUrl is provided', () => {
        const getDownloadUrl = (download: ActiveDownloadItem) => `/downloads/${download.id}`;

        renderWithRouter(
            <DownloadQueueWidget
                counts={defaultCounts}
                activeDownloads={activeDownloads}
                getDownloadUrl={getDownloadUrl}
            />,
        );

        const item = screen.getByText('Breaking Bad S01E01').closest('.download-queue-widget__item');
        expect(item?.tagName).toBe('A');
        expect(item).toHaveAttribute('href', '/downloads/1');
    });

    it('renders download items as divs when getDownloadUrl is not provided', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        const item = screen.getByText('Breaking Bad S01E01').closest('.download-queue-widget__item');
        expect(item?.tagName).toBe('DIV');
    });

    it('makes download item clickable when getDownloadUrl is provided', () => {
        const getDownloadUrl = (download: ActiveDownloadItem) => `/downloads/${download.id}`;

        renderWithRouter(
            <DownloadQueueWidget
                counts={defaultCounts}
                activeDownloads={activeDownloads}
                getDownloadUrl={getDownloadUrl}
            />,
        );

        const item = screen.getByText('Breaking Bad S01E01').closest('.download-queue-widget__item');
        expect(item).toHaveAttribute('data-clickable');
    });

    it('links have correct URLs for each state', () => {
        const getStateUrl = (state: DownloadState) => `/downloads?state=${state}`;

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} getStateUrl={getStateUrl} />);

        expect(screen.getByText('Downloading').closest('a')).toHaveAttribute('href', '/downloads?state=downloading');
        expect(screen.getByText('Queued').closest('a')).toHaveAttribute('href', '/downloads?state=queued');
        expect(screen.getByText('Paused').closest('a')).toHaveAttribute('href', '/downloads?state=paused');
        expect(screen.getByText('Stalled').closest('a')).toHaveAttribute('href', '/downloads?state=stalled');
    });
});

describe('DownloadQueueWidget view all link', () => {
    it('shows view all link when viewAllUrl is provided', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} viewAllUrl="/downloads" />);
        expect(screen.getByText('View all downloads')).toBeInTheDocument();
    });

    it('shows view all link even when no downloads exist', () => {
        renderWithRouter(
            <DownloadQueueWidget
                counts={{ downloading: 0, queued: 0, paused: 0, stalled: 0 }}
                activeDownloads={[]}
                viewAllUrl="/downloads"
            />,
        );
        expect(screen.getByText('View all downloads')).toBeInTheDocument();
    });

    it('hides view all link when viewAllUrl is not provided', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);
        expect(screen.queryByText('View all downloads')).not.toBeInTheDocument();
    });

    it('applies data-action attribute to view all link', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} viewAllUrl="/downloads" />);
        expect(screen.getByText('View all downloads')).toHaveAttribute('data-action', 'view-all');
    });
});

describe('DownloadQueueWidget data attributes', () => {
    it('applies data-status to each status row', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);

        expect(screen.getByText('Downloading').closest('.status-row')).toHaveAttribute('data-status', 'downloading');
        expect(screen.getByText('Downloading').closest('.status-row')).toHaveAttribute('data-status', 'downloading');
        expect(screen.getByText('Queued').closest('.status-row')).toHaveAttribute('data-status', 'queued');
        expect(screen.getByText('Paused').closest('.status-row')).toHaveAttribute('data-status', 'paused');
        expect(screen.getByText('Stalled').closest('.status-row')).toHaveAttribute('data-status', 'stalled');
    });
});

describe('DownloadQueueWidget accessibility', () => {
    it('has accessible heading structure', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Downloads' })).toBeInTheDocument();
    });

    it('supports custom heading level', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} headingLevel={3} />);
        expect(screen.getByRole('heading', { level: 3, name: 'Downloads' })).toBeInTheDocument();
    });

    it('clickable rows have aria-label', () => {
        const getStateUrl = (state: DownloadState) => `/downloads?state=${state}`;
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} getStateUrl={getStateUrl} />);

        const downloadingRow = screen.getByText('Downloading').closest('.status-row');
        expect(downloadingRow).toHaveAttribute('aria-label', 'View 2 downloading items');
    });

    it('clickable download items keep their visible title in the accessible name', () => {
        const getDownloadUrl = (download: ActiveDownloadItem) => `/downloads/${download.id}`;
        renderWithRouter(
            <DownloadQueueWidget
                counts={defaultCounts}
                activeDownloads={activeDownloads}
                getDownloadUrl={getDownloadUrl}
            />,
        );

        const item = screen.getByText('Breaking Bad S01E01').closest('.download-queue-widget__item');
        expect(item).not.toHaveAttribute('aria-label');
        expect(item).toHaveAccessibleName('Breaking Bad S01E01 5m 1.2 GB 45 45%');
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        renderWithRouter(<DownloadQueueWidget icon={<TestIcon />} counts={defaultCounts} />);

        const iconWrapper = document.querySelector('.download-queue-widget__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('status indicators are hidden from screen readers', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} />);

        const indicators = document.querySelectorAll('.status-row__indicator');
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('progress bars have proper ARIA attributes', () => {
        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={activeDownloads} />);

        const progressBars = screen.getAllByRole('progressbar');
        progressBars.forEach((bar) => {
            expect(bar).toHaveAttribute('aria-valuemin', '0');
            expect(bar).toHaveAttribute('aria-valuemax', '100');
            expect(bar).toHaveAttribute('aria-valuenow');
        });
    });
});

describe('DownloadQueueWidget progress clamping', () => {
    it('clamps progress to 0 when negative', () => {
        const downloads: ActiveDownloadItem[] = [{ id: '1', title: 'Test', progress: -10, state: 'downloading' }];

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={downloads} />);

        const progressBar = screen.getByRole('progressbar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '0');
        expect(screen.getByText('0%')).toBeInTheDocument();
    });

    it('clamps progress to 100 when over 100', () => {
        const downloads: ActiveDownloadItem[] = [{ id: '1', title: 'Test', progress: 150, state: 'downloading' }];

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={downloads} />);

        const progressBar = screen.getByRole('progressbar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '100');
        expect(screen.getByText('100%')).toBeInTheDocument();
    });

    it('rounds a fractional progress percent for display', () => {
        const downloads: ActiveDownloadItem[] = [
            { id: '1', title: 'Test', progress: 29.9197357204959, state: 'downloading' },
        ];

        renderWithRouter(<DownloadQueueWidget counts={defaultCounts} activeDownloads={downloads} />);

        expect(screen.getByText('30%')).toBeInTheDocument();
        expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '30');
    });
});
