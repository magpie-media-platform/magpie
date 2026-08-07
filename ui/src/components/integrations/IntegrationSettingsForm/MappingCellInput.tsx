import { useCallback, type ChangeEvent, type ReactNode } from 'react';

import { Input } from '@/components/ui/Input';
import { SecretInput } from '@/components/ui/SecretInput';
import { Switch } from '@/components/ui/Switch';
import type { OptionsSourceContext } from '@/hooks/useOptionsSource';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { MappingCellOptionsSelect } from './MappingCellOptionsSelect';

interface MappingCellInputProps {
    column: IntegrationSettingDefinition;

    rowIndex: number;

    value: string;

    disabled: boolean;

    configured: boolean;

    onChange: (rowIndex: number, columnKey: string, nextValue: string) => void;

    optionsSourceContext: OptionsSourceContext;
}

export function MappingCellInput({
    column,
    rowIndex,
    value,
    disabled,
    configured,
    onChange,
    optionsSourceContext,
}: MappingCellInputProps): ReactNode {
    const cellId = `mapping-${column.key}-${rowIndex}`;
    const handleString = useCallback(
        (e: ChangeEvent<HTMLInputElement>) => {
            onChange(rowIndex, column.key, e.target.value);
        },
        [rowIndex, column.key, onChange],
    );
    const handleSwitch = useCallback(
        (checked: boolean) => {
            onChange(rowIndex, column.key, checked ? 'true' : 'false');
        },
        [rowIndex, column.key, onChange],
    );

    if (column.optionsSource) {
        return (
            <MappingCellOptionsSelect
                column={column}
                cellId={cellId}
                value={value}
                disabled={disabled}
                optionsSourceContext={optionsSourceContext}
                onPickValue={(next) => onChange(rowIndex, column.key, next)}
            />
        );
    }

    if (column.type === SchemaDataType.Boolean) {
        return (
            <Switch
                id={cellId}
                checked={value === 'true'}
                onCheckedChange={handleSwitch}
                disabled={disabled}
                aria-label={column.displayName ?? column.key}
                data-field={column.key}
            />
        );
    }

    const shared = {
        id: cellId,
        value,
        onChange: handleString,
        disabled,
        placeholder: column.placeholder,
        'aria-label': column.displayName ?? column.key,
        'data-field': column.key,
    };

    if (column.type === SchemaDataType.Number) {
        return <Input {...shared} type="number" inputMode="numeric" />;
    }
    if (column.type === SchemaDataType.Date) {
        return <Input {...shared} type="date" />;
    }
    if (column.type === SchemaDataType.String && column.secret) {
        return <SecretInput {...shared} configured={configured} fieldLabel={column.displayName ?? column.key} />;
    }
    return <Input {...shared} type="text" />;
}
