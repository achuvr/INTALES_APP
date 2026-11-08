/**
 * ボードゲーム在庫の日次同期スクリプト。
 *
 * bodoge.hoobby.net のスペースページから全ゲームを取得し（BodogeSpaceJsonExporter の
 * Node 移植）、当店アプリユーザーの「お気に入り」数を人気度として合成した上で、
 * 内容ハッシュが前回と変わっていた場合のみ
 *   - master/boardgames … JSON本文（クライアントがそのまま保存して使う）・version・hash
 *   - master/config.boardgames_version … クライアントの差分同期判定用
 * を更新する。変わっていなければ一覧は書き込まない
 * （＝ユーザー側の追加読み取りも発生しない。登録日の記録 master/boardgames_seen だけは毎回書く）。
 *
 * ハッシュは URL でソートしたゲーム配列から計算する（表示順の揺れや
 * updated 日付ではバージョンが上がらない）。
 *
 * 新着の情報（アプリの図鑑の「新着順」と「NEW」の印）:
 *   - added_rank … お店の一覧で何番目に新しく登録されたか（1=いちばん新しい）。
 *                  一覧を何も指定せずに開いたときの並び＝お店が登録した順（新しいものが先）をそのまま使う
 *                  （2026-11-05 確認。並び替えの「データベース登録順」(order_type=created_desc) は
 *                  ゲームそのものがボドゲーマに登録された順で、お店の登録順ではない）
 *   - added_on   … 毎朝の同期でそのゲームを初めて見つけた日（YYYY-MM-DD）。ボドゲーマには日付が出ないため自前で記録する。
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
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
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
const outIdx = args.indexOf("--out");
const OUT_PATH = outIdx >= 0 ? args[outIdx + 1] : null;
const EXTRAS_ONLY = args.includes("--extras-only");
const EXTRAS_PATH = path.join(__dirname, "boardgames-extra.json");
const EXTRA_URL_PREFIX = "intales/extra/";

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

/**
 * 一覧ページを全ページたどってゲームを収集する（ジャンル以外）。
 * 並び替えを指定しない一覧はお店が登録した順（新しいものが先）なので、読んだ順番を added_rank にする。
 */
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
        designer: [],
        added_rank: games.length + 1, // 1=お店がいちばん新しく登録したゲーム
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

/** 詳細ページのクレジット欄から、見出しラベルが re に一致する項目のリンクテキストを集める。 */
function parseCredit($, re) {
  const result = [];
  $('ul[class*="credit"] li').each((_, li) => {
    const label = clean($(li).find('span[class*="headline"]').first().text());
    if (!re.test(label)) return;
    $(li)
      .find("a")
      .each((_, a) => {
        const t = clean($(a).text());
        if (t && !result.includes(t)) result.push(t);
      });
  });
  return result;
}

/** 「テーマ/フレーバー」「メカニクス」配下のタグ（ジャンル）。 */
const parseGenre = ($) => parseCredit($, /テーマ|フレーバー|メカニクス/);
/** 「ゲームデザイン」のクレジット（ゲームデザイナー。複数の場合あり）。 */
const parseDesigner = ($) => parseCredit($, /ゲームデザイン|デザイナー/);

async function scrape() {
  const games = await scrapeList();

  if (!NO_GENRE) {
    for (let i = 0; i < games.length; i++) {
      try {
        const $ = cheerio.load(await fetchHtml(games[i].url));
        games[i].genre = parseGenre($);
        games[i].designer = parseDesigner($);
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
    designer: Array.isArray(g.designer) ? g.designer : [],
    popularity: Number.isFinite(g.popularity) ? g.popularity : 0, // 当店ユーザーの「お気に入り」数（人気順ソート用）
    added_rank: Number.isFinite(g.added_rank) ? g.added_rank : 0, // お店が登録した順（1=いちばん新しい。0=不明）
    added_on: g.added_on || "", // 同期で初めて見つけた日（""=不明）
    url: g.url || "",
  }));
}

/** クライアント（BoardGameMarks.KeyOf）と同じゲーム識別キー（URL末尾スラッグ）。 */
function keyOf(g) {
  if (g.url) {
    const t = g.url.replace(/\/+$/, "");
    const i = t.lastIndexOf("/");
    if (i >= 0 && i < t.length - 1) return t.slice(i + 1);
  }
  return g.title || "";
}

/**
 * 当店ユーザーの「お気に入り」数をゲームキーごとに集計する（人気順の源泉）。
 * users 全ドキュメントの favorite_boardgames 配列を数える。
 * select() で必要フィールドだけ取るため通信は最小限（読み取り数=ユーザー数/日）。
 */
