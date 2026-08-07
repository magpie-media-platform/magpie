import { parser } from './filter-query-parser.gen';

export function hasFilterQuerySyntaxErrors(query: string): boolean {
    let hasError = false;

    parser.parse(query).iterate({
        enter(node) {
            if (node.type.isError) {
                hasError = true;
            }
        },
    });

    return hasError;
}
