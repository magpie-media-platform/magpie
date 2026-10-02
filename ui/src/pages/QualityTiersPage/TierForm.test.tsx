import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { TierForm } from './TierForm';

const mockUseLibraryTypes = vi.fn();

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => mockUseLibraryTypes(),
}));

const defaultLibraryTypes = {
    contentTypes: [] as {
        id: string;
        name: string;
        pluralName: string;
        slug: string;
        iconName: null;
        levels: never[];
    }[],
    subjectTypes: [],
    isLoading: false,
    error: null,
    refetch: vi.fn(),
};

beforeEach(() => {
    vi.restoreAllMocks();
    vi.clearAllMocks();
    mockUseLibraryTypes.mockReturnValue(defaultLibraryTypes);
});

function TestWrapper({ children }: { children: ReactNode }) {
    return (
        <MemoryRouter>
            <TooltipProvider>{children}</TooltipProvider>
        </MemoryRouter>
    );
}

describe('TierForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <TierForm open={false} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Tier')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Tier')).toBeInTheDocument();
    });

    it('shows "Edit Tier" when editing', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: true,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Tier')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: true,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Bluray-1080p')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders cutoff toggle', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="isCutoff"]')).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
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
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
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
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
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
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Bluray-1080p');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);
        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: 'Bluray-1080p',
                isCutoff: false,
                criteria: [],
            }),
        );
    });

    it('toggles cutoff state', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Bluray-1080p');

        const cutoffSwitch = document.querySelector('[data-field="isCutoff"]') as HTMLButtonElement;
        await user.click(cutoffSwitch);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                name: 'Bluray-1080p',
                isCutoff: true,
                criteria: [],
            }),
        );
    });

    it('shows loading state when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const cutoffSwitch = document.querySelector('[data-field="isCutoff"]') as HTMLButtonElement;
        expect(cutoffSwitch).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });

    it('has data-component attribute on sheet content', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="tier-form"]')).toBeInTheDocument();
    });

    it('has data-field attributes on form fields', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="isCutoff"]')).toBeInTheDocument();
    });

    it('has data-action attributes on buttons', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
    });

    it('renders help text for name field', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/a descriptive name for this quality tier/i)).toBeInTheDocument();
    });

    it('renders help text for cutoff toggle', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/when set as cutoff, content meeting this tier/i)).toBeInTheDocument();
    });

    it('renders description text for add mode', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/add a new tier/i)).toBeInTheDocument();
    });

    it('renders description text for edit mode', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: false,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText(/update the tier configuration/i)).toBeInTheDocument();
    });

    it('includes tier list name in description when provided', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} tierListName="HD Standard" onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/add a new tier to "HD Standard"/i)).toBeInTheDocument();
    });

    it('includes tier list name in edit mode description', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    tierListName="HD Standard"
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: false,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText(/update the tier configuration in "HD Standard"/i)).toBeInTheDocument();
    });

    it('resets form when opening with different initial values', async () => {
        const { rerender } = render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'First Tier',
                        isCutoff: true,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('First Tier')).toBeInTheDocument();

        rerender(
            <TestWrapper>
                <TierForm
                    open={false}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-2',
                        name: 'Second Tier',
                        isCutoff: false,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-2',
                        name: 'Second Tier',
                        isCutoff: false,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Second Tier')).toBeInTheDocument();
    });

    it('resets to empty form when opening for add after edit', async () => {
        const { rerender } = render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'Existing Tier',
                        isCutoff: true,
                        criteria: [],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Existing Tier')).toBeInTheDocument();

        rerender(
            <TestWrapper>
                <TierForm open={false} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i) as HTMLInputElement;
        expect(nameInput.value).toBe('');
    });

    it('renders criteria editor', () => {
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="criteria-editor"]')).toBeInTheDocument();
    });

    it('pre-fills criteria when editing', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: true,
                        criteria: [{ id: 'c1', field: 'source', operator: 'Equals', value: 'Bluray' }],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-criterion-id="c1"]')).toBeInTheDocument();
    });

    it('initializes a stored criterion field outside the current vocabulary', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: true,
                        criteria: [{ id: 'c1', field: 'source', operator: 'Equals', value: 'Bluray' }],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const field = screen.getByLabelText(/criterion 1 field/i);
        expect(field).toHaveValue('source');
    });
});

