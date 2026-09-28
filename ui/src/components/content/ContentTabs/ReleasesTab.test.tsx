import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ReleasesTab, type ReleaseRow } from './ReleasesTab';

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
});

const releaseRow = (overrides: Partial<ReleaseRow> = {}): ReleaseRow => ({
    id: 'release-1',
    title: 'Movie.2024.1080p.BluRay.x264',
    protocol: 'torrent',
    publishDate: '2024-01-15T10:00:00Z',
    size: 5_368_709_120,
    isMatch: true,
    rejectionReason: null,
    properties: '{"seeders":"42","leechers":"3"}',
    ...overrides,
});

describe('ReleasesTab', () => {
    it.each([1024, 390])('shows a persisted capacity reason and clears it after recovery at %i px', (width) => {
        // Arrange
        vi.stubGlobal('matchMedia', createMatchMedia(width));
        const reason =
            'Waiting for destination capacity: insufficient free space. Required: 100 bytes. Available: 99 bytes.';
        const waiting = {
            id: 'waiting-release',
            title: 'Waiting film',
            protocol: 'torrent',
            publishDate: '2026-09-22T12:00:00Z',
            acquisitionWaitingReason: reason,
        };

        // Act
        const { rerender } = render(<ReleasesTab releases={[waiting]} onGrab={vi.fn()} />);

        // Assert
        expect(screen.getByText('Waiting for capacity')).toBeInTheDocument();
        expect(screen.getByText(reason)).toBeVisible();
        expect(screen.queryByRole('button', { name: 'Grab' })).not.toBeInTheDocument();
        expect(screen.queryByText('Failed')).not.toBeInTheDocument();

        // Act
        rerender(
            <ReleasesTab
                releases={[{ ...waiting, acquisitionWaitingReason: null, acquisitionStatus: 'Downloading' }]}
                onGrab={vi.fn()}
            />,
        );

        // Assert
        expect(screen.queryByText('Waiting for capacity')).not.toBeInTheDocument();
        expect(screen.queryByText(reason)).not.toBeInTheDocument();
        expect(screen.getByText('Downloading')).toBeInTheDocument();
    });

    it('shows unavailable capacity instead of a previous failed transfer', () => {
        // Arrange
        const reason =
            'Waiting for destination capacity: the capacity check is unavailable. Required: unknown. Available: 99 bytes.';

        // Act
        render(
            <ReleasesTab
                releases={[
                    {
                        id: 'waiting-release',
                        title: 'Waiting film',
                        protocol: 'torrent',
                        publishDate: '2026-09-22T12:00:00Z',
                        acquisitionStatus: 'Failed',
                        failureReason: 'MissingFiles',
                        acquisitionWaitingReason: reason,
                    },
                ]}
                onGrab={vi.fn()}
            />,
        );

        // Assert
        expect(screen.getByText(reason)).toBeVisible();
        expect(screen.queryByText('Failed')).not.toBeInTheDocument();
    });

    it('renders the empty state exactly once when no releases are available', () => {
        render(<ReleasesTab releases={[]} onGrab={vi.fn()} />);
        expect(screen.getAllByText(/no releases found/i)).toHaveLength(1);
    });

    it('renders exactly the trimmed column set', () => {
        render(<ReleasesTab releases={[releaseRow()]} onGrab={vi.fn()} />);

        expect(screen.getByRole('columnheader', { name: /Title/i })).toBeInTheDocument();
        expect(screen.getByRole('columnheader', { name: /Size/i })).toBeInTheDocument();
        expect(screen.getByRole('columnheader', { name: /Status/i })).toBeInTheDocument();
        expect(screen.getByRole('columnheader', { name: /Published/i })).toBeInTheDocument();

        expect(screen.queryByRole('columnheader', { name: /Protocol/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: /Quality/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: /Match/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: /Acceptable/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: /Score/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: /Indexer/i })).not.toBeInTheDocument();
    });

    it('renders a row without provider-specific properties in columns', () => {
        render(<ReleasesTab releases={[releaseRow()]} onGrab={vi.fn()} />);

        expect(screen.getAllByText('Movie.2024.1080p.BluRay.x264').length).toBeGreaterThan(0);
        expect(screen.getAllByText('5 GB').length).toBeGreaterThan(0);
        expect(screen.queryByText('42')).not.toBeInTheDocument();
        expect(screen.queryByText('3')).not.toBeInTheDocument();
    });

    it('provides accessible compact sort controls with every release sort field', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        render(<ReleasesTab releases={[releaseRow()]} onGrab={vi.fn()} />);

        const controls = screen.getByTestId('releases-tab__sort-controls');
        const field = within(controls).getByRole('combobox', { name: 'Sort by field' });
        const direction = within(controls).getByRole('button', {
            name: 'Sort descending',
        });

        expect(direction).toBeDisabled();

        await user.click(field);

        expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
            'Title',
            'Size',
            'Status',
            'Publish date',
        ]);

        await user.click(screen.getByRole('option', { name: 'Title' }));

        expect(direction).toBeEnabled();
        expect(direction).toHaveAccessibleName('Sort descending');
    });

    it('renders only sorted manual release cards on mobile', async () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const user = userEvent.setup();
        const releases = [
            releaseRow({
                id: 'release-zulu',
                title: 'Zulu',
                size: 2,
                acquisitionStatus: 'Stalled',
                publishDate: '2024-02-01T00:00:00Z',
            }),
            releaseRow({
                id: 'release-alpha',
                title: 'Alpha',
                size: 1,
                acquisitionStatus: 'Failed',
                publishDate: '2024-03-01T00:00:00Z',
            }),
            releaseRow({
                id: 'release-beta',
                title: 'Beta',
                size: 3,
                acquisitionStatus: 'Downloading',
                publishDate: '2024-01-01T00:00:00Z',
            }),
        ];
        render(<ReleasesTab releases={releases} onGrab={vi.fn()} />);

        expect(document.querySelector('.releases-tab__data-table')).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();

        const field = screen.getByRole('combobox', { name: 'Sort by field' });
        const getMobileOrder = () =>
            Array.from(document.querySelectorAll('.releases-tab__mobile-card')).map((card) =>
                card.getAttribute('data-release-id'),
            );

        const expectedAscendingOrder: Record<string, string[]> = {
            Title: ['release-alpha', 'release-beta', 'release-zulu'],
            Size: ['release-alpha', 'release-zulu', 'release-beta'],
            Status: ['release-beta', 'release-alpha', 'release-zulu'],
            'Publish date': ['release-beta', 'release-zulu', 'release-alpha'],
        };

        for (const [label, expected] of Object.entries(expectedAscendingOrder)) {
            await user.click(field);
            await user.click(screen.getByRole('option', { name: label }));

            expect(getMobileOrder()).toEqual(expected);
        }

        await user.click(screen.getByRole('button', { name: 'Sort descending' }));

        expect(getMobileOrder()).toEqual(['release-alpha', 'release-zulu', 'release-beta']);
    });

    it('renders purpose-built release cards at the tablet breakpoint', () => {
        vi.stubGlobal('matchMedia', createMatchMedia(768));
        render(<ReleasesTab releases={[releaseRow()]} onGrab={vi.fn()} />);

        expect(document.querySelector('.releases-tab__data-table')).not.toBeInTheDocument();
        expect(screen.queryByRole('columnheader', { name: 'Title' })).not.toBeInTheDocument();
        expect(document.querySelector('.releases-tab__mobile-card')).toBeInTheDocument();
    });

    it('renders a dash for missing size and status data', () => {
        render(
            <ReleasesTab
                releases={[
                    releaseRow({
                        size: null,
                        acquisitionStatus: null,
                    }),
                ]}
                onGrab={vi.fn()}
            />,
        );

        expect(screen.getAllByText('-').length).toBeGreaterThan(0);
    });

    it('shows the download status in the Status column for a grabbed release', () => {
        render(<ReleasesTab releases={[releaseRow({ acquisitionStatus: 'Downloading' })]} onGrab={vi.fn()} />);

        const badges = screen.getAllByText('Downloading');
        expect(badges.length).toBeGreaterThan(0);
        for (const badge of badges) {
            expect(badge.closest('[data-acquisition-status="Downloading"]')).not.toBeNull();
        }
    });

    it('renders the longest status label in full without truncating the badge text', () => {
        const { container } = render(
            <ReleasesTab releases={[releaseRow({ acquisitionStatus: 'Downloading' })]} onGrab={vi.fn()} />,
        );

        const badges = container.querySelectorAll('[data-acquisition-status="Downloading"]');
        expect(badges.length).toBeGreaterThan(0);
        for (const badge of badges) {
            expect(badge).toHaveClass('badge--sm');
            expect(badge.querySelector('.badge__dot')).not.toBeNull();
            expect(badge.textContent).toBe('Downloading');
        }
    });

    it('renders a dash in the Status column for a never-grabbed release', () => {
        const { container } = render(
            <ReleasesTab releases={[releaseRow({ acquisitionStatus: null })]} onGrab={vi.fn()} />,
        );

        expect(container.querySelector('[data-acquisition-status]')).toBeNull();
        expect(screen.getAllByText('-').length).toBeGreaterThan(0);
    });

    it('opens release details when the title of a grabbed release is clicked', async () => {
        const onTitleClick = vi.fn();
        const release = releaseRow({ acquisitionStatus: 'Downloading' });
        const user = userEvent.setup();

        render(<ReleasesTab releases={[release]} onGrab={vi.fn()} onTitleClick={onTitleClick} />);

        const titleButtons = screen.getAllByRole('button', { name: /Movie\.2024/i });
        expect(titleButtons.length).toBeGreaterThan(0);

        await user.click(titleButtons[0]);

        expect(onTitleClick).toHaveBeenCalledTimes(1);
        expect(onTitleClick).toHaveBeenCalledWith(release);
    });

    it('opens release details when the release has no download', () => {
        render(
            <ReleasesTab
                releases={[releaseRow({ acquisitionStatus: null })]}
                onGrab={vi.fn()}
                onTitleClick={vi.fn()}
            />,
        );

        expect(screen.getAllByRole('button', { name: /Movie\.2024/i }).length).toBeGreaterThan(0);
    });

    it('invokes onGrab with the release when the grab button is clicked', async () => {
        const onGrab = vi.fn();
        const release = releaseRow();
        const user = userEvent.setup();

        render(<ReleasesTab releases={[release]} onGrab={onGrab} />);

        const grabButtons = screen.getAllByRole('button', { name: /grab/i });
        expect(grabButtons.length).toBeGreaterThan(0);

        await user.click(grabButtons[0]);

        expect(onGrab).toHaveBeenCalledTimes(1);
        expect(onGrab).toHaveBeenCalledWith(release);
    });

    it('shows the loading state on the grab button while the release is being grabbed', () => {
        const release = releaseRow();
        render(<ReleasesTab releases={[release]} grabbingReleaseIds={new Set([release.id])} onGrab={vi.fn()} />);

        const grabButtons = screen.getAllByRole('button', { name: /grab/i });
        expect(grabButtons.length).toBeGreaterThan(0);
        for (const button of grabButtons) {
            expect(button).toBeDisabled();
            expect(button).toHaveAttribute('aria-busy', 'true');
            expect(button).toHaveTextContent('Grabbing...');
        }
    });

    it('does not disable other grab buttons while one release is being grabbed', () => {
        const release1 = releaseRow({ id: 'release-1' });
        const release2 = releaseRow({ id: 'release-2', title: 'Show.S01E01.720p.WEB.x264' });
        render(
            <ReleasesTab
                releases={[release1, release2]}
                grabbingReleaseIds={new Set([release1.id])}
                onGrab={vi.fn()}
            />,
        );

        const buttons = screen.getAllByRole('button', { name: /grab/i });
        const release1Buttons = buttons.filter((button) => button.getAttribute('data-release-id') === 'release-1');
        const release2Buttons = buttons.filter((button) => button.getAttribute('data-release-id') === 'release-2');

        expect(release1Buttons.length).toBeGreaterThan(0);
        expect(release2Buttons.length).toBeGreaterThan(0);
        for (const button of release1Buttons) {
            expect(button).toBeDisabled();
        }
        for (const button of release2Buttons) {
            expect(button).not.toBeDisabled();
        }
    });

    it('shows the same accessible pending action in the mobile card layout', () => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const release = releaseRow();

        render(<ReleasesTab releases={[release]} grabbingReleaseIds={new Set([release.id])} onGrab={vi.fn()} />);

        const mobileCard = document.querySelector('.releases-tab__mobile-card');
        expect(mobileCard).toBeInTheDocument();
        const button = within(mobileCard as HTMLElement).getByRole('button', { name: 'Grabbing...' });
        expect(button).toBeDisabled();
        expect(button).toHaveAttribute('aria-busy', 'true');
    });

    it('offers no Grab button for an in-flight download', () => {
        render(<ReleasesTab releases={[releaseRow({ acquisitionStatus: 'Downloading' })]} onGrab={vi.fn()} />);

        expect(screen.queryByRole('button', { name: /grab/i })).not.toBeInTheDocument();
    });

    it('shows an Imported status and no Grab button once the release is imported', () => {
        render(<ReleasesTab releases={[releaseRow({ acquisitionStatus: 'Imported' })]} onGrab={vi.fn()} />);

        expect(screen.getAllByText('Imported').length).toBeGreaterThan(0);
        expect(screen.queryByRole('button', { name: /grab/i })).not.toBeInTheDocument();
    });

    it('keeps Grab enabled and shows a Failed status with the reason for a failed download', () => {
        render(
            <ReleasesTab
                releases={[releaseRow({ acquisitionStatus: 'Failed', failureReason: 'DownloadStuck' })]}
                onGrab={vi.fn()}
            />,
        );

        const failed = screen.getAllByText('Failed');
        expect(failed.length).toBeGreaterThan(0);
        for (const badge of failed) {
            expect(badge).toHaveAttribute('title', 'Download Stuck');
        }
        expect(screen.getAllByRole('button', { name: /grab/i }).length).toBeGreaterThan(0);
    });

    it('keeps Grab enabled and shows a Faulted status with the reason for a faulted download', () => {
        render(
            <ReleasesTab
                releases={[releaseRow({ acquisitionStatus: 'Faulted', failureReason: 'DownloadStuck' })]}
                onGrab={vi.fn()}
            />,
        );

        const faulted = screen.getAllByText('Faulted');
        expect(faulted.length).toBeGreaterThan(0);
        for (const badge of faulted) {
            expect(badge).toHaveAttribute('title', 'Download Stuck');
            expect(badge.closest('[data-acquisition-status]')).toHaveAttribute('data-variant', 'stalled');
        }
        expect(screen.getAllByRole('button', { name: /grab/i }).length).toBeGreaterThan(0);
    });

    it('keeps Grab enabled and shows a Cancelled status for a cancelled download', () => {
        render(<ReleasesTab releases={[releaseRow({ acquisitionStatus: 'Cancelled' })]} onGrab={vi.fn()} />);

        expect(screen.getAllByText('Cancelled').length).toBeGreaterThan(0);
        expect(screen.getAllByRole('button', { name: /grab/i }).length).toBeGreaterThan(0);
    });

    it('offers a normal Grab for a never-grabbed release', () => {
        render(<ReleasesTab releases={[releaseRow({ acquisitionStatus: null })]} onGrab={vi.fn()} />);

        expect(screen.getAllByRole('button', { name: /grab/i }).length).toBeGreaterThan(0);
    });

    it.each([
        ['Matched', 'Unmatch', 'Unmatched'],
        ['Unmatched', 'Undo unmatch', 'Matched'],
    ] as const)('asks a %s release to change through the %s button', async (status, label, expected) => {
        // Arrange
        const user = userEvent.setup();
        const onUnmatchChange = vi.fn();
        const release = releaseRow({ status });
        render(<ReleasesTab releases={[release]} onGrab={vi.fn()} onUnmatchChange={onUnmatchChange} />);

        // Act
        await user.click(screen.getByRole('button', { name: label }));

        // Assert
        expect(onUnmatchChange).toHaveBeenCalledWith(release, expected);
    });

    it('shows an Unmatched status for an unmatched release that has no download', () => {
        render(<ReleasesTab releases={[releaseRow({ status: 'Unmatched' })]} onGrab={vi.fn()} />);

        expect(screen.getByText('Unmatched')).toBeInTheDocument();
    });

    it('shows both the Faulted and Unmatched states with Grab and Undo unmatch when both apply', () => {
        render(
            <ReleasesTab
                releases={[releaseRow({ acquisitionStatus: 'Faulted', status: 'Unmatched' })]}
                onGrab={vi.fn()}
                onUnmatchChange={vi.fn()}
            />,
        );

        expect(screen.getByText('Faulted')).toBeInTheDocument();
        expect(screen.getByText('Unmatched')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /grab/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Undo unmatch' })).toBeInTheDocument();
    });

    it('offers no Unmatch button when the user cannot change the match', () => {
        render(<ReleasesTab releases={[releaseRow()]} onGrab={vi.fn()} />);

        expect(screen.queryByRole('button', { name: /unmatch/i })).not.toBeInTheDocument();
    });
});
