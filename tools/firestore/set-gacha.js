/**
 * ガチャの排出テーブル（master/gacha）を設定する運用スクリプト。
 * アプリは master/gacha の pools を読み、重み付き抽選を行う。
 *
 * 使い方:
 *   node set-gacha.js              … 下の POOLS の内容で master/gacha を上書き
 *   node set-gacha.js --show       … 現在の master/gacha を表示
 *   node set-gacha.js --list-items … master/items のID一覧を表示（entries編集用）
 *
 * entry の形式: { type, id, weight }
 *   type: "coupon" … id: atk | drink | coffee | five | seven
 *         "item"   … id: master/items の item_id（既所持なら抽選から自動除外）
 *   weight: 排出の重み。プール内の合計に対する割合が当選確率になる
 *
 * プール:
 *   standard … 常用ガチャ（cost_lv: 引くのに消費するレベル数）
 *   event    … イベントガチャ（来店時のイベントQR "gacha_event" で無料1回）
 *              QRを "gacha_event:xxx" にすると pools.xxx から引ける（イベント別プール用）
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

// 環境変数が無いシェルから実行されても動くようフォールバック
if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}

// ================================================================
// 排出テーブル（ここを編集して実行する）
// ================================================================
const POOLS = {
  standard: {
    name: "常用ガチャ",
    cost_lv: 5,
    entries: [
      { type: "coupon", id: "drink",  weight: 20 },
      { type: "coupon", id: "coffee", weight: 20 },
      { type: "coupon", id: "atk",    weight: 18 },
      { type: "coupon", id: "five",   weight: 15 },
      { type: "coupon", id: "seven",  weight: 10 },
      // 装備を入れる場合は --list-items でIDを調べて追加する:
      // { type: "item", id: "xxxx", weight: 5 },
    ],
  },
  event: {
    name: "イベントガチャ",
    cost_lv: 0,
    entries: [
      { type: "coupon", id: "drink",  weight: 25 },
      { type: "coupon", id: "coffee", weight: 25 },
      { type: "coupon", id: "seven",  weight: 20 },
      // { type: "item", id: "xxxx", weight: 10 },
    ],
  },
};

const VALID_TYPES = ["coupon", "item"];
const VALID_COUPONS = ["atk", "drink", "coffee", "five", "seven"];

initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });
const db = getFirestore();

async function main() {
  const mode = process.argv[2];

  if (mode === "--show") {
    const snap = await db.doc("master/gacha").get();
    console.log(snap.exists ? JSON.stringify(snap.data(), null, 2) : "(master/gacha は未作成)");
    return;
  }

  if (mode === "--list-items") {
    const snap = await db.doc("master/items").get();
    const items = snap.exists ? snap.data().items || {} : {};
    for (const [id, item] of Object.entries(items)) {
      console.log(`${id}\t${item.name || "?"}\t(${item.job || "-"}/${item.slot_type || "-"})`);
    }
    console.log(`計 ${Object.keys(items).length} 件`);
    return;
  }

  // 投入前の簡易チェック（タイプ・ID・重みの妥当性、itemは実在確認）
  const itemsSnap = await db.doc("master/items").get();
  const itemIds = new Set(Object.keys(itemsSnap.exists ? itemsSnap.data().items || {} : {}));

  for (const [poolId, pool] of Object.entries(POOLS)) {
    for (const e of pool.entries) {
      if (!VALID_TYPES.includes(e.type)) throw new Error(`${poolId}: 不明なtype "${e.type}"`);
      if (e.type === "coupon" && !VALID_COUPONS.includes(e.id)) throw new Error(`${poolId}: 不明なクーポン "${e.id}"`);
      if (e.type === "item" && !itemIds.has(e.id)) throw new Error(`${poolId}: master/items に存在しないitem "${e.id}"`);
      if (!Number.isInteger(e.weight) || e.weight <= 0) throw new Error(`${poolId}: ${e.id} のweightが不正`);
    }
  }

  await db.doc("master/gacha").set({ pools: POOLS });

  for (const [poolId, pool] of Object.entries(POOLS)) {
    const total = pool.entries.reduce((s, e) => s + e.weight, 0);
    console.log(`\n[${poolId}] ${pool.name}（消費Lv: ${pool.cost_lv}）`);
    for (const e of pool.entries) {
      const pct = ((e.weight / total) * 100).toFixed(1);
      console.log(`  ${pct.padStart(5)}%  ${e.type}/${e.id}`);
    }
  }
  console.log("\n更新完了: master/gacha");
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
