const REFERENCE_DEFINITION = /^[ \t]*\[[^\]\r\n]*\]:[^\r\n]*$/gm;
const COLLAPSED_REFERENCE_DEFINITION = /[ \t]\[[^\]\r\n]*\]:[ \t]*[a-zA-Z][a-zA-Z0-9+.-]*:\/\/\S+/g;
const COLLAPSED_LINK_BULLET = /(?<=[.:)\]*])[ \t]+[*+-][ \t]+(?=!?\[[^\]\r\n]*\][([])/g;
const IMAGE = /!\[([^\]]*)\]\([^)]*\)/g;
const INLINE_LINK = /\[([^\]]*)\]\([^)]*\)/g;
const REFERENCE_LINK = /\[([^\]]*)\]\[[^\]\r\n]*\]/g;
const TAG_BESIDE_PUNCTUATION = /(?<=[(["'“‘])<\/?[a-zA-Z][^>]*>|<\/?[a-zA-Z][^>]*>(?=[.,;:!?)\]"'”’])/g;
const TAG_OR_AUTOLINK = /<\/?[a-zA-Z][^>]*>/g;
const BARE_URL = /[a-zA-Z][a-zA-Z0-9+.-]*:\/\/\S+/g;
const THEMATIC_BREAK = /^[ \t]*(?:[-*_][ \t]*){3,}$/gm;
const COLLAPSED_THEMATIC_BREAK = /(?<=[.!?:)\]"'”’])[ \t]+(?:-{3,}|_{3,})(?=\s|$)|(?<![ \t])[ \t]+(?:-{3,}|_{3,})\s*$/g;
const EMPTY_QUOTE_LINE = /^[ \t]*(?:>[ \t]*)+$/gm;
const BLOCK_MARKER = /^[ \t]*(?:#{1,6}|(?:>[ \t]*)+|[*+-]|\d{1,2}[.)])[ \t]+/gm;
const COLLAPSED_QUOTE_MARKER = /([.!?:)][ \t]+)(?:>+[ \t]+)+(?=[\p{L}\p{N}"'“‘])/gu;
const PAIRED_EMPHASIS = /(^|[^\p{L}\p{N}])(\*{1,3}|_{1,3}|~~|`+)(?=[\p{L}\p{N}])(.*?\S)\2(?![\p{L}\p{N}])/gmu;
const WHITESPACE = /\s+/g;

export function toPlainTextSummary(text: string | null | undefined): string {
    if (!text) {
        return '';
    }

    return text
        .replace(REFERENCE_DEFINITION, ' ')
        .replace(COLLAPSED_REFERENCE_DEFINITION, ' ')
        .replace(COLLAPSED_LINK_BULLET, ' ')
        .replace(IMAGE, '$1')
        .replace(INLINE_LINK, '$1')
        .replace(REFERENCE_LINK, '$1')
        .replace(TAG_BESIDE_PUNCTUATION, '')
        .replace(TAG_OR_AUTOLINK, ' ')
        .replace(BARE_URL, ' ')
        .replace(THEMATIC_BREAK, ' ')
        .replace(COLLAPSED_THEMATIC_BREAK, ' ')
        .replace(EMPTY_QUOTE_LINE, ' ')
        .replace(BLOCK_MARKER, '')
        .replace(COLLAPSED_QUOTE_MARKER, '$1')
        .replace(PAIRED_EMPHASIS, '$1$3')
        .replace(WHITESPACE, ' ')
        .trim();
}
