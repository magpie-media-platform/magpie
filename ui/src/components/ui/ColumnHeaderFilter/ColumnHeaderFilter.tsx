import { useState, useId, useCallback, type ReactNode } from 'react';
import { FunnelIcon, XIcon } from '@phosphor-icons/react';

import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuLabel,
    DropdownMenuRadioGroup,
    DropdownMenuRadioItem,
    DropdownMenuSeparator,
} from '../DropdownMenu';
import { Input } from '../Input';
import { Label } from '../Label';
import { Button } from '../Button';

import './ColumnHeaderFilter.css';

export interface FacetOption {
    value: string;
    label: string;
}

interface BaseProps {
    label: ReactNode;
    active: boolean;
    hideLabel?: boolean;
    advanced?: boolean;
}

interface FacetFilterProps extends BaseProps {
    type: 'facet';
    options: FacetOption[];
    value: string;
    onChange: (value: string) => void;
    allLabel?: string;
    hideAll?: boolean;
    disabled?: boolean;
}

interface TextFilterProps extends BaseProps {
    type: 'text';
    value: string;
    onChange: (value: string) => void;
    placeholder?: string;
}

interface DateRangeFilterProps extends BaseProps {
    type: 'dateRange';
    from: string;
    to: string;
    onChange: (range: { from: string; to: string }) => void;
}

interface NumberRangeFilterProps extends BaseProps {
    type: 'numberRange';
    min: string;
    max: string;
    unit?: string;
    onChange: (range: { min: string; max: string }) => void;
}

export type ColumnHeaderFilterProps =
    | FacetFilterProps
    | TextFilterProps
    | DateRangeFilterProps
    | NumberRangeFilterProps;

const FACET_ANY = '__any__';

export function ColumnHeaderFilter(props: ColumnHeaderFilterProps) {
    const { label, hideLabel } = props;
    const active = props.active || Boolean(props.advanced);
    const [open, setOpen] = useState(false);

    const disabled = props.advanced || (props.type === 'facet' && Boolean(props.disabled));
    const columnName = typeof label === 'string' ? label : 'column';
    const triggerLabel = props.advanced
        ? `${columnName} uses advanced MQL; edit the query`
        : disabled
          ? `${columnName} filter locked`
          : active
            ? `${columnName} filtered, edit filter`
            : `Filter by ${columnName}`;

    const trigger = (
        <DropdownMenuTrigger asChild>
            <button
                type="button"
                className="column-header-filter__trigger"
                aria-label={triggerLabel}
                title={triggerLabel}
                aria-pressed={active}
                disabled={disabled}
                data-action="column-filter"
                data-active={active || undefined}
            >
                <FunnelIcon size="1rem" weight={active ? 'fill' : 'regular'} aria-hidden="true" />
            </button>
        </DropdownMenuTrigger>
    );

    return (
        <span
            className="column-header-filter"
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => e.stopPropagation()}
        >
            <span className={hideLabel ? 'sr-only' : 'column-header-filter__label'}>{label}</span>
            <DropdownMenu open={open} onOpenChange={setOpen}>
                {trigger}
                <DropdownMenuContent
                    align="start"
                    className="column-header-filter__menu"
                    onClick={(e) => e.stopPropagation()}
                >
                    {props.type === 'facet' && <FacetBody {...props} onClose={() => setOpen(false)} />}
                    {props.type === 'text' && <TextBody {...props} onClose={() => setOpen(false)} />}
                    {props.type === 'dateRange' && <DateRangeBody {...props} onClose={() => setOpen(false)} />}
                    {props.type === 'numberRange' && <NumberRangeBody {...props} onClose={() => setOpen(false)} />}
                </DropdownMenuContent>
            </DropdownMenu>
        </span>
    );
}

function FacetBody({
    options,
    value,
    onChange,
    label,
    allLabel = 'Any',
    hideAll = false,
}: FacetFilterProps & { onClose: () => void }) {
    return (
        <>
            <DropdownMenuLabel>{label}</DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuRadioGroup
                value={value || FACET_ANY}
                onValueChange={(v) => onChange(v === FACET_ANY ? '' : v)}
            >
                {!hideAll && <DropdownMenuRadioItem value={FACET_ANY}>{allLabel}</DropdownMenuRadioItem>}
                {options.map((opt) => (
                    <DropdownMenuRadioItem key={opt.value} value={opt.value}>
                        {opt.label}
                    </DropdownMenuRadioItem>
                ))}
            </DropdownMenuRadioGroup>
        </>
    );
}

