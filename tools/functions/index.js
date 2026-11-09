/**
 * INTALES の Cloud Functions（2026-11-10 ユーザー指定）。
 *
 * sendNotice … 管理者アカウントのスマホのアプリから、お知らせ通知（FCM）を送る（呼び出し型 onCall）。
 *   ・呼べるのは IDトークンに admin: true のカスタムクレームがある人だけ（tools/firestore/set-admin.js で付与・解除）
 *   ・送り先は話題 "staff"（テスト。管理者のスマホだけが購読）か "all"（アプリを入れてホームを開いた人全員）
 *   ・通知の中身はパソコンの送信画面（Assets/Scripts/Dev/FcmNotice.cs）・tools/firestore/send-notice.js と同じ形
 *   ・送った記録は Firestore の notice_log に残す（ルールが無いのでアプリからは読めない。Firebase の管理画面で見る）
 *   ・同じタイトル・本文を10分以内にもう一度「全員」へ送ろうとしたら止める
 *     （通信が切れて結果が分からず送り直したとき・2人が同じ内容を送ったときの二重送信を防ぐ）
 *   ・dryRun: true なら FCM に中身を確かめてもらうだけで、どこにも届けない（デプロイ後の動作確認用）
 *   アプリ側は Assets/Scripts/Commons/NoticeFunction.cs（SDK を使わず、onCall の決まりどおりに HTTPS で呼ぶ）
 *
 * デプロイ（プロジェクト直下で）:  firebase deploy --only functions --project intales-a0459
 */
"use strict";

const { onCall, HttpsError } = require("firebase-functions/v2/https");
const logger = require("firebase-functions/logger");
const { initializeApp } = require("firebase-admin/app");
const { getMessaging } = require("firebase-admin/messaging");
const { getFirestore, FieldValue, Timestamp } = require("firebase-admin/firestore");

initializeApp();

/** 送り先の話題（アプリの AppNotifications.TOPIC_STAFF / TOPIC_ALL と同じ） */
const TOPICS = ["staff", "all"];
/** 文字数の上限（アプリの入力欄・パソコンの送信画面と同じ） */
const TITLE_MAX = 40;
const BODY_MAX = 150;
/** アプリが作る Android の通知チャンネル（AppNotifications） */
const ANDROID_CHANNEL = "intales_default";
const LOG = "notice_log";
const DUPLICATE_MINUTES = 10;

/** 前後の空白を取り、制御文字を除く（本文の改行だけは残す） */
function clean(value, allowNewline) {
  if (typeof value !== "string") return "";
  let s = value.replace(/\r\n?/g, "\n");
  s = allowNewline
    ? s.replace(/[\u0000-\u0009\u000B-\u001F\u007F]/g, "")
    : s.replace(/[\u0000-\u001F\u007F]/g, " ");
  return s.trim();
}

function buildMessage(topic, title, body) {
  return {
    topic,
    notification: body ? { title, body } : { title },
    android: { notification: { channelId: ANDROID_CHANNEL } },
    apns: { payload: { aps: { sound: "default" } } },
  };
}

/** 同じタイトル・本文を少し前に「全員」へ送っているか（新しい記録から10件だけ見る） */
async function sentRecently(title, body) {
  const since = Date.now() - DUPLICATE_MINUTES * 60 * 1000;
  const snap = await getFirestore().collection(LOG).orderBy("sent_at", "desc").limit(10).get();
  return snap.docs.some((doc) => {
    const x = doc.data();
    const at = x.sent_at instanceof Timestamp ? x.sent_at.toMillis() : 0;
    return x.topic === "all" && x.title === title && (x.body || "") === body && at >= since;
  });
}

exports.sendNotice = onCall(
  { region: "asia-northeast1", maxInstances: 2, timeoutSeconds: 30, memory: "256MiB" },
  async (request) => {
    const auth = request.auth;
    if (!auth) throw new HttpsError("unauthenticated", "ログインしていません");
    if (auth.token.admin !== true) {
      throw new HttpsError("permission-denied", "管理者のアカウントではありません");
    }

    const data = request.data || {};
    const topic = data.topic;
    if (!TOPICS.includes(topic)) throw new HttpsError("invalid-argument", "送り先が正しくありません");
    const title = clean(data.title, false);
    const body = clean(data.body, true);
    if (!title) throw new HttpsError("invalid-argument", "タイトルを入れてください");
    if (title.length > TITLE_MAX) {
      throw new HttpsError("invalid-argument", `タイトルは${TITLE_MAX}文字までです`);
    }
    if (body.length > BODY_MAX) throw new HttpsError("invalid-argument", `本文は${BODY_MAX}文字までです`);
    const dryRun = data.dryRun === true;

    if (topic === "all") {
      let duplicate = false;
      try {
        duplicate = await sentRecently(title, body);
      } catch (e) {
        logger.warn("送った記録を読めませんでした（二重送信の確認は飛ばします）", { detail: e.message });
      }
      if (duplicate) {
        throw new HttpsError("already-exists",
          `同じお知らせを${DUPLICATE_MINUTES}分以内に全員へ送っています（届いていないときは、少し待ってから送り直してください）`);
      }
    }

    let id;
    try {
      id = await getMessaging().send(buildMessage(topic, title, body), dryRun);
    } catch (e) {
      logger.error("通知を送れませんでした", { code: e.code, detail: e.message });
      throw new HttpsError("internal", "通知を送れませんでした");
    }

    if (!dryRun) {
      try {
        await getFirestore().collection(LOG).add({
          topic,
          title,
          body,
          uid: auth.uid,
          email: auth.token.email || null,
          message_id: id,
          sent_at: FieldValue.serverTimestamp(),
        });
      } catch (e) {
        logger.warn("送った記録を残せませんでした（通知は送れています）", { detail: e.message });
      }
    }
    logger.info(dryRun ? "確認だけ（届けていない）" : "送りました", { topic, title, uid: auth.uid });
    return { id, dryRun };
  }
);
