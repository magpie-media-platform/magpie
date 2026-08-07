import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DetailInfoGrid } from '@/components/ui/DetailInfoGrid';

import { ContentPolicyRow } from './ContentPolicySettings';

describe('ContentPolicyRow', () => {
    it('renders a compact policy row with its effective value and control', () => {
        render(
            <DetailInfoGrid aria-label="Content settings">
                <ContentPolicyRow label="Monitoring" effectiveValue="Monitored" control={<button>Inherit</button>} />
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
                <ContentPolicyRow label="Root folder" effectiveValue="Movies" />
            </DetailInfoGrid>,
        );

        expect(screen.getByText('Movies')).toBeInTheDocument();
        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });
});
