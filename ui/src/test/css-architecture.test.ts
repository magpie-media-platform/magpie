import { readdirSync, readFileSync, statSync } from 'fs';
import { basename, resolve } from 'path';
import { describe, expect, it } from 'vitest';

const stylesDir = resolve(__dirname, '../styles');

function readStyleFile(filename: string): string {
    const filepath = resolve(stylesDir, filename);
    return readFileSync(filepath, 'utf-8');
}

describe('CSS Architecture', () => {
    describe('tokens.css', () => {
        const tokens = readStyleFile('tokens.css');

        it('defines background color tokens', () => {
            expect(tokens).toContain('--bg-primary:');
            expect(tokens).toContain('--bg-secondary:');
            expect(tokens).toContain('--bg-tertiary:');
        });

        it('defines accent color tokens', () => {
            expect(tokens).toContain('--accent-primary:');
            expect(tokens).toContain('--accent-secondary:');
        });

        it('defines text color tokens', () => {
            expect(tokens).toContain('--text-primary:');
            expect(tokens).toContain('--text-secondary:');
            expect(tokens).toContain('--text-muted:');
        });

        it('defines border color tokens', () => {
            expect(tokens).toContain('--border-default:');
            expect(tokens).toContain('--border-accent:');
        });

        it('defines status color tokens', () => {
            expect(tokens).toContain('--status-healthy:');
            expect(tokens).toContain('--status-unhealthy:');
            expect(tokens).toContain('--status-degraded:');
            expect(tokens).toContain('--status-unknown:');
            expect(tokens).toContain('--status-stabilizing:');
        });

        it('defines fulfillment color tokens', () => {
            expect(tokens).toContain('--fulfillment-missing:');
            expect(tokens).toContain('--fulfillment-upgradable:');
            expect(tokens).toContain('--fulfillment-fulfilled:');
            expect(tokens).toContain('--fulfillment-external:');
        });

        it('defines typography tokens', () => {
            expect(tokens).toContain('--font-sans:');
            expect(tokens).toContain('--font-mono:');
            expect(tokens).toContain("'Outfit'");
            expect(tokens).toContain("'JetBrains Mono'");
        });

        it('defines spacing scale tokens', () => {
            expect(tokens).toContain('--space-xs:');
            expect(tokens).toContain('--space-sm:');
            expect(tokens).toContain('--space-md:');
            expect(tokens).toContain('--space-lg:');
            expect(tokens).toContain('--space-xl:');
            expect(tokens).toContain('--space-2xl:');
        });

        it('defines radius tokens', () => {
            expect(tokens).toContain('--radius-sm:');
            expect(tokens).toContain('--radius-md:');
            expect(tokens).toContain('--radius-lg:');
        });

        it('defines shadow tokens', () => {
            expect(tokens).toContain('--shadow-sm:');
            expect(tokens).toContain('--shadow-md:');
            expect(tokens).toContain('--shadow-lg:');
        });

        it('defines focus ring tokens', () => {
            expect(tokens).toContain('--focus-ring-width:');
            expect(tokens).toContain('--focus-ring-offset:');
            expect(tokens).toContain('--focus-ring-color:');
        });

        it('uses Tokyo Night color palette', () => {
            expect(tokens).toContain('#1a1b26');
            expect(tokens).toContain('#24283b');
            expect(tokens).toContain('#7aa2f7');
            expect(tokens).toContain('#c0caf5');
        });
    });

    describe('reset.css', () => {
        const reset = readStyleFile('reset.css');

        it('uses @layer reset', () => {
            expect(reset).toContain('@layer reset');
        });

        it('applies border-box sizing', () => {
            expect(reset).toContain('box-sizing: border-box');
        });

        it('resets margins and padding', () => {
            expect(reset).toContain('margin: 0');
            expect(reset).toContain('padding: 0');
        });
    });

    describe('base.css', () => {
        const base = readStyleFile('base.css');

        it('uses @layer base', () => {
            expect(base).toContain('@layer base');
        });

        it('sets dark color scheme', () => {
            expect(base).toContain('color-scheme: dark');
        });

        it('applies font-family token', () => {
            expect(base).toContain('font-family: var(--font-sans)');
        });

        it('defines focus-visible styles', () => {
            expect(base).toContain(':focus-visible');
            expect(base).toContain('--focus-ring-color');
        });
    });

    describe('motion.css', () => {
        const motion = readStyleFile('motion.css');

        it('uses @layer base', () => {
            expect(motion).toContain('@layer base');
        });

        it('defines animation keyframes', () => {
            expect(motion).toContain('@keyframes fadeIn');
            expect(motion).toContain('@keyframes pulse');
        });

        it('respects prefers-reduced-motion', () => {
            expect(motion).toContain('@media (prefers-reduced-motion: reduce)');
            expect(motion).toContain('animation-duration: 0.01ms');
            expect(motion).toContain('transition-duration: 0.01ms');
        });
    });

    describe('utilities.css', () => {
        const utilities = readStyleFile('utilities.css');

        it('uses @layer utilities', () => {
            expect(utilities).toContain('@layer utilities');
        });

        it('defines sr-only class', () => {
            expect(utilities).toContain('.sr-only');
        });

        it('does not define skip-link class (owned by SkipLink component)', () => {
            expect(utilities).not.toContain('.skip-link');
        });

        it('defines truncate class', () => {
            expect(utilities).toContain('.truncate');
        });

        it('defines page-count class with monospace font token', () => {
            expect(utilities).toContain('.page-count');
            expect(utilities).toContain('font-family: var(--font-mono)');
        });

        it('defines tab-count class with monospace font token', () => {
            expect(utilities).toContain('.tab-count');
            expect(utilities).toContain('font-family: var(--font-mono)');
        });
    });

    describe('index.css', () => {
        const index = readStyleFile('index.css');

        it('declares layer order', () => {
            expect(index).toContain('@layer reset, base, components, utilities');
        });

        it('imports all style modules', () => {
            expect(index).toContain("@import './tokens.css'");
            expect(index).toContain("@import './reset.css'");
            expect(index).toContain("@import './base.css'");
            expect(index).toContain("@import './motion.css'");
            expect(index).toContain("@import './utilities.css'");
        });

        it('imports fonts', () => {
            expect(index).toContain('fonts.googleapis.com');
            expect(index).toContain('Outfit');
            expect(index).toContain('JetBrains+Mono');
        });
    });

    describe('CardActions.css', () => {
        const cardActions = readFileSync(resolve(__dirname, '../styles/card-actions.css'), 'utf-8');

        it('uses --touch-target-min token for button base size', () => {
            expect(cardActions).toContain('width: var(--touch-target-min)');
            expect(cardActions).toContain('height: var(--touch-target-min)');
        });

        it('scales down to 36px on desktop (pointer devices)', () => {
            expect(cardActions).toContain('@media (min-width: 900px)');
            expect(cardActions).toContain('width: 36px');
            expect(cardActions).toContain('height: 36px');
        });
    });

    describe('Tabs.css', () => {
        const tabs = readFileSync(resolve(__dirname, '../components/ui/Tabs/Tabs.css'), 'utf-8');

        it('uses horizontally scrollable tab bar on mobile', () => {
            expect(tabs).toContain('@media (max-width: 480px)');
            expect(tabs).toContain('overflow-x: auto');
            expect(tabs).toContain('flex-shrink: 0');
        });
    });

    describe('keyframes deduplication rule', () => {
        function findCssFiles(dir: string): string[] {
            const results: string[] = [];
            for (const entry of readdirSync(dir)) {
                const fullPath = resolve(dir, entry);
                if (statSync(fullPath).isDirectory()) {
                    results.push(...findCssFiles(fullPath));
                } else if (entry.endsWith('.css')) {
                    results.push(fullPath);
                }
            }
            return results;
        }

        function getComponentCssFiles(): string[] {
            const srcDir = resolve(__dirname, '..');
            const dirs = [resolve(srcDir, 'pages'), resolve(srcDir, 'components')];
            return dirs.flatMap(findCssFiles);
        }

        it('no component CSS file defines a spin-equivalent @keyframes (use global spin from motion.css)', () => {
            const srcDir = resolve(__dirname, '..');
            const cssFiles = getComponentCssFiles();
            const violations: string[] = [];

            const keyframesPattern = /@keyframes\s+([\w-]+)\s*\{[^}]*rotate\(360deg\)[^}]*\}/gs;

            for (const file of cssFiles) {
                const content = readFileSync(file, 'utf-8');
                let match;
                keyframesPattern.lastIndex = 0;
                while ((match = keyframesPattern.exec(content)) !== null) {
                    violations.push(`${file.replace(resolve(srcDir) + '/', '')}: @keyframes ${match[1]}`);
                }
            }

            expect(
                violations,
                `These files define spin-equivalent @keyframes -- use the global spin from motion.css:\n${violations.join('\n')}`,
            ).toHaveLength(0);
        });

        it('no component CSS file defines a pulse-equivalent @keyframes (use global pulse from motion.css)', () => {
            const srcDir = resolve(__dirname, '..');
            const cssFiles = getComponentCssFiles();
            const violations: string[] = [];

            const keyframesBlockPattern = /@keyframes\s+([\w-]+)\s*\{([\s\S]*?)\n {4}\}/g;

            for (const file of cssFiles) {
                const content = readFileSync(file, 'utf-8');
                let match;
                keyframesBlockPattern.lastIndex = 0;
                while ((match = keyframesBlockPattern.exec(content)) !== null) {
                    const body = match[2];
                    if (/opacity:\s*1/.test(body) && /opacity:\s*(0\.5|var\(--opacity-disabled\))/.test(body)) {
                        violations.push(`${file.replace(resolve(srcDir) + '/', '')}: @keyframes ${match[1]}`);
                    }
                }
            }

            expect(
                violations,
                `These files define pulse-equivalent @keyframes -- use the global pulse from motion.css:\n${violations.join('\n')}`,
            ).toHaveLength(0);
        });
    });

    describe('monospace font usage rule', () => {
        function findCssFiles(dir: string): string[] {
            const results: string[] = [];
            for (const entry of readdirSync(dir)) {
                const fullPath = resolve(dir, entry);
                if (statSync(fullPath).isDirectory()) {
                    results.push(...findCssFiles(fullPath));
                } else if (entry.endsWith('.css')) {
                    results.push(fullPath);
                }
            }
            return results;
        }

        const hardcodedMonoPattern = /font-family\s*:[^;]*(monospace|Consolas|'Courier New'|'JetBrains Mono')[^;]*;/gi;

        it('uses --font-mono token instead of hardcoded monospace font families', () => {
            const srcDir = resolve(__dirname, '..');
            const dirs = [resolve(srcDir, 'pages'), resolve(srcDir, 'components')];
            const cssFiles = dirs.flatMap(findCssFiles);

            expect(cssFiles.length).toBeGreaterThan(0);

            const violations: string[] = [];

            for (const file of cssFiles) {
                if (basename(file) === 'FilterQueryEditor.css') {
                    continue;
                }

                const content = readFileSync(file, 'utf-8');
                const lines = content.split('\n');

                for (let i = 0; i < lines.length; i++) {
                    const line = lines[i];
                    if (hardcodedMonoPattern.test(line)) {
                        if (!line.includes('var(--font-mono')) {
                            const relativePath = file.replace(resolve(srcDir) + '/', '');
                            violations.push(`${relativePath}:${i + 1}: ${line.trim()}`);
                        }
                    }
                    hardcodedMonoPattern.lastIndex = 0;
                }
            }

            expect(
                violations,
                `Found hardcoded monospace font families (use var(--font-mono) instead):\n${violations.join('\n')}`,
            ).toHaveLength(0);
        });
    });
});