describe('TierForm dirty-on-open guard', () => {
    const populatedTier = {
        id: 't1',
        name: 'Bluray-1080p',
        isCutoff: true,
        criteria: [{ id: 'c1', field: 'source', operator: 'Equals' as const, value: 'Bluray' }],
        exclusions: [
            {
                id: 'r1',
                name: 'Block proper',
                criteria: [{ id: 'rc1', field: 'group', operator: 'Equals' as const, value: 'EVO' }],
            },
        ],
        sourceRules: [
            {
                id: 'r1',
                name: 'Block proper',
                kind: 'Exclude' as const,
                criteria: { and: [{ items: { group: { operator: 'Equals' as const, value: 'EVO' } } }] },
            },
        ],
    };

    function trackBeforeUnload() {
        const state = { armed: false };
        const original = window.addEventListener.bind(window);
        vi.spyOn(window, 'addEventListener').mockImplementation((type, ...rest) => {
            if (type === 'beforeunload') {
                state.armed = true;
            }
            return original(type, ...(rest as [EventListenerOrEventListenerObject]));
        });
        return state;
    }

    it('does not arm the unsaved-changes guard when opening an untouched tier', () => {
        const guard = trackBeforeUnload();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} initialValues={populatedTier} />
            </TestWrapper>,
        );

        expect(guard.armed).toBe(false);
    });

    it('closes without warning on cancel of an untouched existing tier', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} initialValues={populatedTier} />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('arms the guard after a real edit to an existing tier', async () => {
        const user = userEvent.setup();
        const guard = trackBeforeUnload();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} initialValues={populatedTier} />
            </TestWrapper>,
        );

        guard.armed = false;
        await user.type(document.querySelector('[data-field="name"]') as HTMLElement, '!');

        expect(guard.armed).toBe(true);
    });
});

describe('TierForm dirty detection', () => {
    it('does not warn on cancel for a loaded stored criterion field outside the current vocabulary', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 't1-1',
                        name: 'Bluray-1080p',
                        isCutoff: true,
                        criteria: [{ id: 'c1', field: 'source', operator: 'Equals', value: 'Bluray' }],
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('round-trips a changed criterion field through submit and reopen', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const initialValues = {
            id: 't1-1',
            name: 'Bluray-1080p',
            isCutoff: true,
            criteria: [{ id: 'c1', field: 'source', operator: 'Equals' as const, value: 'Bluray' }],
        };

        const { rerender } = render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} initialValues={initialValues} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        fireEvent.change(screen.getByLabelText(/criterion 1 field/i), { target: { value: 'Size' } });
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                criteria: [expect.objectContaining({ id: 'c1', field: 'Size' })],
            }),
        );

        const savedValues = onSubmit.mock.calls[0][0];

        rerender(
            <TestWrapper>
                <TierForm open={false} onOpenChange={vi.fn()} initialValues={savedValues} onSubmit={onSubmit} />
            </TestWrapper>,
        );
        rerender(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} initialValues={savedValues} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toHaveValue('Size');
    });

    it('refuses to save a criterion with no field name', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'Bluray-1080p');
        await user.click(document.querySelector('[data-action="add-criterion"]') as HTMLElement);
        await user.type(screen.getByLabelText(/criterion 1 value/i), 'Bluray');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).not.toHaveBeenCalled();
        expect(screen.getByText('Every criterion needs a field name')).toBeInTheDocument();
    });

    it('refuses to save an exclusion criterion with no field name', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'Bluray-1080p');
        await user.click(document.querySelector('[data-action="add-exclusion"]') as HTMLElement);
        await user.click(document.querySelectorAll('[data-action="add-criterion"]')[1] as HTMLElement);
        await user.type(screen.getByLabelText(/criterion 1 value/i), 'CAM');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).not.toHaveBeenCalled();
        expect(screen.getByText('Every exclusion criterion needs a field name')).toBeInTheDocument();
    });

    it('does not warn on cancel after inserting a blank criterion row', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-action="add-criterion"]') as HTMLElement);
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('does not warn on cancel after inserting a blank exclusion row', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /add exclusion/i }));
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('warns on cancel after a real field edit', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'Bluray-1080p');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('warns on cancel once a criterion value is filled in', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        await user.click(document.querySelector('[data-action="add-criterion"]') as HTMLElement);
        await user.type(screen.getByLabelText(/criterion 1 value/i), 'Bluray');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });
});

describe('TierForm accessibility', () => {
    it('has role="alert" on error message', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
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
                <TierForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const infoButton = screen.getByRole('button', { name: /cutoff help/i });
        expect(infoButton).toBeInTheDocument();
    });
});

