import { forwardRef } from 'react';
import { FunnelXIcon } from '@phosphor-icons/react';

import { Button, type ButtonProps } from '../Button';

import './ClearFiltersButton.css';

export interface ClearFiltersButtonProps extends Omit<ButtonProps, 'asChild' | 'children' | 'variant'> {
    label?: string;
}

export const ClearFiltersButton = forwardRef<HTMLElement, ClearFiltersButtonProps>(
    ({ label = 'Clear filters', size = 'sm', className, ...props }, ref) => {
        const classes = ['clear-filters-button', className].filter(Boolean).join(' ');

        return (
            <Button ref={ref} variant="outline" size={size} className={classes} data-action="clear-filters" {...props}>
                <FunnelXIcon weight="bold" aria-hidden="true" />
                <span>{label}</span>
            </Button>
        );
    },
);

ClearFiltersButton.displayName = 'ClearFiltersButton';

export default ClearFiltersButton;
