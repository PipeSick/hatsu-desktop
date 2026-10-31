// Aplica puntos YA decididos (JSON) a una captura mediante calibracion.html y exporta el JSON de medidas con el mismo código que la herramienta.
// Sirve para obtener medidas REPRODUCIBLES: mismos puntos + misma imagen = mismo resultado.
// Uso: node aplicar_puntos.mjs <captura.png> <puntos.json> <salida.json> [pxPorPunto=1] [origen=mac] [escena=reposo]
//   puntos.json: { "icon_size_rest": [[x1,y1],[x2,y2]], ... }  (coordenadas en píxeles de la imagen)
import { createRequire } from 'node:module';
const { chromium } = createRequire(import.meta.url)('playwright');
import fs from 'node:fs'; import path from 'node:path'; import url from 'node:url';
const here = path.dirname(url.fileURLToPath(import.meta.url));
const [img, pts, out, ppp = '1', src = 'mac', scene = 'reposo'] = process.argv.slice(2);
if (!img || !pts || !out) { console.error('uso: node aplicar_puntos.mjs captura.png puntos.json salida.json [ppp] [origen] [escena]'); process.exit(2); }
const points = JSON.parse(fs.readFileSync(pts, 'utf8'));
const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1500, height: 900 }, acceptDownloads: true });
const errors = []; p.on('pageerror', e => errors.push(e.message));
await p.goto('file://' + path.join(here, '..', 'calibracion.html'));
await p.click('nav button[data-tab=medir]');
await p.setInputFiles('#file', img); await p.waitForFunction(() => window.__cal.S.img && window.__cal.S.w > 0);
await p.fill('#ppp', ppp); await p.dispatchEvent('#ppp', 'change'); await p.selectOption('#src', src); await p.fill('#scene', scene);
const res = await p.evaluate(pts => { const c = window.__cal, o = {}; for (const [id, P] of Object.entries(pts)) { if (id.startsWith('_')) continue; c.S.data[id] = { points: P.map(q => [q[0], q[1]]) }; c.refresh(id); o[id] = c.S.data[id].result || null; } return o; }, points);
const [dl] = await Promise.all([p.waitForEvent('download'), p.click('#export')]);
await dl.saveAs(out);
for (const [id, r] of Object.entries(res)) console.log(id.padEnd(26), r ? (typeof r.value === 'number' ? r.value.toFixed(2) + ' ' + r.unit + (r.rms !== undefined ? ` (rms ${r.rms.toFixed(2)})` : '') : Array.isArray(r.value) ? `${r.value.length} valores` : '') : 'sin resultado');
if (errors.length) { console.error('ERRORES:', errors.join(' | ')); process.exit(1); }
await b.close(); console.log('->', out);
