import type { Stack } from '@lezer/lr';

import {
    NotContains,
    NotEndsWith,
    NotHas,
    NotIn,
    NotNull,
    NotStartsWith,
    kw_and,
    kw_contains,
    kw_endswith,
    kw_false,
    kw_has,
    kw_in,
    kw_not,
    kw_null,
    kw_or,
    kw_startswith,
    kw_true,
} from './filter-query-parser.gen.terms';

const keywords: Record<string, number> = {
    not: kw_not,
    and: kw_and,
    or: kw_or,
    contains: kw_contains,
    startswith: kw_startswith,
    endswith: kw_endswith,
    null: kw_null,
    in: kw_in,
    has: kw_has,
    true: kw_true,
    false: kw_false,
};

const negatedKeywords: Record<string, number> = {
    '!has': NotHas,
    '!contains': NotContains,
    '!startswith': NotStartsWith,
    '!endswith': NotEndsWith,
    '!in': NotIn,
    '!null': NotNull,
};

export function specializeKeyword(value: string, _stack?: Stack): number {
    const normalized = value.toLowerCase();
    return keywords[normalized] ?? -1;
}

export function specializeNegatedKeyword(value: string, _stack?: Stack): number {
    const normalized = value.toLowerCase();
    return negatedKeywords[normalized] ?? -1;
}
