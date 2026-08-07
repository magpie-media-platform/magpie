import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Pager } from './Pager';

function noopHandlers() {
    return { onFirst: vi.fn(), onPrevious: vi.fn(), onNext: vi.fn(), onLast: vi.fn() };
}

describe('Pager', () => {
    it('renders the page-position indicator', () => {
        render(<Pager page={2} pageCount={5} {...noopHandlers()} />);
        expect(screen.getByText('Page 2 of 5')).toBeInTheDocument();
    });

    it('does not render a total row count', () => {
        render(<Pager page={2} pageCount={5} {...noopHandlers()} />);
        expect(screen.queryByText(/of \d+ items/i)).toBeNull();
        expect(screen.queryByText(/\d+-\d+ of \d+/)).toBeNull();
    });

    it('disables First and Previous on the first page', () => {
        render(<Pager page={1} pageCount={5} {...noopHandlers()} />);
        expect(screen.getByRole('button', { name: /first page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /previous page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /next page/i })).toBeEnabled();
        expect(screen.getByRole('button', { name: /last page/i })).toBeEnabled();
    });

    it('disables Next and Last on the last page', () => {
        render(<Pager page={5} pageCount={5} {...noopHandlers()} />);
        expect(screen.getByRole('button', { name: /next page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /last page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /first page/i })).toBeEnabled();
        expect(screen.getByRole('button', { name: /previous page/i })).toBeEnabled();
    });

    it('disables every control when disabled is set', () => {
        render(<Pager page={2} pageCount={5} disabled {...noopHandlers()} />);
        expect(screen.getByRole('button', { name: /first page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /previous page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /next page/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /last page/i })).toBeDisabled();
    });

    it('fires the matching handler for each control', async () => {
        const handlers = noopHandlers();
        const user = userEvent.setup();
        render(<Pager page={3} pageCount={5} {...handlers} />);

        await user.click(screen.getByRole('button', { name: /first page/i }));
        expect(handlers.onFirst).toHaveBeenCalledTimes(1);

        await user.click(screen.getByRole('button', { name: /previous page/i }));
        expect(handlers.onPrevious).toHaveBeenCalledTimes(1);

        await user.click(screen.getByRole('button', { name: /next page/i }));
        expect(handlers.onNext).toHaveBeenCalledTimes(1);

        await user.click(screen.getByRole('button', { name: /last page/i }));
        expect(handlers.onLast).toHaveBeenCalledTimes(1);
    });

    describe('open-ended paging (pageCount omitted)', () => {
        it('renders "Page N" without a total and disables Last', () => {
            render(<Pager page={3} canPrevious canNext {...noopHandlers()} />);
            expect(screen.getByText('Page 3')).toBeInTheDocument();
            expect(screen.queryByText(/of \d+/)).toBeNull();
            expect(screen.getByRole('button', { name: /last page/i })).toBeDisabled();
        });

        it('gates First/Previous and Next on canPrevious/canNext', () => {
            render(<Pager page={1} canPrevious={false} canNext {...noopHandlers()} />);
            expect(screen.getByRole('button', { name: /first page/i })).toBeDisabled();
            expect(screen.getByRole('button', { name: /previous page/i })).toBeDisabled();
            expect(screen.getByRole('button', { name: /next page/i })).toBeEnabled();
        });

        it('disables Next when canNext is false', () => {
            render(<Pager page={4} canPrevious canNext={false} {...noopHandlers()} />);
            expect(screen.getByRole('button', { name: /next page/i })).toBeDisabled();
            expect(screen.getByRole('button', { name: /previous page/i })).toBeEnabled();
        });
    });
});
