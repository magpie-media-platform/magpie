import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { Avatar } from './Avatar';

describe('Avatar', () => {
    it('renders with fallback content', () => {
        render(<Avatar fallback="JD" />);
        expect(screen.getByText('JD')).toBeInTheDocument();
    });

    it('applies default md size class', () => {
        render(<Avatar fallback="A" data-testid="avatar" />);
        const avatar = screen.getByTestId('avatar');
        expect(avatar).toHaveClass('avatar');
        expect(avatar).toHaveAttribute('data-size', 'md');
    });

    it('applies size classes', () => {
        const sizes = ['sm', 'md', 'lg', 'xl'] as const;

        sizes.forEach((size) => {
            const { unmount } = render(<Avatar size={size} fallback={size} data-testid="avatar" />);
            expect(screen.getByTestId('avatar')).toHaveAttribute('data-size', size);
            unmount();
        });
    });

    it('renders image when src is provided', () => {
        render(<Avatar src="https://example.com/avatar.jpg" alt="User avatar" fallback="U" />);
        const img = screen.getByRole('img', { name: 'User avatar' });
        expect(img).toBeInTheDocument();
        expect(img).toHaveAttribute('src', 'https://example.com/avatar.jpg');
    });

    it('image has avatar__image class', () => {
        render(<Avatar src="https://example.com/avatar.jpg" alt="User" fallback="U" />);
        const img = screen.getByRole('img');
        expect(img).toHaveClass('avatar__image');
    });

    it('fallback has avatar__fallback class', () => {
        render(<Avatar fallback="FB" data-testid="avatar" />);
        const avatar = screen.getByTestId('avatar');
        const fallback = avatar.querySelector('.avatar__fallback');
        expect(fallback).toBeInTheDocument();
    });

    it('applies custom className', () => {
        render(<Avatar fallback="C" className="custom-class" data-testid="avatar" />);
        expect(screen.getByTestId('avatar')).toHaveClass('custom-class');
    });

    it('passes through additional props', () => {
        render(<Avatar fallback="P" data-testid="my-avatar" />);
        expect(screen.getByTestId('my-avatar')).toBeInTheDocument();
    });

    it('renders as a span element', () => {
        render(<Avatar fallback="S" data-testid="avatar" />);
        const avatar = screen.getByTestId('avatar');
        expect(avatar.tagName).toBe('SPAN');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        render(<Avatar ref={ref} fallback="R" />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLSpanElement));
    });

    it('maintains base class with size variant', () => {
        render(<Avatar size="xl" fallback="XL" data-testid="avatar" />);
        const avatar = screen.getByTestId('avatar');
        expect(avatar).toHaveClass('avatar');
        expect(avatar).toHaveAttribute('data-size', 'xl');
    });

    it('renders icon fallback', () => {
        const Icon = () => <svg data-testid="icon" />;
        render(<Avatar fallback={<Icon />} />);
        expect(screen.getByTestId('icon')).toBeInTheDocument();
    });

    it('shows fallback when image fails to load', async () => {
        render(<Avatar src="invalid-url" alt="User" fallback="FB" data-testid="avatar" />);

        const img = screen.getByRole('img');
        img.dispatchEvent(new Event('error'));

        await waitFor(() => {
            expect(screen.getByText('FB')).toBeInTheDocument();
        });
    });

    it('shows image when src is valid and no error', () => {
        render(<Avatar src="valid.jpg" alt="Valid" fallback="V" />);
        expect(screen.getByRole('img')).toBeInTheDocument();
        expect(screen.queryByText('V')).not.toBeInTheDocument();
    });

    it('shows fallback when no src provided', () => {
        render(<Avatar fallback="NS" />);
        expect(screen.getByText('NS')).toBeInTheDocument();
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
    });
});
