import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { RootFolderControl } from './RootFolderControl';
import type { ApiRootFolder } from '@/hooks/useRootFolders';
import type { EffectiveRootFolder } from '@/types';

const mockUseEffectiveRootFolder = vi.fn();
const mockUseRootFolders = vi.fn();
const mockMutateAsync = vi.fn();
const mockToast = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useEffectiveRootFolder: () => mockUseEffectiveRootFolder(),
    useSetContentRootFolder: () => ({ mutateAsync: mockMutateAsync, isPending: false }),
}));

vi.mock('@/hooks/useRootFolders', () => ({
    useRootFolders: () => mockUseRootFolders(),
}));

vi.mock('@/components/ui/Toast', async () => {
    const actual = await vi.importActual<typeof import('@/components/ui/Toast')>('@/components/ui/Toast');
    return { ...actual, useToast: () => ({ toast: mockToast }) };
});

function makeRoot(overrides: Partial<ApiRootFolder> = {}): ApiRootFolder {
    return {
        id: 'root-1',
        name: 'Movies-Main',
        path: '/media/movies',
        contentType: 'movie',
        isDefault: false,
        defaultMonitoring: 'Monitored',
        renameTemplate: null,
        upgradeFileHandling: 'Replace',
        autoImport: false,
        createdAt: '2026-01-01T00:00:00Z',
        ...overrides,
        defaults: overrides.defaults ?? {},
    };
}

function makeEffective(overrides: Partial<EffectiveRootFolder> = {}): EffectiveRootFolder {
    return {
        rootFolderId: 'root-1',
        rootFolderName: 'Movies-Main',
        rootFolderPath: '/media/movies',
        source: 'ContentTypeDefault',
        resolvingHierarchyItemId: null,
        isOverriddenOnItem: false,
        ...overrides,
    };
}

