#!/usr/bin/env node
// 2 エージェントのプロスペクティブ制（docs/commentary/styles/prospective.md）。
// 実況と解説の 2 つの LLM（既定 Haiku、別々の会話）が、試合の進行に合わせてマイク 1 本を取り合いながら話す。
// 試合の時計は LLM が考えている間は止まる（記録した試合の上で時計を進めるので、考える時間は動画に入らない）。
//
//   node tools/tvshow/prospective/run.js --card tools/tvshow/cards/wvc-rematch-prospective.json --bout b1 [--driver cli|relay|mock] [--model haiku] [--fresh]
//
// 書き手（--driver）:
//   cli    claude -p（席ごとに 1 つの会話を --resume で続ける）。CLI のログインが要る
//   relay  呼び出しを <work>/request-N.md に書いて終了コード 3。答えを <work>/reply-N.json に置いて、もう一度動かすと続きから進む
//   mock   決まった答え（仕組みの確かめ用。LLM は呼ばない）
// 呼び出しと答えは <work>/calls.jsonl に残り、同じ問いには同じ答えを使う（--fresh で捨てる）。だから台本は記録から再現できる。
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawnSync, spawn } = require('child_process');

const repo = path.resolve(__dirname, '..', '..', '..');
const config = JSON.parse(fs.readFileSync(path.join(__dirname, 'config.json'), 'utf8'));

function parseArgs(argv) {
  const args = { driver: 'cli', model: 'haiku', fresh: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--fresh') { args.fresh = true; continue; }
    if (!a.startsWith('--') || i + 1 >= argv.length) throw new Error(`引数が読めません: ${a}`);
    args[a.slice(2)] = argv[++i];
  }
  if (!args.card || !args.bout) throw new Error('--card <札> と --bout bN を渡してください');
  if (!['cli', 'relay', 'mock'].includes(args.driver)) throw new Error(`--driver は cli / relay / mock: ${args.driver}`);
  return args;
}

const readJson = (p) => JSON.parse(fs.readFileSync(p, 'utf8').replace(/^﻿/, ''));
const round1 = (x) => Math.round(x * 10) / 10;
const fmt = (t) => t.toFixed(1);

// ---- 話す長さ ----
// 実際に読み上げて測る（声入れと同じ Synthesize.ps1・同じ声と速さ）。字数の見積もりは実際より中央値で 1.1〜1.2 倍、長い台詞で
// 1.4 倍短く、声入れで台詞が 1〜3 秒ずつ遅れた（v1）。同じ台詞は <work>/tts.json の記録を使う。mock は見積もり × speech.margin。
function makeMeasure(work, driver) {
  const cachePath = path.join(work, 'tts.json');
  const cache = fs.existsSync(cachePath) ? readJson(cachePath) : {};
  const tmp = path.join(work, 'tts');
  return (text, seat, excited) => {
    if (driver === 'mock') return speechSeconds(text, seat, excited) * config.speech.margin;
    const key = `${seat}|${excited ? 1 : 0}|${text}`;
    if (cache[key] === undefined) {
      fs.mkdirSync(tmp, { recursive: true });
      const script = path.join(tmp, 'line.json');
      fs.writeFileSync(script, JSON.stringify({ lines: [{ at: 0, speaker: seat, text, excited }] }));
      const r = spawnSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(repo, 'tools', 'commentary', 'Synthesize.ps1'), '-Script', script, '-OutDir', tmp], { encoding: 'utf8', timeout: 120000 });
      const manifest = path.join(tmp, 'manifest.json');
      if (r.status !== 0 || !fs.existsSync(manifest)) throw new Error(`読み上げで長さを測れません: ${(r.stderr || r.stdout || '').slice(0, 300)}`);
      cache[key] = readJson(manifest).lines[0].duration;
      fs.rmSync(manifest);
      fs.writeFileSync(cachePath, JSON.stringify(cache));
    }
    return cache[key];
  };
}

// 字数からの見積もり（TvDraft.Duration と同じ式）。LLM への速さの目安と mock に使う。
function speechSeconds(text, seat, excited) {
  const s = config.speech;
  const pauses = (text.match(/[。、！？!?]/g) || []).length + (text.match(/…+/g) || []).length;
  const chars = text.replace(/[。、！？!?…]/g, '').length;
  const rate = s.rates[seat][excited ? 'excited' : 'normal'];
  return (chars * s.charSeconds + pauses * s.pauseSeconds) / (1 + rate);
}

// 話し始めから seconds 秒で言えた所まで（字の割合で切る）。
function spokenPart(text, fraction) {
  if (fraction >= 1) return text;
  return text.slice(0, Math.max(0, Math.floor(text.length * fraction)));
}

// ---- 答えの JSON を取り出す ----
function extractJson(text) {
  for (let start = text.lastIndexOf('{'); start >= 0; start = text.lastIndexOf('{', start - 1)) {
    for (let end = text.indexOf('}', start); end >= 0; end = text.indexOf('}', end + 1)) {
      try { return JSON.parse(text.slice(start, end + 1)); } catch { /* 次の閉じかっこ */ }
    }
  }
  return null;
}

function validate(kind, reply) {
  const actions = { mic: ['speak', 'pass'], 'mic-speaking': ['continue', 'speak', 'pass'], 'mic-planned': ['continue', 'speak', 'pass'], react: ['interrupt', 'reply', 'none'] }[kind];
  const fallback = kind === 'react' ? 'none' : kind === 'mic-speaking' || kind === 'mic-planned' ? 'continue' : 'pass';
  // 2 段の発話（now = 今起きていることの一言、then = 続き）は、つなげて 1 つの台詞にする（先に一言が声になる）。
  if (reply && typeof reply === 'object' && !reply.text && (reply.now || reply.then)) {
    reply.text = [reply.now, reply.then].filter((x) => typeof x === 'string' && x.trim()).map((x) => x.trim()).join(' ');
  }
  if (!reply || !actions.includes(reply.action)) return { action: fallback, invalid: true };
  if (['speak', 'interrupt', 'reply'].includes(reply.action) && (typeof reply.text !== 'string' || !reply.text.trim())) {
    return { action: fallback, invalid: true };
  }
  return reply;
}

