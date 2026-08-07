import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { CollapsibleChipList, type CollapsibleChipItem } from './CollapsibleChipList';

function makeItems(count: number): CollapsibleChipItem[] {
    return Array.from({ length: count }, (_, index) => ({
        key: `item-${index}`,
        node: <span>Item {index}</span>,
    }));
}

describe('CollapsibleChipList', () => {
    it('renders nothing when there are no items', () => {
        const { container } = render(<CollapsibleChipList items={[]} />);
        expect(container.firstChild).toBeNull();
    });

    it('renders every item without a toggle when at or below the threshold', () => {
        render(<CollapsibleChipList items={makeItems(3)} initialVisible={3} />);

        expect(screen.getAllByRole('listitem')).toHaveLength(3);
        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });

    it('truncates to the visible count and expands on demand', async () => {
        const user = userEvent.setup();
        render(<CollapsibleChipList items={makeItems(5)} initialVisible={2} toggleTestId="chip-toggle" />);

        expect(screen.getAllByRole('listitem')).toHaveLength(2);
        const toggle = screen.getByTestId('chip-toggle');
        expect(toggle).toHaveTextContent('Show all (3 more)');
        expect(toggle).toHaveAttribute('aria-expanded', 'false');

        await user.click(toggle);
        expect(screen.getAllByRole('listitem')).toHaveLength(5);
        expect(toggle).toHaveTextContent('Show fewer');
        expect(toggle).toHaveAttribute('aria-expanded', 'true');

        await user.click(toggle);
        expect(screen.getAllByRole('listitem')).toHaveLength(2);
    });
});