async function fetchFavoriteCounts(db) {
  const counts = new Map();
  const snap = await db.collection("users").select("favorite_boardgames").get();
  snap.forEach((doc) => {
    const arr = doc.get("favorite_boardgames");
    if (!Array.isArray(arr)) return;
    for (const key of arr) counts.set(key, (counts.get(key) || 0) + 1);
  });
  return counts;
}

/**
 * 登録日（added_on）を付ける。登録日 = 毎朝の同期でそのゲームを初めて見つけた日。
 *
 * 一度見つけたゲームとその日付は master/boardgames_seen（json 文字列。{ゲームキー: "YYYY-MM-DD" か ""}）に覚えておく。
 * 一覧から消えたゲームも忘れないので、スクレイプが一時的に欠けた日があっても、戻ってきたゲームを
 * 「新しく入った」と取り違えない（お店が登録し直したゲームも元の日付のまま）。
 *
 * 日付は実行した時刻の UTC の日付にする: 毎朝4時（日本時間）の同期で見つかるのは前日の営業中に登録された
 * ゲームなので、日本時間から9時間戻した日（＝UTC の日付）がちょうど登録した日になる。
 *
 * 日付が分からないときは付けない（"" のまま。アプリでは NEW を出さない）:
 *   - 初めて動かすとき（master/boardgames_seen が無い）… 今あるゲームはいつ登録されたか分からない
 *   - 前回の同期から GAP_MAX_HOURS より空いたとき … その間に増えたゲームは、空いていた間のどの日か分からない
 *     （2026-07〜11 は GitHub Actions が動いておらず、一覧が4か月止まっていた）
 *   - 半分以上が「新しい」とき … URL の形が変わった等の異常
 * 最後に同期した時刻は master/boardgames_seen の checked_at に毎回書く。
 */
const GAP_MAX_HOURS = 72;

