using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 営業カレンダー（2026-11-10 ユーザー指定）。定休日（月・火・水。StoreCalendar）に、管理者が登録した
/// 臨時休業・臨時営業と、イベント（1日に複数）を重ねる。
///
/// Firestore: master/calendar = { days: { "2026-11-20": { status, note, events }, … }, updated_at, updated_by }
///   ・読めるのはログインしている人、書けるのは管理者だけ（master のルールのまま）
///   ・status: "closed"＝臨時休業（ふだん営業する日）／"open"＝臨時営業（定休日）／無し＝ふだんどおり。note はその理由（なくてもよい）
///   ・events: その日のイベントの名前の配列（登録した順・EVENTS_MAX 個まで）。イベントがある日は営業する日として扱う
///     （定休日でもイベントがあれば営業。ただし臨時休業にした日は休み）
///   ・その日の登録が何も無くなったら、日付ごと消す
///   ・ホームを開いたときに1回読み、お知らせを開くたびに読み直す（どちらも1回の読み取り）
///   ・最初の形（type: closed|open|event と note。2026-11-10 の数時間だけ）も読める
/// 使うところ:
///   ・お知らせの「営業カレンダー」と、カレンダーの画面（SpaceShopCalendar。管理者は日付を押して登録）
///   ・1か月パスの期限（StorePasses.ExpiryFor）: 期間中の臨時休業で使えなくなった日数だけ、使える日を後ろに足す
///   ・臨時休業の前日の夜の「明日は臨時休業です」（AppNotifications のスマホの中で予約する通知）
/// </summary>
public static class ShopCalendar
{
    public enum Status { None, Closed, Open }

    public sealed class Day
    {
        public DateTime Date;
        public Status Status;
        public string Note = "";                          // 臨時休業・臨時営業の理由（なくてもよい）
        public readonly List<string> Events = new List<string>(); // イベントの名前（登録した順）
        public bool IsEmpty => Status == Status.None && Events.Count == 0;
    }

    public const string T_REGULAR = "定休日";
    public const string T_CLOSED = "臨時休業";
    public const string T_OPEN = "臨時営業";
    public const string T_EVENT = "イベント";
    /// <summary>1日に登録できるイベントの数</summary>
    public const int EVENTS_MAX = 5;
    /// <summary>イベントの名前・理由の文字数</summary>
    public const int TEXT_MAX = 30;

    public static string GlyphText => T_REGULAR + T_CLOSED + T_OPEN + T_EVENT;

    private const string COLLECTION = "master", DOC = "calendar";
    private static readonly Dictionary<string, Day> _days = new Dictionary<string, Day>();
    private static bool _busy;

    /// <summary>一度でも読めたか（読めるまでは登録が無いものとして扱う）</summary>
    public static bool Loaded { get; private set; }

    /// <summary>読み込んだ・管理者が登録を変えたとき（メインスレッド）</summary>
    public static event Action Changed;

    private static DocumentReference Ref => FirebaseFirestore.DefaultInstance.Collection(COLLECTION).Document(DOC);

    private static string Uid => UserDataManager.instance != null ? UserDataManager.instance.UID : null;

    public static string Key(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ================================================================
    // 読み込み
    // ================================================================
    /// <summary>まだ読めていなければ読む（読み込み中ならそれを待つ）</summary>
    public static async UniTask EnsureLoadedAsync()
    {
        if (!Loaded) await LoadAsync();
    }

    /// <summary>読み直す（オフラインなら端末に残っている分）。読めたら true</summary>
    public static async UniTask<bool> LoadAsync()
    {
        if (_busy)
        {
            await UniTask.WaitUntil(() => !_busy); // 同じ UniTask を2回 await しないよう、終わるのを待つだけ
            return Loaded;
        }
        _busy = true;
        try
        {
            var snap = await Ref.GetSnapshotAsync().AsUniTask();
            _days.Clear();
            if (snap.Exists && snap.ToDictionary().TryGetValue("days", out var v) && v is IDictionary<string, object> map)
            {
                foreach (var kv in map)
                {
                    var day = Parse(kv.Key, kv.Value);
                    if (day != null) _days[kv.Key] = day;
                }
            }
            Loaded = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ShopCalendar] 営業カレンダーの読み込みに失敗: {ex.Message}");
            return false;
        }
        finally
        {
            _busy = false;
        }
        Changed?.Invoke();
        return true;
    }

