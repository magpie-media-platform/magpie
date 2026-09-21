import { useCallback, type ReactNode } from 'react';
import { PlusIcon, TrashIcon } from '@phosphor-icons/react';

import type { CriteriaValueOperator } from '@/hooks/useQualityTierLists';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Tooltip } from '@/components/ui/Tooltip';
import { hasBlankField } from './criteriaEditorUtils';
import { CRITERION_OPERATORS, isSetOperator, type CriterionOperator } from './criterionOperators';

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

function generateCriterionId(): string {
    return `c-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`;
}

function operatorLabel(operator: StoredCriterionOperator): string {
    return CRITERION_OPERATORS.find((option) => option.value === operator)?.label ?? operator;
}

export function CriteriaEditor({
    criteria,
    onChange,
    label = 'Criteria',
    scopeNoun = 'this tier',
    helpText,
    disabled = false,
}: CriteriaEditorProps): ReactNode {
    const handleAddCriterion = useCallback(() => {
        const newCriterion: Criterion = {
            id: generateCriterionId(),
            field: '',
            operator: 'Equals',
            value: '',
        };
        onChange([...criteria, newCriterion]);
    }, [criteria, onChange]);

    const handleRemoveCriterion = useCallback(
        (id: string) => {
            onChange(criteria.filter((c) => c.id !== id));
        },
        [criteria, onChange],
    );

    const handleFieldChange = useCallback(
        (id: string, field: string) => {
            onChange(criteria.map((c) => (c.id === id ? { ...c, field } : c)));
        },
        [criteria, onChange],
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
                            onFieldChange={handleFieldChange}
                            onOperatorChange={handleOperatorChange}
                            onValueChange={handleValueChange}
                            onRemove={handleRemoveCriterion}
                        />
                    ))}
                </ul>
            )}

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
    onFieldChange: (id: string, field: string) => void;
    onOperatorChange: (id: string, operator: CriterionOperator) => void;
    onValueChange: (id: string, value: string) => void;
    onRemove: (id: string) => void;
}

function CriterionRow({
    criterion,
    index,
    disabled,
    onFieldChange,
    onOperatorChange,
    onValueChange,
    onRemove,
}: CriterionRowProps): ReactNode {
    const isSet = isSetOperator(criterion.operator);
    const valuePlaceholder = isSet ? 'Comma-separated values' : 'Value';

    return (
        <li className="criteria-editor__row" role="listitem" data-criterion-id={criterion.id}>
            <span className="criteria-editor__row-number">{index + 1}</span>

            <div className="criteria-editor__field">
                <Input
                    type="text"
                    value={criterion.field}
                    onChange={(e) => onFieldChange(criterion.id, e.target.value)}
                    placeholder="Field"
                    error={hasBlankField(criterion)}
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
                    <span className="criteria-editor__operator-value">{operatorLabel(criterion.operator)}</span>
                </SelectTrigger>
                <SelectContent>
                    {CRITERION_OPERATORS.map((op) => (
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
