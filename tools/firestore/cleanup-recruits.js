/**
 * 相席募集ボード（recruits コレクション）の掃除スクリプト。
 * GitHub Actions の日次ワークフロー（sync-boardgames.yml）から毎日実行される。
 *
 * 方針:
 *   ・期限切れ（expires_at < 現在）で通報が付いていない募集は削除する
 *     （募集は当日限りの使い捨て。過去ログを残さないのがプライバシー方針）
 *   ・通報が付いている募集は、店側が確認できるよう期限から7日間だけ残し、
 *     それを過ぎたら削除する
 *
 * 使い方: node cleanup-recruits.js
 *   （GOOGLE_APPLICATION_CREDENTIALS にサービスアカウント鍵を指定して実行）
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore, Timestamp } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

const REPORTED_KEEP_DAYS = 7;

async function main() {
  const now = Timestamp.now();
  const reportedDeadline = Timestamp.fromMillis(
    Date.now() - REPORTED_KEEP_DAYS * 24 * 60 * 60 * 1000
  );

  const snap = await db.collection("recruits").where("expires_at", "<", now).get();
  if (snap.empty) {
    console.log("期限切れの募集はありません");
    return;
  }

  let deleted = 0;
  let kept = 0;
  // 500件ずつのバッチで削除（通常は数件のはず）
  let batch = db.batch();
  let inBatch = 0;
  for (const doc of snap.docs) {
    const d = doc.data();
    const reported = d.reports && Object.keys(d.reports).length > 0;
    if (reported && d.expires_at.toMillis() > reportedDeadline.toMillis()) {
      kept++;
      console.log(
        `保持（通報あり・確認用に${REPORTED_KEEP_DAYS}日残す）: ${doc.id}` +
        ` name=${d.name} 通報=${Object.keys(d.reports).length}件`
      );
      continue;
    }
    batch.delete(doc.ref);
    deleted++;
    inBatch++;
    if (inBatch >= 500) {
      await batch.commit();
      batch = db.batch();
      inBatch = 0;
    }
  }
  if (inBatch > 0) await batch.commit();

  console.log(`掃除完了: 削除 ${deleted}件 / 通報ありで保持 ${kept}件`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
