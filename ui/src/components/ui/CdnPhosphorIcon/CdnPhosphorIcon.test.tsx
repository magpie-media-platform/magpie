import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CubeIcon, IconContext } from '@phosphor-icons/react';
import { CdnPhosphorIcon } from './CdnPhosphorIcon';

const image = document.createElement('img');

beforeEach(() => {
    vi.spyOn(window, 'Image').mockImplementation(function () {
        return image;
    });
});

afterEach(() => vi.restoreAllMocks());

it.each(['UserSquareIcon', 'user-square', 'user_square', 'user square'])('loads a CDN mask for %s', (name) => {
    render(<CdnPhosphorIcon name={name} weight="duotone" data-testid="icon" />);
    expect(image.src).toBe(
        'https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/duotone/user-square-duotone.svg',
    );
    fireEvent.load(image);
    expect(screen.getByTestId('icon')).toHaveAttribute('data-icon-source', 'phosphor-cdn');
    expect(screen.getByTestId('icon').style.maskImage).toBe(
        'url("https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/duotone/user-square-duotone.svg")',
    );
});

describe('fallback and context', () => {
    it.each([undefined, null, '../../user-square'])('uses the cube for an invalid name %s', (name) => {
        render(
            <>
                <CdnPhosphorIcon name={name} data-testid="unknown" />
                <CubeIcon data-testid="cube" />
            </>,
        );
        expect(screen.getByTestId('unknown').innerHTML).toBe(screen.getByTestId('cube').innerHTML);
        expect(window.Image).not.toHaveBeenCalled();
    });

    it('preserves inherited icon options', () => {
        render(
            <IconContext.Provider value={{ size: 32, color: 'red', weight: 'bold', mirrored: true }}>
                <CdnPhosphorIcon name="books" alt="Books" data-testid="icon" />
            </IconContext.Provider>,
        );
        fireEvent.load(image);
        expect(screen.getByTestId('icon')).toHaveAttribute('width', '32');
        expect(screen.getByTestId('icon').style.backgroundColor).toBe('red');
        expect(screen.getByTestId('icon')).toHaveStyle({ transform: 'scaleX(-1)' });
        expect(image.src).toBe('https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/bold/books-bold.svg');
        expect(screen.getByTestId('icon').querySelector('title')).toHaveTextContent('Books');
    });

    it.each(['not-an-icon', 'airplane'])('keeps the cube when the CDN asset %s cannot load', (name) => {
        render(
            <>
                <CdnPhosphorIcon name={name} data-testid="unavailable" />
                <CubeIcon data-testid="fallback" />
            </>,
        );
        fireEvent.error(image);
        expect(screen.getByTestId('unavailable').innerHTML).toBe(screen.getByTestId('fallback').innerHTML);
    });

    it('ignores a previous load after the requested name changes', async () => {
        const { rerender } = render(<CdnPhosphorIcon name="books" data-testid="icon" />);
        const previousLoad = image.onload;
        rerender(<CdnPhosphorIcon name="user-square" data-testid="icon" />);
        await act(() => previousLoad?.call(image, new Event('load')));
        expect(screen.getByTestId('icon')).not.toHaveAttribute('data-icon-source');
        fireEvent.load(image);
        expect(screen.getByTestId('icon').style.maskImage).toBe(
            'url("https://cdn.jsdelivr.net/npm/@phosphor-icons/core@2.1.1/assets/regular/user-square.svg")',
        );
    });
});
