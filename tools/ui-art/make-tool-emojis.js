#!/usr/bin/env node
// Genera los emojis PROVISORIOS del hacha y el pico del Carpintero (v0.29.0, Database/add_tools.sql) como PNG de 128x128 en pixel art:
// hacha_hierro / pico_hierro (Zona 3, acero) y hacha_escoria / pico_escoria (Zona 5, obsidiana con brasas). El nombre de cada archivo es el nombre que hay que ponerle al emoji en el
// Developer Portal (la aplicación -> Emojis); después se conecta en Database/update_item_emojis.sql y se corre Database/apply_emojis.sql (ver CLAUDE.md, «Item emojis»).
//
//   node tools/ui-art/make-tool-emojis.js            # escribe docs/tool-emojis/*.png
//   node tools/ui-art/make-tool-emojis.js <carpeta>  # o en otra carpeta
//
// Son provisorios a propósito: cuando haya arte propio se borra el emoji del portal y se sube otro con el MISMO nombre. Sin dependencias (grilla de 32x32 agrandada 4x con un PNG mínimo).

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const OUT = path.resolve(process.argv[2] || path.join(__dirname, '..', '..', 'docs', 'tool-emojis'));
const G = 32, SCALE = 4;

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
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

const hex = (s) => [parseInt(s.slice(1, 3), 16), parseInt(s.slice(3, 5), 16), parseInt(s.slice(5, 7), 16), 255];
class Canvas {
  constructor() { this.px = Array.from({ length: G }, () => Array(G).fill(null)); }
  set(x, y, color) { if (x >= 0 && x < G && y >= 0 && y < G) this.px[y][x] = hex(color); }
  get(x, y) { return x >= 0 && x < G && y >= 0 && y < G ? this.px[y][x] : null; }
  // Pinta los píxeles que cumplen una condición f(x, y) -> color | null.
  paint(f) { for (let y = 0; y < G; y++) for (let x = 0; x < G; x++) { const c = f(x + 0.5, y + 0.5); if (c) this.set(x, y, c); } }
  // Contorno oscuro grueso alrededor de todo lo dibujado (un píxel hacia afuera).
  outline(color) {
    const mark = [];
    for (let y = 0; y < G; y++) for (let x = 0; x < G; x++) {
      if (this.get(x, y)) continue;
      if ([[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => this.get(x + dx, y + dy))) mark.push([x, y]);
    }
    for (const [x, y] of mark) this.set(x, y, color);
  }
  toPng() {
    const W = G * SCALE, buf = Buffer.alloc(W * W * 4);
    for (let y = 0; y < W; y++) for (let x = 0; x < W; x++) {
      const c = this.px[Math.floor(y / SCALE)][Math.floor(x / SCALE)];
      if (c) { const o = (y * W + x) * 4; buf[o] = c[0]; buf[o + 1] = c[1]; buf[o + 2] = c[2]; buf[o + 3] = c[3]; }
    }
    return encodePng(W, W, buf);
  }
}

// Geometría: el mango va en diagonal "/" de abajo a la izquierda (5,27) hasta arriba a la derecha (21,11).
const H0 = [5, 27], H1 = [21, 11];
function distToSegment(px, py, [ax, ay], [bx, by]) {
  const dx = bx - ax, dy = by - ay, t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
  return { d: Math.hypot(px - (ax + t * dx), py - (ay + t * dy)), t };
}
function inPolygon(px, py, pts) {
  let inside = false;
  for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
    const [xi, yi] = pts[i], [xj, yj] = pts[j];
    if ((yi > py) !== (yj > py) && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

const THEMES = {
  hierro: { outline: '#1b1f27', head: '#aeb8c6', headLight: '#e6edf6', headDark: '#6d7a8c', wood: '#9a6a36', woodLight: '#c08a4a', woodDark: '#6b4522', glow: null },
  escoria: { outline: '#150d14', head: '#4a3a52', headLight: '#7a5f86', headDark: '#2b1f33', wood: '#5e3b27', woodLight: '#8a5a3a', woodDark: '#3b2417', glow: '#ff7a2f' },
};

function handle(c, t) {
  c.paint((x, y) => {
    const { d, t: u } = distToSegment(x, y, H0, H1);
    if (d > 1.35) return null;
    return d < 0.55 ? t.woodLight : d > 1.0 ? t.woodDark : t.wood;
  });
}

function axe(t) {
  const c = new Canvas();
  handle(c, t);
  // La hoja: una cuña ancha en la punta del mango, con el filo curvo hacia arriba a la izquierda.
  const blade = [[13, 3], [22, 2], [28, 6], [29, 12], [24, 13], [20, 15], [16, 9]];
  c.paint((x, y) => {
    if (!inPolygon(x, y, blade)) return null;
    const edge = Math.hypot(x - 13, y - 3) < 4.2 || x + y < 22; // el filo, hacia arriba a la izquierda
    if (edge) return t.headLight;
    return y > 10 ? t.headDark : t.head;
  });
  if (t.glow) c.paint((x, y) => (inPolygon(x, y, blade) && Math.abs(x - 22) + Math.abs(y - 7) < 2.2 ? t.glow : null));
  c.outline(t.outline);
  return c;
}

function pick(t) {
  const c = new Canvas();
  handle(c, t);
  // La cabeza: un arco grueso que cruza la punta del mango, con las puntas afinadas.
  const C = [11, 22], R = 14.5;
  c.paint((x, y) => {
    const dx = x - C[0], dy = y - C[1], r = Math.hypot(dx, dy);
    const ang = Math.atan2(-dy, dx); // 0 = derecha, positivo hacia arriba
    const center = Math.PI / 4;     // la dirección del mango, hacia arriba a la derecha
    const off = Math.abs(ang - center);
    if (off > 0.95) return null;
    const half = 1.6 * (1 - off / 0.95) + 0.5; // más gruesa al medio, más fina en las puntas
    if (Math.abs(r - R) > half) return null;
    return r < R - half * 0.35 ? t.headLight : r > R + half * 0.45 ? t.headDark : t.head;
  });
  if (t.glow) c.paint((x, y) => (Math.hypot(x - 21.5, y - 11.5) < 1.6 ? t.glow : null));
  c.outline(t.outline);
  return c;
}

fs.mkdirSync(OUT, { recursive: true });
const files = { hacha_hierro: axe(THEMES.hierro), pico_hierro: pick(THEMES.hierro), hacha_escoria: axe(THEMES.escoria), pico_escoria: pick(THEMES.escoria) };
for (const [name, canvas] of Object.entries(files)) { fs.writeFileSync(path.join(OUT, name + '.png'), canvas.toPng()); }
console.log(`Listo: ${Object.keys(files).length} emojis provisorios en ${OUT}`);
