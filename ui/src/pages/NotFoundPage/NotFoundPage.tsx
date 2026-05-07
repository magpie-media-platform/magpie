import { Link } from 'react-router-dom';
import { MagnifyingGlass, House } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';

import './NotFoundPage.css';

export function NotFoundPage() {
    useDocumentTitle('Not Found');
    return (
        <div className="not-found-page" data-page="not-found">
            <EmptyState
                icon={<MagnifyingGlass weight="duotone" />}
                title="Page not found"
                description="The page you're looking for doesn't exist or may have moved."
                action={
                    <Button asChild variant="secondary">
                        <Link to="/">
                            <House weight="bold" aria-hidden="true" />
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
