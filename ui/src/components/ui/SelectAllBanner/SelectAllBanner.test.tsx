import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { SelectAllBanner } from './SelectAllBanner';

describe('SelectAllBanner', () => {
    it('offers to extend the page selection to every matching row', async () => {
        const user = userEvent.setup();
        const onSelectAll = vi.fn();
        render(
            <SelectAllBanner
                allPages={false}
                pageSelectedCount={25}
                totalCount={1234}
                selectedCount={25}
                onSelectAllAcrossPages={onSelectAll}
                onClear={vi.fn()}
            />,
        );

        expect(screen.getByText(/all 25 on this page are selected/i)).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: /select all 1,234 across all pages/i }));
        expect(onSelectAll).toHaveBeenCalledOnce();
    });

    it('reports the full count and offers to clear once select-all is active', async () => {
        const user = userEvent.setup();
        const onClear = vi.fn();
        render(
            <SelectAllBanner
                allPages
                pageSelectedCount={25}
                totalCount={1234}
                selectedCount={1233}
                onSelectAllAcrossPages={vi.fn()}
                onClear={onClear}
            />,
        );

        expect(screen.getByText(/all 1,233 matching downloads across all pages are selected/i)).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: /clear selection/i }));
        expect(onClear).toHaveBeenCalledOnce();
    });

    it('uses the supplied noun in the select-all copy', () => {
        render(
            <SelectAllBanner
                allPages
                pageSelectedCount={50}
                totalCount={250}
                selectedCount={250}
                onSelectAllAcrossPages={vi.fn()}
                onClear={vi.fn()}
                itemNounPlural="items"
            />,
        );

        expect(screen.getByText(/all 250 matching items across all pages are selected/i)).toBeInTheDocument();
    });
});
