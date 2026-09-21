import { type ComponentPropsWithoutRef } from 'react';
import { CheckCircleIcon, CircleIcon } from '@phosphor-icons/react';

import './SelectionCircleToggle.css';

export interface SelectionCircleToggleProps extends Omit<ComponentPropsWithoutRef<'button'>, 'onClick' | 'children'> {
    selected: boolean;

    onToggle: () => void;
}

export function SelectionCircleToggle({ selected, onToggle, className, ...props }: SelectionCircleToggleProps) {
    const classes = ['selection-circle-toggle', selected && 'selection-circle-toggle--selected', className]
        .filter(Boolean)
        .join(' ');

    return (
        <button
            type="button"
            className={classes}
            aria-pressed={selected}
            onClick={(event) => {
                event.stopPropagation();
                onToggle();
            }}
            {...props}
        >
            {selected ? <CheckCircleIcon weight="fill" /> : <CircleIcon weight="bold" />}
        </button>
    );
}

SelectionCircleToggle.displayName = 'SelectionCircleToggle';
