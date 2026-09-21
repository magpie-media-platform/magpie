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
});

describe('DetailInfoRow', () => {
    it('renders a compact policy row with its effective value and control', () => {
        render(
            <DetailInfoGrid aria-label="Content settings">
                <DetailInfoRow label="Monitoring" value="Monitored" control={<button>Inherit</button>} />
            </DetailInfoGrid>,
        );

        expect(screen.getByRole('region', { name: 'Content settings' })).toBeInTheDocument();
        expect(screen.getByText('Monitoring')).toBeInTheDocument();
        expect(screen.getByText('Monitored')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Inherit' })).toBeInTheDocument();
    });

    it('renders effective values without requiring an editable control', () => {
        render(
            <DetailInfoGrid aria-label="Content settings">
                <DetailInfoRow label="Root folder" value="Movies" />
            </DetailInfoGrid>,
        );

        expect(screen.getByText('Movies')).toBeInTheDocument();
        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });
});
