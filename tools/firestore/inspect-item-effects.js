const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

async function main() {
  const snap = await db.collection("master").doc("items").get();
  if (!snap.exists) return console.log("master/items が存在しません");
  const items = snap.data().items || {};
  const ids = Object.keys(items);
  console.log(`master/items: ${ids.length}件\n`);

  for (const id of ids) {
    const it = items[id];
    const fx = (it.effects || []).map((e) => `${e.effect_type}=${e.value}`).join(", ");
    console.log(`${id}\t"${it.name}"\t[${it.slot_type}/${it.job}]\t${fx || "(効果なし)"}`);
  }
}

main().catch((e) => { console.error(e); process.exit(1); });
