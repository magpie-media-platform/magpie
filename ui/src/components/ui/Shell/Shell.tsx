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

import { Button } from '../Button';

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
            const next = !sidebarCollapsed;
            setInternalCollapsed(next);
            onSidebarCollapsedChange?.(next);
        }, [sidebarCollapsed, onSidebarCollapsedChange]);

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
            onKeyDown,
            ...props
        },
        ref,
    ) => {
        const { sidebarOpen, setSidebarOpen, sidebarCollapsed } = useShellContext();
        const classes = [`${classPrefix}__sidebar`, className].filter(Boolean).join(' ');
        const navRef = useRef<HTMLElement | null>(null);
        const invocationTargetRef = useRef<HTMLElement | null>(null);
        const [isMobile, setIsMobile] = useState(
            () => typeof window !== 'undefined' && window.matchMedia('(max-width: 767px)').matches,
        );

        useEffect(() => {
            const query = window.matchMedia('(max-width: 767px)');
            const handleChange = (event: MediaQueryListEvent) => setIsMobile(event.matches);
            setIsMobile(query.matches);
            query.addEventListener('change', handleChange);
            return () => query.removeEventListener('change', handleChange);
        }, []);

        useEffect(() => {
            if (!sidebarOpen || !isMobile) {
                return;
            }

            const nav = navRef.current;
            if (!nav) {
                return;
            }

            invocationTargetRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
            const shell = nav.closest<HTMLElement>(`.${classPrefix}`);
            const background = shell
                ? Array.from(
                      shell.querySelectorAll<HTMLElement>(
                          `[data-component="${classPrefix}-header"], ` +
                              `[data-component="${classPrefix}-main"], ` +
                              `[data-component="${classPrefix}-bottom-nav"]`,
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

        const handleKeyDown: ComponentPropsWithoutRef<'nav'>['onKeyDown'] = (event) => {
            onKeyDown?.(event);
            if (event.defaultPrevented || !sidebarOpen || !isMobile || event.key !== 'Tab') {
                return;
            }

            const nav = navRef.current;
            if (!nav) {
                return;
            }
            const focusable = Array.from(
                nav.querySelectorAll<HTMLElement>(
                    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
                        'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
                ),
            );
            if (focusable.length === 0) {
                event.preventDefault();
                nav.focus();
                return;
            }

            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };

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
                    role={isMobile && sidebarOpen ? 'dialog' : undefined}
                    aria-modal={isMobile && sidebarOpen ? true : undefined}
                    tabIndex={isMobile && sidebarOpen ? -1 : undefined}
                    onKeyDown={handleKeyDown}
                >
                    {isMobile && sidebarOpen && (
                        <Button
                            variant="ghost"
                            size="md"
                            className={`${classPrefix}__sidebar-close`}
                            onClick={() => setSidebarOpen(false)}
                        >
                            Collapse sidebar
                        </Button>
                    )}
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
