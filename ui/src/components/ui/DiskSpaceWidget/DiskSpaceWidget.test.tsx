import { render, screen } from '@testing-library/react';
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
    },
    {
        id: 'rf-2',
        name: 'Movies',
        path: '/mnt/media/movies',
        usedBytes: 3.5 * TB,
        totalBytes: 4 * TB,
        freeBytes: 0.5 * TB,
    },
    {
        id: 'rf-3',
        name: 'Music',
        path: '/mnt/media/music',
        usedBytes: 180 * GB,
        totalBytes: 500 * GB,
        freeBytes: 320 * GB,
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

    it('does not show summary when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading />);
        expect(screen.queryByText('Total Used')).not.toBeInTheDocument();
    });

    it('does not show view all button when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading onViewAllClick={() => {}} />);
        expect(screen.queryByText('Manage root folders')).not.toBeInTheDocument();
    });
});

describe('DiskSpaceWidget empty state', () => {
    it('shows empty message when no folders', () => {
        render(<DiskSpaceWidget folders={[]} />);
        expect(screen.getByText('No root folders configured')).toBeInTheDocument();
    });

    it('does not show summary when no folders', () => {
        render(<DiskSpaceWidget folders={[]} />);
        expect(screen.queryByText('Total Used')).not.toBeInTheDocument();
    });

    it('does not show view all button when no folders', () => {
        render(<DiskSpaceWidget folders={[]} onViewAllClick={() => {}} />);
        expect(screen.queryByText('Manage root folders')).not.toBeInTheDocument();
    });
});

describe('DiskSpaceWidget summary', () => {
    it('displays total used space', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByText('Total Used')).toBeInTheDocument();
    });

    it('displays total free space', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByText('Total Free')).toBeInTheDocument();
    });

    it('displays total capacity', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);
        expect(screen.getByText('Total Capacity')).toBeInTheDocument();
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
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        const progressBar = screen.getByRole('progressbar');
        expect(progressBar).toHaveAttribute('aria-valuenow', '50');
        expect(progressBar).toHaveAttribute('aria-valuemin', '0');
        expect(progressBar).toHaveAttribute('aria-valuemax', '100');
        expect(progressBar).toHaveAttribute('aria-label', '50% used');
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

describe('DiskSpaceWidget compact mobile elements', () => {
    it('renders compact summary with used / total format', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        const compact = document.querySelector('.disk-space-widget__summary-compact');
        expect(compact).toBeInTheDocument();
    });

    it('renders compact row details for each folder', () => {
        render(<DiskSpaceWidget folders={mockFolders} />);

        const compactRows = document.querySelectorAll('.disk-space-widget__row-compact');
        expect(compactRows.length).toBe(3);
    });

    it('does not render compact summary when loading', () => {
        render(<DiskSpaceWidget folders={mockFolders} loading />);

        const compact = document.querySelector('.disk-space-widget__summary-compact');
        expect(compact).not.toBeInTheDocument();
    });

    it('does not render compact summary when no folders', () => {
        render(<DiskSpaceWidget folders={[]} />);

        const compact = document.querySelector('.disk-space-widget__summary-compact');
        expect(compact).not.toBeInTheDocument();
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
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('500 B used')).toBeInTheDocument();
    });

    it('formats kilobytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 1024,
            totalBytes: 2048,
            freeBytes: 1024,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('1 KB used')).toBeInTheDocument();
    });

    it('formats megabytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 500 * 1024 * 1024,
            totalBytes: 1024 * 1024 * 1024,
            freeBytes: 524 * 1024 * 1024,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('500.0 MB used')).toBeInTheDocument();
    });

    it('formats gigabytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 50 * GB,
            totalBytes: 100 * GB,
            freeBytes: 50 * GB,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('50.00 GB used')).toBeInTheDocument();
    });

    it('formats terabytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 2 * TB,
            totalBytes: 4 * TB,
            freeBytes: 2 * TB,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('2.00 TB used')).toBeInTheDocument();
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
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('2048.00 PB used')).toBeInTheDocument();
    });

    it('formats zero bytes correctly', () => {
        const folder: DiskSpaceInfo = {
            id: 'test',
            name: 'Test',
            path: '/test',
            usedBytes: 0,
            totalBytes: 100 * GB,
            freeBytes: 100 * GB,
        };
        render(<DiskSpaceWidget folders={[folder]} />);

        expect(screen.getByText('0 B used')).toBeInTheDocument();
    });
});
