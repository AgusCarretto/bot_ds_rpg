#!/usr/bin/env node
// Genera los banners PROVISORIOS del perfil (v0.50.0): los 5 tiers de cada bendición (13 × 5 = 65 archivos), 300x100 (3:1), en Assets/banners/banner_<clave>_<tier>.png.
//
//   node tools/ui-art/make-banners.js            # escribe Assets/banners/*.png
//   node tools/ui-art/make-banners.js <carpeta>  # o en otra carpeta
//
// Cada bendición tiene UN banner que EVOLUCIONA con su nivel (tier I a V): el I es un degradé del color de la bendición con una trama de rombos y un círculo al centro; el II suma un aro por fuera; el III, cuatro
// rombos al costado; el IV, un brillo que sale del centro; el V, el marco dorado, las esquinas doradas y el punto del centro dorado. Son provisorios a propósito: sirven para ver la evolución andando en el perfil y en /config.
// Cuando haya arte propio se reemplaza el archivo con el MISMO nombre (ver docs/arte-banners.md). Sin dependencias: codificador PNG mínimo (zlib).
//
// EL TAMAÑO (300x100) es lo que decide cuánto ocupa en Discord: en la compu un adjunto chico se muestra en su tamaño y uno grande se achica a unos 400 px; con 720x240 salía a todo el ancho y quedaba enorme (el dueño,
// 2026-11-05: «para pc está recontra ancho»). Para cambiarlo, tocar W y H acá (todo lo demás se escala solo) y volver a correr el script; GameData/ProfileBanners.cs no depende del tamaño.

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const OUT = path.resolve(process.argv[2] || path.join(__dirname, '..', '..', 'Assets', 'banners'));
const W = 300, H = 100;
const S = W / 720; // todo se dibujó pensando en 720 de ancho: S lo escala

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
const TIERS = [1, 2, 3, 4, 5];

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
const BLACK = [0, 0, 0], WHITE = [255, 255, 255], GOLD = hex('#f2c94c');
const px = (v) => Math.max(1, Math.round(v * S));

function render(color, tier) {
  const base = hex(color);
  const dark = mix(base, BLACK, 0.55), light = mix(base, WHITE, 0.35);
  const buf = Buffer.alloc(W * H * 3);
  const cx = W / 2, cy = H / 2;
  const lattice = px(48), emblem = px(74), ring = px(8), dotR = px(30);
  const border = px(8), line = Math.max(2, px(3));
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      // degradé en diagonal, en 24 escalones (se comprime mucho mejor que uno continuo)
      const t = Math.round((((x / W) * 0.6) + ((y / H) * 0.4)) * 24) / 24;
      let c = t < 0.5 ? mix(dark, base, t * 2) : mix(base, light, (t - 0.5) * 2);

      // trama de rombos, apenas más clara
      const d1 = (x + y) % lattice, d2 = (x - y + lattice * 40) % lattice;
      if (d1 < 1 || d2 < 1) c = mix(c, WHITE, 0.10);

      const r = Math.hypot(x - cx, y - cy);

      // tier IV y V: un brillo que sale del centro
      if (tier >= 4 && r < emblem * 1.9) c = mix(c, WHITE, 0.24 * (1 - r / (emblem * 1.9)));

      // tier II en adelante: un aro por fuera del círculo
      if (tier >= 2 && r >= emblem + px(8) && r < emblem + px(8) + Math.max(1, px(4))) c = mix(c, WHITE, 0.5);

      // tier III en adelante: cuatro rombos al costado (dos de cada lado)
      if (tier >= 3) {
        for (const dx of [-px(150), -px(220), px(150), px(220)]) {
          if (Math.abs(x - (cx + dx)) + Math.abs(y - cy) < px(18)) c = mix(c, WHITE, 0.6);
        }
      }

      // el círculo del centro: aro claro y adentro un disco más oscuro con un punto de luz (dorado en el tier V)
      if (r < emblem) c = r < emblem - ring ? mix(c, BLACK, 0.38) : mix(c, WHITE, 0.55);
      if (r < dotR) c = tier >= 5 ? GOLD : mix(c, WHITE, 0.30);

      // el borde: un marco claro y una línea oscura adentro; el tier V lo lleva dorado y con las esquinas doradas
      const edge = Math.min(x, W - 1 - x, y, H - 1 - y);
      if (edge < border) c = tier >= 5 ? GOLD : mix(base, WHITE, 0.45);
      else if (edge < border + line) c = mix(base, BLACK, 0.55);
      else if (tier >= 5 && Math.min(x, W - 1 - x) + Math.min(y, H - 1 - y) < px(50)) c = mix(c, GOLD, 0.55); // esquinas doradas (distancia a la esquina más cercana)

      const o = (y * W + x) * 3;
      buf[o] = c[0]; buf[o + 1] = c[1]; buf[o + 2] = c[2];
    }
  }
  return encodePng(W, H, buf);
}

fs.mkdirSync(OUT, { recursive: true });
let total = 0;
for (const [key, color] of Object.entries(BLESSINGS)) {
  for (const tier of TIERS) {
    const file = path.join(OUT, `banner_${key}_${tier}.png`);
    fs.writeFileSync(file, render(color, tier));
    total += fs.statSync(file).size;
  }
  console.log(`${key}: ${TIERS.length} tiers`);
}
console.log(`${Object.keys(BLESSINGS).length * TIERS.length} archivos de ${W}x${H}, ${(total / 1024).toFixed(0)} KB en total`);
