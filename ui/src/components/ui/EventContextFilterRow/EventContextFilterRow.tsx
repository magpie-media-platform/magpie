import type { ReactNode } from 'react';
import { TrashIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Switch } from '@/components/ui/Switch';
import type { EditableEventContextCriterion } from '@/lib/eventContextCriteria';

import './EventContextFilterRow.css';

export interface EventContextFilterRowProps {
    index: number;
    filter: EditableEventContextCriterion;
    keyError?: string;
    patternError?: string;
    disabled?: boolean;
    onKeyChange: (index: number, key: string) => void;
    onPatternChange: (index: number, pattern: string) => void;
    onNegateChange: (index: number, negate: boolean) => void;
    onRemove: (index: number) => void;
}

export function EventContextFilterRow({
    index,
    filter,
    keyError,
    patternError,
    disabled,
    onKeyChange,
    onPatternChange,
    onNegateChange,
    onRemove,
}: EventContextFilterRowProps): ReactNode {
    return (
        <div className="event-context-filter-row" data-filter-index={index}>
            <div className="event-context-filter-row__key">
                <Input
                    value={filter.key}
                    onChange={(e) => onKeyChange(index, e.target.value)}
                    placeholder="Key"
                    error={!!keyError}
                    errorMessage={keyError}
                    disabled={disabled}
                    aria-label={`Filter ${index + 1} key`}
                    data-field={`filter-${index}-key`}
                />
            </div>
            <div className="event-context-filter-row__pattern">
                <Input
                    value={filter.pattern ?? ''}
                    onChange={(e) => onPatternChange(index, e.target.value)}
                    placeholder="Pattern (regex)"
                    error={!!patternError}
                    errorMessage={patternError}
                    disabled={disabled}
                    aria-label={`Filter ${index + 1} pattern`}
                    data-field={`filter-${index}-pattern`}
                />
            </div>
            <div className="event-context-filter-row__negate">
                <Switch
                    checked={filter.negate}
                    onCheckedChange={(negate) => onNegateChange(index, negate)}
                    disabled={disabled}
                    aria-label={`Filter ${index + 1} negate`}
                    data-field={`filter-${index}-negate`}
                />
                <span className="event-context-filter-row__negate-label">Negate</span>
            </div>
            <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => onRemove(index)}
                disabled={disabled}
                aria-label={`Remove filter ${index + 1}`}
                data-action={`remove-filter-${index}`}
                className="event-context-filter-row__remove"
            >
                <TrashIcon weight="bold" aria-hidden="true" />
            </Button>
        </div>
    );
}

EventContextFilterRow.displayName = 'EventContextFilterRow';

export default EventContextFilterRow;
