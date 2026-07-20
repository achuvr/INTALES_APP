/**
 * master/gacha の排出テーブルを一覧表示する（読み取り専用）。
 * 各エントリの重みと、プール内の重み合計に対する排出確率(%)を表示する。
 * item エントリは master/items から名前・職業・部位を引いて表示する。
 *
 * 使い方: node dump-gacha.js
 * 注意: イベントプール（standard以外）では「所持済みの装備（スキルブックA/Bを除く）」と
 *       「選択中キャラの職業に合わない装備（common以外）」が抽選候補から外れるため、
 *       ここに出る確率は「何も持っていないユーザー」の理論値。
 *       standard は所持済みでも排出され GP+2 に変換される（スキルブックはそのまま追加）。
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}
initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

const COUPON_NAMES = {
  atk: "ATKクーポン",
  drink: "ドリンククーポン",
  coffee: "コーヒークーポン",
  five: "5%OFFクーポン",
  seven: "7%OFFクーポン",
};

const JOB_JP = { warrior: "戦士", magician: "魔法使い", archer: "弓使い", gunner: "銃使い", common: "共通" };
const SLOT_JP = {
  weapon: "武器", head: "頭", body: "体", feet: "足", foot: "足",
  skill_book_a: "スキルA", skillA: "スキルA", skill_book_b: "スキルB", skillB: "スキルB",
};

async function main() {
  const db = getFirestore();
  const [gachaSnap, itemsSnap] = await Promise.all([
    db.doc("master/gacha").get(),
    db.doc("master/items").get(),
  ]);
  if (!gachaSnap.exists) {
    console.log("master/gacha がありません");
    return;
  }
  const items = (itemsSnap.exists && itemsSnap.get("items")) || {};
  const series = (itemsSnap.exists && itemsSnap.get("series")) || {}; // series_id → { name: ゲームタイトル, ... }
  const pools = gachaSnap.get("pools") || {};

  for (const [poolId, pool] of Object.entries(pools)) {
    const entries = pool.entries || [];
    const total = entries.reduce((s, e) => s + (e.weight > 0 ? e.weight : 0), 0);
    const cost = pool.cost_gp > 0 ? pool.cost_gp : pool.cost_lv || 0;
    console.log(`\n===== プール: ${poolId}（${pool.name || "名称なし"} / 1回${cost}GP / エントリ${entries.length}件 / 重み合計${total}） =====`);

    const rows = entries
      .slice()
      .sort((a, b) => (b.weight || 0) - (a.weight || 0))
      .map((e) => {
        let label;
        if (e.type === "coupon") {
          label = `[クーポン] ${COUPON_NAMES[e.id] || e.id}`;
        } else if (e.type === "item") {
          const it = items[e.id];
          const game = it && it.series && series[it.series] ? `【${series[it.series].name}】` : "";
          label = it
            ? `[装備] ${game}${it.name}（${JOB_JP[it.job] || it.job}・${SLOT_JP[it.slot_type] || it.slot_type}）`
            : `[装備] （master/itemsに存在しないID: ${e.id}）★要確認`;
        } else {
          label = `[${e.type}] ${e.id}`;
        }
        const pct = total > 0 && e.weight > 0 ? ((e.weight / total) * 100).toFixed(2) : "0（抽選対象外）";
        return { label, weight: e.weight, pct, id: e.id };
      });

    for (const r of rows)
      console.log(`  重み${String(r.weight).padStart(4)} (${r.pct}%)  ${r.label}  id=${r.id}`);
  }
  console.log("\n※ イベントプールでは所持済みの装備（スキルブックA/B除く）と選択中キャラの職業に合わない装備が候補から外れるため、確率は所持状況・職業で変動します");
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
