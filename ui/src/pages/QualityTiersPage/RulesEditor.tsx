import { useCallback, type ReactNode } from 'react';
import { PlusIcon, TrashIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';

import { CriteriaEditor, type Criterion } from './CriteriaEditor';

import './RulesEditor.css';
import './EditorControls.css';

export interface ExclusionDraft {
    id: string;
    name: string;
    criteria: Criterion[];
}

interface ExclusionsEditorProps {
    exclusions: ExclusionDraft[];

    onChange: (exclusions: ExclusionDraft[]) => void;

    label?: string;

    scopeLabel: string;

    helpText?: string;

    disabled?: boolean;
}

function generateExclusionId(): string {
    return crypto.randomUUID();
}

export function ExclusionsEditor({
    exclusions,
    onChange,
    label = 'Exclusions',
    scopeLabel,
    helpText,
    disabled = false,
}: ExclusionsEditorProps): ReactNode {
    const handleAddExclusion = useCallback(() => {
        const newExclusion: ExclusionDraft = {
            id: generateExclusionId(),
            name: '',
            criteria: [],
        };
        onChange([...exclusions, newExclusion]);
    }, [exclusions, onChange]);

    const handleRemoveExclusion = useCallback(
        (id: string) => {
            onChange(exclusions.filter((exclusion) => exclusion.id !== id));
        },
        [exclusions, onChange],
    );

    const handleNameChange = useCallback(
        (id: string, name: string) => {
            onChange(exclusions.map((exclusion) => (exclusion.id === id ? { ...exclusion, name } : exclusion)));
        },
        [exclusions, onChange],
    );

    const handleCriteriaChange = useCallback(
        (id: string, criteria: Criterion[]) => {
            onChange(exclusions.map((exclusion) => (exclusion.id === id ? { ...exclusion, criteria } : exclusion)));
        },
        [exclusions, onChange],
    );

    return (
        <div className="rules-editor" data-component="exclusions-editor">
            <div className="rules-editor__header">
                <span className="rules-editor__label">{label}</span>
                <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleAddExclusion}
                    disabled={disabled}
                    data-action="add-exclusion"
                >
                    <PlusIcon weight="bold" aria-hidden="true" />
                    Add Exclusion
                </Button>
            </div>

            <p className="rules-editor__scope">
                Exclusions apply to <strong>{scopeLabel}</strong>.{' '}
                {helpText ??
                    `Matching any exclusion prevents ${scopeLabel} from matching, even when its criteria match.`}
            </p>

            {exclusions.length === 0 ? (
                <p className="rules-editor__empty">No exclusions defined. No matching release is rejected here.</p>
            ) : (
                <ul className="rules-editor__list" role="list">
                    {exclusions.map((exclusion) => (
                        <li
                            key={exclusion.id}
                            className="rules-editor__rule"
                            role="listitem"
                            data-exclusion-id={exclusion.id}
                        >
                            <div className="rules-editor__rule-header">
                                <Input
                                    value={exclusion.name}
                                    onChange={(e) => handleNameChange(exclusion.id, e.target.value)}
                                    placeholder="Exclusion name (optional)"
                                    disabled={disabled}
                                    size="sm"
                                    className="rules-editor__name-input"
                                    aria-label="Exclusion name"
                                    data-field="name"
                                />
                                <button
                                    type="button"
                                    className="rules-editor__remove-btn"
                                    onClick={() => handleRemoveExclusion(exclusion.id)}
                                    disabled={disabled}
                                    aria-label="Remove exclusion"
                                    data-action="remove-exclusion"
                                    data-target={exclusion.id}
                                >
                                    <TrashIcon weight="bold" aria-hidden="true" />
                                </button>
                            </div>
                            <CriteriaEditor
                                criteria={exclusion.criteria}
                                onChange={(criteria) => handleCriteriaChange(exclusion.id, criteria)}
                                scopeNoun="this exclusion"
                                disabled={disabled}
                            />
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}

ExclusionsEditor.displayName = 'ExclusionsEditor';

export default ExclusionsEditor;
