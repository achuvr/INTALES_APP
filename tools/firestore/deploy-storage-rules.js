/** storage.rules を Firebase Storage にデプロイする */
const fs = require("fs");
const path = require("path");
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getSecurityRules } = require("firebase-admin/security-rules");

// 環境変数が無いシェルから実行されても動くようフォールバック
if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}

initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

const BUCKET = "intales-a0459.firebasestorage.app";

async function main() {
  const source = fs.readFileSync(path.join(__dirname, "storage.rules"), "utf8");
  const rules = getSecurityRules();
  const ruleset = await rules.releaseStorageRulesetFromSource(source, BUCKET);
  console.log(`デプロイ完了: ${ruleset.name} (${ruleset.createTime}) bucket=${BUCKET}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
