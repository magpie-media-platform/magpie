import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiTag } from '@/hooks/useTags';

import { TierListForm } from './TierListForm';

function TestWrapper({ children }: { children: ReactNode }) {
    return (
        <MemoryRouter>
            <TooltipProvider>{children}</TooltipProvider>
        </MemoryRouter>
    );
}

const mockTags: ApiTag[] = [{ name: 'Movies' }, { name: 'TV Shows' }, { name: 'Anime' }];

describe('TierListForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <TierListForm open={false} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Tier List')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Tier List')).toBeInTheDocument();
    });

    it('shows "Edit Tier List" when editing', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'HD Standard',
                        upgradeAllowed: true,
                        tags: [],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Tier List')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'HD Standard',
                        upgradeAllowed: true,
                        tags: ['Movies'],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('HD Standard')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders upgrade allowed toggle', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="upgradeAllowed"]')).toBeInTheDocument();
    });

    it('renders tags picker when tags are available', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Movies')).toBeInTheDocument();
        expect(screen.getByText('TV Shows')).toBeInTheDocument();
        expect(screen.getByText('Anime')).toBeInTheDocument();
    });

    it('does not render tags section when no tags available', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={[]} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Tags')).not.toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
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
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('clears validation error when user starts typing', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);
        expect(screen.getByText('Name is required')).toBeInTheDocument();

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'A');

        expect(screen.queryByText('Name is required')).not.toBeInTheDocument();
    });

    it('calls onSubmit with form values when valid', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, '4K Ultra HD');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: '4K Ultra HD',
                upgradeAllowed: true,
                tags: [],
            }),
        );
    });

    it('toggles upgrade allowed state', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'SD Only');

        const upgradeSwitch = document.querySelector('[data-field="upgradeAllowed"]') as HTMLButtonElement;
        await user.click(upgradeSwitch);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: 'SD Only',
                upgradeAllowed: false,
            }),
        );
    });

    it('toggles tags when clicked', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Tier List');

        const moviesTag = screen.getByText('Movies');
        await user.click(moviesTag);

        const animeTag = screen.getByText('Anime');
        await user.click(animeTag);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                tags: ['Movies', 'Anime'],
            }),
        );
    });

    it('removes tag when clicked again', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Tier List');

        const moviesTag = screen.getByText('Movies');
        await user.click(moviesTag);

        await user.click(moviesTag);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                tags: [],
            }),
        );
    });

    it('pre-selects tags when editing', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'HD Standard',
                        upgradeAllowed: true,
                        tags: ['Movies', 'Anime'],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const moviesTag = screen.getByText('Movies');
        expect(moviesTag).toHaveAttribute('aria-pressed', 'true');
        expect(moviesTag).toHaveAttribute('data-selected', 'true');

        const animeTag = screen.getByText('Anime');
        expect(animeTag).toHaveAttribute('aria-pressed', 'true');
        expect(animeTag).toHaveAttribute('data-selected', 'true');

        const tvShowsTag = screen.getByText('TV Shows');
        expect(tvShowsTag).toHaveAttribute('aria-pressed', 'false');
        expect(tvShowsTag).not.toHaveAttribute('data-selected');
    });

    it('shows loading state when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const upgradeSwitch = document.querySelector('[data-field="upgradeAllowed"]') as HTMLButtonElement;
        expect(upgradeSwitch).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();

        const moviesTag = screen.getByText('Movies');
        expect(moviesTag).toBeDisabled();
    });

    it('has data-component attribute on sheet content', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="tier-list-form"]')).toBeInTheDocument();
    });

    it('has data-field attributes on form fields', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="upgradeAllowed"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="tags"]')).toBeInTheDocument();
    });

    it('has data-action attributes on buttons', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
    });

    it('has data-tag-name attributes on tag buttons', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-tag-name="Movies"]')).toBeInTheDocument();
        expect(document.querySelector('[data-tag-name="TV Shows"]')).toBeInTheDocument();
        expect(document.querySelector('[data-tag-name="Anime"]')).toBeInTheDocument();
    });

    it('renders help text for upgrade allowed toggle', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/when disabled, content will not be replaced/i)).toBeInTheDocument();
    });

    it('renders help text for tags section', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/assign tags to organize and filter/i)).toBeInTheDocument();
    });

    it('renders description text for add mode', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/configure a new quality tier list/i)).toBeInTheDocument();
    });

    it('renders description text for edit mode', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'HD Standard',
                        upgradeAllowed: true,
                        tags: [],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText(/update the tier list configuration/i)).toBeInTheDocument();
    });

    it('resets form when opening with different initial values', async () => {
        const { rerender } = render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'First List',
                        upgradeAllowed: false,
                        tags: ['Movies'],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('First List')).toBeInTheDocument();

        rerender(
            <TestWrapper>
                <TierListForm
                    open={false}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-2',
                        name: 'Second List',
                        upgradeAllowed: true,
                        tags: [],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-2',
                        name: 'Second List',
                        upgradeAllowed: true,
                        tags: [],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Second List')).toBeInTheDocument();
    });

    it('resets to empty form when opening for add after edit', async () => {
        const { rerender } = render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'Existing List',
                        upgradeAllowed: false,
                        tags: ['Movies'],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Existing List')).toBeInTheDocument();

        rerender(
            <TestWrapper>
                <TierListForm
                    open={false}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={null}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i) as HTMLInputElement;
        expect(nameInput.value).toBe('');
    });
});

describe('TierListForm accessibility', () => {
    it('has correct aria-pressed attribute on tag buttons', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'Test',
                        upgradeAllowed: true,
                        tags: ['Movies'],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const selectedTag = screen.getByText('Movies');
        expect(selectedTag).toHaveAttribute('aria-pressed', 'true');

        const unselectedTag = screen.getByText('TV Shows');
        expect(unselectedTag).toHaveAttribute('aria-pressed', 'false');
    });

    it('has role="alert" on error message', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        const errorMessage = screen.getByRole('alert');
        expect(errorMessage).toHaveTextContent('Name is required');
    });

    it('has aria-label on info button', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const infoButton = screen.getByRole('button', { name: /upgrade allowed help/i });
        expect(infoButton).toBeInTheDocument();
    });
});

describe('TierListForm tier configuration hint', () => {
    it('shows tier configuration hint when adding new tier list', () => {
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-hint="tiers"]')).toBeInTheDocument();
        expect(screen.getByText(/quality tiers can be configured after/i)).toBeInTheDocument();
    });

    it('hides tier configuration hint when editing', () => {
        render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'qtl-1',
                        name: 'HD Standard',
                        upgradeAllowed: true,
                        tags: [],
                    }}
                    tags={mockTags}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-hint="tiers"]')).not.toBeInTheDocument();
    });
});
