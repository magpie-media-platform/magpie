import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { ReactNode } from 'react';

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { DetailInfoGrid } from '@/components/ui/DetailInfoGrid';

import { InlineEditRow } from './InlineEditRow';

const inlineEditCss = readFileSync(resolve(process.cwd(), 'src/components/ui/InlineEditRow/InlineEditRow.css'), 'utf8');
const selectCss = readFileSync(resolve(process.cwd(), 'src/components/ui/Select/Select.css'), 'utf8');

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

    it('rings the restored trigger only when focus is visible, so a pointer user sees no outline', () => {
        const triggerBlock = inlineEditCss.match(/\.inline-edit-row__trigger\s*{(?<block>[\s\S]*?)\n {4}}/)?.groups
            ?.block;

        expect(triggerBlock).toContain('&:focus-visible {');
        expect(triggerBlock).not.toMatch(/&:focus\s*{/);
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

    it('reserves one shared control width for every field and both of its states', () => {
        const field = inlineEditCss.match(/\.inline-edit-row__field\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;
        const fieldChild = inlineEditCss.match(/\.inline-edit-row__field > \*\s*{(?<declarations>[\s\S]*?)\n {4}}/)
            ?.groups?.declarations;

        expect(field).toContain('--inline-edit-row-width: 16rem;');
        expect(field).toContain('width: min(var(--inline-edit-row-width), 100%);');
        expect(fieldChild).toContain('width: 100%;');
    });

    it('declares in css a fixed field height rather than a floor, so a taller closed state cannot outgrow the open one', () => {
        const field = inlineEditCss.match(/\.inline-edit-row__field\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;
        const fieldChild = inlineEditCss.match(/\.inline-edit-row__field > \*\s*{(?<declarations>[\s\S]*?)\n {4}}/)
            ?.groups?.declarations;

        expect(field).toContain('height: 32px;');
        expect(field).not.toContain('min-height');
        expect(fieldChild).toContain('height: 100%;');
    });

    it('gives the closed trigger the same box metrics as the small select trigger', () => {
        const trigger = inlineEditCss.match(/\.inline-edit-row__trigger\s*{(?<declarations>[\s\S]*?)\n\s*&:hover/)
            ?.groups?.declarations;
        const selectTrigger = selectCss.match(/\.select-trigger--sm\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;

        expect(trigger).toContain('padding: var(--space-xs) var(--space-sm);');
        expect(trigger).toContain('border-radius: var(--radius-md);');
        expect(trigger).not.toContain('height:');
        expect(selectTrigger).toContain('padding: var(--space-xs) var(--space-sm);');
        expect(selectTrigger).toContain('min-height: 32px;');
    });

    it('outlines the closed trigger at rest, brightens it on hover and keeps a focus ring', () => {
        const triggerBlock = inlineEditCss.match(/\.inline-edit-row__trigger\s*{(?<block>[\s\S]*?)\n {4}}/)?.groups
            ?.block;
        const trigger = triggerBlock?.match(/^(?<declarations>[\s\S]*?)\n\s*&:hover/)?.groups?.declarations;
        const hover = triggerBlock?.match(/&:hover\s*{(?<declarations>[\s\S]*?)\n\s*}/)?.groups?.declarations;
        const focus = triggerBlock?.match(/&:focus-visible\s*{(?<declarations>[\s\S]*?)\n\s*}/)?.groups?.declarations;

        expect(trigger).toContain('border: 1px solid var(--border-subtle);');
        expect(trigger).toContain('background-color: var(--bg-primary);');
        expect(hover).toContain('border-color: var(--border-accent);');
        expect(focus).toContain('outline: var(--focus-ring-width) solid var(--focus-ring-color);');
    });

    it('truncates a long read value instead of growing one control', () => {
        const read = inlineEditCss.match(/\.inline-edit-row__read\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;
        const firstChild = inlineEditCss.match(
            /\.inline-edit-row__read > :first-child\s*{(?<declarations>[\s\S]*?)\n {4}}/,
        )?.groups?.declarations;

        expect(read).toContain('overflow: hidden;');
        expect(read).toContain('min-width: 0;');
        expect(firstChild).toContain('white-space: nowrap;');
        expect(firstChild).toContain('text-overflow: ellipsis;');
    });
});
