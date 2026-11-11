using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ダイスチャレンジ（2026-11-11 ユーザー指定）。さいころ座が完成した人（管理者は初めから）が1日1回だけ 1D100 を振り、
/// その日のランキングに載る。ランキングに名前は出さず「ユーザーA」「ユーザーB」…（その日に振った順）で、上位10位まで見せる。
/// 毎朝4時（営業日の変わり目）にリセット。その日いちばん大きい目を出した人（同じ目なら全員）は、次にアプリを開いたとき
/// 5%OFFチケットを受け取る（「〇月〇日にダイストップで優勝しました！」のお知らせ。NewHomeController の贈りもの）。
///
/// Firestore:
///   dice_daily/{営業日}/rolls/{記録のID} = { v: 出目, t: 振った時刻（サーバー） }
///     記録のID = SHA-256(UID + "|" + 営業日) の16進（小文字）。名前もUIDも入れない（だれの記録かは本人にしかわからない）。
///     作れるのは自分のIDの記録を、その営業日のうちに1回だけ。あとから変えられない（tools/firestore/firestore.rules）
///   users/{uid}.dice_day / dice_value … 自分が振った営業日と出目（自分の今日の出目・1日1回の目安）
///   users/{uid}.dice_claimed … 結果を確かめ終えた営業日（優勝のチケットを二重に受け取らない）
/// 出目は「振る」を押したときに決めて端末に保存してから3Dのダイスを転がす（途中で閉じても出目は変わらず、
/// 次に開いたときに送る。振り直しはできない）。
/// 振れるのは入店中だけ（2026-11-11 ユーザー指定。INTALES WORLD と同じ JourneyService.InStoreToday。振ったあとに退店しても
/// 送れていない出目は送れる）。1日1回は管理者も同じ（2026-11-11 ユーザー指定。管理者の例外は「さいころ座が未完成でも
/// 画面を開ける」ことだけで、回数・入店中の決まりは同じ。サーバーのルールでも管理者は1日1件しか作れない）。
/// </summary>
public static class DiceChallenge
{
    public const string COLLECTION = "dice_daily";
    public const string ROLLS = "rolls";
    public const string FIELD_DAY = "dice_day";
    public const string FIELD_VALUE = "dice_value";
    public const string FIELD_CLAIMED = "dice_claimed";
    /// <summary>優勝のプレゼント（users のフィールド名）</summary>
    public const string REWARD_FIELD = "five_coupon";
    /// <summary>ランキングに出す順位まで</summary>
    public const int TOP = 10;
    /// <summary>履歴の種類（SpaceHistoryPanel）</summary>
    public const string HISTORY = "dicechallenge";

    public const string T_NAME = "ユーザー";
    public const string T_WIN_CAPTION = "DICE TOP";
    private const string T_WIN_TITLE = "にダイストップで優勝しました！";
    private const string T_WIN_BODY_A = "1D100 で ";
    private const string T_WIN_BODY_B = " を出して、その日いちばん大きい目でした";
    public const string T_WIN_GET = " をGET！";
    private const string T_HISTORY_ROLL_A = "ダイスチャレンジで ";
    private const string T_HISTORY_ROLL_B = " を出した";
    private const string T_HISTORY_WIN = "のダイストップ";

    /// <summary>優勝のお知らせ・履歴で使う文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => T_NAME + T_WIN_CAPTION + T_WIN_TITLE + T_WIN_BODY_A + T_WIN_BODY_B + T_WIN_GET
                                      + T_HISTORY_ROLL_A + T_HISTORY_ROLL_B + T_HISTORY_WIN
                                      + "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789月日を1枚入手（）";

    private const string PREF_PENDING = "dice_challenge_pending"; // "UID|営業日|出目"（振ったが、まだ送れていない）
    private const float RETRY_SECONDS = 60f;

    private static UserDataManager Manager => UserDataManager.instance;
    private static string Uid => Manager != null ? Manager.UID : null;
    private static UserData Data => Manager != null ? Manager.UserData : null;
    private static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;

    /// <summary>今日（営業日。朝4時に切り替わる）</summary>
    public static string Today => StoreCalendar.TodayKey;

    /// <summary>ダイスチャレンジができるか（さいころ座が完成している。管理者は初めから）</summary>
    public static bool Unlocked
    {
        get
        {
            if (AdminClaim.IsAdmin) return true;
            var p = Constellations.Progress().Find(x => x.Def.Id == SpaceDiceRoller.CONSTELLATION);
            return p != null && p.Complete;
        }
    }

