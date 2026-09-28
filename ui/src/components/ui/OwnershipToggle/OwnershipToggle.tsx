import { useRef } from 'react';
import { UsersThreeIcon, UserIcon } from '@phosphor-icons/react';

export type OwnershipScope = 'all' | 'mine';

export interface OwnershipToggleProps {
    value: OwnershipScope;

    onValueChange: (value: OwnershipScope) => void;

    'aria-label'?: string;

    'data-filter'?: string;

    className?: string;
}

const OPTIONS: { value: OwnershipScope; label: string; icon: typeof UserIcon }[] = [
    { value: 'all', label: 'All', icon: UsersThreeIcon },
    { value: 'mine', label: 'Mine', icon: UserIcon },
];

export function OwnershipToggle({
    value,
    onValueChange,
    'aria-label': ariaLabel = 'Filter by ownership',
    'data-filter': dataFilter,
    className,
}: OwnershipToggleProps) {
    const classes = ['ownership-toggle', 'tabs-list', className].filter(Boolean).join(' ');
    const optionRefs = useRef<(HTMLButtonElement | null)[]>([]);

    return (
        <div
            className={classes}
            role="radiogroup"
            aria-label={ariaLabel}
            data-component="ownership-toggle"
            data-scope={value}
        >
            {OPTIONS.map(({ value: optionValue, label, icon: Icon }, index) => {
                const isActive = value === optionValue;
                return (
                    <button
                        key={optionValue}
                        ref={(node) => {
                            optionRefs.current[index] = node;
                        }}
                        type="button"
                        role="radio"
                        aria-checked={isActive}
                        tabIndex={isActive ? 0 : -1}
                        onKeyDown={(event) => {
                            const direction =
                                event.key === 'ArrowRight' || event.key === 'ArrowDown'
                                    ? 1
                                    : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
                                      ? -1
                                      : 0;
                            if (!direction) return;
                            event.preventDefault();
                            const next = (index + direction + OPTIONS.length) % OPTIONS.length;
                            onValueChange(OPTIONS[next].value);
                            optionRefs.current[next]?.focus();
                        }}
                        className="ownership-toggle__option tabs-trigger"
                        data-state={isActive ? 'active' : 'inactive'}
                        data-value={optionValue}
                        data-filter={dataFilter}
                        onClick={() => onValueChange(optionValue)}
                    >
                        <Icon weight="bold" aria-hidden="true" />
                        <span>{label}</span>
                    </button>
                );
            })}
        </div>
    );
}

OwnershipToggle.displayName = 'OwnershipToggle';

export default OwnershipToggle;
