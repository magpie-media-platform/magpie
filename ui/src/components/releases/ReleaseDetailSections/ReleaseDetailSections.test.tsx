import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ReleaseDetailSections } from './ReleaseDetailSections';

describe('ReleaseDetailSections published row', () => {
    it.each([['0001-01-01T00:00:00Z'], ['0001-01-01T00:00:00'], ['0001-01-01']])(
        'renders the grid placeholder instead of year 1 for %s',
        (publishDate) => {
            render(<ReleaseDetailSections release={{ title: 'Some.Release.2024.1080p', publishDate }} />);

            expect(document.querySelector('[data-detail="Published"]')?.textContent).toBe('-');
        },
    );

    it('keeps formatting a valid publish date', () => {
        render(
            <ReleaseDetailSections
                release={{ title: 'Some.Release.2024.1080p', publishDate: '2024-06-01T12:00:00Z' }}
            />,
        );

        const published = document.querySelector('[data-detail="Published"]');
        expect(published).toHaveTextContent('2024');
        expect(published).toHaveTextContent('Jun');
    });
});
