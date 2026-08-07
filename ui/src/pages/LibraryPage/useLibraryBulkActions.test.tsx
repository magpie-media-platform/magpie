import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { useBulkSelection } from '@/hooks/useBulkSelection';
import { ApiError } from '@/lib/api';
import * as api from '@/lib/api';

import { useLibraryBulkActions } from './useLibraryBulkActions';

const mockToast = vi.fn();
const mockUseQualityTierLists = vi.fn();

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
        apiPost: vi.fn(),
        apiPatch: vi.fn(),
    };
});

vi.mock('@/components/ui/Toast', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/components/ui/Toast')>();
    return { ...actual, useToast: () => ({ toast: mockToast }) };
});

vi.mock('@/hooks/useEventStream', () => ({
    useEventStream: vi.fn(),
}));

vi.mock('@/hooks/useRootFolders', () => ({
    useRootFolders: () => ({ folders: [] }),
}));

vi.mock('@/hooks/useQualityTierLists', () => ({
    useQualityTierLists: (options: unknown) => mockUseQualityTierLists(options),
}));

const SCOPE_FILTER = 'ParentId null and ContentType = "kind-x" and Level = "kind-x-scene"';

function createWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

function renderBulkActions(contentType?: string, level?: string, bulkFilter: string | null = SCOPE_FILTER) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
    });

    return renderHook(
        () => {
            const selection = useBulkSelection({ pageRowIds: ['content-1', 'content-2'], totalCount: 250 });
            const actions = useLibraryBulkActions({ selection, bulkFilter, contentType, level });
            return { selection, actions };
        },
        { wrapper: createWrapper(queryClient) },
    );
}

