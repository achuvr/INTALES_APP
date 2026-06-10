/** 移行後の新構造を検証する */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

async function main() {
  // master/items
  const itemsDoc = await db.doc("master/items").get();
  const items = itemsDoc.get("items") || {};
  const ids = Object.keys(items);
  const byJob = {};
  for (const id of ids) {
    const job = items[id].job || "?";
    byJob[job] = (byJob[job] || 0) + 1;
  }
  console.log(`master/items: ${ids.length}件`, byJob);
  const sample = items[ids[0]];
  console.log("  サンプル:", JSON.stringify({ ...sample, created_at: "..." }).slice(0, 200));

  // 旧構造と件数比較
  let oldCount = 0;
  for (const jobRef of await db.collection("item").listDocuments()) {
    if (jobRef.id === "_metadata") continue;
    oldCount += (await jobRef.collection("items").count().get()).data().count;
  }
  console.log(`  旧item合計: ${oldCount}件 → ${ids.length === oldCount ? "一致 OK" : "不一致 NG!"}`);

  // master/config
  const configDoc = await db.doc("master/config").get();
  const c = configDoc.data();
  console.log(
    `master/config: items_version=${c.items_version}, today=${c.today.job}/${c.today.el}, achievements=${c.achievements.length}, event_images=${c.event_images.length}`
  );

  // users
  const userRefs = await db.collection("users").listDocuments();
  for (const ref of userRefs) {
    const doc = await ref.get();
    const chars = doc.get("characters") || {};
    const subCount = (await ref.collection("characters").count().get()).data().count;
    const mapCount = Object.keys(chars).length;
    const status = mapCount === subCount ? "OK" : "NG!";
    console.log(`users/${ref.id}: map=${mapCount} sub=${subCount} ${status}`);
    for (const [k, v] of Object.entries(chars)) {
      const inv = Array.isArray(v.inventory) ? v.inventory.length : 0;
      console.log(`    [${k}] ${v.name} ${v.job}/${v.el} lv${v.lv} inv=${inv}`);
    }
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
