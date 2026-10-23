// TouchWrite promo. Every visual is a pure function of time: window.render(t) draws the frame at t seconds.
// URL: promo.html?lang=hant|hans&mode=video|cover169|cover43
(() => {
'use strict';

const Q = new URLSearchParams(location.search);
const LANG = Q.get('lang') === 'hans' ? 'hans' : 'hant';
const MODE = Q.get('mode') || 'video';
const W = MODE === 'cover43' ? 1440 : 1920, H = 1080;
const FPS = 60;
const END = 51.0;

// ------------------------------------------------------------------ text
const TEXT = {
  hant: {
    subtitle: '觸控板手寫輸入 · for Windows',
    premiseA: '在 Mac 上，用觸控板就能寫字。',
    premiseB: '現在，[Windows] 也可以。',
    s3Head: '一根手指，\n寫完就輸入。',
    s3Desc: '在觸控板上寫字，停筆後自動輸入。\n幾乎任何程式都能用。',
    s4HeadA: '寫歪了？\n點一下就換。',
    s4DescA: '輕點觸控板頂部的候選字，\n立刻替換剛輸入的字。',
    s4HeadB: '刪除，\n就在右上角。',
    s4DescB: '輕點觸控板右上角 ⌫，\n按住可連續刪除。',
    s5Title: '常用操作，一個手勢就好。',
    gestures: [['空格', '兩指向右滑'], ['換行', '兩指向下滑'], ['撤銷上一筆', '兩指向上滑']],
    s5Keys: '鍵盤也行：1–9 選字 · Space 立即輸入 · Esc 結束',
    s6Title: '細節，\n都替你想好了。',
    chips: [['', '輸出繁體或簡體'], ['', '自訂快捷鍵'], ['', '自動輸入，可開可關'],
            ['', '跟隨系統深淺色'], ['', '繁體／简体介面'], ['', 'Windows 11 風格設定']],
    s7Tag: '讓觸控板，變成你的筆。',
    s7Spec: 'Windows · 精確式觸控板 · 繁體／簡體中文',
    editorTitle: '新文件.txt', editorMenu: ['檔案', '編輯', '檢視'],
    panelTitle: '觸控板手寫', hint: '用一根手指在觸控板上書寫', placeholder: '候選字會顯示在這裡 · 輕點觸控板頂部選字',
    footer: [['右上角 ⌫', '刪除'], ['兩指 →', '空格'], ['兩指 ↓', '換行'], ['兩指 ↑', '撤銷筆劃'], ['1–9', '選字'], ['Esc', '退出']],
    coverHead: '觸控板\n[手寫輸入]', coverTag: 'Windows 也能用', coverSub: '像 Mac 一樣，用手指寫字',
  },
  hans: {
    subtitle: '触控板手写输入 · for Windows',
    premiseA: '在 Mac 上，用触控板就能写字。',
    premiseB: '现在，[Windows] 也可以。',
    s3Head: '一根手指，\n写完就输入。',
    s3Desc: '在触控板上写字，停笔后自动输入。\n几乎任何程序都能用。',
    s4HeadA: '写歪了？\n点一下就换。',
    s4DescA: '轻点触控板顶部的候选字，\n立刻替换刚输入的字。',
    s4HeadB: '删除，\n就在右上角。',
    s4DescB: '轻点触控板右上角 ⌫，\n按住可连续删除。',
    s5Title: '常用操作，一个手势就好。',
    gestures: [['空格', '两指向右滑'], ['换行', '两指向下滑'], ['撤销上一笔', '两指向上滑']],
    s5Keys: '键盘也行：1–9 选字 · Space 立即输入 · Esc 结束',
    s6Title: '细节，\n都替你想好了。',
    chips: [['', '输出繁体或简体'], ['', '自定义快捷键'], ['', '自动输入，可开可关'],
            ['', '跟随系统深浅色'], ['', '繁體／简体界面'], ['', 'Windows 11 风格设置']],
    s7Tag: '让触控板，变成你的笔。',
    s7Spec: 'Windows · 精确式触控板 · 繁体／简体中文',
    editorTitle: '新文件.txt', editorMenu: ['文件', '编辑', '查看'],
    panelTitle: '触控板手写', hint: '用一根手指在触控板上书写', placeholder: '候选字会显示在这里 · 轻点触控板顶部选字',
    footer: [['右上角 ⌫', '删除'], ['两指 →', '空格'], ['两指 ↓', '换行'], ['两指 ↑', '撤销笔画'], ['1–9', '选字'], ['Esc', '退出']],
    coverHead: '触控板\n[手写输入]', coverTag: 'Windows 也能用', coverSub: '像 Mac 一样，用手指写字',
  },
}[LANG];

const REC_NAME = 'Microsoft 中文(简体)手写识别器'; // the recognizer on the machine TouchWrite was built on
// Real recognizer output for the strokes in strokes.js (see promo/tools/rec).
const CANDS = { '你': ['你', '伱', '妳', '狝', '珎'], '好': ['好', '女', '奸', '妤', '妈'], '人?': ['八', '人', '入', '乀', '𠂉'] };

document.documentElement.style.setProperty('--cjk', LANG === 'hant' ? '"Noto Sans TC"' : '"Noto Sans SC"');
const stage = document.getElementById('stage');
stage.style.width = W + 'px';
stage.style.height = H + 'px';

// ------------------------------------------------------------------ helpers
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const prog = (t, a, b) => clamp((t - a) / (b - a));
const lerp = (a, b, p) => a + (b - a) * p;
const E = {
  outExpo: (p) => (p >= 1 ? 1 : 1 - Math.pow(2, -10 * p)),
  outCubic: (p) => 1 - Math.pow(1 - p, 3),
  outQuart: (p) => 1 - Math.pow(1 - p, 4),
  inCubic: (p) => p * p * p,
  inOut: (p) => (p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2),
  inOutSine: (p) => -(Math.cos(Math.PI * p) - 1) / 2,
  outBack: (p) => { const c1 = 1.5, c3 = c1 + 1; return 1 + c3 * Math.pow(p - 1, 3) + c1 * Math.pow(p - 1, 2); },
};
const bump = (t, a, b) => Math.sin(Math.PI * prog(t, a, b)); // 0 → 1 → 0

function el(tag, cls, parent, css, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (css) Object.assign(e.style, css);
  if (text != null) e.textContent = text;
  if (parent) parent.appendChild(e);
  return e;
}
const SVGNS = 'http://www.w3.org/2000/svg';
function sv(tag, attrs, parent) {
  const e = document.createElementNS(SVGNS, tag);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}
const f2 = (x) => (Math.round(x * 100) / 100).toString();
function put(e, { x = 0, y = 0, s = 1, r = 0, o = 1, b = 0, extra = '' } = {}) {
  e.style.opacity = f2(clamp(o));
  e.style.transform = `translate3d(${f2(x)}px,${f2(y)}px,0) scale(${s.toFixed(4)})${r ? ` rotate(${f2(r)}deg)` : ''}${extra}`;
  e.style.filter = b > 0.05 ? `blur(${f2(b)}px)` : 'none';
  e.style.visibility = o <= 0.001 ? 'hidden' : 'visible';
}

/** Smooth curve through points (cardinal spline, tension 0.5 — same as the app icon). */
function smoothPath(pts) {
  let d = `M${pts[0][0]},${pts[0][1]}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2, k = 1 / 6;
    const c1 = [p1[0] + (p2[0] - p0[0]) * k, p1[1] + (p2[1] - p0[1]) * k];
    const c2 = [p2[0] - (p3[0] - p1[0]) * k, p2[1] - (p3[1] - p1[1]) * k];
    d += ` C${c1[0]},${c1[1]} ${c2[0]},${c2[1]} ${p2[0]},${p2[1]}`;
  }
  return d;
}

/** Text split into per-character spans. "[...]" marks gradient text. */
function textBlock(parent, text, cls, css) {
  const root = el('div', cls, parent, css);
  const spans = [];
  let grad = false;
  for (const line of text.split('\n')) {
    const ln = el('div', 'line', root);
    for (const ch of [...line]) {
      if (ch === '[') { grad = true; continue; }
      if (ch === ']') { grad = false; continue; }
      spans.push(el('span', 'ch' + (grad ? ' grad' : ''), ln, null, ch));
    }
  }
  return { root, spans };
}
const GRADIENT = 'linear-gradient(90deg, #5AB8FF 0%, #7F7DFF 50%, #C78BFF 100%)';
function applyGradients(block) {
  let group = [];
  const flush = () => {
    if (!group.length) return;
    const x0 = group[0].offsetLeft, last = group[group.length - 1], w = last.offsetLeft + last.offsetWidth - x0;
    for (const s of group) {
      s.style.backgroundImage = GRADIENT;
      s.style.backgroundSize = `${w}px 100%`;
      s.style.backgroundPosition = `${x0 - s.offsetLeft}px 0`;
    }
    group = [];
  };
  for (const s of block.spans) {
    const g = s.classList.contains('grad');
    if (g && group.length && s.parentNode === group[0].parentNode) group.push(s);
    else { flush(); if (g) group.push(s); }
  }
  flush();
}
function reveal(block, t, t0, o = {}) {
  const st = o.stagger ?? 0.03, dur = o.dur ?? 0.8, dy = o.dy ?? 40, bl = o.blur ?? 16;
  block.spans.forEach((s, i) => {
    const p = E.outExpo(prog(t, t0 + i * st, t0 + i * st + dur));
    let a = p, y = (1 - p) * dy, b = (1 - p) * bl;
    if (o.out != null) {
      const q = E.inCubic(prog(t, o.out + i * 0.01, o.out + i * 0.01 + 0.4));
      a *= 1 - q; y -= q * 16; b += q * 10;
    }
    s.style.opacity = f2(a);
    s.style.transform = `translate3d(0,${f2(y)}px,0)`;
    s.style.filter = b > 0.05 ? `blur(${f2(b)}px)` : 'none';
  });
}

// ------------------------------------------------------------------ audio cue list (read by music.py)
const EVENTS = [];
const cue = (t, type, extra = {}) => EVENTS.push({ t: +t.toFixed(3), type, ...extra });

// ------------------------------------------------------------------ app icon (same geometry as Assets/app.ico)
let iconCount = 0;
function makeIcon(parent) {
  const id = 'ig' + iconCount++;
  const svg = sv('svg', { viewBox: '0 0 256 256', width: 256, height: 256, style: 'position:absolute;left:0;top:0;overflow:visible' }, parent);
  const defs = sv('defs', {}, svg);
  const lg = sv('linearGradient', { id, x1: 0, y1: 0, x2: 1, y2: 1 }, defs);
  sv('stop', { offset: 0, 'stop-color': '#0A84FF' }, lg);
  sv('stop', { offset: 1, 'stop-color': '#5E5CE6' }, lg);
  const bgG = sv('g', {}, svg);
  sv('rect', { x: 8, y: 8, width: 240, height: 240, rx: 54, fill: `url(#${id})` }, bgG);
  sv('rect', { x: 40, y: 62, width: 176, height: 132, rx: 22, fill: 'rgba(255,255,255,.27)', stroke: 'rgba(255,255,255,.55)', 'stroke-width': 4 }, bgG);
  const d = smoothPath([[70, 150], [95, 95], [118, 160], [145, 100], [168, 150], [190, 118]]);
  // Glow = wider translucent copies underneath (an SVG blur filter at 7× zoom is too heavy to rasterize).
  const glows = [[4.2, 0.10], [2.6, 0.22], [1.7, 0.35]].map(([k, a]) => ({ k, a, p: sv('path', { d, fill: 'none', stroke: '#5aaeff', 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, svg) }));
  const squig = sv('path', { d, fill: 'none', stroke: '#fff', 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, svg);
  const len = squig.getTotalLength();
  return {
    svg, bgG, squig, len,
    draw(p, width, glowAlpha) {
      for (const pth of [squig, ...glows.map((g) => g.p)]) {
        pth.style.strokeDasharray = `${len} ${len + 20}`;
        pth.style.strokeDashoffset = f2(len * (1 - p));
      }
      squig.style.opacity = p > 0.001 ? 1 : 0;
      squig.setAttribute('stroke-width', f2(width));
      for (const g of glows) {
        g.p.setAttribute('stroke-width', f2(width * g.k));
        g.p.style.opacity = p > 0.001 ? f2(glowAlpha * g.a) : 0;
      }
    },
    bg(scale, alpha) {
      bgG.setAttribute('transform', `translate(128 128) scale(${scale.toFixed(4)}) translate(-128 -128)`);
      bgG.style.opacity = f2(alpha);
    },
    tip(p) { return squig.getPointAtLength(len * p); },
  };
}

// ------------------------------------------------------------------ TouchWrite panel replica
const PAD_W = 560, PAD_H = 345, STRIP_H = 69, BS_W = 84;
const toPad = ([u, v]) => [155 + u * 0.25, 82 + v * 0.25]; // character box (1000 units) → pad px
class Panel {
  constructor(parent, x, y, scale) {
    this.root = el('div', 'panel', parent, { left: x + 'px', top: y + 'px', transform: `scale(${scale})` });
    this.scale = scale; this.x = x; this.y = y;
    const head = el('div', 'p-head', this.root);
    this.dot = el('div', 'p-dot', head);
    el('div', 'p-title', head, null, TEXT.panelTitle);
    el('div', 'p-rec', head, null, REC_NAME);
    const frame = el('div', 'p-frame', this.root);
    this.pad = el('div', 'p-pad', frame);
    const strip = el('div', 'p-strip', this.pad);
    this.ph = el('div', 'p-ph', strip, null, TEXT.placeholder);
    this.grid = el('div', 'p-grid', strip);
    const bscol = el('div', 'p-bscol', strip);
    this.bskey = el('div', 'p-bskey', bscol, null, '');
    this.hint = el('div', 'p-hint', this.pad, null, TEXT.hint);
    this.svg = sv('svg', { class: 'p-ink', viewBox: `0 0 ${PAD_W} ${PAD_H}` }, this.pad);
    this.dots = el('div', 'p-dots', this.pad);
    this.dotEls = [0, 1].map(() => el('div', 'p-cdot', this.dots));
    this.ring = el('div', 'p-ring', this.dots);
    const foot = el('div', 'p-foot', this.root);
    for (const [k, v] of TEXT.footer) { const it = el('div', 'p-fi', foot); el('span', 'p-fk', it, null, k); el('span', null, it, null, ' ' + v); }
    this.groups = {};
    this.candKey = '';
  }
  /** Pad-space origin of the panel on the stage (for overlays). */
  padOrigin() { return [this.x + 13 * this.scale, this.y + 41 * this.scale]; }
  addChar(id, key) {
    const g = sv('g', {}, this.svg);
    const paths = STROKES[key].map((pts) => {
      const p = sv('path', { d: smoothPath(pts.map(toPad)), fill: 'none', stroke: '#F5F5F7', 'stroke-width': 5.6, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, g);
      const len = p.getTotalLength();
      p.style.strokeDasharray = `${len} ${len + 20}`;
      return { p, len };
    });
    this.groups[id] = { g, paths };
    return paths;
  }
  drawChar(id, progresses, fade = 0) {
    const G = this.groups[id];
    G.paths.forEach((s, i) => {
      const pr = progresses[i];
      s.p.style.strokeDashoffset = f2(s.len * (1 - pr));
      s.p.style.opacity = pr > 0.001 ? 1 : 0;
    });
    G.g.setAttribute('transform', `translate(0 ${f2(-PAD_H * 0.08 * E.outCubic(fade))})`);
    G.g.style.opacity = f2(1 - fade);
  }
  setCands(list, sel = -1, committed = false, hover = -1) {
    const key = JSON.stringify([list, sel, committed, hover]);
    if (key === this.candKey) return;
    this.candKey = key;
    this.grid.innerHTML = '';
    this.grid.style.gridTemplateColumns = `repeat(${Math.max(1, list.length)}, 1fr)`;
    this.ph.style.display = list.length ? 'none' : 'flex';
    list.forEach((c, i) => {
      let bg = 'transparent';
      if (i === hover) bg = 'rgba(255,255,255,.25)';
      else if (i === sel) bg = committed ? '#0A84FF' : 'rgba(255,255,255,.25)';
      const cell = el('div', 'p-cell', this.grid, { background: bg });
      el('div', 'p-c', cell, null, c);
      el('div', 'p-num', cell, null, String(i + 1));
    });
  }
  setLive(writing) { this.dot.style.background = writing ? '#0A84FF' : '#30D158'; }
  setBackspace(pressed) { this.bskey.style.background = pressed ? '#0A84FF' : 'rgba(255,255,255,.08)'; }
  setHint(visible) { this.hint.style.opacity = visible ? 1 : 0; }
  /** Contact dots (pad px) and an optional touch-down ripple {x, y, p}. */
  setTouches(points, ripple) {
    this.dotEls.forEach((d, i) => {
      const pt = points[i];
      if (!pt) { d.style.opacity = 0; return; }
      d.style.opacity = f2(pt.a ?? 1);
      d.style.left = f2(pt.x) + 'px';
      d.style.top = f2(pt.y) + 'px';
    });
    if (ripple && ripple.p > 0 && ripple.p < 1) {
      const s = 1 + ripple.p * 2.4;
      this.ring.style.left = f2(ripple.x) + 'px';
      this.ring.style.top = f2(ripple.y) + 'px';
      this.ring.style.transform = `scale(${s.toFixed(3)})`;
      this.ring.style.opacity = f2(1 - ripple.p);
    } else this.ring.style.opacity = 0;
  }
}
/** Centre of candidate cell i (of n) / of the ⌫ key, in pad px. */
const cellCenter = (i, n) => [4 + (468 / n) * (i + 0.5), 4 + 30.5];
const BS_CENTER = [PAD_W - BS_W / 2, STRIP_H / 2];

// ------------------------------------------------------------------ editor window
class Editor {
  constructor(parent, x, y, scale) {
    this.root = el('div', 'editor', parent, { left: x + 'px', top: y + 'px', transform: `scale(${scale})` });
    const bar = el('div', 'e-bar', this.root);
    el('span', 'e-doc', bar, null, '');
    el('span', null, bar, null, TEXT.editorTitle);
    const ctl = el('div', 'e-ctl', bar);
    for (const g of ['', '', '']) el('span', null, ctl, null, g);
    const menu = el('div', 'e-menu', this.root);
    for (const m of TEXT.editorMenu) el('span', null, menu, null, m);
    this.body = el('div', 'e-body', this.root);
    this.textEl = el('span', null, this.body);
    this.caret = el('span', 'e-caret', this.body);
    this.items = [];
  }
  add(c, tin, tout = 1e9) {
    const s = el('span', 'e-char', this.textEl, null, c);
    this.items.push({ c, tin, tout, s });
  }
  render(t) {
    let lastChange = -1e9;
    for (const it of this.items) {
      const vis = t >= it.tin && t < it.tout;
      it.s.style.display = vis ? 'inline-block' : 'none';
      if (it.tin <= t) lastChange = Math.max(lastChange, it.tin);
      if (it.tout <= t) lastChange = Math.max(lastChange, it.tout);
      if (!vis) continue;
      const p = prog(t, it.tin, it.tin + 0.5);
      const s = lerp(0.35, 1, E.outBack(p));
      const c = prog(t, it.tin + 0.2, it.tin + 1.0);
      const r = Math.round(lerp(10, 27, c)), g = Math.round(lerp(132, 27, c)), b = Math.round(lerp(255, 31, c));
      it.s.style.transform = `scale(${s.toFixed(4)})`;
      it.s.style.opacity = f2(Math.min(1, p * 4));
      it.s.style.color = `rgb(${r},${g},${b})`;
    }
    const on = t - lastChange < 0.55 || Math.floor((t - lastChange) * 1.7) % 2 === 0;
    this.caret.style.opacity = on ? 1 : 0;
  }
}

// ------------------------------------------------------------------ touchpad (perspective) with finger
class Touchpad {
  constructor(parent, cx, top, width, tilt) {
    const k = width / 640;
    this.wrap = el('div', 'tp-wrap', parent, { left: f2(cx - 320) + 'px', top: top + 'px', width: '640px', height: '394px', transformOrigin: '50% 0' });
    this.k = k; this.tilt = tilt;
    this.tp = el('div', 'tp', this.wrap);
    el('div', 'tp-sheen', this.tp);
    this.svg = sv('svg', { viewBox: `0 0 ${PAD_W} ${PAD_H}`, preserveAspectRatio: 'none' }, this.tp);
    this.ripple = el('div', 'ripple', this.tp);
    this.finger = el('div', 'finger', this.tp);
    this.groups = {};
    this.place(0, 0, 1);
  }
  place(dy, extraTilt, alpha) {
    this.wrap.style.transform = `translate3d(0,${f2(dy)}px,0) scale(${this.k.toFixed(4)})`;
    this.tp.style.transform = `rotateX(${f2(this.tilt + extraTilt)}deg)`;
    this.wrap.style.opacity = f2(alpha);
  }
  addChar(id, key) {
    const g = sv('g', {}, this.svg);
    this.groups[id] = STROKES[key].map((pts) => {
      const p = sv('path', { d: smoothPath(pts.map(toPad)), fill: 'none', stroke: 'rgba(140,195,255,.55)', 'stroke-width': 7, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, g);
      const len = p.getTotalLength();
      p.style.strokeDasharray = `${len} ${len + 20}`;
      return { p, len };
    });
  }
  /** Faint trail: only the stroke being drawn, fading out after the finger lifts. */
  drawTrail(id, sched, t) {
    this.groups[id].forEach((s, i) => {
      const st = sched.strokes[i];
      const pr = E.inOutSine(prog(t, st.t0, st.t1));
      const a = pr > 0 ? 0.9 * (1 - prog(t, st.t1, st.t1 + 0.5)) : 0;
      s.p.style.strokeDashoffset = f2(s.len * (1 - pr));
      s.p.style.opacity = f2(a);
    });
  }
  setFinger(st, ripple) {
    if (!st) { this.finger.style.opacity = 0; this.ripple.style.opacity = 0; return; }
    const x = (st.x / PAD_W) * 640, y = (st.y / PAD_H) * 394;
    const lift = st.lift ?? 0;
    this.finger.style.left = f2(x) + 'px';
    this.finger.style.top = f2(y) + 'px';
    this.finger.style.opacity = f2((st.a ?? 1) * lerp(1, 0.32, lift));
    this.finger.style.transform = `scale(${lerp(1, 1.25, lift).toFixed(3)})`;
    if (ripple && ripple.p > 0 && ripple.p < 1) {
      this.ripple.style.left = f2((ripple.x / PAD_W) * 640) + 'px';
      this.ripple.style.top = f2((ripple.y / PAD_H) * 394) + 'px';
      this.ripple.style.transform = `scale(${(0.35 + ripple.p * 0.6).toFixed(3)})`;
      this.ripple.style.opacity = f2(0.7 * (1 - ripple.p));
    } else this.ripple.style.opacity = 0;
  }
}

// ------------------------------------------------------------------ handwriting schedule
/** Strokes drawn one after another: stroke duration from its length, short lifts in between. */
function schedule(paths, t0, speed = 380, gap = 0.15) {
  let t = t0;
  const strokes = paths.map((s) => {
    const dur = 0.12 + s.len / speed;
    const st = { t0: t, t1: t + dur, s };
    t += dur + gap;
    return st;
  });
  const sch = { strokes, start: t0, end: strokes[strokes.length - 1].t1 };
  for (const st of strokes) cue(st.t0, 'stroke', { dur: +(st.t1 - st.t0).toFixed(3) });
  return sch;
}
const progresses = (sch, t) => sch.strokes.map((st) => E.inOutSine(prog(t, st.t0, st.t1)));
/** Finger position (pad px) while writing: touching during strokes, hovering in between. */
function fingerAt(sch, t) {
  const S = sch.strokes;
  if (t < S[0].t0 - 0.45 || t > sch.end + 0.45) return null;
  if (t < S[0].t0) {
    const p0 = S[0].s.p.getPointAtLength(0), q = E.outCubic(prog(t, S[0].t0 - 0.45, S[0].t0));
    return { x: p0.x + 40 * (1 - q), y: p0.y + 30 * (1 - q), lift: 1, a: q };
  }
  for (const st of S) {
    if (t >= st.t0 && t <= st.t1) {
      const pt = st.s.p.getPointAtLength(st.s.len * E.inOutSine(prog(t, st.t0, st.t1)));
      return { x: pt.x, y: pt.y, lift: 0, touch: true };
    }
  }
  for (let k = 0; k < S.length - 1; k++) {
    const a = S[k], b = S[k + 1];
    if (t > a.t1 && t < b.t0) {
      const pa = a.s.p.getPointAtLength(a.s.len), pb = b.s.p.getPointAtLength(0), q = E.inOut(prog(t, a.t1, b.t0));
      const lift = Math.min(1, (t - a.t1) / 0.06, (b.t0 - t) / 0.06);
      return { x: lerp(pa.x, pb.x, q), y: lerp(pa.y, pb.y, q), lift };
    }
  }
  const pe = S[S.length - 1].s.p.getPointAtLength(S[S.length - 1].s.len), q = prog(t, sch.end, sch.end + 0.45);
  return { x: pe.x + 30 * q, y: pe.y - 30 * q, lift: Math.min(1, q * 8), a: 1 - q };
}
/** Ripple when a stroke starts. */
function rippleAt(sch, t) {
  for (const st of sch.strokes) {
    if (t >= st.t0 && t < st.t0 + 0.5) {
      const p0 = st.s.p.getPointAtLength(0);
      return { x: p0.x, y: p0.y, p: prog(t, st.t0, st.t0 + 0.5) };
    }
  }
  return null;
}

// ------------------------------------------------------------------ background
const bgLayer = el('div', 'layer', stage);
const BLOBS = [
  { c: 'rgba(10,132,255,.30)', size: 1500, x: (t) => 0.14 * W + 140 * Math.sin(t * 0.13), y: (t) => 120 + 90 * Math.cos(t * 0.11) },
  { c: 'rgba(94,92,230,.30)', size: 1600, x: (t) => 0.78 * W + 120 * Math.cos(t * 0.09), y: (t) => 900 + 80 * Math.sin(t * 0.12) },
  { c: 'rgba(60,200,220,.10)', size: 1100, x: (t) => 0.57 * W + 200 * Math.sin(t * 0.07 + 1), y: (t) => 260 + 60 * Math.sin(t * 0.1) },
].map((b) => ({ ...b, e: el('div', 'blob', bgLayer, { width: b.size + 'px', height: b.size + 'px', left: -b.size / 2 + 'px', top: -b.size / 2 + 'px', background: `radial-gradient(circle, ${b.c} 0%, rgba(0,0,0,0) 62%)` }) }));

const scenesLayer = el('div', 'layer', stage);
const grain = el('div', null, stage, { position: 'absolute', inset: '-64px' });
grain.id = 'grain';
el('div', null, stage).id = 'vignette';
const black = el('div', null, stage);
black.id = 'black';
{
  let seed = 7;
  const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const c = document.createElement('canvas');
  c.width = c.height = 256;
  const ctx = c.getContext('2d'), img = ctx.createImageData(256, 256);
  for (let i = 0; i < img.data.length; i += 4) { const v = 128 + (rnd() - 0.5) * 255; img.data[i] = img.data[i + 1] = img.data[i + 2] = v; img.data[i + 3] = 255; }
  ctx.putImageData(img, 0, 0);
  grain.style.backgroundImage = `url(${c.toDataURL()})`;
}
function renderBackground(t) {
  for (const b of BLOBS) b.e.style.transform = `translate3d(${f2(b.x(t))}px,${f2(b.y(t))}px,0)`;
  const fr = Math.round(t * FPS);
  grain.style.transform = `translate3d(${((fr * 37) % 64) - 32}px,${((fr * 53) % 64) - 32}px,0)`;
}

// ------------------------------------------------------------------ scenes
const scenes = [];
/** Outer layer: scene transition (fade, blur, scale). Inner layer: whatever the scene does itself. */
function scene(start, end, build) {
  const root = el('div', 'layer', scenesLayer);
  const inner = el('div', 'layer', root);
  const s = { start, end, root, update: build(inner) };
  scenes.push(s);
  return s;
}
function sceneEnvelope(s, t) {
  const fin = s.start <= 0 ? 1 : E.inOut(prog(t, s.start - 0.2, s.start + 0.4));
  const fout = 1 - E.inOut(prog(t, s.end - 0.4, s.end + 0.2));
  return Math.min(fin, fout);
}

if (MODE === 'video') {
  // ============================================================ 1. opening  0 – 4.8
  scene(0, 4.8, (root) => {
    const iconWrap = el('div', 'abs', root, { left: 0, top: 0, width: '256px', height: '256px', transformOrigin: '0 0' });
    const icon = makeIcon(iconWrap);
    const tip = el('div', 'finger', root, { left: 0, top: 0 });
    const title = textBlock(root, 'TouchWrite', 'display abs', { fontSize: '124px', fontWeight: 600, letterSpacing: '-2px', left: 0, top: 0 });
    const sub = textBlock(root, TEXT.subtitle, 'cjk abs', { fontSize: '34px', fontWeight: 400, color: 'rgba(255,255,255,.66)', left: 0, top: 0, letterSpacing: '1px' });
    const ICON = 176, GAP = 44;
    let laid = false, left = 0, iconTop = 0;
    cue(0.3, 'draw', { dur: 1.6 });
    cue(2.0, 'whoosh');
    cue(2.55, 'title');
    return (t) => {
      if (!laid) {
        const tw = title.root.offsetWidth, gw = ICON + GAP + tw;
        left = (W - gw) / 2; iconTop = H / 2 - ICON / 2 - 30;
        title.root.style.left = left + ICON + GAP + 'px'; title.root.style.top = iconTop - 14 + 'px';
        sub.root.style.left = left + ICON + GAP + 4 + 'px'; sub.root.style.top = iconTop + 146 + 'px';
        laid = true;
      }
      const draw = E.inOut(prog(t, 0.3, 1.9));
      const m = E.inOut(prog(t, 1.85, 2.85));
      const sB = 7.2, sF = ICON / 256;
      const s = sB * Math.pow(sF / sB, m);
      const cx = lerp(W / 2, left + sF * 130, m), cy = lerp(H / 2 - 10, iconTop + sF * 128, m);
      iconWrap.style.transform = `translate3d(${f2(cx - s * 130)}px,${f2(cy - s * 128)}px,0) scale(${s.toFixed(4)})`;
      icon.draw(draw, lerp(2.4, 13, m), lerp(0.85, 0.0, m));
      const bgp = prog(t, 2.05, 2.85);
      icon.bg(lerp(0.3, 1, E.outBack(bgp)), E.outCubic(bgp));
      const tp = icon.tip(draw);
      put(tip, { x: cx - s * 130 + tp.x * s, y: cy - s * 128 + tp.y * s, o: (t > 0.3 ? 1 : 0) * (1 - prog(t, 1.9, 2.15)) * prog(t, 0.2, 0.4) });
      reveal(title, t, 2.55, { stagger: 0.045, dur: 0.9, dy: 30 });
      reveal(sub, t, 3.05, { stagger: 0.02, dur: 0.8, dy: 18, blur: 10 });
    };
  });

  // ============================================================ 2. premise  4.8 – 9.6
  scene(4.8, 9.6, (root) => {
    const a = textBlock(root, TEXT.premiseA, 'cjk abs', { left: 0, width: W + 'px', textAlign: 'center', top: '360px', fontSize: '48px', fontWeight: 400, color: 'rgba(255,255,255,.6)' });
    const b = textBlock(root, TEXT.premiseB, 'head abs', { left: 0, width: W + 'px', textAlign: 'center', top: '452px', fontSize: '120px' });
    let laid = false;
    cue(5.05, 'soft');
    cue(6.1, 'title');
    return (t) => {
      if (!laid) { applyGradients(b); laid = true; }
      reveal(a, t, 5.05, { stagger: 0.025, dur: 0.9, dy: 24, blur: 12 });
      reveal(b, t, 6.1, { stagger: 0.05, dur: 1.0, dy: 50, blur: 20 });
      const push = E.outCubic(prog(t, 4.8, 9.6));
      root.style.transform = `scale(${(1 + 0.03 * push).toFixed(4)})`;
    };
  });

  // ============================================================ 3. hero demo  9.6 – 21.6
  scene(9.6, 21.6, (root) => {
    const cam = el('div', 'layer', root, { transformOrigin: '50% 45%' });
    const editor = new Editor(cam, 110, 120, 1.0);
    const panel = new Panel(cam, 1080, 92, 1.25);
    const tp = new Touchpad(cam, 1446, 676, 640, 54);
    const head = textBlock(cam, TEXT.s3Head, 'head abs', { left: '118px', top: '640px', fontSize: '80px' });
    const desc = textBlock(cam, TEXT.s3Desc, 'desc abs', { left: '122px', top: '862px', fontSize: '28px' });

    const p1 = panel.addChar('a', '你'); tp.addChar('a', '你');
    const s1 = schedule(p1, 10.8, 560, 0.12);
    const c1 = s1.end + 0.12, k1 = s1.end + 0.9;
    const p2 = panel.addChar('b', '好'); tp.addChar('b', '好');
    const s2 = schedule(p2, k1 + 0.7, 560, 0.12);
    const c2 = s2.end + 0.12, k2 = s2.end + 0.9;
    editor.add('你', k1);
    editor.add('好', k2);
    cue(k1, 'commit'); cue(k2, 'commit');
    cue(9.75, 'soft');

    return (t) => {
      const zoom = E.inOut(prog(t, 9.6, 21.6));
      cam.style.transform = `scale(${(1.0 + 0.035 * zoom).toFixed(4)})`;
      const pe = E.outExpo(prog(t, 9.7, 10.7)), pp = E.outExpo(prog(t, 9.85, 10.85)), pt = E.outExpo(prog(t, 10.0, 11.0));
      editor.root.style.transform = `translate3d(${f2(-90 * (1 - pe))}px,0,0)`;
      editor.root.style.opacity = f2(pe);
      panel.root.style.transform = `translate3d(0,${f2(-70 * (1 - pp))}px,0) scale(${panel.scale})`;
      panel.root.style.opacity = f2(pp);
      tp.place(160 * (1 - pt), 18 * (1 - pt), pt);
      reveal(head, t, 10.1, { stagger: 0.04, dur: 0.9 });
      reveal(desc, t, 10.6, { stagger: 0.012, dur: 0.8, dy: 16, blur: 8 });

      // ink in the panel
      panel.drawChar('a', progresses(s1, t), prog(t, k1, k1 + 0.26));
      panel.groups.a.g.style.display = t > k1 + 0.3 ? 'none' : '';
      panel.drawChar('b', progresses(s2, t), prog(t, k2, k2 + 0.26));
      panel.groups.b.g.style.display = t > k2 + 0.3 ? 'none' : '';
      tp.drawTrail('a', s1, t);
      tp.drawTrail('b', s2, t);

      // candidates: live after recognition, committed after auto-input, cleared when the next character starts
      if (t >= c2 && t < k2) panel.setCands(CANDS['好'], 0, false);
      else if (t >= k2) panel.setCands(CANDS['好'], 0, true);
      else if (t >= s2.strokes[0].t0) panel.setCands([]);
      else if (t >= k1) panel.setCands(CANDS['你'], 0, true);
      else if (t >= c1) panel.setCands(CANDS['你'], 0, false);
      else panel.setCands([]);

      const f = fingerAt(s1, t) || fingerAt(s2, t);
      const touching = !!(f && f.touch);
      panel.setLive(touching);
      panel.setHint(t < s1.strokes[0].t0 || (t >= k1 + 0.26 && t < s2.strokes[0].t0) || t >= k2 + 0.26);
      panel.setTouches(touching ? [{ x: f.x, y: f.y }] : [], null);
      tp.setFinger(f, rippleAt(s1, t) || rippleAt(s2, t));
      editor.render(t);
    };
  });

  // ============================================================ 4. pick a candidate, delete  21.6 – 28.8
  scene(21.6, 28.8, (root) => {
    const panel = new Panel(root, 112, 178, 1.55);
    const editor = new Editor(root, 1112, 548, 0.86);
    const headA = textBlock(root, TEXT.s4HeadA, 'head abs', { left: '1112px', top: '170px', fontSize: '76px' });
    const descA = textBlock(root, TEXT.s4DescA, 'desc abs', { left: '1116px', top: '382px', fontSize: '28px' });
    const headB = textBlock(root, TEXT.s4HeadB, 'head abs', { left: '1112px', top: '170px', fontSize: '76px' });
    const descB = textBlock(root, TEXT.s4DescB, 'desc abs', { left: '1116px', top: '382px', fontSize: '28px' });

    const p = panel.addChar('r', '人?');
    const s = schedule(p, 22.7, 460, 0.14);
    const cands = CANDS['人?'];
    const cT = s.end + 0.12, kT = s.end + 0.9;
    const tapDown = kT + 0.75, tapUp = tapDown + 0.22;
    const bsDown = tapUp + 1.35, bsUp = bsDown + 0.2;
    const capSwitch = bsDown - 0.95;
    editor.add('大', -1);
    editor.add('八', kT, tapUp);
    editor.add('人', tapUp, bsUp);
    cue(kT, 'commit'); cue(tapUp, 'tap'); cue(bsUp, 'tap'); cue(21.75, 'soft');
    const tapAt = cellCenter(1, cands.length);

    return (t) => {
      const pp = E.outExpo(prog(t, 21.7, 22.7)), pe = E.outExpo(prog(t, 21.95, 22.95));
      panel.root.style.transform = `translate3d(${f2(-80 * (1 - pp))}px,0,0) scale(${panel.scale})`;
      panel.root.style.opacity = f2(pp);
      editor.root.style.transform = `translate3d(0,${f2(60 * (1 - pe))}px,0) scale(0.86)`;
      editor.root.style.opacity = f2(pe);
      reveal(headA, t, 21.9, { stagger: 0.04, out: capSwitch });
      reveal(descA, t, 22.3, { stagger: 0.012, dur: 0.8, dy: 16, blur: 8, out: capSwitch });
      reveal(headB, t, capSwitch + 0.35, { stagger: 0.04 });
      reveal(descB, t, capSwitch + 0.6, { stagger: 0.012, dur: 0.8, dy: 16, blur: 8 });

      panel.drawChar('r', progresses(s, t), prog(t, kT, kT + 0.26));
      panel.groups.r.g.style.display = t > kT + 0.3 ? 'none' : '';
      const tapping = t >= tapDown && t < tapUp, bsPressed = t >= bsDown && t < bsUp;
      if (t >= bsUp) panel.setCands([]);
      else if (t >= tapUp) panel.setCands(cands, 1, true);
      else if (t >= tapDown) panel.setCands(cands, 0, true, 1);
      else if (t >= kT) panel.setCands(cands, 0, true);
      else if (t >= cT) panel.setCands(cands, 0, false);
      else panel.setCands([]);
      panel.setBackspace(bsPressed);

      const f = fingerAt(s, t);
      const touches = [];
      let ripple = rippleAt(s, t);
      if (f && f.touch) touches.push({ x: f.x, y: f.y });
      if (tapping) touches.push({ x: tapAt[0], y: tapAt[1] });
      if (bsPressed) touches.push({ x: BS_CENTER[0], y: BS_CENTER[1] });
      if (t >= tapDown && t < tapDown + 0.55) ripple = { x: tapAt[0], y: tapAt[1], p: prog(t, tapDown, tapDown + 0.55) };
      if (t >= bsDown && t < bsDown + 0.55) ripple = { x: BS_CENTER[0], y: BS_CENTER[1], p: prog(t, bsDown, bsDown + 0.55) };
      panel.setTouches(touches, ripple);
      panel.setLive(!!(f && f.touch));
      panel.setHint(t < s.strokes[0].t0 || t >= kT + 0.26);
      editor.render(t);
    };
  });

  // ============================================================ 5. gestures  28.8 – 36.0
  scene(28.8, 36.0, (root) => {
    const title = textBlock(root, TEXT.s5Title, 'head abs', { left: 0, width: W + 'px', textAlign: 'center', top: '150px', fontSize: '76px' });
    const keys = textBlock(root, TEXT.s5Keys, 'desc abs', { left: 0, width: W + 'px', textAlign: 'center', top: '868px', fontSize: '28px' });
    const left0 = (W - (3 * 470 + 2 * 40)) / 2;
    const DIRS = [[[110, 117], [272, 117], 'h'], [[190, 52], [190, 182], 'v'], [[190, 182], [190, 52], 'v']];
    const cards = TEXT.gestures.map(([res, how], k) => {
      const card = el('div', 'card', root, { left: left0 + k * 510 + 'px', top: '300px' });
      const mini = el('div', 'mini', card);
      const svgE = sv('svg', { viewBox: '0 0 380 234' }, mini);
      const gid = 'tr' + k;
      const lg = sv('linearGradient', { id: gid, gradientUnits: 'userSpaceOnUse' }, sv('defs', {}, svgE));
      sv('stop', { offset: 0, 'stop-color': 'rgba(120,180,255,0)' }, lg);
      sv('stop', { offset: 1, 'stop-color': 'rgba(120,180,255,.75)' }, lg);
      const trails = [0, 1].map(() => sv('line', { stroke: `url(#${gid})`, 'stroke-width': 10, 'stroke-linecap': 'round' }, svgE));
      const dots = [0, 1].map(() => el('div', 'mdot', mini));
      const row = el('div', 'abs', card, { left: '45px', top: '316px', display: 'flex', alignItems: 'center', gap: '16px' });
      let badge;
      if (k === 0) badge = el('span', 'keycap', row, null, 'Space');
      else if (k === 1) badge = el('span', 'keycap', row, null, 'Enter ↵');
      else badge = el('span', 'keycap', row, { fontSize: '19px', fontFamily: '"Segoe Fluent Icons", "Segoe MDL2 Assets"' }, '');
      el('span', 'cjk', row, { fontSize: '40px', fontWeight: 700 }, res);
      el('div', 'desc abs', card, { left: '46px', top: '384px', fontSize: '24px' }, how);
      return { card, dots, trails, lg, badge, dir: DIRS[k] };
    });
    cue(28.95, 'soft');
    cards.forEach((c, k) => cue(29.25 + k * 0.12, 'pop'));
    return (t) => {
      reveal(title, t, 28.95, { stagger: 0.035 });
      reveal(keys, t, 30.6, { stagger: 0.012, dur: 0.8, dy: 16, blur: 8 });
      cards.forEach((c, k) => {
        const p = E.outExpo(prog(t, 29.25 + k * 0.12, 30.35 + k * 0.12));
        put(c.card, { y: 80 * (1 - p), o: p, b: 10 * (1 - p) });
        const period = 2.1, ph = ((t - 30.0 - k * 0.3) % period + period) % period / period;
        const [a, b, ori] = c.dir;
        const mv = E.inOut(prog(ph, 0.18, 0.62));
        const alpha = t < 30.0 + k * 0.3 ? 0 : Math.min(prog(ph, 0.04, 0.16), 1 - prog(ph, 0.68, 0.82));
        const x = lerp(a[0], b[0], mv), y = lerp(a[1], b[1], mv);
        const off = ori === 'h' ? [[0, -24], [0, 24]] : [[-26, 0], [26, 0]];
        c.dots.forEach((d, i) => { d.style.left = f2(x + off[i][0]) + 'px'; d.style.top = f2(y + off[i][1]) + 'px'; d.style.opacity = f2(alpha); });
        c.trails.forEach((ln, i) => {
          ln.setAttribute('x1', a[0] + off[i][0]); ln.setAttribute('y1', a[1] + off[i][1]);
          ln.setAttribute('x2', f2(x + off[i][0])); ln.setAttribute('y2', f2(y + off[i][1]));
          ln.style.opacity = f2(alpha * (mv > 0.01 ? 1 : 0));
        });
        c.lg.setAttribute('x1', a[0]); c.lg.setAttribute('y1', a[1]); c.lg.setAttribute('x2', f2(x)); c.lg.setAttribute('y2', f2(y));
        const pulse = t < 30.0 + k * 0.3 ? 0 : bump(ph, 0.6, 0.8);
        c.badge.style.transform = `scale(${(1 + 0.12 * pulse).toFixed(3)})`;
        c.badge.style.background = `rgba(${Math.round(lerp(255, 10, pulse))},${Math.round(lerp(255, 132, pulse))},255,${(0.1 + 0.8 * pulse).toFixed(3)})`;
      });
    };
  });

  // ============================================================ 6. settings & details  36.0 – 43.2
  scene(36.0, 43.2, (root) => {
    const persp = el('div', 'layer', root, { perspective: '2200px' });
    const shot = el('div', 'shot', persp, { left: '800px', top: '168px', width: '1040px', height: '731px', transformOrigin: '30% 50%' });
    const img = el('img', null, shot); img.src = `assets/${LANG}/settings.png`;
    const tray = el('div', 'shot', persp, { left: '660px', top: '612px', width: '400px', height: '351px', borderRadius: '10px', transformOrigin: '50% 50%' });
    const img2 = el('img', null, tray); img2.src = `assets/${LANG}/tray.png`;
    const title = textBlock(root, TEXT.s6Title, 'head abs', { left: '118px', top: '170px', fontSize: '80px' });
    const chips = TEXT.chips.map(([g, label], i) => {
      const c = el('div', 'chip', root, { left: '120px', top: 440 + i * 80 + 'px' });
      el('span', 'ico', c, null, g);
      el('span', null, c, null, label);
      return c;
    });
    cue(36.15, 'soft');
    chips.forEach((c, i) => cue(37.0 + i * 0.11, 'pop'));
    return (t) => {
      reveal(title, t, 36.2, { stagger: 0.045 });
      const ps = E.outExpo(prog(t, 36.3, 37.6));
      const drift = prog(t, 36.0, 43.2);
      shot.style.transform = `translate3d(${f2(120 * (1 - ps) - 30 * drift)}px,${f2(20 * (1 - ps))}px,0) rotateY(${f2(-16 + 6 * (1 - ps) + 4 * drift)}deg) rotateX(${f2(4)}deg)`;
      shot.style.opacity = f2(ps);
      const pt = E.outExpo(prog(t, 37.5, 38.7));
      tray.style.transform = `translate3d(${f2(-20 * drift)}px,${f2(140 * (1 - pt) - 16 * drift)}px,80px) rotateY(${f2(-10 + 3 * drift)}deg)`;
      tray.style.opacity = f2(pt);
      chips.forEach((c, i) => {
        const p = E.outExpo(prog(t, 37.0 + i * 0.11, 38.0 + i * 0.11));
        put(c, { x: -50 * (1 - p), o: p, b: 12 * (1 - p) });
      });
    };
  });

  // ============================================================ 7. outro  43.2 – END
  scene(43.2, END + 1, (root) => {
    const ICON = 200;
    const iconWrap = el('div', 'abs', root, { left: (W - ICON) / 2 + 'px', top: '196px', width: '256px', height: '256px', transformOrigin: '0 0', transform: `scale(${ICON / 256})` });
    const icon = makeIcon(iconWrap);
    const title = textBlock(root, 'TouchWrite', 'display abs', { left: 0, width: W + 'px', textAlign: 'center', top: '432px', fontSize: '112px', fontWeight: 600, letterSpacing: '-2px' });
    const tag = textBlock(root, TEXT.s7Tag, 'head abs', { left: 0, width: W + 'px', textAlign: 'center', top: '602px', fontSize: '58px', fontWeight: 700, letterSpacing: '0' });
    const spec = textBlock(root, TEXT.s7Spec, 'desc abs', { left: 0, width: W + 'px', textAlign: 'center', top: '716px', fontSize: '28px' });
    cue(43.45, 'title'); cue(43.7, 'draw', { dur: 0.9 }); cue(44.4, 'soft');
    return (t) => {
      const bgp = prog(t, 43.4, 44.2);
      icon.bg(lerp(0.4, 1, E.outBack(bgp)), E.outCubic(bgp));
      icon.draw(E.inOut(prog(t, 43.7, 44.6)), 13, 0.5 * bump(t, 43.7, 45.2));
      reveal(title, t, 44.1, { stagger: 0.045, dur: 0.9, dy: 30 });
      reveal(tag, t, 44.7, { stagger: 0.05, dur: 1.0 });
      reveal(spec, t, 45.5, { stagger: 0.015, dur: 0.9, dy: 16, blur: 8 });
      root.style.transform = `scale(${(1 + 0.025 * E.inOut(prog(t, 43.2, END))).toFixed(4)})`;
    };
  });

  // scene changes
  for (const tt of [4.8, 9.6, 21.6, 28.8, 36.0, 43.2]) cue(tt - 0.35, 'whoosh');
}

// ------------------------------------------------------------------ covers
function buildCover() {
  const is43 = MODE === 'cover43';
  const root = el('div', 'layer', scenesLayer);
  // wordmark
  const wm = el('div', 'abs', root, { left: is43 ? '86px' : '118px', top: is43 ? '74px' : '96px', display: 'flex', alignItems: 'center' });
  const iconBox = el('div', null, wm, { position: 'relative', width: '72px', height: '72px' });
  const iconWrap = el('div', 'abs', iconBox, { left: 0, top: 0, width: '256px', height: '256px', transformOrigin: '0 0', transform: `scale(${72 / 256})` });
  const icon = makeIcon(iconWrap);
  icon.bg(1, 1); icon.draw(1, 13, 0);
  el('div', 'display', wm, { marginLeft: '20px', fontSize: '44px', fontWeight: 600, letterSpacing: '-0.5px' }, 'TouchWrite');

  const head = textBlock(root, TEXT.coverHead, 'head abs', is43
    ? { left: '80px', top: '190px', fontSize: '164px', lineHeight: 1.06, fontWeight: 900, letterSpacing: '-3px' }
    : { left: '108px', top: '230px', fontSize: '176px', lineHeight: 1.06, fontWeight: 900, letterSpacing: '-3px' });
  const tag = el('div', 'cjk abs', root, {
    left: is43 ? '88px' : '116px', top: is43 ? '578px' : '660px', padding: '10px 30px', borderRadius: '40px',
    background: 'linear-gradient(90deg, #0A84FF, #5E5CE6)', fontSize: is43 ? '44px' : '48px', fontWeight: 800,
    boxShadow: '0 20px 50px rgba(10,132,255,.35)',
  }, TEXT.coverTag);
  const sub = el('div', 'cjk abs', root, { left: is43 ? '92px' : '120px', top: is43 ? '688px' : '782px', fontSize: is43 ? '38px' : '42px', fontWeight: 500, color: 'rgba(255,255,255,.78)' }, TEXT.coverSub);

  const panel = is43 ? new Panel(root, 776, 286, 1.04) : new Panel(root, 990, 170, 1.48);
  panel.addChar('c', '好');
  panel.drawChar('c', STROKES['好'].map(() => 1), 0);
  panel.setCands(CANDS['好'], 0, false);
  panel.setHint(false);
  const lastPath = panel.groups.c.paths[5];
  const end = lastPath.p.getPointAtLength(lastPath.len);
  panel.setTouches([{ x: end.x, y: end.y }], null);
  panel.setLive(true);

  const tp = is43 ? new Touchpad(root, 1068, 796, 520, 58) : new Touchpad(root, 1418, 818, 600, 58);
  tp.addChar('c', '好');
  tp.groups.c.forEach((s, i) => { s.p.style.strokeDashoffset = 0; s.p.style.opacity = i === 5 ? 0.9 : 0.25; });
  tp.setFinger({ x: end.x, y: end.y, lift: 0 }, null);
  return () => {
    applyGradients(head);
    head.spans.forEach((s) => { s.style.opacity = 1; s.style.transform = 'none'; s.style.filter = 'none'; });
  };
}

// ------------------------------------------------------------------ frame
let coverInit = null;
window.render = (t) => {
  renderBackground(MODE === 'video' ? t : 7.5);
  if (MODE !== 'video') {
    if (coverInit) { coverInit(); coverInit = null; }
    black.style.opacity = 0;
    return;
  }
  for (const s of scenes) {
    const a = sceneEnvelope(s, t);
    if (a <= 0.001) { s.root.style.display = 'none'; continue; }
    s.root.style.display = '';
    s.root.style.opacity = f2(a);
    const enter = s.start > 0 ? 1 - E.outCubic(prog(t, s.start - 0.2, s.start + 0.4)) : 0;
    const exit = E.inCubic(prog(t, s.end - 0.4, s.end + 0.2));
    s.root.style.filter = enter + exit > 0.01 ? `blur(${f2(10 * enter + 8 * exit)}px)` : 'none';
    s.root.style.transform = `scale(${(1 - 0.02 * enter + 0.03 * exit).toFixed(4)})`;
    s.update(t);
  }
  black.style.opacity = f2(Math.max(1 - E.outCubic(prog(t, 0.0, 0.7)), E.inOut(prog(t, END - 0.8, END))));
};

// ------------------------------------------------------------------ ready
(async () => {
  const cjk = LANG === 'hant' ? 'Noto Sans TC' : 'Noto Sans SC';
  await Promise.all([
    document.fonts.load(`900 100px "${cjk}"`), document.fonts.load(`800 100px "${cjk}"`), document.fonts.load(`700 100px "${cjk}"`),
    document.fonts.load(`400 40px "${cjk}"`), document.fonts.load(`500 40px "${cjk}"`),
    document.fonts.load('600 100px "Segoe UI Variable Display"'), document.fonts.load('20px "Segoe Fluent Icons"'),
  ]);
  await document.fonts.ready;
  await Promise.all([...document.images].map((i) => i.decode().catch(() => {})));
  if (MODE !== 'video') coverInit = buildCover();
  EVENTS.sort((a, b) => a.t - b.t);
  window.__events = EVENTS;
  window.__duration = END;
  window.render(0);
  window.__ready = true;
})();
})();