function applyFirstSeen(games, seenSnap) {
  const now = new Date();
  const day = now.toISOString().slice(0, 10);

  const seen = new Map();
  let unknownReason = "";
  if (!seenSnap.exists) {
    unknownReason = "登録日の記録を始めるので、今あるゲームは日付不明にします";
  } else {
    let saved = {};
    try {
      saved = JSON.parse(seenSnap.get("json") || "{}");
    } catch (e) {
      console.warn(`[seen] master/boardgames_seen を読めませんでした（${e.message}）`);
    }
    for (const [k, v] of Object.entries(saved)) seen.set(k, typeof v === "string" ? v : "");
    const last = seenSnap.get("checked_at");
    const gapHours = last && typeof last.toDate === "function" ? (now - last.toDate()) / 36e5 : Infinity;
    if (gapHours > GAP_MAX_HOURS)
      unknownReason = `前回の同期から${Number.isFinite(gapHours) ? Math.round(gapHours / 24) + "日" : "長く"}空いたので、` +
        "その間に増えたゲームは日付不明にします";
  }

  const fresh = games.filter((g) => !seen.has(keyOf(g)));
  if (!unknownReason && fresh.length > games.length * 0.5)
    unknownReason = `新しいゲームが${fresh.length}/${games.length}件あり、異常の可能性があるので日付不明にします`;
  if (unknownReason && fresh.length > 0) console.warn(`[seen] ${unknownReason}（${fresh.length}件）`);
  for (const g of fresh) seen.set(keyOf(g), unknownReason ? "" : day);
  for (const g of games) g.added_on = seen.get(keyOf(g)) || "";

  const sorted = Object.fromEntries([...seen.entries()].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
  return {
    day,
    now,
    fresh: unknownReason ? [] : fresh,
    unknown: unknownReason ? fresh.length : 0,
    count: seen.size,
    json: JSON.stringify(sorted),
  };
}

// ================================================================
// お店で追加したゲーム（boardgames-extra.json）
// ================================================================
const isExtra = (g) => (g.url || "").startsWith(EXTRA_URL_PREFIX);

/**
 * boardgames-extra.json を読んで、一覧と同じ形のゲームにする。書き方の誤りは中断する
 * （まちがった一覧を配信しないため）。
 */
function loadExtras(file = EXTRAS_PATH) {
  if (!fs.existsSync(file)) return [];
  const data = JSON.parse(fs.readFileSync(file, "utf8"));
  const list = Array.isArray(data.games) ? data.games : [];
  const ids = new Set();
  const errors = [];
  const games = list.map((e, i) => {
    const where = `games[${i}]${e && e.title ? `（${e.title}）` : ""}`;
    if (!e || typeof e !== "object") {
      errors.push(`${where}: 形がちがいます`);
      return null;
    }
    const id = String(e.id || "");
    if (!/^intales-[a-z0-9-]+$/.test(id)) errors.push(`${where}: id は intales- で始まる小文字の英数字とハイフンにしてください（${id || "なし"}）`);
    else if (ids.has(id)) errors.push(`${where}: id が重なっています（${id}）`);
    ids.add(id);
    if (!clean(e.title)) errors.push(`${where}: title がありません`);
    if (e.added_on && !/^\d{4}-\d{2}-\d{2}$/.test(e.added_on)) errors.push(`${where}: added_on は YYYY-MM-DD で書いてください`);
    for (const f of ["genre", "designer"])
      if (e[f] !== undefined && !Array.isArray(e[f])) errors.push(`${where}: ${f} は ["…", "…"] の形で書いてください`);
    return {
      title: clean(e.title),
      title_en: clean(e.title_en),
      players: clean(e.players),
      time: clean(e.time),
      year: clean(e.year),
      genre: (Array.isArray(e.genre) ? e.genre : []).map(clean).filter(Boolean),
      designer: (Array.isArray(e.designer) ? e.designer : []).map(clean).filter(Boolean),
      added_rank: 0,
      added_on: e.added_on || "",
      _addedOn: e.added_on || "",
      url: EXTRA_URL_PREFIX + id,
    };
  });
  if (errors.length > 0) throw new Error("boardgames-extra.json の書き方に誤りがあります:\n  " + errors.join("\n  "));
  return games;
}

/**
 * ボドゲーマの一覧（base。お店で追加したゲームは除いておく）に、お店で追加したゲームを合わせる。
 *  ・同じキーのゲームがボドゲーマにもあれば中断（id を変えてもらう）。同じタイトルは注意だけ出す
 *  ・新着順: ボドゲーマの並び（added_rank）はそのままに、お店で追加したゲームを added_on の位置へ差し込む
 *    （登録日が分かっていてそれより古いゲーム、または登録日が分からないゲームの手前）。added_on の無いものは最後
 *  ・登録順は 1 から振り直す
 */
function mergeExtras(base, extras) {
  const keys = new Map(base.map((g) => [keyOf(g), g]));
  const titles = new Map(base.map((g) => [g.title, g]));
  for (const e of extras) {
    if (keys.has(keyOf(e))) throw new Error(`お店で追加したゲームの id がボドゲーマのゲームと重なっています: ${keyOf(e)}`);
    if (titles.has(e.title)) console.warn(`[extra] 「${e.title}」はボドゲーマの一覧にも同じタイトルがあります（${titles.get(e.title).url}）`);
  }

  const ranked = [...base].sort((a, b) => (a.added_rank || 1e9) - (b.added_rank || 1e9));
  const dated = extras.filter((e) => e.added_on).sort((a, b) => (a.added_on < b.added_on ? 1 : a.added_on > b.added_on ? -1 : 0));
  const undated = extras.filter((e) => !e.added_on);
  const order = [];
  let di = 0;
  for (const g of ranked) {
    while (di < dated.length && (!g.added_on || g.added_on < dated[di].added_on)) order.push(dated[di++]);
    order.push(g);
  }
  while (di < dated.length) order.push(dated[di++]);
  order.push(...undated);
  order.forEach((g, i) => (g.added_rank = i + 1));
  return order.sort((a, b) => a.title.localeCompare(b.title, "ja"));
}

/** 表示順・updated 日付に依存しない内容ハッシュ。 */
function contentHash(canon) {
  const sorted = [...canon].sort((a, b) => (a.url < b.url ? -1 : a.url > b.url ? 1 : 0));
  return crypto.createHash("sha256").update(JSON.stringify(sorted), "utf8").digest("hex");
}

async function main() {
  initializeApp({ credential: applicationDefault() });
  const db = getFirestore();

  // お店で追加したゲーム（書き方の誤りがあれば、何も書き込まずにここで止まる）
  const extras = loadExtras();
  console.log(`お店で追加したゲーム: ${extras.length}件（${path.basename(EXTRAS_PATH)}）`);

  let games;
  if (EXTRAS_ONLY) {
    // スクレイプせず、いま配信中の一覧（お店で追加したゲームは除く）に合わせ直す
    const cur = await db.doc("master/boardgames").get();
    if (!cur.exists) {
      console.error("master/boardgames がありません。--extras-only は配信中の一覧が要ります");
      process.exit(1);
    }
    games = (JSON.parse(cur.get("json") || "{}").games || []).filter((g) => !isExtra(g));
    console.log(`配信中の一覧を使います: ボドゲーマのゲーム${games.length}件`);
  } else if (FROM_FILE) {
    console.log(`ローカルJSONを読み込み: ${FROM_FILE}`);
    const data = JSON.parse(fs.readFileSync(FROM_FILE, "utf8"));
    games = (data.games || []).filter((g) => !isExtra(g));
  } else {
    console.log(`スクレイプ開始: ${SPACE_URL}${NO_GENRE ? "（ジャンル取得なし）" : ""}`);
    games = await scrape();
  }

  if (games.length === 0) {
    // サイト構造の変更や一時的な障害で0件になった場合に在庫を空で配信しない
    console.error("取得結果が0件のため中断します（サイト構造変更の可能性）");
    process.exit(1);
  }

  // 人気度 = 当店アプリユーザーの「お気に入り」数（世間の人気ではなく店内人気）。
  // お気に入りが増減した日はハッシュが変わり、翌朝の同期でユーザーへ再配信される。
  const favCounts = await fetchFavoriteCounts(db);
  for (const g of games) g.popularity = favCounts.get(keyOf(g)) || 0;
  const favTotal = [...favCounts.values()].reduce((a, b) => a + b, 0);
  console.log(`お気に入り集計: ${favCounts.size}タイトル / 計${favTotal}件`);

  const bgRef = db.doc("master/boardgames");
  const seenRef = db.doc("master/boardgames_seen");
  const [snap, seenSnap] = await Promise.all([bgRef.get(), seenRef.get()]);

  // 新着の情報（登録順は一覧を読んだ順番で付いている。登録日はここで付ける）
  games = [...games, ...extras];
  const firstSeen = applyFirstSeen(games, seenSnap);
  // お店で追加したゲームの登録日は、書いてある日（お店に入った日）にする。新着順もその日の位置へ
  for (const e of extras) if (e._addedOn !== undefined) e.added_on = e._addedOn;
  games = mergeExtras(games.filter((g) => !isExtra(g)), extras);
  console.log(`新着の情報: 登録順あり${games.filter((g) => g.added_rank > 0).length}件 / ` +
    `登録日あり${games.filter((g) => g.added_on).length}件 / 記録${firstSeen.count}件`);
  if (firstSeen.fresh.length > 0)
    console.log(`新しく見つけたゲーム${firstSeen.fresh.length}件（登録日 ${firstSeen.day}）: ` +
      firstSeen.fresh.map((g) => g.title).join("、"));

  const canon = canonicalize(games);
  const hash = contentHash(canon);
  console.log(`取得完了: ${canon.length}件, hash=${hash.slice(0, 12)}…`);

  const changed = !(snap.exists && snap.get("hash") === hash);
  const version = (snap.exists ? snap.get("version") || 0 : 0) + (changed ? 1 : 0);
  const updated = new Date().toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" });
  const json = JSON.stringify({
    count: canon.length,
    source: SPACE_URL,
    updated,
    version,
    games: canon,
  });

  if (OUT_PATH) {
    // アプリ同梱のResources JSONを更新する用途。配信されるものと同一の内容を書く
    fs.writeFileSync(OUT_PATH, json);
    console.log(`ローカルにも書き出し: ${OUT_PATH} (v${version})`);
  }

  const newVersion = version;
  if (changed) {
    const bytes = Buffer.byteLength(json, "utf8");
    console.log(`変更あり → v${newVersion} を配信 (${canon.length}件, ${(bytes / 1024).toFixed(0)}KB)`);
    if (bytes > SIZE_WARN_BYTES)
      console.warn(`警告: JSONが${(bytes / 1024).toFixed(0)}KBあり、1MiBのドキュメント上限に近づいています`);
  } else {
    // 一覧は書かない（ユーザー側の追加読み取りも発生しない）。登録日の記録（最後に同期した時刻）だけ書く
    console.log(`一覧は変更なし (v${version}) → 一覧の書き込みはスキップ`);
  }

  if (DRY_RUN) {
    console.log("--dry-run のため書き込みません");
    return;
  }

  // 一覧と登録日の記録は一緒に書く（片方だけ書かれて食い違うことがないように）。
  // 登録日の記録は毎回書く（checked_at で、同期が何日も止まっていたことに気づけるように）
  const batch = db.batch();
  if (changed) {
    batch.set(bgRef, { version: newVersion, count: canon.length, updated, hash, json });
    batch.set(db.doc("master/config"), { boardgames_version: newVersion }, { merge: true });
  }
  batch.set(seenRef, { count: firstSeen.count, checked_at: firstSeen.now, json: firstSeen.json });
  await batch.commit();
  console.log(changed
    ? `更新完了: master/boardgames v${newVersion} / master/config.boardgames_version=${newVersion} / 登録日の記録${firstSeen.count}件`
    : `更新完了: 登録日の記録${firstSeen.count}件（master/boardgames_seen）`);
}

// 直接実行したときだけ同期する（テストから関数だけを読み込めるように）
if (require.main === module) {
  main().catch((e) => {
    console.error(e);
    process.exit(1);
  });
}

module.exports = { applyFirstSeen, canonicalize, keyOf, loadExtras, mergeExtras };
