using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ボードゲームの「公認」（お店が認めた、遊んだ証拠。2026-11-06 ユーザー決定「案B」）。
/// 図鑑の ✓（遊んだ）は自分で押せるので報酬の根拠にならない。公認はお店のスタッフが見せるQRを読んだときだけ付く。
///  ・管理者の「お客様に見せるQRコード」の「ゲーム」タブでゲームを選ぶと、そのゲームの公認QRが出る。
///    同じ卓の人は全員読める（読むたびに書き込むのは読んだ人のアカウントだけ）
///  ・中身: "play:{ゲームキー}:{営業日 yyyyMMdd}:{署名12桁}"。その営業日（朝4時切り替え）のうちだけ使える
///    （前の日に撮った写真では公認されない）。署名の鍵はアプリの中にあるので、入店QRと同じ程度の対策
///  ・保存先: users/{uid}.certified_boardgames = { ゲームキー: "yyyy-MM-dd"（公認された営業日） }。
///    同時に ✓（played_boardgames）も付ける。users は本人が書けるルールなのでルール変更は不要
///  ・公認されたゲームで星座（Constellations）が光る。星を灯せるのは1日1回まで。
///    達成済みのゲームでも、別の日にまた公認QRを読めば同じ星座・別の星座にもう一度灯せる（2026-11-09 ユーザー指定）
/// </summary>
public static class BoardGameCertify
{
    public const string QR_PREFIX = "play:";
    public const string FIELD = "certified_boardgames";
    /// <summary>
    /// そのゲームが星を灯した星座（ゲームキー → ["星座ID@yyyy-MM-dd", …]。灯した順）。
    /// 複数の星座に当てはまるゲームはお客様が選ぶ。灯した星座が完成すると、同じゲームでまだ灯していない別の星座にも
    /// もう一度灯せる（2026-11-06 ユーザー指定）。以前の記録（"星座ID" の文字だけ）も読める
    /// </summary>
    public const string FIELD_STAR = "certified_constellation";
    private const string DAY_FORMAT = "yyyyMMdd";
    private const string SALT = "intales/boardgame-certify/2026-11";

    public const string T_MARK = "アチーブメント"; // 画面での呼び名（2026-11-06 ユーザー指定で「公認」から変更）
    public const string T_INVALID = "このQRコードは使えません";
    public const string T_EXPIRED = "このQRコードは今日のものではありません";
    public const string T_FAILED = "アチーブメントを記録できませんでした。通信状況を確かめて、もう一度読み取ってください";
    public const string T_ALREADY = "このゲームのアチーブメントは達成済みです";
    public const string T_TODAY_DONE = "今日の星はもう灯しました";
    public const string T_TODAY_DONE_SUB = "星座の星を灯せるのは1日1回までです。また来店したときに、同じゲームでも灯せます";
    public const string T_NO_CONSTELLATION = "このゲームのジャンルには星座がありません";

    public static string GlyphText => T_MARK + T_INVALID + T_EXPIRED + T_FAILED + T_ALREADY + T_TODAY_DONE
                                      + T_TODAY_DONE_SUB + T_NO_CONSTELLATION;

