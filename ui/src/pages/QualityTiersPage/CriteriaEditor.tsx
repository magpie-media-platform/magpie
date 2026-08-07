import { useCallback, useMemo, useId, type ReactNode } from 'react';
import { PlusIcon, TrashIcon } from '@phosphor-icons/react';

import { useQualityFields, type QualityAttributeField } from '@/hooks/useQualityFields';
import type { CriteriaValueOperator } from '@/hooks/useQualityTierLists';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Tooltip } from '@/components/ui/Tooltip';
import { isNumericField, isSetOperator, operatorsForField, type CriterionOperator } from './criterionOperators';

import './CriteriaEditor.css';
import './EditorControls.css';

export type { CriterionOperator };

type StoredCriterionOperator = CriterionOperator | CriteriaValueOperator;

export interface Criterion {
    id: string;
    field: string;
    operator: StoredCriterionOperator;
    value: string;
}

interface CriteriaEditorProps {
    criteria: Criterion[];

    onChange: (criteria: Criterion[]) => void;

    label?: string;

    scopeNoun?: string;

    helpText?: string;

    disabled?: boolean;
}

interface FieldSuggestion {
    value: string;
    label: string;
}

const FALLBACK_FIELDS: FieldSuggestion[] = [
    { value: 'Title', label: 'Title' },
    { value: 'source', label: 'Source' },
    { value: 'resolution', label: 'Resolution' },
    { value: 'codec', label: 'Codec' },
    { value: 'hdr', label: 'HDR' },
    { value: 'audio', label: 'Audio' },
    { value: 'group', label: 'Release Group' },
    { value: 'language', label: 'Language' },
    { value: 'size', label: 'Size' },
    { value: 'age', label: 'Age' },
];

function buildFieldSuggestions(universalFields: QualityAttributeField[]): FieldSuggestion[] {
    if (universalFields.length === 0) {
        return FALLBACK_FIELDS;
    }

    return universalFields.map((f) => ({ value: f.name, label: f.label }));
}

function buildNumericFields(universalFields: QualityAttributeField[]): Set<string> {
    const numeric = new Set<string>();
    for (const f of universalFields) {
        if (f.isNumeric) {
            numeric.add(f.name.toLowerCase());
        }
    }
    return numeric;
}

function generateCriterionId(): string {
    return `c-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`;
}

function operatorLabel(operator: StoredCriterionOperator, field: string, numericFields?: Set<string>): string {
    return operatorsForField(field, numericFields).find((option) => option.value === operator)?.label ?? operator;
}

export function CriteriaEditor({
    criteria,
    onChange,
    label = 'Criteria',
    scopeNoun = 'this tier',
    helpText,
    disabled = false,
}: CriteriaEditorProps): ReactNode {
    const { universalFields } = useQualityFields();
    const fieldListId = useId();

    const fieldSuggestions = useMemo(() => buildFieldSuggestions(universalFields), [universalFields]);

    const numericFields = useMemo(() => buildNumericFields(universalFields), [universalFields]);

    const handleAddCriterion = useCallback(() => {
        const defaultField = fieldSuggestions[0]?.value ?? 'Title';
        const newCriterion: Criterion = {
            id: generateCriterionId(),
            field: defaultField,
            operator: 'Equals',
            value: '',
        };
        onChange([...criteria, newCriterion]);
    }, [criteria, onChange, fieldSuggestions]);

    const handleRemoveCriterion = useCallback(
        (id: string) => {
            onChange(criteria.filter((c) => c.id !== id));
        },
        [criteria, onChange],
    );

    const handleFieldChange = useCallback(
        (id: string, field: string) => {
            onChange(
                criteria.map((c) => {
                    if (c.id !== id) {
                        return c;
                    }
                    const allowed = operatorsForField(field, numericFields);
                    const operator = allowed.some((o) => o.value === c.operator) ? c.operator : 'Equals';
                    return { ...c, field, operator };
                }),
            );
        },
        [criteria, onChange, numericFields],
    );

    const handleOperatorChange = useCallback(
        (id: string, operator: CriterionOperator) => {
            onChange(criteria.map((c) => (c.id === id ? { ...c, operator } : c)));
        },
        [criteria, onChange],
    );

    const handleValueChange = useCallback(
        (id: string, value: string) => {
            onChange(criteria.map((c) => (c.id === id ? { ...c, value } : c)));
        },
        [criteria, onChange],
    );

    return (
        <div className="criteria-editor" data-component="criteria-editor">
            <div className="criteria-editor__header">
                <span className="criteria-editor__label" data-testid="criteria-editor__label">
                    {label}
                </span>
                <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleAddCriterion}
                    disabled={disabled}
                    data-action="add-criterion"
                >
                    <PlusIcon weight="bold" aria-hidden="true" />
                    Add
                </Button>
            </div>

            {criteria.length === 0 ? (
                <p className="criteria-editor__empty">
                    No criteria defined. Add criteria to specify what releases match {scopeNoun}.
                </p>
            ) : (
                <ul className="criteria-editor__list" role="list">
                    {criteria.map((criterion, index) => (
                        <CriterionRow
                            key={criterion.id}
                            criterion={criterion}
                            index={index}
                            disabled={disabled}
                            fieldListId={fieldListId}
                            numericFields={numericFields}
                            onFieldChange={handleFieldChange}
                            onOperatorChange={handleOperatorChange}
                            onValueChange={handleValueChange}
                            onRemove={handleRemoveCriterion}
                        />
                    ))}
                </ul>
            )}

            <datalist id={fieldListId} data-testid="criteria-editor__field-suggestions">
                {fieldSuggestions.map((field) => (
                    <option key={field.value} value={field.value} label={field.label} />
                ))}
            </datalist>

            <p className="criteria-editor__help">
                {helpText ?? `All criteria must match for a release to be considered for ${scopeNoun}.`}
            </p>
        </div>
    );
}

