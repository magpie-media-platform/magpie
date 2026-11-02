import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ContentGallerySkeleton } from './ContentGallerySkeleton';

describe('ContentGallerySkeleton', () => {
    it('Loading_ShowsPosterPlaceholdersWithOneAnnouncement_WhenRendered', () => {
        // Arrange
        // Act
        const { container } = render(<ContentGallerySkeleton />);

        // Assert
        expect(screen.getByRole('status', { name: 'Loading content' })).toBeInTheDocument();
        expect(screen.getAllByRole('status')).toHaveLength(1);
        expect(container.querySelectorAll('.content-gallery-grid > .content-gallery-cell')).toHaveLength(12);
        expect(container.querySelectorAll('.content-gallery-tile__poster')).toHaveLength(12);
        expect(container.querySelector('.content-gallery-tile__poster')).toHaveStyle({ height: 'auto' });
        expect(screen.queryByRole('button')).not.toBeInTheDocument();
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
    });

    it('Loading_UsesRequestedCount_WhenRenderedInAPicker', () => {
        // Arrange
        // Act
        const { container } = render(<ContentGallerySkeleton count={6} data-testid="picker-loading" />);

        // Assert
        expect(screen.getByTestId('picker-loading')).toHaveClass('content-gallery-skeleton');
        expect(container.querySelectorAll('.content-gallery-cell')).toHaveLength(6);
    });
});
