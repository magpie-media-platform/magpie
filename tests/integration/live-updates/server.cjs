const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const { DatabaseSync } = require('node:sqlite');

const backendPort = Number(process.env.MAGPIE_QA_BACKEND_PORT || 15475);
const browserPorts = (process.env.MAGPIE_QA_BROWSER_PORTS || '15476,15478').split(',').map(Number);

const workspace = path.resolve(__dirname, '../../..');
const artifact = path.join(workspace, 'src/Magpie.App/bin/Release/net10.0/Magpie.App.dll');
const builtUi = path.join(workspace, 'ui/dist');
if (!fs.existsSync(artifact) || !fs.existsSync(path.join(builtUi, 'magpie-events-worker.js'))) {
  throw new Error('Build Magpie.App in Release and the production UI before this suite.');
}
const runtime = fs.mkdtempSync(path.join(os.tmpdir(), 'magpie-live-updates-'));
const ui = path.join(runtime, 'ui');
fs.cpSync(builtUi, ui, { recursive: true });
const backend = spawn('dotnet', [artifact, 'run'], {
  cwd: runtime, windowsHide: true,
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', ASPNETCORE_URLS: 'http://127.0.0.1:' + backendPort,
    Magpie__Data__Path: path.join(runtime, 'data'), Magpie__Extensions__Path: path.join(runtime, 'extensions'),
    Magpie__Database__Provider: 'sqlite', Magpie__Database__ConnectionString: 'Data Source=' + path.join(runtime, 'magpie.db'),
    OTEL_EXPORTER_OTLP_ENDPOINT: '', OTEL_EXPORTER_OTLP_HEADERS: '' },
  stdio: ['ignore', 'pipe', 'pipe'],
});
const backendLog = fs.createWriteStream(path.join(runtime, 'backend.log'));
backend.stdout.pipe(backendLog);
backend.stderr.pipe(backendLog);
const streams = new Set();
const calls = {};
let maxStreams = 0;
let streamStarts = 0;
let mode = 'normal';
let deployment = 1;
let renewalMode = 'normal';
const renewalResults = [];
const heldRenewals = [];
const servers = [];

for (const port of browserPorts) {
  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1:' + port);
    if (url.pathname === '/__qa/ready') {
      try {
        const ready = await fetch('http://127.0.0.1:' + backendPort + '/api/system/ready');
        response.writeHead(ready.ok ? 200 : 503).end();
      } catch { response.writeHead(503).end(); }
      return;
    }
    if (url.pathname === '/__qa/state') {
      response.setHeader('Content-Type', 'application/json');
      response.end(JSON.stringify({ active: streams.size, maxStreams, streamStarts, mode, deployment, calls, runtime,
        renewalMode, renewalResults, heldRenewals: heldRenewals.length }));
      return;
    }
    if (url.pathname === '/__qa/control') {
      if (url.searchParams.has('mode')) mode = url.searchParams.get('mode');
      if (url.searchParams.has('renewal')) renewalMode = url.searchParams.get('renewal');
      if (url.searchParams.has('releaseRenewal')) heldRenewals.splice(0).forEach((release) => release());
      if (url.searchParams.has('account')) {
        const db = new DatabaseSync(path.join(runtime, 'magpie.db'));
        db.exec('PRAGMA busy_timeout = 5000');
        const action = url.searchParams.get('account');
        const user = db.prepare('SELECT Id FROM auth_user WHERE Username = ?').get('live-updates-fixture');
        if (user) {
          if (action === 'revoke') db.prepare('DELETE FROM auth_refreshToken WHERE UserId = ?').run(user.Id);
          if (action === 'expire') db.prepare("UPDATE auth_refreshToken SET ExpiresAt = '2000-01-01 00:00:00' WHERE UserId = ?").run(user.Id);
          if (action === 'expire-original') db.prepare("UPDATE auth_refreshToken SET RenewalExpiresAt = '2000-01-01 00:00:00' WHERE UserId = ?").run(user.Id);
          if (action === 'disable') db.prepare('UPDATE auth_user SET Enabled = 0 WHERE Id = ?').run(user.Id);
          if (action === 'lock') db.prepare("UPDATE auth_user SET LockedUntil = '2099-01-01 00:00:00' WHERE Id = ?").run(user.Id);
          if (action === 'enable') db.prepare('UPDATE auth_user SET Enabled = 1, LockedUntil = NULL WHERE Id = ?').run(user.Id);
        }
        db.close();
      }
      if (url.searchParams.has('deploy')) deployment++;
      if (url.searchParams.has('close')) for (const stream of streams) stream.downstream.end();
      if (url.searchParams.has('reset')) { maxStreams = streams.size; streamStarts = 0; }
      response.writeHead(204).end();
      return;
    }
    if (url.pathname.startsWith('/api/')) {
      calls[url.pathname] = (calls[url.pathname] || 0) + 1;
      const isStream = url.pathname === '/api/events/stream';
      const isRenewal = url.pathname === '/api/auth/refresh';
      const responseMode = isRenewal ? renewalMode : 'normal';
      if (isRenewal && responseMode !== 'normal') renewalMode = 'normal';
      if (responseMode === 'drop-before') { response.destroy(); return; }
      if (responseMode === '503') { response.writeHead(503).end(); return; }
      if (isStream && mode !== 'normal') { response.writeHead(mode === 'terminal' ? 404 : 503).end(); return; }
      const upstream = http.request({ hostname: '127.0.0.1', port: backendPort, path: request.url,
        method: request.method, headers: request.headers }, (incoming) => {
        if (isRenewal) renewalResults.push({ status: incoming.statusCode, mode: responseMode });
        if (isRenewal && incoming.statusCode === 200 && ['drop-after', 'drop-body', 'hold-after'].includes(responseMode)) {
          const chunks = [];
          incoming.on('data', (chunk) => chunks.push(chunk));
          incoming.on('end', () => {
            const release = () => { response.writeHead(incoming.statusCode, incoming.headers); response.end(Buffer.concat(chunks)); };
            if (responseMode === 'hold-after') heldRenewals.push(release);
            else if (responseMode === 'drop-body') {
              response.writeHead(incoming.statusCode, incoming.headers); response.flushHeaders();
              setTimeout(() => response.destroy(), 30);
            } else response.destroy();
          });
          return;
        }
        response.writeHead(incoming.statusCode, incoming.headers);
        if (isStream && incoming.statusCode === 200) {
          const stream = { downstream: response, upstream, port };
          streams.add(stream); streamStarts++;
          maxStreams = Math.max(maxStreams, streams.size);
          response.on('close', () => { streams.delete(stream); upstream.destroy(); });
        }
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
    if (filename.endsWith('magpie-events-worker.js') && mode === 'worker-failure') { response.writeHead(404).end(); return; }
    if (filename.endsWith('magpie-events-worker.js')) response.end(fs.readFileSync(filename, 'utf8') + '\n// deployment ' + deployment);
    else fs.createReadStream(filename).pipe(response);
  });
  server.listen(port, '127.0.0.1');
  servers.push(server);
}

let stopping = false;
function stop() {
  if (stopping) return;
  stopping = true;
  servers.forEach((server) => server.close());
  streams.forEach((stream) => stream.downstream.end());
  backend.kill();
  setTimeout(() => process.exit(), 1000);
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
process.on('exit', () => backend.kill());
backend.on('exit', () => { if (!stopping) process.exit(1); });
console.log('Isolated live-update runtime: ' + runtime + '; backend PID ' + backend.pid);
