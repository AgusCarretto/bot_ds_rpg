#!/usr/bin/env node
// Genera los emojis PROVISORIOS de la interfaz (los 25 nombres de GameData/UiEmojis.cs, ver docs/ui-iconos.md) como PNG de 128x128 en pixel art.
//
//   node tools/ui-art/make-ui-emojis.js            # escribe docs/ui-emojis/*.png
//   node tools/ui-art/make-ui-emojis.js <carpeta>  # o en otra carpeta
//
// El nombre de cada archivo es el nombre del emoji: en el Developer Portal (la aplicación -> Emojis) se arrastran todos juntos y el portal los nombra por el archivo.
// Son provisorios a propósito: sirven para ver todo andando y las barras ya encajan pieza con pieza; cuando haya arte propio se borra el emoji del portal y se sube otro con el MISMO
// nombre (el bot los busca por nombre, no por id). Sin dependencias: dibuja en una grilla de 32x32 y la agranda 4x (128x128) con un codificador PNG mínimo (zlib de Node).

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const OUT = path.resolve(process.argv[2] || path.join(__dirname, '..', '..', 'docs', 'ui-emojis'));
const G = 32;      // lado de la grilla lógica
const SCALE = 4;   // 32 * 4 = 128

// ---------- PNG ----------
const crcTable = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(buf) { let c = 0xffffffff; for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function encodePng(w, h, rgba) {
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4); }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

// ---------- Lienzo lógico ----------
const hex = (s) => [parseInt(s.slice(1, 3), 16), parseInt(s.slice(3, 5), 16), parseInt(s.slice(5, 7), 16), 255];
class Canvas {
  constructor() { this.px = Array.from({ length: G }, () => Array(G).fill(null)); }
  set(x, y, color) { if (x >= 0 && x < G && y >= 0 && y < G) this.px[y][x] = typeof color === 'string' ? hex(color) : color; }
  get(x, y) { return x >= 0 && x < G && y >= 0 && y < G ? this.px[y][x] : null; }
  rect(x0, y0, x1, y1, color) { for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) this.set(x, y, color); }
  toPng() {
    const W = G * SCALE, buf = Buffer.alloc(W * W * 4);
    for (let y = 0; y < W; y++) for (let x = 0; x < W; x++) {
      const c = this.px[Math.floor(y / SCALE)][Math.floor(x / SCALE)];
      if (c) { const o = (y * W + x) * 4; buf[o] = c[0]; buf[o + 1] = c[1]; buf[o + 2] = c[2]; buf[o + 3] = c[3]; }
    }
    return encodePng(W, W, buf);
  }
}

// ---------- Barras ----------
// Una banda de 14 filas en el medio del lienzo; el medio llega de borde a borde (así las piezas se pegan sin costura) y los extremos redondean SOLO por fuera.
const BAND_TOP = 9, BAND_ROWS = 14;
const LEFT_BOUND = [3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3]; // primera columna dibujada de cada fila en el extremo izquierdo
const PALETTES = {
  hp:    { outline: '#3a1212', rows: ['#ff9a86', '#f2604b', '#e0412f', '#e0412f', '#e0412f', '#e0412f', '#e0412f', '#d03626', '#c02e20', '#b0281c', '#a02218', '#8c1d14'] },
  xp:    { outline: '#10234a', rows: ['#a9ccff', '#6ea2f5', '#4a86ec', '#4a86ec', '#4a86ec', '#4a86ec', '#4a86ec', '#3b73d6', '#3162c0', '#2a54aa', '#224794', '#1b3a7d'] },
  pesar: { outline: '#25103f', rows: ['#d8b4ff', '#b583f0', '#9a5fe0', '#9a5fe0', '#9a5fe0', '#9a5fe0', '#9a5fe0', '#8750cc', '#7742b8', '#6838a4', '#5a2f90', '#4b267b'] },
  empty: { outline: '#121318', rows: ['#565a66', '#484c57', '#3b3e48', '#3b3e48', '#3b3e48', '#3b3e48', '#3b3e48', '#34363f', '#2e3038', '#282a31', '#23252b', '#1e2025'] },
};
function barPiece(kind, part, full) {
  const pal = PALETTES[full ? kind : 'empty'];
  const c = new Canvas();
  for (let r = 0; r < BAND_ROWS; r++) {
    const y = BAND_TOP + r;
    const lb = part === 'l' ? LEFT_BOUND[r] : 0;
    const rb = part === 'r' ? G - 1 - LEFT_BOUND[r] : G - 1;
    for (let x = lb; x <= rb; x++) {
      const isEdge = r === 0 || r === BAND_ROWS - 1 || (part === 'l' && x === lb) || (part === 'r' && x === rb);
      c.set(x, y, isEdge ? pal.outline : pal.rows[r - 1]);
    }
  }
  return c;
}