CriteriaEditor.displayName = 'CriteriaEditor';

interface CriterionRowProps {
    criterion: Criterion;
    index: number;
    disabled: boolean;
    fieldListId: string;
    numericFields: Set<string>;
    onFieldChange: (id: string, field: string) => void;
    onOperatorChange: (id: string, operator: CriterionOperator) => void;
    onValueChange: (id: string, value: string) => void;
    onRemove: (id: string) => void;
}

function CriterionRow({
    criterion,
    index,
    disabled,
    fieldListId,
    numericFields,
    onFieldChange,
    onOperatorChange,
    onValueChange,
    onRemove,
}: CriterionRowProps): ReactNode {
    const numeric = isNumericField(criterion.field, numericFields);
    const operators = operatorsForField(criterion.field, numericFields);
    const isSet = isSetOperator(criterion.operator);
    const valuePlaceholder = isSet
        ? 'Comma-separated values'
        : criterion.field.toLowerCase() === 'size'
          ? 'Bytes (21474836480)'
          : 'Value';

    return (
        <li className="criteria-editor__row" role="listitem" data-criterion-id={criterion.id}>
            <span className="criteria-editor__row-number">{index + 1}</span>

            <div className="criteria-editor__field">
                <Input
                    type="text"
                    list={fieldListId}
                    value={criterion.field}
                    onChange={(e) => onFieldChange(criterion.id, e.target.value)}
                    placeholder="Field"
                    disabled={disabled}
                    size="sm"
                    className="criteria-editor__field-input"
                    aria-label={`Criterion ${index + 1} field`}
                    data-field="field"
                />
            </div>

            <Select
                value={criterion.operator}
                onValueChange={(value) => onOperatorChange(criterion.id, value as CriterionOperator)}
                disabled={disabled}
            >
                <SelectTrigger
                    size="sm"
                    className="criteria-editor__operator-select"
                    aria-label={`Criterion ${index + 1} operator`}
                    data-field="operator"
                >
                    <span className="criteria-editor__operator-value">
                        {operatorLabel(criterion.operator, criterion.field, numericFields)}
                    </span>
                </SelectTrigger>
                <SelectContent>
                    {operators.map((op) => (
                        <SelectItem key={op.value} value={op.value}>
                            <span className="criteria-editor__operator-item">
                                <span className="criteria-editor__operator-symbol">{op.symbol}</span>
                                <span className="criteria-editor__operator-label">{op.label}</span>
                            </span>
                        </SelectItem>
                    ))}
                </SelectContent>
            </Select>

            <Input
                type="text"
                inputMode={numeric && !isSet ? 'numeric' : undefined}
                value={criterion.value}
                onChange={(e) => onValueChange(criterion.id, e.target.value)}
                placeholder={valuePlaceholder}
                disabled={disabled}
                size="sm"
                className="criteria-editor__value-input"
                aria-label={`Criterion ${index + 1} value`}
                data-field="value"
                data-value-kind={isSet ? 'set' : 'single'}
            />

            <Tooltip content="Remove criterion">
                <button
                    type="button"
                    className="criteria-editor__remove-btn"
                    onClick={() => onRemove(criterion.id)}
                    disabled={disabled}
                    aria-label={`Remove criterion ${index + 1}`}
                    data-action="remove-criterion"
                    data-target={criterion.id}
                >
                    <TrashIcon weight="bold" aria-hidden="true" />
                </button>
            </Tooltip>
        </li>
    );
}

export default CriteriaEditor;
