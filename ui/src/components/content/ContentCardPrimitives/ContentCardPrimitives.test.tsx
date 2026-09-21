import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ContentPoster } from './ContentCardPrimitives';

describe('ContentPoster', () => {
    it('shows a placeholder on failure and retries a replacement URL', () => {
        // Arrange
        const { container, rerender } = render(
            <ContentPoster
                posterUrl="/broken.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
        );

        // Act
        fireEvent.error(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(screen.queryByRole('img')).not.toBeInTheDocument();
        expect(container.querySelector('.placeholder')).toHaveAttribute('aria-hidden', 'true');
        rerender(
            <ContentPoster
                posterUrl="/replacement.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
            />,
        );
        expect(screen.getByRole('img', { name: 'Sample artwork' })).toHaveAttribute('src', '/replacement.jpg');
        expect(container.querySelector('.placeholder')).not.toBeInTheDocument();
    });

    it('keeps the site placeholder when no URL was supplied and shows the failure glyph only on error', () => {
        // Arrange
        const empty = <span data-testid="site-empty" />;
        const { container, rerender } = render(
            <ContentPoster
                posterUrl={null}
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
                placeholder={empty}
            />,
        );

        // Assert
        expect(screen.getByTestId('site-empty')).toBeInTheDocument();
        expect(container.querySelector('.placeholder svg')).not.toBeInTheDocument();

        // Act
        rerender(
            <ContentPoster
                posterUrl="/broken.jpg"
                title="Sample"
                className="poster"
                placeholderClassName="placeholder"
                placeholder={empty}
            />,
        );
        fireEvent.error(screen.getByRole('img', { name: 'Sample artwork' }));

        // Assert
        expect(screen.queryByTestId('site-empty')).not.toBeInTheDocument();
        expect(container.querySelector('.placeholder svg')).toBeInTheDocument();
    });
});
