import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { TagsCell } from './TagsCell';

describe('TagsCell', () => {
    it('renders badges for up to 3 tags', () => {
        render(<TagsCell tags={['action', 'comedy', 'drama']} />);

        expect(screen.getByText('action')).toBeInTheDocument();
        expect(screen.getByText('comedy')).toBeInTheDocument();
        expect(screen.getByText('drama')).toBeInTheDocument();
    });

    it('shows overflow count when more than 3 tags', () => {
        render(<TagsCell tags={['a', 'b', 'c', 'd', 'e']} />);

        expect(screen.getByText('a')).toBeInTheDocument();
        expect(screen.getByText('b')).toBeInTheDocument();
        expect(screen.getByText('c')).toBeInTheDocument();
        expect(screen.getByText('+2')).toBeInTheDocument();
    });

    it('sets title on overflow with remaining tag names', () => {
        render(<TagsCell tags={['a', 'b', 'c', 'd', 'e']} />);

        expect(screen.getByText('+2')).toHaveAttribute('title', 'd, e');
    });

    it('renders empty state when tags is undefined', () => {
        const { container } = render(<TagsCell />);

        expect(container.querySelector('.tags-cell__empty')).toBeInTheDocument();
    });

    it('renders empty state when tags array is empty', () => {
        const { container } = render(<TagsCell tags={[]} />);

        expect(container.querySelector('.tags-cell__empty')).toBeInTheDocument();
    });

    it('shows empty label when showEmptyLabel is true', () => {
        render(<TagsCell tags={[]} showEmptyLabel />);

        expect(screen.getByText('No tags')).toBeInTheDocument();
    });

    it('hides empty label by default', () => {
        render(<TagsCell tags={[]} />);

        expect(screen.queryByText('No tags')).not.toBeInTheDocument();
    });

    it('renders exactly 3 badges when given 4 tags', () => {
        render(<TagsCell tags={['a', 'b', 'c', 'd']} />);

        expect(screen.getByText('+1')).toBeInTheDocument();
    });
});