    /// <summary>
    /// 今日（営業日）もう星座の星を灯したか（2026-11-09 ユーザー指定: 星を灯すのは1日1回まで）。
    /// 星の記録がない古いデータは「当てはまる最初の星座に、達成日に灯った」とみなしているので、それも数える
    /// </summary>
    public static bool LitToday
    {
        get
        {
            string today = StoreCalendar.TodayBusiness.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var kv in All)
                foreach (var (_, day) in Constellations.LitBy(kv.Key, Find(kv.Key), kv.Value))
                    if (day == today) return true;
            return false;
        }
    }

    public enum Result { Ok, Invalid, Expired }

    // ================================================================
    // QR
    // ================================================================
    /// <summary>今日の営業日の、そのゲームの公認QRの中身</summary>
    public static string Payload(BoardGameEntry g) => Payload(BoardGameMarks.KeyOf(g), StoreCalendar.TodayBusiness);

    public static string Payload(string key, DateTime businessDate)
    {
        string day = businessDate.ToString(DAY_FORMAT, CultureInfo.InvariantCulture);
        return QR_PREFIX + key + ":" + day + ":" + Sign(key, day);
    }

    /// <summary>読み取ったQRを確かめる。今日の営業日の正しいQRなら Ok（key はゲームキー）</summary>
    public static Result Parse(string text, DateTime nowUtc, out string key)
    {
        key = null;
        if (string.IsNullOrEmpty(text)) return Result.Invalid;
        text = text.Trim();
        if (!text.StartsWith(QR_PREFIX, StringComparison.Ordinal)) return Result.Invalid;
        string body = text.Substring(QR_PREFIX.Length);
        // キーに ':' が入っていても読めるよう、後ろの2つ（営業日・署名）から切り出す
        int sigAt = body.LastIndexOf(':');
        if (sigAt <= 0) return Result.Invalid;
        int dayAt = body.LastIndexOf(':', sigAt - 1);
        if (dayAt <= 0) return Result.Invalid;
        string k = body.Substring(0, dayAt);
        string dayText = body.Substring(dayAt + 1, sigAt - dayAt - 1);
        string sig = body.Substring(sigAt + 1);
        if (!DateTime.TryParseExact(dayText, DAY_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Result.Invalid;
        if (!string.Equals(sig, Sign(k, dayText), StringComparison.OrdinalIgnoreCase)) return Result.Invalid;
        if (day.Date != StoreCalendar.BusinessDate(StoreCalendar.ToJst(nowUtc.ToUniversalTime()))) return Result.Expired;
        key = k;
        return Result.Ok;
    }

    private static string Sign(string key, string day)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(SALT + "|" + key + "|" + day));
            var sb = new StringBuilder(12);
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    // ================================================================
    // 記録
    // ================================================================
    private static Dictionary<string, object> Map =>
        UserDataManager.instance != null && UserDataManager.instance.UserData != null
            ? UserDataManager.instance.UserData.CertifiedMap
            : null;

    public static bool IsCertified(BoardGameEntry g) => IsCertified(BoardGameMarks.KeyOf(g));

    public static bool IsCertified(string key)
    {
        var m = Map;
        return m != null && !string.IsNullOrEmpty(key) && m.ContainsKey(key);
    }

    /// <summary>公認された営業日（分からなければ false）</summary>
    public static bool TryCertifiedOn(BoardGameEntry g, out DateTime day)
    {
        day = default;
        var m = Map;
        string key = BoardGameMarks.KeyOf(g);
        return m != null && !string.IsNullOrEmpty(key) && m.TryGetValue(key, out var v) && v is string s
               && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    public static int Count => Map?.Count ?? 0;

    /// <summary>そのゲームが星を灯した星座（灯した順。ID と灯した営業日 "yyyy-MM-dd"）。記録がなければ空</summary>
    public static List<(string Id, string Day)> StarsOf(string key)
    {
        var list = new List<(string, string)>();
        var data = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        if (data == null || string.IsNullOrEmpty(key)) return list;
        if (!data.CertifiedStarMap.TryGetValue(key, out var v) || v == null) return list;
        string certDay = data.CertifiedMap.TryGetValue(key, out var d) ? d as string ?? "" : "";
        if (v is string one)
        {
            if (one.Length > 0) list.Add(Split(one, certDay)); // 以前の記録（星座IDだけ）
            return list;
        }
        if (v is System.Collections.IEnumerable items)
            foreach (var item in items)
                if (item is string s && s.Length > 0) list.Add(Split(s, certDay));
        return list;
    }

    private static (string, string) Split(string entry, string fallbackDay)
    {
        int at = entry.IndexOf('@');
        return at < 0 ? (entry, fallbackDay) : (entry.Substring(0, at), entry.Substring(at + 1));
    }

    /// <summary>公認されたゲームキーと日付（"yyyy-MM-dd"）</summary>
    public static IEnumerable<KeyValuePair<string, string>> All
    {
        get
        {
            var m = Map;
            if (m == null) yield break;
            foreach (var kv in m) yield return new KeyValuePair<string, string>(kv.Key, kv.Value as string ?? "");
        }
    }

    /// <summary>
    /// 公認を書き込む（certified_boardgames.{キー} と ✓。星座が完成・レベルアップするなら、その星座のチケットも。
    /// rewards = チケットのフィールド名 → 枚数。星座が1つ上がるごとに1枚: ふつうはドリンク、職人座は7%OFF）。
    /// 書き込みは1回なので、公認とチケットは必ず一緒に入る。メモリ上の UserData にも反映する
    /// </summary>
    /// relight = 達成済みのゲームで、もう一度灯す（達成日は変えず、灯した星座の記録に足すだけ。同じ星座でもよい）
    public static async UniTask CertifyAsync(string key, string starId, IReadOnlyDictionary<string, int> rewards,
        bool relight = false)
    {
        var mgr = UserDataManager.instance;
        string uid = mgr != null ? mgr.UID : null;
        if (string.IsNullOrEmpty(uid)) throw new InvalidOperationException("UID未設定");

        string day = StoreCalendar.TodayBusiness.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var data0 = mgr.UserData;
        var docRef = FirebaseFirestore.DefaultInstance.Collection("users").Document(uid);
        var updates = new Dictionary<FieldPath, object>
        {
            { new FieldPath("played_boardgames"), FieldValue.ArrayUnion(key) },
            { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
        };
        if (!relight) updates[new FieldPath(FIELD, key)] = day;
        // 灯した星座の記録は、いままでの分＋今回の分をまとめて書く（以前の文字だけの記録も配列に直す）
        var stars = new List<object>();
        if (relight)
        {
            // いま灯っている星座（記録がない古いデータは「当てはまる最初の星座」に灯っているとみなしている）を残す
            string certDay = data0.CertifiedMap.TryGetValue(key, out var cd) ? cd as string ?? day : day;
            foreach (var (def, d) in Constellations.LitBy(key, Find(key), certDay)) stars.Add(def.Id + "@" + d);
        }
        if (!string.IsNullOrEmpty(starId)) stars.Add(starId + "@" + day);
        if (stars.Count > 0) updates[new FieldPath(FIELD_STAR, key)] = stars;
        if (rewards != null)
            foreach (var kv in rewards)
                if (kv.Value > 0) updates[new FieldPath(kv.Key)] = FieldValue.Increment(kv.Value);
        await docRef.UpdateAsync(updates).AsUniTask();

        var data = mgr.UserData;
        if (data.CertifiedMap == null) data.CertifiedMap = new Dictionary<string, object>();
        if (!relight) data.CertifiedMap[key] = day;
        if (stars.Count > 0)
        {
            if (data.CertifiedStarMap == null) data.CertifiedStarMap = new Dictionary<string, object>();
            data.CertifiedStarMap[key] = stars;
        }
        BoardGameMarks.NotePlayed(key); // 図鑑の「✓ 遊んだ！」もすぐ付く（2026-11-06 ユーザー指定）
        if (rewards != null)
            foreach (var kv in rewards)
            {
                var coupon = SpaceCoupons.ByField(kv.Key);
                if (coupon != null && kv.Value > 0) coupon.Add(data, kv.Value);
            }
        Debug.Log($"[Certify] 公認: {key} ({day}) チケット: {(rewards == null ? "" : string.Join(",", rewards))}");
    }

    // ================================================================
    // ゲームの一覧
    // ================================================================
    private static BoardGameCatalogData _catalogFrom;
    private static Dictionary<string, BoardGameEntry> _byKey;

    /// <summary>キーからゲームを探す（端末のゲーム一覧に無ければ null）</summary>
    public static BoardGameEntry Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var data = BoardGameCatalog.Load();
        if (_byKey == null || !ReferenceEquals(_catalogFrom, data))
        {
            _catalogFrom = data;
            _byKey = new Dictionary<string, BoardGameEntry>();
            if (data?.games != null)
                foreach (var g in data.games)
                {
                    string k = BoardGameMarks.KeyOf(g);
                    if (!string.IsNullOrEmpty(k) && !_byKey.ContainsKey(k)) _byKey[k] = g;
                }
        }
        return _byKey.TryGetValue(key, out var e) ? e : null;
    }
}
