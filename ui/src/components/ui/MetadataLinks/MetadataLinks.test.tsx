import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import type { ContentReference } from '@/lib/metadata';

import { MetadataLinks } from './MetadataLinks';

const references: ContentReference[] = [
    { url: 'https://imdb.com/x', displayName: 'IMDb page', source: 'IMDb', kind: 'database' },
    { url: 'https://studio.example', displayName: 'Studio', source: 'Studio', kind: 'homepage' },
];

describe('MetadataLinks', () => {
    it('renders references as external links opening in a new tab', () => {
        render(<MetadataLinks references={references} />);

        const imdb = screen.getByRole('link', { name: /IMDb page/ });
        expect(imdb).toHaveAttribute('href', 'https://imdb.com/x');
        expect(imdb).toHaveAttribute('target', '_blank');
        expect(imdb).toHaveAttribute('rel', 'noopener noreferrer');

        const studio = screen.getByRole('link', { name: /Studio/ });
        expect(studio).toHaveAttribute('href', 'https://studio.example/');
    });

    it('renders only the source badge when displayName matches the source', () => {
        render(
            <MetadataLinks
                references={[
                    {
                        url: 'https://provider-a.example/title/1',
                        displayName: ' provider-a ',
                        source: 'Provider-A',
                        kind: 'database',
                    },
                ]}
            />,
        );

        const link = screen.getByTestId('metadata-link-provider-a');
        expect(within(link).getByText('Provider-A', { selector: '.metadata-links__badge' })).toBeInTheDocument();
        expect(within(link).queryByText(/provider-a/i, { selector: '.metadata-links__name' })).not.toBeInTheDocument();
        expect(link).not.toHaveTextContent(/Provider-A\s+Provider-A/i);
    });

    it('renders source badge and display name when they differ', () => {
        render(
            <MetadataLinks
                references={[
                    {
                        url: 'https://provider-a.example/title/1',
                        displayName: 'The Matrix',
                        source: 'Provider-A',
                        kind: 'database',
                    },
                ]}
            />,
        );

        const link = screen.getByTestId('metadata-link-provider-a');
        expect(within(link).getByText('Provider-A', { selector: '.metadata-links__badge' })).toBeInTheDocument();
        expect(within(link).getByText('The Matrix', { selector: '.metadata-links__name' })).toBeInTheDocument();
    });

    it('renders unsafe references as text', () => {
        render(
            <MetadataLinks
                references={[{ url: 'javascript:alert(1)', displayName: 'Unsafe', source: 'Unsafe', kind: 'database' }]}
            />,
        );

        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(screen.getByText('Unsafe')).toBeInTheDocument();
    });

    it('renders nothing when there are no references', () => {
        const { container } = render(<MetadataLinks references={[]} />);
        expect(container.firstChild).toBeNull();
    });
});
