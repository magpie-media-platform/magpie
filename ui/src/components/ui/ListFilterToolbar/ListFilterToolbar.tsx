import { Fragment, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type SyntheticEvent } from 'react';
import { FunnelIcon, XIcon } from '@phosphor-icons/react';
import type { SortingState } from '@tanstack/react-table';

import { Button } from '@/components/ui/Button';
import {
    BETWEEN_DATES_PRESET,
    ColumnHeaderFilter,
    type DateRangeValue,
    type FacetOption,
} from '@/components/ui/ColumnHeaderFilter';
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
import { useCardLayout } from '@/hooks/ui/useCardLayout';

import './ListFilterToolbar.css';

export type ListFilterValue = string | string[] | { min: string; max: string } | DateRangeValue;
export type ListFilterValues = Record<string, ListFilterValue>;

export type ListFilterField =
    | { id: string; label: string; kind: 'facet'; value: string; options: FacetOption[] }
    | { id: string; label: string; kind: 'multi'; value: string[]; options: FacetOption[] }
    | { id: string; label: string; kind: 'text'; value: string }
    | { id: string; label: string; kind: 'numberRange'; value: { min: string; max: string }; unit?: string }
    | { id: string; label: string; kind: 'dateRange'; value: DateRangeValue; presets: FacetOption[] };

export interface ListFilterToolbarProps {
    fields: ListFilterField[];
    onCommit: (values: ListFilterValues) => void;
    sorting: SortingState;
    sortOptions: readonly MobileListSortOption[];
    onSortingChange: (sorting: SortingState) => void;
    selection?: MobileListSelectionControl;
    className?: string;
    dialogClearAll?: boolean;
    pillMultiSelect?: boolean;
}

function isActive(value: ListFilterValue): boolean {
    if (typeof value === 'string') return value !== '';
    if (Array.isArray(value)) return value.length > 0;
    if ('preset' in value) return value.preset !== '' || value.from !== '' || value.to !== '';
    return value.min !== '' || value.max !== '';
}