function Wrap({ children }: { children: ReactNode }) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe('RootFolderControl', () => {
    beforeEach(() => {
        mockUseEffectiveRootFolder.mockReset();
        mockUseRootFolders.mockReset();
        mockMutateAsync.mockReset();
        mockToast.mockReset();
    });

    it('shows only the effective root folder value in read state', () => {
        mockUseEffectiveRootFolder.mockReturnValue({
            data: makeEffective({ source: 'ContentTypeDefault' }),
            isLoading: false,
        });
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        const label = screen.getByTestId('root-folder-control__label');
        expect(label).toHaveTextContent(/^Movies-Main$/);
        expect(label).not.toHaveTextContent('content-type default');
        expect(screen.getByText('Root Folder')).toBeInTheDocument();
        expect(screen.queryByTestId('root-folder-control__select')).not.toBeInTheDocument();
    });

    it('reveals the inline select on click with an Inherit choice', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('root-folder-control__trigger'));

        const select = screen.getByTestId('root-folder-control__select');
        expect(select).toHaveAttribute('aria-label', 'Root folder override');
        expect(select).toHaveAttribute('data-component', 'select');
        expect(select).toHaveAttribute('data-size', 'sm');
        expect(await screen.findByRole('option', { name: 'Inherit' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: /Inherit \/ default/ })).not.toBeInTheDocument();
    });

    it('sets the override when a root folder is selected and returns to read state', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({
            folders: [
                makeRoot({ id: 'root-1', name: 'Movies-Main' }),
                makeRoot({ id: 'root-2', name: 'Movies-Archive', path: '/media/movies-archive' }),
            ],
            isLoading: false,
        });
        mockMutateAsync.mockResolvedValue(makeEffective({ source: 'Override', isOverriddenOnItem: true }));

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('root-folder-control__trigger'));
        await user.click(await screen.findByRole('option', { name: /Movies-Archive/ }));

        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith({
                contentHierarchyItemId: 'c-1',
                rootFolderId: 'root-2',
            });
        });
        expect(mockToast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }));
        await waitFor(() => {
            expect(screen.queryByTestId('root-folder-control__select')).not.toBeInTheDocument();
        });
    });

    it('clears the override when "Inherit" is chosen', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({
            data: makeEffective({ source: 'Override', isOverriddenOnItem: true }),
            isLoading: false,
        });
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });
        mockMutateAsync.mockResolvedValue(makeEffective({ source: 'ContentTypeDefault', isOverriddenOnItem: false }));

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('root-folder-control__trigger'));
        await user.click(await screen.findByRole('option', { name: 'Inherit' }));

        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith({
                contentHierarchyItemId: 'c-1',
                rootFolderId: null,
            });
        });
    });

    it('keeps a usable edit state with an error toast when the mutation rejects', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({
            folders: [makeRoot({ id: 'root-2', name: 'Movies-Archive' })],
            isLoading: false,
        });
        mockMutateAsync.mockRejectedValue(new Error('content type mismatch'));

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('root-folder-control__trigger'));
        await user.click(await screen.findByRole('option', { name: /Movies-Archive/ }));

        await waitFor(() => {
            expect(mockToast).toHaveBeenCalledWith(
                expect.objectContaining({
                    variant: 'error',
                    description: expect.stringContaining('content type mismatch'),
                }),
            );
        });
        expect(screen.getByTestId('root-folder-control__select')).toBeInTheDocument();
    });

    it('swaps the closed value for the dropdown inside the shared inline-edit field', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        const field = screen.getByTestId('root-folder-control__trigger').parentElement;
        expect(field).toHaveClass('inline-edit-row__field');
        expect(field).toHaveAttribute('data-state', 'idle');

        await user.click(screen.getByTestId('root-folder-control__trigger'));

        expect(screen.getByTestId('root-folder-control__select').parentElement).toBe(field);
        expect(field).toHaveAttribute('data-state', 'editing');
    });

    it('returns focus to the row trigger after a root folder is selected', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({
            folders: [
                makeRoot({ id: 'root-1', name: 'Movies-Main' }),
                makeRoot({ id: 'root-2', name: 'Movies-Archive', path: '/media/movies-archive' }),
            ],
            isLoading: false,
        });
        mockMutateAsync.mockResolvedValue(makeEffective({ source: 'Override', isOverriddenOnItem: true }));

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('root-folder-control__trigger'));
        await user.click(await screen.findByRole('option', { name: /Movies-Archive/ }));

        await waitFor(() => {
            expect(screen.getByTestId('root-folder-control__trigger')).toHaveFocus();
        });
    });

    it('returns focus to the row trigger when the editor is dismissed with Escape', async () => {
        const user = userEvent.setup();
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('root-folder-control__trigger'));
        await screen.findByRole('option', { name: 'Inherit' });
        await user.keyboard('{Escape}');

        await waitFor(() => {
            expect(screen.getByTestId('root-folder-control__trigger')).toHaveFocus();
        });
        expect(mockMutateAsync).not.toHaveBeenCalled();
    });

    it('hides the edit affordance while keeping the effective value for read-only users', () => {
        mockUseEffectiveRootFolder.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" canEdit={false} />, {
            wrapper: Wrap,
        });

        expect(screen.getByTestId('root-folder-control__label')).toHaveTextContent('Movies-Main');
        expect(screen.queryByTestId('root-folder-control__trigger')).not.toBeInTheDocument();
        expect(screen.queryByTestId('root-folder-control__select')).not.toBeInTheDocument();
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
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        expect(screen.getByTestId('root-folder-control__label')).toHaveTextContent('Root folder could not be loaded');
        expect(screen.queryByText('No root folder')).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Retry loading root folder' }));
        expect(refetch).toHaveBeenCalledTimes(1);
    });

    it('keeps the empty value copy for a successful empty lookup', () => {
        mockUseEffectiveRootFolder.mockReturnValue({
            data: { rootFolderId: null, source: 'None', resolvingHierarchyItemId: null, isOverriddenOnItem: false },
            isLoading: false,
            error: null,
        });
        mockUseRootFolders.mockReturnValue({ folders: [], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });

        expect(screen.getByTestId('root-folder-control__label')).toHaveTextContent('No root folder');
        expect(screen.queryByRole('button', { name: 'Retry loading root folder' })).not.toBeInTheDocument();
    });

    it('keeps a focused busy retry while a real failed lookup refetches, then shows the value', async () => {
        const user = userEvent.setup();
        let finishRetry: (value: EffectiveRootFolder) => void = () => {};
        const lookup = vi
            .fn<() => Promise<EffectiveRootFolder>>()
            .mockRejectedValueOnce(new Error('Request failed'))
            .mockImplementationOnce(() => new Promise((resolve) => (finishRetry = resolve)));
        mockUseEffectiveRootFolder.mockImplementation(() =>
            useQuery({ queryKey: ['effective-root-folder', 'c-1'], queryFn: lookup }),
        );
        mockUseRootFolders.mockReturnValue({ folders: [makeRoot()], isLoading: false });

        render(<RootFolderControl contentHierarchyItemId="c-1" contentType="movie" />, { wrapper: Wrap });
        const retry = await screen.findByRole('button', { name: 'Retry loading root folder' });
        await user.click(retry);

        expect(retry).toBeInTheDocument();
        expect(retry).toHaveAttribute('aria-disabled', 'true');
        expect(retry).toHaveAttribute('aria-busy', 'true');
        expect(retry).toHaveFocus();
        expect(screen.getByTestId('root-folder-control__label')).toHaveTextContent('Root folder could not be loaded');

        act(() => finishRetry(makeEffective()));

        expect(await screen.findByText('Movies-Main')).toBeInTheDocument();
        expect(screen.getByTestId('root-folder-control__trigger')).toHaveFocus();
    });
});
