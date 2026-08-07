import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ArrowClockwiseIcon, MagnifyingGlassIcon } from '@phosphor-icons/react';

import { QuickActionsButton, type QuickAction } from './QuickActionsButton';

const mockActions: QuickAction[] = [
    {
        id: 'refresh',
        label: 'Refresh Metadata',
        icon: <ArrowClockwiseIcon data-testid="icon-refresh" />,
        onClick: vi.fn(),
    },
    {
        id: 'search',
        label: 'Search Now',
        icon: <MagnifyingGlassIcon data-testid="icon-search" />,
        onClick: vi.fn(),
        variant: 'primary',
    },
];

describe('QuickActionsButton', () => {
    it('renders the FAB trigger', () => {
        render(<QuickActionsButton actions={mockActions} title="Test Actions" />);

        const fab = screen.getByRole('button', { name: 'Test Actions' });
        expect(fab).toBeInTheDocument();
        expect(fab).toHaveClass('quick-actions-button__fab');
    });

    it('does not render when visible is false', () => {
        render(<QuickActionsButton actions={mockActions} visible={false} />);

        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('does not render when actions array is empty', () => {
        render(<QuickActionsButton actions={[]} />);

        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('opens bottom sheet when FAB is clicked', async () => {
        const user = userEvent.setup();
        render(<QuickActionsButton actions={mockActions} title="Test Actions" />);

        const fab = screen.getByRole('button', { name: 'Test Actions' });
        await user.click(fab);

        expect(screen.getByText('Test Actions')).toBeInTheDocument();

        expect(screen.getByText('Refresh Metadata')).toBeInTheDocument();
        expect(screen.getByText('Search Now')).toBeInTheDocument();
    });

    it('renders all actions in the sheet', async () => {
        const user = userEvent.setup();
        render(<QuickActionsButton actions={mockActions} title="Test Actions" />);

        const fab = screen.getByRole('button', { name: 'Test Actions' });
        await user.click(fab);

        const menuItems = screen.getAllByText(/Refresh|Search/);
        expect(menuItems).toHaveLength(2);
    });

    it('single action triggers onClick directly without sheet', async () => {
        const user = userEvent.setup();
        const onRefreshClick = vi.fn();
        const singleAction: QuickAction[] = [
            {
                id: 'refresh',
                label: 'Refresh',
                icon: <ArrowClockwiseIcon />,
                onClick: onRefreshClick,
            },
        ];

        render(<QuickActionsButton actions={singleAction} title="Actions" />);

        const fab = screen.getByRole('button', { name: 'Refresh' });
        await user.click(fab);

        expect(onRefreshClick).toHaveBeenCalledTimes(1);
    });

    it('closes the sheet after action is clicked', async () => {
        const user = userEvent.setup();
        render(<QuickActionsButton actions={mockActions} title="Test Actions" />);

        const fab = screen.getByRole('button', { name: 'Test Actions' });
        await user.click(fab);

        expect(screen.getByText('Test Actions')).toBeInTheDocument();

        const refreshAction = screen.getByRole('button', { name: 'Refresh Metadata' });
        await user.click(refreshAction);

        await vi.waitFor(() => {
            const sheetTitles = screen.queryAllByRole('heading', { name: 'Test Actions' });
            expect(sheetTitles.length).toBe(0);
        });
    });

    it('single disabled action renders disabled FAB', () => {
        const onClickDisabled = vi.fn();
        const disabledAction: QuickAction[] = [
            {
                id: 'disabled-action',
                label: 'Disabled Action',
                icon: <ArrowClockwiseIcon />,
                onClick: onClickDisabled,
                disabled: true,
            },
        ];

        render(<QuickActionsButton actions={disabledAction} title="Actions" />);

        const fab = screen.getByRole('button', { name: 'Disabled Action' });
        expect(fab).toBeDisabled();

        fireEvent.click(fab);
        expect(onClickDisabled).not.toHaveBeenCalled();
    });

    it('applies correct variant class to actions', async () => {
        const user = userEvent.setup();
        const actionsWithVariants: QuickAction[] = [
            {
                id: 'default-action',
                label: 'Default',
                icon: <ArrowClockwiseIcon />,
                onClick: vi.fn(),
            },
            {
                id: 'primary-action',
                label: 'Primary',
                icon: <MagnifyingGlassIcon />,
                onClick: vi.fn(),
                variant: 'primary',
            },
            {
                id: 'destructive-action',
                label: 'Delete',
                icon: <MagnifyingGlassIcon />,
                onClick: vi.fn(),
                variant: 'destructive',
            },
        ];

        render(<QuickActionsButton actions={actionsWithVariants} title="Actions" />);

        const fab = screen.getByRole('button', { name: 'Actions' });
        await user.click(fab);

        const defaultAction = screen.getByRole('button', { name: 'Default' });
        const primaryAction = screen.getByRole('button', { name: 'Primary' });
        const destructiveAction = screen.getByRole('button', { name: 'Delete' });

        expect(defaultAction).toHaveAttribute('data-variant', 'default');
        expect(primaryAction).toHaveAttribute('data-variant', 'primary');
        expect(destructiveAction).toHaveAttribute('data-variant', 'destructive');
    });

    it('has correct data attributes', async () => {
        const user = userEvent.setup();
        render(<QuickActionsButton actions={mockActions} title="Actions" />);

        const fab = screen.getByRole('button', { name: 'Actions' });
        expect(fab).toHaveAttribute('data-action', 'open-quick-actions');

        await user.click(fab);

        const refreshAction = screen.getByRole('button', { name: 'Refresh Metadata' });
        expect(refreshAction).toHaveAttribute('data-action', 'refresh');

        const searchAction = screen.getByRole('button', { name: 'Search Now' });
        expect(searchAction).toHaveAttribute('data-action', 'search');
    });

    it('uses default title when not provided', () => {
        render(<QuickActionsButton actions={mockActions} />);

        const fab = screen.getByRole('button', { name: 'Quick Actions' });
        expect(fab).toBeInTheDocument();
    });

    it('applies custom className to container', () => {
        const { container } = render(<QuickActionsButton actions={mockActions} className="custom-class" />);

        const wrapper = container.querySelector('.quick-actions-button');
        expect(wrapper).toHaveClass('custom-class');
    });

    it('hides only header actions mirrored by the mobile quick action', () => {
        const css = readFileSync(
            resolve(process.cwd(), 'src/components/ui/QuickActionsButton/QuickActionsButton.css'),
            'utf8',
        );
        const mirroredActions = css
            .match(
                /\[data-page\]:has\(\.quick-actions-button\)\s+\.page-header__actions\s+:is\((?<actions>[\s\S]*?)\)\s*{/,
            )
            ?.groups?.actions.matchAll(/\[data-action='(?<action>[^']+)'\]/g);
        const actionNames = [...(mirroredActions ?? [])].map((match) => match.groups?.action);

        expect(actionNames).toEqual([
            'create-backup',
            'add-client',
            'add-server',
            'add-filter',
            'add-tier-list',
            'add-wishlist',
            'add-folder',
            'add-rule',
            'add-mapping',
        ]);
        expect(css).not.toMatch(/\.page-header__actions\s+\[data-action\]\s*{/);
        expect(actionNames).not.toContain('clear-filters');
    });
});
