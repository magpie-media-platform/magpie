import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { CriteriaEditor, type Criterion } from './CriteriaEditor';
import { isNumericField, operatorsForField } from './criterionOperators';

const mockUseQualityFields = vi.fn();

vi.mock('@/hooks/useQualityFields', () => ({
    useQualityFields: () => mockUseQualityFields(),
}));

const defaultQualityFields = {
    universalFields: [
        { name: 'Title', label: 'Title', isNumeric: false },
        { name: 'Protocol', label: 'Protocol', isNumeric: false },
        { name: 'Size', label: 'Size', isNumeric: true },
        { name: 'Age', label: 'Age', isNumeric: true },
        { name: 'Language', label: 'Language', isNumeric: false },
    ],
    isLoading: false,
    error: null,
};

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

function fieldSuggestionValues(): string[] {
    const datalist = screen.getByTestId('criteria-editor__field-suggestions');
    return Array.from(datalist.querySelectorAll('option')).map((option) => option.value);
}

describe('CriteriaEditor', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseQualityFields.mockReturnValue(defaultQualityFields);
    });

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
                field: 'Title',
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
                field: 'Title',
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

    it('shows the stored field value in the field input', () => {
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'resolution' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toHaveValue('resolution');
    });

    it('shows a backend-cased field value verbatim in the field input', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1', field: 'Source' })]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toHaveValue('Source');
    });

    it('renders an unrecognised field key verbatim instead of blanking the input', () => {
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'ReleaseName' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toHaveValue('ReleaseName');
    });

    it('accepts an arbitrary typed field value not in the known vocabulary', () => {
        const onChange = vi.fn();
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1', field: '' })]} onChange={onChange} />
            </TestWrapper>,
        );

        fireEvent.change(screen.getByLabelText(/criterion 1 field/i), { target: { value: 'ReleaseName' } });

        expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ id: 'c1', field: 'ReleaseName' })]);
    });

    it('keeps a stored field value even when the current vocabulary does not include it', () => {
        mockUseQualityFields.mockReturnValue({
            ...defaultQualityFields,
            universalFields: [
                { name: 'Title', label: 'Title', isNumeric: false },
                { name: 'Size', label: 'Size', isNumeric: true },
            ],
        });

        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1', field: 'source' })]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toHaveValue('source');
    });

    it('gives every criterion control an accessible name and leaves no unnamed combobox', () => {
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'source', operator: 'Equals', value: 'Bluray' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/criterion 1 field/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/criterion 1 operator/i)).toBeInTheDocument();
        expect(screen.getByLabelText(/criterion 1 value/i)).toBeInTheDocument();

        for (const combobox of screen.getAllByRole('combobox')) {
            expect(combobox).toHaveAccessibleName();
        }
    });

    it('shows the selected operator label instead of the raw contains symbol', () => {
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', operator: 'Contains', value: 'Star' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        const trigger = screen.getByLabelText(/criterion 1 operator/i);
        expect(trigger).toHaveTextContent('Contains');
        expect(trigger).not.toHaveTextContent('*=');
    });

    it('defaults the empty and help copy to the tier scope', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText(/match this tier/i)).toBeInTheDocument();
        expect(screen.getByText(/considered for this tier/i)).toBeInTheDocument();
    });

    it('uses the provided scope noun in the empty and help copy', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} scopeNoun="this rule" />
            </TestWrapper>,
        );

        expect(screen.getByText(/match this rule/i)).toBeInTheDocument();
        expect(screen.getByText(/considered for this rule/i)).toBeInTheDocument();
        expect(screen.queryByText(/this tier/i)).not.toBeInTheDocument();
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

