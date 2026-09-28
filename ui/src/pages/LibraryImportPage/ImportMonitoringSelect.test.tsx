import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { ImportMonitoringSelect } from './ImportMonitoringSelect';

describe('ImportMonitoringSelect', () => {
    it('shows the label for Future instead of the wire value', () => {
        render(<ImportMonitoringSelect value="Future" onChange={vi.fn()} />);

        expect(screen.getByRole('combobox', { name: 'Import monitoring' })).toHaveTextContent('Future only');
        expect(screen.queryByText('Future')).toBeNull();
    });

    it('shows the placeholder when nothing is chosen', () => {
        render(<ImportMonitoringSelect value="" onChange={vi.fn()} />);

        expect(screen.getByRole('combobox', { name: 'Import monitoring' })).toHaveTextContent('Select monitoring');
    });
});
