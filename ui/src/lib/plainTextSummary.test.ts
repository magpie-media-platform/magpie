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

    it.each([
        ['Bold', '**Also contained in:**', 'Also contained in:'],
        ['EmphasisAndStrikethrough', 'A *very* good ~~bad~~ show.', 'A very good bad show.'],
        ['Code', 'Run `npm install` first.', 'Run npm install first.'],
    ])('toPlainTextSummary_DropsPairedEmphasis_WhenTextHas%s', (_, text, expected) => {
        // Arrange
        // Act
        const summary = toPlainTextSummary(text);

        // Assert
        expect(summary).toBe(expected);
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

    it.each([
        [
            'RawMarkdown',
            "[Comment by Kim Stanley Robinson, on The Guardian's website][1]:\r\nThe Left Hand of Darkness by Ursula K Le Guin (1969)\r\n\r\n> One of my favorite novels is The Left Hand of Darkness.\r\n\r\n> In this case, the thought experiment is quickly revealed.\r\n\r\n\r\n  [1]: https://www.theguardian.com/books/2011/may/14/science-fiction-authors-choice",
            "Comment by Kim Stanley Robinson, on The Guardian's website: The Left Hand of Darkness by Ursula K Le Guin (1969) One of my favorite novels is The Left Hand of Darkness. In this case, the thought experiment is quickly revealed.",
        ],
        [
            'CollapsedMarkdown',
            "[Comment by Kim Stanley Robinson, on The Guardian's website][1]: The Left Hand of Darkness by Ursula K Le Guin (1969) > One of my favorite novels is The Left Hand of Darkness. > In this case, the thought experiment is quickly revealed. [1]: https://www.theguardian.com/books/2011/may/14/science-fiction-authors-choice",
            "Comment by Kim Stanley Robinson, on The Guardian's website: The Left Hand of Darkness by Ursula K Le Guin (1969) One of my favorite novels is The Left Hand of Darkness. In this case, the thought experiment is quickly revealed.",
        ],
        [
            'CollapsedThematicBreak',
            'Pride and Prejudice ([source][1]) ---------- [1]: https://openlibrary.org/works/OL66554W',
            'Pride and Prejudice (source)',
        ],
        ['CollapsedQuoteAfterSentence', 'He left. > Quoted text follows.', 'He left. Quoted text follows.'],
        [
            'RawQuoteWithEmptyQuoteLine',
            'Intro:\n\n> First quoted paragraph.\n>\n> Second quoted paragraph.',
            'Intro: First quoted paragraph. Second quoted paragraph.',
        ],
        [
            'CollapsedQuoteWithEmptyQuoteLine',
            'Intro: > First quoted paragraph. > > Second quoted paragraph.',
            'Intro: First quoted paragraph. Second quoted paragraph.',
        ],
        ['RawNestedQuote', 'Intro:\n\n> Outer.\n>> Nested.', 'Intro: Outer. Nested.'],
        ['CollapsedNestedQuote', 'Intro: > Outer. >> Nested.', 'Intro: Outer. Nested.'],
        [
            'CollapsedLinkBullets',
            'Story text. ---------- **Also contained in:** - [Omnibus](https://openlibrary.org/works/OL1W) - [Collection](https://openlibrary.org/works/OL2W)',
            'Story text. Also contained in: Omnibus Collection',
        ],
        ['NumberedList', 'Raw:\n1. First\n2. Second', 'Raw: First Second'],
    ])('toPlainTextSummary_ReturnsReadableProse_WhenDescriptionIs%s', (_, text, expected) => {
        // Arrange
        // Act
        const summary = toPlainTextSummary(text);

        // Assert
        expect(summary).toBe(expected);
    });

    it.each([
        'Devs focuses on a young software engineer named Lily Chan who works for Amaya, a cutting-edge tech company based in Silicon Valley.',
        'Cobb, a skilled thief who commits corporate espionage by infiltrating the subconscious of his targets is offered a chance to regain his old life as payment for a task considered to be impossible: "inception", the implantation of another person\'s idea into a target\'s subconscious.',
        'Capsmusic LTD. artist',
        '2010 film directed by Christopher Nolan',
        'Rated 5 > 3 by critics.',
        'Loosely based on the real-life M*A*S*H unit 8055, life at the 4077 revolved around the surgeons.',
        'It explores sex, relationships, and the general clusterf*ck that is life through a series of scripted scenes.',
        '$#*! My Dad Says is based on the popular Twitter feed by Justin Halpern.',
        '1715. The Golden Age of Piracy. New Providence Island is a haven for pirates.',
        'For the first time ever, cameras were granted access to three convents* where the women live and work together.',
        "A partner, who has the sparkle of Data from `Star Trek' combined with the body of a model.",
        'A horror/drama based on novels called The Southern American Vampires Mysteries . It focuses on Sookie Stackhouse.',
        'He paused --- then left.',
        'Fill in ___ blanks.',
    ])('toPlainTextSummary_ReturnsTextUnchanged_WhenDescriptionIsPlain: %s', (text) => {
        // Arrange
        // Act
        const summary = toPlainTextSummary(text);

        // Assert
        expect(summary).toBe(text);
    });

    it('collapses a long whitespace run in linear time', { timeout: 500 }, () => {
        // Arrange
        const text = `Plain words.${' '.repeat(50_000)}More words.`;

        // Act
        const summary = toPlainTextSummary(text);

        // Assert
        expect(summary).toBe('Plain words. More words.');
    });
});
