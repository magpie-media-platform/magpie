import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

export const INTEGRATION_ROOT = resolve(here, '..');

function optionalPath(name: string, fallback: string): string {
    const value = process.env[name]?.trim();
    return value ? resolve(value) : fallback;
}

const appHost = process.env['MAGPIE_CLI_APPHOST']?.trim();

export const integrationEnvironment = {
    baseUrl: process.env['MAGPIE_BASE_URL']?.trim() || 'http://localhost:8080',
    skipBoot: process.env['MAGPIE_SKIP_BOOT'] === 'true',
    adminUsername: process.env['MAGPIE_ADMIN_USERNAME']?.trim() || 'magpie-admin',
    adminPassword: process.env['MAGPIE_ADMIN_PASSWORD'] || 'integration-suite-pa55!',
    databaseProvider: process.env['MAGPIE_DATABASE_PROVIDER_NAME']?.trim() || 'SQLite',
    paths: {
        fixtures: optionalPath('MAGPIE_TEST_FIXTURES_ROOT', resolve(INTEGRATION_ROOT, 'test-fixtures')),
        downloads: optionalPath('MAGPIE_TEST_DOWNLOADS_ROOT', resolve(INTEGRATION_ROOT, 'downloads')),
        library: optionalPath('MAGPIE_LIBRARY_ROOT', resolve(INTEGRATION_ROOT, 'library')),
    },
    cli: {
        executable: appHost || 'dotnet',
        dll: appHost
            ? undefined
            : resolve(INTEGRATION_ROOT, '..', '..', 'src', 'Magpie.App', 'bin', 'Release', 'net10.0', 'publish', 'magpie.dll'),
        url: process.env['MAGPIE_CLI_URL']?.trim() || process.env['MAGPIE_BASE_URL']?.trim() || 'http://localhost:8080',
        apiKey: process.env['MAGPIE_CLI_API_KEY']?.trim(),
        timeoutMs: 120_000,
    },
} as const;
