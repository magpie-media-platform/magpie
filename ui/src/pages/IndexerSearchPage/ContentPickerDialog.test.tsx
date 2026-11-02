import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';
import { useContentList } from '@/hooks/useContent';
import { apiGet } from '@/lib/api';
import type { ContentListItem } from '@/types/content';

import { ContentPickerDialog } from './ContentPickerDialog';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
}));

vi.mock('@/hooks/useContent', () => ({ useContentList: vi.fn() }));
vi.mock('@/hooks/useDebouncedValue', () => ({ useDebouncedValue: (value: string) => value }));
vi.mock('@/components/filters/FilterBar', () => ({
    FilterBar: ({ value, onChange }: { value: string; onChange: (value: string) => void }) => (
        <input aria-label="Content filter" value={value} onChange={(event) => onChange(event.target.value)} />
    ),
}));

const ITEM: ContentListItem = {
    id: 'movie-1',
    title: 'The Salt Roads',
    posterUrl: 'https://example.com/poster.jpg',
    level: 'movie',
    levelLabel: 'Movie',
    contentType: 'movies',
    year: 2019,
    monitoring: 'Monitored',
    effectiveMonitoring: 'Monitored',
    fulfillment: 'missing',
};

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
const onSelect = vi.fn();
const onOpenChange = vi.fn();

beforeEach(() => {
    vi.clearAllMocks();
    queryClient.clear();
    localStorage.clear();
    vi.mocked(apiGet).mockResolvedValue([]);
    vi.mocked(useContentList).mockReturnValue({
        data: { items: [ITEM], totalItems: 1, pageNumber: 1, pageSize: 25 },
        isLoading: false,
    } as ReturnType<typeof useContentList>);
});

describe('ContentPickerDialog', () => {
    it('Gallery_ShowsPosterPlaceholders_WhenContentIsLoading', () => {
        // Arrange
        localStorage.setItem('content-picker-view', '"gallery"');
        vi.mocked(useContentList).mockReturnValue({ isLoading: true } as ReturnType<typeof useContentList>);

        // Act
        render(
            <QueryClientProvider client={queryClient}>
                <ContentPickerDialog
                    open
                    mode="match"
                    isSubmitting={false}
                    onOpenChange={onOpenChange}
                    onSelect={onSelect}
                />
            </QueryClientProvider>,
        );

        // Assert
        expect(screen.getByRole('status', { name: 'Loading content' })).toBeInTheDocument();
        expect(document.querySelectorAll('.content-gallery-skeleton .content-gallery-cell')).toHaveLength(6);
        expect(document.querySelector('.content-picker__loading')).not.toBeInTheDocument();
    });

    it.each([
        { mode: 'match' as const, actionLabel: 'Match' },
        { mode: 'grab' as const, actionLabel: 'Grab' },
    ])('Gallery_SelectsOneItemWithoutNavigation_WhenModeIs$mode', async ({ mode, actionLabel }) => {
        // Arrange
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ContentPickerDialog
                        open
                        mode={mode}
                        isSubmitting={false}
                        onOpenChange={onOpenChange}
                        onSelect={onSelect}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Act
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));
        await user.click(screen.getByRole('button', { name: actionLabel }));

        // Assert
        expect(screen.getByRole('list', { name: 'Library content gallery' })).toBeInTheDocument();
        expect(screen.getByText('The Salt Roads')).toBeInTheDocument();
        expect(screen.getByText('(2019)')).toBeInTheDocument();
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Select The Salt Roads' })).not.toBeInTheDocument();
        expect(onSelect).toHaveBeenCalledExactlyOnceWith(ITEM);
        expect(onOpenChange).not.toHaveBeenCalled();
        expect(localStorage.getItem('content-picker-view')).toBe('"gallery"');
    });

    it.each([
        { mode: 'match' as const, actionLabel: 'Match' },
        { mode: 'grab' as const, actionLabel: 'Grab' },
    ])('Gallery_DisablesSelection_WhenSubmittingIn$modeMode', ({ mode, actionLabel }) => {
        // Arrange
        localStorage.setItem('content-picker-view', '"gallery"');
        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ContentPickerDialog
                        open
                        mode={mode}
                        isSubmitting
                        onOpenChange={onOpenChange}
                        onSelect={onSelect}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Act
        fireEvent.click(screen.getByRole('button', { name: actionLabel }));

        // Assert
        expect(screen.getByRole('tab', { name: 'Gallery' })).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByRole('button', { name: actionLabel })).toBeDisabled();
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('ViewSwitch_PreservesSearchFiltersAndSelection_WhenReturningToList', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <ContentPickerDialog
                        open
                        mode="match"
                        isSubmitting={false}
                        onOpenChange={onOpenChange}
                        onSelect={onSelect}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );
        fireEvent.change(screen.getByRole('textbox', { name: 'Search library' }), { target: { value: 'Salt' } });
        fireEvent.change(screen.getByRole('textbox', { name: 'Content filter' }), {
            target: { value: 'year = 2019' },
        });

        // Act
        await user.click(screen.getByRole('tab', { name: 'Gallery' }));
        await user.click(screen.getByRole('tab', { name: 'List' }));
        await user.click(screen.getByRole('button', { name: 'Match' }));

        // Assert
        expect(screen.getByRole('textbox', { name: 'Search library' })).toHaveValue('Salt');
        expect(screen.getByRole('textbox', { name: 'Content filter' })).toHaveValue('year = 2019');
        expect(screen.queryByRole('list', { name: 'Library content gallery' })).not.toBeInTheDocument();
        expect(screen.getByText('The Salt Roads')).toBeInTheDocument();
        expect(useContentList).toHaveBeenLastCalledWith(
            { filter: 'title contains "Salt" and (year = 2019)', pageSize: 25 },
            { enabled: true },
        );
        expect(onSelect).toHaveBeenCalledExactlyOnceWith(ITEM);
    });
});
