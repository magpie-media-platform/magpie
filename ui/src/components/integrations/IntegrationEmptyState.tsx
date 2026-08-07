import type { ReactNode } from 'react';

import { Button } from '@/components/ui/Button';
import { EmptyState, type EmptyStateSize } from '@/components/ui/EmptyState';

interface IntegrationEmptyStateProps {
    icon: ReactNode;

    entity: string;

    hasActiveFilters: boolean;

    addDescription: string;

    addAction?: ReactNode;

    onClearFilters: () => void;

    size?: EmptyStateSize;
}

export function IntegrationEmptyState({
    icon,
    entity,
    hasActiveFilters,
    addDescription,
    addAction,
    onClearFilters,
    size = 'md',
}: IntegrationEmptyStateProps) {
    if (hasActiveFilters) {
        const entityPlural = entity;
        return (
            <EmptyState
                icon={icon}
                title={`No ${entityPlural} match the active filters`}
                description="Adjust or clear the filters to see more."
                action={
                    <Button variant="secondary" size="sm" onClick={onClearFilters}>
                        Clear filters
                    </Button>
                }
                size={size}
            />
        );
    }

    return (
        <EmptyState
            icon={icon}
            title={`No ${entity} configured`}
            description={addDescription}
            action={addAction}
            size={size}
        />
    );
}

IntegrationEmptyState.displayName = 'IntegrationEmptyState';
