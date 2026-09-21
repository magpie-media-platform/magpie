import type { ReactNode } from 'react';

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { DetailInfoGrid } from '@/components/ui/DetailInfoGrid';

import { InlineEditRow } from './InlineEditRow';

function renderRow(canEdit: boolean, readValue: ReactNode) {
    return render(
        <DetailInfoGrid aria-label="Info">
            <InlineEditRow
                label="Root Folder"
                canEdit={canEdit}
                editAriaLabel="Edit root folder"
                triggerTestId="row-trigger"
                readValue={readValue}
            >
                {({ close }) => (
                    <button type="button" data-testid="row-editor" onClick={close}>
                        save
                    </button>
                )}
            </InlineEditRow>
        </DetailInfoGrid>,
    );
}

describe('InlineEditRow', () => {
    it('renders the read value behind an edit trigger and swaps to the editor on click', async () => {
        const user = userEvent.setup();
        renderRow(true, <span data-testid="row-read">Movies-Main</span>);

        expect(screen.getByTestId('row-read')).toHaveTextContent('Movies-Main');
        const trigger = screen.getByTestId('row-trigger');
        expect(trigger).toHaveAttribute('aria-label', 'Edit root folder');
        expect(screen.queryByTestId('row-editor')).not.toBeInTheDocument();

        await user.click(trigger);
        expect(screen.getByTestId('row-editor')).toBeInTheDocument();
        expect(screen.queryByTestId('row-trigger')).not.toBeInTheDocument();
    });

    it('returns to the read state when the editor closes', async () => {
        const user = userEvent.setup();
        renderRow(true, <span data-testid="row-read">Movies-Main</span>);

        await user.click(screen.getByTestId('row-trigger'));
        await user.click(screen.getByTestId('row-editor'));

        expect(screen.getByTestId('row-trigger')).toBeInTheDocument();
        expect(screen.queryByTestId('row-editor')).not.toBeInTheDocument();
    });

    it('shows the plain read value with no edit affordance when editing is disabled', () => {
        renderRow(false, <span data-testid="row-read">Movies-Main</span>);

        expect(screen.getByTestId('row-read')).toHaveTextContent('Movies-Main');
        expect(screen.queryByTestId('row-trigger')).not.toBeInTheDocument();
        expect(document.querySelector('.inline-edit-row__field')).toBeNull();
    });

    it('keeps the closed value and the open editor in the same width-owning field element', async () => {
        const user = userEvent.setup();
        renderRow(true, <span data-testid="row-read">Movies-Main</span>);

        const field = screen.getByTestId('row-trigger').parentElement;
        expect(field).toHaveClass('inline-edit-row__field');
        expect(field).toHaveAttribute('data-component', 'inline-edit-field');
        expect(field).toHaveAttribute('data-state', 'idle');

        await user.click(screen.getByTestId('row-trigger'));

        expect(screen.getByTestId('row-editor').parentElement).toBe(field);
        expect(field).toHaveAttribute('data-state', 'editing');

        await user.click(screen.getByTestId('row-editor'));

        expect(screen.getByTestId('row-trigger').parentElement).toBe(field);
        expect(field).toHaveAttribute('data-state', 'idle');
    });

    it('opens the editor with the keyboard and returns focus to the trigger when the editor closes', async () => {
        const user = userEvent.setup();
        render(
            <DetailInfoGrid aria-label="Info">
                <InlineEditRow
                    label="Root Folder"
                    canEdit
                    editAriaLabel="Edit root folder"
                    triggerTestId="row-trigger"
                    readValue={<span>Movies-Main</span>}
                >
                    {({ close }) => (
                        <div data-testid="row-editor-panel">
                            <button type="button" data-testid="row-editor-save" onClick={close}>
                                save
                            </button>
                        </div>
                    )}
                </InlineEditRow>
            </DetailInfoGrid>,
        );

        await user.tab();
        expect(screen.getByTestId('row-trigger')).toHaveFocus();

        await user.keyboard('{Enter}');
        expect(screen.getByTestId('row-editor-panel')).toBeInTheDocument();

        await user.tab();
        expect(screen.getByTestId('row-editor-save')).toHaveFocus();

        await user.keyboard('{Enter}');
        expect(screen.getByTestId('row-trigger')).toHaveFocus();
    });

    it('returns focus to the trigger when the editor closes after a pointer interaction', async () => {
        const user = userEvent.setup();
        render(
            <DetailInfoGrid aria-label="Info">
                <InlineEditRow
                    label="Quality"
                    canEdit
                    editAriaLabel="Edit quality"
                    triggerTestId="row-trigger"
                    readValue={<span>HD Tiers</span>}
                >
                    {({ close }) => (
                        <div data-testid="row-editor-panel">
                            <button type="button" data-testid="row-editor-save" onClick={close}>
                                save
                            </button>
                        </div>
                    )}
                </InlineEditRow>
            </DetailInfoGrid>,
        );

        await user.click(screen.getByTestId('row-trigger'));
        await user.click(screen.getByTestId('row-editor-save'));

        expect(screen.getByTestId('row-trigger')).toHaveFocus();
    });

    it('does not pull focus to the trigger when the row first renders', () => {
        renderRow(true, <span data-testid="row-read">Movies-Main</span>);

        expect(screen.getByTestId('row-trigger')).not.toHaveFocus();
    });

    it('renders an inherited placeholder value through the same trigger', () => {
        renderRow(true, <span data-testid="row-read">Inherit</span>);

        expect(screen.getByTestId('row-read')).toHaveTextContent('Inherit');
        expect(screen.getByTestId('row-trigger').parentElement).toHaveClass('inline-edit-row__field');
    });

    it('keeps a long read value as the first shrinkable child of the read box', () => {
        renderRow(
            true,
            <>
                <span data-testid="row-read">Movies-Main-Archive-Overflow-Cold-Storage-Volume-Four</span>
                <span data-testid="row-badge">Met cutoff</span>
            </>,
        );

        const read = screen.getByTestId('row-read').parentElement;
        expect(read).toHaveClass('inline-edit-row__read');
        expect(read?.firstElementChild).toBe(screen.getByTestId('row-read'));
    });
});
