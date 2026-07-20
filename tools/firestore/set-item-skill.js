/**
 * master/items の装備1件に skill_id（クライアントの BattleSkillRegistry のキー）を設定/解除する。
 * スキルブックを装備登録シーンで登録したあと、バトルスキルを紐付けるのに使う
 * （装備登録シーンには skill_id の入力UIが無いため）。
 * 変更後は master/config.items_version を +1 して各端末の差分同期に乗せる
 * （各端末はアプリ次回起動時に反映）。
 *
 * 使い方: node set-item-skill.js <item_id または装備名（完全一致）> <skill_id | none>
 * 例:     node set-item-skill.js "ドミニオンのスキルブック(A)" dominion_d8_prob20
 *         node set-item-skill.js xbYwpomRqcjWFdREYTry none    ← 解除
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore, FieldValue } = require("firebase-admin/firestore");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}
initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

// クライアント（Assets/Scripts/Commons/BattleSkill.cs の BattleSkillRegistry）に定義済みのID。
// ここに無いIDも設定はできるが、クライアント側に定義が無いと発動しない。
const KNOWN_SKILL_IDS = [
  "dice_prob10",              // ダイスの加護: 8面で1,3,6→確率+10（手動発動）
  "dice_d8_subtract",         // ダイスの極意: 1D100と一緒に8面を振って出目を引く
  "engine_prob_per_book",     // エンジンビルド: 所持スキル本1冊につき確率+1
  "coop_party_prob20",        // 協力: 自分ATK-1で味方全員 確率+20
  "wingspan_crit_x2",         // ウイングスパン: クリ率×2〜3（戦闘ごとにランダム）
  "wall_overkill_gp",         // 城壁都市: オーバーキル3以上でGP+2
  "wingspan_prob_per_streak", // ウイングスパン: 連勝数ぶん確率+、5連勝ごとにレベル+1
  "dominion_d8_prob20",       // ドミニオン: 戦闘開始時に8面で1,3,6→確率+20（自動発動）
  "catan_overkill_level",     // カタン: オーバーキル4ダメージごとに討伐時のレベル+1
];

const [, , target, skillIdArg] = process.argv;
if (!target || !skillIdArg) {
  console.error('使い方: node set-item-skill.js <item_id または装備名（完全一致）> <skill_id | none>');
  console.error("定義済みskill_id:\n  " + KNOWN_SKILL_IDS.join("\n  "));
  process.exit(1);
}
const removing = skillIdArg === "none";

async function main() {
  const db = getFirestore();
  const snap = await db.doc("master/items").get();
  if (!snap.exists) throw new Error("master/items がありません");
  const items = snap.get("items") || {};

  // item_id 一致を優先し、無ければ装備名の完全一致で探す
  let hits = items[target] ? [[target, items[target]]] : [];
  if (hits.length === 0)
    hits = Object.entries(items).filter(([, it]) => (it.name || "") === target);

  if (hits.length === 0) throw new Error(`装備が見つかりません: ${target}`);
  if (hits.length > 1)
    throw new Error(`同名の装備が${hits.length}件あります。item_id で指定してください:\n`
      + hits.map(([id, it]) => `  ${id} (${it.slot_type})`).join("\n"));

  const [itemId, item] = hits[0];

  if (!removing && !KNOWN_SKILL_IDS.includes(skillIdArg))
    console.warn(`※ 警告: "${skillIdArg}" はクライアントの BattleSkillRegistry に無いIDです（設定はしますが発動しません）`);

  await db.doc("master/items").update({
    [`items.${itemId}.skill_id`]: removing ? FieldValue.delete() : skillIdArg,
  });
  // クライアント差分同期のバージョンを上げる（次回起動時に各端末へ反映）
  await db.doc("master/config").update({ items_version: FieldValue.increment(1) });

  console.log(removing
    ? `解除完了: ${item.name}（${itemId}）の skill_id を削除しました`
    : `設定完了: ${item.name}（${itemId}）→ skill_id=${skillIdArg}`);
  console.log("items_version を +1 しました（各端末にはアプリ次回起動時に反映）");
}

main().catch((e) => {
  console.error(e.message || e);
  process.exit(1);
});
