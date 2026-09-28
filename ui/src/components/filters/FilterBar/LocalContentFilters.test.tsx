import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { FilterBar } from './FilterBar';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => ({ contentTypes: [], subjectTypes: [], isLoading: false }),
}));
vi.mock('@/editor/FilterQueryEditor', () => ({
    FilterQueryEditor: ({ value, onChange }: { value: string; onChange: (value: string) => void }) => (
        <textarea aria-label="MQL" value={value} onChange={(event) => onChange(event.target.value)} />
    ),
}));

function Filters({ initial = '' }: { initial?: string }) {
    const [query, setQuery] = useState(initial);
    return (
        <>
            <FilterBar value={query} onChange={setQuery} localContentPage defaultExpanded />
            <output aria-label="Query">{query}</output>
        </>
    );
}

describe('local content filters', () => {
    it('places Monitoring, Status and the sync hint in the simple builder grid', () => {
        // Arrange
        // Act
        render(<Filters />);

        // Assert
        const builder = document.querySelector('[data-component="simple-filter-builder"]');
        const monitoringField = screen
            .getByRole('radiogroup', { name: 'Monitoring filter' })
            .closest('.simple-filter-builder__field');
        const statusField = screen
            .getByRole('combobox', { name: 'Status filter' })
            .closest('.simple-filter-builder__field');
        const syncHint = screen.getByText(/These filters and the table headers edit the same MQL query/);
        expect(monitoringField?.parentElement).toBe(builder);
        expect(statusField?.parentElement).toBe(builder);
        expect(syncHint.parentElement).toBe(builder);
    });

    it('shows only the MQL editor in advanced mode and restores synced fields in simple mode', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<Filters initial="monitoring != Unmonitored" />);

        // Act
        await user.click(screen.getByRole('button', { name: 'Advanced filter' }));

        // Assert
        expect(screen.queryByRole('radiogroup', { name: 'Monitoring filter' })).not.toBeInTheDocument();
        expect(screen.queryByRole('combobox', { name: 'Status filter' })).not.toBeInTheDocument();
        expect(screen.queryByText(/These filters and the table headers/)).not.toBeInTheDocument();

        // Act
        fireEvent.change(screen.getByLabelText('MQL'), {
            target: { value: 'fulfillment = Partial and monitoring = Unmonitored' },
        });
        await user.click(screen.getByRole('button', { name: 'Simple filter' }));

        // Assert
        expect(screen.queryByLabelText('MQL')).not.toBeInTheDocument();
        expect(screen.getByRole('radio', { name: /^Unmonitored:/ })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByRole('combobox', { name: 'Status filter' })).toHaveTextContent('Partial');
    });

    it('synchronizes monitoring, status, year and the original title control in one query', async () => {
        const user = userEvent.setup();
        render(<Filters initial={'title contains "Dune"'} />);
        await user.click(screen.getByRole('radio', { name: /^Monitored:/ }));
        await user.click(screen.getByRole('combobox', { name: 'Status filter' }));
        await user.click(screen.getByRole('option', { name: 'Missing' }));
        fireEvent.change(screen.getByLabelText('Release year'), { target: { value: '2020' } });
        expect(screen.getByLabelText('Query')).toHaveTextContent(
            'monitoring != Unmonitored and fulfillment = Missing and title contains "Dune" and publishDate = "2020"',
        );
        await user.click(screen.getByRole('radio', { name: /^Any:/ }));
        expect(screen.getByLabelText('Query')).not.toHaveTextContent('monitoring');
        expect(screen.getByLabelText('Query')).toHaveTextContent('fulfillment = Missing');
        expect(screen.queryByRole('radio', { name: /Inherit/ })).not.toBeInTheDocument();
    });

    it('switches between the Shotgun and monitoring predicates without leaving both', async () => {
        const user = userEvent.setup();
        render(<Filters initial="monitoring = Unmonitored" />);

        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
        expect(screen.getByLabelText('Query')).toHaveTextContent(/^monitoring = Shotgun$/);
        expect(screen.getByRole('radio', { name: /^Shotgun:/ })).toHaveAttribute('aria-checked', 'true');

        await user.click(screen.getByRole('radio', { name: /^Monitored:/ }));
        expect(screen.getByLabelText('Query')).toHaveTextContent(/^monitoring != Unmonitored$/);

        await user.click(screen.getByRole('radio', { name: /^Shotgun:/ }));
        await user.click(screen.getByRole('radio', { name: /^Any:/ }));
        expect(screen.getByLabelText('Query')).toBeEmptyDOMElement();
    });

    it('hides Monitoring and Status when a disjunction forces advanced mode', () => {
        // Arrange
        // Act
        render(<Filters initial={'title contains "a" or tag has "b"'} />);

        // Assert
        expect(screen.getByLabelText('MQL')).toHaveValue('title contains "a" or tag has "b"');
        expect(screen.queryByRole('radiogroup', { name: 'Monitoring filter' })).not.toBeInTheDocument();
        expect(screen.queryByRole('combobox', { name: 'Status filter' })).not.toBeInTheDocument();
    });

    it('uses the compact controls of the default filter bar', () => {
        // Arrange
        // Act
        render(<Filters />);

        // Assert
        expect(document.querySelector('[data-component="filter-bar"]')).toHaveClass('filter-bar filter-bar--expanded', {
            exact: true,
        });
        expect(screen.getByRole('combobox', { name: 'Status filter' })).toHaveAttribute('data-size', 'sm');
    });

    it('keeps no local-content size overrides and never lets a page layout collapse the filter bar', () => {
        // Arrange
        // Act
        const css = readFileSync(resolve(process.cwd(), 'src/components/filters/FilterBar/FilterBar.css'), 'utf8');

        // Assert
        expect(css).not.toContain('local-content');
        expect(css).toMatch(/\.filter-bar \{\s*flex-shrink: 0;/);
    });

    it('does not extend default modal or provider contexts', () => {
        render(<FilterBar value="" onChange={vi.fn()} defaultExpanded />);
        expect(screen.queryByRole('radiogroup', { name: 'Monitoring filter' })).not.toBeInTheDocument();
        expect(screen.queryByRole('combobox', { name: 'Status filter' })).not.toBeInTheDocument();
    });

    it('does not extend non-content filters even when opted in', () => {
        render(<FilterBar value="" context="subject" localContentPage onChange={vi.fn()} defaultExpanded />);
        expect(screen.queryByRole('radiogroup', { name: 'Monitoring filter' })).not.toBeInTheDocument();
    });
});
