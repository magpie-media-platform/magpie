import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { RouteLoadingState } from './RouteLoadingState';

describe('RouteLoadingState', () => {
    it('renders loading state', () => {
        render(<RouteLoadingState />);

        expect(screen.getByLabelText('Loading page')).toBeInTheDocument();
    });

    it('has accessible label', () => {
        render(<RouteLoadingState />);
        const container = screen.getByLabelText('Loading page');
        expect(container).toHaveAttribute('aria-label', 'Loading page');
        expect(container).toHaveAttribute('role', 'status');
    });

    it('has data-component attribute', () => {
        render(<RouteLoadingState />);
        expect(document.querySelector('[data-component="route-loading"]')).toBeInTheDocument();
    });

    it('renders skeleton elements', () => {
        render(<RouteLoadingState />);

        const skeletons = document.querySelectorAll('.skeleton');
        expect(skeletons.length).toBeGreaterThan(0);
    });

    it('renders header skeleton section', () => {
        render(<RouteLoadingState />);
        expect(document.querySelector('.route-loading__header')).toBeInTheDocument();
    });

    it('renders content skeleton section', () => {
        render(<RouteLoadingState />);
        expect(document.querySelector('.route-loading__content')).toBeInTheDocument();
    });

    it('renders card skeletons', () => {
        render(<RouteLoadingState />);
        const cards = document.querySelectorAll('.route-loading__card');
        expect(cards.length).toBe(2);
    });
});
