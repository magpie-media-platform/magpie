import { Link, useLocation } from 'react-router-dom';
import { GearIcon, HouseIcon } from '@phosphor-icons/react';

import { RequireRole } from './RequireRole';
import { Role } from './types';
import { isAdminRoute } from '@/lib/routes';
import { Button } from '@/components/ui/Button';

import './AdminLink.css';

function AdminLink() {
    const { pathname } = useLocation();
    const inAdmin = isAdminRoute(pathname);
    const label = inAdmin ? 'Home' : 'Admin';
    const Icon = inAdmin ? HouseIcon : GearIcon;

    return (
        <RequireRole min={Role.Admin}>
            <Button asChild variant="ghost" size="md" className="admin-link">
                <Link to={inAdmin ? '/' : '/admin'} aria-label={label}>
                    <Icon size={20} weight="duotone" aria-hidden="true" />
                    <span className="admin-link__label">{label}</span>
                </Link>
            </Button>
        </RequireRole>
    );
}

AdminLink.displayName = 'AdminLink';

export { AdminLink };
