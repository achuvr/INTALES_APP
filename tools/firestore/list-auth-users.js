/** Firebase Auth のユーザー一覧（メールとUID）を表示する */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}
initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

async function main() {
  const result = await getAuth().listUsers(100);
  for (const u of result.users) {
    const claims = u.customClaims ? JSON.stringify(u.customClaims) : "-";
    console.log(`${u.uid}  ${u.email || "(no email)"}  claims=${claims}`);
  }
  console.log(`合計: ${result.users.length}人`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
