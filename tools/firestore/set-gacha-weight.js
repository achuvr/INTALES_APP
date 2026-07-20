/**
 * master/gacha の既存エントリ1件の重みだけを更新する（他のエントリは触らない）。
 * 全体を上書きする set-gacha.js と違い、アプリの装備登録シーンが追加した
 * エントリを消さずに済む。
 *
 * 使い方: node set-gacha-weight.js <プールID> <エントリID> <重み>
 * 例:     node set-gacha-weight.js standard five 20
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}
initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

const [, , poolId, entryId, weightStr] = process.argv;
const weight = Number(weightStr);

if (!poolId || !entryId || !Number.isInteger(weight) || weight <= 0) {
  console.error("使い方: node set-gacha-weight.js <プールID> <エントリID> <重み(1以上の整数)>");
  process.exit(1);
}

async function main() {
  const db = getFirestore();
  const ref = db.doc("master/gacha");
  const snap = await ref.get();
  if (!snap.exists) throw new Error("master/gacha がありません");

  const pools = snap.get("pools") || {};
  const pool = pools[poolId];
  if (!pool) throw new Error(`プール "${poolId}" がありません（存在: ${Object.keys(pools).join(", ")}）`);

  const entries = pool.entries || [];
  const entry = entries.find((e) => e.id === entryId);
  if (!entry) throw new Error(`プール "${poolId}" にエントリ "${entryId}" がありません`);

  const before = entry.weight;
  entry.weight = weight;
  await ref.update({ [`pools.${poolId}.entries`]: entries });

  const total = entries.reduce((s, e) => s + (e.weight > 0 ? e.weight : 0), 0);
  console.log(`更新完了: ${poolId}/${entryId} 重み ${before} → ${weight}`);
  console.log(`プール重み合計: ${total}（${entryId} の確率: ${((weight / total) * 100).toFixed(2)}%）`);
}

main().catch((e) => {
  console.error(e.message || e);
  process.exit(1);
});
