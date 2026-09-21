import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { useState, type ChangeEvent } from 'react';

import { FilterForm } from './FilterForm';
import type { FilterFormProps } from './FilterForm';
import { parseMagpieQl } from '@/lib/magpieQl';

vi.mock('@/editor/FilterQueryEditor', () => ({
    FilterQueryEditor: ({
        value,
        onChange,
        ...props
    }: {
        value: string;
        onChange: (v: string) => void;
        [key: string]: unknown;
    }) => (
        <div data-testid="filter-query-editor" data-value={value} {...props}>
            <input
                data-testid="query-input"
                value={value}
                onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
            />
        </div>
    ),
}));

vi.mock('@/components/filters/SimpleFilterBuilder', () => ({
    SimpleFilterBuilder: ({
        value,
        onChange: _onChange,
        loading: _loading,
        defaultContentType: _defaultContentType,
        ...props
    }: {
        value: string;
        onChange: (v: string) => void;
        [key: string]: unknown;
    }) => <div data-testid="simple-filter-builder" data-value={value} {...props} />,
}));

vi.mock('@/lib/simpleFilterQuery', () => ({
    isSimpleQuery: (query: string) => {
        if (!query.trim()) return true;
        return !query.includes(' or ') && !query.includes('>=') && !query.includes('<=');
    },
}));

vi.mock('@/lib/magpieQl', () => ({
    parseMagpieQl: vi.fn().mockResolvedValue({ criteria: {} }),
}));

function renderFilterForm(overrides: Partial<FilterFormProps> = {}) {
    const props: FilterFormProps = {
        open: true,
        onOpenChange: vi.fn(),
        initialValues: null,
        onSubmit: vi.fn(),
        isSubmitting: false,
        isOwner: true,
        canEdit: true,
        isShared: false,
        ...overrides,
    };

    return { ...render(<FilterForm {...props} />), props };
}

describe('FilterForm mode switch confirmation dialog', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('switches to advanced mode without confirmation', async () => {
        const user = userEvent.setup();
        renderFilterForm();
        expect(screen.queryByRole('switch', { name: 'Shared Filter' })).not.toBeInTheDocument();

        const switchControl = document.querySelector('[data-action="switch-to-advanced"]') as HTMLButtonElement;
        expect(switchControl.tagName).toBe('BUTTON');
        expect(switchControl).toHaveClass('button', 'button--ghost', 'button--sm');
        await user.click(switchControl);

        expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();
        expect(screen.queryByTestId('simple-filter-builder')).not.toBeInTheDocument();
    });

    it('switches back to simple mode without confirmation when query is simple-compatible', async () => {
        const user = userEvent.setup();
        renderFilterForm({
            initialValues: {
                id: '1',
                name: 'Test',
                ownerId: 'user-1',
                query: 'genre = "Action"',
            },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-action="switch-to-advanced"]')).toBeInTheDocument();
        });

        const switchToAdvanced = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
        await user.click(switchToAdvanced);

        const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
        await user.click(switchToSimple);

        expect(screen.getByTestId('simple-filter-builder')).toBeInTheDocument();
    });

    it('opens confirmation dialog when switching to simple mode with complex query', async () => {
        const user = userEvent.setup();
        renderFilterForm({
            initialValues: {
                id: '1',
                name: 'Complex Filter',
                ownerId: 'user-1',
                query: 'genre = "Action" or year >= 2020',
            },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-action="switch-to-simple"]')).toBeInTheDocument();
        });

        const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
        await user.click(switchToSimple);

        expect(screen.getByText('Switch to Simple Mode')).toBeInTheDocument();
        expect(
            screen.getByText(
                'This query uses options that simple mode cannot represent. Switching modes will clear it.',
            ),
        ).toBeInTheDocument();
    });

    it('does not switch mode when cancel is clicked in confirmation dialog', async () => {
        const user = userEvent.setup();
        renderFilterForm({
            initialValues: {
                id: '1',
                name: 'Complex Filter',
                ownerId: 'user-1',
                query: 'genre = "Action" or year >= 2020',
            },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-action="switch-to-simple"]')).toBeInTheDocument();
        });

        const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
        await user.click(switchToSimple);

        const cancelButton = document.querySelector(
            '[data-component="confirm-mode-switch"] [data-action="cancel"]',
        ) as HTMLElement;
        await user.click(cancelButton);

        expect(screen.getByTestId('filter-query-editor')).toBeInTheDocument();
        expect(screen.queryByTestId('simple-filter-builder')).not.toBeInTheDocument();
    });

    it('switches to simple mode and resets query when confirmed', async () => {
        const user = userEvent.setup();
        renderFilterForm({
            initialValues: {
                id: '1',
                name: 'Complex Filter',
                ownerId: 'user-1',
                query: 'genre = "Action" or year >= 2020',
            },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-action="switch-to-simple"]')).toBeInTheDocument();
        });

        const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
        await user.click(switchToSimple);

        const confirmButton = document.querySelector('[data-action="confirm-switch"]') as HTMLElement;
        await user.click(confirmButton);

        await waitFor(() => {
            expect(screen.getByTestId('simple-filter-builder')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('filter-query-editor')).not.toBeInTheDocument();
    });

    it('confirmation dialog has accessible title', async () => {
        const user = userEvent.setup();
        renderFilterForm({
            initialValues: {
                id: '1',
                name: 'Complex Filter',
                ownerId: 'user-1',
                query: 'genre = "Action" or year >= 2020',
            },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-action="switch-to-simple"]')).toBeInTheDocument();
        });

        const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
        await user.click(switchToSimple);

        expect(screen.getByRole('heading', { name: 'Switch to Simple Mode' })).toBeInTheDocument();
    });

    it('does not use window.confirm', async () => {
        const confirmSpy = vi.spyOn(window, 'confirm');
        const user = userEvent.setup();

        renderFilterForm({
            initialValues: {
                id: '1',
                name: 'Complex Filter',
                ownerId: 'user-1',
                query: 'genre = "Action" or year >= 2020',
            },
        });

        await waitFor(() => {
            expect(document.querySelector('[data-action="switch-to-simple"]')).toBeInTheDocument();
        });

        const switchToSimple = document.querySelector('[data-action="switch-to-simple"]') as HTMLElement;
        await user.click(switchToSimple);

        expect(confirmSpy).not.toHaveBeenCalled();
        confirmSpy.mockRestore();
    });
});

