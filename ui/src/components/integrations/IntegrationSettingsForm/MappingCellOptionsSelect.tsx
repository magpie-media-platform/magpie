import type { ReactNode } from 'react';

import { Button } from '@/components/ui/Button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { useOptionsSource, type OptionsSourceContext } from '@/hooks/useOptionsSource';
import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

type PickerStateKind = 'error' | 'not-configured' | 'loading' | 'empty';

interface OptionsFailure {
    userMessage?: unknown;
    notConfigured?: unknown;
}

function isOptionsFailure(error: unknown): error is OptionsFailure {
    return typeof error === 'object' && error !== null;
}

interface MappingCellOptionsSelectProps {
    column: IntegrationSettingDefinition;

    cellId: string;

    value: string;

    disabled: boolean;

    optionsSourceContext: OptionsSourceContext;

    onPickValue: (next: string) => void;
}

export function MappingCellOptionsSelect({
    column,
    cellId,
    value,
    disabled,
    optionsSourceContext,
    onPickValue,
}: MappingCellOptionsSelectProps): ReactNode {
    const query = useOptionsSource(column.optionsSource, optionsSourceContext);
    const options = query.data ?? [];
    const errored = query.isError;
    const loading = query.isPending || query.isFetching;
    const label = column.displayName ?? column.key;

    if (errored) {
        const failure: OptionsFailure = isOptionsFailure(query.error) ? query.error : {};
        const notConfigured = failure.notConfigured === true;
        const userMessage = typeof failure.userMessage === 'string' ? failure.userMessage.trim() : '';
        return (
            <PickerState
                column={column}
                cellId={cellId}
                value={value}
                label={label}
                message={
                    userMessage ||
                    (notConfigured
                        ? 'Complete the connection settings to load options.'
                        : 'Failed to load options. Check the source connection.')
                }
                state={notConfigured ? 'not-configured' : 'error'}
                onRetry={notConfigured ? undefined : () => void query.refetch()}
                disabled={disabled}
            />
        );
    }

    if (loading) {
        return (
            <PickerState
                column={column}
                cellId={cellId}
                value={value}
                label={label}
                message="Loading options..."
                state="loading"
                disabled={disabled}
            />
        );
    }

    if (options.length === 0) {
        return (
            <PickerState
                column={column}
                cellId={cellId}
                value={value}
                label={label}
                message="No options are available."
                state="empty"
                onRetry={() => void query.refetch()}
                disabled={disabled}
            />
        );
    }

    return (
        <Select value={value || undefined} onValueChange={onPickValue} disabled={disabled}>
            <SelectTrigger id={cellId} aria-label={label} data-field={column.key}>
                <SelectValue placeholder={column.placeholder ?? 'Select an option'} />
            </SelectTrigger>
            <SelectContent>
                {options.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                        {option.description ? `${option.label} (${option.description})` : option.label}
                    </SelectItem>
                ))}
            </SelectContent>
        </Select>
    );
}

interface PickerStateProps {
    column: IntegrationSettingDefinition;
    cellId: string;
    value: string;
    label: string;
    message: string;
    state: PickerStateKind;
    onRetry?: () => void;
    disabled: boolean;
}

function PickerState({ column, cellId, value, label, message, state, onRetry, disabled }: PickerStateProps): ReactNode {
    const errored = state === 'error';

    return (
        <div className="integration-settings-form__cell-fallback" data-picker-state={state}>
            <Select value={value || undefined} disabled>
                <SelectTrigger id={cellId} aria-label={label} data-field={column.key}>
                    <SelectValue placeholder={column.placeholder ?? 'Select an option'} />
                </SelectTrigger>
                <SelectContent />
            </Select>
            <p
                className={errored ? 'integration-settings-form__error' : 'integration-settings-form__help'}
                role={errored ? 'alert' : 'status'}
            >
                {message}
            </p>
            {onRetry && (
                <Button type="button" variant="ghost" size="sm" onClick={onRetry} disabled={disabled}>
                    Retry options
                </Button>
            )}
        </div>
    );
}
