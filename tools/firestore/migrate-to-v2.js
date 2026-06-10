/**
 * 旧構造 → 新構造への移行スクリプト。
 * 旧データは削除せず残す（バックアップ兼、旧バージョンアプリの互換用）。
 *
 * 新構造:
 *   master/items  : { items: { <itemId>: {...} } }            … 全アイテムマスター
 *   master/config : { items_version, today, achievements, event_images } … セッション設定
 *   users/{uid}   : 既存フィールド + characters: { "0": {...} }  … キャラをmap内蔵
 *
 * 実行: node migrate-to-v2.js          (dry-run)
 *       node migrate-to-v2.js --apply  (実際に書き込み)
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

const APPLY = process.argv.includes("--apply");

async function buildMasterItems() {
  const itemsMap = {};
  const jobDocs = await db.collection("item").listDocuments();
  for (const jobRef of jobDocs) {
    if (jobRef.id === "_metadata") continue;
    const snap = await jobRef.collection("items").get();
    for (const doc of snap.docs) {
      if (itemsMap[doc.id]) {
        console.warn(`  ! itemId衝突: ${doc.id} (${jobRef.id})`);
      }
      itemsMap[doc.id] = { ...doc.data(), job: jobRef.id };
    }
    console.log(`  item/${jobRef.id}: ${snap.size}件`);
  }
  return itemsMap;
}

async function buildMasterConfig() {
  // today
  const todaySnap = await db.collection("today").limit(1).get();
  const today = todaySnap.empty
    ? { job: "", el: "" }
    : { job: todaySnap.docs[0].get("job") || "", el: todaySnap.docs[0].get("el") || "" };

  // achievements
  const achSnap = await db.collection("achievements").get();
  const achievements = achSnap.docs.map((d) => ({
    id: d.id,
    name: d.get("name") || "",
    text: d.get("text") || "",
    image_url: d.get("image_url") || "",
    auto: d.get("auto") === true,
  }));

  // events/notice → 数字キーを順番に配列へ
  const eventsDoc = await db.doc("events/notice").get();
  const eventImages = [];
  if (eventsDoc.exists) {
    const data = eventsDoc.data();
    const keys = Object.keys(data)
      .filter((k) => /^\d+$/.test(k))
      .sort((a, b) => Number(a) - Number(b));
    for (const k of keys) eventImages.push(String(data[k]));
  }

  return {
    items_version: 1,
    today,
    achievements,
    event_images: eventImages,
  };
}

async function buildUserCharacters() {
  // listDocuments で「フィールドなし・サブコレクションのみ」の phantom doc も拾う
  const userRefs = await db.collection("users").listDocuments();
  const result = [];
  for (const userRef of userRefs) {
    const charSnap = await userRef.collection("characters").get();
    if (charSnap.empty) {
      result.push({ ref: userRef, characters: null, count: 0 });
      continue;
    }
    const characters = {};
    for (const doc of charSnap.docs) characters[doc.id] = doc.data();
    result.push({ ref: userRef, characters, count: charSnap.size });
  }
  return result;
}

function summarize(obj) {
  return JSON.stringify(obj).length;
}

async function main() {
  console.log(`=== 移行 ${APPLY ? "(本実行)" : "(dry-run)"} ===\n`);

  console.log("[1/3] master/items を構築...");
  const itemsMap = await buildMasterItems();
  const itemCount = Object.keys(itemsMap).length;
  console.log(`  合計: ${itemCount}件, 推定サイズ: ${(summarize(itemsMap) / 1024).toFixed(1)} KB\n`);

  console.log("[2/3] master/config を構築...");
  const config = await buildMasterConfig();
  console.log(
    `  today=${config.today.job}/${config.today.el}, achievements=${config.achievements.length}件, event_images=${config.event_images.length}件\n`
  );

  console.log("[3/3] users の characters を構築...");
  const users = await buildUserCharacters();
  for (const u of users) {
    console.log(`  ${u.ref.id}: ${u.count}キャラ`);
  }
  console.log();

  if (!APPLY) {
    console.log("dry-run 完了。--apply を付けると書き込みます。");
    return;
  }

  console.log("書き込み開始...");
  await db.doc("master/items").set({ items: itemsMap });
  console.log("  master/items 書き込み完了");

  await db.doc("master/config").set(config);
  console.log("  master/config 書き込み完了");

  for (const u of users) {
    if (!u.characters) continue;
    await u.ref.set({ characters: u.characters }, { merge: true });
    console.log(`  users/${u.ref.id} characters 書き込み完了 (${u.count}キャラ)`);
  }

  console.log("\n=== 移行完了 ===");
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
