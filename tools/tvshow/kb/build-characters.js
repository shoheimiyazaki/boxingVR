// roster/*.json から docs/commentary/kb/characters/<id>.md と index.md を作る。
// 使い方: node tools/tvshow/kb/build-characters.js
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..', '..', '..');
const rosterDir = path.join(root, 'games', 'MiniRing', 'Characters', 'roster');
const outDir = path.join(root, 'docs', 'commentary', 'kb', 'characters');
fs.mkdirSync(outDir, { recursive: true });

const WEAPON = {
  staff: '六尺棒（長い木の棒。両手で持ち、両端で打つ・突く）',
  ironSword: '鉄の片手剣（右手）',
  katana: '日本刀（鉄の刃）',
  longsword: '長剣（鉄の両刃の剣）',
  club: '木の棍棒（刃のない打撃武器）',
};
const WEAR = {
  none: '素手（武器・はめ物なし）',
  glove: 'ボクシンググローブ',
  knuckles: '素手に近い拳の保護（ナックル）',
  shield: '左手の円盾',
  gauntlet: '鉄の籠手',
  tonfa: '両手のトンファー（木の棒）',
  katar: 'カタール（拳に握る短い刃）',
};
const TEXTBOOK = {
  champion: '王道のボクシング（バランス型）', karate: '空手', jabber: 'ジャブ中心の拳闘',
  taekwondo: 'テコンドー', slugger: '強打で打ち合う型', boxer: 'ボクサー型（距離を取って組み立てる）',
  outboxer: '距離を保つアウトボクサー型', swarmer: '前へ出続けるスウォーマー型',
  tonfa: 'トンファー術', counter: 'カウンターパンチャー型',
};

const list = (a) => (a && a.length ? a.map((x) => `- ${x}`).join('\n') : '- （記載なし）');
const row = (k, v) => (v === undefined || v === null || v === '' ? '' : `| ${k} | ${v} |\n`);

const chars = fs.readdirSync(rosterDir).filter((f) => f.endsWith('.json'))
  .map((f) => JSON.parse(fs.readFileSync(path.join(rosterDir, f), 'utf8')))
  .sort((a, b) => (a.order ?? 99) - (b.order ?? 99));

const idx = [];
for (const c of chars) {
  const p = c.profile || {};
  const b = c.body || {};
  const bd = b.build || {};
  const r = p.fightRecord;
  const wear = WEAR[b.wear] || b.wear;
  const weapon = b.weapon ? (WEAPON[b.weapon] || b.weapon) : '（持たない）';
  const tb = TEXTBOOK[c.textbook] || c.textbook;
  const disc = p.fightingStyle ? '' : '';
  let md = `# ${c.name}（${c.reading || ''}）\n\n`;
  md += `> ${c.tagline || ''}\n\n`;
  md += `## 基本プロフィール\n\n| 項目 | 内容 |\n|---|---|\n`;
  md += row('ID', c.id) + row('名前', c.name) + row('読み', c.reading) + row('画面表示名', c.hudName);
  md += row('出身国・地域', c.origin) + row('通称・二つ名', c.tagline);
  md += row('年齢', p.age !== undefined ? `${p.age}歳` : '') + row('身長', p.heightCm ? `${p.heightCm} cm` : '') + row('体重', p.weightKg ? `${p.weightKg} kg` : '');
  md += row('生まれ', p.birthplace) + row('地元', p.hometown) + row('構え', p.stance);
  if (r) md += row('戦績', `${r.wins}勝 ${r.losses}敗 ${r.draws}分（うちKO勝ち ${r.kos}）`);
  md += row('トレーナー', p.trainer);
  md += row('リングネームの由来', p.nicknameStory);
  md += row('決め台詞', p.quote ? `「${p.quote}」` : '');
  md += `\n## 体格・装備\n\n| 項目 | 内容 |\n|---|---|\n`;
  md += row('手のはめ物・防具', wear) + row('武器', weapon);
  const rel = (v) => (v === 1 ? '標準' : v > 1 ? `標準より大きい（${v}倍）` : `標準より小さい（${v}倍）`);
  if (bd.height !== undefined) md += row('体の大きさ（標準比）', `身長 ${rel(bd.height)} / 腕の長さ ${rel(bd.arms)} / 脚の長さ ${rel(bd.legs)} / 筋肉の太さ ${rel(bd.muscle)}`);
  md += `\n## 戦い方\n\n${p.fightingStyle || '（記載なし）'}\n\n### 強み\n\n${list(p.strengths)}\n\n### 弱み\n\n${list(p.weaknesses)}\n\n`;
  md += `## 人となり\n\n- 性格: ${p.personality || '（記載なし）'}\n- 家族: ${p.family || '（記載なし）'}\n- 趣味:\n${(p.hobbies || []).map((x) => `  - ${x}`).join('\n') || '  - （記載なし）'}\n\n`;
  md += `## 経歴の年表\n\n${list(p.career)}\n\n## エピソード\n\n${list(p.episodes)}\n\n`;
  md += `## 実況で拾える小ネタ\n\n${list(c.commentaryNotes)}\n`;
  fs.writeFileSync(path.join(outDir, `${c.id}.md`), md);
  const shortDesc = (p.fightingStyle || '').split('。')[0];
  idx.push(`| ${c.id} | ${c.name} | ${c.tagline || ''} | ${shortDesc} |`);
}
let im = `# 選手一覧（WVC所属）\n\n各選手の詳細は \`characters/<id>.md\` を開く。\n\n| id | 名前 | 通称（種別） | 一言 |\n|---|---|---|---|\n${idx.join('\n')}\n`;
fs.writeFileSync(path.join(outDir, 'index.md'), im);
console.log(`wrote ${chars.length} characters`);
