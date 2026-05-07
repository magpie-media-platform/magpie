import { useCallback, type ReactNode } from 'react';
import { Plus, Trash } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Tooltip } from '@/components/ui/Tooltip';

import './CriteriaEditor.css';

export type CriterionOperator = 'Equals' | 'NotEquals' | 'Contains' | 'NotContains' | 'StartsWith' | 'NotStartsWith';

export interface Criterion {
    id: string;
    field: string;
    operator: CriterionOperator;
    value: string;
}

export interface CriteriaEditorProps {
    criteria: Criterion[];

    onChange: (criteria: Criterion[]) => void;

    disabled?: boolean;
}

const AVAILABLE_FIELDS = [
    { value: 'source', label: 'Source' },
    { value: 'resolution', label: 'Resolution' },
    { value: 'codec', label: 'Codec' },
    { value: 'hdr', label: 'HDR' },
    { value: 'audio', label: 'Audio' },
    { value: 'group', label: 'Release Group' },
    { value: 'language', label: 'Language' },
    { value: 'size', label: 'Size' },
    { value: 'age', label: 'Age' },
    { value: 'custom', label: 'Custom Field' },
] as const;

const AVAILABLE_OPERATORS: { value: CriterionOperator; label: string; symbol: string }[] = [
    { value: 'Equals', label: 'Equals', symbol: '=' },
    { value: 'NotEquals', label: 'Not Equals', symbol: '!=' },
    { value: 'Contains', label: 'Contains', symbol: '*=' },
    { value: 'NotContains', label: 'Not Contains', symbol: '!*' },
    { value: 'StartsWith', label: 'Starts With', symbol: '^=' },
    { value: 'NotStartsWith', label: 'Not Starts With', symbol: '!^' },
];

function generateCriterionId(): string {
    return `c-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`;
}

export function CriteriaEditor({ criteria, onChange, disabled = false }: CriteriaEditorProps): ReactNode {
    const handleAddCriterion = useCallback(() => {
        const newCriterion: Criterion = {
            id: generateCriterionId(),
            field: 'source',
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
                    Criteria
                </span>
                <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleAddCriterion}
                    disabled={disabled}
                    data-action="add-criterion"
                >
                    <Plus weight="bold" aria-hidden="true" />
                    Add
                </Button>
            </div>

            {criteria.length === 0 ? (
                <p className="criteria-editor__empty">
                    No criteria defined. Add criteria to specify what releases match this tier.
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
                All criteria must match for a release to be considered for this tier.
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
    return (
        <li className="criteria-editor__row" role="listitem" data-criterion-id={criterion.id}>
            <span className="criteria-editor__row-number">{index + 1}</span>

            <Select
                value={criterion.field}
                onValueChange={(value) => onFieldChange(criterion.id, value)}
                disabled={disabled}
            >
                <SelectTrigger
                    size="sm"
                    className="criteria-editor__field-select"
                    aria-label={`Criterion ${index + 1} field`}
                    data-field="field"
                />
                <SelectContent>
                    {AVAILABLE_FIELDS.map((field) => (
                        <SelectItem key={field.value} value={field.value}>
                            {field.label}
                        </SelectItem>
                    ))}
                </SelectContent>
            </Select>

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
                />
                <SelectContent>
                    {AVAILABLE_OPERATORS.map((op) => (
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
                placeholder="Value"
                disabled={disabled}
                size="sm"
                className="criteria-editor__value-input"
                aria-label={`Criterion ${index + 1} value`}
                data-field="value"
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
                    <Trash weight="bold" aria-hidden="true" />
                </button>
            </Tooltip>
        </li>
    );
}

export default CriteriaEditor;
