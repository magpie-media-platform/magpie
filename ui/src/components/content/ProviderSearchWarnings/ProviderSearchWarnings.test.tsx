import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ProviderSearchWarnings } from './ProviderSearchWarnings';

describe('ProviderSearchWarnings', () => {
    it('shows each distinct warning once', () => {
        render(
            <ProviderSearchWarnings
                hasResults
                warnings={['TMDb could not apply the Awards filter.', 'TMDb could not apply the Awards filter.']}
            />,
        );

        expect(screen.getByRole('status')).toHaveTextContent('Some results may be missing');
        expect(screen.getAllByRole('listitem')).toHaveLength(1);
        expect(screen.getByRole('listitem')).toHaveTextContent('TMDb could not apply the Awards filter.');
    });

    it('explains that an empty result was limited', () => {
        render(<ProviderSearchWarnings hasResults={false} warnings={['The search was too broad.']} />);

        expect(screen.getByRole('status')).toHaveTextContent('This search could not return results');
    });
});
