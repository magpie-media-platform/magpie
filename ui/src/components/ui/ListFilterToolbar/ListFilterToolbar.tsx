import { useId, useLayoutEffect, useMemo, useRef, useState, type SyntheticEvent } from 'react';
import { FunnelIcon, XIcon } from '@phosphor-icons/react';
import type { SortingState } from '@tanstack/react-table';

import { Button } from '@/components/ui/Button';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import {
    Dialog,
    DialogBody,
    DialogContent,
    DialogFooter,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
} from '@/components/ui/Dialog';
import { Input } from '@/components/ui/Input';
import {
    MobileListToolbar,
    type MobileListSelectionControl,
    type MobileListSortOption,
} from '@/components/ui/MobileListToolbar';
import { MultiSelectColumnFilter } from '@/components/ui/MultiSelectColumnFilter';

import './ListFilterToolbar.css';

export type ListFilterValue = string | string[] | { min: string; max: string };
export type ListFilterValues = Record<string, ListFilterValue>;

export type ListFilterField =
    | { id: string; label: string; kind: 'facet'; value: string; options: FacetOption[] }
    | { id: string; label: string; kind: 'multi'; value: string[]; options: FacetOption[] }
    | { id: string; label: string; kind: 'text'; value: string }
    | { id: string; label: string; kind: 'numberRange'; value: { min: string; max: string }; unit?: string };

export interface ListFilterToolbarProps {
    fields: ListFilterField[];
    onCommit: (values: ListFilterValues) => void;
    sorting: SortingState;
    sortOptions: readonly MobileListSortOption[];
    onSortingChange: (sorting: SortingState) => void;
    selection?: MobileListSelectionControl;
    className?: string;
}

function isActive(value: ListFilterValue): boolean {
    if (typeof value === 'string') return value !== '';
    if (Array.isArray(value)) return value.length > 0;
    return value.min !== '' || value.max !== '';
}

function emptyValue(field: ListFilterField): ListFilterValue {
    if (field.kind === 'multi') return [];
    if (field.kind === 'numberRange') return { min: '', max: '' };
    return '';
}

