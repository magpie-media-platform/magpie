import { spawn } from 'node:child_process';
import { rm } from 'node:fs/promises';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const integrationRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const generatedDirectories = ['checkpoints', 'db', 'downloads', 'library', 'test-fixtures'];

export async function reset() {
    await run('docker', ['compose', 'down', '-v', '--remove-orphans'], { allowFailure: true });

    const targets = generatedDirectories.map((directory) => {
        const target = resolve(integrationRoot, directory);
        if (dirname(target) !== integrationRoot || relative(integrationRoot, target).startsWith('..')) {
            throw new Error(`Refusing to remove path outside the integration workspace: ${target}`);
        }
        return target;
    });

    try {
        for (const target of targets) {
            await rm(target, { recursive: true, force: true });
        }
    } catch {
        await run('docker', [
            'run',
            '--rm',
            '-v',
            `${integrationRoot}:/work`,
            'alpine:3.21',
            'rm',
            '-rf',
            ...generatedDirectories.map((directory) => `/work/${directory}`),
        ]);
    }
}

async function run(command, args, options = {}) {
    return new Promise((resolveRun, rejectRun) => {
        const child = spawn(command, args, {
            cwd: integrationRoot,
            shell: false,
            stdio: 'inherit',
            windowsHide: true,
        });
        child.once('error', rejectRun);
        child.once('close', (exitCode) => {
            if (exitCode === 0 || options.allowFailure) {
                resolveRun();
                return;
            }
            rejectRun(new Error(`${command} ${args.join(' ')} exited with ${exitCode ?? 1}.`));
        });
    });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    await reset();
}
