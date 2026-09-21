import '@testing-library/jest-dom/vitest';
import * as matchers from 'vitest-axe/matchers';
import { expect } from 'vitest';

expect.extend(matchers);

if (typeof ResizeObserver === 'undefined') {
    global.ResizeObserver = class ResizeObserver {
        observe() {}
        unobserve() {}
        disconnect() {}
    };
}

if (typeof Element !== 'undefined' && typeof Element.prototype.hasPointerCapture !== 'function') {
    Element.prototype.hasPointerCapture = () => false;
}
if (typeof Element !== 'undefined' && typeof Element.prototype.setPointerCapture !== 'function') {
    Element.prototype.setPointerCapture = () => {};
}
if (typeof Element !== 'undefined' && typeof Element.prototype.releasePointerCapture !== 'function') {
    Element.prototype.releasePointerCapture = () => {};
}

if (typeof Element !== 'undefined' && typeof Element.prototype.scrollIntoView !== 'function') {
    Element.prototype.scrollIntoView = () => {};
}

const TEST_LOCALE = 'en-US';

const toLocaleDateString = Date.prototype.toLocaleDateString;
Date.prototype.toLocaleDateString = function (
    this: Date,
    locales?: Intl.LocalesArgument,
    options?: Intl.DateTimeFormatOptions,
) {
    return toLocaleDateString.call(this, locales ?? TEST_LOCALE, options);
};

const toLocaleTimeString = Date.prototype.toLocaleTimeString;
Date.prototype.toLocaleTimeString = function (
    this: Date,
    locales?: Intl.LocalesArgument,
    options?: Intl.DateTimeFormatOptions,
) {
    return toLocaleTimeString.call(this, locales ?? TEST_LOCALE, options);
};

const toDateLocaleString = Date.prototype.toLocaleString;
Date.prototype.toLocaleString = function (
    this: Date,
    locales?: Intl.LocalesArgument,
    options?: Intl.DateTimeFormatOptions,
) {
    return toDateLocaleString.call(this, locales ?? TEST_LOCALE, options);
};

const toNumberLocaleString = Number.prototype.toLocaleString;
Number.prototype.toLocaleString = function (locales, options) {
    return toNumberLocaleString.call(this, locales ?? TEST_LOCALE, options);
};

Intl.DateTimeFormat = new Proxy(Intl.DateTimeFormat, {
    construct: (target, [locales, options]: [Intl.LocalesArgument, Intl.DateTimeFormatOptions?]) =>
        new target(locales ?? TEST_LOCALE, options),
    apply: (target, _thisArg, [locales, options]: [Intl.LocalesArgument, Intl.DateTimeFormatOptions?]) =>
        target(locales ?? TEST_LOCALE, options),
});

Intl.NumberFormat = new Proxy(Intl.NumberFormat, {
    construct: (target, [locales, options]: [Intl.LocalesArgument, Intl.NumberFormatOptions?]) =>
        new target(locales ?? TEST_LOCALE, options),
    apply: (target, _thisArg, [locales, options]: [Intl.LocalesArgument, Intl.NumberFormatOptions?]) =>
        target(locales ?? TEST_LOCALE, options),
});

const WIDTH_FEATURE = /\((min|max)-width:\s*(\d+)px\)/g;

function matchesViewportWidth(query: string): boolean {
    return query
        .split(',')
        .some((group) =>
            [...group.matchAll(WIDTH_FEATURE)].every(([, bound, pixels]) =>
                bound === 'min' ? window.innerWidth >= Number(pixels) : window.innerWidth <= Number(pixels),
            ),
        );
}

if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') {
    Object.defineProperty(window, 'matchMedia', {
        writable: true,
        value: (query: string) => ({
            matches: matchesViewportWidth(query),
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        }),
    });
}
