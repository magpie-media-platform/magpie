import { describe, expect, it } from 'vitest';

import { buildAwardGroups } from './materializedMetadataDisplay';
import type { MaterializedMetadata, MetadataAward } from '@/types/contentMetadata';

function metadataWithAwards(awards: MetadataAward[]): MaterializedMetadata {
    return { containerId: 'container-1', awards };
}

describe('buildAwardGroups', () => {
    it('returns no rows when there is no award data', () => {
        // Arrange, Act, Assert
        expect(buildAwardGroups(undefined)).toEqual([]);
        expect(buildAwardGroups(metadataWithAwards([]))).toEqual([]);
    });

    it('keeps both years when two awards share a label and neither has a status', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a1', ordinal: 0, body: 'BAFTA', name: 'Best Actor', year: 2011 },
            { id: 'a2', ordinal: 1, body: 'BAFTA', name: 'Best Actor', year: 2015 },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:bafta',
                body: 'BAFTA',
                entries: [{ label: 'Best Actor', results: ['2011', '2015'] }],
            },
        ]);
    });

    it('keeps a win and a nomination distinct when neither has a year', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a1', ordinal: 0, body: 'BAFTA', name: 'Best Actor', status: 'Won' },
            { id: 'a2', ordinal: 1, body: 'BAFTA', name: 'Best Actor', status: 'Nominated' },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:bafta',
                body: 'BAFTA',
                entries: [{ label: 'Best Actor', results: ['(Won)', '(Nominated)'] }],
            },
        ]);
    });

    it('keeps a separate entry for each award that carries no year and no status', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a1', ordinal: 0, body: 'BAFTA', name: 'Best Actor' },
            { id: 'a2', ordinal: 1, body: 'BAFTA', name: 'Best Actor' },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:bafta',
                body: 'BAFTA',
                entries: [
                    { label: 'Best Actor', results: [] },
                    { label: 'Best Actor', results: [] },
                ],
            },
        ]);
    });

    it('renders a bare year for a win and the provider word for any other status', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a1', ordinal: 0, body: 'Emmy Awards', name: 'Drama', status: 'Won', year: 2013 },
            { id: 'a2', ordinal: 1, body: 'Emmy Awards', name: 'Drama', status: 'nominated', year: 2014 },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:emmy awards',
                body: 'Emmy Awards',
                entries: [{ label: 'Drama', results: ['2013', '2014 (nominated)'] }],
            },
        ]);
    });

    it('groups bodies and labels without regard to case', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a1', ordinal: 0, body: 'MPAA', name: 'Best Actor', year: 2011 },
            { id: 'a2', ordinal: 1, body: 'mpaa', name: 'best actor', year: 2015 },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:mpaa',
                body: 'MPAA',
                entries: [{ label: 'Best Actor', results: ['2011', '2015'] }],
            },
        ]);
    });

    it('separates the label parts from the results', () => {
        // Arrange
        const metadata = metadataWithAwards([
            {
                id: 'a1',
                ordinal: 0,
                body: 'Emmy Awards',
                name: 'Ensemble Prize',
                category: 'Repertory',
                language: 'en',
                country: 'US',
            },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:emmy awards',
                body: 'Emmy Awards',
                entries: [{ label: 'Ensemble Prize · Repertory · en · US', results: [] }],
            },
        ]);
    });

    it('degrades to the surviving text when the body or the name is whitespace only', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a1', ordinal: 0, body: '   ', name: 'Best Actor', year: 2011 },
            { id: 'a2', ordinal: 1, body: 'BAFTA', name: ' ', year: 2015 },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            { key: 'award-body:best actor', body: 'Best Actor', entries: [{ label: '', results: ['2011'] }] },
            { key: 'award-body:bafta', body: 'BAFTA', entries: [{ label: '', results: ['2015'] }] },
        ]);
    });

    it('drops an award that has neither a body nor a name', () => {
        // Arrange
        const metadata = metadataWithAwards([{ id: 'a1', ordinal: 0, body: '', name: '', year: 2011 }]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([]);
    });

    it('preserves ordinal order across bodies and entries', () => {
        // Arrange
        const metadata = metadataWithAwards([
            { id: 'a3', ordinal: 2, body: 'BAFTA', name: 'Best Film', year: 2020 },
            { id: 'a1', ordinal: 0, body: 'Emmy Awards', name: 'Drama', year: 2013 },
            { id: 'a2', ordinal: 1, body: 'BAFTA', name: 'Best Actor', year: 2015 },
        ]);

        // Act
        const rows = buildAwardGroups(metadata);

        // Assert
        expect(rows).toEqual([
            {
                key: 'award-body:emmy awards',
                body: 'Emmy Awards',
                entries: [{ label: 'Drama', results: ['2013'] }],
            },
            {
                key: 'award-body:bafta',
                body: 'BAFTA',
                entries: [
                    { label: 'Best Actor', results: ['2015'] },
                    { label: 'Best Film', results: ['2020'] },
                ],
            },
        ]);
    });
});
