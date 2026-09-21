import { spawn } from 'node:child_process';
import { integrationEnvironment } from './environment';

export interface CliExecution {
    command: readonly string[];
    exitCode: number;
    stdout: string;
    stderr: string;
}

export class CliExecutionError extends Error {
    public readonly execution: CliExecution;

    constructor(execution: CliExecution) {
        super(
            `Magpie CLI exited with ${execution.exitCode}: ${execution.command.join(' ')}\n` +
                `${execution.stderr || execution.stdout}`,
        );
        this.name = 'CliExecutionError';
        this.execution = execution;
    }
}

export class CliClient {
    private apiKey: string | undefined;

    async run(args: readonly string[], options: { allowFailure?: boolean; timeoutMs?: number } = {}): Promise<CliExecution> {
        const invocation = this.invocation(args);
        const execution = redact(
            await execute(
                integrationEnvironment.cli.executable,
                invocation,
                options.timeoutMs ?? integrationEnvironment.cli.timeoutMs,
                this.apiKey,
            ),
            this.apiKey,
        );

        if (execution.exitCode !== 0 && !options.allowFailure) {
            throw new CliExecutionError(execution);
        }

        return execution;
    }

    async json<T>(args: readonly string[]): Promise<T> {
        const execution = await this.run(args);
        try {
            return JSON.parse(execution.stdout) as T;
        } catch (error) {
            throw new Error(
                `Magpie CLI returned invalid JSON for '${args.join(' ')}': ${execution.stdout.slice(0, 500)}`,
                { cause: error },
            );
        }
    }

    async text(args: readonly string[]): Promise<string> {
        return (await this.run(args)).stdout;
    }

    setApiKey(apiKey: string): void {
        this.apiKey = apiKey;
    }

    private invocation(args: readonly string[]): string[] {
        const invocation = integrationEnvironment.cli.dll ? [integrationEnvironment.cli.dll, ...args] : [...args];
        if (hasHelpOrVersion(args)) {
            return invocation;
        }

        this.apiKey ??= integrationEnvironment.cli.apiKey;
        if (!this.apiKey) {
            throw new Error('The Magpie CLI requires an API key created by the bootstrap journey.');
        }

        invocation.push('--url', integrationEnvironment.cli.url, '--api-key', this.apiKey);
        return invocation;
    }
}

export const cli = new CliClient();

function hasHelpOrVersion(args: readonly string[]): boolean {
    return args.includes('--help') || args.includes('-h') || args[0] === 'version' || args[0] === '--version';
}

function execute(
    executable: string,
    args: readonly string[],
    timeoutMs: number,
    secret: string | undefined,
): Promise<CliExecution> {
    return new Promise((resolve, reject) => {
        const child = spawn(executable, args, {
            env: process.env,
            shell: false,
            windowsHide: true,
        });
        let stdout = '';
        let stderr = '';
        let timedOut = false;
        let forceKillTimeout: ReturnType<typeof setTimeout> | undefined;

        child.stdout.setEncoding('utf8');
        child.stdout.on('data', (chunk: string) => {
            stdout += chunk;
        });
        child.stderr.setEncoding('utf8');
        child.stderr.on('data', (chunk: string) => {
            stderr += chunk;
        });

        const timeout = setTimeout(() => {
            timedOut = true;
            child.kill();
            forceKillTimeout = setTimeout(() => {
                if (child.exitCode === null && child.signalCode === null) {
                    child.kill('SIGKILL');
                }
            }, 2_000);
        }, timeoutMs);

        const clearTimers = (): void => {
            clearTimeout(timeout);
            if (forceKillTimeout) {
                clearTimeout(forceKillTimeout);
            }
        };

        child.once('error', (error) => {
            clearTimers();
            reject(error);
        });
        child.once('close', (exitCode) => {
            clearTimers();
            if (timedOut) {
                const command = redactText(args.join(' '), secret);
                reject(new Error(`Magpie CLI timed out after ${timeoutMs}ms for '${command}'.`));
                return;
            }

            resolve({
                command: [executable, ...args],
                exitCode: exitCode ?? 1,
                stdout: stdout.trim(),
                stderr: stderr.trim(),
            });
        });
    });
}

function redact(execution: CliExecution, secret: string | undefined): CliExecution {
    if (!secret) {
        return execution;
    }

    return {
        ...execution,
        command: execution.command.map((value) => redactText(value, secret)),
        stdout: redactText(execution.stdout, secret),
        stderr: redactText(execution.stderr, secret),
    };
}

function redactText(value: string, secret: string | undefined): string {
    return secret ? value.split(secret).join('[redacted]') : value;
}
