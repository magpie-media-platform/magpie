import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DownloadDetailsDialog } from './DownloadDetailsDialog';

import type { ApiDownload } from '@/hooks/useDownloads';
import { formatDateTime } from '@/lib/dateFormatting';

function makeDownload(overrides: Partial<ApiDownload> = {}): ApiDownload {
    return {
        id: 'download-1',
        downloadClientId: 'client-1',
        downloadClientName: 'DownloadClient A',
        externalId: 'abc123',
        title: 'Some.Release.2024.1080p',
        status: 'Downloading',
        createdAt: '2024-01-01T12:00:00Z',
        ...overrides,
    };
}

const defaultProps = {
    open: true,
    onOpenChange: () => {},
};

describe('DownloadDetailsDialog release link', () => {
    it.each([['https://indexer.example/release/123'], ['http://indexer.example/release/123']])(
        'renders the release page as an anchor for %s',
        async (releaseInfoUrl) => {
            render(<DownloadDetailsDialog {...defaultProps} download={makeDownload({ releaseInfoUrl })} />);

            await waitFor(() => {
                expect(screen.getByRole('dialog')).toBeInTheDocument();
            });

            const link = document.querySelector('[data-detail="Release page"] a');
            expect(link?.tagName).toBe('A');
            expect(link).toHaveAttribute('href', releaseInfoUrl);
            expect(link).toHaveTextContent(releaseInfoUrl);
        },
    );

    it.each([
        ['javascript:alert(1)', 'a[href*="javascript:"]'],
        ['data:text/html,<script>alert(1)</script>', 'a[href^="data:"]'],
        ['/relative/release/123', 'a[href^="/relative"]'],
    ])('omits the release page row and renders no anchor for %s', async (releaseInfoUrl, unsafeSelector) => {
        render(<DownloadDetailsDialog {...defaultProps} download={makeDownload({ releaseInfoUrl })} />);

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(document.querySelector(unsafeSelector)).toBeNull();
        expect(document.querySelector('[data-detail="Release page"]')).toBeNull();
        expect(document.querySelector('[data-detail="Title"]')).toHaveTextContent('Some.Release.2024.1080p');
    });

    it('exposes each value as a tooltip so ellipsised text stays readable', async () => {
        render(
            <DownloadDetailsDialog
                {...defaultProps}
                download={makeDownload({
                    outputPath: 'D:\\Kind-X\\A.Very.Long.Scene.Name.2024.2160p.WEB.DL\\file.mkv',
                })}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-detail="File path"]')).toHaveAttribute(
            'title',
            'D:\\Kind-X\\A.Very.Long.Scene.Name.2024.2160p.WEB.DL\\file.mkv',
        );
        expect(document.querySelector('[data-detail="Transfer id"]')).toHaveAttribute('title', 'abc123');
    });
});

describe('DownloadDetailsDialog failure fields', () => {
    it('wraps a capacity status reason without changing truncated identity fields', async () => {
        const statusReason =
            'Waiting for destination capacity: insufficient free space. Required: 1394 bytes. Available: 100 bytes.';

        render(
            <DownloadDetailsDialog
                {...defaultProps}
                download={makeDownload({
                    status: 'Importing',
                    statusReason,
                })}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-detail="Status reason"]')).toHaveClass('details-panel__value--multiline');
        expect(document.querySelector('[data-detail="Title"]')).not.toHaveClass('details-panel__value--multiline');
        expect(document.querySelector('[data-detail="Status reason"]')).toHaveTextContent(statusReason);
    });

    it('does not show a failed timestamp while the current status is Downloading', async () => {
        render(
            <DownloadDetailsDialog
                {...defaultProps}
                download={makeDownload({
                    status: 'Downloading',
                    failedAt: '2024-01-03T04:05:00Z',
                })}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByText('Downloading')).toBeInTheDocument();
        expect(screen.queryByText('Failed at')).not.toBeInTheDocument();
        expect(document.querySelector('[data-detail="Failed at"]')).toBeNull();
    });

    it('shows a failed timestamp when the current status is Failed', async () => {
        const failedAt = '2024-01-03T04:05:00Z';

        render(
            <DownloadDetailsDialog
                {...defaultProps}
                download={makeDownload({
                    status: 'Failed',
                    failedAt,
                })}
            />,
        );

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        const failedAtCell = document.querySelector('[data-detail="Failed at"]');
        expect(screen.getByText('Failed')).toBeInTheDocument();
        expect(screen.getByText('Failed at')).toBeInTheDocument();
        expect(failedAtCell).toHaveTextContent(formatDateTime(failedAt));
    });
});
