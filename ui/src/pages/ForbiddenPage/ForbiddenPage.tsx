import { useLocation, Link, useNavigate } from 'react-router-dom';
import { ShieldWarningIcon, HouseIcon, ArrowLeftIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useAuth, Role, RoleName } from '@/auth';

import './ForbiddenPage.css';

interface ForbiddenLocationState {
    requiredRole?: string;
}

const roleAccessDescription: Record<Role, string> = {
    [Role.Viewer]: 'You have read-only access to content and status.',
    [Role.Requester]: 'You can request content and manage your own requests.',
    [Role.Manager]: 'You can manage content, downloads, wishlists, filters, and tags, and run jobs.',
    [Role.Admin]: 'You have administrative access to most resources.',
};

export function ForbiddenPage() {
    useDocumentTitle('Access Denied');
    const location = useLocation();
    const navigate = useNavigate();
    const { user } = useAuth();

    const state = (location.state as ForbiddenLocationState) || {};
    const { requiredRole } = state;

    let description = 'You do not have permission to access this page.';
    if (requiredRole) {
        description = `This page requires ${requiredRole} role or higher.`;
    }

    const handleGoBack = () => {
        void navigate(-1);
    };

    return (
        <div className="forbidden-page" data-page="forbidden">
            <EmptyState
                icon={<ShieldWarningIcon weight="duotone" />}
                title="Access denied"
                titleLevel={1}
                description={description}
                action={
                    <div className="forbidden-page__actions">
                        {user && (
                            <div className="forbidden-page__user-info">
                                <p className="forbidden-page__user">
                                    Signed in as {user.username} ({RoleName[user.role]})
                                </p>
                                <p className="forbidden-page__access">{roleAccessDescription[user.role]}</p>
                            </div>
                        )}
                        <div className="forbidden-page__buttons">
                            <Button variant="secondary" onClick={handleGoBack} data-action="go-back">
                                <ArrowLeftIcon weight="bold" aria-hidden="true" />
                                Go Back
                            </Button>
                            <Button asChild variant="secondary">
                                <Link to="/">
                                    <HouseIcon weight="bold" aria-hidden="true" />
                                    Go to Dashboard
                                </Link>
                            </Button>
                        </div>
                    </div>
                }
            />
        </div>
    );
}

ForbiddenPage.displayName = 'ForbiddenPage';

export default ForbiddenPage;
