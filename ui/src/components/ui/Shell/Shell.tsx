/* eslint-disable react-refresh/only-export-components */
import {
    createContext,
    forwardRef,
    useCallback,
    useContext,
    useEffect,
    useRef,
    useState,
    type ComponentPropsWithoutRef,
} from 'react';

import './Shell.css';

interface ShellContextValue {
    sidebarOpen: boolean;
    setSidebarOpen: (open: boolean) => void;
    sidebarCollapsed: boolean;
    setSidebarCollapsed: (collapsed: boolean) => void;
    toggleSidebarOpen: () => void;
    toggleSidebarCollapsed: () => void;
}

const ShellContext = createContext<ShellContextValue | null>(null);

function useShellContext() {
    const context = useContext(ShellContext);
    if (!context) {
        throw new Error('Shell components must be used within a Shell');
    }
    return context;
}

export interface ShellProps extends ComponentPropsWithoutRef<'div'> {
    classPrefix?: string;

    defaultSidebarCollapsed?: boolean;

    sidebarCollapsed?: boolean;

    onSidebarCollapsedChange?: (collapsed: boolean) => void;
}

const Shell = forwardRef<HTMLDivElement, ShellProps>(
    (
        {
            className,
            classPrefix = 'shell',
            defaultSidebarCollapsed = false,
            sidebarCollapsed: controlledCollapsed,
            onSidebarCollapsedChange,
            children,
            ...props
        },
        ref,
    ) => {
        const [sidebarOpen, setSidebarOpen] = useState(false);
        const [internalCollapsed, setInternalCollapsed] = useState(defaultSidebarCollapsed);

        const sidebarCollapsed = controlledCollapsed ?? internalCollapsed;
        const setSidebarCollapsed = useCallback(
            (collapsed: boolean) => {
                setInternalCollapsed(collapsed);
                onSidebarCollapsedChange?.(collapsed);
            },
            [onSidebarCollapsedChange],
        );

        const toggleSidebarOpen = useCallback(() => {
            setSidebarOpen((prev) => !prev);
        }, []);

        const toggleSidebarCollapsed = useCallback(() => {
            setInternalCollapsed((prev) => {
                const next = !prev;
                onSidebarCollapsedChange?.(next);
                return next;
            });
        }, [onSidebarCollapsedChange]);

        const classes = [classPrefix, className].filter(Boolean).join(' ');

        return (
            <ShellContext.Provider
                value={{
                    sidebarOpen,
                    setSidebarOpen,
                    sidebarCollapsed,
                    setSidebarCollapsed,
                    toggleSidebarOpen,
                    toggleSidebarCollapsed,
                }}
            >
                <div
                    ref={ref}
                    className={classes}
                    data-component={classPrefix}
                    data-sidebar-open={sidebarOpen}
                    data-sidebar-collapsed={sidebarCollapsed}
                    {...props}
                >
                    {children}
                </div>
            </ShellContext.Provider>
        );
    },
);

Shell.displayName = 'Shell';

export interface ShellHeaderProps extends ComponentPropsWithoutRef<'div'> {
    classPrefix?: string;
}

const ShellHeader = forwardRef<HTMLDivElement, ShellHeaderProps>(
    ({ className, classPrefix = 'shell', ...props }, ref) => {
        const classes = [`${classPrefix}__header`, className].filter(Boolean).join(' ');
        const innerRef = useRef<HTMLDivElement>(null);

        useEffect(() => {
            const el = innerRef.current;
            if (!el) {
                return;
            }

            const update = () => {
                const shell = el.closest(`.${classPrefix}`);
                if (shell instanceof HTMLElement) {
                    shell.style.setProperty('--header-height', `${el.offsetHeight}px`);
                }
            };

            update();

            const observer = new ResizeObserver(update);
            observer.observe(el);
            return () => observer.disconnect();
        }, [classPrefix]);

        return (
            <div
                ref={(node) => {
                    (innerRef as React.MutableRefObject<HTMLDivElement | null>).current = node;
                    if (typeof ref === 'function') {
                        ref(node);
                    } else if (ref) {
                        (ref as React.MutableRefObject<HTMLDivElement | null>).current = node;
                    }
                }}
                className={classes}
                data-component={`${classPrefix}-header`}
                {...props}
            />
        );
    },
);

