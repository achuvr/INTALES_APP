/** Firebaseプロジェクトに登録済みのアプリ（iOS/Android）を一覧表示する */
const { GoogleAuth } = require("google-auth-library");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}

const PROJECT = "intales-a0459";

async function main() {
  const auth = new GoogleAuth({
    scopes: ["https://www.googleapis.com/auth/cloud-platform"],
  });
  const client = await auth.getClient();

  for (const kind of ["iosApps", "androidApps"]) {
    const url = `https://firebase.googleapis.com/v1beta1/projects/${PROJECT}/${kind}`;
    const res = await client.request({ url });
    const apps = res.data.apps || [];
    console.log(`--- ${kind}: ${apps.length}件 ---`);
    for (const a of apps) {
      console.log(
        `  appId=${a.appId}  bundle/package=${a.bundleId || a.packageName}  state=${a.state}`
      );
    }
  }
}

main().catch((e) => {
  console.error(e.response ? JSON.stringify(e.response.data, null, 2) : e);
  process.exit(1);
});