describe('TierForm shared tier', () => {
    const sharedTier = {
        id: 't1-1',
        name: 'Bluray-1080p',
        isCutoff: false,
        criteria: [{ id: 'c1', field: 'source', operator: 'Equals' as const, value: 'Bluray' }],
    };

    it('names the other lists that use the tier', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Update the tier configuration in "HD Copy" and 1 other list.')).toBeInTheDocument();
        expect(screen.getByTestId('shared-tier-notice')).toHaveTextContent(
            'Name, criteria and exclusions also change in "HD Standard". The cutoff applies only to "HD Copy". ' +
                'Clone Tier adds a copy that only this list uses and keeps the shared tier until you remove it.',
        );
    });

    it('shows no shared notice when no other list uses the tier', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={[]}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Update the tier configuration in "HD Copy".')).toBeInTheDocument();
        expect(screen.queryByTestId('shared-tier-notice')).not.toBeInTheDocument();
    });

    it('asks before saving a name change to a shared tier and saves nothing on keep editing', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), '!');
        await user.click(screen.getByRole('button', { name: 'Save' }));

        const dialog = screen.getByRole('dialog', { name: 'Save changes to 2 lists?' });
        expect(dialog).toHaveTextContent(
            'Name, criteria and exclusions change in every list that uses this tier: "HD Copy", "HD Standard".',
        );
        await user.click(within(dialog).getByRole('button', { name: 'Keep editing' }));

        expect(onSubmit).not.toHaveBeenCalled();
        expect(screen.getByLabelText(/name/i)).toHaveValue('Bluray-1080p!');
    });

    it('saves a shared tier change after the user confirms it', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), '!');
        await user.click(screen.getByRole('button', { name: 'Save' }));
        await user.click(screen.getByRole('button', { name: 'Save anyway' }));

        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ name: 'Bluray-1080p!' }));
    });

    it('saves a cutoff-only change to a shared tier without asking', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('switch', { name: 'Set as Cutoff' }));
        await user.click(screen.getByRole('button', { name: 'Save' }));

        expect(screen.queryByRole('dialog', { name: /save changes to/i })).not.toBeInTheDocument();
        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ isCutoff: true }));
    });

    it('disables save and names no list scope while the lists that use the tier are unknown', () => {
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={null}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
        expect(screen.getByText('Update the tier configuration.')).toBeInTheDocument();
    });

    it('shows the load error and keeps save disabled when the lists that use the tier fail to load', async () => {
        const user = userEvent.setup();
        const onRetrySharedWith = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={null}
                    sharedWithFailed
                    onRetrySharedWith={onRetrySharedWith}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByTestId('shared-tier-error')).toHaveTextContent(
            'Failed to load the tier listsThe lists that use this tier are unknown.',
        );
        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
        await user.click(screen.getByRole('button', { name: 'Retry' }));

        expect(onRetrySharedWith).toHaveBeenCalledTimes(1);
    });

    it('clones the tier from the shared notice', async () => {
        const user = userEvent.setup();
        const onClone = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onClone={onClone}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: 'Clone Tier' }));

        expect(onClone).toHaveBeenCalledTimes(1);
    });

    it('asks to discard unsaved edits before cloning from the shared notice', async () => {
        const user = userEvent.setup();
        const onClone = vi.fn();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onClone={onClone}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), '!');
        await user.click(screen.getByRole('button', { name: 'Clone Tier' }));

        const dialog = screen.getByRole('dialog', { name: 'Discard Changes and Clone?' });
        expect(dialog).toHaveTextContent('Clone Tier copies the saved tier. It does not copy your unsaved changes.');
        expect(onClone).not.toHaveBeenCalled();
        await user.click(within(dialog).getByRole('button', { name: 'Discard and Clone' }));

        expect(onClone).toHaveBeenCalledTimes(1);
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('keeps the unsaved edits when the user backs out of cloning', async () => {
        const user = userEvent.setup();
        const onClone = vi.fn();
        render(
            <TestWrapper>
                <TierForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={sharedTier}
                    tierListName="HD Copy"
                    sharedWith={['HD Standard']}
                    onClone={onClone}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), '!');
        await user.click(screen.getByRole('button', { name: 'Clone Tier' }));
        await user.click(screen.getByRole('button', { name: 'Keep Editing' }));

        expect(onClone).not.toHaveBeenCalled();
        expect(screen.getByLabelText(/name/i)).toHaveValue('Bluray-1080p!');
    });
});
