import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { BulkSelection } from '@/hooks/useBulkSelection';

import { BulkActionsToolbar, SelectModeToggle } from './BulkActionsToolbar';

const pageSelected: BulkSelection = {
    rowSelection: { 'row-1': true },
    headerChecked: true,
    toggleHeader: vi.fn(),
    isRowChecked: () => true,
    toggleRow: vi.fn(),
    allPages: false,
    selectAllAcrossPages: vi.fn(),
    showAcrossPagesPrompt: true,
    selectedCount: 25,
    pageSelectedCount: 25,
    totalCount: 120,
    hasSelection: true,
    selectedIds: ['row-1'],
    selectionMode: false,
    toggleSelectionMode: vi.fn(),
    selectOnly: vi.fn(),
    clear: vi.fn(),
    resolve: () => ({ mode: 'ids', ids: ['row-1'] }),
};

describe('BulkActionsToolbar', () => {
    it('offers the across-pages escalation when the page is full and more rows match', () => {
        render(
            <BulkActionsToolbar selection={pageSelected} label="Rule selection actions" itemNounPlural="rules">
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        expect(screen.getByText('25 selected')).toBeInTheDocument();
        expect(screen.getByText('All 25 on this page are selected.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select all 120 across all pages' })).toBeInTheDocument();
    });

    it('escalates to every page when the banner action is used', async () => {
        const selectAllAcrossPages = vi.fn();
        const user = userEvent.setup();
        render(
            <BulkActionsToolbar
                selection={{ ...pageSelected, selectAllAcrossPages }}
                label="Rule selection actions"
                itemNounPlural="rules"
            >
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        await user.click(screen.getByRole('button', { name: 'Select all 120 across all pages' }));

        expect(selectAllAcrossPages).toHaveBeenCalledTimes(1);
    });

    it('says the whole filtered set is selected once the escalation is taken', () => {
        render(
            <BulkActionsToolbar
                selection={{ ...pageSelected, allPages: true, showAcrossPagesPrompt: false, selectedCount: 120 }}
                label="Rule selection actions"
                itemNounPlural="rules"
            >
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        expect(screen.getByText('All 120 matching rules across all pages are selected.')).toBeInTheDocument();
        expect(document.querySelector('[data-action="clear-across-pages"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="select-all-across-pages"]')).not.toBeInTheDocument();
    });

    it('shows no banner when the page already holds every matching row', () => {
        render(
            <BulkActionsToolbar
                selection={{ ...pageSelected, showAcrossPagesPrompt: false, totalCount: 25 }}
                label="Rule selection actions"
                itemNounPlural="rules"
            >
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        expect(screen.queryByText(/on this page are selected/)).not.toBeInTheDocument();
        expect(screen.queryByText(/across all pages/)).not.toBeInTheDocument();
    });

    it('stays inert and renders no actions with nothing selected', () => {
        render(
            <BulkActionsToolbar
                selection={{ ...pageSelected, hasSelection: false, selectedCount: 0, showAcrossPagesPrompt: false }}
                label="Rule selection actions"
                itemNounPlural="rules"
            >
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        const presence = document.querySelector('[data-component="bulk-actions-toolbar"]');
        expect(presence).toHaveAttribute('data-visible', 'false');
        expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
    });

    it('clears the selection from the toolbar', async () => {
        const clear = vi.fn();
        const user = userEvent.setup();
        render(
            <BulkActionsToolbar
                selection={{ ...pageSelected, clear }}
                label="Rule selection actions"
                itemNounPlural="rules"
            >
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        await user.click(screen.getByRole('button', { name: 'Clear selection' }));

        expect(clear).toHaveBeenCalledTimes(1);
    });

    it('drops the desktop-only class for a page whose only bulk affordance is this toolbar', () => {
        render(
            <BulkActionsToolbar
                selection={pageSelected}
                label="Rule selection actions"
                itemNounPlural="rules"
                desktopOnly={false}
            >
                <button type="button">Delete</button>
            </BulkActionsToolbar>,
        );

        expect(document.querySelector('[data-component="bulk-actions-toolbar"]')).not.toHaveClass(
            'bulk-actions-toolbar-presence--desktop',
        );
    });
});

describe('SelectModeToggle', () => {
    it('keeps one name and shows selection mode as its pressed state', async () => {
        const onToggle = vi.fn();
        const user = userEvent.setup();
        const { rerender } = render(<SelectModeToggle active={false} onToggle={onToggle} />);

        expect(document.querySelector('[data-action="enter-select-mode"]')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Select', pressed: false }));
        expect(onToggle).toHaveBeenCalledTimes(1);

        rerender(<SelectModeToggle active onToggle={onToggle} />);
        expect(screen.getByRole('button', { name: 'Select', pressed: true })).toBeInTheDocument();
        expect(document.querySelector('[data-action="exit-select-mode"]')).toBeInTheDocument();
    });

    it('wraps in the narrow-viewport container by default and skips it when shown at every width', () => {
        const { rerender } = render(<SelectModeToggle active={false} onToggle={vi.fn()} />);

        expect(document.querySelector('.mobile-select-toggle')).toBeInTheDocument();

        rerender(<SelectModeToggle active={false} mobileOnly={false} onToggle={vi.fn()} />);

        expect(document.querySelector('.mobile-select-toggle')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Select' })).toBeInTheDocument();
    });
});
