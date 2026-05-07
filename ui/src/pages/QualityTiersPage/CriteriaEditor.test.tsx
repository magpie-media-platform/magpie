import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { CriteriaEditor, type Criterion } from './CriteriaEditor';

function TestWrapper({ children }: { children: ReactNode }) {
    return <TooltipProvider>{children}</TooltipProvider>;
}

function createMockCriterion(overrides: Partial<Criterion> = {}): Criterion {
    return {
        id: 'c-test-1',
        field: 'source',
        operator: 'Equals',
        value: 'Bluray',
        ...overrides,
    };
}

describe('CriteriaEditor', () => {
    it('renders the component with data-component attribute', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="criteria-editor"]')).toBeInTheDocument();
    });

    it('renders empty state when no criteria', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/no criteria defined/i)).toBeInTheDocument();
    });

    it('renders the header with label and add button', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByTestId('criteria-editor__label')).toHaveTextContent('Criteria');
        expect(screen.getByRole('button', { name: /add/i })).toBeInTheDocument();
    });

    it('renders criteria rows when criteria exist', () => {
        const criteria = [
            createMockCriterion({ id: 'c1' }),
            createMockCriterion({ id: 'c2', field: 'resolution', value: '1080p' }),
        ];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        const list = screen.getByRole('list');
        const items = within(list).getAllByRole('listitem');
        expect(items).toHaveLength(2);
    });

    it('displays row numbers for criteria', () => {
        const criteria = [createMockCriterion({ id: 'c1' }), createMockCriterion({ id: 'c2' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('1')).toBeInTheDocument();
        expect(screen.getByText('2')).toBeInTheDocument();
    });

    it('calls onChange with new criterion when add button is clicked', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();

        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={onChange} />
            </TestWrapper>,
        );

        const addButton = screen.getByRole('button', { name: /add/i });
        await user.click(addButton);

        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith([
            expect.objectContaining({
                field: 'source',
                operator: 'Equals',
                value: '',
            }),
        ]);
    });

    it('adds to existing criteria when add button is clicked', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        const existingCriteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={existingCriteria} onChange={onChange} />
            </TestWrapper>,
        );

        const addButton = screen.getByRole('button', { name: /add/i });
        await user.click(addButton);

        expect(onChange).toHaveBeenCalledWith([
            existingCriteria[0],
            expect.objectContaining({
                field: 'source',
                operator: 'Equals',
                value: '',
            }),
        ]);
    });

    it('calls onChange when criterion is removed', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        const criteria = [createMockCriterion({ id: 'c1' }), createMockCriterion({ id: 'c2' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={onChange} />
            </TestWrapper>,
        );

        const removeButtons = screen.getAllByLabelText(/remove criterion/i);
        await user.click(removeButtons[0]);

        expect(onChange).toHaveBeenCalledWith([criteria[1]]);
    });

    it('calls onChange when value input changes', async () => {
        const user = userEvent.setup();
        const onChange = vi.fn();
        const criteria = [createMockCriterion({ id: 'c1', value: '' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={onChange} />
            </TestWrapper>,
        );

        const valueInput = screen.getByLabelText(/criterion 1 value/i);
        await user.type(valueInput, 'x');

        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith([
            expect.objectContaining({
                id: 'c1',
                value: 'x',
            }),
        ]);
    });

    it('disables inputs when disabled prop is true', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} disabled />
            </TestWrapper>,
        );

        const valueInput = screen.getByLabelText(/criterion 1 value/i);
        expect(valueInput).toBeDisabled();

        const addButton = screen.getByRole('button', { name: /add/i });
        expect(addButton).toBeDisabled();

        const removeButton = screen.getByLabelText(/remove criterion 1/i);
        expect(removeButton).toBeDisabled();
    });

    it('renders help text', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/all criteria must match/i)).toBeInTheDocument();
    });

    it('has data-criterion-id attribute on rows', () => {
        const criteria = [createMockCriterion({ id: 'test-criterion-id' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-criterion-id="test-criterion-id"]')).toBeInTheDocument();
    });

    it('has data-action attribute on add button', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="add-criterion"]')).toBeInTheDocument();
    });

    it('has data-action attribute on remove button', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="remove-criterion"]')).toBeInTheDocument();
    });

    it('has data-field attributes on inputs', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="field"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="operator"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="value"]')).toBeInTheDocument();
    });
});

describe('CriteriaEditor accessibility', () => {
    it('has proper aria-label on field selector', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toBeInTheDocument();
    });

    it('has proper aria-label on operator selector', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 operator/i)).toBeInTheDocument();
    });

    it('has proper aria-label on value input', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 value/i)).toBeInTheDocument();
    });

    it('has proper aria-label on remove button', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/remove criterion 1/i)).toBeInTheDocument();
    });

    it('has role="list" on criteria list', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByRole('list')).toBeInTheDocument();
    });

    it('has role="listitem" on criteria rows', () => {
        const criteria = [createMockCriterion({ id: 'c1' })];

        render(
            <TestWrapper>
                <CriteriaEditor criteria={criteria} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByRole('listitem')).toBeInTheDocument();
    });
});
