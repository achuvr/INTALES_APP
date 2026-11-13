/**
 * ボードゲーム在庫の同期スクリプト（パソコン・GitHub Actions 用）。
 *
 * bodoge.hoobby.net のスペースページから全ゲームを取得し（BodogeSpaceJsonExporter の
 * Node 移植）、当店アプリユーザーの「お気に入り」数を人気度として合成した上で、
 * 内容ハッシュが前回と変わっていた場合のみ
 *   - master/boardgames … JSON本文（クライアントがそのまま保存して使う）・version・hash
 *   - master/config.boardgames_version … クライアントの差分同期判定用
 * を更新する。変わっていなければ一覧は書き込まない
 * （＝ユーザー側の追加読み取りも発生しない。登録日の記録 master/boardgames_seen だけは毎回書く）。
 *
 * 中身の処理（スクレイプ・登録日・お店で追加したゲームの合わせ方・配信の書き込み）は tools/functions/boardgames.js にあり、
 * アプリの管理者の「ボドゲーマから図鑑を更新」（Cloud Functions の syncBoardgames。2026-11-13）と共通。
 * アプリからの更新との違い: このスクリプトは全部のゲームの詳細ページ（ジャンル・デザイナー）を読み直し、
 * お店で追加したゲームは boardgames-extra.json から配信する（アプリからの更新は、配信中の一覧のものをそのまま残す）。
 * 動かす前に tools/functions でも npm install しておく（cheerio をそちらから読むため）。
 *
 * ハッシュは URL でソートしたゲーム配列から計算する（表示順の揺れや
 * updated 日付ではバージョンが上がらない）。
 *
 * 新着の情報（アプリの図鑑の「新着順」と「NEW」の印）:
 *   - added_rank … お店の一覧で何番目に新しく登録されたか（1=いちばん新しい）。
 *                  一覧を何も指定せずに開いたときの並び＝お店が登録した順（新しいものが先）をそのまま使う
 *   - added_on   … 同期でそのゲームを初めて見つけた日（YYYY-MM-DD）。ボドゲーマには日付が出ないため自前で記録する。
 *                  一度見つけたゲームの日付は master/boardgames_seen に覚えておく（一覧から消えたゲームも忘れない）。
 *                  この仕組みを入れる前からあるゲームは "" (不明)
 *
 * お店で追加したゲーム（ボドゲーマに無いゲーム。2026-11-06）:
 *   boardgames-extra.json の games に書いたゲームを、ボドゲーマの一覧と合わせて配信する（毎日の同期でも消えない）。
 *   url は "intales/extra/{id}"（アプリはこの最後の部分＝id でゲームを見分ける。リンクとしては使っていない）。
 *   新着順は added_on（お店に入った日）の位置に差し込み、登録日もその日にする。
 *   追加・修正したら --extras-only で、スクレイプせずにいまの一覧へ合わせ直して配信できる。
 *
 * 使い方:
 *   node sync-boardgames.js                 # スクレイプして同期（GitHub Actions が毎日実行）
 *   node sync-boardgames.js --extras-only   # スクレイプせず、いま配信中の一覧にお店で追加したゲームを合わせ直す
 *   node sync-boardgames.js --dry-run       # 書き込まず差分の有無だけ表示
 *   node sync-boardgames.js --no-genre      # 詳細ページを見ない高速モード（ジャンル・デザイナー無し）
 *   node sync-boardgames.js --file <path>   # スクレイプせずローカルJSON（Unityエクスポート）を投入
 *   node sync-boardgames.js --out <path>    # 配信と同じJSONをローカルにも書き出す（同梱Resources更新用）
 *
 * 認証: GOOGLE_APPLICATION_CREDENTIALS にサービスアカウント鍵のパスを設定しておく。
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const fs = require("fs");
const path = require("path");
const bg = require("../functions/boardgames");

const args = process.argv.slice(2);
const DRY_RUN = args.includes("--dry-run");
const NO_GENRE = args.includes("--no-genre");
const fileIdx = args.indexOf("--file");
const FROM_FILE = fileIdx >= 0 ? args[fileIdx + 1] : null;
const outIdx = args.indexOf("--out");
const OUT_PATH = outIdx >= 0 ? args[outIdx + 1] : null;
const EXTRAS_ONLY = args.includes("--extras-only");
const EXTRAS_PATH = path.join(__dirname, "boardgames-extra.json");

/** 一覧を全部読み、全部のゲームの詳細ページ（ジャンル・デザイナー）も読む */
async function scrape() {
  const games = await bg.scrapeList();
  if (!NO_GENRE) await bg.fetchDetails(games);
  games.sort(bg.byTitle);
  return games;
}

