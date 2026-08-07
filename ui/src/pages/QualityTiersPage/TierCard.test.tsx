import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { TierCard } from './TierCard';
import type { TierWithCriteria } from './tierListDetailMappers';

function TestWrapper({ children }: { children: ReactNode }) {
    return <TooltipProvider>{children}</TooltipProvider>;
}

function createTier(overrides: Partial<TierWithCriteria> = {}): TierWithCriteria {
    return {
        id: 'tier-1',
        name: 'Bluray-1080p',
        isCutoff: false,
        criteriaCount: 1,
        criteria: [{ id: 'c-1', field: 'source', operator: 'Equals', value: 'Bluray' }],
        exclusions: [],
        sourceRules: [],
        ...overrides,
    };
}

const noopHandlers = {
    onEdit: vi.fn(),
    onClone: vi.fn(),
    onDelete: vi.fn(),
    onToggleCutoff: vi.fn(),
    onDragStart: vi.fn(),
    onDragEnd: vi.fn(),
    onDragOver: vi.fn(),
    onDragLeave: vi.fn(),
    onDrop: vi.fn(),
};

describe('TierCard', () => {
    it('renders the tier name, priority, and criterion', () => {
        render(
            <TestWrapper>
                <TierCard
                    tier={createTier()}
                    priority={1}
                    totalTiers={3}
                    isDeleting={false}
                    isCloning={false}
                    canManage
                    isDragging={false}
                    isDropTarget={false}
                    dropPosition={null}
                    {...noopHandlers}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Bluray-1080p')).toBeInTheDocument();
        expect(screen.getByText('source')).toBeInTheDocument();
        expect(screen.getByText('=')).toBeInTheDocument();
        expect(screen.getByText('Bluray')).toBeInTheDocument();
        expect(screen.getByText('of 3')).toBeInTheDocument();
    });

    it('shows the cutoff badge when the tier is a cutoff', () => {
        render(
            <TestWrapper>
                <TierCard
                    tier={createTier({ isCutoff: true })}
                    priority={1}
                    totalTiers={1}
                    isDeleting={false}
                    isCloning={false}
                    canManage
                    isDragging={false}
                    isDropTarget={false}
                    dropPosition={null}
                    {...noopHandlers}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Cutoff')).toBeInTheDocument();
    });

    it('invokes action callbacks when management buttons are clicked', async () => {
        const onEdit = vi.fn();
        const onDelete = vi.fn();
        const onToggleCutoff = vi.fn();
        render(
            <TestWrapper>
                <TierCard
                    tier={createTier()}
                    priority={1}
                    totalTiers={1}
                    isDeleting={false}
                    isCloning={false}
                    canManage
                    isDragging={false}
                    isDropTarget={false}
                    dropPosition={null}
                    {...noopHandlers}
                    onEdit={onEdit}
                    onDelete={onDelete}
                    onToggleCutoff={onToggleCutoff}
                />
            </TestWrapper>,
        );

        await userEvent.click(screen.getByRole('button', { name: /edit bluray-1080p/i }));
        await userEvent.click(screen.getByRole('button', { name: /set bluray-1080p as cutoff/i }));
        await userEvent.click(screen.getByRole('button', { name: /delete bluray-1080p/i }));

        expect(onEdit).toHaveBeenCalledWith('tier-1');
        expect(onToggleCutoff).toHaveBeenCalledWith('tier-1');
        expect(onDelete).toHaveBeenCalledWith('tier-1', 'Bluray-1080p');
    });

    it('hides management actions when the user cannot manage', () => {
        render(
            <TestWrapper>
                <TierCard
                    tier={createTier()}
                    priority={1}
                    totalTiers={1}
                    isDeleting={false}
                    isCloning={false}
                    canManage={false}
                    isDragging={false}
                    isDropTarget={false}
                    dropPosition={null}
                    {...noopHandlers}
                />
            </TestWrapper>,
        );

        expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument();
        expect(screen.queryByTestId('tier-list-detail-page__drag-handle')).not.toBeInTheDocument();
    });
});