// ---------- Letras (fuente 5x7) ----------
const FONT = {
  L: ['X....', 'X....', 'X....', 'X....', 'X....', 'X....', 'XXXXX'],
  V: ['X...X', 'X...X', 'X...X', 'X...X', '.X.X.', '.X.X.', '..X..'],
  X: ['X...X', 'X...X', '.X.X.', '..X..', '.X.X.', 'X...X', 'X...X'],
  P: ['XXXX.', 'X...X', 'X...X', 'XXXX.', 'X....', 'X....', 'X....'],
  F: ['XXXXX', 'X....', 'X....', 'XXXX.', 'X....', 'X....', 'X....'],
  N: ['X...X', 'XX..X', 'XX..X', 'X.X.X', 'X..XX', 'X..XX', 'X...X'],
};
function badge(text, palette) {
  const c = new Canvas();
  // cuadrado redondeado de 28x28 con borde, degradé vertical y brillo arriba
  const x0 = 2, y0 = 2, x1 = 29, y1 = 29;
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
    const corner = (x - x0 < 2 && y - y0 < 2 - (x - x0)) || (x1 - x < 2 && y - y0 < 2 - (x1 - x)) || (x - x0 < 2 && y1 - y < 2 - (x - x0)) || (x1 - x < 2 && y1 - y < 2 - (x1 - x));
    if (corner) continue;
    const edge = x === x0 || x === x1 || y === y0 || y === y1 || (x - x0 === 1 && (y === y0 + 1 || y === y1 - 1)) || (x1 - x === 1 && (y === y0 + 1 || y === y1 - 1));
    const t = (y - y0) / (y1 - y0);
    c.set(x, y, edge ? palette.outline : t < 0.12 ? palette.light : t < 0.55 ? palette.main : t < 0.85 ? palette.dark : palette.darker);
  }
  // las dos letras, cada punto de la fuente = 2x2 celdas, con sombra oscura
  const letters = text.split('');
  const totalW = letters.length * 5 * 2 + (letters.length - 1) * 2;
  let ox = Math.floor((G - totalW) / 2), oy = Math.floor((G - 14) / 2) + 0;
  for (const dx of [[1, 1], [0, 0]]) {
    letters.forEach((ch, i) => {
      FONT[ch].forEach((row, ry) => [...row].forEach((v, rx) => {
        if (v !== 'X') return;
        const bx = ox + i * 12 + rx * 2 + dx[0], by = oy + ry * 2 + dx[1];
        const color = dx[0] === 1 ? palette.shadow : palette.text;
        c.rect(bx, by, bx + 1, by + 1, color);
      }));
    });
  }
  return c;
}
const BADGES = {
  ui_lv: ['LV', { outline: '#0c1c4a', light: '#7eb0ff', main: '#2f6fe0', dark: '#2559c0', darker: '#1b46a0', text: '#ffffff', shadow: '#0c2260' }],
  ui_fn: ['FN', { outline: '#4a0e08', light: '#ff9a54', main: '#e8461f', dark: '#c93314', darker: '#a52610', text: '#ffe27a', shadow: '#5a1308' }],
  ui_xp: ['XP', { outline: '#0a3a2a', light: '#7ae6b4', main: '#1fb27a', dark: '#17966a', darker: '#117a56', text: '#ffffff', shadow: '#0a4a35' }],
};