ShellHeader.displayName = 'ShellHeader';

export interface ShellSidebarProps extends ComponentPropsWithoutRef<'nav'> {
    ariaLabel?: string;
    navId?: string;
    classPrefix?: string;
}

const ShellSidebar = forwardRef<HTMLElement, ShellSidebarProps>(
    (
        {
            className,
            ariaLabel = 'Main navigation',
            navId = 'navigation-menu',
            classPrefix = 'shell',
            children,
            ...props
        },
        ref,
    ) => {
        const { sidebarOpen, setSidebarOpen, sidebarCollapsed } = useShellContext();
        const classes = [`${classPrefix}__sidebar`, className].filter(Boolean).join(' ');

        return (
            <>
                <div
                    className={`${classPrefix}__sidebar-backdrop`}
                    data-visible={sidebarOpen}
                    onClick={() => setSidebarOpen(false)}
                    aria-hidden="true"
                />
                <nav
                    ref={ref}
                    className={classes}
                    data-component={`${classPrefix}-sidebar`}
                    id={navId}
                    aria-label={ariaLabel}
                    data-open={sidebarOpen}
                    data-collapsed={sidebarCollapsed}
                    {...props}
                >
                    {children}
                </nav>
            </>
        );
    },
);

ShellSidebar.displayName = 'ShellSidebar';

export interface ShellMainProps extends ComponentPropsWithoutRef<'main'> {
    mainId?: string;
    classPrefix?: string;
}

const ShellMain = forwardRef<HTMLElement, ShellMainProps>(
    ({ className, mainId = 'main-content', classPrefix = 'shell', children, ...props }, ref) => {
        const classes = [`${classPrefix}__main`, className].filter(Boolean).join(' ');

        return (
            <main
                ref={ref}
                id={mainId}
                className={classes}
                data-component={`${classPrefix}-main`}
                tabIndex={-1}
                {...props}
            >
                <div className={`${classPrefix}__main-wrapper`}>{children}</div>
            </main>
        );
    },
);

ShellMain.displayName = 'ShellMain';

export interface ShellBottomNavProps extends ComponentPropsWithoutRef<'div'> {
    classPrefix?: string;
}

const ShellBottomNav = forwardRef<HTMLDivElement, ShellBottomNavProps>(
    ({ className, classPrefix = 'shell', ...props }, ref) => {
        const classes = [`${classPrefix}__bottom-nav`, className].filter(Boolean).join(' ');

        return <div ref={ref} className={classes} data-component={`${classPrefix}-bottom-nav`} {...props} />;
    },
);

ShellBottomNav.displayName = 'ShellBottomNav';

function useShell() {
    const context = useShellContext();

    const closeSidebar = useCallback(() => {
        context.setSidebarOpen(false);
    }, [context]);

    const toggleSidebar = useCallback(() => {
        if (window.matchMedia('(min-width: 768px)').matches) {
            context.toggleSidebarCollapsed();
        } else {
            context.toggleSidebarOpen();
        }
    }, [context]);

    return {
        sidebarOpen: context.sidebarOpen,
        sidebarCollapsed: context.sidebarCollapsed,
        toggleSidebar,
        toggleSidebarCollapsed: context.toggleSidebarCollapsed,
        closeSidebar,
        setSidebarOpen: context.setSidebarOpen,
        setSidebarCollapsed: context.setSidebarCollapsed,
    };
}

export { Shell, ShellHeader, ShellSidebar, ShellMain, ShellBottomNav, useShell };

export default Shell;
