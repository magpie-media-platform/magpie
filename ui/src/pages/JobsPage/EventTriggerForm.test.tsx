import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

import { EventTriggerForm, type EventTriggerFormValues, type JobTemplateOption } from './EventTriggerForm';

function TestWrapper({ children }: { children: ReactNode }) {
    return (
        <TooltipProvider>
            <ToastProvider>
                <ToastStateProvider>
                    {children}
                    <ToastViewport />
                </ToastStateProvider>
            </ToastProvider>
        </TooltipProvider>
    );
}

const mockTemplates: JobTemplateOption[] = [
    { id: 'template-1', name: 'Refresh Metadata' },
    { id: 'template-2', name: 'Search Missing' },
    { id: 'template-3', name: 'RSS Sync' },
];

describe('EventTriggerForm', () => {
    it('renders add form title when no initialValues', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Event Trigger')).toBeInTheDocument();
    });

    it('renders edit form title when initialValues has id', () => {
        const initialValues: EventTriggerFormValues = {
            id: 'trigger-1',
            name: 'Test Trigger',
            templateId: 'template-1',
            eventNamePattern: 'content\\.added',
            filters: [],
            executionMode: 'queue',
            enabled: true,
        };

        render(
            <TestWrapper>
                <EventTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={initialValues}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Event Trigger')).toBeInTheDocument();
    });

    it('renders all form fields', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="templateId"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="preset"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="eventNamePattern"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="executionMode"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="enabled"]')).toBeInTheDocument();
    });

    it('renders data-component attribute', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="event-trigger-form"]')).toBeInTheDocument();
    });

    it('has data-field attributes on form inputs', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="templateId"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="eventNamePattern"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="executionMode"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="enabled"]')).toBeInTheDocument();
    });
});

describe('EventTriggerForm - Name input', () => {
    it('allows typing in name field', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const nameInput = document.querySelector<HTMLInputElement>('[data-field="name"]')!;
        await user.type(nameInput, 'My Event Trigger');

        expect(nameInput).toHaveValue('My Event Trigger');
    });

    it('shows validation error when name is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByLabelText(/job template/i);
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Refresh Metadata' }));

        const patternInput = screen.getByLabelText(/pattern.*regex/i);
        await user.type(patternInput, 'content\\.added');

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('EventTriggerForm - Template selector', () => {
    it('shows template options in dropdown', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByLabelText(/job template/i);
        await user.click(templateTrigger);

        expect(screen.getByRole('option', { name: 'Refresh Metadata' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Search Missing' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'RSS Sync' })).toBeInTheDocument();
    });

    it('selects template when clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByLabelText(/job template/i);
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'RSS Sync' }));

        expect(templateTrigger).toHaveTextContent('RSS Sync');
    });
});

describe('EventTriggerForm - Event pattern', () => {
    it('shows preset options in dropdown', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const presetTrigger = document.querySelector<HTMLElement>('[data-field="preset"]')!;
        await user.click(presetTrigger);

        expect(screen.getByRole('option', { name: 'All events' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Content added' })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: 'Download completed' })).toBeInTheDocument();
    });

    it('updates pattern input when preset is selected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const presetTrigger = document.querySelector<HTMLElement>('[data-field="preset"]')!;
        await user.click(presetTrigger);
        await user.click(screen.getByRole('option', { name: 'Download completed' }));

        const patternInput = document.querySelector<HTMLInputElement>('[data-field="eventNamePattern"]')!;
        expect(patternInput).toHaveValue('download\\.completed');
    });

    it('shows validation error for invalid regex pattern', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = document.querySelector<HTMLInputElement>('[data-field="name"]')!;
        await user.type(nameInput, 'Test Trigger');

        const templateTrigger = document.querySelector<HTMLElement>('[data-field="templateId"]')!;
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Refresh Metadata' }));

        const patternInput = document.querySelector<HTMLInputElement>('[data-field="eventNamePattern"]')!;
        await user.type(patternInput, '{[}invalid(regex');

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(screen.getByText('Invalid regex pattern')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('EventTriggerForm - Execution mode', () => {
    it('defaults to Queue mode', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const modeTrigger = screen.getByLabelText(/execution mode/i);
        expect(modeTrigger).toHaveTextContent('Queue');
    });

    it('allows selecting Immediate mode', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const modeTrigger = screen.getByLabelText(/execution mode/i);
        await user.click(modeTrigger);
        await user.click(screen.getByRole('option', { name: 'Immediate' }));

        expect(modeTrigger).toHaveTextContent('Immediate');
    });
});

describe('EventTriggerForm - Filters', () => {
    it('shows empty filters message initially', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(screen.getByText(/no filters configured/i)).toBeInTheDocument();
    });

    it('adds filter when Add Filter button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const addButton = screen.getByRole('button', { name: /add filter/i });
        await user.click(addButton);

        expect(screen.getByLabelText(/filter 1 key/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/filter 1 pattern/i)).toBeInTheDocument();
        expect(screen.queryByText(/no filters configured/i)).not.toBeInTheDocument();
    });

    it('removes filter when remove button is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /add filter/i }));
        expect(screen.getByLabelText(/filter 1 key/i)).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /remove filter 1/i }));

        expect(screen.getByText(/no filters configured/i)).toBeInTheDocument();
    });

    it('shows validation error for empty filter key', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector<HTMLInputElement>('[data-field="name"]')!, 'Test Trigger');

        const templateTrigger = document.querySelector<HTMLElement>('[data-field="templateId"]')!;
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Refresh Metadata' }));

        await user.type(
            document.querySelector<HTMLInputElement>('[data-field="eventNamePattern"]')!,
            'content\\.added',
        );

        await user.click(screen.getByRole('button', { name: /add filter/i }));

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(screen.getByText(/key is required/i)).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('EventTriggerForm - Enabled toggle', () => {
    it('defaults to enabled', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const enabledSwitch = screen.getByLabelText(/^enabled$/i);
        expect(enabledSwitch).toHaveAttribute('data-state', 'checked');
    });

    it('can be toggled off', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const enabledSwitch = screen.getByLabelText(/^enabled$/i);
        await user.click(enabledSwitch);

        expect(enabledSwitch).toHaveAttribute('data-state', 'unchecked');
    });
});

