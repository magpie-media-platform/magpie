import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { FunnelIcon, CheckIcon, XIcon, MagnifyingGlassIcon } from '@phosphor-icons/react';

import { Popover, PopoverTrigger, PopoverContent } from '../Popover';
import { Input } from '../Input';
import { Badge } from '../Badge';
import { Button } from '../Button';

import './MultiSelectColumnFilter.css';

export interface MultiSelectOption {
    value: string;
    label: string;
}

export interface MultiSelectColumnFilterProps {
    label: ReactNode;
    options: ReadonlyArray<MultiSelectOption | string>;
    value: string[];
    onChange: (next: string[]) => void;
    searchPlaceholder?: string;
    hideLabel?: boolean;
}

function normalizeOptions(options: ReadonlyArray<MultiSelectOption | string>): MultiSelectOption[] {
    return options.map((o) => (typeof o === 'string' ? { value: o, label: o } : o));
}

export function MultiSelectColumnFilter({
    label,
    options,
    value,
    onChange,
    searchPlaceholder,
    hideLabel,
}: MultiSelectColumnFilterProps) {
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const [activeIndex, setActiveIndex] = useState(0);
    const listId = useId();
    const optionId = (index: number) => `${listId}-option-${index}`;
    const activeRef = useRef<HTMLLIElement>(null);
    const searchRef = useRef<HTMLInputElement>(null);

    const allOptions = useMemo(() => normalizeOptions(options), [options]);
    const selectedSet = useMemo(() => new Set(value), [value]);

    const active = value.length > 0;
    const columnName = typeof label === 'string' ? label : 'column';
    const triggerLabel = active ? `${columnName} filtered (${value.length}), edit filter` : `Filter by ${columnName}`;

    const filtered = useMemo(() => {
        const q = query.trim().toLowerCase();
        if (q === '') {
            return allOptions;
        }
        return allOptions.filter((o) => o.label.toLowerCase().includes(q));
    }, [allOptions, query]);

    const toggle = (optionValue: string) => {
        if (selectedSet.has(optionValue)) {
            onChange(value.filter((v) => v !== optionValue));
        } else {
            onChange([...value, optionValue]);
        }
    };

    useEffect(() => {
        setActiveIndex((i) => (filtered.length === 0 ? 0 : Math.min(i, filtered.length - 1)));
    }, [filtered.length]);

    useEffect(() => {
        if (open) {
            setActiveIndex(0);
        }
    }, [open]);

    useEffect(() => {
        activeRef.current?.scrollIntoView({ block: 'nearest' });
    }, [activeIndex]);

    const handleListKeyDown = (e: KeyboardEvent) => {
        if (filtered.length === 0) {
            if (e.key === 'Escape') {
                setOpen(false);
            }
            return;
        }
        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                setActiveIndex((i) => (i + 1) % filtered.length);
                break;
            case 'ArrowUp':
                e.preventDefault();
                setActiveIndex((i) => (i - 1 + filtered.length) % filtered.length);
                break;
            case 'Home':
                e.preventDefault();
                setActiveIndex(0);
                break;
            case 'End':
                e.preventDefault();
                setActiveIndex(filtered.length - 1);
                break;
            case ' ':
                if (query !== '') {
                    return;
                }
                e.preventDefault();
                toggle(filtered[activeIndex].value);
                break;
            case 'Enter':
                e.preventDefault();
                toggle(filtered[activeIndex].value);
                break;
            case 'Escape':
                e.preventDefault();
                setOpen(false);
                break;
            default:
                break;
        }
    };

    return (
        <span className="multiselect-column-filter">
            <span className={hideLabel ? 'sr-only' : 'multiselect-column-filter__label'}>{label}</span>
            <Popover
                open={open}
                onOpenChange={(next) => {
                    setOpen(next);
                    if (!next) {
                        setQuery('');
                    }
                }}
            >
                <PopoverTrigger asChild>
                    <button
                        type="button"
                        className="multiselect-column-filter__trigger"
                        onClick={(e) => e.stopPropagation()}
                        onKeyDown={(e) => e.stopPropagation()}
                        aria-label={triggerLabel}
                        title={triggerLabel}
                        aria-pressed={active}
                        data-action="column-filter-multi"
                        data-active={active || undefined}
                    >
                        <FunnelIcon size="1rem" weight={active ? 'fill' : 'regular'} aria-hidden="true" />
                    </button>
                </PopoverTrigger>
                <PopoverContent
                    align="start"
                    className="multiselect-column-filter__menu"
                    onClick={(e) => e.stopPropagation()}
                    onOpenAutoFocus={(e) => {
                        e.preventDefault();
                        searchRef.current?.focus();
                    }}
                    onCloseAutoFocus={(e) => e.preventDefault()}
                >
                    <div className="multiselect-column-filter__body">
                        <Input
                            ref={searchRef}
                            size="sm"
                            value={query}
                            onChange={(e) => setQuery(e.target.value)}
                            placeholder={searchPlaceholder ?? `Search ${columnName.toLowerCase()}...`}
                            aria-label={`Search ${columnName}`}
                            data-field="multiselect-search"
                            aria-controls={listId}
                            aria-activedescendant={filtered.length > 0 ? optionId(activeIndex) : undefined}
                            leadingIcon={<MagnifyingGlassIcon size="1rem" weight="regular" aria-hidden="true" />}
                            onKeyDown={(e) => {
                                handleListKeyDown(e);
                                e.stopPropagation();
                            }}
                        />

                        {value.length > 0 && (
                            <div className="multiselect-column-filter__chips" data-testid="multiselect-chips">
                                {value.map((v) => {
                                    const opt = allOptions.find((o) => o.value === v);
                                    return (
                                        <Badge
                                            key={v}
                                            variant="default"
                                            size="sm"
                                            className="multiselect-column-filter__chip"
                                        >
                                            {opt?.label ?? v}
                                            <button
                                                type="button"
                                                className="multiselect-column-filter__chip-remove"
                                                onClick={() => toggle(v)}
                                                aria-label={`Remove ${opt?.label ?? v}`}
                                            >
                                                <XIcon weight="bold" aria-hidden="true" />
                                            </button>
                                        </Badge>
                                    );
                                })}
                            </div>
                        )}

                        <ul
                            id={listId}
                            className="multiselect-column-filter__options"
                            role="listbox"
                            aria-multiselectable="true"
                        >
                            {filtered.length === 0 ? (
                                <li className="multiselect-column-filter__empty">No matches</li>
                            ) : (
                                filtered.map((opt, index) => {
                                    const selected = selectedSet.has(opt.value);
                                    const isActive = index === activeIndex;
                                    return (
                                        <li
                                            key={opt.value}
                                            id={optionId(index)}
                                            ref={isActive ? activeRef : undefined}
                                            role="option"
                                            aria-selected={selected}
                                            className={`multiselect-column-filter__option${selected ? ' multiselect-column-filter__option--selected' : ''}${isActive ? ' multiselect-column-filter__option--active' : ''}`}
                                            data-value={opt.value}
                                            data-active={isActive || undefined}
                                            onMouseDown={(e) => e.preventDefault()}
                                            onClick={() => toggle(opt.value)}
                                            onMouseEnter={() => setActiveIndex(index)}
                                        >
                                            <span
                                                className="multiselect-column-filter__option-check"
                                                aria-hidden="true"
                                            >
                                                {selected && <CheckIcon size={14} weight="bold" />}
                                            </span>
                                            <span className="multiselect-column-filter__option-label">{opt.label}</span>
                                        </li>
                                    );
                                })
                            )}
                        </ul>

                        {value.length > 0 && (
                            <div className="multiselect-column-filter__actions">
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => onChange([])}
                                    data-action="clear-multi"
                                >
                                    <XIcon weight="bold" aria-hidden="true" />
                                    Clear
                                </Button>
                            </div>
                        )}
                    </div>
                </PopoverContent>
            </Popover>
        </span>
    );
}

MultiSelectColumnFilter.displayName = 'MultiSelectColumnFilter';

export default MultiSelectColumnFilter;
