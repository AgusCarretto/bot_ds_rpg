#!/usr/bin/env node
// Genera los banners PROVISORIOS del perfil (v0.50.0): uno de tier I por cada bendición, 720x240 (3:1), en Assets/banners/banner_<clave>_1.png.
//
//   node tools/ui-art/make-banners.js            # escribe Assets/banners/*.png
//   node tools/ui-art/make-banners.js <carpeta>  # o en otra carpeta
//
// Son simples a propósito (un degradé del color de la bendición, una trama de rombos, un borde y un círculo al centro): sirven para ver el banner andando en el perfil y en /config. Cuando haya arte propio se
// reemplaza el archivo con el MISMO nombre (ver docs/arte-banners.md); los tiers II a V usan el de tier I hasta que exista su archivo (GameData/ProfileBanners.cs). Sin dependencias: codificador PNG mínimo (zlib).

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const OUT = path.resolve(process.argv[2] || path.join(__dirname, '..', '..', 'Assets', 'banners'));
const W = 720, H = 240;

// Las claves son las de GameData/BlessingCatalog.cs (player_blessings.blessing_key); el color, el de la idea de cada una.
const BLESSINGS = {
  mano_lenador: '#8b5a2b',
  pico_fino: '#6b7a8f',
  brasa_vieja: '#d9480f',
  ojo_de_halcon: '#3b82c4',
  aprendiz: '#5b5bd6',
  filo_antiguo: '#9aa5b1',
  piel_curtida: '#c08a4a',
  corazon_de_brasa: '#c92a2a',
  manada: '#2f9e44',
  buen_pienso: '#f08c00',
  bolsillo_hondo: '#e0a800',
  alforja_del_fogonero: '#0ca678',
  brasa_de_color: '#e8590c', // el color del tier I de la bendición (GameData/BlessingCatalog.cs, ColorValues)
};

// ---------- PNG ----------
const crcTable = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(buf) { let c = 0xffffffff; for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function encodePng(w, h, rgb) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; rgb.copy(raw, y * (w * 3 + 1) + 1, y * w * 3, (y + 1) * w * 3); }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0; // 8 bits, RGB
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

// ---------- Dibujo ----------
const hex = (s) => [parseInt(s.slice(1, 3), 16), parseInt(s.slice(3, 5), 16), parseInt(s.slice(5, 7), 16)];
const mix = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));
const BLACK = [0, 0, 0], WHITE = [255, 255, 255];

function render(color) {
  const base = hex(color);
  const dark = mix(base, BLACK, 0.55), light = mix(base, WHITE, 0.35);
  const buf = Buffer.alloc(W * H * 3);
  const cx = W / 2, cy = H / 2;
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      // degradé en diagonal, en 24 escalones (se comprime mucho mejor que uno continuo)
      const t = Math.round((((x / W) * 0.6) + ((y / H) * 0.4)) * 24) / 24;
      let c = t < 0.5 ? mix(dark, base, t * 2) : mix(base, light, (t - 0.5) * 2);

      // trama de rombos, apenas más clara
      const d1 = (x + y) % 48, d2 = (x - y + 960) % 48;
      if (d1 < 2 || d2 < 2) c = mix(c, WHITE, 0.10);

      // círculo al centro: aro claro y adentro un disco más oscuro con un punto de luz
      const r = Math.hypot(x - cx, y - cy);
      if (r < 74) c = r < 66 ? mix(c, BLACK, 0.38) : mix(c, WHITE, 0.55);
      if (r < 30) c = mix(c, WHITE, 0.30);

      // borde: un marco claro de 8 px y una línea oscura adentro
      const edge = Math.min(x, W - 1 - x, y, H - 1 - y);
      if (edge < 8) c = mix(base, WHITE, 0.45);
      else if (edge < 11) c = mix(base, BLACK, 0.55);

      const o = (y * W + x) * 3;
      buf[o] = c[0]; buf[o + 1] = c[1]; buf[o + 2] = c[2];
    }
  }
  return encodePng(W, H, buf);
}

fs.mkdirSync(OUT, { recursive: true });
for (const [key, color] of Object.entries(BLESSINGS)) {
  const file = path.join(OUT, `banner_${key}_1.png`);
  fs.writeFileSync(file, render(color));
  console.log(`${path.basename(file)}  ${(fs.statSync(file).size / 1024).toFixed(1)} KB`);
}
