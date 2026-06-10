/**
 * 指定したメールアドレスのアカウントに管理者権限（カスタムクレーム admin=true）を付与する。
 * 管理者はFirestoreルール上で以下が許可される:
 *   - master/*（アイテムマスター・設定）の書き込み
 *   - transfers/*（会員証引き継ぎコード）の発行・管理
 *
 * 使い方:
 *   node set-admin.js <メールアドレス>          … 管理者にする
 *   node set-admin.js <メールアドレス> --remove … 管理者を解除する
 *
 * 注意: 付与後、その端末/エディタで一度ログアウト→再ログインする
 *       （IDトークンが更新されるまで新しい権限が反映されないため）
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getAuth } = require("firebase-admin/auth");

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}
initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

const email = process.argv[2];
const remove = process.argv.includes("--remove");

if (!email || !email.includes("@")) {
  console.error("使い方: node set-admin.js <メールアドレス> [--remove]");
  process.exit(1);
}

async function main() {
  const auth = getAuth();
  const user = await auth.getUserByEmail(email);
  await auth.setCustomUserClaims(user.uid, remove ? null : { admin: true });
  console.log(
    remove
      ? `管理者を解除しました: ${email} (${user.uid})`
      : `管理者に設定しました: ${email} (${user.uid})`
  );
  console.log("※ アプリ/エディタで再ログインすると反映されます");
}

main().catch((e) => {
  console.error(e.message || e);
  process.exit(1);
});
