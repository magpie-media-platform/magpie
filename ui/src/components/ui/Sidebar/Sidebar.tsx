import {
    createContext,
    forwardRef,
    useCallback,
    useContext,
    useMemo,
    useState,
    type ComponentPropsWithoutRef,
    type ComponentRef,
    type ReactNode,
} from 'react';
import { NavLink, type NavLinkProps } from 'react-router-dom';
import * as Collapsible from '@radix-ui/react-collapsible';
import { CaretDownIcon, CaretRightIcon } from '@phosphor-icons/react';

import { Popover, PopoverTrigger, PopoverContent } from '../Popover';

import { useMediaQuery } from '@/hooks/ui/useMediaQuery';

import './Sidebar.css';

interface SidebarContextValue {
    collapsed: boolean;
    setCollapsed: (collapsed: boolean) => void;
}

const SidebarContext = createContext<SidebarContextValue | null>(null);

function useSidebarContext() {
    const context = useContext(SidebarContext);
    if (!context) {
        throw new Error('Sidebar components must be used within a Sidebar');
    }
    return context;
}

export interface SidebarProps extends ComponentPropsWithoutRef<'aside'> {
    open?: boolean;

    collapsed?: boolean;

    onCollapsedChange?: (collapsed: boolean) => void;

    defaultCollapsed?: boolean;
}

const Sidebar = forwardRef<HTMLElement, SidebarProps>(
    (
        {
            className,
            open,
            collapsed: controlledCollapsed,
            onCollapsedChange,
            defaultCollapsed = false,
            children,
            ...props
        },
        ref,
    ) => {
        const [internalCollapsed, setInternalCollapsed] = useState(defaultCollapsed);

        const isDesktop = useMediaQuery('(min-width: 768px)');

        const collapsed = isDesktop ? (controlledCollapsed ?? internalCollapsed) : false;
        const setCollapsed = useCallback(
            (value: boolean) => {
                if (controlledCollapsed === undefined) setInternalCollapsed(value);
                onCollapsedChange?.(value);
            },
            [controlledCollapsed, onCollapsedChange],
        );

        const classes = ['sidebar', collapsed && 'sidebar--collapsed', className].filter(Boolean).join(' ');
        const contextValue = useMemo<SidebarContextValue>(
            () => ({ collapsed, setCollapsed }),
            [collapsed, setCollapsed],
        );

        return (
            <SidebarContext.Provider value={contextValue}>
                <aside ref={ref} className={classes} data-open={open} data-collapsed={collapsed} {...props}>
                    {children}
                </aside>
            </SidebarContext.Provider>
        );
    },
);

Sidebar.displayName = 'Sidebar';

export type SidebarContentProps = ComponentPropsWithoutRef<'div'>;

