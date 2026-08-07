import { describe, expect, it } from 'vitest';

import { parser } from './filter-query-parser.gen';

function parseQuery(query: string): { success: boolean; errorNodes: string[] } {
    const tree = parser.parse(query);
    const errorNodes: string[] = [];

    tree.iterate({
        enter(node) {
            if (node.name === '\u26A0') {
                const errorText = query.slice(node.from, node.to) || '<empty>';
                errorNodes.push(`Error at ${node.from}-${node.to}: ${errorText}`);
            }
        },
    });

    return { success: errorNodes.length === 0, errorNodes };
}

function expectParses(query: string): void {
    const result = parseQuery(query);
    expect(result.success, `Query "${query}" failed to parse: ${result.errorNodes.join(', ')}`).toBe(true);
}

function expectFailsToParse(query: string): void {
    const result = parseQuery(query);
    expect(result.success, `Query "${query}" should have failed to parse`).toBe(false);
}

describe('Filter Query Grammar', () => {
    describe('Scalar Expressions', () => {
        describe('Comparison Operators', () => {
            it('parses equals with string', () => {
                expectParses('name = "Breaking Bad"');
            });

            it('parses equals with number', () => {
                expectParses('fileSize > 1000000000');
            });

            it('parses equals with boolean true', () => {
                expectParses('monitoring = true');
            });

            it('parses equals with boolean false', () => {
                expectParses('monitoring = false');
            });

            it('parses not equals', () => {
                expectParses('status != "active"');
            });

            it('parses greater than or equal', () => {
                expectParses('year >= 2020');
            });

            it('parses less than', () => {
                expectParses('duration < "02:00:00"');
            });

            it('parses less than or equal', () => {
                expectParses('count <= 10');
            });
        });

        describe('String Operators', () => {
            it('parses contains', () => {
                expectParses('title contains "Star"');
            });

            it('parses not contains', () => {
                expectParses('title !contains "bad"');
            });

            it('parses startswith', () => {
                expectParses('name startswith "The"');
            });

            it('parses not startswith', () => {
                expectParses('name !startswith "A"');
            });

            it('parses endswith', () => {
                expectParses('name endswith "ion"');
            });

            it('parses not endswith', () => {
                expectParses('name !endswith "s"');
            });
        });

        describe('Null Operators', () => {
            it('parses has-no-value semantics for monitoring', () => {
                expectParses('monitoring null');
                expectParses('monitoring !null');
            });

            it('parses null', () => {
                expectParses('overview null');
            });

            it('parses not null', () => {
                expectParses('overview !null');
            });
        });

        describe('In Operators', () => {
            it('parses in with strings', () => {
                expectParses('contentType in ("movie", "show")');
            });

            it('parses not in with identifiers', () => {
                expectParses('status !in (Active, Ended)');
            });

            it('parses in with single value', () => {
                expectParses('status in ("active")');
            });

            it('parses in with multiple values', () => {
                expectParses('status in (Active, Ended, Cancelled)');
            });

            it('parses in with mixed types', () => {
                expectParses('field in ("test", 42, Active)');
            });
        });
    });

    describe('Binary Expressions', () => {
        it('parses and expression', () => {
            expectParses('monitoring = true and contentType = "show"');
        });

        it('parses or expression', () => {
            expectParses('genre has "Action" or genre has "Comedy"');
        });

        it('parses multiple ands', () => {
            expectParses('a = 1 and b = 2 and c = 3 and d = 4');
        });

        it('parses multiple ors', () => {
            expectParses('a = 1 or b = 2 or c = 3 or d = 4');
        });

        it('parses mixed precedence (and binds before or)', () => {
            expectParses('a = 1 or b = 2 and c = 3');
        });

        it('parses with parentheses to override precedence', () => {
            expectParses('(a = 1 or b = 2) and c = 3');
        });

        it('parses lowercase keywords', () => {
            expectParses('name = "a" and type = "b" or status = "c"');
        });

        it('parses uppercase keywords (case-insensitive)', () => {
            expectParses('name = "a" AND type = "b" OR status = "c"');
        });

        it('parses mixed-case keywords (case-insensitive)', () => {
            expectParses('name = "a" And type = "b" Or status = "c"');
        });
    });

    describe('Not Expressions', () => {
        it('parses not with collection expression', () => {
            expectParses('not genre has "Horror"');
        });

        it('parses not with grouped expression', () => {
            expectParses('not (a = 1 or b = 2)');
        });
    });

    describe('Group Expressions', () => {
        it('parses parenthesized expression', () => {
            expectParses('(name = "test")');
        });

        it('parses double parenthesized expression', () => {
            expectParses('((a = 1))');
        });

        it('parses nested groups in complex expression', () => {
            expectParses('(a = 1 or (b = 2 and c = 3))');
        });
    });

    describe('Collection Expressions', () => {
        describe('Simple Has', () => {
            it('parses has with string', () => {
                expectParses('genre has "Action"');
            });

            it('parses has with identifier', () => {
                expectParses('tags has Active');
            });

            it('parses not has', () => {
                expectParses('genre !has "Horror"');
            });
        });

        describe('Complex Has (Item Expressions)', () => {
            it('parses has with field comparison', () => {
                expectParses('genres has (name startswith "A" and name endswith "ion")');
            });

            it('parses has with multiple conditions', () => {
                expectParses('awards has (body = "Academy Awards" and name = "Best Picture" and year >= 2000)');
            });

            it('parses has with or in item expression', () => {
                expectParses('awards has (name = "Oscar" or (name = "Emmy" and year >= 2010))');
            });

            it('parses has with simple value in parens', () => {
                expectParses('genre has ("Action")');
            });

            it('parses has with grouped item', () => {
                expectParses('awards has ((name = "Oscar"))');
            });
        });
    });

    describe('Complex Real-World Queries', () => {
        it('parses complex query with multiple conditions', () => {
            expectParses(
                'monitoring = true and contentType = "movie" and genre has "Action" and genre !has "Horror" and fileSize > 1000000000',
            );
        });

        it('parses query with monitoring and genres', () => {
            expectParses('Monitoring = true and (Genre has "Action" or Genre has "Comedy")');
        });

        it('parses query with multiple sections', () => {
            expectParses('ContentType = "movie" and FileSize > 5000000000');
        });

        it('parses complex genre expression', () => {
            expectParses('Genre has "Action" and Genre !has "Horror"');
        });
    });

    describe('Values', () => {
        describe('Strings', () => {
            it('parses empty string', () => {
                expectParses('name = ""');
            });

            it('parses string with escaped quote', () => {
                expectParses('name = "Hello \\"World\\""');
            });

            it('parses string with escaped backslash', () => {
                expectParses('path = "path\\\\to\\\\file"');
            });

            it('parses string with escape sequences', () => {
                expectParses('text = "line1\\nline2\\ttab"');
            });
        });

        describe('Numbers', () => {
            it('parses integer', () => {
                expectParses('count = 12345');
            });

            it('parses decimal', () => {
                expectParses('rating = 123.45');
            });

            it('parses negative number', () => {
                expectParses('offset = -42');
            });
        });

        describe('Identifiers', () => {
            it('parses boolean true', () => {
                expectParses('enabled = true');
            });

            it('parses boolean false', () => {
                expectParses('enabled = false');
            });

            it('parses enum value', () => {
                expectParses('status = Active');
            });
        });
    });

    describe('Round-Trip Test Queries (from C# tests)', () => {
        it('parses: Name = "Breaking Bad"', () => {
            expectParses('Name = "Breaking Bad"');
        });

        it('parses: Monitoring = true', () => {
            expectParses('Monitoring = true');
        });

        it('parses: FileSize > 1000000000', () => {
            expectParses('FileSize > 1000000000');
        });

        it('parses: Title contains "Star"', () => {
            expectParses('Title contains "Star"');
        });

        it('parses: PublishDate >= "2020-01-01"', () => {
            expectParses('PublishDate >= "2020-01-01"');
        });

        it('parses: Duration > "02:00:00"', () => {
            expectParses('Duration > "02:00:00"');
        });

        it('parses: Name null', () => {
            expectParses('Name null');
        });

        it('parses: Name !null', () => {
            expectParses('Name !null');
        });

        it('parses: ContentType in ("movie", "show")', () => {
            expectParses('ContentType in ("movie", "show")');
        });

        it('parses: ContentType !in ("movie", "show")', () => {
            expectParses('ContentType !in ("movie", "show")');
        });

        it('parses: Genre has "Action"', () => {
            expectParses('Genre has "Action"');
        });

        it('parses: Genre !has "Horror"', () => {
            expectParses('Genre !has "Horror"');
        });

        it('parses: ParentId = "12345678-1234-1234-1234-123456789abc"', () => {
            expectParses('ParentId = "12345678-1234-1234-1234-123456789abc"');
        });

        it('parses: ContentType = "show" and Monitoring = true', () => {
            expectParses('ContentType = "show" and Monitoring = true');
        });

        it('parses: Genre has "Action" or Genre has "Comedy"', () => {
            expectParses('Genre has "Action" or Genre has "Comedy"');
        });

        it('parses: Genre has "Action" and Genre !has "Horror"', () => {
            expectParses('Genre has "Action" and Genre !has "Horror"');
        });

        it('parses: Genres has (Name startswith "A" and Name endswith "ion")', () => {
            expectParses('Genres has (Name startswith "A" and Name endswith "ion")');
        });

        it('parses: Awards has (Body = "Academy Awards" and Name = "Best Picture" and Year >= 2000)', () => {
            expectParses('Awards has (Body = "Academy Awards" and Name = "Best Picture" and Year >= 2000)');
        });

        it('parses: ExternalIds has (Source = "imdb" and Value = "tt0903747")', () => {
            expectParses('ExternalIds has (Source = "imdb" and Value = "tt0903747")');
        });

        it('parses: ContentType = "movie" and FileSize > 5000000000', () => {
            expectParses('ContentType = "movie" and FileSize > 5000000000');
        });

        it('parses: Monitoring = true and (Genre has "Action" or Genre has "Comedy")', () => {
            expectParses('Monitoring = true and (Genre has "Action" or Genre has "Comedy")');
        });

        it('parses: Kind = "person"', () => {
            expectParses('Kind = "person"');
        });

        it('parses: DateLastRefreshed < "2024-01-01"', () => {
            expectParses('DateLastRefreshed < "2024-01-01"');
        });

        it('parses: Title contains "Smith"', () => {
            expectParses('Title contains "Smith"');
        });

        it('parses: BirthDate >= "1990-01-01"', () => {
            expectParses('BirthDate >= "1990-01-01"');
        });

        it('parses: Kind = "person" and Title contains "John"', () => {
            expectParses('Kind = "person" and Title contains "John"');
        });

        it('parses: Award has "Oscar"', () => {
            expectParses('Award has "Oscar"');
        });

        it('parses: Awards has (Body = "Academy Awards" and Name = "Best Actor" and Year >= 2000)', () => {
            expectParses('Awards has (Body = "Academy Awards" and Name = "Best Actor" and Year >= 2000)');
        });

        it('parses: ExternalIds has (Source = "imdb" and Value = "nm0000138")', () => {
            expectParses('ExternalIds has (Source = "imdb" and Value = "nm0000138")');
        });

        it('parses: Images has (Type = "poster")', () => {
            expectParses('Images has (Type = "poster")');
        });

        it('parses: Award !has "Razzie"', () => {
            expectParses('Award !has "Razzie"');
        });

        it('parses: Award has "Oscar" or Award has "Emmy"', () => {
            expectParses('Award has "Oscar" or Award has "Emmy"');
        });

        it('parses: duration > "2h"', () => {
            expectParses('duration > "2h"');
        });
    });

    describe('Error Cases (should fail to parse)', () => {
        it('fails on empty query', () => {
            const tree = parser.parse('');
            expect(tree.length).toBe(0);
        });

        it('fails on missing operator', () => {
            expectFailsToParse('name "test"');
        });

        it('fails on missing value', () => {
            expectFailsToParse('name =');
        });

        it('fails on missing right paren', () => {
            expectFailsToParse('(name = "test"');
        });

        it('fails on unexpected token after expression', () => {
            expectFailsToParse('name = "test" extra');
        });

        it('fails on empty in list', () => {
            expectFailsToParse('type in ()');
        });

        it('fails on lexer error (invalid character)', () => {
            expectFailsToParse('name @ "test"');
        });

        it('fails on missing field name', () => {
            expectFailsToParse('= "test"');
        });

        it('fails on missing right paren in collection', () => {
            expectFailsToParse('genres has (name = "Action"');
        });

        it('fails on missing right paren in value list', () => {
            expectFailsToParse('type in ("a", "b"');
        });
    });

    describe('Edge Cases', () => {
        it('handles whitespace correctly', () => {
            expectParses('  name   =   "test"  ');
        });

        it('handles newlines', () => {
            expectParses('name = "test"\nand\nage > 10');
        });

        it('handles tabs', () => {
            expectParses('name\t=\t"test"');
        });

        it('handles identifiers with underscores', () => {
            expectParses('field_name = "value"');
        });

        it('handles identifiers with numbers', () => {
            expectParses('field123 = "value"');
        });

        it('handles very long numbers', () => {
            expectParses('size > 999999999999999999');
        });

        it('handles decimal numbers', () => {
            expectParses('rating >= 7.5');
        });
    });

    describe('Case-Insensitive Keywords', () => {
        it('parses uppercase AND', () => {
            expectParses('name = "a" AND type = "b"');
        });

        it('parses uppercase OR', () => {
            expectParses('name = "a" OR type = "b"');
        });

        it('parses uppercase NOT', () => {
            expectParses('NOT genre has "Horror"');
        });

        it('parses uppercase CONTAINS', () => {
            expectParses('title CONTAINS "Star"');
        });

        it('parses uppercase STARTSWITH', () => {
            expectParses('name STARTSWITH "The"');
        });

        it('parses uppercase ENDSWITH', () => {
            expectParses('name ENDSWITH "ion"');
        });

        it('parses uppercase NULL', () => {
            expectParses('overview NULL');
        });

        it('parses uppercase IN', () => {
            expectParses('status IN ("active", "pending")');
        });

        it('parses uppercase HAS', () => {
            expectParses('genre HAS "Action"');
        });

        it('parses mixed-case Contains', () => {
            expectParses('title Contains "Star"');
        });

        it('parses mixed-case StartsWith', () => {
            expectParses('name StartsWith "The"');
        });

        it('parses mixed-case EndsWith', () => {
            expectParses('name EndsWith "ion"');
        });

        it('parses complex query with mixed-case keywords', () => {
            expectParses('name CONTAINS "test" AND status = Active OR type HAS "movie"');
        });

        it.each([
            ['!HAS', 'genre !HAS "Horror"'],
            ['!Contains', 'title !Contains "bad"'],
            ['!STARTSWITH', 'name !STARTSWITH "A"'],
            ['!EndsWith', 'name !EndsWith "s"'],
            ['!IN', 'status !IN (Active, Ended)'],
            ['!Null', 'overview !Null'],
        ])('parses case-insensitive negated keyword %s', (_operator, query) => {
            expectParses(query);
        });
    });

    describe('Tokenizer Parity', () => {
        it('treats a negated keyword prefix as part of one invalid token', () => {
            expectFailsToParse('genre !hasValue');
            expectFailsToParse('status !inList (Active)');
        });

        it('handles Unicode whitespace accepted by MagpieQL', () => {
            expectParses('name\u00a0=\u2003"test"');
        });
    });
});
