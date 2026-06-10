const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

const MAX_DOCS_PER_COLLECTION = 5;
const MAX_DEPTH = 4;

function summarizeValue(v) {
  if (v === null) return "null";
  if (Array.isArray(v)) return `array(${v.length})[${v.slice(0, 2).map(summarizeValue).join(", ")}...]`;
  if (typeof v === "object") {
    if (v._seconds !== undefined) return "Timestamp";
    if (v.constructor && v.constructor.name === "Timestamp") return "Timestamp";
    const keys = Object.keys(v);
    return `map{${keys.slice(0, 10).join(",")}}`;
  }
  if (typeof v === "string") return `"${v.length > 60 ? v.slice(0, 60) + "..." : v}"`;
  return String(v);
}

async function dumpCollection(colRef, depth, indent) {
  const countSnap = await colRef.count().get();
  const total = countSnap.data().count;
  console.log(`${indent}[${colRef.id}] (${total} docs)`);

  const snap = await colRef.limit(MAX_DOCS_PER_COLLECTION).get();
  for (const doc of snap.docs) {
    const data = doc.data();
    const fields = Object.entries(data)
      .map(([k, v]) => `${k}=${summarizeValue(v)}`)
      .join(", ");
    console.log(`${indent}  ${doc.id}: ${fields || "(empty)"}`);
    if (depth < MAX_DEPTH) {
      const subs = await doc.ref.listCollections();
      for (const sub of subs) {
        await dumpCollection(sub, depth + 1, indent + "    ");
      }
    }
  }
  // also check docs that may only exist as subcollection parents (phantom docs)
  if (depth < MAX_DEPTH) {
    const allDocRefs = await colRef.listDocuments();
    const shownIds = new Set(snap.docs.map((d) => d.id));
    for (const ref of allDocRefs) {
      if (shownIds.has(ref.id)) continue;
      const subs = await ref.listCollections();
      if (subs.length > 0) {
        console.log(`${indent}  ${ref.id}: (phantom or not sampled, has subcollections)`);
        for (const sub of subs) {
          await dumpCollection(sub, depth + 1, indent + "    ");
        }
      }
    }
  }
}

async function main() {
  const collections = await db.listCollections();
  for (const col of collections) {
    await dumpCollection(col, 0, "");
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
