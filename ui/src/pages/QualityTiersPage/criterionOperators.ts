export type CriterionOperator =
    | 'Equals'
    | 'NotEquals'
    | 'In'
    | 'NotIn'
    | 'Contains'
    | 'NotContains'
    | 'StartsWith'
    | 'NotStartsWith'
    | 'EndsWith'
    | 'NotEndsWith'
    | 'ContainsWord'
    | 'Regex'
    | 'GreaterThan'
    | 'GreaterThanOrEqual'
    | 'LessThan'
    | 'LessThanOrEqual';

interface OperatorOption {
    value: CriterionOperator;
    label: string;
    symbol: string;
}

const DEFAULT_NUMERIC_FIELDS = new Set(['size', 'age']);

const SET_OPERATORS = new Set<string>(['In', 'NotIn']);

const STRING_OPERATORS: OperatorOption[] = [
    { value: 'Equals', label: 'Equals', symbol: '=' },
    { value: 'NotEquals', label: 'Not Equals', symbol: '!=' },
    { value: 'In', label: 'In', symbol: '∈' },
    { value: 'NotIn', label: 'Not In', symbol: '∉' },
    { value: 'Contains', label: 'Contains', symbol: '*=' },
    { value: 'NotContains', label: 'Not Contains', symbol: '!*' },
    { value: 'StartsWith', label: 'Starts With', symbol: '^=' },
    { value: 'NotStartsWith', label: 'Not Starts With', symbol: '!^' },
    { value: 'EndsWith', label: 'Ends With', symbol: '=$' },
    { value: 'NotEndsWith', label: 'Not Ends With', symbol: '!$' },
    { value: 'ContainsWord', label: 'Contains Word', symbol: '~=' },
    { value: 'Regex', label: 'Regex', symbol: '.*' },
];

const NUMERIC_OPERATORS: OperatorOption[] = [
    { value: 'Equals', label: 'Equals', symbol: '=' },
    { value: 'NotEquals', label: 'Not Equals', symbol: '!=' },
    { value: 'GreaterThan', label: 'Greater Than', symbol: '>' },
    { value: 'GreaterThanOrEqual', label: 'At Least', symbol: '>=' },
    { value: 'LessThan', label: 'Less Than', symbol: '<' },
    { value: 'LessThanOrEqual', label: 'At Most', symbol: '<=' },
];

export function isNumericField(field: string, numericFields?: Set<string>): boolean {
    const set = numericFields ?? DEFAULT_NUMERIC_FIELDS;
    return set.has(field.toLowerCase());
}

export function operatorsForField(field: string, numericFields?: Set<string>): OperatorOption[] {
    return isNumericField(field, numericFields) ? NUMERIC_OPERATORS : STRING_OPERATORS;
}

export function isSetOperator(operator: string): boolean {
    return SET_OPERATORS.has(operator);
}
