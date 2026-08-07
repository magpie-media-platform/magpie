import 'vitest';
import type { AxeMatchers } from 'vitest-axe/matchers';

declare module 'vitest' {
    interface Assertion<_T = unknown> {
        toHaveNoViolations: AxeMatchers['toHaveNoViolations'];
    }

    interface AsymmetricMatchersContaining {
        toHaveNoViolations: AxeMatchers['toHaveNoViolations'];
    }
}
