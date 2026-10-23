// Renders an HTML animation frame by frame with headless Edge (Chrome DevTools Protocol, no npm packages).
//
// The page must define:  window.__ready (true once fonts/assets are loaded)  and  window.render(tSeconds).
//
//   node capture.mjs --url file:///.../promo.html?lang=hant --out frames/ --fps 60 --duration 49 [--workers 6]
//   node capture.mjs --url ... --still 12.5 --png cover.png [--width 1440 --height 1080]
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : 'true']);
  return acc;
}, []));

const width = +(args.width ?? 1920), height = +(args.height ?? 1080);
const fps = +(args.fps ?? 60), duration = +(args.duration ?? 5);
const workers = +(args.workers ?? 6);
const browserPath = args.browser ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

// ---------------------------------------------------------------- tiny CDP client
class CDP {
  constructor(url) {
    this.ws = new WebSocket(url);
    this.nextId = 1;
    this.pending = new Map();
    this.ws.onmessage = (ev) => {
      const m = JSON.parse(ev.data);
      if (m.id && this.pending.has(m.id)) {
        const { resolve, reject } = this.pending.get(m.id);
        this.pending.delete(m.id);
        m.error ? reject(new Error(m.error.message)) : resolve(m.result);
      }
    };
    this.opened = new Promise((res, rej) => { this.ws.onopen = res; this.ws.onerror = rej; });
  }
  send(method, params = {}, sessionId) {
    const id = this.nextId++;
    this.ws.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    return new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
  }
}

async function launch() {
  const profile = mkdtempSync(join(tmpdir(), 'tw-edge-'));
  const proc = spawn(browserPath, [
    '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--mute-audio',
    '--hide-scrollbars', '--force-color-profile=srgb', '--allow-file-access-from-files',
    '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows',
    `--window-size=${width},${height}`, ...(args.flags ? args.flags.split(' ') : []), 'about:blank',
  ], { stdio: ['ignore', 'ignore', 'pipe'] });
  const wsUrl = await new Promise((resolve, reject) => {
    let buf = '';
    proc.stderr.on('data', (d) => {
      buf += d.toString();
      const m = buf.match(/DevTools listening on (ws:\/\/\S+)/);
      if (m) resolve(m[1]);
    });
    proc.on('exit', (c) => reject(new Error('browser exited ' + c)));
    setTimeout(() => reject(new Error('browser did not start')), 20000);
  });
  const cdp = new CDP(wsUrl);
  await cdp.opened;
  return { cdp, close: () => { try { proc.kill(); } catch {} try { rmSync(profile, { recursive: true, force: true }); } catch {} } };
}

async function openPage(cdp) {
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  const s = (m, p) => cdp.send(m, p, sessionId);
  await s('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
  await s('Page.enable');
  await s('Page.navigate', { url: args.url });
  for (let i = 0; i < 600; i++) {
    const r = await s('Runtime.evaluate', { expression: 'window.__ready === true', returnByValue: true });
    if (r.result.value === true) break;
    await new Promise((r) => setTimeout(r, 50));
  }
  if (args.setup) await s('Runtime.evaluate', { expression: args.setup });
  // Warm-up: the first screenshot of a fresh tab can come back blank, so take one and throw it away.
  await s('Runtime.evaluate', { expression: 'window.render(0)' });
  await s('Page.captureScreenshot', { format: 'jpeg', quality: 50 });
  await new Promise((r) => setTimeout(r, 200));
  return s;
}

async function renderAt(s, t) {
  const r = await s('Runtime.evaluate', { expression: `window.render(${t}); 1`, returnByValue: true });
  if (r.exceptionDetails) throw new Error('render failed at t=' + t + ': ' + JSON.stringify(r.exceptionDetails));
}

// Several browser processes render in parallel (tabs of one browser share a single compositor).
const browserCount = args.eval !== undefined || args.still !== undefined ? 1 : +(args.browsers ?? 1);
const browsers = await Promise.all([...Array(browserCount).keys()].map(() => launch()));
const { cdp } = browsers[0];
const close = () => browsers.forEach((b) => b.close());
try {
  if (args.eval !== undefined) {
    // Debug: run an expression in the page (after it is ready) and print the result.
    const s = await openPage(cdp);
    const r = await s('Runtime.evaluate', { expression: args.eval, returnByValue: true, awaitPromise: true });
    if (args.save && !r.exceptionDetails) { writeFileSync(args.save, String(r.result.value), 'utf8'); console.log('wrote', args.save); }
    else console.log(JSON.stringify(r.exceptionDetails ?? r.result.value, null, 1));
  } else if (args.still !== undefined) {
    const s = await openPage(cdp);
    await renderAt(s, +args.still);
    await new Promise((r) => setTimeout(r, 300));
    await renderAt(s, +args.still);
    if (args.pre) await s('Runtime.evaluate', { expression: args.pre });
    const isJpg = /\.jpe?g$/i.test(args.png);
    const shot = await s('Page.captureScreenshot', isJpg ? { format: 'jpeg', quality: +(args.quality ?? 92) } : { format: 'png' });
    writeFileSync(args.png, Buffer.from(shot.data, 'base64'));
    console.log('wrote', args.png);
  } else {
    mkdirSync(args.out, { recursive: true });
    const total = Math.round(duration * fps);
    const only = args.frames ? args.frames.split(',').map(Number) : null;
    const todo = only ?? [...Array(total).keys()];
    let done = 0;
    const start = Date.now();
    const slots = [];
    for (const b of browsers) for (let k = 0; k < workers; k++) slots.push(b.cdp);
    const pages = await Promise.all(slots.slice(0, todo.length).map((c) => openPage(c)));
    await Promise.all(pages.map(async (s, w) => {
      for (let k = w; k < todo.length; k += pages.length) {
        const i = todo[k];
        const file = join(args.out, String(i).padStart(5, '0') + '.jpg');
        if (args.resume && existsSync(file)) { done++; continue; }
        await renderAt(s, i / fps);
        const shot = await s('Page.captureScreenshot', { format: 'jpeg', quality: +(args.quality ?? 94) });
        writeFileSync(file, Buffer.from(shot.data, 'base64'));
        if (++done % 120 === 0) {
          const el = (Date.now() - start) / 1000;
          console.log(`  ${done}/${todo.length} frames, ${el.toFixed(0)}s, eta ${(el / done * (todo.length - done)).toFixed(0)}s`);
        }
      }
    }));
    console.log(`captured ${done} frames in ${((Date.now() - start) / 1000).toFixed(1)}s`);
  }
} finally {
  close();
}
process.exit(0);
