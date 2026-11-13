/**
 * ボードゲーム図鑑の一覧（ボドゲーマのお店のページ）を読み取って配信する共通の処理（2026-11-13 に分けた）。
 *
 * 使うところ:
 *   ・Cloud Functions の syncBoardgames（index.js）… アプリの管理者が図鑑の「ボドゲーマから図鑑を更新」から呼ぶ（syncFromApp）
 *   ・パソコンから動かす tools/firestore/sync-boardgames.js … 全部の詳細ページを読み直す・お店で追加したゲームのファイルを配信する
 * firebase-admin はここでは読み込まない（呼ぶ側の Firestore＝db を受け取る。パソコンのスクリプトは tools/firestore の
 * firebase-admin を使うため）。cheerio は tools/functions の node_modules から読む。
 *
 * 配信するもの（どちらから動かしても同じ）:
 *   - master/boardgames … JSON本文（アプリがそのまま保存して使う）・version・hash
 *   - master/config.boardgames_version … アプリの差分同期の判定用
 *   - master/boardgames_seen … 一度見つけたゲームと登録日の記録（毎回書く。checked_at＝最後に調べた時刻）
 * 中身（ハッシュ）が変わったときだけ一覧と version を書く（変わっていなければお客様のアプリは何も読み直さない）。
 */
"use strict";

const crypto = require("crypto");
const fs = require("fs");
const cheerio = require("cheerio");

const BASE = "https://bodoge.hoobby.net";
const SPACE_URL = `${BASE}/spaces/intales-cafe/games`;
const UA = "Mozilla/5.0 (compatible; bodoge-space-scraper/1.1)";
const SLEEP_MS = 500;
// Firestore の1ドキュメント上限は約1MiB。超えそうなら早めに分割等を検討する
const SIZE_WARN_BYTES = 900_000;
const SIZE_MAX_BYTES = 1_000_000;
const EXTRA_URL_PREFIX = "intales/extra/";
/** 毎朝の同期で、前回からこれより空いたら、その間に増えたゲームは登録日不明にする（applyFirstSeen） */
const GAP_MAX_HOURS = 72;
/**
 * アプリからの更新で、ボドゲーマから読めたゲームが今の図鑑（ボドゲーマのぶん）のこの割合より少なければ配信しない
 * （ページの作りが変わって途中までしか読めなかったときに、欠けた図鑑をお客様へ配らないため）
 */
const MIN_KEEP_RATIO = 0.8;

/** 管理者に見せてよい失敗（文はそのままアプリに出す） */
class SyncError extends Error {}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const clean = (s) => (s || "").replace(/\s+/g, " ").trim();
const match = (s, re) => {
  const m = (s || "").match(re);
  return m ? m[1] : "";
};

async function fetchHtml(url) {
  let res;
  try {
    res = await fetch(url, { headers: { "User-Agent": UA } });
  } catch (e) {
    throw new SyncError(`ボドゲーマにつながりませんでした（${e.message}）`);
  }
  if (!res.ok) throw new SyncError(`ボドゲーマのページを読み込めませんでした（HTTP ${res.status}）`);
  return res.text();
}

/**
 * 一覧ページを全ページたどってゲームを収集する（ジャンル以外）。
 * 並び替えを指定しない一覧はお店が登録した順（新しいものが先）なので、読んだ順番を added_rank にする
 * （2026-11-05 確認。並び替えの「データベース登録順」(order_type=created_desc) はゲームそのものが
 * ボドゲーマに登録された順で、お店の登録順ではない）。
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

/** games の詳細ページを読んで、ジャンルとデザイナーを入れる（読めなかったゲームは空のまま続ける） */
async function fetchDetails(games) {
  for (let i = 0; i < games.length; i++) {
    try {
      const $ = cheerio.load(await fetchHtml(games[i].url));
      games[i].genre = parseGenre($);
      games[i].designer = parseDesigner($);
    } catch (e) {
      console.warn(`[genre] 取得失敗（空のまま続行）: ${games[i].url} (${e.message})`);
    }
    if ((i + 1) % 50 === 0) console.log(`[genre] ${i + 1}/${games.length}`);
    if (i < games.length - 1) await sleep(SLEEP_MS);
  }
}

