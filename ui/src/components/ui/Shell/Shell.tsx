import {
    createContext,
    forwardRef,
    useCallback,
    useContext,
    useEffect,
    useMemo,
    useRef,
    useState,
    type ComponentPropsWithoutRef,
} from 'react';
import { useLocation } from 'react-router-dom';

import { useMediaQuery } from '@/hooks/ui/useMediaQuery';

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
                if (controlledCollapsed === undefined) setInternalCollapsed(collapsed);
                onSidebarCollapsedChange?.(collapsed);
            },
            [controlledCollapsed, onSidebarCollapsedChange],
        );

        const toggleSidebarOpen = useCallback(() => {
            setSidebarOpen((prev) => !prev);
        }, []);

        const toggleSidebarCollapsed = useCallback(() => {
            setSidebarCollapsed(!sidebarCollapsed);
        }, [sidebarCollapsed, setSidebarCollapsed]);

        const classes = [classPrefix, className].filter(Boolean).join(' ');
        const contextValue = useMemo<ShellContextValue>(
            () => ({
                sidebarOpen,
                setSidebarOpen,
                sidebarCollapsed,
                setSidebarCollapsed,
                toggleSidebarOpen,
                toggleSidebarCollapsed,
            }),
            [sidebarOpen, sidebarCollapsed, setSidebarCollapsed, toggleSidebarOpen, toggleSidebarCollapsed],
        );

        return (
            <ShellContext.Provider value={contextValue}>
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
                const shell = el.closest(`[data-component="${classPrefix}"]`);
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
                    (innerRef as React.RefObject<HTMLDivElement | null>).current = node;
                    if (typeof ref === 'function') {
                        ref(node);
                    } else if (ref) {
                        (ref as React.RefObject<HTMLDivElement | null>).current = node;
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
        const { pathname } = useLocation();
        const classes = [`${classPrefix}__sidebar`, className].filter(Boolean).join(' ');
        const navRef = useRef<HTMLElement | null>(null);
        const invocationTargetRef = useRef<HTMLElement | null>(null);
        const isMobile = useMediaQuery('(max-width: 767px)');

        useEffect(() => {
            setSidebarOpen(false);
        }, [pathname, setSidebarOpen]);

        useEffect(() => {
            if (!sidebarOpen || !isMobile) {
                return;
            }

            const nav = navRef.current;
            if (!nav) {
                return;
            }

            invocationTargetRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
            const shell = nav.closest<HTMLElement>(`[data-component="${classPrefix}"]`);
            // The header is not in this list: it holds the toggle that closes this drawer.
            const background = shell
                ? Array.from(
                      shell.querySelectorAll<HTMLElement>(
                          `[data-component="${classPrefix}-main"], [data-component="${classPrefix}-bottom-nav"]`,
                      ),
                  )
                : [];
            const previousOverflow = document.body.style.overflow;

            for (const element of background) {
                element.inert = true;
            }
            document.body.style.overflow = 'hidden';

            const firstFocusable = nav.querySelector<HTMLElement>(
                'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
                    'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
            );
            (firstFocusable ?? nav).focus();

            const handleEscape = (event: globalThis.KeyboardEvent) => {
                if (event.key === 'Escape') {
                    event.preventDefault();
                    setSidebarOpen(false);
                }
            };
            document.addEventListener('keydown', handleEscape);

            return () => {
                document.removeEventListener('keydown', handleEscape);
                for (const element of background) {
                    element.inert = false;
                }
                document.body.style.overflow = previousOverflow;
                if (invocationTargetRef.current?.isConnected) {
                    invocationTargetRef.current.focus();
                }
                invocationTargetRef.current = null;
            };
        }, [classPrefix, isMobile, setSidebarOpen, sidebarOpen]);

        return (
            <>
                <div
                    className={`${classPrefix}__sidebar-backdrop`}
                    data-visible={sidebarOpen}
                    onClick={() => setSidebarOpen(false)}
                    aria-hidden="true"
                />
                <nav
                    ref={(node) => {
                        navRef.current = node;
                        if (typeof ref === 'function') {
                            ref(node);
                        } else if (ref) {
                            ref.current = node;
                        }
                    }}
                    className={classes}
                    data-component={`${classPrefix}-sidebar`}
                    id={navId}
                    aria-label={ariaLabel}
                    data-open={sidebarOpen}
                    data-collapsed={sidebarCollapsed}
                    {...props}
                    tabIndex={isMobile && sidebarOpen ? -1 : undefined}
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
                data-scroll-container=""
                tabIndex={-1}
                {...props}
            >
                <div className={`${classPrefix}__main-wrapper`} data-component={`${classPrefix}-main-wrapper`}>
                    {children}
                </div>
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

    const isDesktop = useMediaQuery('(min-width: 768px)');

    const closeSidebar = useCallback(() => {
        context.setSidebarOpen(false);
    }, [context]);

    const toggleSidebar = useCallback(() => {
        if (isDesktop) {
            context.toggleSidebarCollapsed();
        } else {
            context.toggleSidebarOpen();
        }
    }, [context, isDesktop]);

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