class CacheMismatch extends Error {
  constructor(n) { super(`記録の ${n} 回目から問いが変わった`); this.n = n; }
}

// ---- 書き手 ----
const openDrivers = []; // 頭から聞き直すとき・終わるときに、起動したままの claude を閉じる

class Drivers {
  constructor(args, work) {
    this.args = args;
    this.work = work;
    this.cachePath = path.join(work, 'calls.jsonl');
    if (args.fresh && fs.existsSync(this.cachePath)) fs.renameSync(this.cachePath, this.cachePath + '.' + Date.now() + '.old');
    this.cache = fs.existsSync(this.cachePath)
      ? fs.readFileSync(this.cachePath, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)) : [];
    this.n = 0;
    this.live = false; // 記録から外れたら、以後は書き手に聞く
    this.turns = {}; // 席ごとの、いまの会話（プロセス）での問いの数
    this.procs = {}; // 席ごとに起動したままの claude
    this.system = {}; // 席ごとの指示（最初の説明）
    openDrivers.push(this);
  }

  /// 次の問いで、その席の会話を新しく始めるか（会話が turnsPerSession 回に達したら、読む量が膨らまないよう作り直す）。
  /// 記録をたどるときも同じ数え方なので、作り直す所は決まっている。
  freshNext(seat) { return (this.turns[seat] || 0) % config.turnsPerSession === 0; }

  async ask(seat, kind, prompt, mockReply) {
    const n = this.n++;
    const fresh = this.freshNext(seat);
    this.turns[seat] = (this.turns[seat] || 0) + 1;
    // 同じ指示・同じ問い・同じ席・同じ書き手とモデルなら、記録の答えを使う。
    const hash = crypto.createHash('sha256').update([this.args.driver, this.args.model, seat, this.system[seat] || '', prompt].join('\n')).digest('hex').slice(0, 16);
    const cached = this.cache[n];
    if (!this.live && cached && cached.hash === hash) return cached.reply;
    if (!this.live && cached) {
      // 問いが変わった（資料・決まりを直した）。席の会話は前の続きを含んでいて途中から分けられないので、
      // 記録を脇へ寄せて、試合の頭から聞き直す（main が受ける）。
      throw new CacheMismatch(n);
    }
    this.live = true;
    let raw;
    let session;
    if (this.args.driver === 'mock') raw = JSON.stringify(mockReply());
    else if (this.args.driver === 'relay') raw = this.relay(n, seat, prompt);
    else ({ raw, session } = await this.stream(seat, prompt, fresh));
    const reply = validate(kind, extractJson(raw || ''));
    if (reply.invalid) console.warn(`${n}: ${seat} の答えが読めません（パスとして扱う）: ${String(raw).slice(0, 200)}`);
    const record = { n, seat, kind, hash, driver: this.args.driver, model: this.args.model, session, prompt, raw, reply };
    fs.appendFileSync(this.cachePath, JSON.stringify(record) + '\n');
    this.cache[n] = record;
    return reply;
  }

  relay(n, seat, prompt) {
    const tag = `${n}-${crypto.createHash('sha256').update(prompt).digest('hex').slice(0, 8)}`; // 問いが変わったら古い答えを使わない
    const answer = path.join(this.work, `reply-${tag}.json`);
    if (fs.existsSync(answer)) return fs.readFileSync(answer, 'utf8');
    fs.writeFileSync(path.join(this.work, `request-${tag}.md`), `<!-- seat: ${seat} -->\n${prompt}`);
    console.log(`REQUEST: ${path.relative(repo, path.join(this.work, `request-${tag}.md`)).replace(/\\/g, '/')} (${seat})`);
    console.log(`REPLY:   ${path.relative(repo, answer).replace(/\\/g, '/')}`);
    process.exit(3);
  }

  // 席ごとに claude を 1 つ起動したままにして、問いを流し込む（stream-json）。呼び出しのたびに起動すると、起動 1 回が 1 セッションに
  // 数えられ（1 日 2,000 回）、毎回 Claude Code の既定の指示と CLAUDE.md と会話の全部を読み直して 1 回 8 万トークンを読んでいた。
  // ここでは指示を席の説明だけに差し替え（--system-prompt-file）、設定・MCP を読まず、turnsPerSession 回ごとに会話を作り直す。
  async stream(seat, prompt, fresh) {
    if (this.procs[seat] && (fresh || this.procs[seat].exited)) { this.procs[seat].proc.stdin.end(); delete this.procs[seat]; }
    // 会話の途中で新しく起動する（記録をたどったあと本番に入った・プロセスが終わっていた）ときは、それまでの流れを先に付ける
    // （新しい会話は何も知らない）。記録の鍵（hash）は付ける前の問いで取るので、たどり方は変わらない。
    if (!this.procs[seat] && !fresh && this.recapFor) prompt = this.recapFor(seat) + prompt;
    if (!this.procs[seat]) {
      const systemFile = path.join(this.work, `system-${seat}.md`);
      fs.writeFileSync(systemFile, this.system[seat] || '');
      const args = ['-p', '--model', this.args.model, '--input-format', 'stream-json', '--output-format', 'stream-json', '--verbose',
        '--system-prompt-file', `"${systemFile}"`, '--tools', 'Read', '--setting-sources', '""', '--strict-mcp-config'];
      const proc = spawn('claude', args, { cwd: repo, shell: true, env: { ...process.env, CLAUDECODE: '' } });
      const state = { proc, buffer: '', waiter: null, error: '' };
      proc.stdout.setEncoding('utf8');
      proc.stdout.on('data', (chunk) => {
        state.buffer += chunk;
        let i;
        while ((i = state.buffer.indexOf('\n')) >= 0) {
          const line = state.buffer.slice(0, i);
          state.buffer = state.buffer.slice(i + 1);
          let j;
          try { j = JSON.parse(line); } catch { continue; }
          if (j.type === 'result' && state.waiter) { const w = state.waiter; state.waiter = null; w(j); }
        }
      });
      proc.stderr.setEncoding('utf8');
      proc.stderr.on('data', (chunk) => { state.error += chunk; });
      const fail = (why) => { state.exited = true; if (state.waiter) { const w = state.waiter; state.waiter = null; w({ is_error: true, result: `${why}: ${state.error.slice(0, 300)}` }); } };
      proc.on('exit', () => fail('claude が終わりました'));
      proc.stdin.on('error', (e) => fail(`claude へ書けません（${e.message}）`));
      this.procs[seat] = state;
    }
    const state = this.procs[seat];
    const out = await new Promise((resolve) => {
      const timer = setTimeout(() => { if (state.waiter) { state.waiter = null; state.exited = true; resolve({ is_error: true, result: '5 分たっても答えが来ません' }); } }, 300000);
      state.waiter = (j) => { clearTimeout(timer); resolve(j); };
      state.proc.stdin.write(JSON.stringify({ type: 'user', message: { role: 'user', content: prompt } }) + '\n');
    });
    if (out.is_error) throw new Error(`claude が失敗しました: ${out.result}（ログインが切れていたら、端末で claude を開いて /login）`);
    return { raw: out.result || '', session: out.session_id };
  }

  close() {
    for (const s of Object.values(this.procs)) s.proc.stdin.end();
    this.procs = {};
  }
}