describe('EventTriggerForm - Form submission', () => {
    it('calls onSubmit with correct values', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector<HTMLInputElement>('[data-field="name"]')!, 'My Event Trigger');

        const templateTrigger = document.querySelector<HTMLElement>('[data-field="templateId"]')!;
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'RSS Sync' }));

        const presetTrigger = document.querySelector<HTMLElement>('[data-field="preset"]')!;
        await user.click(presetTrigger);
        await user.click(screen.getByRole('option', { name: 'Download completed' }));

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith({
            name: 'My Event Trigger',
            templateId: 'template-3',
            eventNamePattern: 'download\\.completed',
            filters: [],
            executionMode: 'queue',
            enabled: true,
        });
    });

    it('disables inputs when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <EventTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeDisabled();
        expect(document.querySelector('[data-field="eventNamePattern"]')).toBeDisabled();
    });

    it('shows loading state when isSubmitting', () => {
        render(
            <TestWrapper>
                <EventTriggerForm
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
});

describe('EventTriggerForm - Cancel button', () => {
    it('renders cancel button', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });

    it('has data-action attribute', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
    });
});

describe('EventTriggerForm - Initial values', () => {
    it('populates form with initial values', () => {
        const initialValues: EventTriggerFormValues = {
            id: 'trigger-1',
            name: 'Existing Trigger',
            templateId: 'template-2',
            eventNamePattern: 'file\\.imported',
            filters: [{ key: 'type', pattern: 'movie', negate: false }],
            executionMode: 'immediate',
            enabled: false,
        };

        render(
            <TestWrapper>
                <EventTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={initialValues}
                    templates={mockTemplates}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toHaveValue('Existing Trigger');
        expect(document.querySelector('[data-field="templateId"]')).toHaveTextContent('Search Missing');
        expect(document.querySelector('[data-field="eventNamePattern"]')).toHaveValue('file\\.imported');
        expect(document.querySelector('[data-field="executionMode"]')).toHaveTextContent('Immediate');
        expect(document.querySelector('[data-field="enabled"]')).toHaveAttribute('data-state', 'unchecked');

        expect(screen.getByLabelText(/filter 1 key/i)).toHaveValue('type');
        expect(screen.getByLabelText(/filter 1 pattern/i)).toHaveValue('movie');
    });
});

describe('EventTriggerForm - accessibility', () => {
    it('has required indicators on required fields', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const nameLabel = document.querySelector('label[for="event-trigger-name"]');
        const requiredIndicator = nameLabel?.querySelector('.label__required');
        expect(requiredIndicator).toBeInTheDocument();
    });

    it('shows error messages with role="alert"', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /save/i }));

        const alerts = screen.getAllByRole('alert');
        expect(alerts.length).toBeGreaterThan(0);
    });

    it('info buttons have aria-labels', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /event pattern help/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /filters help/i })).toBeInTheDocument();
    });
});
