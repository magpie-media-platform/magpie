import { act, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import userEvent from '@testing-library/user-event';
import { beforeEach, vi } from 'vitest';

import { PathControl } from './PathControl';
import type { EffectiveRootFolder } from '@/types';

const mockApply = vi.fn();
const mockUseEffectiveRootFolder = vi.fn();

vi.mock('@/auth', () => ({
    useAuth: () => ({ hasPermission: () => true }),
}));

vi.mock('@/hooks/useContent', () => ({
    useApplyContentPath: () => ({ mutateAsync: mockApply, isPending: false }),
    useEffectiveRootFolder: () => mockUseEffectiveRootFolder(),
}));

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: vi.fn() }),
}));

vi.mock('@/components/configuration/FileBrowser', () => ({
    FileBrowser: ({
        open,
        onSelect,
        initialPath,
    }: {
        open: boolean;
        onSelect: (path: string) => void;
        initialPath?: string;
    }) =>
        open ? (
            <div role="dialog" aria-label="Select Folder" data-initial-path={initialPath}>
                <button type="button" onClick={() => onSelect('/media/selected')}>
                    Select /media/selected
                </button>
            </div>
        ) : null,
}));

describe('PathControl', () => {
    beforeEach(() => {
        mockApply.mockReset();
        mockUseEffectiveRootFolder.mockReset();
        mockUseEffectiveRootFolder.mockReturnValue({
            data: { rootFolderPath: '/media/library' },
            isLoading: false,
        });
    });

    it('shows the root-folder-derived path without provenance when the path uses Inherit', () => {
        render(<PathControl contentHierarchyItemId="content-1" rawPath={null} />);

        expect(screen.getByTestId('path-control__value')).toHaveTextContent(/^\/media\/library$/);
        expect(screen.getByTestId('path-control__value')).toHaveAttribute('title', '/media/library');
        expect(screen.queryByText('Owned path')).not.toBeInTheDocument();
        expect(screen.queryByText('Inherited path')).not.toBeInTheDocument();
        expect(screen.queryByText('No explicit location')).not.toBeInTheDocument();
    });

    it('shows the explicit path without provenance when one is selected', () => {
        render(<PathControl contentHierarchyItemId="content-1" rawPath="/media/explicit" />);

        expect(screen.getByTestId('path-control__value')).toHaveTextContent(/^\/media\/explicit$/);
        expect(screen.getByTestId('path-control__value')).toHaveAttribute('title', '/media/explicit');
        expect(screen.queryByText('Owned path')).not.toBeInTheDocument();
        expect(screen.queryByText('Inherited path')).not.toBeInTheDocument();
    });

    it('offers the current path, Inherit, and folder selection as exact choices', async () => {
        const user = userEvent.setup();
        render(<PathControl contentHierarchyItemId="content-1" rawPath="/media/explicit" />);

        await user.click(screen.getByRole('button', { name: 'Edit content path' }));

        expect((await screen.findAllByRole('option')).map((option) => option.textContent)).toEqual([
            'Inherit',
            '/media/explicit',
            'Select folder',
        ]);
    });

    it('applies Inherit when the choice is selected', async () => {
        const user = userEvent.setup();
        mockApply.mockResolvedValue(undefined);
        render(<PathControl contentHierarchyItemId="content-1" rawPath="/media/explicit" />);

        await user.click(screen.getByRole('button', { name: 'Edit content path' }));
        await user.click(await screen.findByRole('option', { name: 'Inherit' }));

        await waitFor(() => {
            expect(mockApply).toHaveBeenCalledWith({ contentHierarchyItemId: 'content-1', path: null });
        });
    });

    it('opens the folder browser and applies the selected folder', async () => {
        const user = userEvent.setup();
        mockApply.mockResolvedValue(undefined);
        const { rerender } = render(<PathControl contentHierarchyItemId="content-1" rawPath={null} />);

        await user.click(screen.getByRole('button', { name: 'Edit content path' }));
        await user.click(await screen.findByRole('option', { name: 'Select folder' }));

        const browser = await screen.findByRole('dialog', { name: 'Select Folder' });
        expect(browser).toHaveAttribute('data-initial-path', '/media/library');
        await user.click(screen.getByRole('button', { name: 'Select /media/selected' }));

        await waitFor(() => {
            expect(mockApply).toHaveBeenCalledWith({
                contentHierarchyItemId: 'content-1',
                path: '/media/selected',
            });
        });

        rerender(<PathControl contentHierarchyItemId="content-1" rawPath="/media/selected" />);
        expect(screen.getByTestId('path-control__value')).toHaveTextContent(/^\/media\/selected$/);
    });

    it('reports a failed lookup with a retry action instead of the empty value', async () => {
        const user = userEvent.setup();
        const refetch = vi.fn();
        mockUseEffectiveRootFolder.mockReturnValue({
            data: undefined,
            isLoading: false,
            error: new Error('Request failed'),
            isFetching: false,
            refetch,
        });

        render(<PathControl contentHierarchyItemId="content-1" rawPath={null} />);

        expect(screen.getByTestId('path-control__value')).toHaveTextContent('Path could not be loaded');
        expect(screen.queryByText('No path')).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Retry loading path' }));
        expect(refetch).toHaveBeenCalledTimes(1);
    });

    it('keeps the explicit path when the root folder lookup fails', () => {
        mockUseEffectiveRootFolder.mockReturnValue({
            data: undefined,
            isLoading: false,
            error: new Error('Request failed'),
            isFetching: false,
            refetch: vi.fn(),
        });

        render(<PathControl contentHierarchyItemId="content-1" rawPath="/media/explicit" />);

        expect(screen.getByTestId('path-control__value')).toHaveTextContent(/^\/media\/explicit$/);
        expect(screen.queryByRole('button', { name: 'Retry loading path' })).not.toBeInTheDocument();
    });

    it('keeps the empty value copy for a successful empty lookup', () => {
        mockUseEffectiveRootFolder.mockReturnValue({
            data: { rootFolderId: null, source: 'None', resolvingHierarchyItemId: null, isOverriddenOnItem: false },
            isLoading: false,
            error: null,
        });

        render(<PathControl contentHierarchyItemId="content-1" rawPath={null} />);

        expect(screen.getByTestId('path-control__value')).toHaveTextContent('No path');
        expect(screen.queryByRole('button', { name: 'Retry loading path' })).not.toBeInTheDocument();
    });

    it('keeps a focused busy retry while a real failed lookup refetches, then shows the value', async () => {
        const user = userEvent.setup();
        let finishRetry: (value: EffectiveRootFolder) => void = () => {};
        const lookup = vi
            .fn<() => Promise<EffectiveRootFolder>>()
            .mockRejectedValueOnce(new Error('Request failed'))
            .mockImplementationOnce(() => new Promise((resolve) => (finishRetry = resolve)));
        mockUseEffectiveRootFolder.mockImplementation(() =>
            useQuery({ queryKey: ['effective-root-folder', 'content-1'], queryFn: lookup }),
        );

        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <PathControl contentHierarchyItemId="content-1" rawPath={null} />
            </QueryClientProvider>,
        );
        const retry = await screen.findByRole('button', { name: 'Retry loading path' });
        await user.click(retry);

        expect(retry).toBeInTheDocument();
        expect(retry).toHaveAttribute('aria-disabled', 'true');
        expect(retry).toHaveAttribute('aria-busy', 'true');
        expect(retry).toHaveFocus();
        expect(screen.getByTestId('path-control__value')).toHaveTextContent('Path could not be loaded');

        act(() =>
            finishRetry({
                rootFolderId: 'root-1',
                rootFolderName: 'Movies-Main',
                rootFolderPath: '/media/movies',
                source: 'ContentTypeDefault',
                resolvingHierarchyItemId: null,
                isOverriddenOnItem: false,
            }),
        );

        expect(await screen.findByText('/media/movies')).toBeInTheDocument();
        expect(screen.getByTestId('path-control__trigger')).toHaveFocus();
    });
});
