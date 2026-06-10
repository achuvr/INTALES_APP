/**
 * 「今日の職業・属性」を更新する運用スクリプト。
 * 新構造ではアプリは master/config の today フィールドを読む。
 *
 * 使い方: node set-today.js <job> <element>
 *   job     : warrior | magician | archer | gunner
 *   element : fire | water | nature | thunder
 * 例: node set-today.js archer nature
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");

const JOBS = ["warrior", "magician", "archer", "gunner"];
const ELEMENTS = ["fire", "water", "nature", "thunder"];

const [job, el] = process.argv.slice(2);
if (!JOBS.includes(job) || !ELEMENTS.includes(el)) {
  console.error(`使い方: node set-today.js <job> <element>`);
  console.error(`  job: ${JOBS.join(" | ")}`);
  console.error(`  element: ${ELEMENTS.join(" | ")}`);
  process.exit(1);
}

initializeApp({ credential: applicationDefault() });
const db = getFirestore();

db.doc("master/config")
  .update({ today: { job, el } })
  .then(() => console.log(`更新完了: 今日は ${job} / ${el} の日`))
  .catch((e) => {
    console.error(e);
    process.exit(1);
  });