describe('FilterForm parse error accessibility', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders parse error with role="alert" for screen readers', async () => {
        vi.mocked(parseMagpieQl).mockResolvedValueOnce({
            criteria: {},
            error: { message: 'Unexpected token at position 5' },
        } as never);

        const user = userEvent.setup();
        renderFilterForm();

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Filter');

        const switchToAdvanced = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
        await user.click(switchToAdvanced);

        const queryInput = screen.getByTestId('query-input');
        await user.type(queryInput, 'unknown = true');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            const errorDiv = screen.getByRole('alert');
            expect(errorDiv).toHaveTextContent('Unexpected token at position 5');
        });
    });

    it('renders query validation request failures without submitting', async () => {
        vi.mocked(parseMagpieQl).mockRejectedValueOnce(new Error('Network unavailable'));
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderFilterForm({ onSubmit });

        await user.type(screen.getByLabelText(/name/i), 'Test Filter');
        await user.click(document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement);
        await user.type(screen.getByTestId('query-input'), 'unknown = true');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Network unavailable');
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('FilterForm duplicate-name guard', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('blocks save and warns when the name matches an existing filter (case-insensitive)', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderFilterForm({ onSubmit, existingNames: ['Action Movies', 'Recently Added'] });

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'action movies');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByText(/a filter with this name already exists/i)).toBeInTheDocument();
        });
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('allows save when the name is unique', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderFilterForm({ onSubmit, existingNames: ['Action Movies'] });

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Comedy Movies');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(onSubmit).toHaveBeenCalledTimes(1);
        });
        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ name: 'Comedy Movies' }));
        expect(screen.queryByText(/a filter with this name already exists/i)).not.toBeInTheDocument();
    });

    it('clears the duplicate warning when the name is edited', async () => {
        const user = userEvent.setup();
        renderFilterForm({ existingNames: ['Action Movies'] });

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Action Movies');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByText(/a filter with this name already exists/i)).toBeInTheDocument();
        });

        await user.type(nameInput, ' 2024');

        expect(screen.queryByText(/a filter with this name already exists/i)).not.toBeInTheDocument();
    });

    it('allows keeping the same name when editing (own name excluded from existingNames)', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderFilterForm({
            onSubmit,
            existingNames: ['Recently Added'],
            initialValues: { id: 'filter-1', name: 'Action Movies', ownerId: 'user-1' },
        });

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(onSubmit).toHaveBeenCalledTimes(1);
        });
        expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ name: 'Action Movies' }));
        expect(screen.queryByText(/a filter with this name already exists/i)).not.toBeInTheDocument();
    });

    it('blocks renaming an existing filter to another filter name', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderFilterForm({
            onSubmit,
            existingNames: ['Recently Added'],
            initialValues: { id: 'filter-1', name: 'Action Movies', ownerId: 'user-1' },
        });

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Recently Added');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            expect(screen.getByText(/a filter with this name already exists/i)).toBeInTheDocument();
        });
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

