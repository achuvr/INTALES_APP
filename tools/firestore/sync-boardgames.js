/**
 * ボードゲーム在庫の日次同期スクリプト。
 *
 * bodoge.hoobby.net のスペースページから全ゲームを取得し（BodogeSpaceJsonExporter の
 * Node 移植）、内容ハッシュが前回と変わっていた場合のみ
 *   - master/boardgames … JSON本文（クライアントがそのまま保存して使う）・version・hash
 *   - master/config.boardgames_version … クライアントの差分同期判定用
 * を更新する。変わっていなければ Firestore への書き込みは行わない
 * （＝ユーザー側の追加読み取りも発生しない）。
 *
 * ハッシュは URL でソートしたゲーム配列から計算する（表示順の揺れや
 * updated 日付ではバージョンが上がらない）。
 *
 * 使い方:
 *   node sync-boardgames.js                 # スクレイプして同期（GitHub Actions が毎日実行）
 *   node sync-boardgames.js --dry-run       # 書き込まず差分の有無だけ表示
 *   node sync-boardgames.js --no-genre      # 詳細ページを見ない高速モード（ジャンル無し）
 *   node sync-boardgames.js --file <path>   # スクレイプせずローカルJSON（Unityエクスポート）を投入
 *
 * 認証: GOOGLE_APPLICATION_CREDENTIALS にサービスアカウント鍵のパスを設定しておく。
 */
const { initializeApp, applicationDefault } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const crypto = require("crypto");
const fs = require("fs");
const cheerio = require("cheerio");

const BASE = "https://bodoge.hoobby.net";
const SPACE_URL = `${BASE}/spaces/intales-cafe/games`;
const UA = "Mozilla/5.0 (compatible; bodoge-space-scraper/1.1)";
const SLEEP_MS = 500;
// Firestore の1ドキュメント上限は約1MiB。超えそうなら早めに分割等を検討する
const SIZE_WARN_BYTES = 900_000;

const args = process.argv.slice(2);
const DRY_RUN = args.includes("--dry-run");
const NO_GENRE = args.includes("--no-genre");
const fileIdx = args.indexOf("--file");
const FROM_FILE = fileIdx >= 0 ? args[fileIdx + 1] : null;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const clean = (s) => (s || "").replace(/\s+/g, " ").trim();
const match = (s, re) => {
  const m = (s || "").match(re);
  return m ? m[1] : "";
};

async function fetchHtml(url) {
  const res = await fetch(url, { headers: { "User-Agent": UA } });
  if (!res.ok) throw new Error(`HTTP ${res.status}: ${url}`);
  return res.text();
}

/** 一覧ページを全ページたどってゲームを収集する（ジャンル以外）。 */
async function scrapeList() {
  const games = [];
  const seen = new Set();

  for (let page = 1; ; page++) {
    const url = `${SPACE_URL}?page=${page}`;
    const $ = cheerio.load(await fetchHtml(url));

    let added = 0;
    $("a").each((_, el) => {
      const a = $(el);
      const cls = a.attr("class") || "";
      const href = a.attr("href") || "";
      if (!cls.includes("name") || !href.startsWith("/games/")) return;

      const gameUrl = new URL(href.split("?")[0], BASE).toString();
      if (seen.has(gameUrl)) return;
      seen.add(gameUrl);

      const data = clean(a.find('span[class*="data"]').first().text());
      games.push({
        title: clean(a.find('span[class*="jp"]').first().text()),
        title_en: clean(a.find('span[class*="en"]').first().text()).replace(/^[（(]|[）)]$/g, ""),
        players: match(data, /([0-9]+人(?:～[0-9]+人)?)/),
        time: match(data, /([0-9]+分(?:～[0-9]+分)?|[0-9]+分前後|分前後)/),
        year: match(data, /([0-9]{4}年)/),
        genre: [],
        url: gameUrl,
      });
      added++;
    });

    const hasNext = $("a")
      .toArray()
      .some((el) => clean($(el).text()) === "次へ");
    console.log(`[list] page=${page} added=${added} total=${games.length} hasNext=${hasNext}`);
    if (!hasNext || added === 0) break;
    await sleep(SLEEP_MS);
  }
  return games;
}

