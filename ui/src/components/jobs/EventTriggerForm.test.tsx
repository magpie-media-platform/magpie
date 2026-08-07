import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

import { EventTriggerForm, type EventTriggerFormValues, type JobTemplateOption } from './EventTriggerForm';
import { eventPatternExample, eventPatternPresets } from './eventPatternPresets';

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
    { id: 'template-1', name: 'Content metadata refresh item' },
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

    it('surfaces a passive advanced-feature caution in the modal', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const caution = screen.getByTestId('event-trigger-form__caution');
        expect(caution).toHaveTextContent(/advanced feature/i);
        expect(caution).toHaveTextContent(/caution/i);
    });

    it('renders edit form title when initialValues has id', () => {
        const initialValues: EventTriggerFormValues = {
            id: 'trigger-1',
            name: 'Test Trigger',
            templateId: 'template-1',
            eventNamePatterns: ['content\\.added'],
            filters: [],
            enabled: true,
            delaySeconds: 0,
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
        expect(document.querySelector('[data-field="delaySeconds"]')).toBeInTheDocument();
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
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

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

        expect(screen.getByRole('option', { name: 'Content metadata refresh item' })).toBeInTheDocument();
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

    it('allows selecting an event-only template when on-demand is disabled', async () => {
        const user = userEvent.setup();
        const templates: JobTemplateOption[] = [
            {
                id: 'MediaInfoExtractItem',
                name: 'Media info extract item',
                enabled: true,
                onDemand: false,
                supportedTriggerModes: ['Event'],
            },
        ];

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={templates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByLabelText(/job template/i);
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Media info extract item' }));

        expect(templateTrigger).toHaveTextContent('Media info extract item');
    });

    it('marks templates unavailable when they do not support event triggers', async () => {
        const user = userEvent.setup();
        const templates: JobTemplateOption[] = [
            {
                id: 'manual-only',
                name: 'Manual only',
                enabled: true,
                onDemand: true,
                supportedTriggerModes: ['Manual'],
            },
        ];

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={templates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        const templateTrigger = screen.getByLabelText(/job template/i);
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Manual only (unavailable)' }));

        expect(templateTrigger).toHaveTextContent('Select a job template...');
    });

    it('locks template selection when editing', () => {
        const initialValues: EventTriggerFormValues = {
            id: 'trigger-1',
            name: 'Existing Trigger',
            templateId: 'template-2',
            eventNamePatterns: ['download\\.completed'],
            filters: [],
            enabled: true,
            delaySeconds: 0,
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

        const templateTrigger = screen.getByRole('combobox', { name: /job template/i });
        expect(templateTrigger).toBeDisabled();
        expect(templateTrigger).toHaveTextContent('Search Missing');
        expect(
            screen.getByText('Job template cannot be changed after the event trigger is created.'),
        ).toBeInTheDocument();
    });
});

describe('EventTriggerForm - Event pattern', () => {
    it('maps every preset to a canonical published event name', () => {
        expect(eventPatternPresets).toEqual([
            { label: 'All events', value: '.*' },
            { label: 'Content added', value: 'content\\.hierarchyItem\\.created' },
            { label: 'Content updated', value: 'content\\.hierarchyItem\\.updated' },
            { label: 'Content removed', value: 'content\\.hierarchyItem\\.deleted' },
            { label: 'Download completed', value: 'download\\.completed' },
            { label: 'Download failed', value: 'download\\.failed' },
            { label: 'File imported', value: 'download\\.imported' },
            { label: 'Metadata refreshed', value: 'metadata\\.refreshed' },
        ]);
    });

    it('uses a canonical published event name for the placeholder and help example', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(eventPatternExample).toEqual({
            eventName: 'content.hierarchyItem.created',
            pattern: 'content\\.hierarchyItem\\.created',
        });
        expect(document.querySelector('[data-field="eventNamePattern"]')).toHaveAttribute(
            'placeholder',
            'content\\.hierarchyItem\\.created',
        );

        await user.hover(screen.getByRole('button', { name: 'Event pattern help' }));

        expect(await screen.findByRole('tooltip')).toHaveTextContent(
            'Example: content\\.hierarchyItem\\.created (matches content.hierarchyItem.created)',
        );
    });

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
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

        const patternInput = document.querySelector<HTMLInputElement>('[data-field="eventNamePattern"]')!;
        await user.type(patternInput, '{[}invalid(regex');

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(screen.getByText('Invalid regex pattern')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
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
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

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
            eventNamePatterns: ['download\\.completed'],
            filters: [],
            enabled: true,
            delaySeconds: 0,
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
        expect(saveButton).toHaveAttribute('data-loading', 'true');
        expect(saveButton).toBeDisabled();
        expect(saveButton.querySelector('.button__spinner')).toBeInTheDocument();
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
            eventNamePatterns: ['file\\.imported'],
            filters: [{ key: 'type', pattern: 'movie', negate: false }],
            enabled: false,
            delaySeconds: 30,
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
        expect(document.querySelector('[data-field="delaySeconds"]')).toHaveValue(30);
        expect(document.querySelector('[data-field="enabled"]')).toHaveAttribute('data-state', 'unchecked');

        expect(screen.getByLabelText(/filter 1 key/i)).toHaveValue('type');
        expect(screen.getByLabelText(/filter 1 pattern/i)).toHaveValue('movie');
    });

    it('renders every pattern for a trigger with multiple event names', () => {
        const initialValues: EventTriggerFormValues = {
            id: 'trigger-2',
            name: 'Multi Pattern Trigger',
            templateId: 'template-2',
            eventNamePatterns: ['download.imported', 'download.failed', 'download.completed'],
            filters: [],
            enabled: true,
            delaySeconds: 0,
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

        expect(document.querySelector('[data-field="eventNamePattern"]')).toHaveValue('download.imported');
        expect(document.querySelector('[data-field="eventNamePattern-1"]')).toHaveValue('download.failed');
        expect(document.querySelector('[data-field="eventNamePattern-2"]')).toHaveValue('download.completed');
    });
});

describe('EventTriggerForm - Delay', () => {
    it('defaults delay to 0', () => {
        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={() => {}} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="delaySeconds"]')).toHaveValue(0);
    });

    it('allows setting a delay and includes it in the submitted values', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector<HTMLInputElement>('[data-field="name"]')!, 'Delayed Trigger');

        const templateTrigger = document.querySelector<HTMLElement>('[data-field="templateId"]')!;
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

        await user.type(
            document.querySelector<HTMLInputElement>('[data-field="eventNamePattern"]')!,
            'content\\.added',
        );

        const delayInput = document.querySelector<HTMLInputElement>('[data-field="delaySeconds"]')!;
        await user.clear(delayInput);
        await user.type(delayInput, '45');

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ delaySeconds: 45 }));
    });
});