const byTitle = (a, b) => a.title.localeCompare(b.title, "ja");

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
 * select() で必要フィールドだけ取るため通信は最小限（読み取り数=ユーザー数）。
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

/** 人気度 = 当店アプリユーザーの「お気に入り」数（お店で追加したゲームも、キー＝id で数える） */
function applyPopularity(games, favCounts) {
  for (const g of games) g.popularity = favCounts.get(keyOf(g)) || 0;
}

/** 日本時間の営業日（朝4時で切り替え。アプリの StoreCalendar.TodayBusiness と同じ）の "YYYY-MM-DD" */
function businessDay(now = new Date()) {
  return new Date(now.getTime() + (9 - 4) * 3600e3).toISOString().slice(0, 10);
}

/**
 * 登録日（added_on）を付ける。登録日 = そのゲームを同期で初めて見つけた日。
 *
 * 一度見つけたゲームとその日付は master/boardgames_seen（json 文字列。{ゲームキー: "YYYY-MM-DD" か ""}）に覚えておく。
 * 一覧から消えたゲームも忘れないので、スクレイプが一時的に欠けた日があっても、戻ってきたゲームを
 * 「新しく入った」と取り違えない（お店が登録し直したゲームも元の日付のまま）。
 *
 * options:
 *   day         … 初めて見つけたゲームに付ける日。指定しなければ実行した時刻の UTC の日付
 *                 （毎朝4時（日本時間）の同期で見つかるのは前日の営業中に登録されたゲームなので、日本時間から9時間戻した日
 *                 ＝UTC の日付がちょうど登録した日になる）。アプリからの更新は businessDay（今日の営業日）
 *   gapMaxHours … 前回の同期からこれより空いたら、その間に増えたゲームは日付不明にする（毎朝の同期は 72。
 *                 アプリからの更新は Infinity＝何日空いても今日の日付。管理者が「入ったので更新する」ときに使うため）
 *
 * 日付が分からないときは付けない（"" のまま。アプリでは NEW を出さない）:
 *   - 初めて動かすとき（master/boardgames_seen が無い）… 今あるゲームはいつ登録されたか分からない
 *   - 前回の同期から gapMaxHours より空いたとき（上）
 *   - 半分以上が「新しい」とき … URL の形が変わった等の異常
 * 最後に同期した時刻は master/boardgames_seen の checked_at に毎回書く。
 */
