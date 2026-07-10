/** 現在の Firebase Storage セキュリティルールを取得して表示する（読み取りのみ） */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getSecurityRules } = require("firebase-admin/security-rules");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}

initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

// 既定バケット名は旧形式(appspot.com)と新形式(firebasestorage.app)の両方を試す
const BUCKETS = ["intales-a0459.appspot.com", "intales-a0459.firebasestorage.app"];

async function main() {
  const rules = getSecurityRules();
  let ruleset = null;
  let bucket = null;
  for (const b of BUCKETS) {
    try {
      ruleset = await rules.getStorageRuleset(b);
      bucket = b;
      break;
    } catch (e) {
      console.log(`(${b}: ${e.message})`);
    }
  }
  if (!ruleset) throw new Error("どのバケット名でもルールを取得できませんでした");
  console.log(`Bucket: ${bucket}`);
  console.log(`Ruleset: ${ruleset.name} (created: ${ruleset.createTime})`);
  for (const file of ruleset.source) {
    console.log(`--- ${file.name} ---`);
    console.log(file.content);
  }
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