// ---- 中継 ----
async function main() {
  const args = parseArgs(process.argv.slice(2));
  const card = readJson(path.resolve(repo, args.card));
  const out = path.resolve(repo, card.out);
  const key = args.bout;
  const events = readJson(path.join(out, `${key}.events.json`));
  const work = path.join(out, 'prospective', key);
  fs.mkdirSync(work, { recursive: true });
  const rel = (p) => path.relative(repo, p).replace(/\\/g, '/');

  // 傷のアップ（wound_cam）は寄り切るまで数秒かかるので、寄り切った時刻にも画面の出来事を置く（そこで寄った画像とマイクを渡す）。
  const zoomed = events.events.filter((e) => e.type === 'wound_cam').map((e) => ({
    ...e, t: e.t + ((config.frameOffsets || {}).wound_cam ?? config.frameOffset), type: 'wound_zoom', text: e.text.replace('にズームで寄る', 'に寄り切った'),
  }));
  const all = events.events.concat(zoomed).sort((a, b) => a.t - b.t);
  const endEvent = all.find((e) => e.type === 'end');
  const length = endEvent ? endEvent.t : all[all.length - 1].t;
  const seats = Object.keys(config.seats);
  const other = (s) => seats.find((x) => x !== s);
  const sees = (s, e) => config.seats[s].sees.includes(e.for);

  // 選手の資料（未来の出来事を含む digest は渡さない）。
  const dossier = path.join(work, 'fighters.md');
  fs.writeFileSync(dossier, [
    `# ${events.event || 'WVC'} ${events.bout || key}`, '',
    `## 赤コーナー: ${events.red}`, '', String(events.redDossier || events.redProfile || '').replace(/\r/g, ''), '',
    `## 青コーナー: ${events.blue}`, '', String(events.blueDossier || events.blueProfile || '').replace(/\r/g, ''), '',
  ].join('\n'));

  // キーとなるフレーム。
  const frameDir = path.join(work, 'frames');
  fs.mkdirSync(frameDir, { recursive: true });
  const video = path.join(out, `${key}.mp4`);
  const frames = [];
  for (const e of all.filter((x) => config.frames.includes(x.type) && x.for === '実況')) {
    const t = Math.min(length - 0.1, e.t + ((config.frameOffsets || {})[e.type] ?? config.frameOffset));
    const file = path.join(frameDir, `t${t.toFixed(1).padStart(6, '0')}.jpg`);
    if (frames.some((f) => f.file === file)) continue;
    if (!fs.existsSync(file) && args.driver !== 'mock') {
      const r = spawnSync('ffmpeg', ['-v', 'error', '-ss', t.toFixed(2), '-i', video, '-frames:v', '1', '-vf', 'scale=640:-2', '-q:v', '4', '-y', file], { encoding: 'utf8' });
      if (r.status !== 0) throw new Error(`フレームを切り出せません（${t}s）: ${r.stderr}`);
    }
    frames.push({ t, file, what: e.text });
  }

  const drivers = new Drivers(args, work);
  for (const s of seats) drivers.system[s] = intro(s);
  drivers.recapFor = (s) => recap(s);
  // 撮影のときの生の記録（姿勢・当たりの中身・削れた場所。TvTelemetry）。無い動画（前の撮影）では使わない。
  const telemetryPath = path.join(out, `${key}.telemetry.json`);
  const tele = fs.existsSync(telemetryPath) ? readJson(telemetryPath) : null;
  const measure = makeMeasure(work, args.driver);
  const lines = []; // { seat, start, end, text, said, cutBy }
  const log = [];
  // 席ごとに、何をもう伝えたか。heard: 台詞ごとに、伝えた時点の「言った所」（あとで切れたら、切れたことを伝え直す）。
  const told = Object.fromEntries(seats.map((s) => [s, { time: -1, heard: new Map(), frame: 0, first: true }]));
  let T = 0;

  function update(seat) {
    // 前に聞いてから今までの、この席に見えた出来事・2 人の台詞・新しい画像。
    const k = told[seat];
    const parts = [];
    const seen = all.filter((e) => e.t > k.time && e.t <= T + 1e-6 && sees(seat, e));
    if (seen.length) parts.push('出来事:', ...seen.map((e) => `- ${fmt(e.t)}s ${e.for}${e.for === '解説' ? '（あなたにだけ見える体の中・狙い）' : ''}: ${e.text}`));
    const said = [];
    lines.forEach((l, i) => {
      if (l.start > T + 1e-6) return;
      const who = l.seat === seat ? 'あなた' : `${config.seats[l.seat].label}（${config.seats[l.seat].name}）`;
      const cut = l.ended && l.said.length < l.text.length;
      const known = k.heard.get(i);
      if (known === undefined) {
        // 初めて伝える: 話している最中なら全文（言い始めた時点で相方には内容が届く）、切れていれば切れた所まで。
        said.push(cut
          ? `- ${fmt(l.start)}s ${who}: 「${l.said}」（ここまで言ったところで${l.cutBy}。言うつもりだった全文: 「${l.text}」）`
          : `- ${fmt(l.start)}s ${who}: 「${l.text}」${l.ended ? '' : '（話している最中）'}`);
      }
      else if (cut && known === 'full') {
        said.push(`- ${fmt(l.start)}s ${who}の「${l.text}」は、「${l.said}」まで言ったところで${l.cutBy}（その先は声に出ていない）`);
      }
      else return;
      k.heard.set(i, cut ? 'cut' : l.ended ? 'done' : 'full');
    });
    if (said.length) parts.push('台詞:', ...said);
    const pics = frames.filter((f) => f.t <= T + 1e-6).slice(k.frame);
    if (pics.length) parts.push('新しい画面（見たければ Read で開ける）:', ...pics.map((f) => `- ${fmt(f.t)}s ${rel(f.file)}（${f.what}）`));
    k.time = T; k.frame = frames.filter((f) => f.t <= T + 1e-6).length;
    return parts.join('\n');
  }

  function intro(seat) {
    const me = config.seats[seat];
    const you = config.seats[other(seat)];
    return [
      `あなたは WVC（Wonderful Voxing Council）のテレビ中継の${me.label}、${me.name}です。相方は${you.label}の${you.name}。競技は Voxing。日本語の話し言葉で話します。`,
      '',
      '## 中継のしくみ',
      `- 試合の進行に合わせて、その場で話します。中継の時間は動画の ${fmt(length)} 秒で決まっていて、それが 2 人で使える時間のすべてです。`,
      '- マイクは 1 本で、同時に話せるのは 1 人。あなたにマイクが来たら、話すか、パスするかを決めます。考えている間、試合は止まっています。',
      '- 話すなら、何秒くらい話すつもりか（seconds）と、話す言葉を決めます。言葉は 2 段: 今この瞬間に画面で起きていることの一言（now）と、その続き（then、無くてもよい）。now が先に声になります。長さの決まりはありません。言葉が長ければ、そのぶん時間を使います。',
      '- マイクが来るたびに、いまの様子（試合の段階・倒れている選手・映っているカメラ）と、いまの画面の画像が届きます。',
      `- 「生の情報」も届きます: 前に聞いてから今までの当たり（誰が何の技で、どの手足で、相手の体のどこ（おでこ・目・頬・こめかみ・みぞおち…）に、どれだけの速さ・重さで当てたか、そこが何セル削れたか）と、直近 1 秒の 2 人の動き（頭と両拳の位置と動き、腰の高さ、体の傾き、相手との距離）。長さは選手の身長に合わせた人の大きさの cm。${seat === 'commentator' ? 'あなたには体の中（意識・スタミナ・臓器の打撲・これまでに失った場所）も届きます。' : '体の外から見える損傷（これまでに削れた場所）と、疲れの見え方も届きます。'}数字はそのまま言わなくてよい。情景を思い描くのに使ってください。`,
      `- 話している途中で出来事が起きると、その瞬間にマイクがあなたに戻ってきます（どこまで言えたかと、何が起きたかが届きます）。そのまま言い続けるか、言い直すか、黙るかを決めます。`,
      '- 相方が話し始めると、その言葉があなたに届きます。途中で割り込む（何秒後か）、話し終わってから返す、何もしない、から選べます。あなたが話しているときも、相方は同じように割り込めます。返すと決めてから声に出すまでに試合が進んでいたら、言う前にもう一度聞きます（そのまま言う・言い直す・やめる）。',
      `- 誰も話さないまま ${config.silenceSeconds} 秒たつと、マイクは${config.seats.announcer.label}に渡ります。`,
      `- 声の速さの目安: あなたは 1 秒に約 ${Math.round(1 / speechSeconds('あ', seat, false))} 字。`,
      '',
      '## 守ること',
      '- 起きたことを記録と違えて言わない（まだ起きていないことは知りません）。',
      '- 作中の世界の言葉で話す。VGV・ボット・物理・セル・ダメージの数値・打撲・% は言わない。',
      '',
      '## 資料（読むかどうかは自由。Read で開けます）',
      `- 資料の索引: ${me.index}`,
      `- この試合の両選手の資料: ${rel(dossier)}`,
      '',
      '## 答え方（JSON だけを返す）',
      'マイクが来たとき: {"action":"speak","seconds":4,"now":"…","then":"…","excited":false,"handTo":null} か {"action":"pass","wait":null}',
      '- excited: true で声が高く速くなる。handTo: "partner" にすると、話し終わったらマイクを相方へ渡す。wait: パスのあと、無言でマイクが戻ってくるのを何秒待つか（null なら次の出来事まで）。',
      '相方が話し始めたとき: {"action":"interrupt","after":1.5,"text":"…","excited":false} か {"action":"reply","text":"…","excited":false} か {"action":"none"}',
      '- after: 相方が話し始めてから何秒後に割り込むか。',
    ].join('\n');
  }

  // いまの画面（マイクを渡す瞬間のコマ）。要所のコマだけでは、出来事と出来事の間の今の画面が分からず、直前の出来事を
  // 言い直してワンテンポ遅れた（2026-11-17、ユーザーの指摘）。同じ時刻のコマは使い回す。
  function frameNow() {
    const t = Math.min(length - 0.1, T);
    const file = path.join(frameDir, `now_t${t.toFixed(1).padStart(6, '0')}.jpg`);
    if (!fs.existsSync(file) && args.driver !== 'mock') {
      const r = spawnSync('ffmpeg', ['-v', 'error', '-ss', t.toFixed(2), '-i', video, '-frames:v', '1', '-vf', 'scale=640:-2', '-q:v', '5', '-y', file], { encoding: 'utf8' });
      if (r.status !== 0) throw new Error(`いまの画面を切り出せません（${t}s）: ${r.stderr}`);
    }
    return file;
  }

  // いまの様子（その席に見えている記録から、今この瞬間の状態だけ。未来は入れない）: 試合の段階・倒れている選手・映っているカメラ・
  // 直近の打ち合いの密度。
  // 生の情報（撮影の記録 TvTelemetry から）: 前に聞いてから今までの当たりと骨折・切断、直近 1 秒の 2 人の動き、今の損傷と疲れ。
  // 言葉と数字を組み合わせて、言葉だけで情景をほぼ思い描けるように（2026-11-17、ユーザーの指定）。解説だけに体の中（意識・臓器）。
  const corner = (name) => (tele && name === tele.red ? 'red' : 'blue');
  // 立った時の腰の高さ: 始めの 2 秒でいちばん高い所（始めの 1 コマが中腰のこともある）。
  const standing = tele && tele.samples.length ? Object.fromEntries(['red', 'blue'].map((c) => [c, Math.max(...tele.samples.slice(0, 20).map((s) => s[c].pelvisHeight))])) : null;
  function rawNow(seat) {
    if (!tele) return '';
    const k = told[seat];
    const from = k.rawTime ?? -1;
    k.rawTime = T;
    const analyst = seat === 'commentator';
    // 実況には外から見えない臓器の削れは渡さない（実況が知るのは外から見える損傷）。
    const visible = ([w]) => analyst || !/の(脳|心臓|肺|肝臓|腸)$/.test(w);
    const out = [];
    const hits = tele.hits.filter((h) => h.t > from && h.t <= T + 1e-6);
    const breaks = tele.breaks.filter((b) => b.t > from && b.t <= T + 1e-6);
    if (hits.length || breaks.length) {
      out.push('生の情報（当たり）:');
      for (const h of hits) {
        const carvedSeen = h.carved ? Object.entries(h.carved).filter(visible) : [];
        const carved = carvedSeen.length
          ? `削れた: ${carvedSeen.map(([w, n]) => `${w} ${n} セル`).join('・')}` : '削れなし（皮膚の下に響いた）';
        let line = `- ${fmt(h.t)}s ${h.attacker}${h.technique ? `（技: ${h.technique}）` : ''}の${h.limb} → ${h.victim}の${h.region || h.zone}（${h.zone}）: 速さ ${h.speed} m/s`;
        if (analyst) line += `・運動量 ${h.momentum}${h.energy != null ? `・エネルギー ${h.energy} J` : ''}`;
        line += `。${carved}`;
        if (analyst && h.victimAfter) {
          const bruise = h.victimAfter.bruise && Object.keys(h.victimAfter.bruise).length ? `・臓器の打撲 ${Object.entries(h.victimAfter.bruise).map(([o, v]) => `${o} ${v}%`).join('・')}` : '';
          line += `。${h.victim}の意識 ${h.victimAfter.consciousness}%・スタミナ ${h.victimAfter.stamina}%${bruise}`;
        }
        out.push(line);
      }
      for (const b of breaks) out.push(`- ${fmt(b.t)}s ${b.who}の${b.part}が${b.kind}`);
    }
    const window = tele.samples.filter((s) => s.t >= T - 1 - 1e-6 && s.t <= T + 1e-6);
    if (window.length) {
      const a = window[0], b = window[window.length - 1];
      out.push(`生の情報（直近 1 秒の動き、${fmt(a.t)}〜${fmt(b.t)}s。前 = 相手の方、左右は本人の左右、高さはマットから）:`);
      out.push(`- 2 人の距離 ${b.red.distance} cm（1 秒前 ${a.red.distance} cm）`);
      for (const [name, c] of [[tele.red, 'red'], [tele.blue, 'blue']]) {
        const p0 = a[c], p1 = b[c];
        const head = p1.head;
        const move = [p1.head[0] - p0.head[0], p1.head[1] - p0.head[1], p1.head[2] - p0.head[2]];
        const words = [];
        if (Math.abs(move[0]) >= 4) words.push(`${move[0] > 0 ? '前へ' : '後ろへ'} ${Math.abs(move[0])}`);
        if (Math.abs(move[1]) >= 4) words.push(`${move[1] > 0 ? '左へ' : '右へ'} ${Math.abs(move[1])}`);
        if (Math.abs(move[2]) >= 4) words.push(`${move[2] > 0 ? '上へ' : '下へ'} ${Math.abs(move[2])}`);
        const fist = (key, label) => {
          const f = p1[key];
          const drop = head[2] - f[2];
          const height = drop < 12 ? '顔の高さ' : drop < 35 ? '胸の高さ' : '下がっている';
          const reach = Math.max(...window.map((s) => s[c][key][0])) - p0[key][0];
          return `${label}は${height}・顔の前 ${f[0] - head[0]} cm${reach >= 15 ? `（この 1 秒でいちばん前へ ${Math.round(reach)} cm 伸びた）` : ''}`;
        };
        const crouch = standing ? standing[c] - p1.pelvisHeight : 0;
        const parts = [
          `頭は腰から前 ${head[0]} cm・高さ ${head[2]} cm${words.length ? `（1 秒で ${words.join('・')} cm 動いた）` : '（ほぼ動かず）'}`,
          fist('leftFist', '左拳'), fist('rightFist', '右拳'),
          `腰の高さ ${p1.pelvisHeight} cm${crouch >= 6 ? `（立った時より ${Math.round(crouch)} cm 沈んでいる）` : ''}`,
          `前傾 ${p1.leanForward}°${Math.abs(p1.leanLeft) >= 8 ? `・${p1.leanLeft > 0 ? '左' : '右'}へ ${Math.abs(p1.leanLeft)}° 傾く` : ''}${Math.abs(p1.facing) >= 25 ? `・相手から ${Math.abs(p1.facing)}° 体が${p1.facing > 0 ? '左' : '右'}を向いている` : ''}`,
          p1.down ? '倒れている' : '',
          p1.technique ? `いまの技: ${p1.technique}` : '', p1.defense ? `守り: ${p1.defense}` : '',
          `スタミナ ${p1.stamina}%`, analyst ? `意識 ${p1.consciousness}%` : '',
        ].filter(Boolean);
        out.push(`- ${name}: ${parts.join('、')}`);
        const lastHit = [...tele.hits].reverse().find((h) => h.t <= T + 1e-6 && corner(h.victim) === c && h.victimAfter && h.victimAfter.lostTotal);
        if (lastHit && Object.keys(lastHit.victimAfter.lostTotal).length) {
          out.push(`  ${name}のこれまでに削れた所: ${Object.entries(lastHit.victimAfter.lostTotal).filter(visible).slice(0, analyst ? 8 : 5).map(([w, n]) => `${w} ${n} セル`).join('・')}`);
        }
      }
    }
    return out.join('\n');
  }

  function stateNow(seat) {
    const past = all.filter((e) => e.t <= T + 1e-6 && sees(seat, e) && e.for === '実況');
    const last = (types) => [...past].reverse().find((e) => types.includes(e.type));
    const phaseEvent = last(['round_start', 'round_end', 'ko', 'decision', 'result']);
    const phase = !phaseEvent ? '試合の前' : phaseEvent.type === 'round_start' ? phaseEvent.text.replace('開始', 'の最中') : phaseEvent.type === 'round_end' ? 'ラウンドの間の休み' : '試合の後';
    // 倒れている選手: その選手の最後の「ダウン」「立ち上がり」がダウン（倒れたまま休みや次のラウンドへ入ることもある）。
    const down = [events.red, events.blue].filter((name) => {
      const mine = [...past].reverse().find((e) => (e.type === 'knockdown' || e.type === 'get_up') && e.who === name);
      return mine?.type === 'knockdown';
    });
    const camera = last(['cut', 'camera', 'replay', 'wound_cam', 'wound_zoom', 'round_start']);
    const screen = !camera || camera.type === 'round_start' ? '中継カメラ' : camera.text;
    const hits = past.filter((e) => (e.type === 'hit' || e.type === 'weapon_hit') && e.t > T - 3).length;
    return `いまの様子: ${phase}・倒れている選手: ${down.length ? down.join('、') : 'なし'}・画面: ${screen}・直近 3 秒の打撃 ${hits} 発\nいまの画面（見たければ Read で開ける）: ${rel(frameNow())}`;
  }

  // 会話を作り直したときの、それまでの流れ（その席に見えた大きな出来事と、直近の 2 人の台詞）。
  function recap(seat) {
    const big = all.filter((e) => e.t <= T + 1e-6 && sees(seat, e) && e.for === '実況'
      && ['round_start', 'round_end', 'knockdown', 'get_up', 'fracture', 'severed', 'ko', 'decision'].includes(e.type));
    const recent = lines.filter((l) => l.start <= T + 1e-6).slice(-8)
      .map((l) => `- ${fmt(l.start)}s ${l.seat === seat ? 'あなた' : config.seats[l.seat].label}: 「${l.said || l.text}」`);
    return ['（会話が長くなったので、ここで区切り直しました。ここまでの流れ）',
      '大きな出来事:', ...big.map((e) => `- ${fmt(e.t)}s ${e.text}`),
      '直近の台詞:', ...recent].join('\n') + '\n\n';
  }

  function prompt(seat, situation, format) {
    // 席の説明（中継のしくみ・守ること・答え方）は会話の指示（system）として渡す。会話を作り直した最初の問いには、それまでの流れを付ける。
    const first = told[seat].first;
    told[seat].first = false;
    const head = !first && drivers.freshNext(seat) ? recap(seat) : '';
    const news = update(seat);
    const detail = rawNow(seat);
    return `${head}いま ${fmt(T)} 秒（残り ${fmt(length - T)} 秒）。\n${news ? news + '\n' : ''}${detail ? detail + '\n' : ''}${stateNow(seat)}\n\n${situation}\n\n${format}`;
  }

  const MIC_FORMAT = '答え: {"action":"speak",...} か {"action":"pass",...} の JSON だけ。';
  const MIC_SPEAKING_FORMAT = '答え: そのまま言い続けるなら {"action":"continue"}（残りをそのまま言う）、言い直す・別のことを言うなら {"action":"speak",...}（今の言葉はそこで切れる）、黙るなら {"action":"pass",...}。JSON だけ。';
  const MIC_PLANNED_FORMAT = '答え: 決めたとおりに言うなら {"action":"continue"}、言い直すなら {"action":"speak",...}、やめるなら {"action":"pass",...}。JSON だけ。';
  const REACT_FORMAT = '答え: {"action":"interrupt",...} か {"action":"reply",...} か {"action":"none"} の JSON だけ。';

  // ---- 状態 ----
  let cur = null; // 話している最中の台詞
  let pending = null; // 相方の割り込み・返事の予定
  let silenceAt = config.silenceSeconds; // 無言でマイクが実況へ渡る時刻（Infinity なら渡らない）
  const groups = [];
  for (const e of all.filter((x) => config.triggers.includes(x.type))) {
    const g = groups[groups.length - 1];
    if (g && e.t - g.t <= config.together) g.events.push(e); else groups.push({ t: e.t, events: [e] });
  }
  let gi = 0;

  function finish(line, at, cutBy) {
    line.end = at;
    line.said = cutBy ? spokenPart(line.text, (at - line.start) / (line.full || 1e-6)) : line.text; // 言い終えた台詞は全文（割り算の丸めで 1 字欠けた）
    line.cutBy = cutBy || '';
    line.ended = true;
    log.push(`${fmt(at)}s ${line.seat} の台詞が${cutBy ? cutBy + 'で切れた' : '終わった'}`);
  }

  // speaking: 話している最中にマイクが戻ったときの、その台詞。話し手は言い続ける（continue）こともできる。
  async function mic(seat, situation, speaking) {
    if (T >= length - config.gap) return false; // 番組の残りが無い
    const mock = () => mockMic(seat, situation);
    const r = speaking
      ? await drivers.ask(seat, 'mic-speaking', prompt(seat, situation, MIC_SPEAKING_FORMAT), mock)
      : await drivers.ask(seat, 'mic', prompt(seat, situation, MIC_FORMAT), mock);
    log.push(`${fmt(T)}s マイク → ${seat}（${situation.split('\n')[0]}）: ${r.action}${r.text ? ' 「' + r.text + '」' : ''}`);
    if (r.action === 'continue') return true;
    if (speaking) { finish(speaking, T, '出来事'); cur = null; }
    if (r.action === 'speak') { await start(seat, r); return true; }
    // パスのあとの無言: 待つ秒数を言えばその後に、言わなければ、実況のパスは次の出来事まで・解説のパスは決まりどおり 1 秒で実況へ。
    const wait = typeof r.wait === 'number' && r.wait > 0 ? Math.max(config.silenceSeconds, r.wait) : null;
    if (!cur) silenceAt = wait !== null ? T + wait : seat === 'announcer' ? Infinity : T + config.silenceSeconds;
    return false;
  }

  async function start(seat, r) {
    // 予約した秒数（seconds）は話し手の目安で、時間を使うのは言葉の長さ（長さの決まりは無い）。予約で文を途中で切ると、
    // 言い切れない文が並んだ（第1試合の最初の試し）。
    const text = r.text.trim();
    const excited = !!r.excited;
    const full = measure(text, seat, excited);
    if (!text) return;
    const line = { seat, start: T, full, end: T + full, text, said: text, excited, handTo: r.handTo === 'partner', cutBy: '' };
    lines.push(line);
    cur = line;
    pending = null;
    silenceAt = Infinity;
    // 相方へ、その言葉を伝える（割り込み・返事・何もしない）。
    const partner = other(seat);
    const situation = `${config.seats[seat].label}（${config.seats[seat].name}）が ${fmt(T)} 秒から話し始めました（約 ${fmt(full)} 秒）: 「${text}」`;
    const mock = () => mockReact(partner, line);
    const a = await drivers.ask(partner, 'react', prompt(partner, situation, REACT_FORMAT), mock);
    log.push(`${fmt(T)}s ${partner} の反応: ${a.action}${a.text ? ' 「' + a.text + '」' : ''}${a.after != null ? ' after ' + a.after : ''}`);
    if (a.action === 'interrupt') {
      const after = Math.min(Math.max(Number(a.after) || 0, 0.3), Math.max(0.3, full - 0.1));
      pending = { seat: partner, at: T + after, kind: 'interrupt', reply: a, decidedAt: T };
    }
    else if (a.action === 'reply') pending = { seat: partner, at: line.end + config.gap, kind: 'reply', reply: a, decidedAt: T };
  }

  const EPS = 1e-6;
  let guard = 0;
  for (; guard < 2000; guard++) {
    const tg = gi < groups.length ? groups[gi].t : Infinity;
    const te = cur ? cur.end : Infinity;
    const tp = pending ? pending.at : Infinity;
    const ts = !cur && !pending ? silenceAt : Infinity;
    const next = Math.min(tg, te, tp, ts);
    if (next >= length) break;
    if (te <= next + EPS && te <= tg + EPS) { // 言い終えた
      T = te;
      const done = cur;
      finish(done, te, '');
      cur = null;
      if (!pending) {
        // 渡す前に出来事が来るなら、出来事の方でマイクが動く（時計を戻さない）。
        if (done.handTo && tg > te + config.gap) { T = te + config.gap; await mic(other(done.seat), `${config.seats[done.seat].label}がマイクをあなたに渡しました。`); }
        else silenceAt = te + config.silenceSeconds;
      }
      continue;
    }
    if (tg <= next + EPS) { // 出来事
      const g = groups[gi++];
      T = Math.max(T, g.t);
      const text = (s) => g.events.filter((e) => sees(s, e)).map((e) => e.text).join(' / ');
      if (cur && sees(cur.seat, g.events[0]) || cur && g.events.some((e) => sees(cur.seat, e))) {
        const holder = cur.seat;
        const speaking = cur;
        pending = null; // 出来事が起きたので、相方の割り込み・返事の予定は取り消す
        const said = spokenPart(speaking.text, (T - speaking.start) / speaking.full);
        await mic(holder, `話している途中で出来事が起きたので、マイクがあなたに戻りました: ${text(holder)}\nあなたは「${speaking.text}」のうち「${said}」まで言ったところです。`, speaking);
      }
      else if (cur) {
        // 話している人には見えない出来事（解説だけに見える体の中など）: 見えた側に割り込む機会。
        const seat = other(cur.seat);
        const speaking = cur;
        pending = null; // 出来事が起きたので、前に決めた割り込み・返事は取り消す
        const r = await drivers.ask(seat, 'mic', prompt(seat, `相方が話している最中に、あなたにだけ見える出来事がありました: ${text(seat)}\n割り込むなら speak（相方の言葉はそこで切れます）、待つなら pass。`, MIC_FORMAT), () => ({ action: 'pass' }));
        log.push(`${fmt(T)}s ${seat} だけに見える出来事: ${r.action}`);
        if (r.action === 'speak') { finish(speaking, T, `${config.seats[seat].label}の割り込み`); cur = null; await start(seat, r); }
      }
      else {
        pending = null;
        const seat = g.events.some((e) => sees('announcer', e)) ? 'announcer' : 'commentator';
        await mic(seat, `出来事が起きました: ${text(seat)}`);
      }
      continue;
    }
    if (tp <= next + EPS) { // 相方の割り込み・返事
      const p = pending;
      pending = null;
      T = Math.max(T, p.at);
      if (cur) { finish(cur, T, `${config.seats[p.seat].label}の割り込み`); cur = null; }
      // 話し終わってから返す返事は、決めてから声に出すまでに試合が進んでいたら（その席に見える出来事があった・
      // replyFreshSeconds より古い）、言う前にもう一度聞く。古い返事が数秒遅れて流れ、映像とずれた（2026-11-17、ユーザーの指摘）。
      const since = all.filter((e) => e.t > p.decidedAt && e.t <= T + 1e-6 && sees(p.seat, e) && config.triggers.includes(e.type));
      if (p.kind === 'reply' && (since.length > 0 || T - p.decidedAt > config.replyFreshSeconds)) {
        const situation = `${fmt(p.decidedAt)} 秒に、あなたは相方が話し終わったら「${p.reply.text}」と返すと決めました。いま声に出せます（決めてから ${fmt(T - p.decidedAt)} 秒たちました）。`;
        const r = await drivers.ask(p.seat, 'mic-planned', prompt(p.seat, situation, MIC_PLANNED_FORMAT), () => ({ action: 'continue' }));
        log.push(`${fmt(T)}s ${p.seat} の返事を聞き直した: ${r.action}${r.text ? ' 「' + r.text + '」' : ''}`);
        if (r.action === 'continue') await start(p.seat, p.reply);
        else if (r.action === 'speak') await start(p.seat, r);
        else silenceAt = T + config.silenceSeconds;
        continue;
      }
      await start(p.seat, p.reply);
      continue;
    }
    T = Math.max(T, ts); // 無言
    silenceAt = Infinity;
    await mic('announcer', `${config.silenceSeconds} 秒、誰も話していません。`);
  }
  if (guard >= 2000) console.warn('ループの上限に達しました（中継が途中で止まった）');
  if (cur) finish(cur, length, '番組の終わり');

  // ---- 台本 ----
  // 途中で切れた台詞は、切れるまでの枠（次の台詞まで）にしか置けない。切れ端を単独で読み上げると読み出しの間のぶん枠より長く、
  // 声入れで次の台詞が 1〜3 秒ずつ押し出された（v2）。切れた台詞は読み上げて測り、枠に収まる所まで字を縮める。
  // 言い出してすぐ切れた切れ端（minSaid 字未満: 「意」「ディエ」）と、縮めても収まらない台詞は声に出さない（記録 transcript.md には残る）。
  function fitted(l) {
    if (!l.cutBy) return l.said;
    const slot = l.end - l.start - config.gap;
    let text = l.said;
    while (text.length >= config.minSaid && measure(text, l.seat, l.excited) > slot) text = text.slice(0, Math.floor(text.length * 0.8));
    return text;
  }
  const out_lines = lines.map((l) => ({ l, text: fitted(l) })).filter((x) => x.text.trim().length >= config.minSaid)
    .map(({ l, text }) => ({ at: round1(l.start), speaker: l.seat, text, excited: l.excited }));
  const extras = { style: 'prospective', useDraft: false, placed: true, lines: out_lines };
  fs.writeFileSync(path.join(out, `${key}.extras.json`), JSON.stringify(extras, null, 2) + '\n');
  const transcript = [
    `# ${events.bout || key} 2 エージェントのプロスペクティブ制（${args.driver}・${args.model}）`, '',
    `呼び出し ${drivers.n} 回、台詞 ${out_lines.length}（実況 ${out_lines.filter((l) => l.speaker === 'announcer').length}・解説 ${out_lines.filter((l) => l.speaker === 'commentator').length}）、話した時間 ${fmt(lines.reduce((s, l) => s + (l.end - l.start), 0))} / ${fmt(length)} 秒`, '',
    '## 台詞', '', ...lines.map((l) => `- ${fmt(l.start)}–${fmt(l.end)}s ${config.seats[l.seat].label}: 「${l.said}」${l.cutBy ? `（${l.cutBy}で切れた）` : ''}`), '',
    '## マイクの動き', '', ...log.map((x) => `- ${x}`), '',
  ].join('\n');
  fs.writeFileSync(path.join(work, 'transcript.md'), transcript);
  drivers.close();
  console.log(`${key}: 呼び出し ${drivers.n} 回・台詞 ${out_lines.length} → ${rel(path.join(out, key + '.extras.json'))}・${rel(path.join(work, 'transcript.md'))}`);
}

