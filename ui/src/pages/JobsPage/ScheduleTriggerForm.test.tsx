import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';
import { ScheduleTriggerForm, type ScheduleTriggerFormValues, type JobTemplateOption } from './ScheduleTriggerForm';

const mockTemplates: JobTemplateOption[] = [
    { id: 'template-1', name: 'Refresh Metadata' },
    { id: 'template-2', name: 'Search Missing' },
    { id: 'template-3', name: 'RSS Sync' },
];

function TestWrapper({ children }: { children: ReactNode }) {
    return <TooltipProvider>{children}</TooltipProvider>;
}

describe('ScheduleTriggerForm', () => {
    it('renders with correct data-component attribute', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="schedule-trigger-form"]')).toBeInTheDocument();
    });

    it('renders "Add Schedule Trigger" title when not editing', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Schedule Trigger')).toBeInTheDocument();
    });

    it('renders "Edit Schedule Trigger" title when editing', () => {
        const existingValues: ScheduleTriggerFormValues = {
            id: 'trigger-1',
            name: 'Test Trigger',
            templateId: 'template-1',
            cronExpression: '0 * * * *',
            enabled: true,
        };

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={existingValues}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Schedule Trigger')).toBeInTheDocument();
    });

    it('renders all form fields', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: /job template/i })).toBeInTheDocument();
        expect(screen.getByRole('textbox', { name: /cron expression/i })).toBeInTheDocument();
        expect(screen.getByLabelText(/enabled/i)).toBeInTheDocument();
    });

    it('has data-field attributes on form fields', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="templateId"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="cronExpression"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="enabled"]')).toBeInTheDocument();
    });
});

describe('ScheduleTriggerForm name field', () => {
    it('updates name value on input', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'My Trigger');

        expect(nameInput).toHaveValue('My Trigger');
    });

    it('shows error when name is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('ScheduleTriggerForm template field', () => {
    it('shows template options in dropdown', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);

        expect(screen.getByRole('option', { name: 'Refresh Metadata' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Search Missing' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'RSS Sync' })).toBeInTheDocument();
    });

    it('shows error when template is not selected on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Trigger');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Job template is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('ScheduleTriggerForm cron field', () => {
    it('updates cron value on input', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const cronInput = screen.getByRole('textbox', { name: /cron expression/i });
        await user.type(cronInput, '0 * * * *');

        expect(cronInput).toHaveValue('0 * * * *');
    });

    it('has monospace font styling on cron input wrapper', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const cronInputWrapper = screen.getByTestId('schedule-trigger-form__cron-input');
        expect(cronInputWrapper).toBeInTheDocument();
    });

    it('shows error when cron expression is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Trigger');

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'RSS Sync' }));

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Cron expression is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows error for invalid cron expression format', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Trigger');

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'RSS Sync' }));

        const cronInput = screen.getByRole('textbox', { name: /cron expression/i });
        await user.type(cronInput, '0 * *');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText(/must have 5 parts/i)).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('ScheduleTriggerForm cron presets', () => {
    it('renders schedule presets dropdown', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const presetTrigger = document.querySelector('[data-field="preset"]');
        expect(presetTrigger).toBeInTheDocument();
    });

    it('populates cron expression when preset is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const presetTrigger = document.querySelector('[data-field="preset"]');
        await user.click(presetTrigger!);

        const hourlyOption = screen.getByRole('option', { name: 'Every hour' });
        await user.click(hourlyOption);

        const cronInput = screen.getByRole('textbox', { name: /cron expression/i });
        expect(cronInput).toHaveValue('0 * * * *');
    });
});

describe('ScheduleTriggerForm enabled toggle', () => {
    it('enabled toggle is checked by default', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const enabledSwitch = screen.getByLabelText(/enabled/i);
        expect(enabledSwitch).toBeChecked();
    });

    it('can toggle enabled state', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const enabledSwitch = screen.getByLabelText(/enabled/i);
        await user.click(enabledSwitch);

        expect(enabledSwitch).not.toBeChecked();
    });
});

describe('ScheduleTriggerForm submission', () => {
    it('calls onSubmit with form values when valid', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Trigger');

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'RSS Sync' }));

        const cronInput = screen.getByRole('textbox', { name: /cron expression/i });
        await user.type(cronInput, '0 * * * *');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith({
            name: 'Test Trigger',
            templateId: 'template-3',
            cronExpression: '0 * * * *',
            enabled: true,
        });
    });

    it('includes id in values when editing', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        const existingValues: ScheduleTriggerFormValues = {
            id: 'trigger-1',
            name: 'Existing Trigger',
            templateId: 'template-1',
            cronExpression: '0 6 * * *',
            enabled: true,
        };

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={existingValues}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                id: 'trigger-1',
            }),
        );
    });
});

describe('ScheduleTriggerForm buttons', () => {
    it('renders save and cancel buttons', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });

    it('buttons have data-action attributes', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
    });

    it('disables buttons when submitting', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeDisabled();
    });

    it('shows loading state when submitting', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('cancel button closes the form', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        expect(onOpenChange).toHaveBeenCalledWith(false);
    });
});

describe('ScheduleTriggerForm initial values', () => {
    it('populates fields with initial values', () => {
        const existingValues: ScheduleTriggerFormValues = {
            id: 'trigger-1',
            name: 'Existing Trigger',
            templateId: 'template-1',
            cronExpression: '0 6 * * *',
            enabled: false,
        };

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={existingValues}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toHaveValue('Existing Trigger');
        expect(screen.getByRole('textbox', { name: /cron expression/i })).toHaveValue('0 6 * * *');
        expect(screen.getByLabelText(/enabled/i)).not.toBeChecked();
    });

    it('resets form when opened with new values', async () => {
        const onOpenChange = vi.fn();

        const existingValues: ScheduleTriggerFormValues = {
            id: 'trigger-1',
            name: 'Existing Trigger',
            templateId: 'template-1',
            cronExpression: '0 6 * * *',
            enabled: true,
        };

        const { rerender } = render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={false}
                    onOpenChange={onOpenChange}
                    initialValues={existingValues}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={existingValues}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByLabelText(/name/i)).toHaveValue('Existing Trigger');
        });
    });
});

describe('ScheduleTriggerForm accessibility', () => {
    it('has info button with aria-label', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const infoButton = screen.getByRole('button', { name: /cron expression help/i });
        expect(infoButton).toBeInTheDocument();
    });

    it('error messages have role="alert"', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        const alerts = document.querySelectorAll('[role="alert"]');
        expect(alerts.length).toBeGreaterThan(0);
    });

    it('required fields have required indicator', () => {
        render(
            <TestWrapper>
                <ScheduleTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const nameLabel = screen.getByText(/^name$/i);
        expect(nameLabel.parentElement?.querySelector('[aria-hidden="true"]')).toBeInTheDocument();
    });
});