function applyFirstSeen(games, seenSnap, options = {}) {
  const now = options.now || new Date();
  const day = options.day || now.toISOString().slice(0, 10);
  const gapMaxHours = options.gapMaxHours === undefined ? GAP_MAX_HOURS : options.gapMaxHours;

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
    if (gapHours > gapMaxHours)
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
// お店で追加したゲーム（ボドゲーマに無いゲーム。2026-11-06）
// ================================================================
const isExtra = (g) => (g.url || "").startsWith(EXTRA_URL_PREFIX);

/**
 * boardgames-extra.json（パソコンのスクリプトの置き場所にある）を読んで、一覧と同じ形のゲームにする。
 * 書き方の誤りは中断する（まちがった一覧を配信しないため）。
 */
function loadExtras(file) {
  if (!file || !fs.existsSync(file)) return [];
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
 * いま配信中の一覧に入っている「お店で追加したゲーム」を、合わせ直す前の形にする（アプリからの更新用。
 * 追加・修正はパソコンの boardgames-extra.json で行い、配信された一覧がアプリからの更新でもそのまま残る）。
 * 並びは今の登録順（＝同じ日に入ったゲームどうしは boardgames-extra.json に書いた順）にする。
 * 一覧は五十音順なので、そのまま使うと同じ日のゲームの新着順が入れかわり、中身が同じでも配信し直してしまう
 */
function extrasFromList(games) {
  return games.filter(isExtra).sort((a, b) => (a.added_rank || 1e9) - (b.added_rank || 1e9)).map((g) => ({
    title: g.title || "",
    title_en: g.title_en || "",
    players: g.players || "",
    time: g.time || "",
    year: g.year || "",
    genre: Array.isArray(g.genre) ? g.genre : [],
    designer: Array.isArray(g.designer) ? g.designer : [],
    added_rank: 0,
    added_on: g.added_on || "",
    _addedOn: g.added_on || "",
    url: g.url,
  }));
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
    if (keys.has(keyOf(e))) throw new SyncError(`お店で追加したゲームの id がボドゲーマのゲームと重なっています: ${keyOf(e)}`);
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
  return order.sort(byTitle);
}

/** 表示順・updated 日付に依存しない内容ハッシュ。 */
function contentHash(canon) {
  const sorted = [...canon].sort((a, b) => (a.url < b.url ? -1 : a.url > b.url ? 1 : 0));
  return crypto.createHash("sha256").update(JSON.stringify(sorted), "utf8").digest("hex");
}

/** 配信する JSON 本文（アプリの BoardGameCatalogData と同じ形） */
function buildJson(canon, version, now = new Date()) {
  const updated = now.toLocaleDateString("sv-SE", { timeZone: "Asia/Tokyo" });
  return {
    updated,
    json: JSON.stringify({ count: canon.length, source: SPACE_URL, updated, version, games: canon }),
  };
}

/**
 * 一覧と登録日の記録を書く（一緒に書く＝片方だけ書かれて食い違うことがないように）。
 * 一覧（と master/config.boardgames_version）は変わったときだけ。登録日の記録は毎回（checked_at で、同期が止まっていたことに気づける）
 */
async function commit(db, { changed, version, count, updated, hash, json, firstSeen }) {
  const batch = db.batch();
  if (changed) {
    batch.set(db.doc("master/boardgames"), { version, count, updated, hash, json });
    batch.set(db.doc("master/config"), { boardgames_version: version }, { merge: true });
  }
  batch.set(db.doc("master/boardgames_seen"), { count: firstSeen.count, checked_at: firstSeen.now, json: firstSeen.json });
  await batch.commit();
}

/**
 * アプリの管理者の「ボドゲーマから図鑑を更新」（Cloud Functions の syncBoardgames）の中身。
 *  ・ボドゲーマのお店の一覧を全部のページ読む（タイトル・人数・時間・発売年・お店が登録した順）
 *  ・ジャンルとデザイナー（詳細ページ）は、いまの図鑑に無いゲームだけ読む（ほかは今の図鑑の値をそのまま使う。
 *    全部読み直すと10分以上かかるため。全部読み直すときはパソコンの sync-boardgames.js）
 *  ・お店で追加したゲームは、いま配信中の一覧のものをそのまま残す（追加・修正はパソコンの boardgames-extra.json）
 *  ・登録日: この更新で初めて見つけたゲームは今日の営業日（前回から何日空いていても）
 *  ・読めたゲームが今の図鑑より大きく減っていたら（MIN_KEEP_RATIO）、途中までしか読めなかったとみて配信しない
 * dryRun なら何も書かずに結果だけ返す。scrape・details は試験でボドゲーマを読まずに済ませるための差し替え口。
 * 返すもの: { changed, version, count, added: [新しく図鑑に入ったゲームの名前], removed: [図鑑からなくなったゲームの名前], details }
 */
async function syncFromApp(db, { dryRun = false, now = new Date(), scrape = scrapeList, details = fetchDetails } = {}) {
  const bgRef = db.doc("master/boardgames");
  const seenRef = db.doc("master/boardgames_seen");
  const cur = await bgRef.get();
  if (!cur.exists) throw new SyncError("配信中の図鑑（master/boardgames）がありません。パソコンの同期スクリプトで先に配信してください");
  let curGames;
  try {
    curGames = JSON.parse(cur.get("json") || "{}").games || [];
  } catch (e) {
    throw new SyncError(`配信中の図鑑を読めませんでした（${e.message}）`);
  }
  const curBodoge = curGames.filter((g) => !isExtra(g));
  const extras = extrasFromList(curGames);

  console.log(`スクレイプ開始: ${SPACE_URL}（いまの図鑑 ボドゲーマ${curBodoge.length}件・お店で追加${extras.length}件）`);
  const games = await scrape();
  if (games.length === 0)
    throw new SyncError("ボドゲーマから1件も読めませんでした（ページの作りが変わったかもしれません）。図鑑はそのままです");
  if (games.length < curBodoge.length * MIN_KEEP_RATIO)
    throw new SyncError(`ボドゲーマから読めたゲームが${games.length}件だけでした（いまの図鑑は${curBodoge.length}件）。` +
      "途中までしか読めていないかもしれないので、図鑑はそのままにしました");

  // ジャンル・デザイナー: 今の図鑑にあるゲームはその値を使い、無いゲームだけ詳細ページを読む
  const known = new Map(curBodoge.map((g) => [keyOf(g), g]));
  const unknown = [];
  for (const g of games) {
    const old = known.get(keyOf(g));
    if (!old) {
      unknown.push(g);
      continue;
    }
    g.genre = Array.isArray(old.genre) ? old.genre : [];
    g.designer = Array.isArray(old.designer) ? old.designer : [];
  }
  if (unknown.length > 0) {
    console.log(`詳細ページを読みます: ${unknown.length}件`);
    await details(unknown);
  }

  const seenSnap = await seenRef.get();
  const all = [...games, ...extras];
  const firstSeen = applyFirstSeen(all, seenSnap, { now, day: businessDay(now), gapMaxHours: Infinity });
  // お店で追加したゲームの登録日は、書いてある日（お店に入った日）のまま。新着順もその日の位置へ
  for (const e of extras) e.added_on = e._addedOn;
  const merged = mergeExtras(games, extras);
  applyPopularity(merged, await fetchFavoriteCounts(db));

  const canon = canonicalize(merged);
  const hash = contentHash(canon);
  const changed = cur.get("hash") !== hash;
  const version = (cur.get("version") || 0) + (changed ? 1 : 0);
  const { updated, json } = buildJson(canon, version, now);
  const bytes = Buffer.byteLength(json, "utf8");
  if (changed && bytes > SIZE_MAX_BYTES)
    throw new SyncError(`図鑑のデータが大きすぎて配信できません（${Math.round(bytes / 1024)}KB）。パソコン側での対応が必要です`);
  if (bytes > SIZE_WARN_BYTES) console.warn(`警告: JSONが${(bytes / 1024).toFixed(0)}KBあり、1MiBのドキュメント上限に近づいています`);

  const before = new Set(curGames.map(keyOf));
  const after = new Set(canon.map(keyOf));
  const added = canon.filter((g) => !before.has(keyOf(g))).map((g) => g.title);
  const removed = curGames.filter((g) => !after.has(keyOf(g))).map((g) => g.title);
  console.log(`結果: ${canon.length}件（追加${added.length}・削除${removed.length}・詳細を読んだ${unknown.length}）` +
    `${changed ? ` → v${version}` : "（変更なし）"}${dryRun ? "（dryRun のため書き込みません）" : ""}`);

  if (!dryRun) await commit(db, { changed, version, count: canon.length, updated, hash, json, firstSeen });
  return { changed, version, count: canon.length, added, removed, details: unknown.length, dryRun };
}

module.exports = {
  BASE,
  SPACE_URL,
  EXTRA_URL_PREFIX,
  SIZE_WARN_BYTES,
  GAP_MAX_HOURS,
  MIN_KEEP_RATIO,
  SyncError,
  sleep,
  fetchHtml,
  scrapeList,
  fetchDetails,
  parseGenre,
  parseDesigner,
  byTitle,
  canonicalize,
  keyOf,
  fetchFavoriteCounts,
  applyPopularity,
  businessDay,
  applyFirstSeen,
  isExtra,
  loadExtras,
  extrasFromList,
  mergeExtras,
  contentHash,
  buildJson,
  commit,
  syncFromApp,
};
