/** firestore.rules をデプロイする */
const fs = require("fs");
const path = require("path");
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getSecurityRules } = require("firebase-admin/security-rules");

initializeApp({ credential: applicationDefault() });

async function main() {
  const source = fs.readFileSync(path.join(__dirname, "firestore.rules"), "utf8");
  const rules = getSecurityRules();
  const ruleset = await rules.releaseFirestoreRulesetFromSource(source);
  console.log(`デプロイ完了: ${ruleset.name} (${ruleset.createTime})`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
