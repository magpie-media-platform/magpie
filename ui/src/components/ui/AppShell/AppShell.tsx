/* eslint-disable react-refresh/only-export-components */
import { forwardRef, type ComponentPropsWithoutRef } from 'react';

import { Shell, ShellHeader, ShellSidebar, ShellMain, ShellBottomNav, useShell, type ShellProps } from '../Shell';

export interface AppShellProps extends Omit<ComponentPropsWithoutRef<'div'>, keyof ShellProps>, ShellProps {}

const AppShell = forwardRef<HTMLDivElement, AppShellProps>((props, ref) => (
    <Shell ref={ref} classPrefix="app-shell" {...props} />
));

AppShell.displayName = 'AppShell';

export type AppShellHeaderProps = ComponentPropsWithoutRef<'div'>;

const AppShellHeader = forwardRef<HTMLDivElement, AppShellHeaderProps>((props, ref) => (
    <ShellHeader ref={ref} classPrefix="app-shell" {...props} />
));

AppShellHeader.displayName = 'AppShellHeader';

export interface AppShellSidebarProps extends ComponentPropsWithoutRef<'nav'> {
    ariaLabel?: string;
}

const AppShellSidebar = forwardRef<HTMLElement, AppShellSidebarProps>(
    ({ ariaLabel = 'Main navigation', ...props }, ref) => (
        <ShellSidebar ref={ref} classPrefix="app-shell" ariaLabel={ariaLabel} navId="navigation-menu" {...props} />
    ),
);

AppShellSidebar.displayName = 'AppShellSidebar';

export type AppShellMainProps = ComponentPropsWithoutRef<'main'>;

const AppShellMain = forwardRef<HTMLElement, AppShellMainProps>((props, ref) => (
    <ShellMain ref={ref} classPrefix="app-shell" mainId="main-content" {...props} />
));

AppShellMain.displayName = 'AppShellMain';

export type AppShellBottomNavProps = ComponentPropsWithoutRef<'div'>;

const AppShellBottomNav = forwardRef<HTMLDivElement, AppShellBottomNavProps>((props, ref) => (
    <ShellBottomNav ref={ref} classPrefix="app-shell" {...props} />
));

AppShellBottomNav.displayName = 'AppShellBottomNav';

const useAppShell = useShell;

export { AppShell, AppShellHeader, AppShellSidebar, AppShellMain, AppShellBottomNav, useAppShell };

export default AppShell;
