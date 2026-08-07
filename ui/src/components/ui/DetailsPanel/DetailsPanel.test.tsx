import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DetailsPanel } from './DetailsPanel';

describe('DetailsPanel', () => {
    it('renders label/value rows in order', () => {
        render(
            <DetailsPanel
                data-component="test-panel"
                rows={[
                    { label: 'Height', value: '170 cm' },
                    { label: 'Ethnicity', value: 'Caucasian' },
                ]}
            />,
        );

        const panel = document.querySelector('[data-component="test-panel"]') as HTMLElement;
        expect(panel).toBeInTheDocument();

        const labels = within(panel)
            .getAllByRole('term')
            .map((node) => node.textContent);
        expect(labels).toEqual(['Height', 'Ethnicity']);
        expect(within(panel).getByText('170 cm')).toBeInTheDocument();
        expect(within(panel).getByText('Caucasian')).toBeInTheDocument();
    });

    it('renders an optional title with icon', () => {
        render(
            <DetailsPanel
                title="Details"
                icon={<svg data-testid="panel-icon" />}
                rows={[{ label: 'Birth date', value: '1956-03-07' }]}
            />,
        );

        expect(screen.getByRole('heading', { name: /details/i })).toBeInTheDocument();
        expect(screen.getByTestId('panel-icon')).toBeInTheDocument();
    });

    it('renders nothing when there are no rows', () => {
        const { container } = render(<DetailsPanel data-component="empty-panel" rows={[]} />);
        expect(container.querySelector('[data-component="empty-panel"]')).not.toBeInTheDocument();
        expect(container).toBeEmptyDOMElement();
    });
});
