import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { NavLink, type NavLinkProps } from 'react-router-dom';

import './BottomNav.css';

export interface BottomNavButtonProps extends ComponentPropsWithoutRef<'button'> {
    icon: ReactNode;

    label: string;

    active?: boolean;

    showLabel?: boolean;
}

export interface BottomNavProps extends ComponentPropsWithoutRef<'nav'> {
    slotContent?: ReactNode;
}

const BottomNav = forwardRef<HTMLElement, BottomNavProps>(({ className, slotContent, children, ...props }, ref) => {
    const classes = ['bottom-nav', className].filter(Boolean).join(' ');

    return (
        <nav ref={ref} className={classes} aria-label="Primary navigation" data-component="bottom-nav" {...props}>
            <div className="bottom-nav__container">
                {children}
                {slotContent}
            </div>
        </nav>
    );
});

BottomNav.displayName = 'BottomNav';

export interface BottomNavItemProps extends Omit<NavLinkProps, 'className'> {
    icon: ReactNode;

    label: string;

    active?: boolean;

    showLabel?: boolean;

    className?: string;
}

const BottomNavItem = forwardRef<HTMLAnchorElement, BottomNavItemProps>(
    ({ className, icon, label, active, showLabel = true, ...props }, ref) => {
        const classes = ['bottom-nav-item', active && 'bottom-nav-item--active', className].filter(Boolean).join(' ');

        return (
            <NavLink
                ref={ref}
                className={classes}
                aria-current={active ? 'page' : undefined}
                data-active={active || undefined}
                data-show-label={showLabel}
                {...props}
            >
                <span className="bottom-nav-item__icon" aria-hidden="true">
                    {icon}
                </span>
                <span className={showLabel ? 'bottom-nav-item__label' : 'sr-only'}>{label}</span>
            </NavLink>
        );
    },
);

BottomNavItem.displayName = 'BottomNavItem';

const BottomNavButton = forwardRef<HTMLButtonElement, BottomNavButtonProps>(
    ({ className, icon, label, active, showLabel = true, ...props }, ref) => {
        const classes = ['bottom-nav-item', active && 'bottom-nav-item--active', className].filter(Boolean).join(' ');

        return (
            <button
                ref={ref}
                type="button"
                className={classes}
                aria-pressed={active}
                data-active={active || undefined}
                data-show-label={showLabel}
                {...props}
            >
                <span className="bottom-nav-item__icon" aria-hidden="true">
                    {icon}
                </span>
                <span className={showLabel ? 'bottom-nav-item__label' : 'sr-only'}>{label}</span>
            </button>
        );
    },
);

BottomNavButton.displayName = 'BottomNavButton';

export { BottomNav, BottomNavItem, BottomNavButton };
