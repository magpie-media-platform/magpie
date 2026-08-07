import { useCallback, type ChangeEvent, type ReactNode } from 'react';

import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { SecretInput } from '@/components/ui/SecretInput';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Switch } from '@/components/ui/Switch';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { MappingsListEditor } from './MappingsListEditor';

const NONE_VALUE = '__none__';

interface SettingFieldInputProps {
    entry: IntegrationSettingDefinition;

    inputId: string;

    value: string;

    disabled: boolean;

    hasError: boolean;

    describedBy?: string;

    label: string;

    secretIsSet: boolean;

    onChange: (key: string, value: string) => void;

    onFieldBlur?: (key: string) => void;

    extensionId: string | null;

    entityId: string | null;

    allValues: Record<string, string>;
}

export function SettingFieldInput({
    entry,
    inputId,
    value,
    disabled,
    hasError,
    describedBy,
    label,
    secretIsSet,
    onChange,
    onFieldBlur,
    extensionId,
    entityId,
    allValues,
}: SettingFieldInputProps): ReactNode {
    const required = !!entry.required;
    const handleStringChange = useCallback(
        (e: ChangeEvent<HTMLInputElement>) => {
            onChange(entry.key, e.target.value);
        },
        [entry.key, onChange],
    );
    const handleBlur = useCallback(() => {
        onFieldBlur?.(entry.key);
    }, [entry.key, onFieldBlur]);
    const handleSelectChange = useCallback(
        (next: string) => {
            if (next === NONE_VALUE && !required) {
                onChange(entry.key, '');
                return;
            }
            onChange(entry.key, next);
        },
        [entry.key, onChange, required],
    );
    const handleSwitchChange = useCallback(
        (checked: boolean) => {
            onChange(entry.key, checked ? 'true' : 'false');
        },
        [entry.key, onChange],
    );

    if (entry.type === SchemaDataType.MappingsList) {
        return (
            <MappingsListEditor
                entry={entry}
                value={value}
                disabled={disabled}
                inputId={inputId}
                onChange={onChange}
                extensionId={extensionId}
                entityId={entityId}
                allValues={allValues}
            />
        );
    }

    if (entry.type === SchemaDataType.Boolean) {
        const checked = value === 'true';
        return (
            <div className="integration-settings-form__toggle-wrapper">
                <Switch
                    id={inputId}
                    checked={checked}
                    onCheckedChange={handleSwitchChange}
                    disabled={disabled}
                    error={hasError}
                    data-field={entry.key}
                    aria-describedby={describedBy}
                />
                <Label htmlFor={inputId} error={hasError} required={required}>
                    {label}
                </Label>
            </div>
        );
    }

    if (entry.type === SchemaDataType.String && entry.options && entry.options.length > 0) {
        const placeholder = entry.placeholder ?? 'Select an option';
        return (
            <Select
                value={value || (required ? undefined : NONE_VALUE)}
                onValueChange={handleSelectChange}
                disabled={disabled}
            >
                <SelectTrigger
                    id={inputId}
                    error={hasError}
                    aria-label={label}
                    aria-describedby={describedBy}
                    data-field={entry.key}
                >
                    <SelectValue placeholder={placeholder} />
                </SelectTrigger>
                <SelectContent>
                    {!required && (
                        <SelectItem value={NONE_VALUE}>
                            <span aria-label="None">{placeholder}</span>
                        </SelectItem>
                    )}
                    {entry.options.map((option) => (
                        <SelectItem key={option} value={option}>
                            {option}
                        </SelectItem>
                    ))}
                </SelectContent>
            </Select>
        );
    }

    const sharedProps = {
        id: inputId,
        value,
        onChange: handleStringChange,
        onBlur: handleBlur,
        disabled,
        error: hasError,
        placeholder: entry.placeholder,
        'aria-describedby': describedBy,
        'data-field': entry.key,
    };

    if (entry.type === SchemaDataType.Number) {
        return <Input {...sharedProps} type="number" inputMode="numeric" />;
    }

    if (entry.type === SchemaDataType.Date) {
        return <Input {...sharedProps} type="date" />;
    }

    if (entry.type === SchemaDataType.String && entry.secret) {
        return <SecretInput {...sharedProps} configured={secretIsSet} fieldLabel={label} />;
    }

    return <Input {...sharedProps} type="text" />;
}
