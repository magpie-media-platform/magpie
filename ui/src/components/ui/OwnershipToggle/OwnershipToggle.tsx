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

    return (
        <div
            className={classes}
            role="radiogroup"
            aria-label={ariaLabel}
            data-component="ownership-toggle"
            data-scope={value}
        >
            {OPTIONS.map(({ value: optionValue, label, icon: Icon }) => {
                const isActive = value === optionValue;
                return (
                    <button
                        key={optionValue}
                        type="button"
                        role="radio"
                        aria-checked={isActive}
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