describe('EventTriggerForm - Multi-pattern editing', () => {
    it('adds and removes event name patterns', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm open={true} onOpenChange={() => {}} templates={mockTemplates} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        await user.type(document.querySelector<HTMLInputElement>('[data-field="name"]')!, 'Multi Trigger');

        const templateTrigger = document.querySelector<HTMLElement>('[data-field="templateId"]')!;
        await user.click(templateTrigger);
        await user.click(screen.getByRole('option', { name: 'Content metadata refresh item' }));

        await user.type(document.querySelector<HTMLInputElement>('[data-field="eventNamePattern"]')!, 'content.added');

        await user.click(screen.getByRole('button', { name: /add event name/i }));
        await user.type(
            document.querySelector<HTMLInputElement>('[data-field="eventNamePattern-1"]')!,
            'content.updated',
        );

        await user.click(screen.getByRole('button', { name: /add event name/i }));
        await user.type(
            document.querySelector<HTMLInputElement>('[data-field="eventNamePattern-2"]')!,
            'content.removed',
        );

        await user.click(screen.getByRole('button', { name: /remove event name 2/i }));

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({
                eventNamePatterns: ['content.added', 'content.removed'],
            }),
        );
    });

    it('round-trips a multi-pattern trigger through edit without data loss', () => {
        const initialValues: EventTriggerFormValues = {
            id: 'trigger-3',
            name: 'Round Trip Trigger',
            templateId: 'template-2',
            eventNamePatterns: ['event.one', 'event.two'],
            filters: [],
            enabled: true,
            delaySeconds: 10,
        };
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <EventTriggerForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={initialValues}
                    templates={mockTemplates}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="eventNamePattern"]')).toHaveValue('event.one');
        expect(document.querySelector('[data-field="eventNamePattern-1"]')).toHaveValue('event.two');
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

describe('EventTriggerForm dirty detection', () => {
    it('does not warn on cancel after inserting a blank filter row', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <EventTriggerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    templates={mockTemplates}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /add filter/i }));
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('warns on cancel after a real field edit', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <EventTriggerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    templates={mockTemplates}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const nameInput = document.querySelector<HTMLInputElement>('[data-field="name"]')!;
        await user.type(nameInput, 'My Trigger');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });
});
