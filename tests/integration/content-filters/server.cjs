const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { randomUUID } = require('node:crypto');
const { DatabaseSync } = require('node:sqlite');
const { spawn } = require('node:child_process');

const workspace = path.resolve(__dirname, '../../..');
const backendPort = Number(process.env.MAGPIE_FILTER_TEST_BACKEND_PORT ?? '15422');
const frontendPort = Number(process.env.MAGPIE_FILTER_TEST_PORT ?? '15423');
const artifact = path.join(workspace, 'src/Magpie.App/bin/Release/net10.0/Magpie.App.dll');
const builtUi = path.join(workspace, 'ui/dist');
if (!fs.existsSync(artifact) || !fs.existsSync(path.join(builtUi, 'index.html'))) {
    throw new Error('Build Magpie.App in Release and the production UI before this suite.');
}
const runtime = fs.mkdtempSync(path.join(os.tmpdir(), 'magpie-content-filters-'));
const ui = path.join(runtime, 'ui');
fs.cpSync(builtUi, ui, { recursive: true });
const extensions = path.join(runtime, 'extensions');
fs.mkdirSync(extensions);
for (const id of ['Magpie.Extensions.Movies', 'Magpie.Extensions.Tv']) {
    fs.cpSync(path.join(workspace, 'src', id, 'bin/Release/netstandard2.1'), path.join(extensions, id, 'lib/netstandard2.1'), { recursive: true });
    fs.writeFileSync(path.join(extensions, id, id + '.nuspec'), `<package><metadata><id>${id}</id><version>1.0.0</version></metadata></package>`);
}
const backend = spawn('dotnet', [artifact, 'run'], {
    cwd: runtime, windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', ASPNETCORE_URLS: 'http://127.0.0.1:' + backendPort,
        Magpie__Data__Path: path.join(runtime, 'data'), Magpie__Extensions__Path: extensions,
        Magpie__Database__Provider: 'sqlite', Magpie__Database__ConnectionString: 'Data Source=' + path.join(runtime, 'magpie.db'),
        OTEL_EXPORTER_OTLP_ENDPOINT: '', OTEL_EXPORTER_OTLP_HEADERS: '' },
    stdio: ['ignore', 'pipe', 'pipe'],
});
const backendLog = fs.createWriteStream(path.join(runtime, 'backend.log'));
backend.stdout.pipe(backendLog);
backend.stderr.pipe(backendLog);
let fixture;
const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1:' + frontendPort);
    if (url.pathname === '/__qa/ready') {
        try {
            const ready = await fetch('http://127.0.0.1:' + backendPort + '/api/system/ready');
            response.writeHead(ready.ok ? 200 : 503).end();
        } catch { response.writeHead(503).end(); }
        return;
    }
    if (url.pathname === '/__qa/seed' && request.method === 'POST') {
        if (!fixture) {
            const db = new DatabaseSync(path.join(runtime, 'magpie.db'));
            const now = '2026-09-21 00:00:00';
            const subjectId = randomUUID().toUpperCase();
            const subjectMetadataId = randomUUID().toUpperCase();
            db.exec('PRAGMA foreign_keys = ON; BEGIN IMMEDIATE');
            db.prepare('INSERT INTO metadata_container (Id, CreatedAt) VALUES (?, ?)').run(subjectMetadataId, now);
            db.prepare('INSERT INTO metadata_contribution (Id, CreatedAt, MetadataContainerId, Ordinal, ProviderType, Title) VALUES (?, ?, ?, 0, ?, ?)').run(randomUUID().toUpperCase(), now, subjectMetadataId, 'fixture', 'Fixture Person');
            db.prepare('INSERT INTO content_subject (Id, MetadataContainerId, Kind, CreatedAt) VALUES (?, ?, ?, ?)').run(subjectId, subjectMetadataId, 'person', now);
            const items = [];
            const hierarchyIds = new Map();
            for (const [title, year, monitored, fulfillment, related, contentType = 'movie', level = 'movie', parentTitle] of [
                ['Alpha Movie', 2020, 1, 0, true], ['Beta Movie', 2021, 0, 2, false],
                ['Gamma Movie', 1999, 1, 1, true], ['Delta Movie', 2020, 1, 4, false],
                ['Fixture Show', 2020, 1, 4, false, 'tv', 'show'],
                ['Fixture Season', 2020, 1, 4, false, 'tv', 'season', 'Fixture Show'],
                ['Fixture Episode', 2020, 1, 0, false, 'tv', 'episode', 'Fixture Season'],
            ]) {
                const id = randomUUID().toUpperCase();
                const metadataId = randomUUID().toUpperCase();
                const contributionId = randomUUID().toUpperCase();
                db.prepare('INSERT INTO metadata_container (Id, CreatedAt) VALUES (?, ?)').run(metadataId, now);
                db.prepare('INSERT INTO metadata_contribution (Id, CreatedAt, MetadataContainerId, Ordinal, ProviderType, Title, PublishDate) VALUES (?, ?, ?, 0, ?, ?, ?)').run(contributionId, now, metadataId, 'fixture', title, year + '-01-01 00:00:00');
                db.prepare('INSERT INTO content_hierarchyItem (Id, ContentType, CreatedAt, EffectiveMonitoring, EffectiveRootFolderId, LanguagePreferences, Level, MetadataContainerId, Name, Settings, Slug, Monitoring, ParentId) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)').run(id, contentType, now, monitored, '00000000-0000-0000-0000-000000000000', '[]', level, metadataId, title, '{}', title.toLowerCase().replaceAll(' ', '-'), monitored, hierarchyIds.get(parentTitle) ?? null);
                hierarchyIds.set(title, id);
                db.prepare('INSERT INTO content_item (Id, ContentHierarchyItemId, Fulfillment, CreatedAt) VALUES (?, ?, ?, ?)').run(randomUUID().toUpperCase(), id, fulfillment, now);
                if (related) db.prepare('INSERT INTO metadata_relationship (Id, MetadataContributionId, Ordinal, Kind, RelatedMetadataContainerId) VALUES (?, ?, 0, ?, ?)').run(randomUUID().toUpperCase(), contributionId, 'actor', subjectMetadataId);
                items.push({ id, title });
            }
            db.exec('COMMIT');
            db.close();
            fixture = { subjectId, items, runtime };
        }
        response.setHeader('Content-Type', 'application/json');
        response.end(JSON.stringify(fixture));
        return;
    }
    if (url.pathname.startsWith('/api/')) {
        const upstream = http.request({ hostname: '127.0.0.1', port: backendPort, path: request.url,
            method: request.method, headers: request.headers }, (incoming) => {
            response.writeHead(incoming.statusCode, incoming.headers);
            incoming.pipe(response);
        });
        upstream.on('error', () => { if (!response.headersSent) response.writeHead(502); response.end(); });
        response.on('close', () => upstream.destroy());
        request.pipe(upstream);
        return;
    }
    let filename = path.resolve(ui, '.' + decodeURIComponent(url.pathname));
    if (filename !== ui && !filename.startsWith(ui + path.sep)) { response.writeHead(400).end(); return; }
    if (!fs.existsSync(filename) || fs.statSync(filename).isDirectory()) filename = path.join(ui, 'index.html');
    const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.woff2': 'font/woff2', '.png': 'image/png', '.svg': 'image/svg+xml' };
    response.setHeader('Content-Type', types[path.extname(filename)] || 'application/octet-stream');
    response.setHeader('Cache-Control', 'no-store');
    fs.createReadStream(filename).pipe(response);
});
server.listen(frontendPort, '127.0.0.1');
let stopping = false;
function stop() {
    if (stopping) return;
    stopping = true;
    server.close();
    backend.kill();
    setTimeout(() => process.exit(), 1000);
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
process.on('exit', () => backend.kill());
backend.on('exit', () => { if (!stopping) process.exit(1); });
console.log('Isolated content-filter runtime: ' + runtime + '; backend PID ' + backend.pid);
