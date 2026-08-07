import { type RenderResult } from '@testing-library/react';
import { configureAxe } from 'vitest-axe';
import type { AxeResults, RunOptions } from 'axe-core';

const axe = configureAxe({
    rules: {
        'color-contrast': { enabled: false },
        region: { enabled: false },
        'skip-link': { enabled: false },
    },
});

const defaultAxeOptions: RunOptions = {
    runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa', 'best-practice'],
    },
};

export async function checkA11y(
    container: Element | RenderResult['container'],
    options?: RunOptions,
): Promise<AxeResults> {
    const mergedOptions = {
        ...defaultAxeOptions,
        ...options,
    };
    return axe(container, mergedOptions);
}
