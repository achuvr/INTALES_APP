/**
 * 入店中の人数（visitors / visitor_keys コレクション）の掃除スクリプト。
 * GitHub Actions の日次ワークフロー（sync-boardgames.yml、毎日 04:00 JST）から毎日実行される。
 * 新ホームの中央の惑星のいちばん外の円を、入店中の人数ぶんの球が回る機能で使っている
 * （Assets/Scripts/Commons/StoreVisitors.cs）。
 *
 * 方針:
 *   ・入店した時刻が直近の営業終了時刻（26:00＝翌2:00 JST）より前の記録は、退店し忘れなので消す
 *     （アプリ側でも営業終了後は数えないが、記録を残さないため）
 *   ・指している記録がなくなった鍵（visitor_keys）も消す
 *
 * 使い方: node cleanup-visitors.js [--dry-run]
 *   （GOOGLE_APPLICATION_CREDENTIALS にサービスアカウント鍵を指定して実行。--dry-run は数えるだけで消さない）
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore, Timestamp } = require("firebase-admin/firestore");

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

const DRY = process.argv.includes("--dry-run");

// 営業終了 26:00（翌2:00）。Assets/Scripts/Commons/LocalVisitLog.cs の ClosingMinutesFromMidnight と同じ
const CLOSING_MINUTES = 26 * 60;
const JST_OFFSET_MS = 9 * 60 * 60 * 1000;
const DAY_MS = 24 * 60 * 60 * 1000;

/** いまより前で、いちばん新しい営業終了時刻（UTCのミリ秒） */
function lastClosing(nowMs) {
  const jst = nowMs + JST_OFFSET_MS;
  let close = Math.floor(jst / DAY_MS) * DAY_MS + CLOSING_MINUTES * 60 * 1000;
  while (close > jst) close -= DAY_MS;
  return close - JST_OFFSET_MS;
}

async function main() {
  const cutoff = Timestamp.fromMillis(lastClosing(Date.now()));

  // 1) 営業終了時刻より前に入店した記録（退店し忘れ）。入店した時刻はあとから変えられない（ルール）
  const stale = await db.collection("visitors").where("at", "<", cutoff).get();
  if (!DRY) {
    for (let i = 0; i < stale.docs.length; i += 500) {
      const batch = db.batch();
      stale.docs.slice(i, i + 500).forEach((d) => batch.delete(d.ref));
      await batch.commit();
    }
  }

  // 2) 指している記録がなくなった鍵。鍵を先に一覧してから記録を一覧する
  //    （鍵と記録は同じ書き込みで作られるので、この順なら今まさに入店した人の鍵を消さない）。
  //    一覧した後に付け替えられた鍵は消さない（更新時刻を条件に消す）
  const keys = await db.collection("visitor_keys").get();
  const staleIds = new Set(stale.docs.map((d) => d.id)); // --dry-run では消していないので、ここで除く
  const alive = new Set(
    (await db.collection("visitors").select().get()).docs.map((d) => d.id).filter((id) => !staleIds.has(id))
  );
  const orphan = keys.docs.filter((k) => !alive.has(k.get("id")));
  let removed = 0;
  let skipped = 0;
  for (const k of orphan) {
    if (DRY) continue;
    try {
      await k.ref.delete({ lastUpdateTime: k.updateTime });
      removed++;
    } catch (e) {
      skipped++; // 一覧した後に入店し直した（鍵が付け替えられた）
    }
  }

  // UIDなどは出さない（件数だけ）
  console.log(
    `${DRY ? "（確認のみ・削除なし）" : ""}掃除完了: 退店し忘れの記録 ${stale.size}件` +
    ` / 記録のない鍵 ${DRY ? orphan.length : removed}件` +
    (skipped ? `（入店し直したので残した鍵 ${skipped}件）` : "") +
    ` / 基準: ${cutoff.toDate().toISOString()} より前の入店`
  );
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
