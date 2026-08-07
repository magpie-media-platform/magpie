import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DetailInfoGrid, DetailInfoRow } from './DetailInfoGrid';

describe('DetailInfoGrid', () => {
    it('shares one label and value layout across information and controlled rows', () => {
        render(
            <DetailInfoGrid aria-label="Supporting information">
                <DetailInfoRow label="Genres" value="Drama" />
                <DetailInfoRow label="Monitoring" value="Monitored" control={<button>Inherit</button>} />
            </DetailInfoGrid>,
        );

        expect(screen.getByRole('region', { name: 'Supporting information' })).toBeInTheDocument();
        expect(screen.getByText('Genres').parentElement).toHaveClass('detail-info-row');
        expect(screen.getByText('Monitoring').parentElement).toHaveClass('detail-info-row');
        expect(screen.getByRole('button', { name: 'Inherit' })).toBeInTheDocument();
    });

    it('marks rows without controls so values span the shared trailing columns', () => {
        render(
            <DetailInfoGrid aria-label="Supporting information">
                <DetailInfoRow label="Publish Date" value="Jan 15, 2024" data-testid="publish-date-row" />
            </DetailInfoGrid>,
        );

        expect(screen.getByTestId('publish-date-row')).toHaveAttribute('data-has-control', 'false');
    });

    it('sets one shared gap token so every consecutive row pair keeps the same pitch', () => {
        const css = readFileSync(resolve(process.cwd(), 'src/components/ui/DetailInfoGrid/DetailInfoGrid.css'), 'utf8');
        const grid = css.match(/\.detail-info-grid\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups?.declarations;
        const row = css.match(/\.detail-info-row\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups?.declarations;

        expect(grid).toContain('gap: var(--space-sm);');
        expect(grid).not.toContain('gap: var(--space-xs);');
        expect(row).toContain('min-height: 32px;');
        expect(row).not.toContain('margin-top:');
    });

    it('declares the under-header offset once for every detail page that consumes it', () => {
        const css = readFileSync(resolve(process.cwd(), 'src/components/ui/DetailInfoGrid/DetailInfoGrid.css'), 'utf8');
        const underHeader = css.match(/\.detail-info-grid--under-header\s*{(?<declarations>[\s\S]*?)\n {4}}/)?.groups
            ?.declarations;
        const wideUnderHeader = css.match(
            /@media \(min-width: 1100px\) {\s*\.detail-info-grid--under-header\s*{(?<declarations>[\s\S]*?)\n {8}}/,
        )?.groups?.declarations;
        const contentPageCss = readFileSync(
            resolve(process.cwd(), 'src/pages/ContentDetailPage/ContentDetailPage.css'),
            'utf8',
        );
        const subjectPageCss = readFileSync(
            resolve(process.cwd(), 'src/pages/SubjectDetailPage/SubjectDetailPage.css'),
            'utf8',
        );

        expect(underHeader).toContain('margin-top: calc(-1 * var(--space-md));');
        expect(wideUnderHeader).toContain('margin-top: calc(-1 * var(--space-lg));');
        expect(contentPageCss).not.toContain('__supporting-info');
        expect(subjectPageCss).not.toContain('__supporting-info');
    });
});