const SidebarContent = forwardRef<HTMLDivElement, SidebarContentProps>(({ className, ...props }, ref) => {
    const classes = ['sidebar-content', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

SidebarContent.displayName = 'SidebarContent';

export type SidebarFooterProps = ComponentPropsWithoutRef<'div'>;

const SidebarFooter = forwardRef<HTMLDivElement, SidebarFooterProps>(({ className, ...props }, ref) => {
    const classes = ['sidebar-footer', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

SidebarFooter.displayName = 'SidebarFooter';

export interface SidebarGroupProps extends ComponentPropsWithoutRef<typeof Collapsible.Root> {
    label?: string;
}

const SidebarGroup = forwardRef<ComponentRef<typeof Collapsible.Root>, SidebarGroupProps>(
    ({ className, label, children, defaultOpen = true, open: controlledOpen, onOpenChange, ...props }, ref) => {
        const [internalOpen, setInternalOpen] = useState(defaultOpen);
        const isOpen = controlledOpen ?? internalOpen;
        const setOpen = (value: boolean) => {
            if (controlledOpen === undefined) setInternalOpen(value);
            onOpenChange?.(value);
        };

        const classes = ['sidebar-group', className].filter(Boolean).join(' ');

        return (
            <Collapsible.Root ref={ref} className={classes} open={isOpen} onOpenChange={setOpen} {...props}>
                {label && (
                    <Collapsible.Trigger asChild>
                        <button type="button" className="sidebar-group-trigger" aria-label={label}>
                            <span className="sidebar-group-label">{label}</span>
                            <CaretDownIcon className="sidebar-group-icon" weight="bold" aria-hidden />
                        </button>
                    </Collapsible.Trigger>
                )}
                <Collapsible.Content className="sidebar-group-content">{children}</Collapsible.Content>
            </Collapsible.Root>
        );
    },
);

SidebarGroup.displayName = 'SidebarGroup';

export interface SidebarItemProps extends Omit<NavLinkProps, 'className' | 'children'> {
    icon?: ReactNode;

    active?: boolean;

    className?: string;

    children?: ReactNode;
}

const SidebarItem = forwardRef<HTMLAnchorElement, SidebarItemProps>(
    ({ className, icon, active, children, ...props }, ref) => {
        const { collapsed } = useSidebarContext();
        const classes = ['sidebar-item', active && 'sidebar-item--active', className].filter(Boolean).join(' ');

        return (
            <NavLink
                ref={ref}
                className={classes}
                data-active={active || undefined}
                aria-current={active ? 'page' : undefined}
                {...props}
            >
                {icon && <span className="sidebar-item-icon">{icon}</span>}
                {collapsed ? (
                    <span className="sr-only">{children}</span>
                ) : (
                    <span className="sidebar-item-label">{children}</span>
                )}
            </NavLink>
        );
    },
);

SidebarItem.displayName = 'SidebarItem';

export interface SidebarSubMenuProps extends ComponentPropsWithoutRef<typeof Collapsible.Root> {
    icon?: ReactNode;

    label: string;

    active?: boolean;

    collapsedTo?: NavLinkProps['to'];
}

const SidebarSubMenu = forwardRef<ComponentRef<typeof Collapsible.Root>, SidebarSubMenuProps>(
    ({ className, icon, label, active, children, defaultOpen, collapsedTo, ...props }, ref) => {
        const sidebar = useSidebarContext();
        const { collapsed } = sidebar;
        const expandedContext = useMemo(() => ({ ...sidebar, collapsed: false }), [sidebar]);
        const classes = ['sidebar-submenu', active && 'sidebar-submenu--active', className].filter(Boolean).join(' ');

        if (collapsed && !collapsedTo) {
            return (
                <div ref={ref} className={classes}>
                    <Popover>
                        <PopoverTrigger asChild>
                            <button
                                type="button"
                                className="sidebar-submenu-trigger sidebar-submenu-trigger--collapsed"
                                aria-label={label}
                            >
                                {icon ? <span className="sidebar-item-icon">{icon}</span> : label}
                            </button>
                        </PopoverTrigger>
                        <PopoverContent side="right" align="start" aria-label={label}>
                            <SidebarContext.Provider value={expandedContext}>{children}</SidebarContext.Provider>
                        </PopoverContent>
                    </Popover>
                </div>
            );
        }

        if (collapsed && collapsedTo) {
            const content = icon ? <span className="sidebar-item-icon">{icon}</span> : label;
            const triggerClassName = 'sidebar-submenu-trigger sidebar-submenu-trigger--collapsed';

            return (
                <div ref={ref as React.Ref<HTMLDivElement>} className={classes} data-active={active || undefined}>
                    <NavLink
                        to={collapsedTo}
                        end
                        className={triggerClassName}
                        aria-label={label}
                        data-active={active || undefined}
                    >
                        {content}
                    </NavLink>
                </div>
            );
        }

        return (
            <Collapsible.Root ref={ref} className={classes} defaultOpen={defaultOpen ?? active} {...props}>
                <Collapsible.Trigger asChild>
                    <button
                        type="button"
                        className="sidebar-submenu-trigger"
                        aria-label={label}
                        data-active={active || undefined}
                    >
                        {icon && <span className="sidebar-item-icon">{icon}</span>}
                        <span className="sidebar-item-label">{label}</span>
                        <CaretRightIcon className="sidebar-submenu-caret" weight="bold" aria-hidden />
                    </button>
                </Collapsible.Trigger>
                <Collapsible.Content className="sidebar-submenu-content">{children}</Collapsible.Content>
            </Collapsible.Root>
        );
    },
);

SidebarSubMenu.displayName = 'SidebarSubMenu';

export type SidebarSeparatorProps = ComponentPropsWithoutRef<'hr'>;

const SidebarSeparator = forwardRef<HTMLHRElement, SidebarSeparatorProps>(({ className, ...props }, ref) => {
    const classes = ['sidebar-separator', className].filter(Boolean).join(' ');

    return <hr ref={ref} className={classes} {...props} />;
});

SidebarSeparator.displayName = 'SidebarSeparator';

export {
    Sidebar,
    SidebarContent,
    SidebarFooter,
    SidebarGroup,
    SidebarItem,
    SidebarSubMenu,
    SidebarSeparator,
    useSidebarContext,
};

export default Sidebar;
