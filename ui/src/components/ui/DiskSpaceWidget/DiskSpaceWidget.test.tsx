import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { DiskSpaceWidget, type DiskSpaceInfo } from './DiskSpaceWidget';

const GB = 1024 * 1024 * 1024;
const TB = 1024 * 1024 * 1024 * 1024;

const mockFolders: DiskSpaceInfo[] = [
    {
        id: 'rf-1',
        name: 'TV Shows',
        path: '/mnt/media/tv',
        usedBytes: 2.1 * TB,
        totalBytes: 4 * TB,
        freeBytes: 1.9 * TB,
        contentBytes: 1.5 * TB,
    },
    {
        id: 'rf-2',
        name: 'Movies',
        path: '/mnt/media/movies',
        usedBytes: 3.5 * TB,
        totalBytes: 4 * TB,
        freeBytes: 0.5 * TB,
        contentBytes: 3 * TB,
    },
    {
        id: 'rf-3',
        name: 'Music',
        path: '/mnt/media/music',
        usedBytes: 180 * GB,
        totalBytes: 500 * GB,
        freeBytes: 320 * GB,
        contentBytes: 120 * GB,
    },
];

describe('DiskSpaceWidget', () => {
    it('renders with default heading', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByRole('heading', { name: 'Disk Space' })).toBeInTheDocument();
    });

    it('renders with custom heading', () => {
        render(<DiskSpaceWidget heading="Storage" folders={mockFolders} />);
        expect(screen.getByRole('heading', { name: 'Storage' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        render(<DiskSpaceWidget icon={<TestIcon />} folders={mockFolders} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays folder count in header', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByText('3 folders')).toBeInTheDocument();
    });

    it('displays singular folder count', () => {
        render(<DiskSpaceWidget folders={[mockFolders[0]]} />);
        expect(screen.getByText('1 folder')).toBeInTheDocument();
    });

    it('displays folder names', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByText('TV Shows')).toBeInTheDocument();
        expect(screen.getByText('Movies')).toBeInTheDocument();
        expect(screen.getByText('Music')).toBeInTheDocument();
    });

    it('displays folder paths', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByText('/mnt/media/tv')).toBeInTheDocument();
        expect(screen.getByText('/mnt/media/movies')).toBeInTheDocument();
        expect(screen.getByText('/mnt/media/music')).toBeInTheDocument();
    });

    it('applies data-component attribute', () => {
        render(<DiskSpaceWidget folders={mockFolders} data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveAttribute('data-component', 'disk-space-widget');
    });

    it('applies custom className', () => {
        render(<DiskSpaceWidget folders={mockFolders} className="custom-class" data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveClass('disk-space-widget', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<DiskSpaceWidget ref={ref} folders={mockFolders} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        render(<DiskSpaceWidget folders={mockFolders} data-testid="my-widget" />);
        expect(screen.getByTestId('my-widget')).toBeInTheDocument();
    });
});

describe('DiskSpaceWidget loading state', () => {
    it('shows skeleton rows when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading />);
        const skeletonRows = document.querySelectorAll('.disk-space-widget__row--skeleton');
        expect(skeletonRows.length).toBe(2);
    });

    it('shows dash for folder count when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading />);
        expect(screen.getByText(/- folders?/)).toBeInTheDocument();
    });

    it('does not show folder names when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading />);
        expect(screen.queryByText('TV Shows')).not.toBeInTheDocument();
    });

    it('does not show view all button when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading onViewAllClick={() => {}} />);
        expect(screen.queryByText('Manage root folders')).not.toBeInTheDocument();
    });
});

describe('DiskSpaceWidget failed state', () => {
    it('DiskSpaceWidget_ShowsTheFailedRequestStateAndRetries_WhenTheRequestFails', async () => {
        // Arrange
        const user = userEvent.setup();
        const onRetry = vi.fn();
        render(
            <DiskSpaceWidget folders={[]} error={new Error('offline')} onRetry={onRetry} onViewAllClick={vi.fn()} />,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Try again' }));

        // Assert
        expect(screen.getByRole('alert')).toHaveTextContent('Failed to load');
        expect(screen.getByText('- folders')).toBeInTheDocument();
        expect(screen.queryByText('No root folders configured')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Manage root folders' })).not.toBeInTheDocument();
        expect(onRetry).toHaveBeenCalledTimes(1);
    });
});

describe('DiskSpaceWidget empty state', () => {
    it('shows empty message when no folders', () => {
        render(<DiskSpaceWidget folders={[]} />);
        expect(screen.getByText('No root folders configured')).toBeInTheDocument();
    });

    it('does not show view all button when no folders', () => {
        render(<DiskSpaceWidget folders={[]} onViewAllClick={() => {}} />);
        expect(screen.queryByText('Manage root folders')).not.toBeInTheDocument();
    });
});

describe('DiskSpaceWidget figures', () => {
    it('shows the content size, free space and disk size of each folder', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        const tv = within(screen.getByText('TV Shows').closest<HTMLElement>('[data-folder-id]')!);
        expect(tv.getByText('1.50 TB content')).toBeInTheDocument();
        expect(tv.getByText('1.90 TB free')).toBeInTheDocument();
        expect(tv.getByText('4.00 TB total')).toBeInTheDocument();
        const music = within(screen.getByText('Music').closest<HTMLElement>('[data-folder-id]')!);
        expect(music.getByText('120.00 GB content')).toBeInTheDocument();
        expect(music.getByText('320.00 GB free')).toBeInTheDocument();
        expect(music.getByText('500.00 GB total')).toBeInTheDocument();
    });

    it('shows no total across the folders', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        expect(screen.queryByText('Total Used')).not.toBeInTheDocument();
        expect(screen.queryByText('Total Free')).not.toBeInTheDocument();
        expect(screen.queryByText('Total Capacity')).not.toBeInTheDocument();
        expect(document.querySelector('.disk-space-widget__summary')).not.toBeInTheDocument();
    });
});

