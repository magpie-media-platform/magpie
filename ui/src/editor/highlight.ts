import { styleTags, tags as t } from '@lezer/highlight';

export const filterQueryHighlight = styleTags({
    Identifier: t.variableName,
    String: t.string,
    Number: t.number,
    'contains startswith endswith': t.operatorKeyword,
    NotContains: t.operatorKeyword,
    NotStartsWith: t.operatorKeyword,
    NotEndsWith: t.operatorKeyword,
    has: t.keyword,
    NotHas: t.keyword,
    null: t.null,
    NotNull: t.null,
    in: t.operatorKeyword,
    NotIn: t.operatorKeyword,
    'and or': t.logicOperator,
    not: t.logicOperator,
    'CompareOp/...': t.compareOperator,
    '"(" ")"': t.paren,
    '","': t.separator,
});
