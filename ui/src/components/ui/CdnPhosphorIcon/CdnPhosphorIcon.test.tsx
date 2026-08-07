import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CdnPhosphorIcon, buildPhosphorIconUrl, normalizePhosphorIconName } from './CdnPhosphorIcon';

type ImageResult = 'load' | 'error';

let imageResult: ImageResult;

class MockImage {
    onload: (() => void) | null = null;

    onerror: (() => void) | null = null;

    set src(_value: string) {
        queueMicrotask(() => {
            if (imageResult === 'load') {
                this.onload?.();
            } else {
                this.onerror?.();
            }
        });
    }
}

describe('CdnPhosphorIcon', () => {
    beforeEach(() => {
        imageResult = 'load';
        vi.stubGlobal('Image', MockImage);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('builds pinned CDN URLs for regular and weighted icon assets', () => {
        expect(buildPhosphorIconUrl('user-square', 'regular')).toBe(
            'https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/regular/user-square.svg',
        );
        expect(buildPhosphorIconUrl('user-square', 'duotone')).toBe(
            'https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/duotone/user-square-duotone.svg',
        );
    });

    it('normalizes supported Phosphor name formats and rejects unsafe names', () => {
        expect(normalizePhosphorIconName('UserSquareIcon')).toBe('user-square');
        expect(normalizePhosphorIconName(' user square ')).toBe('user-square');
        expect(normalizePhosphorIconName('user_square')).toBe('user-square');
        expect(normalizePhosphorIconName('../../user-square')).toBeNull();
        expect(normalizePhosphorIconName(undefined)).toBeNull();
    });

    it('renders the CDN icon after its asset loads', async () => {
        render(<CdnPhosphorIcon name="books" weight="duotone" data-testid="icon" />);

        await act(async () => {
            await Promise.resolve();
        });

        const icon = screen.getByTestId('icon');
        expect(icon).toHaveAttribute('data-icon-source', 'phosphor-cdn');
        expect(icon).toHaveStyle({
            maskImage:
                'url("https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/duotone/books-duotone.svg")',
        });
    });

    it('renders the cube fallback when the name is absent or the CDN asset fails', async () => {
        const { rerender } = render(<CdnPhosphorIcon name={undefined} data-testid="icon" />);
        expect(screen.getByTestId('icon')).not.toHaveAttribute('data-icon-source');

        imageResult = 'error';
        rerender(<CdnPhosphorIcon name="not-an-icon" data-testid="icon" />);

        await act(async () => {
            await Promise.resolve();
        });

        expect(screen.getByTestId('icon')).not.toHaveAttribute('data-icon-source');
        expect(screen.getByTestId('icon').querySelector('path')).toBeInTheDocument();
    });
});
