import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiGet, apiPost } from '@/lib/api';
import { FileReassignDialog } from './FileReassignDialog';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return { ...actual, apiGet: vi.fn(), apiPost: vi.fn() };
});

describe('FileReassignDialog', () => {
    let client: QueryClient;
    const close = vi.fn();
    const file = { id: 'file', fileName: 's04e05.mkv', relativePath: '/library/Season 4/s04e05.mkv', size: 4 };

    beforeEach(() => {
        vi.resetAllMocks();
        client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        vi.mocked(apiGet).mockResolvedValueOnce({
            items: [{ id: 'season', name: 'Provider season', ordinal: 2 }],
            totalItems: 1,
        });
        vi.mocked(apiGet).mockResolvedValueOnce({
            items: [{ id: 'episode', name: 'Episode five', ordinal: 5 }],
            totalItems: 1,
        });
        vi.mocked(apiPost).mockResolvedValue(undefined);
    });

    it('corrects only the explicitly selected episode and sends the displayed file path', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileReassignDialog contentId="show" showId="show" file={file} onClose={close} />
            </QueryClientProvider>,
        );
        expect(screen.getByRole('button', { name: 'Correct association' })).toBeDisabled();
        await user.click(screen.getByRole('combobox', { name: 'Destination season' }));
        await user.click(await screen.findByRole('option', { name: '2: Provider season' }));
        await user.click(screen.getByRole('combobox', { name: 'Destination episode' }));
        await user.click(await screen.findByRole('option', { name: '5: Episode five' }));
        expect(apiPost).not.toHaveBeenCalled();
        await user.click(screen.getByRole('button', { name: 'Correct association' }));
        await waitFor(() => expect(close).toHaveBeenCalledOnce());
        expect(apiPost).toHaveBeenCalledExactlyOnceWith('/content/show/files/file/reassign', {
            targetContentHierarchyItemId: 'episode',
            expectedFilePath: '/library/Season 4/s04e05.mkv',
        });
    });

    it('orders lexical paged responses by hierarchy ordinal and ignores metadata sequence numbers', async () => {
        vi.mocked(apiGet).mockReset();
        vi.mocked(apiGet).mockImplementation((url) => {
            if (url === '/content?parentId=show&pageNumber=1&pageSize=100&sortBy=Ordinal&sortDirection=Ascending') {
                return Promise.resolve({
                    items: [
                        { id: 'season-1', name: 'Season one', ordinal: 1 },
                        { id: 'season-10', name: 'Season ten', ordinal: 10 },
                        { id: 'season-2', name: 'Season two', ordinal: 2 },
                        { id: 'season-special', name: 'Special season', ordinal: null },
                    ],
                    totalItems: 4,
                });
            }
            if (url === '/content?parentId=season-2&pageNumber=1&pageSize=100&sortBy=Ordinal&sortDirection=Ascending') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'episode-1',
                            name: 'Episode one',
                            ordinal: 1,
                            metadataContainer: { contributions: [{ sequenceNumber: 101 }] },
                        },
                        {
                            id: 'episode-10',
                            name: 'Episode ten',
                            ordinal: 10,
                            metadataContainer: { contributions: [{ sequenceNumber: null }] },
                        },
                        {
                            id: 'episode-100',
                            name: 'Episode hundred',
                            ordinal: 100,
                            metadataContainer: { contributions: [{}] },
                        },
                        {
                            id: 'episode-101',
                            name: 'Episode hundred one',
                            ordinal: 101,
                            metadataContainer: { contributions: [{ sequenceNumber: 1 }] },
                        },
                    ],
                    totalItems: 7,
                });
            }
            if (url === '/content?parentId=season-2&pageNumber=2&pageSize=100&sortBy=Ordinal&sortDirection=Ascending') {
                return Promise.resolve({
                    items: [
                        {
                            id: 'episode-2',
                            name: 'Episode two',
                            ordinal: 2,
                            metadataContainer: { contributions: [{ sequenceNumber: 100 }] },
                        },
                        {
                            id: 'episode-unranked-b',
                            name: 'Alpha special',
                            metadataContainer: { contributions: [{ sequenceNumber: 2 }] },
                        },
                        {
                            id: 'episode-unranked-a',
                            name: 'Zulu special',
                            ordinal: null,
                            metadataContainer: { contributions: [{ sequenceNumber: 3 }] },
                        },
                    ],
                    totalItems: 7,
                });
            }
            return Promise.reject(new Error(`Unexpected destination request: ${url}`));
        });
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileReassignDialog contentId="show" showId="show" file={file} onClose={close} />
            </QueryClientProvider>,
        );

        await user.click(screen.getByRole('combobox', { name: 'Destination season' }));
        await screen.findByRole('option', { name: 'Special season' });
        expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
            '1: Season one',
            '2: Season two',
            '10: Season ten',
            'Special season',
        ]);
        await user.click(screen.getByRole('option', { name: '2: Season two' }));
        await user.click(screen.getByRole('combobox', { name: 'Destination episode' }));
        await screen.findByRole('option', { name: '101: Episode hundred one' });
        expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual([
            '1: Episode one',
            '2: Episode two',
            '10: Episode ten',
            '100: Episode hundred',
            '101: Episode hundred one',
            'Zulu special',
            'Alpha special',
        ]);
        await user.click(screen.getByRole('option', { name: '101: Episode hundred one' }));
        await user.click(screen.getByRole('button', { name: 'Correct association' }));

        await waitFor(() => expect(close).toHaveBeenCalledOnce());
        expect(apiPost).toHaveBeenCalledExactlyOnceWith('/content/show/files/file/reassign', {
            targetContentHierarchyItemId: 'episode-101',
            expectedFilePath: '/library/Season 4/s04e05.mkv',
        });
    });

    it('does not change associations when cancelled', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileReassignDialog contentId="show" showId="show" file={file} onClose={close} />
            </QueryClientProvider>,
        );
        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(close).toHaveBeenCalledOnce();
        expect(apiPost).not.toHaveBeenCalled();
    });

    it('focuses the stable files context after a successful correction removes the source action', async () => {
        const returnTarget = document.createElement('button');
        const fallbackTarget = document.createElement('div');
        fallbackTarget.tabIndex = -1;
        document.body.append(returnTarget, fallbackTarget);
        const returnFocusRef = { current: returnTarget };
        const fallbackFocusRef = { current: fallbackTarget };
        const user = userEvent.setup();
        const result = render(
            <QueryClientProvider client={client}>
                <FileReassignDialog
                    contentId="show"
                    showId="show"
                    file={file}
                    onClose={() => {
                        returnTarget.remove();
                        result.rerender(<QueryClientProvider client={client}>Closed</QueryClientProvider>);
                    }}
                    returnFocusRef={returnFocusRef}
                    fallbackFocusRef={fallbackFocusRef}
                />
            </QueryClientProvider>,
        );

        await user.click(screen.getByRole('combobox', { name: 'Destination season' }));
        await user.click(await screen.findByRole('option', { name: '2: Provider season' }));
        await user.click(screen.getByRole('combobox', { name: 'Destination episode' }));
        await user.click(await screen.findByRole('option', { name: '5: Episode five' }));
        await user.click(screen.getByRole('button', { name: 'Correct association' }));

        await waitFor(() => expect(fallbackTarget).toHaveFocus());
        expect(returnTarget.isConnected).toBe(false);
        fallbackTarget.remove();
    });

    it('marks an unbroken filename for wrapping without shortening it', () => {
        const longFileName = `${'episode'.repeat(30)}.mkv`;
        render(
            <QueryClientProvider client={client}>
                <FileReassignDialog
                    contentId="show"
                    showId="show"
                    file={{ ...file, fileName: longFileName }}
                    onClose={close}
                />
            </QueryClientProvider>,
        );

        expect(screen.getByText(longFileName)).toHaveClass('file-reassign-dialog__file-name');
    });

    it('keeps a failed correction visible', async () => {
        vi.mocked(apiPost).mockRejectedValue(new Error('The file path or association changed.'));
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileReassignDialog contentId="show" showId="show" file={file} onClose={close} />
            </QueryClientProvider>,
        );
        await user.click(screen.getByRole('combobox', { name: 'Destination season' }));
        await user.click(await screen.findByRole('option', { name: '2: Provider season' }));
        await user.click(screen.getByRole('combobox', { name: 'Destination episode' }));
        await user.click(await screen.findByRole('option', { name: '5: Episode five' }));
        await user.click(screen.getByRole('button', { name: 'Correct association' }));
        expect(await screen.findByRole('alert')).toHaveTextContent('The file path or association changed.');
        expect(close).not.toHaveBeenCalled();
    });
});
