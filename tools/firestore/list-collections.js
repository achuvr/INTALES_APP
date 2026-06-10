const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });

const db = getFirestore();

async function main() {
  const collections = await db.listCollections();
  console.log("Collections:");
  for (const col of collections) {
    const snap = await col.limit(3).get();
    console.log(`- ${col.id} (sample ${snap.size} docs)`);
    for (const doc of snap.docs) {
      console.log(`    ${doc.id}: ${JSON.stringify(doc.data()).slice(0, 120)}`);
    }
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
