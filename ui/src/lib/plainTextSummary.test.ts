import { describe, expect, it } from 'vitest';

import { toPlainTextSummary } from './plainTextSummary';

describe('toPlainTextSummary', () => {
    it('keeps the link text and drops the inline link target', () => {
        expect(toPlainTextSummary('A novel by [Jane Austen](https://openlibrary.org/authors/OL21594A).')).toBe(
            'A novel by Jane Austen.',
        );
    });

    it('keeps the link text and drops reference-style links and their definitions', () => {
        expect(
            toPlainTextSummary(
                'Pride and Prejudice ([source][1])\n\n----------\n\n[1]: https://openlibrary.org/works/OL66554W',
            ),
        ).toBe('Pride and Prejudice (source)');
    });

    it('drops bare link URLs', () => {
        expect(toPlainTextSummary('Read more at https://openlibrary.org/works/OL66554W now.')).toBe(
            'Read more at now.',
        );
    });

    it('keeps the words and drops emphasis, heading, quote, and list markers', () => {
        expect(
            toPlainTextSummary('# Summary\n\n> **Elizabeth** meets _Darcy_.\n\n* First `ball`\n1. Second ball'),
        ).toBe('Summary Elizabeth meets Darcy. First ball Second ball');
    });

    it('keeps an underscore inside a word', () => {
        expect(toPlainTextSummary('The file_name field.')).toBe('The file_name field.');
    });

    it('drops HTML tags', () => {
        expect(toPlainTextSummary('Austen <script>alert(1)</script> wrote <b>this</b>.')).toBe(
            'Austen alert(1) wrote this.',
        );
    });

    it('returns an empty string for missing text', () => {
        expect(toPlainTextSummary(null)).toBe('');
        expect(toPlainTextSummary(undefined)).toBe('');
    });
});