describe('DiskSpaceWidget usage percentage', () => {
    it('displays usage percentage for each folder', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        expect(screen.getByText('53%')).toBeInTheDocument();

        expect(screen.getByText('88%')).toBeInTheDocument();

        expect(screen.getByText('36%')).toBeInTheDocument();
    });

    it('applies healthy status for usage below 70%', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 60 * GB,
            totalBytes: 100 * GB,
            freeBytes: 40 * GB,
            contentBytes: 0,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        const row = screen.getByText('60%').closest('[data-folder-id]');
        expect(row).toHaveAttribute('data-usage-status', 'healthy');
    });

    it('applies warning status for usage between 70-85%', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 75 * GB,
            totalBytes: 100 * GB,
            freeBytes: 25 * GB,
            contentBytes: 0,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        const row = screen.getByText('75%').closest('[data-folder-id]');
        expect(row).toHaveAttribute('data-usage-status', 'warning');
    });

    it('applies critical status for usage above 85%', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 90 * GB,
            totalBytes: 100 * GB,
            freeBytes: 10 * GB,
            contentBytes: 0,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        const row = screen.getByText('90%').closest('[data-folder-id]');
        expect(row).toHaveAttribute('data-usage-status', 'critical');
    });
});

describe('DiskSpaceWidget progress bar', () => {
    it('renders progress bar for each folder', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        const progressBars = document.querySelectorAll('.disk-space-widget__progress-bar');
        expect(progressBars.length).toBe(3);
    });

    it('sets correct width on progress bar', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 50 * GB,
            totalBytes: 100 * GB,
            freeBytes: 50 * GB,
            contentBytes: 0,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        const progressBar = document.querySelector('.disk-space-widget__progress-bar');
        expect(progressBar).toHaveStyle({ width: '50%' });
    });

    it('has progressbar role with aria attributes', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 50 * GB,
            totalBytes: 100 * GB,
            freeBytes: 50 * GB,
            contentBytes: 0,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        const progressBar = screen.getByRole('progressbar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '50');
        expect(progressBar).toHaveAttribute('aria-valuemin', '0');
        expect(progressBar).toHaveAttribute('aria-valuemax', '100');
        expect(progressBar).toHaveAttribute('aria-label', '50% used');
    });

    it('ProgressBar_ShowsContentAndOtherUsedSpaceAsTwoParts_WhenTheContentIsPartOfTheUsedSpace', () => {
        // Arrange
        const folder: DiskSpaceInfo = {
            id: 'tv',
            name: 'TV',
            path: '/mnt/share/tv',
            usedBytes: 7 * TB - 224.58 * GB,
            totalBytes: 7 * TB,
            freeBytes: 224.58 * GB,
            contentBytes: 4.86 * TB,
        };

        // Act
        render(<DiskSpaceWidget folders={[folder]} />);

        // Assert
        const progressBar = screen.getByRole('progressbar', { name: '97% used' });
        expect(progressBar.querySelector('.disk-space-widget__progress-content')).toHaveStyle({ width: '69%' });
        expect(progressBar.querySelector('.disk-space-widget__progress-bar--critical')).toHaveStyle({ width: '28%' });
        expect(screen.getByText('97%')).toHaveClass('disk-space-widget__usage-percent--critical');
    });

    it('ProgressBar_KeepsAThinContentPart_WhenTheContentShareIsVerySmall', () => {
        // Arrange
        const folder: DiskSpaceInfo = {
            id: 'music',
            name: 'Music',
            path: '/mnt/share/music',
            usedBytes: 7 * TB - 224.58 * GB,
            totalBytes: 7 * TB,
            freeBytes: 224.58 * GB,
            contentBytes: 6.08 * GB,
        };

        // Act
        render(<DiskSpaceWidget folders={[folder]} />);

        // Assert
        const progressBar = screen.getByRole('progressbar', { name: '97% used' });
        expect(progressBar.querySelector('.disk-space-widget__progress-content')).toHaveStyle({ width: '1%' });
        expect(progressBar.querySelector('.disk-space-widget__progress-bar--critical')).toHaveStyle({ width: '96%' });
    });

    it.each([
        ['nearly all of the used space', 49.8],
        ['more than the used space', 80],
    ])('ProgressBar_FillsTheUsedShareWithContentOnly_WhenTheContentIs %s', (_, contentGigabytes) => {
        // Arrange
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 50 * GB,
            totalBytes: 100 * GB,
            freeBytes: 50 * GB,
            contentBytes: contentGigabytes * GB,
        };

        // Act
        render(<DiskSpaceWidget folders={[folder]} />);

        // Assert
        const progressBar = screen.getByRole('progressbar', { name: '50% used' });
        expect(progressBar.querySelector('.disk-space-widget__progress-content')).toHaveStyle({ width: '50%' });
        expect(progressBar.querySelector('.disk-space-widget__progress-bar')).toBeNull();
    });

    it('ProgressBar_DescribesItselfWithTheContentAndTotalFigures_WhenTheDiskIsAvailable', () => {
        // Arrange
        const folder: DiskSpaceInfo = {
            id: 'tv',
            name: 'TV',
            path: '/mnt/share/tv',
            usedBytes: 7 * TB - 224.58 * GB,
            totalBytes: 7 * TB,
            freeBytes: 224.58 * GB,
            contentBytes: 4.86 * TB,
        };

        // Act
        render(<DiskSpaceWidget folders={[folder]} />);

        // Assert
        const progressBar = screen.getByRole('progressbar', { name: '97% used' });
        expect(progressBar).toHaveAttribute('aria-valuenow', '97');
        expect(progressBar).toHaveAccessibleDescription('4.86 TB content 7.00 TB total');
        expect(screen.getByText('4.86 TB content').querySelector('.disk-space-widget__content-marker')).toHaveAttribute(
            'aria-hidden',
            'true',
        );
    });
});

