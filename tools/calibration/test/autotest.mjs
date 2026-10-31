// Autotest de calibracion.html con una captura sintética de geometría conocida. Uso: node autotest.mjs (requiere playwright y python3+Pillow)
import { createRequire } from 'node:module';
const { chromium } = createRequire(import.meta.url)('playwright'); // resuelve NODE_PATH (npm root -g)
import { execSync } from 'node:child_process';
import fs from 'node:fs'; import path from 'node:path'; import url from 'node:url';
const here = path.dirname(url.fileURLToPath(import.meta.url));
const png = path.join(process.env.TMPDIR || '/tmp', 'fixture.png');
execSync(`python3 ${path.join(here, 'make_fixture.py')} ${png}`);
const T = JSON.parse(fs.readFileSync(png + '.json', 'utf8'));
const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1500, height: 900 } });
const errors = []; p.on('pageerror', e => errors.push(e.message)); p.on('console', m => m.type() === 'error' && errors.push(m.text()));
await p.goto('file://' + path.join(here, '..', 'calibracion.html'));
await p.click('nav button[data-tab=medir]');
await p.setInputFiles('#file', png); await p.waitForFunction(() => window.__cal.S.img && window.__cal.S.w > 0);
await p.fill('#ppp', '2'); await p.dispatchEvent('#ppp', 'change');
let fails = 0; const ok = (name, got, want, tol) => { const pass = Math.abs(got - want) <= tol; if (!pass) fails++; console.log((pass ? 'OK  ' : 'FAIL'), name, 'obtenido', got.toFixed(2), 'esperado', want, '±', tol); };
const click = async (px, py) => { // clic en coordenadas de IMAGEN (px) con zoom 1 y origen (0,0)
  const r = await p.evaluate(() => { const s = window.__cal.S, c = document.getElementById('view').getBoundingClientRect(); return { px: s.px, py: s.py, z: s.zoom, l: c.left, t: c.top }; });
  await p.mouse.click(r.l + r.px + (px) * r.z, r.t + r.py + (py) * r.z); };
const sel = async id => p.click(`.step[data-id=${id}]`);
const val = id => p.evaluate(id => window.__cal.S.data[id] && window.__cal.S.data[id].result, id);
// alta ampliación para clics precisos
await p.evaluate(() => { const s = window.__cal.S; s.zoom = 1; s.px = 0; s.py = 0; });
await p.evaluate(() => document.getElementById('z1').click());
await p.evaluate(() => { const s = window.__cal.S; s.px = 0; s.py = 0; });

// altura de barra: borde superior (y=barT) y borde inferior (y=barT+barH-1) → el centro del píxel +0.5; la altura = barH px
await sel('bar_height'); await click(T.barL + 200, T.barT); await click(T.barL + 200, T.barT + T.barH);
let r = await val('bar_height'); ok('bar_height (pt)', r.value, T.bar_height_pt, 0.6);
await sel('bar_width_total'); await click(T.barL, T.barT + 60); await click(T.barL + T.barW, T.barT + 60);
r = await val('bar_width_total'); ok('bar_width_total (pt)', r.value, T.bar_width_pt, 0.6);
await sel('icon_size_rest'); await click(T.icons[0][0], T.icons[0][1] + 20); await click(T.icons[0][2], T.icons[0][1] + 20);
r = await val('icon_size_rest'); ok('icon_size_rest (pt)', r.value, 60, 0.6);
await sel('icon_gap_rest'); await click(T.icons[0][2], T.icons[0][1] + 20); await click(T.icons[1][0], T.icons[1][1] + 20);
r = await val('icon_gap_rest'); ok('icon_gap_rest (pt)', r.value, 6, 0.6);
await sel('bar_pad_left'); await click(T.barL, T.barT + 60); await click(T.icons[0][0], T.barT + 60);
r = await val('bar_pad_left'); ok('bar_pad_left (pt)', r.value, 10, 0.6);
// radio de esquina: puntos sobre el arco superior izquierdo (centro = barL+R, barT+R)
await sel('bar_corner_radius'); const R = 26 * 2, cx = T.barL + R, cy = T.barT + R;
for (const a of [185, 200, 215, 230, 245, 260]) { const th = a * Math.PI / 180; await click(Math.round(cx + R * Math.cos(th)), Math.round(cy + R * Math.sin(th))); }
r = await val('bar_corner_radius'); ok('bar_corner_radius (pt)', r.value, 26, 1.5);
// perfil: rampa negro→blanco 40 px
await sel('border_profile_top'); await click(T.ramp[0], 150); await click(T.ramp[2], 150);
const prof = await val('border_profile_top'); const mono = prof.value.every((v, i, a) => i === 0 || v >= a[i - 1] - 3);
console.log(mono && prof.value[0] < 20 && prof.value.at(-1) > 235 ? 'OK  ' : 'FAIL', 'perfil monótono negro→blanco, n =', prof.value.length); if (!(mono && prof.value[0] < 20 && prof.value.at(-1) > 235)) fails++;
// exportación
const exp = await p.evaluate(() => { const o = {}; for (const [id, d] of Object.entries(window.__cal.S.data)) if (d.result) o[id] = d.result; return Object.keys(o); });
console.log(exp.length >= 7 ? 'OK  ' : 'FAIL', 'medidas con resultado:', exp.join(',')); if (exp.length < 7) fails++;
// comparar
await p.click('nav button[data-tab=comparar]');
const j = JSON.stringify({ meta: { source: 'mac' }, measurements: { bar_height: { value: 80, kind: 'observable' } } });
fs.writeFileSync('/tmp/a.json', j); fs.writeFileSync('/tmp/b.json', JSON.stringify({ meta: { source: 'winmac' }, measurements: { bar_height: { value: 82, kind: 'observable' } } }));
await p.setInputFiles('#cmpA', '/tmp/a.json'); await p.setInputFiles('#cmpB', '/tmp/b.json'); await p.waitForSelector('#cmpOut table');
const txt = await p.textContent('#cmpOut'); console.log(txt.includes('+2.00') ? 'OK  ' : 'FAIL', 'comparar Δ WinMac−Mac = +2.00'); if (!txt.includes('+2.00')) fails++;
// fondos
await p.click('nav button[data-tab=fondos]'); const nb = await p.locator('#fondoBtns button').count(); console.log(nb >= 11 ? 'OK  ' : 'FAIL', 'botones de fondos:', nb); if (nb < 11) fails++;
console.log(errors.length ? 'FAIL errores de consola: ' + errors.join(' | ') : 'OK   sin errores de consola'); if (errors.length) fails++;
await p.click('nav button[data-tab=medir]'); await p.screenshot({ path: path.join(process.env.TMPDIR || '/tmp', 'calibracion-medir.png') });
await b.close(); console.log(fails ? `\n${fails} FALLOS` : '\nTODO OK'); process.exit(fails ? 1 : 0);
