import { execSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';

const REPORT_FILE = path.join(process.cwd(), 'test-output.txt');
const SRC_DIR = path.join(process.cwd(), 'src');
const POLL_INTERVAL_MS = 3000;
const DEBOUNCE_MS = 1000;

const pathFilter = process.argv.slice(2).filter(a => a !== '--').join(' ');

function getModifiedTime(dir: string): number {
    let latest = 0;
    try {
        const entries = fs.readdirSync(dir, { withFileTypes: true });
        for (const entry of entries) {
            if (entry.name === 'node_modules' || entry.name === '.git') {
                continue;
            }
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                latest = Math.max(latest, getModifiedTime(full));
            } else if (/\.(ts|tsx|css)$/.test(entry.name)) {
                const stat = fs.statSync(full);
                latest = Math.max(latest, stat.mtimeMs);
            }
        }
    } catch {
    }
    return latest;
}

function runTests(): void {
    const timestamp = new Date().toLocaleTimeString();
    fs.writeFileSync(REPORT_FILE, `[${timestamp}] RUNNING...\n`);

    const cmd = `npx vitest run ${pathFilter} --reporter=./scripts/format-test-report.ts`;

    try {
        execSync(cmd, {
            cwd: process.cwd(),
            stdio: 'ignore',
            timeout: 120_000,
        });
        console.log(`[${timestamp}] PASS`);
    } catch {
        console.log(`[${timestamp}] FAIL -- see test-output.txt`);
    }
}

console.log(`Test reporter started. Watching src/ for changes (poll ${POLL_INTERVAL_MS}ms).`);
console.log(`Scope: ${pathFilter || '(all tests)'}`);
console.log(`Report: ${REPORT_FILE}`);
console.log('Press Ctrl+C to stop.\n');

runTests();

let lastMtime = getModifiedTime(SRC_DIR);
let debounceTimer: ReturnType<typeof setTimeout> | null = null;

setInterval(() => {
    const current = getModifiedTime(SRC_DIR);
    if (current > lastMtime) {
        lastMtime = current;
        if (debounceTimer) {
            clearTimeout(debounceTimer);
        }
        debounceTimer = setTimeout(() => {
            console.log('File change detected, re-running tests...');
            runTests();
        }, DEBOUNCE_MS);
    }
}, POLL_INTERVAL_MS);