export function ListFilterToolbar({
    fields,
    onCommit,
    sorting,
    sortOptions,
    onSortingChange,
    selection,
    className,
}: ListFilterToolbarProps) {
    const formId = useId();
    const toolbarRef = useRef<HTMLDivElement>(null);
    const [inline, setInline] = useState(false);
    const [open, setOpen] = useState(false);
    const values = useMemo<ListFilterValues>(
        () => Object.fromEntries(fields.map((field) => [field.id, field.value])),
        [fields],
    );
    const [draft, setDraft] = useState<ListFilterValues>({});
    const activeCount = fields.filter((field) => isActive(field.value)).length;

    useLayoutEffect(() => {
        const toolbar = toolbarRef.current;
        if (!toolbar) return;

        const measure = () => {
            const nextInline = toolbar.getBoundingClientRect().width >= (fields.length > 2 ? 1000 : 760);
            setInline(nextInline);
            if (nextInline) setOpen(false);
        };
        measure();
        window.addEventListener('resize', measure);
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
        observer?.observe(toolbar);
        return () => {
            observer?.disconnect();
            window.removeEventListener('resize', measure);
        };
    }, [fields.length]);

    const commitField = (id: string, value: ListFilterValue) => onCommit({ ...values, [id]: value });
    const clearAll = () => onCommit(Object.fromEntries(fields.map((field) => [field.id, emptyValue(field)])));
    const changeOpen = (next: boolean) => {
        if (next) setDraft(values);
        setOpen(next);
    };
    const apply = (event: SyntheticEvent<HTMLFormElement>) => {
        event.preventDefault();
        onCommit(draft);
        setOpen(false);
    };

    const controls = (
        <div className="list-filter-toolbar__controls">
            {inline && (
                <div className="list-filter-toolbar__inline" role="group" aria-label="List filters">
                    {fields.map((field) =>
                        field.kind === 'multi' ? (
                            <MultiSelectColumnFilter
                                key={field.id}
                                label={field.label}
                                options={field.options}
                                value={field.value}
                                onChange={(value) => commitField(field.id, value)}
                            />
                        ) : field.kind === 'facet' ? (
                            <ColumnHeaderFilter
                                key={field.id}
                                type="facet"
                                label={field.label}
                                active={isActive(field.value)}
                                options={field.options}
                                value={field.value}
                                onChange={(value) => commitField(field.id, value)}
                            />
                        ) : field.kind === 'text' ? (
                            <ColumnHeaderFilter
                                key={field.id}
                                type="text"
                                label={field.label}
                                active={isActive(field.value)}
                                value={field.value}
                                onChange={(value) => commitField(field.id, value)}
                            />
                        ) : (
                            <ColumnHeaderFilter
                                key={field.id}
                                type="numberRange"
                                label={field.label}
                                active={isActive(field.value)}
                                min={field.value.min}
                                max={field.value.max}
                                unit={field.unit}
                                onChange={(value) => commitField(field.id, value)}
                            />
                        ),
                    )}
                </div>
            )}
            {!inline && (
                <Dialog open={open} onOpenChange={changeOpen}>
                    <DialogTrigger asChild>
                        <Button
                            variant="secondary"
                            size="sm"
                            className="list-filter-toolbar__compact"
                            aria-label={`Filters, ${activeCount} active`}
                        >
                            <FunnelIcon weight="bold" aria-hidden="true" />
                            Filters{activeCount > 0 ? ` (${activeCount})` : ''}
                        </Button>
                    </DialogTrigger>
                    <DialogContent aria-describedby={undefined}>
                        <DialogHeader>
                            <DialogTitle>Filters</DialogTitle>
                        </DialogHeader>
                        <form onSubmit={apply} className="list-filter-toolbar__form">
                            <DialogBody>
                                {fields.map((field) => {
                                    const value = draft[field.id] ?? field.value;
                                    return (
                                        <div key={field.id} className="list-filter-toolbar__field">
                                            <label
                                                className="list-filter-toolbar__label"
                                                htmlFor={`${formId}-${field.id}`}
                                            >
                                                {field.label}
                                                {field.kind === 'numberRange' && field.unit ? ` (${field.unit})` : ''}
                                            </label>
                                            {field.kind === 'facet' && (
                                                <select
                                                    id={`${formId}-${field.id}`}
                                                    className="list-filter-toolbar__select"
                                                    value={value as string}
                                                    onChange={(event) =>
                                                        setDraft({ ...draft, [field.id]: event.target.value })
                                                    }
                                                >
                                                    <option value="">All</option>
                                                    {field.options.map((option) => (
                                                        <option key={option.value} value={option.value}>
                                                            {option.label}
                                                        </option>
                                                    ))}
                                                </select>
                                            )}
                                            {field.kind === 'text' && (
                                                <Input
                                                    id={`${formId}-${field.id}`}
                                                    size="sm"
                                                    value={value as string}
                                                    onChange={(event) =>
                                                        setDraft({ ...draft, [field.id]: event.target.value })
                                                    }
                                                />
                                            )}
                                            {field.kind === 'multi' && (
                                                <div
                                                    className="list-filter-toolbar__choices"
                                                    role="group"
                                                    aria-label={field.label}
                                                >
                                                    {field.options.map((option) => {
                                                        const selected = value as string[];
                                                        return (
                                                            <label
                                                                key={option.value}
                                                                className="list-filter-toolbar__choice"
                                                            >
                                                                <input
                                                                    type="checkbox"
                                                                    checked={selected.includes(option.value)}
                                                                    onChange={(event) =>
                                                                        setDraft({
                                                                            ...draft,
                                                                            [field.id]: event.target.checked
                                                                                ? [...selected, option.value]
                                                                                : selected.filter(
                                                                                      (item) => item !== option.value,
                                                                                  ),
                                                                        })
                                                                    }
                                                                />
                                                                {option.label}
                                                            </label>
                                                        );
                                                    })}
                                                </div>
                                            )}
                                            {field.kind === 'numberRange' && (
                                                <div className="list-filter-toolbar__range">
                                                    <Input
                                                        id={`${formId}-${field.id}`}
                                                        size="sm"
                                                        type="number"
                                                        step="any"
                                                        aria-label={`${field.label} minimum`}
                                                        placeholder="Minimum"
                                                        value={(value as { min: string; max: string }).min}
                                                        onChange={(event) =>
                                                            setDraft({
                                                                ...draft,
                                                                [field.id]: {
                                                                    ...(value as { min: string; max: string }),
                                                                    min: event.target.value,
                                                                },
                                                            })
                                                        }
                                                    />
                                                    <Input
                                                        size="sm"
                                                        type="number"
                                                        step="any"
                                                        aria-label={`${field.label} maximum`}
                                                        placeholder="Maximum"
                                                        value={(value as { min: string; max: string }).max}
                                                        onChange={(event) =>
                                                            setDraft({
                                                                ...draft,
                                                                [field.id]: {
                                                                    ...(value as { min: string; max: string }),
                                                                    max: event.target.value,
                                                                },
                                                            })
                                                        }
                                                    />
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </DialogBody>
                            <DialogFooter>
                                <Button variant="ghost" size="sm" onClick={() => setOpen(false)}>
                                    Cancel
                                </Button>
                                <Button variant="primary" size="sm" type="submit">
                                    Apply
                                </Button>
                            </DialogFooter>
                        </form>
                    </DialogContent>
                </Dialog>
            )}
        </div>
    );

    return (
        <div
            ref={toolbarRef}
            className={['list-filter-toolbar', className].filter(Boolean).join(' ')}
            data-component="list-filter-toolbar"
        >
            <MobileListToolbar
                sorting={sorting}
                sortOptions={sortOptions}
                onSortingChange={onSortingChange}
                filterActions={controls}
                selection={selection}
            />
            {activeCount > 0 && (
                <div className="list-filter-toolbar__active" aria-label="Active filters">
                    {fields
                        .flatMap((field) => {
                            if (field.kind === 'multi') {
                                return field.value.map((item) => ({
                                    key: `${field.id}-${item}`,
                                    label: `${field.label}: ${field.options.find((option) => option.value === item)?.label ?? item}`,
                                    remove: () =>
                                        commitField(
                                            field.id,
                                            field.value.filter((value) => value !== item),
                                        ),
                                }));
                            }
                            if (field.kind === 'numberRange') {
                                return [
                                    field.value.min
                                        ? {
                                              key: `${field.id}-min`,
                                              label: `${field.label} ≥ ${field.value.min}${field.unit ?? ''}`,
                                              remove: () => commitField(field.id, { ...field.value, min: '' }),
                                          }
                                        : null,
                                    field.value.max
                                        ? {
                                              key: `${field.id}-max`,
                                              label: `${field.label} ≤ ${field.value.max}${field.unit ?? ''}`,
                                              remove: () => commitField(field.id, { ...field.value, max: '' }),
                                          }
                                        : null,
                                ].filter(Boolean) as { key: string; label: string; remove: () => void }[];
                            }
                            if (!field.value) return [];
                            const display =
                                field.kind === 'facet'
                                    ? (field.options.find((option) => option.value === field.value)?.label ??
                                      field.value)
                                    : field.value;
                            return [
                                {
                                    key: field.id,
                                    label: `${field.label}: ${display}`,
                                    remove: () => commitField(field.id, ''),
                                },
                            ];
                        })
                        .map((chip) => (
                            <Button
                                key={chip.key}
                                variant="ghost"
                                size="sm"
                                onClick={chip.remove}
                                aria-label={`Remove ${chip.label} filter`}
                                className="list-filter-toolbar__chip"
                            >
                                {chip.label}
                                <XIcon weight="bold" aria-hidden="true" />
                            </Button>
                        ))}
                    <Button variant="ghost" size="sm" onClick={clearAll}>
                        Clear all
                    </Button>
                </div>
            )}
        </div>
    );
}
