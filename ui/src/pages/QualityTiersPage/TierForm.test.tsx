import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { TierForm } from './TierForm';

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
