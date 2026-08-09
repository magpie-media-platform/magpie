import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

export const INTEGRATION_ROOT = resolve(here, '..');

export type CliMode = 'local' | 'remote';

function cliMode(): CliMode {
    const value = (process.env['MAGPIE_CLI_MODE'] ?? 'remote').trim().toLowerCase();
    if (value === 'local' || value === 'remote') {
        return value;
    }

    throw new Error(`MAGPIE_CLI_MODE must be 'local' or 'remote', received '${value}'.`);
}

function optionalPath(name: string, fallback: string): string {
    const value = process.env[name]?.trim();
    return value ? resolve(value) : fallback;
}

function positiveInteger(name: string, fallback: number): number {
    const value = Number(process.env[name]);
    return Number.isSafeInteger(value) && value > 0 ? value : fallback;
}

export const integrationEnvironment = {
    name: process.env['MAGPIE_INTEGRATION_PROFILE']?.trim() || 'sqlite-single-node',
    baseUrl: process.env['MAGPIE_BASE_URL']?.trim() || 'http://localhost:8080',
    skipBoot: process.env['MAGPIE_SKIP_BOOT'] === 'true',
    adminUsername: process.env['MAGPIE_ADMIN_USERNAME']?.trim() || 'magpie-admin',
    adminPassword: process.env['MAGPIE_ADMIN_PASSWORD'] || 'integration-suite-pa55!',
    paths: {
        fixtures: optionalPath('MAGPIE_TEST_FIXTURES_ROOT', resolve(INTEGRATION_ROOT, 'test-fixtures')),
        downloads: optionalPath('MAGPIE_TEST_DOWNLOADS_ROOT', resolve(INTEGRATION_ROOT, 'downloads')),
        library: optionalPath('MAGPIE_LIBRARY_ROOT', resolve(INTEGRATION_ROOT, 'library')),
        state: optionalPath('MAGPIE_HOST_STATE_ROOT', resolve(INTEGRATION_ROOT, 'db')),
    },
    cli: {
        mode: cliMode(),
        executable:
            process.env['MAGPIE_CLI_APPHOST']?.trim() || process.env['MAGPIE_CLI_EXECUTABLE']?.trim() || 'dotnet',
        dll: process.env['MAGPIE_CLI_APPHOST']?.trim()
            ? undefined
            : optionalPath(
                  'MAGPIE_CLI_DLL',
                  resolve(
                      INTEGRATION_ROOT,
                      '..',
                      '..',
                      'src',
                      'Magpie.App',
                      'bin',
                      'Release',
                      'net10.0',
                      'publish',
                      'Magpie.App.dll',
                  ),
              ),
        url: process.env['MAGPIE_CLI_URL']?.trim() || process.env['MAGPIE_BASE_URL']?.trim() || 'http://localhost:8080',
        apiKey: process.env['MAGPIE_CLI_API_KEY']?.trim(),
        timeoutMs: positiveInteger('MAGPIE_CLI_TIMEOUT_MS', 120_000),
    },
} as const;
