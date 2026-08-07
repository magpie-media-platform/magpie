import type { ReactNode } from 'react';

import { Button } from '@/components/ui/Button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { useOptionsSource, type OptionsSourceContext } from '@/hooks/useOptionsSource';
import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

const JELLYFIN_LIBRARY_OPTIONS_FUNCTION = 'magpieJellyfin.listLibraries';

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
    const jellyfinPicker =
        column.optionsSource?.kind === 'JSFunction' &&
        column.optionsSource.functionName === JELLYFIN_LIBRARY_OPTIONS_FUNCTION;
    const missingJellyfinCredentials =
        jellyfinPicker &&
        !optionsSourceContext.entityId &&
        (!optionsSourceContext.state.host?.trim() || !optionsSourceContext.state.apiKey?.trim());
    const query = useOptionsSource(column.optionsSource, optionsSourceContext, !missingJellyfinCredentials);
    const options = query.data ?? [];
    const errored = query.isError;
    const loading = query.isPending || query.isFetching;
    const label = column.displayName ?? column.key;

    if (jellyfinPicker) {
        if (missingJellyfinCredentials) {
            return (
                <PickerState
                    column={column}
                    cellId={cellId}
                    value={value}
                    label={label}
                    message="Enter the Jellyfin host URL and API key to load libraries."
                    state="missing-credentials"
                    disabled={disabled}
                />
            );
        }

        if (errored) {
            const authenticationFailure =
                query.error instanceof Error && query.error.name === 'JellyfinAuthenticationError';
            return (
                <PickerState
                    column={column}
                    cellId={cellId}
                    value={value}
                    label={label}
                    message={
                        authenticationFailure
                            ? 'Jellyfin rejected the API key. Check the credentials and retry.'
                            : 'Failed to connect to Jellyfin. Check the host and network, then retry.'
                    }
                    state={authenticationFailure ? 'authentication-error' : 'network-error'}
                    onRetry={() => void query.refetch()}
                    retryLabel="Retry libraries"
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
                    message="Loading Jellyfin libraries..."
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
                    message="No Jellyfin libraries were found. Add a library or verify its access, then retry."
                    state="empty"
                    onRetry={() => void query.refetch()}
                    retryLabel="Retry libraries"
                    disabled={disabled}
                />
            );
        }
    }

    if (errored) {
        return (
            <PickerState
                column={column}
                cellId={cellId}
                value={value}
                label={label}
                message="Failed to load options. Check the source connection."
                state="error"
                onRetry={() => void query.refetch()}
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
    state: string;
    onRetry?: () => void;
    retryLabel?: string;
    disabled: boolean;
}

function PickerState({
    column,
    cellId,
    value,
    label,
    message,
    state,
    onRetry,
    retryLabel = 'Retry options',
    disabled,
}: PickerStateProps): ReactNode {
    const errored = state === 'authentication-error' || state === 'network-error' || state === 'error';

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
                    {retryLabel}
                </Button>
            )}
        </div>
    );
}
