import { useState } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { MultiSelectColumnFilter, type MultiSelectOption } from './MultiSelectColumnFilter';
import { encodeMultiValue, decodeMultiValue } from './multiSelectUrl';

const OPTIONS: MultiSelectOption[] = [
    { value: 'newznab', label: 'newznab' },
    { value: 'torznab', label: 'torznab' },
    { value: 'indexer-a', label: 'indexer-a' },
];

function Harness({ initial = [] as string[] }) {
    const [value, setValue] = useState<string[]>(initial);
    return (
        <div>
            <MultiSelectColumnFilter label="Provider" options={OPTIONS} value={value} onChange={setValue} />
            <output data-testid="selection">{value.join('|')}</output>
        </div>
    );
}

async function openMenu(user: ReturnType<typeof userEvent.setup>) {
    await user.click(document.querySelector('[data-action="column-filter-multi"]') as HTMLElement);
    return await screen.findByRole('listbox');
}

describe('MultiSelectColumnFilter', () => {
    it('marks the trigger inactive with no selection and active once values are chosen', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        const trigger = screen.getByRole('button', { name: 'Filter by Provider' });
        expect(trigger).toHaveAttribute('aria-pressed', 'false');
        expect(trigger).not.toHaveAttribute('data-active');

        const listbox = await openMenu(user);
        await user.click(within(listbox).getByRole('option', { name: 'newznab' }));

        const active = document.querySelector('[data-action="column-filter-multi"]') as HTMLElement;
        expect(active).toHaveAttribute('aria-pressed', 'true');
        expect(active).toHaveAttribute('data-active', 'true');
        expect(active).toHaveAttribute('aria-label', 'Provider filtered (1), edit filter');
    });

    it('selects multiple options and reports them all', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        const listbox = await openMenu(user);
        await user.click(within(listbox).getByRole('option', { name: 'newznab' }));
        await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'torznab' }));

        expect(screen.getByTestId('selection')).toHaveTextContent('newznab|torznab');
    });

    it('keeps the panel open across repeated mouse selections', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        const listbox = await openMenu(user);

        await user.click(within(listbox).getByRole('option', { name: 'newznab' }));
        expect(screen.getByRole('listbox')).toBeInTheDocument();

        await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'indexer-a' }));
        expect(screen.getByRole('listbox')).toBeInTheDocument();
        expect(screen.getByTestId('selection')).toHaveTextContent('newznab|indexer-a');

        await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'newznab' }));
        expect(screen.getByRole('listbox')).toBeInTheDocument();
        expect(screen.getByTestId('selection')).toHaveTextContent('indexer-a');
    });

    it('mixes mouse clicks and keyboard toggles in one open session', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        const listbox = await openMenu(user);
        await user.click(within(listbox).getByRole('option', { name: 'newznab' }));

        const search = screen.getByRole('textbox', { name: /search provider/i });
        await user.type(search, '{ArrowDown}[Space]');

        expect(screen.getByRole('listbox')).toBeInTheDocument();
        expect(screen.getByTestId('selection')).toHaveTextContent('newznab|torznab');
    });

    it('closes the panel on an outside click', async () => {
        const user = userEvent.setup();
        render(
            <div>
                <button type="button">outside</button>
                <Harness />
            </div>,
        );

        await openMenu(user);
        await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'newznab' }));
        expect(screen.getByRole('listbox')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'outside' }));
        await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());

        expect(screen.getByTestId('selection')).toHaveTextContent('newznab');
    });

    it('filters the option list by the typeahead query', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        await openMenu(user);
        await user.type(screen.getByRole('textbox', { name: /search provider/i }), 'inde');

        const listbox = screen.getByRole('listbox');
        const options = within(listbox).getAllByRole('option');
        expect(options).toHaveLength(1);
        expect(options[0]).toHaveTextContent('indexer-a');
    });

    it('Enter commits the sole typeahead match', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        await openMenu(user);
        const search = screen.getByRole('textbox', { name: /search provider/i });
        await user.type(search, 'torz{Enter}');

        expect(screen.getByTestId('selection')).toHaveTextContent('torznab');
    });

    it('navigates with arrow keys and toggles the active option with Enter', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        await openMenu(user);
        const search = screen.getByRole('textbox', { name: /search provider/i });

        await user.type(search, '{ArrowDown}{Enter}');

        expect(screen.getByTestId('selection')).toHaveTextContent('torznab');
        const active = screen.getByRole('option', { name: 'torznab' });
        expect(search).toHaveAttribute('aria-activedescendant', active.id);
    });

    it('wraps the cursor and toggles with Space when the search box is empty', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        await openMenu(user);
        const search = screen.getByRole('textbox', { name: /search provider/i });

        await user.type(search, '{ArrowUp}[Space]');

        expect(screen.getByTestId('selection')).toHaveTextContent('indexer-a');
    });

    it('closes the menu on Escape', async () => {
        const user = userEvent.setup();
        render(<Harness />);

        await openMenu(user);
        const search = screen.getByRole('textbox', { name: /search provider/i });
        await user.type(search, '{Escape}');

        expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    });

    it('removes a value via its chip', async () => {
        const user = userEvent.setup();
        render(<Harness initial={['newznab', 'torznab']} />);

        await openMenu(user);
        await user.click(screen.getByRole('button', { name: /remove newznab/i }));

        expect(screen.getByTestId('selection')).toHaveTextContent('torznab');
    });

    it('clears every value via the clear action', async () => {
        const user = userEvent.setup();
        render(<Harness initial={['newznab', 'torznab']} />);

        await openMenu(user);
        await user.click(screen.getByRole('button', { name: /^clear$/i }));

        expect(screen.getByTestId('selection')).toHaveTextContent('');
    });

    it('accepts bare string options', async () => {
        const user = userEvent.setup();
        function StringHarness() {
            const [value, setValue] = useState<string[]>([]);
            return (
                <div>
                    <MultiSelectColumnFilter label="Tags" options={['en', 'ja']} value={value} onChange={setValue} />
                    <output data-testid="selection">{value.join('|')}</output>
                </div>
            );
        }
        render(<StringHarness />);

        await user.click(screen.getByRole('button', { name: /filter by tags/i }));
        const listbox = await screen.findByRole('listbox');
        await user.click(within(listbox).getByRole('option', { name: 'ja' }));

        expect(screen.getByTestId('selection')).toHaveTextContent('ja');
    });
});

describe('multiSelectUrl serialization', () => {
    it('round-trips a selection through the URL string form', () => {
        const values = ['newznab', 'torznab'];
        const encoded = encodeMultiValue(values);
        expect(encoded).toBe('newznab,torznab');
        expect(decodeMultiValue(encoded)).toEqual(values);
    });

    it('encodes an empty selection as the empty string', () => {
        expect(encodeMultiValue([])).toBe('');
        expect(decodeMultiValue('')).toEqual([]);
        expect(decodeMultiValue(undefined)).toEqual([]);
        expect(decodeMultiValue(null)).toEqual([]);
    });

    it('preserves values containing a comma via percent-encoding', () => {
        const values = ['a,b', 'c'];
        const encoded = encodeMultiValue(values);
        expect(encoded).toBe('a%2Cb,c');
        expect(decodeMultiValue(encoded)).toEqual(values);
    });
});
