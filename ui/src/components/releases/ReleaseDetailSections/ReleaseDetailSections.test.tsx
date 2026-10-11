import { render, screen } from '@testing-library/react';
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

describe('ReleaseDetailSections release summary', () => {
    it('Summary_ListsDescriptiveRowsBeforeIdentifiers_WhenTheReleaseHasEveryField', () => {
        // Arrange
        const release = {
            title: 'Some.Release.2024.1080p',
            indexerName: 'TheRARBG',
            protocol: 'torrent',
            size: 1389401866,
            publishDate: '2025-03-30T15:32:46Z',
            infoUrl: 'https://indexer.example/release/706145',
            externalId: '85c55a2b13d58d4ebef80e4b06a0e98357514e3e',
            categories: ['TV'],
            languages: ['English'],
        };

        // Act
        render(<ReleaseDetailSections release={release} />);

        // Assert
        const labels = screen.getAllByRole('term').map((term) => term.textContent);
        expect(labels).toEqual([
            'Title',
            'Indexer',
            'Protocol',
            'Size',
            'Published',
            'Categories',
            'Languages',
            'External id',
            'Release page',
        ]);
    });
});

describe('ReleaseDetailSections indexer properties', () => {
    it('Properties_ShowEveryEntryAsReceived_WhenValuesAreNullOrEmpty', () => {
        // Arrange
        const properties = '{"size":"1389401866","poster":null,"note":""}';

        // Act
        render(<ReleaseDetailSections release={{ title: 'Some.Release.2024.1080p', properties }} />);

        // Assert
        const labels = screen.getAllByRole('term').map((term) => term.textContent);
        expect(labels).toEqual(['Title', 'note', 'poster', 'size']);
        expect(document.querySelector('[data-component="release-indexer-properties"]')).toBeInTheDocument();
        expect(document.querySelector('[data-component="release-indexer-properties-text"]')).toBeNull();
        expect(document.querySelector('[data-detail="size"]')).toHaveTextContent('1389401866');
        expect(document.querySelector('[data-detail="poster"]')).toHaveTextContent('null');
        expect(document.querySelector('[data-detail="note"]')).toBeEmptyDOMElement();
    });

    it.each([['not json'], ['["a","b"]']])(
        'Properties_ShowTheTextAsReceivedInOneMonospaceRow_WhenTheValueIsNotAnObject',
        (properties) => {
            // Arrange
            const release = { title: 'Some.Release.2024.1080p', properties };

            // Act
            render(<ReleaseDetailSections release={release} />);

            // Assert
            const row = document.querySelector('[data-detail="Properties"]');
            expect(row).toHaveTextContent(properties);
            expect(row).toHaveClass('details-panel__value--mono', 'details-panel__value--multiline');
            expect(document.querySelector('[data-component="release-indexer-properties-text"]')).toContainElement(
                row as HTMLElement,
            );
            expect(document.querySelector('[data-component="release-indexer-properties"]')).toBeNull();
        },
    );
});