describe('DiskSpaceWidget interactions', () => {
    it('calls onFolderClick when folder row is clicked', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();

        render(<DiskSpaceWidget folders={mockFolders} onFolderClick={handleClick} />);

        const row = screen.getByText('TV Shows').closest('.disk-space-widget__row');
        await user.click(row!);

        expect(handleClick).toHaveBeenCalledWith(mockFolders[0]);
    });

    it('calls onFolderClick with correct folder for each row', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();

        render(<DiskSpaceWidget folders={mockFolders} onFolderClick={handleClick} />);

        const moviesRow = screen.getByText('Movies').closest('.disk-space-widget__row');
        await user.click(moviesRow!);

        expect(handleClick).toHaveBeenCalledWith(mockFolders[1]);
    });

    it('makes row clickable when onFolderClick is provided', () => {
        const handleClick = vi.fn();
        render(<DiskSpaceWidget folders={mockFolders} onFolderClick={handleClick} />);

        const row = screen.getByText('TV Shows').closest('.disk-space-widget__row');
        expect(row).toHaveAttribute('data-clickable', 'true');
        expect(row).toHaveAttribute('role', 'button');
    });

    it('does not make row clickable when onFolderClick is not provided', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        const row = screen.getByText('TV Shows').closest('.disk-space-widget__row');
        expect(row).not.toHaveAttribute('data-clickable');
        expect(row).not.toHaveAttribute('role', 'button');
    });

    it('supports keyboard navigation for clickable rows', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();

        render(<DiskSpaceWidget folders={mockFolders} onFolderClick={handleClick} />);

        const row = screen.getByText('TV Shows').closest<HTMLElement>('.disk-space-widget__row');
        row?.focus();

        await user.keyboard('{Enter}');
        expect(handleClick).toHaveBeenCalledWith(mockFolders[0]);
    });

    it('calls onViewAllClick when view all button is clicked', async () => {
        const user = userEvent.setup();
        const handleClick = vi.fn();

        render(<DiskSpaceWidget folders={mockFolders} onViewAllClick={handleClick} />);

        await user.click(screen.getByText('Manage root folders'));
        expect(handleClick).toHaveBeenCalledTimes(1);
    });

    it('applies data-folder-id to rows', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        const rows = document.querySelectorAll('[data-folder-id]');
        expect(rows[0]).toHaveAttribute('data-folder-id', 'rf-1');
        expect(rows[1]).toHaveAttribute('data-folder-id', 'rf-2');
        expect(rows[2]).toHaveAttribute('data-folder-id', 'rf-3');
    });
});

