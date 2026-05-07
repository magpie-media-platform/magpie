import type { Reporter } from 'vitest/reporters';
import * as fs from 'node:fs';
import * as path from 'node:path';

const REPORT_FILE = path.join(process.cwd(), 'test-output.txt');

interface TaskResult {
    state?: string;
    errors?: Array<{ message?: string; stack?: string }>;
}

interface Task {
    name: string;
    result?: TaskResult;
    tasks?: Task[];
}

interface TestFile {
    filepath: string;
    tasks: Task[];
    result?: TaskResult;
}

function firstLine(msg: string): string {
    const line = msg.split('\n')[0].trim();
    return line.length > 200 ? line.slice(0, 200) + '...' : line;
}

function collectFailures(tasks: Task[], filepath: string, ancestors: string[] = []): string[] {
    const failures: string[] = [];
    for (const task of tasks) {
        if (task.tasks && task.tasks.length > 0) {
            failures.push(...collectFailures(task.tasks, filepath, [...ancestors, task.name]));
        } else if (task.result?.state === 'fail') {
            const error = firstLine(task.result.errors?.[0]?.message ?? 'Unknown error');
            const rel = path.relative(process.cwd(), filepath);
            const path_ = [...ancestors, task.name].join(' > ');
            failures.push(`  FAIL ${rel} > ${path_}\n       ${error}`);
        }
    }
    return failures;
}

const SummaryReporter: { new(): Reporter } = class implements Reporter {
    onFinished(files?: TestFile[]) {
        const timestamp = new Date().toLocaleTimeString();

        if (!files || files.length === 0) {
            fs.writeFileSync(REPORT_FILE, `[${timestamp}] No tests found.\n`);
            return;
        }

        let totalTests = 0;
        let passedTests = 0;
        const allFailures: string[] = [];

        for (const file of files) {
            const count = (tasks: Task[]): void => {
                for (const task of tasks) {
                    if (task.tasks && task.tasks.length > 0) {
                        count(task.tasks);
                    } else if (task.result?.state) {
                        totalTests++;
                        if (task.result.state === 'pass') {
                            passedTests++;
                        }
                    }
                }
            };
            count(file.tasks);
            allFailures.push(...collectFailures(file.tasks, file.filepath));
        }

        const failedTests = totalTests - passedTests;

        if (failedTests === 0) {
            fs.writeFileSync(REPORT_FILE,
                `[${timestamp}] ALL PASS -- ${totalTests} tests\n`);
        } else {
            const lines = [
                `[${timestamp}] FAIL -- ${failedTests} failed, ${passedTests} passed, ${totalTests} total`,
                '',
                ...allFailures,
                '',
            ];
            fs.writeFileSync(REPORT_FILE, lines.join('\n'));
        }
    }
};

export default SummaryReporter;
