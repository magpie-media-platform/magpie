import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { SignOutIcon, CaretDownIcon } from '@phosphor-icons/react';

import { useAuth } from '@/auth';
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
                    <DropdownMenuItem onSelect={signOut} data-action="logout">
                        <SignOutIcon size={16} weight="duotone" aria-hidden="true" />
                        Log out
                    </DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>
        </div>
    );
});

UserMenu.displayName = 'UserMenu';

export { UserMenu };
