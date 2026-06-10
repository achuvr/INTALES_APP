/** 現在のFirestoreセキュリティルールを取得して表示する */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getSecurityRules } = require("firebase-admin/security-rules");

initializeApp({ credential: applicationDefault() });

async function main() {
  const rules = getSecurityRules();
  const ruleset = await rules.getFirestoreRuleset();
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
