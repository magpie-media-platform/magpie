import { type RenderResult } from '@testing-library/react';
import { configureAxe } from 'vitest-axe';
import { expect } from 'vitest';
import type { AxeResults, RunOptions } from 'axe-core';

export const axe = configureAxe({
    rules: {
        region: { enabled: false },

        'skip-link': { enabled: false },
    },
});

export const defaultAxeOptions: RunOptions = {
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

export async function expectNoA11yViolations(
    container: Element | RenderResult['container'],
    options?: RunOptions,
): Promise<void> {
    const results = await checkA11y(container, options);
    expect(results).toHaveNoViolations();
}

export function formatViolations(violations: AxeResults['violations']): string {
    if (violations.length === 0) {
        return 'No accessibility violations';
    }

    return violations
        .map((violation) => {
            const nodes = violation.nodes
                .map((node) => {
                    const target = node.target.join(', ');
                    const fix = node.failureSummary || 'No fix summary available';
                    return `  - ${target}\n    Fix: ${fix}`;
                })
                .join('\n');

            return `${violation.id} (${violation.impact}): ${violation.description}\n${nodes}`;
        })
        .join('\n\n');
}
