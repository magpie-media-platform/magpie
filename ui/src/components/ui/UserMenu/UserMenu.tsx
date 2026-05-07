import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { SignOut, CaretDown } from '@phosphor-icons/react';

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
    const navigate = useNavigate();

    const classes = ['user-menu', className].filter(Boolean).join(' ');

    if (!user) {
        return null;
    }

    const initials = user.username
        .split(/\s+/)
        .map((word) => word.charAt(0).toUpperCase())
        .slice(0, 2)
        .join('');

    const handleLogout = async () => {
        await signOut();
        navigate('/login');
    };

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
                        <CaretDown size={16} weight="bold" className="user-menu__caret" aria-hidden="true" />
                    </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" sideOffset={8}>
                    <DropdownMenuLabel>{user.username}</DropdownMenuLabel>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem onSelect={handleLogout} data-action="logout">
                        <SignOut size={16} weight="duotone" aria-hidden="true" />
                        Log out
                    </DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>
        </div>
    );
});

UserMenu.displayName = 'UserMenu';

export { UserMenu };
