import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { QualityTierControl } from './QualityTierControl';
import type { ApiQualityTierList } from '@/hooks/useQualityTierLists';
import type { EffectiveQualityTierList } from '@/types';
import { QualityStatusState } from '@/hooks/useQualityStatus';

const mockUseEffectiveQualityTierList = vi.fn();
const mockUseQualityTierLists = vi.fn();
const mockUseQualityStatus = vi.fn();
const mockMutateAsync = vi.fn();
const mockToast = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useEffectiveQualityTierList: () => mockUseEffectiveQualityTierList(),
    useSetQualityTierList: () => ({ mutateAsync: mockMutateAsync, isPending: false }),
}));

vi.mock('@/hooks/useQualityTierLists', () => ({
    useQualityTierLists: (options: unknown) => mockUseQualityTierLists(options),
}));

vi.mock('@/hooks/useQualityStatus', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useQualityStatus')>('@/hooks/useQualityStatus');
    return { ...actual, useQualityStatus: () => mockUseQualityStatus() };
});

vi.mock('@/components/ui/Toast', async () => {
    const actual = await vi.importActual<typeof import('@/components/ui/Toast')>('@/components/ui/Toast');
    return { ...actual, useToast: () => ({ toast: mockToast }) };
});

function makeList(overrides: Partial<ApiQualityTierList> = {}): ApiQualityTierList {
    return {
        id: 'list-1',
        name: 'HD Tiers',
        mimeTypes: [],
        upgradeAllowed: false,
        createdAt: '2026-01-01T00:00:00Z',
        qualityTierRanks: [],
        ...overrides,
    };
}

function makeEffective(overrides: Partial<EffectiveQualityTierList> = {}): EffectiveQualityTierList {
    return {
        qualityTierListId: 'list-1',
        qualityTierListName: 'HD Tiers',
        source: 'ContentTypeDefaultRootFolder',
        resolvingHierarchyItemId: null,
        resolvingHierarchyItemName: null,
        ownQualityTierListId: null,
        ...overrides,
    };
}

