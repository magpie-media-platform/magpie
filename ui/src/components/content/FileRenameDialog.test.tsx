import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiPost } from '@/lib/api';
import { FileRenameDialog, type FileRenamePreview } from './FileRenameDialog';

vi.mock('@/lib/api', () => ({ apiPost: vi.fn() }));

describe('FileRenameDialog', () => {
    let client: QueryClient;
    let preview: FileRenamePreview;
    const associate = vi.fn();
    const close = vi.fn();

    beforeEach(() => {
        vi.clearAllMocks();
        client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        preview = {
            hasMask: true,
            files: [
                {
                    contentHierarchyItemId: 'content',
                    originalPath: '/library/old.mkv',
                    destinationPath: '/library/Named/Episode.mkv',
                    isSidecar: false,
                    completed: false,
                },
            ],
        };
        vi.mocked(apiPost).mockImplementation(async (path) =>
            path === '/content/rename-tokens' ? { tokens: [], preview: null } : preview,
        );
        associate.mockResolvedValue({ mediaFile: { id: 'attached' } });
    });

    it('shows the destination on enabling before association and starts disabled', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileRenameDialog
                    contentId="content"
                    filePath="/library/old.mkv"
                    onAssociate={associate}
                    onClose={close}
                />
            </QueryClientProvider>,
        );
        const toggle = await screen.findByRole('checkbox', { name: 'Apply rename mask' });
        expect(toggle).not.toBeChecked();
        expect(screen.queryByText('/library/Named/Episode.mkv')).not.toBeInTheDocument();
        await user.click(toggle);
        expect(screen.getByText('/library/Named/Episode.mkv')).toBeInTheDocument();
        expect(associate).not.toHaveBeenCalled();
        vi.mocked(apiPost).mockResolvedValueOnce({ ...preview, files: [{ ...preview.files[0], completed: true }] });
        await user.click(screen.getByRole('button', { name: 'Associate and apply valid renames' }));
        await screen.findByText('1 completed; 0 failed.');
        expect(associate).toHaveBeenCalledExactlyOnceWith('/library/old.mkv');
        expect(apiPost).toHaveBeenLastCalledWith('/content/content/files/rename', {
            ...preview,
            files: [{ ...preview.files[0], mediaFileId: 'attached' }],
        });
    });

    it('associates without renaming when the user leaves the option off', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileRenameDialog
                    contentId="content"
                    filePath="/library/old.mkv"
                    onAssociate={associate}
                    onClose={close}
                />
            </QueryClientProvider>,
        );
        await screen.findByRole('checkbox', { name: 'Apply rename mask' });
        await user.click(screen.getByRole('button', { name: 'Associate file' }));
        await waitFor(() => expect(close).toHaveBeenCalledOnce());
        expect(apiPost).toHaveBeenCalledOnce();
        expect(associate).toHaveBeenCalledOnce();
    });

    it('hides the optional control without a mask', async () => {
        const user = userEvent.setup();
        vi.mocked(apiPost).mockResolvedValue({ ...preview, hasMask: false });
        render(
            <QueryClientProvider client={client}>
                <FileRenameDialog
                    contentId="content"
                    filePath="/library/old.mkv"
                    onAssociate={associate}
                    onClose={close}
                />
            </QueryClientProvider>,
        );
        await waitFor(() => expect(screen.getByRole('button', { name: 'Associate file' })).toBeEnabled());
        expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(associate).not.toHaveBeenCalled();
    });

    it('keeps per-file errors visible and reports mixed outcomes', async () => {
        const user = userEvent.setup();
        preview.files.push({
            contentHierarchyItemId: 'other',
            mediaFileId: 'missing',
            originalPath: '/library/missing.mkv',
            error: 'The source file does not exist.',
            isSidecar: false,
            completed: false,
        });
        render(
            <QueryClientProvider client={client}>
                <FileRenameDialog contentId="parent" onClose={close} />
            </QueryClientProvider>,
        );
        await screen.findByText('The source file does not exist.');
        vi.mocked(apiPost).mockResolvedValueOnce({
            ...preview,
            files: [{ ...preview.files[0], completed: true }, preview.files[1]],
        });
        await user.click(screen.getByRole('button', { name: 'Apply valid renames' }));
        await screen.findByText('1 completed; 1 failed.');
        expect(screen.getByText('The source file does not exist.')).toBeInTheDocument();
    });

    it('shows token values for the chosen primary file', async () => {
        const user = userEvent.setup();
        preview.files = [
            { ...preview.files[0], mediaFileId: 'first' },
            { ...preview.files[0], mediaFileId: 'sidecar', originalPath: '/library/old.srt', isSidecar: true },
            {
                ...preview.files[0],
                contentHierarchyItemId: 'other',
                mediaFileId: 'second',
                originalPath: '/library/b.mkv',
            },
        ];
        render(
            <QueryClientProvider client={client}>
                <FileRenameDialog contentId="parent" onClose={close} />
            </QueryClientProvider>,
        );
        const picker = await screen.findByRole('combobox', { name: 'Token values for' });
        await waitFor(() =>
            expect(apiPost).toHaveBeenCalledWith('/content/rename-tokens', {
                contentType: undefined,
                contentHierarchyItemId: 'content',
                mediaFileId: 'first',
                template: undefined,
            }),
        );
        await user.click(picker);
        expect(screen.queryByRole('option', { name: '/library/old.srt' })).not.toBeInTheDocument();
        await user.click(screen.getByRole('option', { name: '/library/b.mkv' }));
        await waitFor(() =>
            expect(apiPost).toHaveBeenLastCalledWith('/content/rename-tokens', {
                contentType: undefined,
                contentHierarchyItemId: 'other',
                mediaFileId: 'second',
                template: undefined,
            }),
        );
    });

    it('does not repeat association when a rename transport failure is retried', async () => {
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={client}>
                <FileRenameDialog
                    contentId="content"
                    filePath="/library/old.mkv"
                    onAssociate={associate}
                    onClose={close}
                />
            </QueryClientProvider>,
        );
        await user.click(await screen.findByRole('checkbox', { name: 'Apply rename mask' }));
        vi.mocked(apiPost).mockRejectedValueOnce(new Error('Connection lost'));
        await user.click(screen.getByRole('button', { name: 'Associate and apply valid renames' }));
        await screen.findByRole('alert');
        vi.mocked(apiPost).mockResolvedValueOnce({ ...preview, files: [{ ...preview.files[0], completed: true }] });
        await user.click(screen.getByRole('button', { name: 'Associate and apply valid renames' }));
        await screen.findByText('1 completed; 0 failed.');
        expect(associate).toHaveBeenCalledOnce();
    });
});