    /// <summary>その日の自分の記録のID（SHA-256(UID|営業日) の16進・小文字。ルールと同じ計算）</summary>
    public static string RollId(string uid, string day)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(uid + "|" + day));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    /// <summary>今日もう振って、記録を送れている</summary>
    public static bool RolledToday
    {
        get
        {
            var d = Data;
            return d != null && d.DiceDay == Today && d.DiceValue > 0;
        }
    }

    /// <summary>振ったが、まだ送れていない今日の出目（なければ0）</summary>
    public static int PendingValue
    {
        get
        {
            string s = PlayerPrefs.GetString(PREF_PENDING, "");
            if (string.IsNullOrEmpty(s)) return 0;
            var parts = s.Split('|');
            if (parts.Length != 3 || parts[0] != Uid || parts[1] != Today) return 0;
            return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 1 && v <= 100
                ? v
                : 0;
        }
    }

    /// <summary>今日の自分の出目（送る前の出目も含む。まだ振っていなければ0）</summary>
    public static int TodayValue => RolledToday ? Data.DiceValue : PendingValue;

    /// <summary>入店中か（振れるのは入店中だけ。管理者も同じ）</summary>
    public static bool InStore => JourneyService.InStoreToday;

    /// <summary>いま振れるか（入店中で、今日まだ振っていない。1日1回は管理者も同じ）</summary>
    public static bool CanRoll => InStore && TodayValue <= 0;

    /// <summary>
    /// 今日の出目を決めて端末に保存する（「振る」を押したとき）。もう決まっていれば同じ出目。
    /// 振れないとき（入店していない・今日もう送った）は 0。
    /// 3Dのダイスはこの出目が出るように見せる（Dice3DSimulator.RollPercentileFixedAsync）
    /// </summary>
    public static int Decide()
    {
        int pending = PendingValue;
        if (pending > 0) return pending;
        if (!CanRoll) return 0;
        int v = UnityEngine.Random.Range(1, 101);
        PlayerPrefs.SetString(PREF_PENDING, Uid + "|" + Today + "|" + v.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
        return v;
    }

    public enum SubmitResult { Ok, Already, Failed }

    /// <summary>送る前の出目があれば送る（新ホームを開いたとき。途中で閉じたまま、ダイスの画面を開かなかった人のため）</summary>
    public static async UniTaskVoid SubmitPendingAsync()
    {
        if (PendingValue <= 0 || RolledToday) return;
        await SubmitAsync();
    }

    /// <summary>
    /// 決めた出目を送る（自分の記録と users の dice_day・dice_value を1回の書き込みで）。
    /// ほかの端末で今日もう振っていたら（記録がもうある）、その出目にそろえて Already
    /// </summary>
    public static async UniTask<SubmitResult> SubmitAsync()
    {
        int v = PendingValue;
        string uid = Uid, day = Today;
        if (v <= 0 || string.IsNullOrEmpty(uid)) return SubmitResult.Failed;
        var rollRef = Db.Collection(COLLECTION).Document(day).Collection(ROLLS).Document(RollId(uid, day));
        var userRef = Db.Collection("users").Document(uid);
        try
        {
            var batch = Db.StartBatch();
            batch.Set(rollRef, new Dictionary<string, object> { { "v", v }, { "t", FieldValue.ServerTimestamp } });
            batch.Update(userRef, new Dictionary<string, object> { { FIELD_DAY, day }, { FIELD_VALUE, v } });
            await batch.CommitAsync().AsUniTask();
            ApplyLocal(day, v);
            LocalHistoryLog.Add(HISTORY, T_HISTORY_ROLL_A + v + T_HISTORY_ROLL_B);
            Debug.Log($"[DiceChallenge] {day} の出目 {v} を送りました");
            return SubmitResult.Ok;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DiceChallenge] 出目を送れませんでした: {ex.Message}");
        }

        // 記録がもうある（ほかの端末で振った）なら、その出目にそろえる
        try
        {
            var snap = await rollRef.GetSnapshotAsync().AsUniTask();
            int existing = snap.Exists ? Int(snap.ToDictionary(), "v") : 0;
            if (existing > 0)
            {
                try
                {
                    await userRef.UpdateAsync(new Dictionary<string, object> { { FIELD_DAY, day }, { FIELD_VALUE, existing } })
                        .AsUniTask();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DiceChallenge] 出目の記録をそろえられませんでした: {ex.Message}");
                }
                ApplyLocal(day, existing);
                return SubmitResult.Already;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DiceChallenge] 記録を確かめられませんでした: {ex.Message}");
        }
        return SubmitResult.Failed;
    }

    private static void ApplyLocal(string day, int v)
    {
        var d = Data;
        if (d != null)
        {
            d.DiceDay = day;
            d.DiceValue = v;
        }
        PlayerPrefs.DeleteKey(PREF_PENDING);
        PlayerPrefs.Save();
    }

    // ================================================================
    // ランキング（ダイスの画面のチャレンジを開いている間だけ購読）
    // ================================================================
    /// <summary>ランキングの1行</summary>
    public sealed class Entry
    {
        public string Name;  // 「ユーザーA」など（その日に振った順）
        public int Value;
        public int Rank;     // 同じ目は同じ順位
        public bool Mine;
    }

    /// <summary>その日のランキング</summary>
    public sealed class Board
    {
        public string Day;
        public readonly List<Entry> Top = new List<Entry>(); // 上から最大10件
        public Entry Mine;   // 自分（まだ振っていなければ null）
        public int Count;    // 振った人数
        public int Max;      // いちばん大きい目（だれもいなければ 0）
    }

    /// <summary>いま購読しているランキング（まだ届いていなければ null）</summary>
    public static Board Current { get; private set; }

    /// <summary>ランキングが変わった（だれかが振った。購読中・メインスレッド）</summary>
    public static event Action Changed;

    private static ListenerRegistration _listener;
    private static string _listenDay;

    /// <summary>購読している営業日（していなければ null）</summary>
    public static string ListeningDay => _listenDay;

    public static void StartListening()
    {
        string day = Today;
        if (_listener != null && _listenDay == day) return;
        StopListening();
        _listenDay = day;
        string myId = string.IsNullOrEmpty(Uid) ? null : RollId(Uid, day);
        _listener = Db.Collection(COLLECTION).Document(day).Collection(ROLLS).Listen(snap =>
        {
            if (_listenDay != day) return;
            Current = Build(day, snap, myId);
            // ほかの端末で今日もう振っていた（この端末の記録が古い）ときは、表示だけ合わせる
            var d = Data;
            if (Current.Mine != null && d != null && d.DiceDay != day)
            {
                d.DiceDay = day;
                d.DiceValue = Current.Mine.Value;
            }
            Changed?.Invoke();
        });
    }

    public static void StopListening()
    {
        _listener?.Stop();
        _listener = null;
        _listenDay = null;
    }

    private static Board Build(string day, QuerySnapshot snap, string myId)
    {
        var rows = new List<(string Id, int V, DateTime T)>();
        foreach (var doc in snap.Documents)
        {
            var d = doc.ToDictionary(ServerTimestampBehavior.Estimate);
            int v = Int(d, "v");
            if (v < 1 || v > 100) continue;
            var t = d.TryGetValue("t", out var o) && o is Timestamp ts ? ts.ToDateTime() : DateTime.MaxValue;
            rows.Add((doc.Id, v, t));
        }
        // 名前: その日に振った順に A, B, C …（同じ時刻は ID の順）
        rows.Sort((a, b) =>
        {
            int c = a.T.CompareTo(b.T);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        var names = new Dictionary<string, string>();
        for (int i = 0; i < rows.Count; i++) names[rows[i].Id] = T_NAME + Letters(i);

        // 順位: 出目の大きい順（同じ目は同じ順位。並びは先に振った人が上）
        var byValue = new List<(string Id, int V, DateTime T)>(rows);
        byValue.Sort((a, b) =>
        {
            int c = b.V.CompareTo(a.V);
            if (c != 0) return c;
            c = a.T.CompareTo(b.T);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        var board = new Board { Day = day, Count = rows.Count, Max = byValue.Count > 0 ? byValue[0].V : 0 };
        int rank = 0;
        for (int i = 0; i < byValue.Count; i++)
        {
            if (i == 0 || byValue[i].V != byValue[i - 1].V) rank = i + 1;
            var e = new Entry { Name = names[byValue[i].Id], Value = byValue[i].V, Rank = rank, Mine = byValue[i].Id == myId };
            if (i < TOP) board.Top.Add(e);
            if (e.Mine) board.Mine = e;
        }
        return board;
    }

    /// <summary>0→A, 1→B, … 25→Z, 26→AA, 27→AB …</summary>
    private static string Letters(int index)
    {
        var sb = new StringBuilder();
        int n = index + 1;
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)('A' + n % 26));
            n /= 26;
        }
        return sb.ToString();
    }

    // ================================================================
    // 優勝のプレゼント（終わった日の結果を、次にアプリを開いたときに確かめる）
    // ================================================================
    private static float _retryAt;

    /// <summary>まだ結果を確かめていない、終わった日の自分の記録がある</summary>
    public static bool ClaimDue
    {
        get
        {
            var d = Data;
            if (d == null || string.IsNullOrEmpty(Uid)) return false;
            if (string.IsNullOrEmpty(d.DiceDay) || d.DiceValue <= 0) return false;
            if (string.CompareOrdinal(d.DiceDay, Today) >= 0) return false; // その日はまだ終わっていない
            return d.DiceClaimed != d.DiceDay;
        }
    }

    /// <summary>いま確かめに行ってよいか（失敗したら少し待ってから。毎秒の見回りから呼ばれるため）</summary>
    public static bool ClaimReady => ClaimDue && Time.realtimeSinceStartup >= _retryAt;

    public struct Win
    {
        public string Day;  // 優勝した営業日 "yyyy-MM-dd"
        public int Value;   // その日の出目
    }

    /// <summary>
    /// 終わった日の結果を確かめる。その日いちばん大きい目（同じ目なら全員）なら5%OFFチケットを1枚受け取る
    /// （users の dice_claimed と同じ1回の書き込み。同じ日は二重に受け取らない）。優勝して受け取れたら Win、それ以外は null
    /// </summary>
    public static async UniTask<Win?> ClaimAsync()
    {
        if (!ClaimDue) return null;
        var data = Data;
        string uid = Uid, day = data.DiceDay;
        try
        {
            var rolls = Db.Collection(COLLECTION).Document(day).Collection(ROLLS);
            var top = await rolls.OrderByDescending("v").Limit(1).GetSnapshotAsync().AsUniTask();
            int max = 0;
            foreach (var doc in top.Documents) max = Math.Max(max, Int(doc.ToDictionary(), "v"));
            // 自分の出目は、ランキングに載った記録で確かめる
            var mineSnap = await rolls.Document(RollId(uid, day)).GetSnapshotAsync().AsUniTask();
            int mine = mineSnap.Exists ? Int(mineSnap.ToDictionary(), "v") : 0;
            bool won = mine > 0 && mine >= max;

            var userRef = Db.Collection("users").Document(uid);
            bool granted = await Db.RunTransactionAsync(tx => tx.GetSnapshotAsync(userRef).ContinueWith(t =>
            {
                var u = t.Result != null && t.Result.Exists ? t.Result.ToDictionary() : new Dictionary<string, object>();
                if (u.TryGetValue(FIELD_CLAIMED, out var c) && c as string == day) return false; // ほかの端末で受け取り済み
                var updates = new Dictionary<string, object> { { FIELD_CLAIMED, day } };
                if (won) updates[REWARD_FIELD] = FieldValue.Increment(1);
                tx.Update(userRef, updates);
                return true;
            })).AsUniTask();

            data.DiceClaimed = day;
            if (!granted || !won) return null;
            var coupon = SpaceCoupons.ByField(REWARD_FIELD);
            coupon?.Add(data, 1);
            LocalHistoryLog.Add("coupon", $"{SpaceCoupons.NameOf(REWARD_FIELD)} を1枚 入手（{DayLabel(day)}{T_HISTORY_WIN}）");
            Debug.Log($"[DiceChallenge] {day} に優勝（{mine}）。{SpaceCoupons.NameOf(REWARD_FIELD)}を受け取りました");
            return new Win { Day = day, Value = mine };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DiceChallenge] 結果を確かめられませんでした: {ex.Message}");
            _retryAt = Time.realtimeSinceStartup + RETRY_SECONDS;
            return null;
        }
    }

    /// <summary>優勝のお知らせの見出し「11月11日にダイストップで優勝しました！」</summary>
    public static string WinTitle(Win w) => DayLabel(w.Day) + T_WIN_TITLE;

    /// <summary>優勝のお知らせの本文「1D100 で 98 を出して、その日いちばん大きい目でした」</summary>
    public static string WinBody(Win w) => T_WIN_BODY_A + w.Value + T_WIN_BODY_B;

    /// <summary>"yyyy-MM-dd" → 「11月11日」</summary>
    public static string DayLabel(string day) =>
        DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? $"{d.Month}月{d.Day}日"
            : day;

    private static int Int(IDictionary<string, object> d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }
}
