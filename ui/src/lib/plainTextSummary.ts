const REFERENCE_DEFINITION = /^[ \t]*\[[^\]\r\n]*\]:[^\r\n]*$/gm;
const IMAGE = /!\[([^\]]*)\]\([^)]*\)/g;
const INLINE_LINK = /\[([^\]]*)\]\([^)]*\)/g;
const REFERENCE_LINK = /\[([^\]]*)\]\[[^\]\r\n]*\]/g;
const TAG_OR_AUTOLINK = /<\/?[a-zA-Z][^>]*>/g;
const BARE_URL = /[a-zA-Z][a-zA-Z0-9+.-]*:\/\/\S+/g;
const THEMATIC_BREAK = /^[ \t]*(?:[-*_][ \t]*){3,}$/gm;
const BLOCK_MARKER = /^[ \t]*(?:#{1,6}|>|[*+-]|\d+[.)])[ \t]+/gm;
const EMPHASIS_MARKER = /\*+|~~|`+/g;
const UNDERSCORE_EMPHASIS = /(^|\W)_+([^_\r\n]+)_+(?=\W|$)/g;
const WHITESPACE = /\s+/g;
const SPACE_BEFORE_PUNCTUATION = / ([.,;:!?)\]])/g;

export function toPlainTextSummary(text: string | null | undefined): string {
    if (!text) {
        return '';
    }

    return text
        .replace(REFERENCE_DEFINITION, ' ')
        .replace(IMAGE, '$1')
        .replace(INLINE_LINK, '$1')
        .replace(REFERENCE_LINK, '$1')
        .replace(TAG_OR_AUTOLINK, ' ')
        .replace(BARE_URL, ' ')
        .replace(THEMATIC_BREAK, ' ')
        .replace(BLOCK_MARKER, '')
        .replace(UNDERSCORE_EMPHASIS, '$1$2')
        .replace(EMPHASIS_MARKER, '')
        .replace(WHITESPACE, ' ')
        .replace(SPACE_BEFORE_PUNCTUATION, '$1')
        .trim();
}
