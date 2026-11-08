// 古参の惑星「レガシア」（legacia）を、以前のアプリでレベルを上げてきた人に配る（2026-11-07）。
// users/{uid}.characters のいちばん高いレベルが MIN_LV 以上の人の users.planets に "legacia" を足す。
// アプリ側は、持っていればホームを開いたときに1回だけ「これまでの冒険に感謝をこめて」のお知らせを出す。
//
//   node grant-legacy-planet.js            … 対象者の一覧だけ（書き込まない）
//   node grant-legacy-planet.js --min 5    … しきい値を変える（既定 2 = 1回でもレベルが上がった人）
//   node grant-legacy-planet.js --apply    … 実際に配る（ArrayUnion なので何度実行しても重複しない）
//   node grant-legacy-planet.js --apply --only <uid>,<uid>  … 指定した人だけ
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore, FieldValue } = require("firebase-admin/firestore");

const PLANET = "legacia";
const args = process.argv.slice(2);
const apply = args.includes("--apply");
const minIdx = args.indexOf("--min");
const MIN_LV = minIdx >= 0 ? Number(args[minIdx + 1]) : 2;
const onlyIdx = args.indexOf("--only");
const only = onlyIdx >= 0 ? new Set(args[onlyIdx + 1].split(",")) : null;

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

function maxLevel(u) {
  const ch = u.characters;
  const list = Array.isArray(ch) ? ch : ch && typeof ch === "object" ? Object.values(ch) : [];
  let max = 0;
  for (const c of list) {
    const lv = Number(c && (c.lv ?? c.level)) || 0;
    if (lv > max) max = lv;
  }
  return max;
}

(async () => {
  const snap = await db.collection("users").get();
  const targets = [];
  for (const d of snap.docs) {
    const u = d.data();
    const lv = maxLevel(u);
    if (lv < MIN_LV) continue;
    if (only && !only.has(d.id)) continue;
    const has = Array.isArray(u.planets) && u.planets.includes(PLANET);
    targets.push({ uid: d.id, name: u.name ?? "", lv, has });
  }
  targets.sort((a, b) => b.lv - a.lv);
  console.log(`しきい値: Lv.${MIN_LV} 以上 / 対象 ${targets.length} 人${apply ? "（配ります）" : "（確認のみ・書き込みなし）"}`);
  for (const t of targets)
    console.log(`  Lv.${String(t.lv).padStart(3)}  ${t.name.padEnd(12)}  ${t.uid}${t.has ? "  ※もう持っている" : ""}`);
  if (!apply) return;

  let n = 0;
  for (const t of targets) {
    if (t.has) continue;
    await db.collection("users").doc(t.uid).update({ planets: FieldValue.arrayUnion(PLANET) });
    n++;
  }
  console.log(`${n} 人に ${PLANET} を配りました`);
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
