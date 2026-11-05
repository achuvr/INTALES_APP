/** 登録済みiOSアプリの GoogleService-Info.plist を取得して保存する */
const fs = require("fs");
const { GoogleAuth } = require("google-auth-library");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}

// Unity側のiOSバンドルID com.intalescafe.intalescafeapp に対応するアプリ
const APP_ID = "1:332131859386:ios:403ccca668a921836f3b83";
const OUT = process.argv[2] || "GoogleService-Info.plist";

async function main() {
  const auth = new GoogleAuth({
    scopes: ["https://www.googleapis.com/auth/cloud-platform"],
  });
  const client = await auth.getClient();
  const url = `https://firebase.googleapis.com/v1beta1/projects/-/iosApps/${APP_ID}/config`;
  const res = await client.request({ url });
  const buf = Buffer.from(res.data.configFileContents, "base64");
  fs.writeFileSync(OUT, buf);
  console.log(`保存しました: ${OUT} (${res.data.configFilename})`);
  console.log(buf.toString("utf-8"));
}

main().catch((e) => {
  console.error(e.response ? JSON.stringify(e.response.data, null, 2) : e);
  process.exit(1);
});