describe('CriteriaEditor field-type gating', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseQualityFields.mockReturnValue(defaultQualityFields);
    });

    it('classifies size and age as numeric fields', () => {
        expect(isNumericField('size')).toBe(true);
        expect(isNumericField('age')).toBe(true);
    });

    it('classifies string fields as non-numeric', () => {
        expect(isNumericField('source')).toBe(false);
        expect(isNumericField('language')).toBe(false);
        expect(isNumericField('custom')).toBe(false);
    });

    it('offers the four numeric operators (plus equality) for numeric fields', () => {
        const values = operatorsForField('size').map((o) => o.value);
        expect(values).toEqual([
            'Equals',
            'NotEquals',
            'GreaterThan',
            'GreaterThanOrEqual',
            'LessThan',
            'LessThanOrEqual',
        ]);
    });

    it('does not offer numeric operators for string fields', () => {
        const values = operatorsForField('source').map((o) => o.value);
        expect(values).not.toContain('GreaterThan');
        expect(values).not.toContain('LessThanOrEqual');
        expect(values).toContain('Contains');
        expect(values).toContain('StartsWith');
    });

    it('offers the set, regex and ends-with string operators', () => {
        const values = operatorsForField('source').map((o) => o.value);
        expect(values).toContain('In');
        expect(values).toContain('NotIn');
        expect(values).toContain('Regex');
        expect(values).toContain('EndsWith');
        expect(values).toContain('NotEndsWith');
    });

    it('offers ContainsWord as a selectable string operator', () => {
        const values = operatorsForField('source').map((o) => o.value);
        expect(values).toContain('ContainsWord');
    });

    it('renders the Contains Word option in a string field dropdown', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1', field: 'source' })]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText(/criterion 1 operator/i));
        const optionLabels = screen
            .getAllByRole('option')
            .map((o) => o.querySelector('.criteria-editor__operator-label')?.textContent ?? '');
        expect(optionLabels).toContain('Contains Word');
    });

    it('treats ContainsWord as a single-value (scalar) operator, not a set input', () => {
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[
                        createMockCriterion({ id: 'c1', field: 'source', operator: 'ContainsWord', value: 'Remux' }),
                    ]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        const valueInput = screen.getByLabelText(/criterion 1 value/i);
        expect(valueInput).not.toHaveAttribute('data-value-kind', 'set');
        expect(valueInput).toHaveValue('Remux');
    });

    it('renders In, Not In, Regex and Ends With options in a string field dropdown', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1', field: 'source' })]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText(/criterion 1 operator/i));
        const optionLabels = screen
            .getAllByRole('option')
            .map((o) => o.querySelector('.criteria-editor__operator-label')?.textContent ?? '');
        expect(optionLabels).toContain('In');
        expect(optionLabels).toContain('Not In');
        expect(optionLabels).toContain('Regex');
        expect(optionLabels).toContain('Ends With');
    });

    it('switches the value input to a comma-separated set input for In', () => {
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'source', operator: 'In', value: 'a, b' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        const valueInput = screen.getByLabelText(/criterion 1 value/i);
        expect(valueInput).toHaveAttribute('data-value-kind', 'set');
        expect(valueInput).toHaveAttribute('placeholder', expect.stringMatching(/comma-separated/i));
        expect(valueInput).toHaveValue('a, b');
    });

    it('suggests declared fields without constraining the input', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1' })]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(fieldSuggestionValues()).toContain('Title');
    });

    it('shows numeric operators in the operator dropdown for a numeric field', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'size', value: '10' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText(/criterion 1 operator/i));
        expect(screen.getByRole('option', { name: /greater than/i })).toBeInTheDocument();
        expect(screen.getByRole('option', { name: /at least/i })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: /contains/i })).not.toBeInTheDocument();
    });

    it('does not show numeric operators for a string field', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1', field: 'source' })]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText(/criterion 1 operator/i));
        const optionNames = screen.getAllByRole('option').map((o) => o.textContent ?? '');
        expect(optionNames.some((n) => /contains/i.test(n))).toBe(true);
        expect(optionNames.some((n) => /starts with/i.test(n))).toBe(true);
        expect(optionNames.some((n) => /greater than/i.test(n))).toBe(false);
        expect(optionNames.some((n) => /at least/i.test(n))).toBe(false);
    });

    it('resets a numeric operator to Equals when switching to a string field', () => {
        const onChange = vi.fn();
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'size', operator: 'GreaterThan', value: '5' })]}
                    onChange={onChange}
                />
            </TestWrapper>,
        );

        fireEvent.change(screen.getByLabelText(/criterion 1 field/i), { target: { value: 'Source' } });

        expect(onChange).toHaveBeenCalledWith([
            expect.objectContaining({ id: 'c1', field: 'Source', operator: 'Equals' }),
        ]);
    });

    it('keeps a shared operator when switching between field types', () => {
        const onChange = vi.fn();
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'size', operator: 'Equals', value: '5' })]}
                    onChange={onChange}
                />
            </TestWrapper>,
        );

        fireEvent.change(screen.getByLabelText(/criterion 1 field/i), { target: { value: 'Source' } });

        expect(onChange).toHaveBeenCalledWith([
            expect.objectContaining({ id: 'c1', field: 'Source', operator: 'Equals' }),
        ]);
    });
});

describe('CriteriaEditor field suggestions', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseQualityFields.mockReturnValue(defaultQualityFields);
    });

    it('does not render a per-rule content type context selector', () => {
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[]} onChange={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByLabelText(/content type context/i)).not.toBeInTheDocument();
        expect(document.querySelector('[data-field="content-type-context"]')).not.toBeInTheDocument();
    });

    it('accepts a custom field value not in the suggestions', () => {
        const onChange = vi.fn();
        render(
            <TestWrapper>
                <CriteriaEditor criteria={[createMockCriterion({ id: 'c1' })]} onChange={onChange} />
            </TestWrapper>,
        );

        fireEvent.change(screen.getByLabelText(/criterion 1 field/i), { target: { value: 'CustomField' } });

        expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ id: 'c1', field: 'CustomField' })]);
    });

    it('uses numeric operators for registry-known numeric fields', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <CriteriaEditor
                    criteria={[createMockCriterion({ id: 'c1', field: 'Size', value: '100' })]}
                    onChange={vi.fn()}
                />
            </TestWrapper>,
        );

        await user.click(screen.getByLabelText(/criterion 1 operator/i));
        expect(screen.getByRole('option', { name: /greater than/i })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: /contains/i })).not.toBeInTheDocument();
    });
});

describe('CriteriaEditor accessibility', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseQualityFields.mockReturnValue(defaultQualityFields);
    });

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
