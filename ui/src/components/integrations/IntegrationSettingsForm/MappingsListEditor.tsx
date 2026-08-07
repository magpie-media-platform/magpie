import { useCallback, useMemo, useState, type ReactNode } from 'react';

import { Button } from '@/components/ui/Button';
import type { OptionsSourceContext } from '@/hooks/useOptionsSource';
import type { IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { MappingCellInput } from './MappingCellInput';
import { parseMappingRows } from './parseMappingRows';

interface MappingsListEditorProps {
    entry: IntegrationSettingDefinition;

    value: string;

    disabled: boolean;

    inputId: string;

    onChange: (key: string, value: string) => void;

    extensionId: string | null;

    entityId: string | null;

    allValues: Record<string, string>;
}

export function MappingsListEditor({
    entry,
    value,
    disabled,
    inputId,
    onChange,
    extensionId,
    entityId,
    allValues,
}: MappingsListEditorProps): ReactNode {
    const rows = useMemo(() => parseMappingRows(value), [value]);
    const columns = useMemo<IntegrationSettingDefinition[]>(() => entry.rowSchema ?? [], [entry.rowSchema]);

    const [originalRowCount, setOriginalRowCount] = useState(() => rows.length);

    const handleCellChange = useCallback(
        (rowIndex: number, columnKey: string, nextValue: string) => {
            const next = rows.map((row, i) => (i === rowIndex ? { ...row, [columnKey]: nextValue } : row));
            onChange(entry.key, JSON.stringify(next));
        },
        [rows, entry.key, onChange],
    );

    const handleAddRow = useCallback(() => {
        const blank: Record<string, string> = {};
        for (const column of columns) {
            blank[column.key] = column.defaultValue ?? '';
        }
        const next = [...rows, blank];
        onChange(entry.key, JSON.stringify(next));
    }, [rows, columns, entry.key, onChange]);

    const handleRemoveRow = useCallback(
        (rowIndex: number) => {
            setOriginalRowCount((prev) => (rowIndex < prev ? prev - 1 : prev));
            const next = rows.filter((_, i) => i !== rowIndex);
            onChange(entry.key, JSON.stringify(next));
        },
        [rows, entry.key, onChange],
    );

    const optionsSourceContext = useMemo<OptionsSourceContext>(
        () => ({ extensionId, entityId, state: allValues }),
        [extensionId, entityId, allValues],
    );

    if (columns.length === 0) {
        return (
            <p className="integration-settings-form__help" id={inputId}>
                This setting has no row definition, so its mappings cannot be edited.
            </p>
        );
    }

    return (
        <div
            className="integration-settings-form__mappings"
            data-field={entry.key}
            data-field-kind="mappings-list"
            id={inputId}
        >
            <table className="integration-settings-form__mappings-table" role="grid">
                <thead>
                    <tr>
                        {columns.map((column) => (
                            <th key={column.key} scope="col">
                                {column.displayName ?? column.key}
                                {column.required && (
                                    <span aria-hidden="true" className="integration-settings-form__required-marker">
                                        {' '}
                                        *
                                    </span>
                                )}
                            </th>
                        ))}
                        <th scope="col" aria-label="Actions" />
                    </tr>
                </thead>
                <tbody>
                    {rows.map((row, rowIndex) => (
                        <tr key={rowIndex} data-row-index={rowIndex}>
                            {columns.map((column) => (
                                <td key={column.key} data-label={column.displayName ?? column.key}>
                                    <MappingCellInput
                                        column={column}
                                        rowIndex={rowIndex}
                                        value={row[column.key] ?? ''}
                                        disabled={disabled}
                                        configured={rowIndex < originalRowCount}
                                        onChange={handleCellChange}
                                        optionsSourceContext={optionsSourceContext}
                                    />
                                </td>
                            ))}
                            <td>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => handleRemoveRow(rowIndex)}
                                    disabled={disabled}
                                    data-action="remove-row"
                                    aria-label={`Remove row ${rowIndex + 1}`}
                                >
                                    Remove
                                </Button>
                            </td>
                        </tr>
                    ))}
                    {rows.length === 0 && (
                        <tr>
                            <td colSpan={columns.length + 1} className="integration-settings-form__mappings-empty">
                                No rows configured. Click &ldquo;Add row&rdquo; to add one.
                            </td>
                        </tr>
                    )}
                </tbody>
            </table>
            <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleAddRow}
                disabled={disabled}
                data-action="add-row"
            >
                Add row
            </Button>
        </div>
    );
}
