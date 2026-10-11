import { forwardRef, useState, useSyncExternalStore, type ComponentPropsWithoutRef } from 'react';
import { SignOutIcon, CaretDownIcon, PaletteIcon, CircleHalfIcon } from '@phosphor-icons/react';

import { useAuth } from '@/auth';
import { getThemeState, getThemeVariant, setThemePreference, subscribeTheme } from '@/lib/theme';
import { ThemeDialog } from '../ThemeDialog';
import { Avatar } from '../Avatar';
import { Button } from '../Button';
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuLabel,
} from '../DropdownMenu';

import './UserMenu.css';

export interface UserMenuProps extends ComponentPropsWithoutRef<'div'> {
    showUsername?: boolean;
}

const UserMenu = forwardRef<HTMLDivElement, UserMenuProps>(({ showUsername = true, className, ...props }, ref) => {
    const { user, signOut } = useAuth();
    const [themeOpen, setThemeOpen] = useState(false);
    const { catalog, preference } = useSyncExternalStore(subscribeTheme, getThemeState);
    const themeSettings = preference ?? catalog.defaults;
    const nextThemeMode = getThemeVariant(themeSettings) === 'Dark' ? 'Light' : 'Dark';

    const classes = ['user-menu', className].filter(Boolean).join(' ');

    if (!user) {
        return null;
    }

    const initials = user.username
        .split(/\s+/)
        .map((word) => word.charAt(0).toUpperCase())
        .slice(0, 2)
        .join('');

    return (
        <div ref={ref} className={classes} data-component="user-menu" {...props}>
            <DropdownMenu>
                <DropdownMenuTrigger asChild>
                    <Button
                        variant="ghost"
                        size="md"
                        className="user-menu__trigger"
                        aria-label={`User menu for ${user.username}`}
                    >
                        <Avatar size="sm" fallback={initials} alt={user.username} />
                        {showUsername && <span className="user-menu__username">{user.username}</span>}
                        <CaretDownIcon size={16} weight="bold" className="user-menu__caret" aria-hidden="true" />
                    </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" sideOffset={8}>
                    <DropdownMenuLabel>{user.username}</DropdownMenuLabel>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem onSelect={() => setThemeOpen(true)}>
                        <PaletteIcon size={16} aria-hidden="true" />
                        Theme…
                    </DropdownMenuItem>
                    {preference !== null && (
                        <DropdownMenuItem
                            onSelect={() => setThemePreference({ ...themeSettings, mode: nextThemeMode })}
                        >
                            <CircleHalfIcon
                                size={16}
                                weight="fill"
                                mirrored={nextThemeMode === 'Dark'}
                                aria-hidden="true"
                            />
                            {nextThemeMode} mode
                        </DropdownMenuItem>
                    )}
                    <DropdownMenuSeparator />
                    <DropdownMenuItem onSelect={signOut} data-action="logout">
                        <SignOutIcon size={16} weight="duotone" aria-hidden="true" />
                        Log out
                    </DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>
            <ThemeDialog open={themeOpen} onOpenChange={setThemeOpen} />
        </div>
    );
});

UserMenu.displayName = 'UserMenu';

export { UserMenu };
