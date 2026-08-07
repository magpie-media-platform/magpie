import { Link } from 'react-router-dom';
import { MagnifyingGlassIcon, HouseIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';

import './NotFoundPage.css';

export function NotFoundPage() {
    useDocumentTitle('Not Found');
    return (
        <div className="not-found-page" data-page="not-found">
            <EmptyState
                icon={<MagnifyingGlassIcon weight="duotone" />}
                title="Page not found"
                titleLevel={1}
                description="The requested page does not exist."
                action={
                    <Button asChild variant="secondary">
                        <Link to="/">
                            <HouseIcon weight="bold" aria-hidden="true" />
                            Go to Dashboard
                        </Link>
                    </Button>
                }
            />
        </div>
    );
}

NotFoundPage.displayName = 'NotFoundPage';

export default NotFoundPage;