    private static Day Parse(string key, object value)
    {
        if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        if (!(value is IDictionary<string, object> m)) return null;
        var day = new Day { Date = date.Date };
        string status = m.TryGetValue("status", out var s) ? s as string : null;
        day.Status = status == "closed" ? Status.Closed : status == "open" ? Status.Open : Status.None;
        day.Note = m.TryGetValue("note", out var n) ? n as string ?? "" : "";
        if (m.TryGetValue("events", out var ev) && ev is IEnumerable<object> list)
        {
            foreach (var e in list)
                if (e is string name && name.Trim().Length > 0 && !day.Events.Contains(name)) day.Events.Add(name);
        }
        // 最初の形（type と note）
        if (day.IsEmpty && m.TryGetValue("type", out var t) && t is string type)
        {
            if (type == "closed") day.Status = Status.Closed;
            else if (type == "open") day.Status = Status.Open;
            else if (type == "event" && day.Note.Length > 0)
            {
                day.Events.Add(day.Note);
                day.Note = "";
            }
        }
        return day.IsEmpty ? null : day;
    }

    private static string StatusId(Status s) => s == Status.Closed ? "closed" : "open";

    /// <summary>入力を整える（前後の空白・改行などを除き、TEXT_MAX 文字まで）</summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsControl(c) ? ' ' : c);
        string r = sb.ToString().Trim();
        return r.Length > TEXT_MAX ? r.Substring(0, TEXT_MAX) : r;
    }

    // ================================================================
    // 調べる
    // ================================================================
    /// <summary>その日の登録（無ければ null）</summary>
    public static Day Get(DateTime date) => _days.TryGetValue(Key(date.Date), out var d) ? d : null;

    /// <summary>臨時休業の登録が1つでもあるか（パスの期限の計算を省くため）</summary>
    public static bool HasClosures
    {
        get
        {
            foreach (var d in _days.Values)
                if (d.Status == Status.Closed) return true;
            return false;
        }
    }

    /// <summary>定休日（月・火・水）か。登録とは関係なく曜日だけで決まる</summary>
    public static bool IsRegularClosed(DateTime date) => StoreCalendar.IsClosed(date.DayOfWeek);

    /// <summary>
    /// 営業する日か: 臨時休業なら休み、臨時営業かイベントがあれば営業、登録が無ければ定休日かどうか
    /// </summary>
    public static bool IsOpen(DateTime date)
    {
        var d = Get(date);
        if (d != null)
        {
            if (d.Status == Status.Closed) return false;
            if (d.Status == Status.Open || d.Events.Count > 0) return true;
        }
        return !IsRegularClosed(date);
    }

    /// <summary>ふだんは営業する日の臨時休業か（定休日に入った「臨時休業」は数えない）</summary>
    public static bool IsTempClosed(DateTime date)
    {
        var d = Get(date);
        return d != null && d.Status == Status.Closed && !IsRegularClosed(date);
    }

    /// <summary>定休日に「臨時営業」を登録した日か（イベントだけで営業になる日は含めない）</summary>
    public static bool IsTempOpen(DateTime date)
    {
        var d = Get(date);
        return d != null && d.Status == Status.Open && IsRegularClosed(date);
    }

    /// <summary>from〜to（両端を含む）の登録（日付順）</summary>
    public static List<Day> Between(DateTime from, DateTime to)
    {
        var list = new List<Day>();
        foreach (var d in _days.Values)
            if (d.Date >= from.Date && d.Date <= to.Date) list.Add(d);
        list.Sort((a, b) => a.Date.CompareTo(b.Date));
        return list;
    }

    /// <summary>from から days 日先までの臨時休業（ふだんは営業する日のものだけ。日付順）</summary>
    public static List<Day> UpcomingClosures(DateTime from, int days)
    {
        var list = Between(from, from.AddDays(days));
        list.RemoveAll(d => d.Status != Status.Closed || IsRegularClosed(d.Date));
        return list;
    }

    // ================================================================
    // 管理者: 登録する・消す（ルールで管理者だけが書ける）
    // ================================================================
    /// <summary>
    /// お休み・営業の状態を決める（Closed＝臨時休業／Open＝臨時営業／None＝ふだんどおりに戻す）。note は理由。成功したら true
    /// </summary>
    public static async UniTask<bool> SetStatusAsync(DateTime date, Status status, string note)
    {
        string key = Key(date.Date);
        note = Clean(note);
        var cur = Get(date);
        try
        {
            if (status == Status.None)
            {
                if (cur == null || cur.Status == Status.None) return true;
                if (cur.Events.Count == 0) await DeleteDayAsync(key);
                else
                    await Ref.UpdateAsync(new Dictionary<FieldPath, object>
                    {
                        { new FieldPath("days", key, "status"), FieldValue.Delete },
                        { new FieldPath("days", key, "note"), FieldValue.Delete },
                        { new FieldPath("updated_at"), FieldValue.ServerTimestamp },
                        { new FieldPath("updated_by"), Uid ?? "" },
                    }).AsUniTask();
            }
            else
            {
                // days の中のその日の status・note だけを書き換える（ほかの日・その日のイベントはそのまま）
                await Ref.SetAsync(new Dictionary<string, object>
                {
                    {
                        "days", new Dictionary<string, object>
                        {
                            { key, new Dictionary<string, object> { { "status", StatusId(status) }, { "note", note } } },
                        }
                    },
                    { "updated_at", FieldValue.ServerTimestamp },
                    { "updated_by", Uid ?? "" },
                }, SetOptions.MergeAll).AsUniTask();
            }
            var day = cur ?? new Day { Date = date.Date };
            day.Status = status;
            day.Note = status == Status.None ? "" : note;
            if (day.IsEmpty) _days.Remove(key);
            else _days[key] = day;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ShopCalendar] お休み・営業の登録に失敗 ({key}): {ex.Message}");
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>イベントを1つ足す（同じ名前は1つだけ）。成功したら true</summary>
    public static async UniTask<bool> AddEventAsync(DateTime date, string name)
    {
        string key = Key(date.Date);
        name = Clean(name);
        if (name.Length == 0) return false;
        try
        {
            await Ref.SetAsync(new Dictionary<string, object>
            {
                {
                    "days", new Dictionary<string, object>
                    {
                        { key, new Dictionary<string, object> { { "events", FieldValue.ArrayUnion(name) } } },
                    }
                },
                { "updated_at", FieldValue.ServerTimestamp },
                { "updated_by", Uid ?? "" },
            }, SetOptions.MergeAll).AsUniTask();
            var day = Get(date) ?? new Day { Date = date.Date };
            if (!day.Events.Contains(name)) day.Events.Add(name);
            _days[key] = day;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ShopCalendar] イベントの登録に失敗 ({key}): {ex.Message}");
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>イベントを1つ消す。成功したら true</summary>
    public static async UniTask<bool> RemoveEventAsync(DateTime date, string name)
    {
        string key = Key(date.Date);
        var cur = Get(date);
        if (cur == null || !cur.Events.Contains(name)) return true;
        try
        {
            if (cur.Status == Status.None && cur.Events.Count == 1) await DeleteDayAsync(key);
            else
                await Ref.UpdateAsync(new Dictionary<FieldPath, object>
                {
                    { new FieldPath("days", key, "events"), FieldValue.ArrayRemove(name) },
                    { new FieldPath("updated_at"), FieldValue.ServerTimestamp },
                    { new FieldPath("updated_by"), Uid ?? "" },
                }).AsUniTask();
            cur.Events.RemoveAll(e => e == name);
            if (cur.IsEmpty) _days.Remove(key);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ShopCalendar] イベントを消せませんでした ({key}): {ex.Message}");
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>その日の登録を日付ごと消す</summary>
    private static async UniTask DeleteDayAsync(string key)
    {
        await Ref.UpdateAsync(new Dictionary<FieldPath, object>
        {
            { new FieldPath("days", key), FieldValue.Delete },
            { new FieldPath("updated_at"), FieldValue.ServerTimestamp },
            { new FieldPath("updated_by"), Uid ?? "" },
        }).AsUniTask();
    }
}
