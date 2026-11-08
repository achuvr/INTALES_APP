// お店からのお知らせを、アプリを入れている全員に通知する（Firebase Cloud Messaging の topic "all"。2026-11-07）。
// アプリはホームを開いたときに "all" を購読する（Assets/Scripts/Commons/AppNotifications.cs）。
//
//   node send-notice.js "タイトル" "本文"            … 送る前の確認だけ（送らない）
//   node send-notice.js "タイトル" "本文" --send     … 実際に全員へ送る（取り消せない）
//
// 例: node send-notice.js "新しいゲームが入りました" "今週は『〇〇』が遊べます" --send
// iPhone に届けるには、Firebase の管理画面（プロジェクトの設定 → Cloud Messaging）に APNs 認証キーの登録が必要。
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getMessaging } = require("firebase-admin/messaging");

const args = process.argv.slice(2);
const send = args.includes("--send");
const topicIdx = args.indexOf("--topic");
const topic = topicIdx >= 0 ? args[topicIdx + 1] : "all"; // "staff" = 管理者のスマホだけ（テスト）
const topicValueIdx = topicIdx >= 0 ? topicIdx + 1 : -1;
const [title, body] = args.filter((a, i) => !a.startsWith("--") && i !== topicValueIdx);
if (!title) {
  console.log('使い方: node send-notice.js "タイトル" "本文" [--send]');
  process.exit(1);
}

if (!process.env.GOOGLE_APPLICATION_CREDENTIALS) {
  process.env.GOOGLE_APPLICATION_CREDENTIALS =
    "C:\\Users\\intal\\.secrets\\intales-a0459-firebase-adminsdk-fbsvc-6f20ee1a7f.json";
}
initializeApp({ credential: applicationDefault(), projectId: "intales-a0459" });

const message = {
  topic,
  notification: { title, body: body ?? "" },
  android: { notification: { channelId: "intales_default" } },
  apns: { payload: { aps: { sound: "default" } } },
};

console.log("送る内容:");
console.log(`  タイトル: ${title}`);
console.log(`  本文    : ${body ?? ""}`);
console.log(`  送り先  : ${topic === "all" ? "全員" : topic === "staff" ? "テスト（管理者のスマホだけ）" : topic}`);
if (!send) {
  console.log("（確認だけです。送るときは最後に --send を付けてください）");
  process.exit(0);
}

getMessaging()
  .send(message)
  .then((id) => console.log(`送りました: ${id}`))
  .catch((e) => {
    console.error(e);
    process.exit(1);
  });