async function main() {
  initializeApp({ credential: applicationDefault() });
  const db = getFirestore();

  // お店で追加したゲーム（書き方の誤りがあれば、何も書き込まずにここで止まる）
  const extras = bg.loadExtras(EXTRAS_PATH);
  console.log(`お店で追加したゲーム: ${extras.length}件（${path.basename(EXTRAS_PATH)}）`);

  let games;
  if (EXTRAS_ONLY) {
    // スクレイプせず、いま配信中の一覧（お店で追加したゲームは除く）に合わせ直す
    const cur = await db.doc("master/boardgames").get();
    if (!cur.exists) {
      console.error("master/boardgames がありません。--extras-only は配信中の一覧が要ります");
      process.exit(1);
    }
    games = (JSON.parse(cur.get("json") || "{}").games || []).filter((g) => !bg.isExtra(g));
    console.log(`配信中の一覧を使います: ボドゲーマのゲーム${games.length}件`);
  } else if (FROM_FILE) {
    console.log(`ローカルJSONを読み込み: ${FROM_FILE}`);
    const data = JSON.parse(fs.readFileSync(FROM_FILE, "utf8"));
    games = (data.games || []).filter((g) => !bg.isExtra(g));
  } else {
    console.log(`スクレイプ開始: ${bg.SPACE_URL}${NO_GENRE ? "（ジャンル取得なし）" : ""}`);
    games = await scrape();
  }

  if (games.length === 0) {
    // サイト構造の変更や一時的な障害で0件になった場合に在庫を空で配信しない
    console.error("取得結果が0件のため中断します（サイト構造変更の可能性）");
    process.exit(1);
  }

  const bgRef = db.doc("master/boardgames");
  const seenRef = db.doc("master/boardgames_seen");
  const [snap, seenSnap] = await Promise.all([bgRef.get(), seenRef.get()]);

  // 新着の情報（登録順は一覧を読んだ順番で付いている。登録日はここで付ける）
  games = [...games, ...extras];
  const firstSeen = bg.applyFirstSeen(games, seenSnap);
  // お店で追加したゲームの登録日は、書いてある日（お店に入った日）にする。新着順もその日の位置へ
  for (const e of extras) if (e._addedOn !== undefined) e.added_on = e._addedOn;
  games = bg.mergeExtras(games.filter((g) => !bg.isExtra(g)), extras);
  console.log(`新着の情報: 登録順あり${games.filter((g) => g.added_rank > 0).length}件 / ` +
    `登録日あり${games.filter((g) => g.added_on).length}件 / 記録${firstSeen.count}件`);
  if (firstSeen.fresh.length > 0)
    console.log(`新しく見つけたゲーム${firstSeen.fresh.length}件（登録日 ${firstSeen.day}）: ` +
      firstSeen.fresh.map((g) => g.title).join("、"));

  // 人気度 = 当店アプリユーザーの「お気に入り」数（世間の人気ではなく店内人気）。
  // お気に入りが増減した日はハッシュが変わり、次の同期でユーザーへ再配信される。
  // お店で追加したゲームも数える（2026-11-13 まではボドゲーマのゲームだけで、お店で追加したゲームはいつも 0 だった。
  // アプリからの更新と同じ数え方にそろえ、交互に動かしても中身が行ったり来たりしないように）
  const favCounts = await bg.fetchFavoriteCounts(db);
  bg.applyPopularity(games, favCounts);
  const favTotal = [...favCounts.values()].reduce((a, b) => a + b, 0);
  console.log(`お気に入り集計: ${favCounts.size}タイトル / 計${favTotal}件`);

  const canon = bg.canonicalize(games);
  const hash = bg.contentHash(canon);
  console.log(`取得完了: ${canon.length}件, hash=${hash.slice(0, 12)}…`);

  const changed = !(snap.exists && snap.get("hash") === hash);
  const version = (snap.exists ? snap.get("version") || 0 : 0) + (changed ? 1 : 0);
  const { updated, json } = bg.buildJson(canon, version);

  if (OUT_PATH) {
    // アプリ同梱のResources JSONを更新する用途。配信されるものと同一の内容を書く
    fs.writeFileSync(OUT_PATH, json);
    console.log(`ローカルにも書き出し: ${OUT_PATH} (v${version})`);
  }

  if (changed) {
    const bytes = Buffer.byteLength(json, "utf8");
    console.log(`変更あり → v${version} を配信 (${canon.length}件, ${(bytes / 1024).toFixed(0)}KB)`);
    if (bytes > bg.SIZE_WARN_BYTES)
      console.warn(`警告: JSONが${(bytes / 1024).toFixed(0)}KBあり、1MiBのドキュメント上限に近づいています`);
  } else {
    // 一覧は書かない（ユーザー側の追加読み取りも発生しない）。登録日の記録（最後に同期した時刻）だけ書く
    console.log(`一覧は変更なし (v${version}) → 一覧の書き込みはスキップ`);
  }

  if (DRY_RUN) {
    console.log("--dry-run のため書き込みません");
    return;
  }

  await bg.commit(db, { changed, version, count: canon.length, updated, hash, json, firstSeen });
  console.log(changed
    ? `更新完了: master/boardgames v${version} / master/config.boardgames_version=${version} / 登録日の記録${firstSeen.count}件`
    : `更新完了: 登録日の記録${firstSeen.count}件（master/boardgames_seen）`);
}

// 直接実行したときだけ同期する（テストから関数だけを読み込めるように）
if (require.main === module) {
  main().catch((e) => {
    console.error(e);
    process.exit(1);
  });
}

module.exports = {
  applyFirstSeen: bg.applyFirstSeen,
  canonicalize: bg.canonicalize,
  keyOf: bg.keyOf,
  loadExtras: (file = EXTRAS_PATH) => bg.loadExtras(file),
  mergeExtras: bg.mergeExtras,
};
