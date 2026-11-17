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
const { spawnSync } = require('child_process');

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
  const actions = { mic: ['speak', 'pass'], 'mic-speaking': ['continue', 'speak', 'pass'], react: ['interrupt', 'reply', 'none'] }[kind];
  const fallback = kind === 'react' ? 'none' : kind === 'mic-speaking' ? 'continue' : 'pass';
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
    this.sessions = {}; // 記録をたどった所までの、席ごとの会話
  }

  ask(seat, kind, prompt, mockReply) {
    const n = this.n++;
    // 同じ問い・同じ席・同じ書き手とモデルなら、記録の答えを使う。
    const hash = crypto.createHash('sha256').update([this.args.driver, this.args.model, seat, prompt].join('\n')).digest('hex').slice(0, 16);
    const cached = this.cache[n];
    if (!this.live && cached && cached.hash === hash) {
      if (cached.session) this.sessions[seat] = cached.session;
      return cached.reply;
    }
    if (!this.live && cached) {
      // 問いが変わった（資料・決まりを直した）。席の会話（claude のセッション）は前の続きの台詞まで含んでいて途中から分けられないので、
      // 記録を脇へ寄せて、試合の頭から聞き直す（main が受ける）。
      throw new CacheMismatch(n);
    }
    this.live = true;
    let raw;
    let session = this.sessions[seat];
    if (this.args.driver === 'mock') raw = JSON.stringify(mockReply());
    else if (this.args.driver === 'relay') raw = this.relay(n, seat, prompt);
    else ({ raw, session } = this.cli(seat, prompt));
    const reply = validate(kind, extractJson(raw || ''));
    if (reply.invalid) console.warn(`${n}: ${seat} の答えが読めません（パスとして扱う）: ${String(raw).slice(0, 200)}`);
    const record = { n, seat, kind, hash, driver: this.args.driver, model: this.args.model, session, prompt, raw, reply };
    fs.appendFileSync(this.cachePath, JSON.stringify(record) + '\n');
    this.cache[n] = record;
    if (session) this.sessions[seat] = session;
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

  cli(seat, prompt) {
    const previous = this.sessions[seat];
    const args = ['-p', '--model', this.args.model, '--output-format', 'json', '--allowedTools', 'Read', 'Glob', 'Grep'];
    if (previous) args.push('--resume', previous);
    const run = spawnSync('claude', args, { cwd: repo, input: prompt, encoding: 'utf8', shell: true, timeout: 600000, maxBuffer: 64 * 1024 * 1024, env: { ...process.env, CLAUDECODE: '' } });
    if (run.error) throw new Error(`claude を起動できません: ${run.error.message}`);
    let out;
    try { out = JSON.parse(run.stdout); } catch { throw new Error(`claude の出力が読めません: ${(run.stdout || run.stderr || '').slice(0, 400)}`); }
    if (out.is_error) throw new Error(`claude が失敗しました: ${out.result}（ログインが切れていたら、端末で claude を開いて /login）`);
    return { raw: out.result || '', session: out.session_id || previous };
  }
}