describe('FilterForm unsaved changes', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows unsaved changes dialog when closing dirty create form', async () => {
        const user = userEvent.setup();

        function ControlledFilterForm() {
            const [open, setOpen] = useState(true);

            return (
                <FilterForm
                    open={open}
                    onOpenChange={setOpen}
                    initialValues={null}
                    onSubmit={vi.fn()}
                    isSubmitting={false}
                    isOwner={true}
                    canEdit={true}
                    isShared={false}
                />
            );
        }

        render(<ControlledFilterForm />);

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'New Filter');

        const activeEvent = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
        const activePreventSpy = vi.spyOn(activeEvent, 'preventDefault');
        window.dispatchEvent(activeEvent);
        expect(activePreventSpy).toHaveBeenCalledOnce();

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-component="filter-form"]')).toHaveAttribute('data-state', 'open');

        const stillActiveEvent = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
        const stillActivePreventSpy = vi.spyOn(stillActiveEvent, 'preventDefault');
        window.dispatchEvent(stillActiveEvent);
        expect(stillActivePreventSpy).toHaveBeenCalledOnce();

        const discardButton = screen.getByRole('button', { name: /discard changes/i });
        await user.click(discardButton);

        await waitFor(() => {
            expect(screen.queryByRole('heading', { name: /create filter/i })).not.toBeInTheDocument();
        });

        const inactiveEvent = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
        const inactivePreventSpy = vi.spyOn(inactiveEvent, 'preventDefault');
        window.dispatchEvent(inactiveEvent);
        expect(inactivePreventSpy).not.toHaveBeenCalled();
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });

    it('shows unsaved changes dialog when closing dirty edit form', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderFilterForm({
            onOpenChange,
            initialValues: {
                id: '1',
                name: 'Original Name',
                ownerId: 'user-1',
            },
        });

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Modified Name');

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('closes the form when discarding unsaved changes', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderFilterForm({
            onOpenChange,
            initialValues: {
                id: '1',
                name: 'Original Name',
                ownerId: 'user-1',
            },
        });

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Modified Name');

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
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
        renderFilterForm({
            onOpenChange,
            initialValues: {
                id: '1',
                name: 'Original Name',
                ownerId: 'user-1',
            },
        });

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Modified Name');

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
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
        expect(screen.getByLabelText(/name/i)).toHaveValue('Modified Name');
    });

    it('closes without warning when form is not dirty', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderFilterForm({ onOpenChange });

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
        await user.click(cancelButton);

        expect(onOpenChange).toHaveBeenCalledWith(false);
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });

    it('does not show unsaved changes dialog in read-only mode', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderFilterForm({
            onOpenChange,
            canEdit: false,
            initialValues: {
                id: '1',
                name: 'Locked Filter',
                ownerId: 'user-1',
            },
        });

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
        await user.click(cancelButton);

        expect(onOpenChange).toHaveBeenCalledWith(false);
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });

    it('shows unsaved changes dialog when editing and name is modified', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderFilterForm({
            onOpenChange,
            initialValues: {
                id: '1',
                name: 'Original Name',
                ownerId: 'user-1',
                query: 'genre = "Action"',
            },
        });

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Modified Name');

        const cancelButton = document.querySelector('[data-action="cancel"]') as HTMLElement;
        await user.click(cancelButton);

        await waitFor(() => {
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        expect(onOpenChange).not.toHaveBeenCalled();
    });
});
