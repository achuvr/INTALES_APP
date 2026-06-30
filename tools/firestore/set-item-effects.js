// アイテム（master/items）の効果を「アイテム個別」に設定するツール。
// 一律付与はやめ、OVERRIDES にアイテム名（または item_id）ごとに効果を指定する方式。
//
// 使い方:
//   node set-item-effects.js              … OVERRIDES の付与プランを表示（dry-run）
//   node set-item-effects.js --apply      … OVERRIDES を実際に書き込む
//   node set-item-effects.js --clear-all  … 全アイテムの effects を空に戻す（暫定一律値の取り消し）
//
// 書き込み時は master/config.items_version を +1（クライアント再同期トリガー）。
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore, FieldValue } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

// ▼ アイテムごとの効果を指定（キー = アイテム名 か item_id、値 = [[EffectType名, 値], ...]）
//   例: "城壁都市の帽子(弓)": [["CriticalRateUp", 8]],
// ▼ スキルブックの任意発動スキル（キー = アイテム名 か item_id、値 = BattleSkillRegistry のID）
const SKILLS = {
  "王国領主のスキルブック(A)": "dice_prob10",      // 8面で1/3/6なら確率+10
  "ダイスのスキルブック(A)":   "dice_d8_subtract",      // 1D100と一緒に8面を振り出目を引く
  "エンジンビルドのスキルブック(A)": "engine_prob_per_book", // 所持アクティブスキル本1冊につき確率+1
  "協力のスキルブック(A)":           "coop_party_prob20",     // 自ATK-1で味方全員 確率+20
  "ウイングスパンのスキルブック(A)": "wingspan_crit_x2",      // クリティカル率2倍
  "城壁都市のスキルブック(B)":       "wall_overkill_gp",      // オーバーキル3以上でGP+2
  "ウイングスパンのスキルブック(B)": "wingspan_prob_per_streak", // 連勝数ぶん確率+
};

// ▼ アイテムの説明文を上書き（キー = アイテム名 か item_id）
const DESCRIPTIONS = {
  "城壁都市のスキルブック(B)": "オーバーキルしたダメージが3以上あるならGPを追加で2もらえる",
  "ウイングスパンのスキルブック(B)": "連勝数ぶん確率があがる。さらに5連勝するたびに討伐時のレベルが1追加で上がる",
};

const OVERRIDES = {
  // 「夕日の開拓者」武器（各職業）: クリティカルダメージ +1
  "夕日の開拓者(戦士)": [["CriticalDamageUp", 1]],
  "夕日の開拓者(杖)":   [["CriticalDamageUp", 1]],
  "夕日の開拓者(弓)":   [["CriticalDamageUp", 1]],
  "夕日の開拓者(銃)":   [["CriticalDamageUp", 1]],

  // 「夕日開拓者の帽子」頭装備（各職業）: クリティカル率 +2
  "夕日開拓者の帽子(戦士)": [["CriticalRateUp", 2]],
  "夕日開拓者の帽子(魔法)": [["CriticalRateUp", 2]],
  "夕日開拓者の帽子(弓)":   [["CriticalRateUp", 2]],
  "夕日開拓者の帽子(銃)":   [["CriticalRateUp", 2]],

  // 「夕日開拓者の」体装備（各職業）: 確率上昇 +10
  "夕日開拓者の鎧(戦士)":   [["ProbUp", 10]],
  "夕日開拓者のローブ(魔法)": [["ProbUp", 10]],
  "夕日開拓者の狩衣(弓)":   [["ProbUp", 10]],
  "夕日開拓者の戦闘服(銃)": [["ProbUp", 10]],

  // 「夕日開拓者の靴」足装備（各職業）: クリティカル率 +1
  "夕日開拓者の靴(戦士)": [["CriticalRateUp", 1]],
  "夕日開拓者の靴(魔法)": [["CriticalRateUp", 1]],
  "夕日開拓者の靴(弓)":   [["CriticalRateUp", 1]],
  "夕日開拓者の靴(銃)":   [["CriticalRateUp", 1]],

  // 「城壁都市の」武器（各職業）: クリティカルダメージ +1
  "城壁都市の剣(戦士)": [["CriticalDamageUp", 1]],
  "城壁都市の杖(魔法)": [["CriticalDamageUp", 1]],
  "城壁都市の弓(弓)":   [["CriticalDamageUp", 1]],
  "城壁都市の銃(銃)":   [["CriticalDamageUp", 1]],

  // 「城壁都市の」頭装備（各職業）: 確率上昇 +5、クリティカル率 +1
  "城壁都市の兜(戦士)":   [["ProbUp", 5], ["CriticalRateUp", 1]],
  "城壁都市の帽子(魔法)": [["ProbUp", 5], ["CriticalRateUp", 1]],
  "城壁都市の帽子(弓)":   [["ProbUp", 5], ["CriticalRateUp", 1]],
  "城壁都市のハット(銃)": [["ProbUp", 5], ["CriticalRateUp", 1]],

  // 「城壁都市の」体装備（各職業）: クリティカル率 +2
  "城壁都市の鎧(戦士)":   [["CriticalRateUp", 2]],
  "城壁都市のローブ(魔法)": [["CriticalRateUp", 2]],
  "城壁都市の狩衣(弓)":   [["CriticalRateUp", 2]],
  "城壁都市の戦闘服(銃)": [["CriticalRateUp", 2]],

  // 「城壁都市の」足装備（各職業）: 確率上昇 +5
  "城壁都市のブーツ(戦士)": [["ProbUp", 5]],
  "城壁都市の靴(魔法)":   [["ProbUp", 5]],
  "城壁都市のブーツ(弓)":   [["ProbUp", 5]],
  "城壁都市のブーツ(銃)":   [["ProbUp", 5]],

  // 「王国領主の」武器（各職業）: クリティカルダメージ +1
  "王国領主の剣(戦士)": [["CriticalDamageUp", 1]],
  "王国領主の杖(魔法)": [["CriticalDamageUp", 1]],
  "王国領主の弓(弓)":   [["CriticalDamageUp", 1]],
  "王国領主の銃(銃)":   [["CriticalDamageUp", 1]],

  // 「王国領主の」頭装備（各職業）: 確率上昇 +5
  "王国領主の兜(戦士)":   [["ProbUp", 5]],
  "王国領主の帽子(魔法)": [["ProbUp", 5]],
  "王国領主の帽子(弓)":   [["ProbUp", 5]],
  "王国領主のハット(銃)": [["ProbUp", 5]],

  // 「王国領主の」体装備（各職業）: クリティカル率 +1、確率上昇 +5
  "王国領主の鎧(戦士)":   [["CriticalRateUp", 1], ["ProbUp", 5]],
  "王国領主のローブ(魔法)": [["CriticalRateUp", 1], ["ProbUp", 5]],
  "王国領主の狩衣(弓)":   [["CriticalRateUp", 1], ["ProbUp", 5]],
  "王国領主の戦闘服(銃)": [["CriticalRateUp", 1], ["ProbUp", 5]],

  // 「王国領主の」足装備（各職業）: クリティカル率 +2
  "王国領主のブーツ(戦士)": [["CriticalRateUp", 2]],
  "王国領主の靴(魔法)":   [["CriticalRateUp", 2]],
  "王国領主のブーツ(弓)":   [["CriticalRateUp", 2]],
  "王国領主のブーツ(銃)":   [["CriticalRateUp", 2]],

  // 固有武器（各職業）: クリティカル率 +2
  "フェザーアックス(戦士)": [["CriticalRateUp", 2]],
  "オルニスの囁き(魔法)":   [["CriticalRateUp", 2]],
  "スカイウィング(弓)":     [["CriticalRateUp", 2]],
  "アヴィアン・エコー(銃)": [["CriticalRateUp", 2]],

  // 固有頭装備（各職業）: 確率上昇 +5
  "猛禽王の羽兜(戦士)":     [["ProbUp", 5]],
  "森梟の叡智(魔法)":       [["ProbUp", 5]],
  "渡り風の羽帽子(弓)":     [["ProbUp", 5]],
  "黒翼の観測ハット(銃)":   [["ProbUp", 5]],

  // 固有体装備（各職業）: 確率上昇 +5
  "天空狩猟の戦鎧(戦士)":         [["ProbUp", 5]],
  "囀りの魔導ローブ(魔法)":       [["ProbUp", 5]],
  "風翔のレンジャークローク(弓)": [["ProbUp", 5]],
  "機巧鳥の射撃コート(銃)":       [["ProbUp", 5]],

  // 固有足装備（各職業）: クリティカル率 +2
  "断崖の爪踏みブーツ(戦士)": [["CriticalRateUp", 2]],
  "静謐林の羽歩靴(魔法)":     [["CriticalRateUp", 2]],
  "滑空ブーツ(弓)":           [["CriticalRateUp", 2]],
  "照準安定ブーツ(銃)":       [["CriticalRateUp", 2]],
};

