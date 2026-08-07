import { LRLanguage, LanguageSupport } from '@codemirror/language';

import { parser } from './filter-query-parser.gen';
import { filterQueryHighlight } from './highlight';
import { filterQueryAutocomplete } from './autocomplete';
import { filterQueryLinter } from './lint';
import { type FieldRegistry, contentFieldRegistry, subjectFieldRegistry } from './field-registry';

const parserWithHighlight = parser.configure({
    props: [filterQueryHighlight],
});

const filterQueryLanguage = LRLanguage.define({
    name: 'filter-query',
    parser: parserWithHighlight,
    languageData: {},
});

export function filterQuery(context: 'content' | 'subject' = 'content'): LanguageSupport {
    const registry: FieldRegistry = context === 'content' ? contentFieldRegistry : subjectFieldRegistry;

    return new LanguageSupport(filterQueryLanguage, [
        filterQueryLanguage.data.of({
            autocomplete: filterQueryAutocomplete(registry),
        }),
        filterQueryLinter(),
    ]);
}