// ---- 中継 ----
function main() {
  const args = parseArgs(process.argv.slice(2));
  const card = readJson(path.resolve(repo, args.card));
  const out = path.resolve(repo, card.out);
  const key = args.bout;
  const events = readJson(path.join(out, `${key}.events.json`));
  const work = path.join(out, 'prospective', key);
  fs.mkdirSync(work, { recursive: true });
  const rel = (p) => path.relative(repo, p).replace(/\\/g, '/');

  const all = events.events.slice().sort((a, b) => a.t - b.t);
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
    const t = Math.min(length - 0.1, e.t + config.frameOffset);
    const file = path.join(frameDir, `t${t.toFixed(1).padStart(6, '0')}.jpg`);
    if (frames.some((f) => f.file === file)) continue;
    if (!fs.existsSync(file) && args.driver !== 'mock') {
      const r = spawnSync('ffmpeg', ['-v', 'error', '-ss', t.toFixed(2), '-i', video, '-frames:v', '1', '-vf', 'scale=640:-2', '-q:v', '4', '-y', file], { encoding: 'utf8' });
      if (r.status !== 0) throw new Error(`フレームを切り出せません（${t}s）: ${r.stderr}`);
    }
    frames.push({ t, file, what: e.text });
  }

  const drivers = new Drivers(args, work);
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
      '- 話すなら、何秒くらい話すつもりか（seconds）と、話す言葉（text）を決めます。長さの決まりはありません。言葉が長ければ、そのぶん時間を使います。',
      `- 話している途中で出来事が起きると、その瞬間にマイクがあなたに戻ってきます（どこまで言えたかと、何が起きたかが届きます）。そのまま言い続けるか、言い直すか、黙るかを決めます。`,
      '- 相方が話し始めると、その言葉があなたに届きます。途中で割り込む（何秒後か）、話し終わってから返す、何もしない、から選べます。あなたが話しているときも、相方は同じように割り込めます。',
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
      'マイクが来たとき: {"action":"speak","seconds":4,"text":"…","excited":false,"handTo":null} か {"action":"pass","wait":null}',
      '- excited: true で声が高く速くなる。handTo: "partner" にすると、話し終わったらマイクを相方へ渡す。wait: パスのあと、無言でマイクが戻ってくるのを何秒待つか（null なら次の出来事まで）。',
      '相方が話し始めたとき: {"action":"interrupt","after":1.5,"text":"…","excited":false} か {"action":"reply","text":"…","excited":false} か {"action":"none"}',
      '- after: 相方が話し始めてから何秒後に割り込むか。',
    ].join('\n');
  }

  function prompt(seat, situation, format) {
    const head = told[seat].first ? intro(seat) + '\n\n---\n\n' : '';
    told[seat].first = false;
    const news = update(seat);
    return `${head}いま ${fmt(T)} 秒（残り ${fmt(length - T)} 秒）。\n${news ? news + '\n' : ''}\n${situation}\n\n${format}`;
  }

  const MIC_FORMAT = '答え: {"action":"speak",...} か {"action":"pass",...} の JSON だけ。';
  const MIC_SPEAKING_FORMAT = '答え: そのまま言い続けるなら {"action":"continue"}（残りをそのまま言う）、言い直す・別のことを言うなら {"action":"speak",...}（今の言葉はそこで切れる）、黙るなら {"action":"pass",...}。JSON だけ。';
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
    line.said = spokenPart(line.text, (at - line.start) / (line.full || 1e-6));
    line.cutBy = cutBy || '';
    line.ended = true;
    log.push(`${fmt(at)}s ${line.seat} の台詞が${cutBy ? cutBy + 'で切れた' : '終わった'}`);
  }

  // speaking: 話している最中にマイクが戻ったときの、その台詞。話し手は言い続ける（continue）こともできる。
  function mic(seat, situation, speaking) {
    if (T >= length - config.gap) return false; // 番組の残りが無い
    const mock = () => mockMic(seat, situation);
    const r = speaking
      ? drivers.ask(seat, 'mic-speaking', prompt(seat, situation, MIC_SPEAKING_FORMAT), mock)
      : drivers.ask(seat, 'mic', prompt(seat, situation, MIC_FORMAT), mock);
    log.push(`${fmt(T)}s マイク → ${seat}（${situation.split('\n')[0]}）: ${r.action}${r.text ? ' 「' + r.text + '」' : ''}`);
    if (r.action === 'continue') return true;
    if (speaking) { finish(speaking, T, '出来事'); cur = null; }
    if (r.action === 'speak') { start(seat, r); return true; }
    // パスのあとの無言: 待つ秒数を言えばその後に、言わなければ、実況のパスは次の出来事まで・解説のパスは決まりどおり 1 秒で実況へ。
    const wait = typeof r.wait === 'number' && r.wait > 0 ? Math.max(config.silenceSeconds, r.wait) : null;
    if (!cur) silenceAt = wait !== null ? T + wait : seat === 'announcer' ? Infinity : T + config.silenceSeconds;
    return false;
  }

  function start(seat, r) {
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
    const a = drivers.ask(partner, 'react', prompt(partner, situation, REACT_FORMAT), mock);
    log.push(`${fmt(T)}s ${partner} の反応: ${a.action}${a.text ? ' 「' + a.text + '」' : ''}${a.after != null ? ' after ' + a.after : ''}`);
    if (a.action === 'interrupt') {
      const after = Math.min(Math.max(Number(a.after) || 0, 0.3), Math.max(0.3, full - 0.1));
      pending = { seat: partner, at: T + after, kind: 'interrupt', reply: a };
    }
    else if (a.action === 'reply') pending = { seat: partner, at: line.end + config.gap, kind: 'reply', reply: a };
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
        if (done.handTo && tg > te + config.gap) { T = te + config.gap; mic(other(done.seat), `${config.seats[done.seat].label}がマイクをあなたに渡しました。`); }
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
        mic(holder, `話している途中で出来事が起きたので、マイクがあなたに戻りました: ${text(holder)}\nあなたは「${speaking.text}」のうち「${said}」まで言ったところです。`, speaking);
      }
      else if (cur) {
        // 話している人には見えない出来事（解説だけに見える体の中など）: 見えた側に割り込む機会。
        const seat = other(cur.seat);
        const speaking = cur;
        pending = null; // 出来事が起きたので、前に決めた割り込み・返事は取り消す
        const r = drivers.ask(seat, 'mic', prompt(seat, `相方が話している最中に、あなたにだけ見える出来事がありました: ${text(seat)}\n割り込むなら speak（相方の言葉はそこで切れます）、待つなら pass。`, MIC_FORMAT), () => ({ action: 'pass' }));
        log.push(`${fmt(T)}s ${seat} だけに見える出来事: ${r.action}`);
        if (r.action === 'speak') { finish(speaking, T, `${config.seats[seat].label}の割り込み`); cur = null; start(seat, r); }
      }
      else {
        pending = null;
        const seat = g.events.some((e) => sees('announcer', e)) ? 'announcer' : 'commentator';
        mic(seat, `出来事が起きました: ${text(seat)}`);
      }
      continue;
    }
    if (tp <= next + EPS) { // 相方の割り込み・返事
      const p = pending;
      pending = null;
      T = Math.max(T, p.at);
      if (cur) { finish(cur, T, `${config.seats[p.seat].label}の割り込み`); cur = null; }
      start(p.seat, p.reply);
      continue;
    }
    T = Math.max(T, ts); // 無言
    silenceAt = Infinity;
    mic('announcer', `${config.silenceSeconds} 秒、誰も話していません。`);
  }
  if (guard >= 2000) console.warn('ループの上限に達しました（中継が途中で止まった）');
  if (cur) finish(cur, length, '番組の終わり');

  // ---- 台本 ----
  // 言い出してすぐ切れた切れ端（minSaid 字未満: 「意」「ディエ」）は声に出さない（記録 transcript.md には残る）。
  const out_lines = lines.filter((l) => l.said.trim().length >= config.minSaid).map((l) => ({ at: round1(l.start), speaker: l.seat, text: l.said, excited: l.excited }));
  const extras = { style: 'prospective', useDraft: false, placed: true, lines: out_lines };
  fs.writeFileSync(path.join(out, `${key}.extras.json`), JSON.stringify(extras, null, 2) + '\n');
  const transcript = [
    `# ${events.bout || key} 2 エージェントのプロスペクティブ制（${args.driver}・${args.model}）`, '',
    `呼び出し ${drivers.n} 回、台詞 ${out_lines.length}（実況 ${out_lines.filter((l) => l.speaker === 'announcer').length}・解説 ${out_lines.filter((l) => l.speaker === 'commentator').length}）、話した時間 ${fmt(lines.reduce((s, l) => s + (l.end - l.start), 0))} / ${fmt(length)} 秒`, '',
    '## 台詞', '', ...lines.map((l) => `- ${fmt(l.start)}–${fmt(l.end)}s ${config.seats[l.seat].label}: 「${l.said}」${l.cutBy ? `（${l.cutBy}で切れた）` : ''}`), '',
    '## マイクの動き', '', ...log.map((x) => `- ${x}`), '',
  ].join('\n');
  fs.writeFileSync(path.join(work, 'transcript.md'), transcript);
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

function run() {
  try { main(); }
  catch (e) {
    if (!(e instanceof CacheMismatch)) throw e;
    // 問いが変わった: 記録を脇へ寄せて、頭から聞き直す。
    const args = parseArgs(process.argv.slice(2));
    const card = readJson(path.resolve(repo, args.card));
    const cache = path.join(path.resolve(repo, card.out), 'prospective', args.bout, 'calls.jsonl');
    fs.renameSync(cache, cache + '.' + Date.now() + '.old');
    console.warn(`${e.message}: 前の記録を脇へ寄せて、頭から聞き直します`);
    main();
  }
}

try { run(); } catch (e) { console.error(e.message); process.exit(1); }