// ---- 決まった答え（仕組みの確かめ用） ----
function mockMic(seat, situation) {
  if (seat === 'announcer' && situation.startsWith('出来事')) {
    const what = situation.split(': ').slice(1).join(': ').split(' / ')[0];
    return { action: 'speak', seconds: 3, text: what.slice(0, 30) + '。', excited: /ダウン|ノックアウト/.test(what), handTo: /ラウンド終了/.test(what) ? 'partner' : null };
  }
  if (seat === 'commentator' && situation.includes('マイクをあなたに渡しました')) return { action: 'speak', text: 'そうですね、ここまでは互角の流れです。' };
  if (situation.includes('戻りました') && !/ダウン|ノックアウト|ラウンド/.test(situation.split('\n')[0])) return { action: 'continue' };
  if (situation.includes('戻りました')) return { action: 'speak', text: situation.split(': ').slice(1).join(': ').slice(0, 20) + '！', excited: true };
  return { action: 'pass', wait: 4 };
}

function mockReact(seat, line) {
  if (seat === 'commentator' && /ダウン/.test(line.text)) return { action: 'interrupt', after: 0.8, text: '効いていますね。' };
  if (seat === 'commentator' && /入った/.test(line.text)) return { action: 'reply', text: 'いい当たりでした。' };
  return { action: 'none' };
}

async function run() {
  try { await main(); }
  catch (e) {
    openDrivers.forEach((d) => d.close());
    if (!(e instanceof CacheMismatch)) throw e;
    // 問いが変わった: 記録を脇へ寄せて、頭から聞き直す。
    const args = parseArgs(process.argv.slice(2));
    const card = readJson(path.resolve(repo, args.card));
    const cache = path.join(path.resolve(repo, card.out), 'prospective', args.bout, 'calls.jsonl');
    fs.renameSync(cache, cache + '.' + Date.now() + '.old');
    console.warn(`${e.message}: 前の記録を脇へ寄せて、頭から聞き直します`);
    await main();
  }
}

run().then(() => process.exit(0), (e) => { console.error(e.message); process.exit(1); });