/** 詳細ページの「テーマ/フレーバー」「メカニクス」配下のタグを集める。 */
function parseGenre(html) {
  const $ = cheerio.load(html);
  const result = [];
  $('ul[class*="credit"] li').each((_, li) => {
    const label = clean($(li).find('span[class*="headline"]').first().text());
    if (!/テーマ|フレーバー|メカニクス/.test(label)) return;
    $(li)
      .find("a")
      .each((_, a) => {
        const t = clean($(a).text());
        if (t && !result.includes(t)) result.push(t);
      });
  });
  return result;
}

async function scrape() {
  const games = await scrapeList();

  if (!NO_GENRE) {
    for (let i = 0; i < games.length; i++) {
      try {
        games[i].genre = parseGenre(await fetchHtml(games[i].url));
      } catch (e) {
        console.warn(`[genre] 取得失敗（空のまま続行）: ${games[i].url} (${e.message})`);
      }
      if ((i + 1) % 50 === 0) console.log(`[genre] ${i + 1}/${games.length}`);
      await sleep(SLEEP_MS);
    }
  }

  games.sort((a, b) => a.title.localeCompare(b.title, "ja"));
  return games;
}

/** フィールド順を固定した正規形（ハッシュとJSON出力の両方で使う）。 */
function canonicalize(games) {
  return games.map((g) => ({
    title: g.title || "",
    title_en: g.title_en || "",
    players: g.players || "",
    time: g.time || "",
    year: g.year || "",
    genre: Array.isArray(g.genre) ? g.genre : [],
    url: g.url || "",
  }));
}

/** 表示順・updated 日付に依存しない内容ハッシュ。 */
function contentHash(canon) {
  const sorted = [...canon].sort((a, b) => (a.url < b.url ? -1 : a.url > b.url ? 1 : 0));
  return crypto.createHash("sha256").update(JSON.stringify(sorted), "utf8").digest("hex");
}

async function main() {
  let games;
  if (FROM_FILE) {
    console.log(`ローカルJSONを読み込み: ${FROM_FILE}`);
    const data = JSON.parse(fs.readFileSync(FROM_FILE, "utf8"));
    games = data.games || [];
  } else {
    console.log(`スクレイプ開始: ${SPACE_URL}${NO_GENRE ? "（ジャンル取得なし）" : ""}`);
    games = await scrape();
  }

  if (games.length === 0) {
    // サイト構造の変更や一時的な障害で0件になった場合に在庫を空で配信しない
    console.error("取得結果が0件のため中断します（サイト構造変更の可能性）");
    process.exit(1);
  }

  const canon = canonicalize(games);
  const hash = contentHash(canon);
  console.log(`取得完了: ${canon.length}件, hash=${hash.slice(0, 12)}…`);

  initializeApp({ credential: applicationDefault() });
  const db = getFirestore();
  const bgRef = db.doc("master/boardgames");
  const snap = await bgRef.get();

  if (snap.exists && snap.get("hash") === hash) {
    console.log(`変更なし (v${snap.get("version")}) → 書き込みスキップ`);
    return;
  }

  const newVersion = (snap.exists ? snap.get("version") || 0 : 0) + 1;
  const updated = new Date().toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" });
  const json = JSON.stringify({
    count: canon.length,
    source: SPACE_URL,
    updated,
    version: newVersion,
    games: canon,
  });

  const bytes = Buffer.byteLength(json, "utf8");
  console.log(`変更あり → v${newVersion} を配信 (${canon.length}件, ${(bytes / 1024).toFixed(0)}KB)`);
  if (bytes > SIZE_WARN_BYTES)
    console.warn(`警告: JSONが${(bytes / 1024).toFixed(0)}KBあり、1MiBのドキュメント上限に近づいています`);

  if (DRY_RUN) {
    console.log("--dry-run のため書き込みません");
    return;
  }

  const batch = db.batch();
  batch.set(bgRef, { version: newVersion, count: canon.length, updated, hash, json });
  batch.set(db.doc("master/config"), { boardgames_version: newVersion }, { merge: true });
  await batch.commit();
  console.log(`更新完了: master/boardgames v${newVersion} / master/config.boardgames_version=${newVersion}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