describe('DiskSpaceWidget view all button', () => {
    it('shows view all button when onViewAllClick is provided and folders exist', () => {
        render(<DiskSpaceWidget folders={mockFolders} onViewAllClick={() => {}} />);
        expect(screen.getByText('Manage root folders')).toBeInTheDocument();
    });

    it('hides view all button when onViewAllClick is not provided', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.queryByText('Manage root folders')).not.toBeInTheDocument();
    });

    it('applies data-action attribute to view all button', () => {
        render(<DiskSpaceWidget folders={mockFolders} onViewAllClick={() => {}} />);
        expect(screen.getByText('Manage root folders')).toHaveAttribute('data-action', 'view-all');
    });
});

describe('DiskSpaceWidget accessibility', () => {
    it('has accessible heading structure', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Disk Space' })).toBeInTheDocument();
    });

    it('supports custom heading level', () => {
        render(<DiskSpaceWidget folders={mockFolders} headingLevel={3} />);
        expect(screen.getByRole('heading', { level: 3, name: 'Disk Space' })).toBeInTheDocument();
    });

    it('clickable rows have aria-label', () => {
        const handleClick = vi.fn();
        render(<DiskSpaceWidget folders={mockFolders} onFolderClick={handleClick} />);

        const row = screen.getByText('TV Shows').closest('.disk-space-widget__row');
        expect(row).toHaveAttribute('aria-label', 'View root folder TV Shows');
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        render(<DiskSpaceWidget icon={<TestIcon />} folders={mockFolders} />);

        const iconWrapper = document.querySelector('.disk-space-widget__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });
});

describe('DiskSpaceWidget unavailable state', () => {
    const unavailableFolder: DiskSpaceInfo = {
        id: 'rf-unavailable',
        name: 'Network Share',
        path: '\\\\server\\share',
        usedBytes: 0,
        totalBytes: 0,
        freeBytes: 0,
        contentBytes: 2 * GB,
    };

    it('shows Unavailable instead of a usage percentage', () => {
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);
        expect(screen.getByText('Unavailable')).toBeInTheDocument();
        expect(screen.queryByText('0%')).not.toBeInTheDocument();
    });

    it('applies the unavailable usage status', () => {
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);
        const row = screen.getByText('Unavailable').closest('[data-folder-id]');
        expect(row).toHaveAttribute('data-usage-status', 'unavailable');
    });

    it('does not render a progress bar', () => {
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);
        expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    });

    it('does not render free or total figures', () => {
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);
        expect(screen.queryByText(/free$/)).not.toBeInTheDocument();
        expect(screen.queryByText(/total$/)).not.toBeInTheDocument();
    });

    it('still shows the content size', () => {
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);
        expect(screen.getByText('2.00 GB content')).toBeInTheDocument();
    });

    it('ContentFigure_ShowsNoMarker_WhenTheDiskIsUnavailable', () => {
        // Act
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);

        // Assert
        expect(document.querySelector('.disk-space-widget__content-marker')).toBeNull();
    });

    it('still shows the folder path', () => {
        render(<DiskSpaceWidget folders={[unavailableFolder]} />);
        expect(screen.getByText('\\\\server\\share')).toBeInTheDocument();
    });
});

describe('DiskSpaceWidget byte formatting', () => {
    it('formats bytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 500,
            totalBytes: 1000,
            freeBytes: 500,
            contentBytes: 500,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('500 B content')).toBeInTheDocument();
    });

    it('formats kilobytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 1024,
            totalBytes: 2048,
            freeBytes: 1024,
            contentBytes: 1024,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('1 KB content')).toBeInTheDocument();
    });

    it('formats megabytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 500 * 1024 * 1024,
            totalBytes: 1024 * 1024 * 1024,
            freeBytes: 524 * 1024 * 1024,
            contentBytes: 500 * 1024 * 1024,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('500.0 MB content')).toBeInTheDocument();
    });

    it('formats gigabytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 50 * GB,
            totalBytes: 100 * GB,
            freeBytes: 50 * GB,
            contentBytes: 50 * GB,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('50.00 GB content')).toBeInTheDocument();
    });

    it('formats terabytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 2 * TB,
            totalBytes: 4 * TB,
            freeBytes: 2 * TB,
            contentBytes: 2 * TB,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('2.00 TB content')).toBeInTheDocument();
    });

    it('clamps units index for values beyond petabytes', () => {
        const EB = 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 2 * EB,
            totalBytes: 4 * EB,
            freeBytes: 2 * EB,
            contentBytes: 2 * EB,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('2048.00 PB content')).toBeInTheDocument();
    });

    it('formats zero bytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 0,
            totalBytes: 100 * GB,
            freeBytes: 100 * GB,
            contentBytes: 0,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('0 B content')).toBeInTheDocument();
    });
});