function emptyValue(field: ListFilterField): ListFilterValue {
    if (field.kind === 'multi') return [];
    if (field.kind === 'numberRange') return { min: '', max: '' };
    if (field.kind === 'dateRange') return { preset: '', from: '', to: '' };
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
    dialogClearAll = false,
    pillMultiSelect = false,
}: ListFilterToolbarProps) {
    const formId = useId();
    const usesCardLayout = useCardLayout();
    const toolbarRef = useRef<HTMLDivElement>(null);
    const [inline, setInline] = useState(false);
    const [open, setOpen] = useState(false);
    const values = useMemo<ListFilterValues>(
        () => Object.fromEntries(fields.map((field) => [field.id, field.value])),
        [fields],
    );
    const [draft, setDraft] = useState<ListFilterValues>({});
    const activeCount = fields.filter((field) => isActive(field.value)).length;

    useEffect(() => {
        if (!usesCardLayout) setOpen(false);
    }, [usesCardLayout]);

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
                    {fields.map((field) => (
                        <Fragment key={field.id}>
                            {field.kind === 'multi' && (
                                <MultiSelectColumnFilter
                                    label={field.label}
                                    options={field.options}
                                    value={field.value}
                                    onChange={(value) => commitField(field.id, value)}
                                />
                            )}
                            {field.kind === 'facet' && (
                                <ColumnHeaderFilter
                                    type="facet"
                                    label={field.label}
                                    active={isActive(field.value)}
                                    options={field.options}
                                    value={field.value}
                                    onChange={(value) => commitField(field.id, value)}
                                />
                            )}
                            {field.kind === 'text' && (
                                <ColumnHeaderFilter
                                    type="text"
                                    label={field.label}
                                    active={isActive(field.value)}
                                    value={field.value}
                                    onChange={(value) => commitField(field.id, value)}
                                />
                            )}
                            {field.kind === 'numberRange' && (
                                <ColumnHeaderFilter
                                    type="numberRange"
                                    label={field.label}
                                    active={isActive(field.value)}
                                    min={field.value.min}
                                    max={field.value.max}
                                    unit={field.unit}
                                    onChange={(value) => commitField(field.id, value)}
                                />
                            )}
                            {field.kind === 'dateRange' && (
                                <ColumnHeaderFilter
                                    type="dateRange"
                                    label={field.label}
                                    active={isActive(field.value)}
                                    preset={field.value.preset}
                                    presets={field.presets}
                                    from={field.value.from}
                                    to={field.value.to}
                                    onChange={(value) => commitField(field.id, value)}
                                />
                            )}
                        </Fragment>
                    ))}
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
                            <span>
                                <span className="list-filter-toolbar__compact-label">Filters</span>
                                {activeCount > 0 ? ` (${activeCount})` : ''}
                            </span>
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
                                    const range = field.kind === 'dateRange' ? (value as DateRangeValue) : null;
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
                                            {field.kind === 'multi' && pillMultiSelect && (
                                                <MultiSelectColumnFilter
                                                    label={field.label}
                                                    hideLabel
                                                    trigger="field"
                                                    options={field.options}
                                                    value={value as string[]}
                                                    onChange={(selected) =>
                                                        setDraft({ ...draft, [field.id]: selected })
                                                    }
                                                />
                                            )}
                                            {field.kind === 'multi' && !pillMultiSelect && (
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
                                            {field.kind === 'dateRange' && range && (
                                                <>
                                                    <select
                                                        id={`${formId}-${field.id}`}
                                                        className="list-filter-toolbar__select"
                                                        value={range.preset}
                                                        onChange={(event) =>
                                                            setDraft({
                                                                ...draft,
                                                                [field.id]: {
                                                                    preset: event.target.value,
                                                                    from: '',
                                                                    to: '',
                                                                },
                                                            })
                                                        }
                                                    >
                                                        <option value="">All</option>
                                                        {[...field.presets, BETWEEN_DATES_PRESET].map((option) => (
                                                            <option key={option.value} value={option.value}>
                                                                {option.label}
                                                            </option>
                                                        ))}
                                                    </select>
                                                    {range.preset === BETWEEN_DATES_PRESET.value && (
                                                        <>
                                                            <Input
                                                                size="sm"
                                                                type="date"
                                                                aria-label={`${field.label} from`}
                                                                value={range.from}
                                                                onChange={(event) =>
                                                                    setDraft({
                                                                        ...draft,
                                                                        [field.id]: {
                                                                            ...range,
                                                                            from: event.target.value,
                                                                        },
                                                                    })
                                                                }
                                                            />
                                                            <Input
                                                                size="sm"
                                                                type="date"
                                                                aria-label={`${field.label} to`}
                                                                value={range.to}
                                                                onChange={(event) =>
                                                                    setDraft({
                                                                        ...draft,
                                                                        [field.id]: {
                                                                            ...range,
                                                                            to: event.target.value,
                                                                        },
                                                                    })
                                                                }
                                                            />
                                                        </>
                                                    )}
                                                </>
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
                                {dialogClearAll ? (
                                    <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => {
                                            clearAll();
                                            setOpen(false);
                                        }}
                                    >
                                        Clear all
                                    </Button>
                                ) : (
                                    <Button variant="ghost" size="sm" onClick={() => setOpen(false)}>
                                        Cancel
                                    </Button>
                                )}
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
            {usesCardLayout && (
                <MobileListToolbar
                    sorting={sorting}
                    sortOptions={sortOptions}
                    onSortingChange={onSortingChange}
                    filterActions={controls}
                    selection={selection}
                />
            )}
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
                            if (field.kind === 'dateRange') {
                                const { preset, from, to } = field.value;
                                if (preset !== '' && preset !== BETWEEN_DATES_PRESET.value) {
                                    return [
                                        {
                                            key: field.id,
                                            label: `${field.label}: ${field.presets.find((option) => option.value === preset)?.label ?? preset}`,
                                            remove: () => commitField(field.id, emptyValue(field)),
                                        },
                                    ];
                                }
                                return [
                                    from
                                        ? {
                                              key: `${field.id}-from`,
                                              label: `${field.label} from ${from}`,
                                              remove: () => commitField(field.id, { ...field.value, from: '' }),
                                          }
                                        : null,
                                    to
                                        ? {
                                              key: `${field.id}-to`,
                                              label: `${field.label} to ${to}`,
                                              remove: () => commitField(field.id, { ...field.value, to: '' }),
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