// ---------- Formas sueltas ----------
function dist(x, y, cx, cy) { return Math.hypot(x - cx, y - cy); }
function coin() {
  const c = new Canvas();
  for (let y = 0; y < G; y++) for (let x = 0; x < G; x++) {
    const d = dist(x + 0.5, y + 0.5, 16, 16);
    if (d > 13.4) continue;
    if (d > 12) c.set(x, y, '#6b4309');
    else if (d > 10.2) c.set(x, y, (x + y < 30) ? '#ffe27a' : '#d99a1c');
    else if (d > 9) c.set(x, y, '#b97a10');
    else c.set(x, y, (x + y < 28) ? '#ffd23d' : '#f0b429');
  }
  // reflejo y la marca central
  c.rect(9, 9, 11, 9, '#fff2b0'); c.rect(9, 10, 9, 11, '#fff2b0');
  c.rect(15, 11, 16, 21, '#b97a10'); c.rect(12, 13, 19, 14, '#b97a10'); c.rect(12, 18, 19, 19, '#b97a10');
  return c;
}
function bank() {
  const c = new Canvas();
  c.rect(3, 4, 28, 27, '#1d2433');
  c.rect(4, 5, 27, 26, '#7384a6');
  c.rect(4, 5, 27, 8, '#9aaacb'); c.rect(4, 23, 27, 26, '#5b6b8c');
  c.rect(4, 5, 5, 26, '#8696b8'); c.rect(26, 5, 27, 26, '#4f5e7d');
  // rueda de la caja fuerte
  for (let y = 0; y < G; y++) for (let x = 0; x < G; x++) {
    const d = dist(x + 0.5, y + 0.5, 16, 16);
    if (d <= 7.2) c.set(x, y, '#1d2433');
    if (d <= 6.2) c.set(x, y, '#ffd23d');
    if (d <= 4.6) c.set(x, y, '#c98f16');
    if (d <= 3) c.set(x, y, '#1d2433');
  }
  c.rect(15, 8, 16, 24, '#1d2433'); c.rect(8, 15, 24, 16, '#1d2433');
  c.rect(15, 15, 16, 16, '#ffd23d');
  return c;
}
function star(c, cx, cy, r, color, core) {
  for (let i = -r; i <= r; i++) { c.set(cx + i, cy, color); c.set(cx, cy + i, color); }
  for (let i = -Math.floor(r / 2); i <= Math.floor(r / 2); i++) { c.set(cx + i, cy + i, color); c.set(cx + i, cy - i, color); }
  c.set(cx, cy, core);
}
function dust() {
  const c = new Canvas();
  star(c, 16, 17, 9, '#b583f0', '#ffffff');
  star(c, 7, 8, 5, '#6fd6ff', '#ffffff');
  star(c, 25, 7, 4, '#ffe27a', '#ffffff');
  star(c, 24, 25, 4, '#6fd6ff', '#ffffff');
  star(c, 6, 25, 3, '#ffe27a', '#ffffff');
  return c;
}
const FLAME = [
  '.......XX.......', '......XXX.......', '.....XXXX.......', '.....XXXXX......', '....XXXXXX..X...', '...XXXXXXXX.XX..', '...XXXXXXXXXXX..', '..XXXXXXXXXXXXX.',
  '..XXXXXXXXXXXXX.', '..XXXXXXXXXXXXX.', '..XXXXXXXXXXXXX.', '...XXXXXXXXXXX..', '....XXXXXXXXX...', '.....XXXXXXX....', '......XXXXX.....', '.......XXX......',
];
function flame() {
  const mask = FLAME.map((r) => [...r].map((v) => v === 'X'));
  const at = (x, y) => (y >= 0 && y < 16 && x >= 0 && x < 16 && mask[y][x]);
  const erode = (x, y, n) => { for (let dy = -n; dy <= n; dy++) for (let dx = -n; dx <= n; dx++) if (!at(x + dx, y + dy)) return false; return true; };
  const c = new Canvas();
  for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
    if (!at(x, y)) continue;
    const color = erode(x, y, 3) && y > 7 ? '#fff0a0' : erode(x, y, 2) && y > 5 ? '#ffd23d' : erode(x, y, 1) ? '#ff8a1f' : '#7a1d0a';
    c.rect(x * 2, y * 2, x * 2 + 1, y * 2 + 1, color);
  }
  return c;
}

// ---------- Salida ----------
fs.mkdirSync(OUT, { recursive: true });
const files = {};
for (const [name, [text, palette]] of Object.entries(BADGES)) files[name] = badge(text, palette);
files.ui_oro = coin();
files.ui_banco = bank();
files.ui_polvo = dust();
files.ui_racha = flame();
for (const kind of ['hp', 'xp', 'pesar']) {
  for (const part of ['l', 'm', 'r']) {
    files[`bar_${kind}_${part}_f`] = barPiece(kind, part, true);
    files[`bar_${kind}_${part}_e`] = barPiece(kind, part, false);
  }
}
for (const [name, canvas] of Object.entries(files)) fs.writeFileSync(path.join(OUT, `${name}.png`), canvas.toPng());
console.log(`${Object.keys(files).length} emojis en ${OUT}`);
