import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
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

beforeEach(() => {
    vi.restoreAllMocks();
});

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
                        mimeTypes: ['video/*'],
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
                        mimeTypes: ['video/*'],
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
                mimeTypes: [],
                upgradeAllowed: true,
                tags: [],
            }),
        );
    });

    it('normalizes MIME types on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/^Name/), 'Audio');
        await user.type(screen.getByRole('combobox', { name: 'MIME Types' }), 'AUDIO/*{Enter}');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ mimeTypes: ['audio/*'] }));
    });

    it('rejects invalid MIME types with a field-level error', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/^Name/), 'Invalid');
        const input = screen.getByRole('combobox', { name: 'MIME Types' });
        await user.type(input, 'audio{Enter}');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(screen.getByRole('alert')).toHaveTextContent('audio is not a valid MIME pattern.');
        expect(input).toHaveAttribute('aria-invalid', 'true');
        expect(onSubmit).not.toHaveBeenCalled();
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
                        mimeTypes: ['video/*'],
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
        expect(document.querySelector('[data-field="mimeTypes"]')).toBeInTheDocument();
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

        expect(screen.getByText(/configure a new tier list/i)).toBeInTheDocument();
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
                        mimeTypes: ['video/*'],
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
                        mimeTypes: [],
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
                        mimeTypes: ['audio/*'],
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
                        mimeTypes: ['audio/*'],
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
                        mimeTypes: ['video/*'],
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
                        mimeTypes: [],
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
                        mimeTypes: ['video/*'],
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

describe('TierListForm unsaved-changes guard', () => {
    function trackGuard() {
        const state = { active: 0 };
        const originalAdd = window.addEventListener.bind(window);
        const originalRemove = window.removeEventListener.bind(window);
        vi.spyOn(window, 'addEventListener').mockImplementation((type, ...rest) => {
            if (type === 'beforeunload') {
                state.active += 1;
            }
            return originalAdd(type, ...(rest as [EventListenerOrEventListenerObject]));
        });
        vi.spyOn(window, 'removeEventListener').mockImplementation((type, ...rest) => {
            if (type === 'beforeunload') {
                state.active -= 1;
            }
            return originalRemove(type, ...(rest as [EventListenerOrEventListenerObject]));
        });
        return state;
    }

    it('does not arm the guard when opening an untouched add form', () => {
        const guard = trackGuard();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(guard.active).toBe(0);
    });

    it('arms the guard after a real edit', async () => {
        const user = userEvent.setup();
        const guard = trackGuard();
        render(
            <TestWrapper>
                <TierListForm open={true} onOpenChange={vi.fn()} tags={mockTags} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'HD Standard');

        expect(guard.active).toBeGreaterThan(0);
    });

    it('disarms the guard while a save is in flight, before navigation', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const guard = trackGuard();
        const { rerender } = render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={onSubmit}
                    isSubmitting={false}
                />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'HD Standard');
        expect(guard.active).toBeGreaterThan(0);

        await user.click(screen.getByRole('button', { name: /save/i }));
        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ name: 'HD Standard' }));

        // the parent flips isSubmitting while the create is in flight, so the guard
        // disarms before the parent navigates on success
        rerender(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={onSubmit}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );
        expect(guard.active).toBe(0);
    });

    it('keeps the guard armed when a save fails', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const guard = trackGuard();
        const { rerender } = render(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={onSubmit}
                    isSubmitting={false}
                />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'HD Standard');
        await user.click(screen.getByRole('button', { name: /save/i }));

        // save in flight
        rerender(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={onSubmit}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );
        // save failed: the parent resets isSubmitting, the form stays open with unsaved
        // data, so the guard must re-arm
        rerender(
            <TestWrapper>
                <TierListForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tags={mockTags}
                    onSubmit={onSubmit}
                    isSubmitting={false}
                />
            </TestWrapper>,
        );
        expect(guard.active).toBeGreaterThan(0);
    });
});
