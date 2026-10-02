import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { NavLink, type NavLinkProps } from 'react-router-dom';

import './BottomNav.css';

export type BottomNavProps = ComponentPropsWithoutRef<'nav'>;

const BottomNav = forwardRef<HTMLElement, BottomNavProps>(({ className, children, ...props }, ref) => {
    const classes = ['bottom-nav', className].filter(Boolean).join(' ');

    return (
        <nav ref={ref} className={classes} aria-label="Primary navigation" data-component="bottom-nav" {...props}>
            <div className="bottom-nav__container">{children}</div>
        </nav>
    );
});

BottomNav.displayName = 'BottomNav';

export interface BottomNavItemProps extends Omit<NavLinkProps, 'className'> {
    icon: ReactNode;

    label: string;

    active?: boolean;

    className?: string;
}

const BottomNavItem = forwardRef<HTMLAnchorElement, BottomNavItemProps>(
    ({ className, icon, label, active, ...props }, ref) => {
        const classes = ['bottom-nav-item', active && 'bottom-nav-item--active', className].filter(Boolean).join(' ');

        return (
            <NavLink
                ref={ref}
                className={classes}
                aria-current={active ? 'page' : undefined}
                data-active={active || undefined}
                {...props}
            >
                <span className="bottom-nav-item__icon" aria-hidden="true">
                    {icon}
                </span>
                <span className="bottom-nav-item__label">{label}</span>
            </NavLink>
        );
    },
);

BottomNavItem.displayName = 'BottomNavItem';

export { BottomNav, BottomNavItem };
