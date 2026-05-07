import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import type { ChangeEvent } from 'react';

import { FilterForm } from './FilterForm';
import type { FilterFormProps } from './FilterForm';
import { parseFilterQuery } from '@/lib/filterQuery';

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

vi.mock('@/components/ui/SimpleFilterBuilder', () => ({
    SimpleFilterBuilder: ({
        value,
        onChange: _onChange,
        ...props
    }: {
        value: string;
        onChange: (v: string) => void;
        [key: string]: unknown;
    }) => <div data-testid="simple-filter-builder" data-value={value} {...props} />,
    isSimpleQuery: (query: string) => {
        if (!query.trim()) return true;
        return !query.includes(' or ') && !query.includes('>=') && !query.includes('<=');
    },
}));

vi.mock('@/lib/filterQuery', () => ({
    parseFilterQuery: vi.fn().mockResolvedValue({ criteria: {} }),
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

        const switchLink = document.querySelector('[data-action="switch-to-advanced"]') as HTMLElement;
        await user.click(switchLink);

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
        expect(screen.getByText(/not available in simple mode/)).toBeInTheDocument();
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
        vi.mocked(parseFilterQuery).mockResolvedValueOnce({
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
        await user.type(queryInput, 'bad query');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        await waitFor(() => {
            const errorDiv = screen.getByRole('alert');
            expect(errorDiv).toHaveTextContent('Unexpected token at position 5');
        });
    });
});

describe('FilterForm unsaved changes', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows unsaved changes dialog when closing with dirty name', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderFilterForm({ onOpenChange });

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'New Filter');

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
        renderFilterForm({ onOpenChange });

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'New Filter');

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
        renderFilterForm({ onOpenChange });

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'New Filter');

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