describe('useLibraryBulkActions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(api.apiGet).mockResolvedValue([]);
        vi.mocked(api.apiPatch).mockResolvedValue({ count: 1, error: null });
        vi.mocked(api.apiPost).mockResolvedValue({ count: 1, error: null });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [] });
    });

    it('requests compatible tier lists for the active content level', () => {
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [
                { id: 'video', name: 'Video', mimeTypes: ['video/*'] },
                { id: 'universal', name: 'Universal', mimeTypes: [] },
            ],
        });

        const { result } = renderBulkActions('movies', 'movie');

        expect(mockUseQualityTierLists).toHaveBeenCalledWith({ contentType: 'movies', level: 'movie' });
        expect(result.current.actions.qualityTierListOptions).toEqual([
            { id: 'video', label: 'Video - video/*' },
            { id: 'universal', label: 'Universal - Any MIME type' },
        ]);
    });

    it('offers only universal tier lists when the selection spans content types', () => {
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [
                { id: 'video', name: 'Video', mimeTypes: ['video/*'] },
                { id: 'universal', name: 'Universal', mimeTypes: [] },
            ],
        });

        const { result } = renderBulkActions();

        expect(result.current.actions.qualityTierListOptions).toEqual([
            { id: 'universal', label: 'Universal - Any MIME type' },
        ]);
    });

    describe('explicit ids', () => {
        it('does nothing when the route has no safe bulk scope', async () => {
            const { result } = renderBulkActions(undefined, undefined, null);

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(true);
            });

            expect(api.apiPatch).not.toHaveBeenCalled();
        });

        it('PATCHes the consolidated bulk route to monitor the selected ids', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(true);
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                ids: ['content-1'],
                patch: { monitoring: { value: true } },
            });
        });

        it('PATCHes the consolidated bulk route to unmonitor the selected ids', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            act(() => result.current.selection.toggleRow('content-2', true));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(false);
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                ids: ['content-1', 'content-2'],
                patch: { monitoring: { value: false } },
            });
        });

        it('PATCHes the consolidated bulk route to add and remove tags on the selected ids', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleApplyTags({
                    tagsToAdd: ['Favorites'],
                    tagsToRemove: ['Archive'],
                });
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                ids: ['content-1'],
                patch: { tags: { add: ['Favorites'], remove: ['Archive'] } },
            });
        });

        it('PATCHes the consolidated bulk route to set the root folder on the selected ids', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleApplyRootFolder('root-1');
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                ids: ['content-1'],
                patch: { rootFolderId: { value: 'root-1' } },
            });
        });

        it('PATCHes the consolidated bulk route to reset the quality tier list on the selected ids', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleApplyQualityTierList(null);
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                ids: ['content-1'],
                patch: { qualityTierListId: { value: null } },
            });
        });

        it('POSTs the bulk delete route with the selected ids', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleConfirmBulkDelete();
            });

            expect(api.apiPost).toHaveBeenCalledWith('/content/bulk/delete', { ids: ['content-1'] });
        });
    });

    describe('select across pages', () => {
        it('sends the scope filter instead of ids when monitoring every matching item', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.selectAllAcrossPages());
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(false);
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                filter: SCOPE_FILTER,
                patch: { monitoring: { value: false } },
            });
        });

        it('sends the scope filter instead of ids when tagging every matching item', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.selectAllAcrossPages());
            await act(async () => {
                await result.current.actions.handleApplyTags({ tagsToAdd: ['Favorites'], tagsToRemove: [] });
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                filter: SCOPE_FILTER,
                patch: { tags: { add: ['Favorites'] } },
            });
        });

        it('sends the scope filter instead of ids when setting the root folder on every matching item', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.selectAllAcrossPages());
            await act(async () => {
                await result.current.actions.handleApplyRootFolder('root-1');
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                filter: SCOPE_FILTER,
                patch: { rootFolderId: { value: 'root-1' } },
            });
        });

        it('sends the scope filter instead of ids when setting quality tiers on every matching item', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.selectAllAcrossPages());
            await act(async () => {
                await result.current.actions.handleApplyQualityTierList('tier-1');
            });

            expect(api.apiPatch).toHaveBeenCalledWith('/content/bulk', {
                filter: SCOPE_FILTER,
                patch: { qualityTierListId: { value: 'tier-1' } },
            });
        });

        it('sends the scope filter instead of ids when deleting every matching item', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.selectAllAcrossPages());
            await act(async () => {
                await result.current.actions.handleConfirmBulkDelete();
            });

            expect(api.apiPost).toHaveBeenCalledWith('/content/bulk/delete', { filter: SCOPE_FILTER });
        });

        it('refuses to widen the target set when rows were deselected inside a select-all', async () => {
            const { result } = renderBulkActions();

            act(() => result.current.selection.selectAllAcrossPages());
            act(() => result.current.selection.toggleRow('content-2', false));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(false);
            });

            expect(api.apiPatch).not.toHaveBeenCalled();
            expect(mockToast).toHaveBeenCalledWith({
                title: 'Cannot apply to a partial select-all',
                description:
                    'Deselecting rows within a select-all is not supported. Clear the selection and pick rows directly.',
                variant: 'error',
            });
        });
    });

    describe('outcome handling', () => {
        it('reports the updated count and clears the selection on success', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue({ count: 3, error: null });
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(false);
            });

            expect(mockToast).toHaveBeenCalledWith({
                title: '3 items unmonitored',
                variant: 'success',
            });
            await waitFor(() => expect(result.current.selection.selectedIds).toEqual([]));
        });

        it('refreshes the content list after a bulk patch', async () => {
            const queryClient = new QueryClient({
                defaultOptions: { queries: { retry: false, gcTime: 0, staleTime: 0 } },
            });
            const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries');
            const { result } = renderHook(
                () => {
                    const selection = useBulkSelection({ pageRowIds: ['content-1'], totalCount: 1 });
                    const actions = useLibraryBulkActions({ selection, bulkFilter: SCOPE_FILTER });
                    return { selection, actions };
                },
                { wrapper: createWrapper(queryClient) },
            );

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(false);
            });

            await waitFor(() => expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['content'] }));
        });

        it('surfaces the server error detail when the request fails', async () => {
            vi.mocked(api.apiPatch).mockRejectedValue(
                new ApiError(400, 'Unknown field Bogus at position 4', { message: 'Unknown field Bogus' }),
            );
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleBulkSetMonitoring(false);
            });

            expect(mockToast).toHaveBeenCalledWith({
                title: 'Failed to unmonitor items',
                description: 'Unknown field Bogus at position 4',
                variant: 'error',
            });
        });

        it('surfaces the server error carried on a successful response', async () => {
            vi.mocked(api.apiPatch).mockResolvedValue({ count: 0, error: 'Root folder is not writable' });
            const { result } = renderBulkActions();

            act(() => result.current.selection.toggleRow('content-1', true));
            await act(async () => {
                await result.current.actions.handleApplyRootFolder('root-1');
            });

            expect(mockToast).toHaveBeenCalledWith({
                title: 'Failed to update root folder',
                description: 'Root folder is not writable',
                variant: 'error',
            });
        });
    });
});
