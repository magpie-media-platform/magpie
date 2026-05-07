import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Label } from './Label';

describe('Label', () => {
    it('renders a label element', () => {
        render(<Label>Test Label</Label>);
        expect(screen.getByText('Test Label')).toBeInTheDocument();
    });

    it('renders as a label element', () => {
        render(<Label>Test Label</Label>);
        const label = screen.getByText('Test Label');
        expect(label.tagName).toBe('LABEL');
    });

    it('applies size classes', () => {
        const { rerender } = render(<Label size="sm">Small</Label>);
        expect(screen.getByText('Small')).toHaveAttribute('data-size', 'sm');

        rerender(<Label size="md">Medium</Label>);
        expect(screen.getByText('Medium')).toHaveAttribute('data-size', 'md');

        rerender(<Label size="lg">Large</Label>);
        expect(screen.getByText('Large')).toHaveAttribute('data-size', 'lg');
    });

    it('defaults to md size', () => {
        render(<Label>Default size</Label>);
        expect(screen.getByText('Default size')).toHaveAttribute('data-size', 'md');
    });

    it('applies error state', () => {
        render(<Label error>Error label</Label>);
        expect(screen.getByText('Error label')).toHaveAttribute('data-error', 'true');
    });

    it('shows required indicator when required is true', () => {
        render(<Label required>Required field</Label>);
        expect(screen.getByText('*')).toBeInTheDocument();
    });

    it('does not show required indicator by default', () => {
        render(<Label>Optional field</Label>);
        expect(screen.queryByText('*')).not.toBeInTheDocument();
    });

    it('required indicator is aria-hidden', () => {
        render(<Label required>Required field</Label>);
        expect(screen.getByText('*')).toHaveAttribute('aria-hidden', 'true');
    });

    it('applies custom className', () => {
        render(<Label className="custom-class">Custom</Label>);
        expect(screen.getByText('Custom')).toHaveClass('custom-class');
    });

    it('maintains base class with modifiers', () => {
        render(
            <Label error size="lg" className="extra">
                Combined
            </Label>,
        );
        const label = screen.getByText('Combined');
        expect(label).toHaveClass('label');
        expect(label).toHaveAttribute('data-size', 'lg');
        expect(label).toHaveAttribute('data-error', 'true');
        expect(label).toHaveClass('extra');
    });

    it('passes through htmlFor attribute', () => {
        render(<Label htmlFor="my-input">Input label</Label>);
        expect(screen.getByText('Input label')).toHaveAttribute('for', 'my-input');
    });

    it('passes through additional props', () => {
        render(<Label data-testid="my-label">Props test</Label>);
        expect(screen.getByTestId('my-label')).toBeInTheDocument();
    });

    it('renders children correctly', () => {
        render(
            <Label>
                <span data-testid="child">Child content</span>
            </Label>,
        );
        expect(screen.getByTestId('child')).toBeInTheDocument();
    });

    it('combines required with error state', () => {
        render(
            <Label required error>
                Required error
            </Label>,
        );
        const label = screen.getByText('Required error');
        expect(label).toHaveAttribute('data-error', 'true');
        expect(screen.getByText('*')).toBeInTheDocument();
    });

    it('combines all size and state combinations', () => {
        const { rerender } = render(
            <Label size="sm" error required>
                Small error required
            </Label>,
        );
        let label = screen.getByText('Small error required');
        expect(label).toHaveClass('label');
        expect(label).toHaveAttribute('data-size', 'sm');
        expect(label).toHaveAttribute('data-error', 'true');
        expect(screen.getByText('*')).toBeInTheDocument();

        rerender(<Label size="lg">Large normal</Label>);
        label = screen.getByText('Large normal');
        expect(label).toHaveClass('label');
        expect(label).toHaveAttribute('data-size', 'lg');
        expect(label).not.toHaveAttribute('data-error', 'true');
    });
});
