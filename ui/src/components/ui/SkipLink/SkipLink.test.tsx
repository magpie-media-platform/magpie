import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { SkipLink } from './SkipLink';

describe('SkipLink', () => {
    it('renders with default text', () => {
        render(<SkipLink targetId="main-content" />);

        const link = screen.getByRole('link', { name: 'Skip to main content' });
        expect(link).toBeInTheDocument();
    });

    it('renders with custom text', () => {
        render(<SkipLink targetId="main-content">Skip navigation</SkipLink>);

        const link = screen.getByRole('link', { name: 'Skip navigation' });
        expect(link).toBeInTheDocument();
    });

    it('has correct href from targetId', () => {
        render(<SkipLink targetId="main-content" />);

        const link = screen.getByRole('link', { name: 'Skip to main content' });
        expect(link).toHaveAttribute('href', '#main-content');
    });

    it('has data-component attribute', () => {
        render(<SkipLink targetId="main-content" />);

        const link = screen.getByRole('link', { name: 'Skip to main content' });
        expect(link).toHaveAttribute('data-component', 'skip-link');
    });

    it('applies custom className', () => {
        render(<SkipLink targetId="main-content" className="custom-class" />);

        const link = screen.getByRole('link', { name: 'Skip to main content' });
        expect(link).toHaveClass('skip-link', 'custom-class');
    });

    it('passes through additional props', () => {
        render(<SkipLink targetId="main-content" data-testid="skip" />);

        const link = screen.getByTestId('skip');
        expect(link).toBeInTheDocument();
    });
});