function Wrap({ children }: { children: ReactNode }) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe('QualityTierControl', () => {
    beforeEach(() => {
        mockUseEffectiveQualityTierList.mockReset();
        mockUseQualityTierLists.mockReset();
        mockUseQualityStatus.mockReset();
        mockUseQualityStatus.mockReturnValue({ data: undefined, isLoading: false });
        mockMutateAsync.mockReset();
        mockToast.mockReset();
    });

    it('shows only the effective tier list value in read state', () => {
        mockUseEffectiveQualityTierList.mockReturnValue({
            data: makeEffective({ source: 'ContentTypeDefaultRootFolder' }),
            isLoading: false,
        });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        const label = screen.getByTestId('quality-tier-control__label');
        expect(label).toHaveTextContent(/^HD Tiers$/);
        expect(label).not.toHaveTextContent('content-type default');
        expect(screen.getByText('Quality')).toBeInTheDocument();
        expect(screen.queryByTestId('quality-tier-control__select')).not.toBeInTheDocument();
    });

    it('reveals the inline select on click with an Inherit choice', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('quality-tier-control__trigger'));

        const select = screen.getByTestId('quality-tier-control__select');
        expect(select).toHaveAttribute('aria-label', 'Quality');
        expect(select).toHaveAttribute('data-component', 'select');
        expect(select).toHaveAttribute('data-size', 'sm');
        expect(await screen.findByRole('option', { name: 'Inherit' })).toBeInTheDocument();
    });

    it('requests lists for the target level and displays their MIME applicability', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [
                makeList({ mimeTypes: ['video/*'] }),
                makeList({ id: 'list-2', name: 'Universal', mimeTypes: [] }),
            ],
            isLoading: false,
        });

        render(<QualityTierControl contentHierarchyItemId="c-1" contentType="movies" level="movie" />, {
            wrapper: Wrap,
        });
        await user.click(screen.getByTestId('quality-tier-control__trigger'));

        expect(mockUseQualityTierLists).toHaveBeenCalledWith({ contentType: 'movies', level: 'movie' });
        expect(await screen.findByRole('option', { name: 'HD Tiers - video/*' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Universal - Any MIME type' })).toBeInTheDocument();
    });

    it('resolves an excluded current assignment from the unfiltered tier lists', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({
            data: makeEffective({ ownQualityTierListId: 'audio-list' }),
            isLoading: false,
        });
        mockUseQualityTierLists.mockImplementation((options: unknown) => {
            const scoped = typeof options === 'object' && options !== null && 'contentType' in options;
            return {
                tierLists: scoped
                    ? [makeList({ id: 'universal-list', name: 'Universal' })]
                    : [makeList({ id: 'audio-list', name: 'Audio Tiers', mimeTypes: ['audio/*'] })],
                isLoading: false,
            };
        });

        render(<QualityTierControl contentHierarchyItemId="c-1" contentType="movies" level="movie" />, {
            wrapper: Wrap,
        });
        await user.click(screen.getByTestId('quality-tier-control__trigger'));

        expect(screen.getByTestId('quality-tier-control__select')).toHaveTextContent('Audio Tiers - audio/*');
        expect(await screen.findByRole('option', { name: 'Universal - Any MIME type' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Audio Tiers - audio/*' })).not.toBeInTheDocument();
    });

    it('shows the cutoff-status badge alongside the tier value', () => {
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });
        mockUseQualityStatus.mockReturnValue({
            data: { status: QualityStatusState.MetCutoff, cutoffScore: 1, bestScore: 2 },
            isLoading: false,
        });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        const cutoff = screen.getByTestId('quality-tier-control__cutoff');
        expect(cutoff).toHaveAttribute('data-status', 'met-cutoff');
        expect(cutoff).toHaveTextContent('Met cutoff');
        expect(screen.getByTestId('quality-tier-control__label').textContent).toContain('HD Tiers');
    });

    it('omits the cutoff badge when the endpoint reports no tier list', () => {
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });
        mockUseQualityStatus.mockReturnValue({
            data: { status: QualityStatusState.NoTierList },
            isLoading: false,
        });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        expect(screen.queryByTestId('quality-tier-control__cutoff')).not.toBeInTheDocument();
    });

    it('omits the inherited source and resolving ancestor name', () => {
        mockUseEffectiveQualityTierList.mockReturnValue({
            data: makeEffective({ source: 'Inherited', resolvingHierarchyItemName: 'My Show' }),
            isLoading: false,
        });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        const label = screen.getByTestId('quality-tier-control__label');
        expect(label).toHaveTextContent(/^HD Tiers$/);
        expect(label).not.toHaveTextContent('inherited from My Show');
    });

    it('sets the override when a tier list is selected and returns to read state', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [makeList({ id: 'list-1', name: 'HD Tiers' }), makeList({ id: 'list-2', name: 'SD Tiers' })],
            isLoading: false,
        });
        mockMutateAsync.mockResolvedValue(makeEffective({ source: 'ExplicitOnItem', ownQualityTierListId: 'list-2' }));

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('quality-tier-control__trigger'));
        await user.click(await screen.findByRole('option', { name: 'SD Tiers - Any MIME type' }));

        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith({
                contentHierarchyItemId: 'c-1',
                qualityTierListId: 'list-2',
            });
        });
        expect(mockToast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }));
        await waitFor(() => {
            expect(screen.queryByTestId('quality-tier-control__select')).not.toBeInTheDocument();
        });
    });

    it('clears the override when "Inherit" is chosen', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({
            data: makeEffective({ source: 'ExplicitOnItem', ownQualityTierListId: 'list-1' }),
            isLoading: false,
        });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });
        mockMutateAsync.mockResolvedValue(makeEffective({ source: 'None', ownQualityTierListId: null }));

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('quality-tier-control__trigger'));
        await user.click(await screen.findByRole('option', { name: 'Inherit' }));

        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith({
                contentHierarchyItemId: 'c-1',
                qualityTierListId: null,
            });
        });
    });

    it('keeps a usable edit state with an error toast when the mutation rejects', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [makeList({ id: 'list-2', name: 'SD Tiers' })],
            isLoading: false,
        });
        mockMutateAsync.mockRejectedValue(new Error('tier list not found'));

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('quality-tier-control__trigger'));
        await user.click(await screen.findByRole('option', { name: 'SD Tiers - Any MIME type' }));

        await waitFor(() => {
            expect(mockToast).toHaveBeenCalledWith(
                expect.objectContaining({
                    variant: 'error',
                    description: expect.stringContaining('tier list not found'),
                }),
            );
        });
        expect(screen.getByTestId('quality-tier-control__select')).toBeInTheDocument();
    });

    it('swaps the closed value for the dropdown inside the shared inline-edit field', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        const field = screen.getByTestId('quality-tier-control__trigger').parentElement;
        expect(field).toHaveClass('inline-edit-row__field');
        expect(field).toHaveAttribute('data-state', 'idle');

        await user.click(screen.getByTestId('quality-tier-control__trigger'));

        expect(screen.getByTestId('quality-tier-control__select').parentElement).toBe(field);
        expect(field).toHaveAttribute('data-state', 'editing');
    });

    it('returns focus to the row trigger after a tier list is selected', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({
            tierLists: [makeList({ id: 'list-1', name: 'HD Tiers' }), makeList({ id: 'list-2', name: 'SD Tiers' })],
            isLoading: false,
        });
        mockMutateAsync.mockResolvedValue(makeEffective({ source: 'ExplicitOnItem', ownQualityTierListId: 'list-2' }));

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('quality-tier-control__trigger'));
        await user.click(await screen.findByRole('option', { name: 'SD Tiers - Any MIME type' }));

        await waitFor(() => {
            expect(screen.getByTestId('quality-tier-control__trigger')).toHaveFocus();
        });
    });

    it('returns focus to the row trigger when the editor is dismissed with Escape', async () => {
        const user = userEvent.setup();
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });

        render(<QualityTierControl contentHierarchyItemId="c-1" />, { wrapper: Wrap });

        await user.click(screen.getByTestId('quality-tier-control__trigger'));
        await screen.findByRole('option', { name: 'Inherit' });
        await user.keyboard('{Escape}');

        await waitFor(() => {
            expect(screen.getByTestId('quality-tier-control__trigger')).toHaveFocus();
        });
        expect(mockMutateAsync).not.toHaveBeenCalled();
    });

    it('hides the edit affordance while keeping the effective value for read-only users', () => {
        mockUseEffectiveQualityTierList.mockReturnValue({ data: makeEffective(), isLoading: false });
        mockUseQualityTierLists.mockReturnValue({ tierLists: [makeList()], isLoading: false });

        render(<QualityTierControl contentHierarchyItemId="c-1" canEdit={false} />, { wrapper: Wrap });

        expect(screen.getByTestId('quality-tier-control__label')).toHaveTextContent('HD Tiers');
        expect(screen.queryByTestId('quality-tier-control__trigger')).not.toBeInTheDocument();
        expect(screen.queryByTestId('quality-tier-control__select')).not.toBeInTheDocument();
    });
});
