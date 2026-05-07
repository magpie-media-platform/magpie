import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import type { ContentFilterOption, QualityTierListOption, TagOption } from '@/types';
import { WishlistForm, valuesEqual } from './WishlistForm';

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
            },
        },
    });
}

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = createTestQueryClient();
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <TooltipProvider>{children}</TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

const mockFilters: ContentFilterOption[] = [
    { id: 'filter-1', name: 'Action Movies' },
    { id: 'filter-2', name: 'Sci-Fi >= 7.5' },
    { id: 'filter-3', name: 'Family Friendly' },
];

const mockQualityTierLists: QualityTierListOption[] = [
    { id: 'qtl-1', name: 'HD' },
    { id: 'qtl-2', name: '4K Ultra HD' },
    { id: 'qtl-3', name: 'Any Quality' },
];

const mockTags: TagOption[] = [
    { id: 'tag-1', name: 'Movies' },
    { id: 'tag-2', name: 'TV Shows' },
    { id: 'tag-3', name: 'Documentary' },
];

describe('WishlistForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={false}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Wishlist')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Wishlist')).toBeInTheDocument();
    });

    it('shows "Edit Wishlist" when editing', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'wl-1',
                        name: 'My Wishlist',
                        filterId: null,
                        qualityTierListId: null,
                        monitored: true,
                        tagIds: [],
                    }}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Wishlist')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'wl-1',
                        name: 'My Test Wishlist',
                        filterId: 'filter-1',
                        qualityTierListId: 'qtl-2',
                        monitored: true,
                        tagIds: [],
                    }}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('My Test Wishlist')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders content filter picker', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const elements = screen.getAllByText('No filter');
        expect(elements.length).toBeGreaterThan(0);
    });

    it('renders quality tier list picker', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const elements = screen.getAllByText('Default quality');
        expect(elements.length).toBeGreaterThan(0);
    });

    it('renders monitored toggle', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/monitored/i)).toBeInTheDocument();
    });

    it('renders tags picker when tags are available', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Movies')).toBeInTheDocument();
        expect(screen.getByText('TV Shows')).toBeInTheDocument();
        expect(screen.getByText('Documentary')).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });

    it('shows validation error when name is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('calls onSubmit with form values when valid', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Wishlist');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: 'Test Wishlist',
                monitored: true,
            }),
        );
    });

    it('toggles tags when clicked', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Wishlist');

        const moviesTag = screen.getByText('Movies');
        await user.click(moviesTag);

        const docTag = screen.getByText('Documentary');
        await user.click(docTag);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                tagIds: ['tag-1', 'tag-3'],
            }),
        );
    });

    it('shows loading spinner on save button when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });

    it('shows read-only mode when canEdit is false', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'wl-1',
                        name: 'Other User Wishlist',
                        filterId: null,
                        qualityTierListId: null,
                        monitored: true,
                        tagIds: [],
                    }}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    canEdit={false}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();

        const closeButtons = screen.getAllByRole('button', { name: /close/i });
        expect(closeButtons.length).toBeGreaterThan(0);
    });

    it('shows owner lock indicator when editing another user wishlist', () => {
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'wl-1',
                        name: 'Other User Wishlist',
                        filterId: null,
                        qualityTierListId: null,
                        monitored: true,
                        tagIds: [],
                    }}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    isOwner={false}
                />
            </TestWrapper>,
        );

        expect(screen.getByTestId('wishlist-form__owner-lock')).toBeInTheDocument();
    });

    it('selects filter from picker', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Wishlist');

        const filterTrigger = document.querySelector('[data-field="filterId"]') as HTMLElement;
        await user.click(filterTrigger);
        const filterOption = await screen.findByRole('option', { name: 'Action Movies' });
        await user.click(filterOption);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                filterId: 'filter-1',
            }),
        );
    });

    it('selects quality tier list from picker', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Wishlist');

        const qtlTrigger = document.querySelector('[data-field="qualityTierListId"]') as HTMLElement;
        await user.click(qtlTrigger);
        const qtlOption = await screen.findByRole('option', { name: '4K Ultra HD' });
        await user.click(qtlOption);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                qualityTierListId: 'qtl-2',
            }),
        );
    });

    it('toggles monitored state', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={vi.fn()}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Wishlist');

        const monitoredSwitch = screen.getByLabelText(/monitored/i);
        await user.click(monitoredSwitch);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                monitored: false,
            }),
        );
    });
});

describe('WishlistForm unsaved changes', () => {
    it('shows unsaved changes dialog when closing with dirty form', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={onOpenChange}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Dirty Value');

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('closes the form when discarding unsaved changes', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={onOpenChange}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Dirty Value');

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        const discardButton = screen.getByRole('button', { name: /discard changes/i });
        await user.click(discardButton);

        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('keeps the form open when choosing to keep editing', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={onOpenChange}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Dirty Value');

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        const keepEditingButton = screen.getByRole('button', { name: /keep editing/i });
        await user.click(keepEditingButton);

        expect(onOpenChange).not.toHaveBeenCalled();
        await waitFor(() => {
            expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        });
    });

    it('closes without warning when form is not dirty', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={onOpenChange}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        expect(onOpenChange).toHaveBeenCalledWith(false);
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });

    it('does not show unsaved changes dialog in read-only mode', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <WishlistForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'wl-1',
                        name: 'Read Only Wishlist',
                        filterId: null,
                        qualityTierListId: null,
                        monitored: true,
                        tagIds: [],
                    }}
                    filters={mockFilters}
                    qualityTierLists={mockQualityTierLists}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    canEdit={false}
                />
            </TestWrapper>,
        );

        const closeButtons = screen.getAllByRole('button', { name: /close/i });
        await user.click(closeButtons[closeButtons.length - 1]);

        expect(onOpenChange).toHaveBeenCalledWith(false);
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });
});

describe('valuesEqual', () => {
    const base = {
        name: 'Test',
        filterId: null,
        qualityTierListId: null,
        monitored: true,
        tagIds: ['a', 'b', 'c'],
    };

    it('returns true when tagIds are identical', () => {
        expect(valuesEqual(base, { ...base })).toBe(true);
    });

    it('returns true when tagIds have the same values in different order', () => {
        expect(valuesEqual(base, { ...base, tagIds: ['c', 'a', 'b'] })).toBe(true);
    });

    it('returns false when tagIds differ', () => {
        expect(valuesEqual(base, { ...base, tagIds: ['a', 'b', 'd'] })).toBe(false);
    });

    it('returns false when tagIds have different lengths', () => {
        expect(valuesEqual(base, { ...base, tagIds: ['a', 'b'] })).toBe(false);
    });
});
