import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { SidebarSimple } from '@phosphor-icons/react';

import { Button } from '../Button';

import './Header.css';

export interface HeaderProps extends ComponentPropsWithoutRef<'header'> {
    brand?: ReactNode;

    status?: ReactNode;

    onSidebarToggle?: () => void;

    sidebarOpen?: boolean;

    sidebarNavId?: string;

    slotContent?: ReactNode;

    leftSlot?: ReactNode;

    rightSlot?: ReactNode;

    actions?: ReactNode;
}

const Header = forwardRef<HTMLElement, HeaderProps>(
    (
        {
            brand,
            status,
            onSidebarToggle,
            sidebarOpen = false,
            sidebarNavId = 'navigation-menu',
            slotContent,
            leftSlot,
            rightSlot,
            actions,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['header', className].filter(Boolean).join(' ');

        return (
            <header ref={ref} className={classes} data-component="header" {...props}>
                <div className="header__container">
                    <div className="header__start">
                        {onSidebarToggle && (
                            <Button
                                variant="ghost"
                                size="md"
                                className="header__sidebar-toggle"
                                onClick={onSidebarToggle}
                                aria-label={sidebarOpen ? 'Collapse sidebar' : 'Expand sidebar'}
                                aria-expanded={sidebarOpen}
                                aria-controls={sidebarNavId}
                            >
                                <SidebarSimple size={22} weight="duotone" aria-hidden="true" />
                            </Button>
                        )}
                        {brand && <div className="header__brand">{brand}</div>}
                        {status && <div className="header__status">{status}</div>}
                        {leftSlot && <div className="header__left-slot">{leftSlot}</div>}
                    </div>

                    {slotContent && <div className="header__slot">{slotContent}</div>}

                    {(rightSlot || actions) && (
                        <div className="header__end">
                            {rightSlot && <div className="header__right-slot">{rightSlot}</div>}
                            {actions}
                        </div>
                    )}
                </div>
            </header>
        );
    },
);

Header.displayName = 'Header';

export { Header };
