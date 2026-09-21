import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ImportMonitoringSelect } from './ImportMonitoringSelect';

describe('ImportMonitoringSelect', () => {
    it.each([
        ['Unmonitored', 'Not monitored'],
        ['Future', 'Future only'],
    ] as const)('shows the label for %s instead of the wire value', (value, label) => {
        render(<ImportMonitoringSelect value={value} onChange={vi.fn()} />);

        expect(screen.getByRole('combobox', { name: 'Import monitoring' })).toHaveTextContent(label);
        expect(screen.queryByText(value)).toBeNull();
    });

    it('shows the placeholder when nothing is chosen', () => {
        render(<ImportMonitoringSelect value="" onChange={vi.fn()} />);

        expect(screen.getByRole('combobox', { name: 'Import monitoring' })).toHaveTextContent('Select monitoring');
    });
});