function TextBody({ value, onChange, placeholder, label, onClose }: TextFilterProps & { onClose: () => void }) {
    const inputId = useId();
    const [draft, setDraft] = useState(value);

    const apply = useCallback(() => {
        onChange(draft.trim());
        onClose();
    }, [draft, onChange, onClose]);

    return (
        <div className="column-header-filter__form">
            <Label htmlFor={inputId}>{label}</Label>
            <Input
                id={inputId}
                size="sm"
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                placeholder={placeholder ?? 'Contains...'}
                onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        apply();
                    }
                }}
            />
            <div className="column-header-filter__actions">
                <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                        setDraft('');
                        onChange('');
                        onClose();
                    }}
                >
                    <XIcon weight="bold" aria-hidden="true" />
                    Clear
                </Button>
                <Button variant="primary" size="sm" onClick={apply}>
                    Apply
                </Button>
            </div>
        </div>
    );
}

function DateRangeBody({ from, to, onChange, label, onClose }: DateRangeFilterProps & { onClose: () => void }) {
    const inputId = useId();
    const [draftFrom, setDraftFrom] = useState(from);
    const [draftTo, setDraftTo] = useState(to);

    const apply = useCallback(() => {
        onChange({ from: draftFrom, to: draftTo });
        onClose();
    }, [draftFrom, draftTo, onChange, onClose]);

    return (
        <form
            className="column-header-filter__form"
            onSubmit={(event) => {
                event.preventDefault();
                apply();
            }}
        >
            <Label htmlFor={`${inputId}-from`}>{label} from</Label>
            <Input
                id={`${inputId}-from`}
                size="sm"
                type="date"
                value={draftFrom}
                onChange={(e) => setDraftFrom(e.target.value)}
            />
            <Label htmlFor={`${inputId}-to`}>{label} to</Label>
            <Input
                id={`${inputId}-to`}
                size="sm"
                type="date"
                value={draftTo}
                onChange={(e) => setDraftTo(e.target.value)}
            />
            <div className="column-header-filter__actions">
                <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                        setDraftFrom('');
                        setDraftTo('');
                        onChange({ from: '', to: '' });
                        onClose();
                    }}
                >
                    <XIcon weight="bold" aria-hidden="true" />
                    Clear
                </Button>
                <Button type="submit" variant="primary" size="sm">
                    Apply
                </Button>
            </div>
        </form>
    );
}

function NumberRangeBody({
    min,
    max,
    unit,
    onChange,
    label,
    onClose,
}: NumberRangeFilterProps & { onClose: () => void }) {
    const inputId = useId();
    const [draftMin, setDraftMin] = useState(min);
    const [draftMax, setDraftMax] = useState(max);

    const apply = useCallback(() => {
        onChange({ min: draftMin, max: draftMax });
        onClose();
    }, [draftMax, draftMin, onChange, onClose]);

    return (
        <form
            className="column-header-filter__form"
            onSubmit={(event) => {
                event.preventDefault();
                apply();
            }}
        >
            <Label htmlFor={`${inputId}-min`}>
                {label} minimum{unit ? ` (${unit})` : ''}
            </Label>
            <Input
                id={`${inputId}-min`}
                size="sm"
                type="number"
                step="any"
                min="0"
                value={draftMin}
                onChange={(e) => setDraftMin(e.target.value)}
            />
            <Label htmlFor={`${inputId}-max`}>
                {label} maximum{unit ? ` (${unit})` : ''}
            </Label>
            <Input
                id={`${inputId}-max`}
                size="sm"
                type="number"
                step="any"
                min="0"
                value={draftMax}
                onChange={(e) => setDraftMax(e.target.value)}
            />
            <div className="column-header-filter__actions">
                <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                        setDraftMin('');
                        setDraftMax('');
                        onChange({ min: '', max: '' });
                        onClose();
                    }}
                >
                    <XIcon weight="bold" aria-hidden="true" />
                    Clear
                </Button>
                <Button type="submit" variant="primary" size="sm">
                    Apply
                </Button>
            </div>
        </form>
    );
}

ColumnHeaderFilter.displayName = 'ColumnHeaderFilter';

export default ColumnHeaderFilter;
