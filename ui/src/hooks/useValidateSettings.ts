import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import { isApiError } from '@/lib/api';

export interface IntegrationSettingsValidationIssue {
    key: string | null;

    message: string;
}

export interface IntegrationSettingsValidationResult {
    isValid: boolean;

    issues: IntegrationSettingsValidationIssue[];
}

export function extractValidationResultFromError(error: unknown): IntegrationSettingsValidationResult | null {
    if (!isApiError(error) || error.status !== 400) {
        return null;
    }
    const details = error.details;
    if (
        details &&
        typeof details === 'object' &&
        'issues' in (details as object) &&
        Array.isArray((details as IntegrationSettingsValidationResult).issues)
    ) {
        return details as IntegrationSettingsValidationResult;
    }
    return null;
}

function label(entry: IntegrationSettingDefinition): string {
    return entry.displayName?.trim() || entry.key;
}

function isBlank(value: string | undefined): boolean {
    return !value?.trim();
}

function isValidDate(value: string): boolean {
    return !Number.isNaN(Date.parse(value));
}

const DECIMAL_MAX_UNSCALED = 79_228_162_514_264_337_593_543_950_335n;

function isInvariantDecimal(value: string): boolean {
    let number = value.trim();
    if (!number) {
        return false;
    }

    const hasTrailingSign = /[+-]$/.test(number);
    if (hasTrailingSign) {
        number = number.slice(0, -1).trimEnd();
    }
    if (/^[+-]/.test(number)) {
        if (hasTrailingSign) {
            return false;
        }
        number = number.slice(1);
    }

    const parts = number.split('.');
    if (parts.length > 2) {
        return false;
    }

    const [integerPart, fractionalPart = ''] = parts;
    if (
        (integerPart && !/^\d[\d,]*$/.test(integerPart)) ||
        (fractionalPart && !/^\d+$/.test(fractionalPart)) ||
        (!integerPart && !fractionalPart)
    ) {
        return false;
    }

    let unscaled = BigInt(`${integerPart.replace(/,/g, '')}${fractionalPart}` || '0');
    let scale = fractionalPart.length;
    while (scale > 28 || unscaled > DECIMAL_MAX_UNSCALED) {
        if (scale === 0) {
            return false;
        }

        const remainder = unscaled % 10n;
        unscaled /= 10n;
        if (remainder > 5n || (remainder === 5n && unscaled % 2n !== 0n)) {
            unscaled += 1n;
        }
        scale -= 1;
    }

    return true;
}

function parseMappingRows(value: string): Record<string, string>[] | null {
    try {
        const parsed: unknown = JSON.parse(value);
        if (!Array.isArray(parsed)) {
            return null;
        }

        const rows: Record<string, string>[] = [];
        for (const entry of parsed) {
            if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) {
                return null;
            }

            const row: Record<string, string> = {};
            for (const [key, cell] of Object.entries(entry)) {
                row[key] = cell == null ? '' : typeof cell === 'string' ? cell : JSON.stringify(cell);
            }
            rows.push(row);
        }
        return rows;
    } catch {
        return null;
    }
}

function matchesType(value: string, entry: IntegrationSettingDefinition): boolean {
    switch (entry.type) {
        case SchemaDataType.String:
            return true;
        case SchemaDataType.Number:
            return isInvariantDecimal(value);
        case SchemaDataType.Boolean:
            return ['true', 'false', '1', '0'].includes(value.toLowerCase());
        case SchemaDataType.Date:
            return isValidDate(value);
        case SchemaDataType.MappingsList:
            return parseMappingRows(value) !== null;
        default:
            return false;
    }
}

function typeMismatchMessage(entry: IntegrationSettingDefinition): string {
    const fieldLabel = label(entry);
    switch (entry.type) {
        case SchemaDataType.String:
            return `${fieldLabel} is invalid.`;
        case SchemaDataType.Number:
            return `${fieldLabel} must be a number.`;
        case SchemaDataType.Boolean:
            return `${fieldLabel} must be true or false.`;
        case SchemaDataType.Date:
            return `${fieldLabel} must be a valid date.`;
        case SchemaDataType.MappingsList:
            return `${fieldLabel} must be a JSON array of mapping rows.`;
    }
}

function settingValue(settings: Record<string, string>, key: string): string | undefined {
    const matchingKey = Object.keys(settings).find((candidate) => candidate.toLowerCase() === key.toLowerCase());
    return matchingKey === undefined ? undefined : settings[matchingKey];
}

export function validateIntegrationSettings(
    schema: IntegrationSettingDefinition[],
    settings: Record<string, string>,
    preserveExistingSecrets: boolean,
): IntegrationSettingsValidationResult {
    const issues: IntegrationSettingsValidationIssue[] = [];

    for (const entry of schema) {
        const value = settingValue(settings, entry.key);
        if (entry.required && isBlank(value) && !(preserveExistingSecrets && entry.secret)) {
            issues.push({ key: entry.key, message: `${label(entry)} is required.` });
        }
    }

    for (const [key, value] of Object.entries(settings)) {
        const entry = schema.find((candidate) => candidate.key.toLowerCase() === key.toLowerCase());
        if (!entry || isBlank(value)) {
            continue;
        }
        if (!matchesType(value, entry)) {
            issues.push({ key: entry.key, message: typeMismatchMessage(entry) });
            continue;
        }
        if (entry.options?.length && !entry.options.some((option) => option.toLowerCase() === value.toLowerCase())) {
            issues.push({ key: entry.key, message: `${label(entry)} must be one of: ${entry.options.join(', ')}.` });
        }
        if (entry.type !== SchemaDataType.MappingsList || !entry.rowSchema?.length) {
            continue;
        }

        const rows = parseMappingRows(value);
        if (!rows) {
            continue;
        }
        rows.forEach((row, rowIndex) => {
            for (const column of entry.rowSchema ?? []) {
                if (!column.required || !isBlank(settingValue(row, column.key))) {
                    continue;
                }
                issues.push({
                    key: `${entry.key}[${rowIndex}].${column.key}`,
                    message: `${label(entry)} row ${rowIndex + 1}: ${label(column)} is required.`,
                });
            }
        });
    }

    return { isValid: issues.length === 0, issues };
}
