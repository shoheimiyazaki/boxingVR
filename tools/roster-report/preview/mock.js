const parseCsv = (t) => { const L = t.trim().split(/\r?\n/); const h = L[0].split(','); const parse = (l) => { const o = []; let c = ''; let q = false; for (let i = 0; i < l.length; i++) { const ch = l[i]; if (ch === '"') { if (q && l[i + 1] === '"') { c += '"'; i++; continue; } q = !q; continue; } if (ch === ',' && !q) { o.push(c); c = ''; continue; } c += ch; } o.push(c); return o; }; const rows = L.slice(1).map((l) => Object.fromEntries(parse(l).map((v, i) => [h[i], v]))); rows.columns = h; return rows; };
const DATA = {}; for (const k in window.RAW) DATA[k] = parseCsv(window.RAW[k]);
const draws = {}; const calcs = {}; const errors = [];
window.addEventListener('error', (e) => errors.push(String(e.message)));
window.dash = {
  colors: ['#3b6fd8', '#d9822b', '#8a5cc7', '#2f9e9e', '#c74a7a', '#6b8e23', '#b8860b', '#4682b4', '#9932cc', '#cd5c5c', '#20b2aa', '#778899', '#daa520', '#5f9ea0'],
  data(id) { if (DATA[id]) return { status: 'ok', data: DATA[id] }; if (calcs[id]) { const c = calcs[id]; try { return { status: 'ok', data: c.fn(...c.inputs.map((i) => DATA[i])) }; } catch (e) { errors.push('calc ' + id + ': ' + e.message); return { status: 'error', data: null }; } } return { status: 'missing', data: [] }; },
  draw(name, fn) { if (fn) { draws[name] = fn; return; } run(name); },
  calc(id, o) { calcs[id] = o; }, onData(fn) { fn(); }, params() { return {}; }, setParams() {}, setLink() {},
};
function run(name) { const fn = draws[name]; for (const el of document.querySelectorAll('[data-draw="' + name + '"]')) { try { if (typeof fn === 'function') fn(el); else { const m = fn.mount ? fn.mount(el) : undefined; if (fn.update) fn.update(el, m); } } catch (e) { errors.push(name + ': ' + e.message); } } }
window.addEventListener('load', () => { for (const n in draws) run(n); document.title = 'ERR:' + errors.length; window.__errors = errors; });