const toEffects = (pairs) =>
  pairs.map(([effect_type, value]) => ({ effect_type, value }));

async function main() {
  const apply    = process.argv.includes("--apply");
  const clearAll = process.argv.includes("--clear-all");

  const snap = await db.collection("master").doc("items").get();
  if (!snap.exists) return console.log("master/items が存在しません");
  const items = snap.data().items || {};

  const updates = {};
  let count = 0;

  if (clearAll) {
    for (const [id, it] of Object.entries(items)) {
      if ((it.effects || []).length === 0) continue;
      updates[`items.${id}.effects`] = [];
      count++;
      console.log(`空に戻す: "${it.name}" [${it.slot_type}]`);
    }
  } else {
    for (const [id, it] of Object.entries(items)) {
      const pairs = OVERRIDES[it.name] || OVERRIDES[id];
      if (pairs) {
        const fx = toEffects(pairs);
        updates[`items.${id}.effects`] = fx;
        count++;
        console.log(`${apply ? "付与" : "予定"}: "${it.name}" [${it.slot_type}] -> ${fx.map(e => `${e.effect_type}=${e.value}`).join(", ")}`);
      }
      const skillId = SKILLS[it.name] ?? SKILLS[id];
      if (skillId !== undefined) {
        updates[`items.${id}.skill_id`] = skillId;
        count++;
        console.log(`${apply ? "付与" : "予定"}: "${it.name}" [${it.slot_type}] -> skill_id=${skillId || "(クリア)"}`);
      }
      const desc = DESCRIPTIONS[it.name] ?? DESCRIPTIONS[id];
      if (desc !== undefined) {
        updates[`items.${id}.description`] = desc;
        count++;
        console.log(`${apply ? "付与" : "予定"}: "${it.name}" [${it.slot_type}] -> desc="${desc}"`);
      }
    }
  }

  console.log(`\n対象 ${count}件`);

  const willWrite = clearAll || apply;
  if (!willWrite) {
    console.log("\n(dry-run) 書き込むには --apply（個別付与）または --clear-all（全消去）を付けて再実行してください。");
    return;
  }
  if (count === 0) { console.log("書き込む対象がありません。"); return; }

  await db.collection("master").doc("items").update(updates);
  await db.collection("master").doc("config").update("items_version", FieldValue.increment(1));
  console.log(`\n書き込み完了: ${count}件を更新し、items_version を +1 しました。`);
}

main().catch((e) => { console.error(e); process.exit(1); });
