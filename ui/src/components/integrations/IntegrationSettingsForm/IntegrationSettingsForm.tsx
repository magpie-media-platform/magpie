import { useMemo, type ReactNode } from 'react';

import { Label } from '@/components/ui/Label';
import { SchemaDataType, isRenderableSetting, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import type { IntegrationSettingsValidationIssue } from '@/hooks/useValidateSettings';

import { SettingFieldInput } from './SettingFieldInput';

import './IntegrationSettingsForm.css';

export interface IntegrationSettingsFormProps {
    schema: IntegrationSettingDefinition[];

    values: Record<string, string>;

    onChange: (key: string, value: string) => void;

    secretsConfigured?: Record<string, boolean>;

    issues?: IntegrationSettingsValidationIssue[];

    disabled?: boolean;

    idPrefix?: string;

    extensionId?: string | null;

    entityId?: string | null;

    onFieldBlur?: (key: string) => void;
}

function fieldHasIssue(issues: IntegrationSettingsValidationIssue[], key: string): boolean {
    return issues.some((issue) => issue.key === key);
}

function fieldIssues(issues: IntegrationSettingsValidationIssue[], key: string): IntegrationSettingsValidationIssue[] {
    return issues.filter((issue) => issue.key === key);
}

function formLevelIssues(issues: IntegrationSettingsValidationIssue[]): IntegrationSettingsValidationIssue[] {
    return issues.filter((issue) => issue.key == null);
}

const EMPTY_ISSUES: IntegrationSettingsValidationIssue[] = [];

const EMPTY_SECRETS_CONFIGURED: Record<string, boolean> = {};

export function IntegrationSettingsForm({
    schema,
    values,
    onChange,
    secretsConfigured = EMPTY_SECRETS_CONFIGURED,
    issues = EMPTY_ISSUES,
    disabled = false,
    idPrefix = 'integration-setting',
    extensionId = null,
    entityId = null,
    onFieldBlur,
}: IntegrationSettingsFormProps): ReactNode {
    const formIssues = useMemo(() => formLevelIssues(issues), [issues]);

    const renderableEntries = useMemo(() => schema.filter(isRenderableSetting), [schema]);

    return (
        <div className="integration-settings-form" data-component="integration-settings-form">
            {renderableEntries.map((entry) => {
                const inputId = `${idPrefix}-${entry.key}`;
                const helpId = entry.description ? `${inputId}-help` : undefined;
                const issuesForField = fieldIssues(issues, entry.key);
                const errorId = issuesForField.length > 0 ? `${inputId}-error` : undefined;
                const describedBy = [helpId, errorId].filter(Boolean).join(' ') || undefined;
                const hasError = fieldHasIssue(issues, entry.key);
                const label = entry.displayName ?? entry.key;
                const value = values[entry.key] ?? '';

                return (
                    <SettingFieldShell
                        key={entry.key}
                        entry={entry}
                        inputId={inputId}
                        helpId={helpId}
                        errorId={errorId}
                        hasError={hasError}
                        label={label}
                        issuesForField={issuesForField}
                    >
                        <SettingFieldInput
                            entry={entry}
                            inputId={inputId}
                            value={value}
                            disabled={disabled}
                            hasError={hasError}
                            describedBy={describedBy}
                            label={label}
                            secretIsSet={secretsConfigured[entry.key] ?? false}
                            onChange={onChange}
                            onFieldBlur={onFieldBlur}
                            extensionId={extensionId}
                            entityId={entityId}
                            allValues={values}
                        />
                    </SettingFieldShell>
                );
            })}

            {formIssues.length > 0 && (
                <div role="alert">
                    <ul
                        className="integration-settings-form__form-issues"
                        data-field="integration-settings-form-issues"
                        role="list"
                    >
                        {formIssues.map((issue, index) => (
                            <li key={index} className="integration-settings-form__error">
                                {issue.message}
                            </li>
                        ))}
                    </ul>
                </div>
            )}
        </div>
    );
}

IntegrationSettingsForm.displayName = 'IntegrationSettingsForm';

interface SettingFieldShellProps {
    entry: IntegrationSettingDefinition;

    inputId: string;

    helpId?: string;

    errorId?: string;

    hasError: boolean;

    label: string;

    issuesForField: IntegrationSettingsValidationIssue[];

    children: ReactNode;
}

function SettingFieldShell({
    entry,
    inputId,
    helpId,
    errorId,
    hasError,
    label,
    issuesForField,
    children,
}: SettingFieldShellProps): ReactNode {
    const isToggle = entry.type === SchemaDataType.Boolean;

    return (
        <div
            className={
                isToggle
                    ? 'integration-settings-form__field integration-settings-form__field--toggle'
                    : 'integration-settings-form__field'
            }
            data-setting-key={entry.key}
        >
            {!isToggle && (
                <Label htmlFor={inputId} error={hasError} required={!!entry.required}>
                    {label}
                </Label>
            )}
            {children}
            {entry.description && (
                <p id={helpId} className="integration-settings-form__help">
                    {entry.description}
                </p>
            )}
            {issuesForField.length > 0 && (
                <p id={errorId} className="integration-settings-form__error" role="alert">
                    {issuesForField.map((issue) => issue.message).join(' ')}
                </p>
            )}
        </div>
    );
}

export default IntegrationSettingsForm;
