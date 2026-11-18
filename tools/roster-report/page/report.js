// 総当たり・考察・距離の図（profiles: 選手ごと、matchups: 組ごと）。画像は images.js の window.rpImages。
(() => {
  const num = (v) => (v === '' || v === null || v === undefined ? NaN : +v);
  const pct0 = d3.format('.0%');
  const f2 = d3.format('.2f');
  const imgs = () => window.rpImages || {};
  const profiles = () => { const p = dash.data('profiles'); return p.status === 'ok' ? [...p.data].sort((a, b) => num(b.score) - num(a.score)) : []; };
  const armedColor = (r) => (r.armed === '1' ? dash.colors[1] : dash.colors[0]);

  const pearson = (a, b) => {
    const n = a.length;
    const ma = d3.mean(a);
    const mb = d3.mean(b);
    let s = 0; let sa = 0; let sb = 0;
    for (let i = 0; i < n; i++) { s += (a[i] - ma) * (b[i] - mb); sa += (a[i] - ma) ** 2; sb += (b[i] - mb) ** 2; }
    return s / Math.sqrt(sa * sb);
  };

  dash.calc('rp_corr', {
    inputs: ['profiles'],
    title: '勝ち点率との相関',
    description: '選手ごとの値と勝ち点率のピアソンの相関（mass は素手・はめる物の選手だけ）。',
    fn: (profiles) => {
      const n = (v) => +v;
      const sc = profiles.map((r) => n(r.score));
      const fist = profiles.filter((r) => r.armed !== '1');
      const corr = (a, b) => { const ma = a.reduce((x, y) => x + y, 0) / a.length; const mb = b.reduce((x, y) => x + y, 0) / b.length; let s = 0; let sa = 0; let sb = 0; for (let i = 0; i < a.length; i++) { s += (a[i] - ma) * (b[i] - mb); sa += (a[i] - ma) ** 2; sb += (b[i] - mb) ** 2; } return Math.round((s / Math.sqrt(sa * sb)) * 100) / 100; };
      return [
        { metric: 'kd', r: corr(sc, profiles.map((r) => n(r.kd_per_bout))) },
        { metric: 'kdt', r: corr(sc, profiles.map((r) => n(r.kd_taken_per_bout))) },
        { metric: 'dmg', r: corr(sc, profiles.map((r) => n(r.dmg_per_bout))) },
        { metric: 'mass', r: corr(fist.map((r) => n(r.score)), fist.map((r) => n(r.mass))) },
      ];
    },
  });

  dash.calc('rp_weapon', {
    inputs: ['matchups', 'profiles'],
    title: '武器 対 素手の勝ち点率',
    description: '武器を持つ選手が、武器を持たない選手と戦った全試合の勝ち点率（勝ち 1・引き分け 0.5）。',
    fn: (matchups, profiles) => {
      const armed = new Set(profiles.filter((p) => p.armed === '1').map((p) => p.fighter));
      let points = 0; let bouts = 0;
      for (const m of matchups) {
        if (armed.has(m.fighter) && !armed.has(m.opponent)) { points += (+m.wins) + 0.5 * (+m.draws); bouts += +m.bouts; }
      }
      return Math.round((points / bouts) * 1000) / 1000;
    },
  });

  const markCorr = (name, metric, signed) => dash.draw(name, (el) => {
    const c = dash.data('rp_corr');
    el.setAttribute('data-source', 'rp_corr');
    el.setAttribute('data-field', 'r');
    el.setAttribute('data-where', `metric=${metric}`);
    const row = c.status === 'ok' ? c.data.find((r) => r.metric === metric) : null;
    el.textContent = row ? (signed ? d3.format('+.2f')(row.r) : f2(row.r)) : '—';
  });
  markCorr('rp-r-kd', 'kd', false);
  markCorr('rp-r-kdt', 'kdt', true);
  markCorr('rp-r-dmg', 'dmg', false);
  markCorr('rp-r-mass', 'mass', false);
  dash.draw('rp-weapon', (el) => {
    const w = dash.data('rp_weapon');
    el.setAttribute('data-source', 'rp_weapon');
    el.textContent = w.status === 'ok' && w.data !== null ? d3.format('.1%')(w.data) : '—';
  });

  dash.draw('rp-top', (el) => { const t = profiles()[0]; el.textContent = t ? `${t.fighter}（${t.name}、${t.tagline}）` : '—'; });
  dash.draw('rp-top-score', (el) => {
    const t = profiles()[0];
    el.textContent = t ? pct0(num(t.score)) : '—';
    if (t) { el.setAttribute('data-source', 'profiles'); el.setAttribute('data-field', 'score'); el.setAttribute('data-where', `fighter=${encodeURIComponent(t.fighter)}`); }
  });

  dash.draw('rp-gallery', (el) => {
    const rows = profiles();
    el.replaceChildren();
    if (!rows.length) { el.textContent = '—'; return; }
    rows.forEach((r, i) => {
      const card = document.createElement('div');
      card.className = 'rp-fighter';
      const shot = document.createElement('div');
      shot.className = 'rp-shot';
      const im = imgs()[r.fighter] || {};
      if (im.shot) { const img = document.createElement('img'); img.className = 'shot'; img.src = im.shot; img.alt = `${r.fighter} の試合の 1 コマ`; shot.append(img); }
      if (im.face) { const img = document.createElement('img'); img.className = 'face'; img.src = im.face; img.alt = `${r.fighter} の顔`; shot.append(img); }
      const rank = document.createElement('span');
      rank.className = 'rank';
      rank.textContent = `${i + 1} 位 ${pct0(num(r.score))}`;
      shot.append(rank);
      const meta = document.createElement('div');
      meta.className = 'rp-meta';
      const nm = document.createElement('div'); nm.className = 'nm'; nm.textContent = `${r.fighter}  ${r.name}`;
      const sub = document.createElement('div'); sub.className = 'sub'; sub.textContent = `${r.tagline}・${r.equipment}・腕 ${d3.format('.1f')(num(r.reach_cm))} cm`;
      const sub2 = document.createElement('div'); sub2.className = 'sub'; sub2.textContent = `${r.wins} 勝 ${r.losses} 敗 ${r.draws} 分・得意 ${r.best_from_cm}〜${r.best_to_cm} cm・いい技 ${r.top_technique}`;
      const bar = document.createElement('div'); bar.className = 'bar';
      const fill = document.createElement('i'); fill.style.width = `${num(r.score) * 100}%`; fill.style.background = armedColor(r);
      bar.append(fill);
      meta.append(nm, sub, sub2, bar);
      card.append(shot, meta);
      el.append(card);
    });
  });

  // 勝ち点率と 95% 区間（横の点と線）。
  dash.draw('rp-rank', (el) => {
    const rows = profiles();
    el.replaceChildren();
    if (!rows.length) { el.textContent = '—'; return; }
    const W = Math.max(300, el.clientWidth || 600);
    const rowH = 22;
    const m = { top: 8, right: 16, bottom: 30, left: 76 };
    const H = m.top + m.bottom + rowH * rows.length;
    const x = d3.scaleLinear().domain([0, 1]).range([m.left, W - m.right]);
    const svg = d3.create('svg').attr('viewBox', `0 0 ${W} ${H}`).attr('role', 'img').attr('aria-label', '選手ごとの勝ち点率と 95% 区間');
    el.append(svg.node());
    svg.append('g').attr('transform', `translate(0,${H - m.bottom})`).call(d3.axisBottom(x).ticks(5).tickFormat(pct0).tickSize(-(H - m.top - m.bottom)))
      .call((g) => g.selectAll('.tick line').attr('stroke', 'var(--cds-chart-grid)')).call((g) => g.select('.domain').remove());
    svg.append('line').attr('x1', x(0.5)).attr('x2', x(0.5)).attr('y1', m.top).attr('y2', H - m.bottom).style('stroke', 'var(--cds-chart-reference)').attr('stroke-dasharray', '3 3');
    rows.forEach((r, i) => {
      const y = m.top + rowH * i + rowH / 2;
      const c = armedColor(r);
      svg.append('text').attr('x', m.left - 8).attr('y', y).attr('dy', '0.35em').attr('text-anchor', 'end').style('fill', 'var(--color-fg)').text(r.fighter);
      svg.append('line').attr('x1', x(num(r.ci_low))).attr('x2', x(num(r.ci_high))).attr('y1', y).attr('y2', y).style('stroke', c).attr('stroke-width', 3).attr('stroke-linecap', 'round').attr('opacity', 0.45);
      if (r.armed === '1') svg.append('rect').attr('x', x(num(r.score)) - 5).attr('y', y - 5).attr('width', 10).attr('height', 10).style('fill', c);
      else svg.append('circle').attr('cx', x(num(r.score))).attr('cy', y).attr('r', 5).style('fill', c);
      svg.append('text').attr('x', x(num(r.ci_high)) + 6).attr('y', y).attr('dy', '0.35em').text(pct0(num(r.score)));
    });
  });

  // 組ごとの勝ち点率の表。
  dash.draw('rp-matrix', (el) => {
    el.replaceChildren();
    const f = profiles();
    const md = dash.data('matchups');
    const m = md.status === 'ok' ? md.data : [];
    if (!f.length || !m.length) { el.textContent = '—'; return; }
    const ids = f.map((r) => r.fighter);
    const by = new Map(m.map((r) => [`${r.fighter}|${r.opponent}`, r]));
    const W = Math.max(520, el.clientWidth || 640);
    const left = 70; const top = 70;
    const cell = Math.max(24, Math.floor((W - left - 8) / ids.length));
    const width = left + cell * ids.length + 8;
    const H = top + cell * ids.length + 8;
    const svg = d3.create('svg').attr('viewBox', `0 0 ${width} ${H}`).attr('role', 'img').attr('aria-label', '行の選手から見た組ごとの勝ち点率').style('max-width', `${width}px`);
    el.append(svg.node());
    const color = (s) => {
      const a = Math.min(1, Math.abs(s - 0.5) * 2);
      return `color-mix(in oklab, ${s >= 0.5 ? 'var(--color-ok)' : 'var(--cds-chart-status-critical)'} ${Math.round(a * 85)}%, var(--cds-chart-grid))`;
    };
    ids.forEach((id, i) => {
      svg.append('text').attr('x', left - 6).attr('y', top + i * cell + cell / 2).attr('dy', '0.35em').attr('text-anchor', 'end').style('fill', 'var(--color-fg)').text(id);
      svg.append('text').attr('transform', `translate(${left + i * cell + cell / 2},${top - 6}) rotate(-55)`).style('fill', 'var(--color-fg)').text(id);
    });
    const cells = [];
    ids.forEach((a, i) => ids.forEach((b, j) => { if (a !== b && by.get(`${a}|${b}`)) cells.push({ i, j, r: by.get(`${a}|${b}`) }); }));
    svg.append('g').selectAll('rect').data(cells).join('rect')
      .attr('x', (d) => left + d.j * cell + 1).attr('y', (d) => top + d.i * cell + 1).attr('width', cell - 2).attr('height', cell - 2).attr('rx', 3)
      .style('fill', (d) => color(num(d.r.score)));
    svg.append('g').selectAll('text').data(cells).join('text')
      .attr('x', (d) => left + d.j * cell + cell / 2).attr('y', (d) => top + d.i * cell + cell / 2).attr('dy', '0.35em').attr('text-anchor', 'middle')
      .style('fill', 'var(--color-fg)').style('font-size', '10px')
      .text((d) => `${Math.round(num(d.r.score) * 100)}${d.r.verdict !== 'even' ? '●' : ''}`);
    ids.forEach((id, i) => svg.append('rect').attr('x', left + i * cell + 1).attr('y', top + i * cell + 1).attr('width', cell - 2).attr('height', cell - 2).style('fill', 'var(--color-border-line)'));
    const tip = document.createElement('div');
    tip.className = 'tip';
    tip.hidden = true;
    el.append(tip);
    const hover = (event) => {
      const [px, py] = d3.pointer(event, svg.node());
      const j = Math.floor((px - left) / cell);
      const i = Math.floor((py - top) / cell);
      const d = cells.find((c) => c.i === i && c.j === j);
      if (!d) { tip.hidden = true; return; }
      const r = d.r;
      tip.replaceChildren();
      [`${r.fighter} 対 ${r.opponent}`, `${r.wins} 勝 ${r.losses} 敗 ${r.draws} 分（${r.bouts} 試合）`,
        `勝ち点率 ${pct0(num(r.score))}（95% 区間 ${pct0(num(r.ci_low))}〜${pct0(num(r.ci_high))}）`,
        `ダウン / 試合 取った ${r.knockdowns_per_bout}・取られた ${r.knockdowns_taken_per_bout}`]
        .forEach((line, k) => { const div = document.createElement('div'); div.textContent = line; if (k === 0) div.style.fontWeight = '600'; tip.append(div); });
      tip.hidden = false;
      const s = (svg.node().getBoundingClientRect().width || width) / width;
      const tw = tip.offsetWidth;
      tip.style.left = `${Math.min(Math.max(0, (left + j * cell + cell / 2) * s - tw / 2), Math.max(0, el.scrollWidth - tw))}px`;
      tip.style.top = `${Math.max(0, (top + i * cell) * s - tip.offsetHeight - 6)}px`;
    };
    svg.on('pointermove', hover).on('pointerdown', hover).on('pointerleave', () => { tip.hidden = true; });
  });

  // 散布図: 点は選手の顔の肖像（無ければ丸）。
  const scatter = (name, field, label, fmt, onlyFist) => dash.draw(name, (el) => {
    el.replaceChildren();
    let rows = profiles();
    if (onlyFist) rows = rows.filter((r) => r.armed !== '1');
    if (!rows.length) { el.textContent = '—'; return; }
    const W = Math.max(280, el.clientWidth || 360);
    const H = 260;
    const m = { top: 12, right: 56, bottom: 36, left: 44 };
    const x = d3.scaleLinear().domain(d3.extent(rows, (r) => num(r[field]))).nice().range([m.left, W - m.right]);
    const y = d3.scaleLinear().domain([0, 1]).range([H - m.bottom, m.top]);
    const svg = d3.create('svg').attr('viewBox', `0 0 ${W} ${H}`).attr('role', 'img').attr('aria-label', `${label}と勝ち点率`);
    el.append(svg.node());
    svg.append('g').attr('transform', `translate(${m.left},0)`).call(d3.axisLeft(y).ticks(5).tickFormat(pct0).tickSize(-(W - m.left - m.right)))
      .call((g) => g.selectAll('.tick line').attr('stroke', 'var(--cds-chart-grid)')).call((g) => g.select('.domain').remove());
    svg.append('g').attr('transform', `translate(0,${H - m.bottom})`).call(d3.axisBottom(x).ticks(5).tickFormat(fmt));
    svg.append('text').attr('x', W - m.right).attr('y', H - 4).attr('text-anchor', 'end').text(label);
    // 最小二乗の直線（傾向だけ）。
    const xs = rows.map((r) => num(r[field]));
    const ys = rows.map((r) => num(r.score));
    const mx = d3.mean(xs); const my = d3.mean(ys);
    const b = d3.sum(xs.map((v, i) => (v - mx) * (ys[i] - my))) / d3.sum(xs.map((v) => (v - mx) ** 2));
    const [x0, x1] = x.domain();
    svg.append('line').attr('x1', x(x0)).attr('x2', x(x1)).attr('y1', y(my + b * (x0 - mx))).attr('y2', y(my + b * (x1 - mx)))
      .style('stroke', 'var(--cds-chart-reference)').attr('stroke-dasharray', '4 3');
    svg.append('text').attr('x', m.left + 4).attr('y', m.top + 10).text(`r = ${f2(pearson(xs, ys))}（${rows.length} 人）`);
    for (const r of rows) {
      const cx = x(num(r[field])); const cy = y(num(r.score));
      const face = imgs()[r.fighter]?.face;
      if (face) svg.append('image').attr('href', face).attr('x', cx - 7).attr('y', cy - 10).attr('width', 14).attr('height', 19).style('image-rendering', 'pixelated');
      else svg.append('circle').attr('cx', cx).attr('cy', cy).attr('r', 5).style('fill', armedColor(r));
      svg.append('text').attr('x', cx + 9).attr('y', cy).attr('dy', '0.35em').style('font-size', '10px').style('fill', armedColor(r)).text(r.fighter);
    }
  });
  scatter('rp-scatter-kd', 'kd_per_bout', 'ダウンを取った数 / 試合', d3.format('.1f'), false);
  scatter('rp-scatter-dmg', 'dmg_per_bout', '与えたダメージの点 / 試合', d3.format('.0f'), false);
  scatter('rp-scatter-mass', 'mass', '体の重さ（素手・はめる物の選手）', d3.format('.1f'), true);

  // 得意・不得意の距離と、いつもいる距離（選手ごとに 1 行）。
  dash.draw('rp-dumbbell', (el) => {
    const rows = profiles();
    el.replaceChildren();
    if (!rows.length) { el.textContent = '—'; return; }
    const W = Math.max(320, el.clientWidth || 640);
    const rowH = 26;
    const m = { top: 8, right: 16, bottom: 32, left: 90 };
    const H = m.top + m.bottom + rowH * rows.length;
    const lo = d3.min(rows, (r) => Math.min(num(r.best_from_cm), num(r.worst_from_cm), num(r.median_cm), num(r.reach_cm))) - 2;
    const hi = d3.max(rows, (r) => Math.max(num(r.best_to_cm), num(r.worst_to_cm), num(r.median_cm), num(r.reach_cm))) + 2;
    const x = d3.scaleLinear().domain([lo, hi]).nice().range([m.left, W - m.right]);
    const svg = d3.create('svg').attr('viewBox', `0 0 ${W} ${H}`).attr('role', 'img').attr('aria-label', '選手ごとの得意な距離・不得意な距離・いつもいる距離');
    el.append(svg.node());
    svg.append('g').attr('transform', `translate(0,${H - m.bottom})`).call(d3.axisBottom(x).ticks(Math.max(4, Math.floor(W / 70))).tickSize(-(H - m.top - m.bottom)))
      .call((g) => g.selectAll('.tick line').attr('stroke', 'var(--cds-chart-grid)')).call((g) => g.select('.domain').remove());
    svg.append('text').attr('x', W - m.right).attr('y', H - 4).attr('text-anchor', 'end').text('胸と胸の距離 [cm]');
    const tip = document.createElement('div');
    tip.className = 'tip';
    tip.hidden = true;
    rows.forEach((r, i) => {
      const y = m.top + rowH * i + rowH / 2;
      const face = imgs()[r.fighter]?.face;
      if (face) svg.append('image').attr('href', face).attr('x', 4).attr('y', y - 10).attr('width', 14).attr('height', 19).style('image-rendering', 'pixelated');
      svg.append('text').attr('x', m.left - 8).attr('y', y).attr('dy', '0.35em').attr('text-anchor', 'end').style('fill', armedColor(r)).text(r.fighter);
      svg.append('rect').attr('x', x(num(r.worst_from_cm))).attr('width', x(num(r.worst_to_cm)) - x(num(r.worst_from_cm))).attr('y', y - 7).attr('height', 14).attr('rx', 3).style('fill', 'var(--cds-chart-status-critical)').attr('opacity', 0.8);
      svg.append('rect').attr('x', x(num(r.best_from_cm))).attr('width', x(num(r.best_to_cm)) - x(num(r.best_from_cm))).attr('y', y - 7).attr('height', 14).attr('rx', 3).style('fill', 'var(--color-ok)');
      const bestMid = (num(r.best_from_cm) + num(r.best_to_cm)) / 2;
      svg.append('line').attr('x1', x(num(r.median_cm))).attr('x2', x(bestMid)).attr('y1', y).attr('y2', y).style('stroke', 'var(--cds-chart-muted)').attr('stroke-dasharray', '2 2');
      svg.append('circle').attr('cx', x(num(r.median_cm))).attr('cy', y).attr('r', 5).style('fill', 'var(--cds-chart-muted)').style('stroke', 'var(--color-bg)');
      svg.append('line').attr('x1', x(num(r.reach_cm))).attr('x2', x(num(r.reach_cm))).attr('y1', y - 10).attr('y2', y + 10).style('stroke', 'var(--color-fg)').attr('stroke-width', 1.5);
      svg.append('rect').attr('x', m.left).attr('width', W - m.left - m.right).attr('y', y - rowH / 2).attr('height', rowH).style('fill', 'transparent')
        .on('pointerenter pointerdown', () => {
          tip.replaceChildren();
          [`${r.fighter}（腕 ${r.reach_cm} cm）`,
            `得意 ${r.best_from_cm}〜${r.best_to_cm} cm（差し引き ${d3.format('+.1f')(num(r.best_net_per_s))} / 秒）`,
            `不得意 ${r.worst_from_cm}〜${r.worst_to_cm} cm（${d3.format('+.1f')(num(r.worst_net_per_s))} / 秒）`,
            `いつもいる距離 ${r.median_cm} cm`]
            .forEach((line, k) => { const div = document.createElement('div'); div.textContent = line; if (k === 0) div.style.fontWeight = '600'; tip.append(div); });
          tip.hidden = false;
          const s = (svg.node().getBoundingClientRect().width || W) / W;
          tip.style.left = `${Math.max(0, m.left * s)}px`;
          tip.style.top = `${Math.max(0, (y - rowH) * s - tip.offsetHeight)}px`;
        })
        .on('pointerleave', () => { tip.hidden = true; });
    });
    el.append(tip);
  });
})();
