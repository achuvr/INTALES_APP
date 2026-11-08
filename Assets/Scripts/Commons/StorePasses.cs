using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>1か月遊び放題パスの種類（Firestore の type と、カード・一覧の表記）</summary>
public sealed class PassTypeDef
{
    public string Id;    // Firestore の type（"weekday" / "weekend" / "allday"）
    public string Label; // 「平日のみ」「土日のみ」「ALL DAY」
    public string En;    // カードの英字の名前
}

/// <summary>パス1枚（受け取る前の、発行しただけのものも含む）</summary>
public sealed class PassInfo
{
    public string Id;
    public PassTypeDef Type;
    public bool Claimed;
    public string HolderUid, HolderName; // 受け取った人（名前は受け取ったときのユーザー名）
    public DateTime IssuedAt;            // 受け取りQRを発行した時刻
    public DateTime Start;               // 購入日（受け取った日）
    public DateTime Expiry;              // 最終日（購入日を1日目として30日目）

    public bool IsActive(DateTime now) => Claimed && now.Date <= Expiry.Date;

    /// <summary>今日を含めた残り日数（最終日なら1）</summary>
    public int DaysLeft(DateTime now) => (Expiry.Date - now.Date).Days + 1;
}

public enum PassClaimResult { Ok, AlreadyMine, Taken, Expired, NotFound, Failed }

/// <summary>
/// 1か月遊び放題パス（店頭で購入 → 管理者が受け取りQRを表示 → お客様のアプリで読み取って受け取る）。
///
/// Firestore: passes/{ランダムなID} = { type, issued_at, issued_by, claimed_by, claimed_at, holder_name }
///   ・管理者が種類を選ぶたびに1枚発行する（受け取る前の形）。QRの中身は "pass:{ID}"
///   ・1回だけ、発行から CLAIM_MINUTES 分以内に受け取れる。受け取ると claimed_by に自分のUID、
///     claimed_at にサーバーの時刻、holder_name に自分のユーザー名が入る（購入者一覧に出る）
///   ・有効期間は受け取った日を1日目として DAYS 日間（日付はこの端末の暦で計算する）
///   ・パスはユーザーごとのデータ（本人が自由に書ける users）ではなくここに置き、
///     受け取り・発行・取り消しの形はルール（tools/firestore/firestore.rules の passes）で決めている
///   ・受け取られたパスは消せない。受け取られなかった受け取りQRは、管理者が閉じたときに取り消す
/// </summary>
public static class StorePasses
{
    public const string QR_PREFIX = "pass:";
    public const int DAYS = 30;          // 購入日を1日目として30日間
    public const int CLAIM_MINUTES = 60; // 受け取りQRが使える時間（ルールと同じ）
    private const string COLLECTION = "passes";

    public const string T_NAME = "1か月遊び放題パス";
    public const string T_SHORT = "1か月パス";
    private const string T_TAKEN = "このパスはすでに受け取られています";
    private const string T_EXPIRED_QR = "このQRコードは期限切れです。スタッフにお声がけください";
    private const string T_INVALID = "このQRコードは使えません（取り消されたか、正しくないQRです）";
    private const string T_FAILED = "通信に失敗しました。もう一度読み取ってください";

    /// <summary>受け取れなかったときの知らせ</summary>
    public static string ClaimMessage(PassClaimResult r) => r switch
    {
        PassClaimResult.Taken => T_TAKEN,
        PassClaimResult.Expired => T_EXPIRED_QR,
        PassClaimResult.NotFound => T_INVALID,
        _ => T_FAILED,
    };

    /// <summary>パスまわりで出しうる文字（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText =>
        T_NAME + T_SHORT + T_TAKEN + T_EXPIRED_QR + T_INVALID + T_FAILED + "受け取り済みパスをまで遊び放題です！「」";

    public static readonly PassTypeDef Weekday = new PassTypeDef { Id = "weekday", Label = "平日のみ", En = "WEEKDAY PASS" };
    public static readonly PassTypeDef Weekend = new PassTypeDef { Id = "weekend", Label = "土日のみ", En = "WEEKEND PASS" };
    public static readonly PassTypeDef AllDay = new PassTypeDef { Id = "allday", Label = "ALL DAY", En = "ALL DAY PASS" };

    /// <summary>並び順（管理者の一覧・タブ）。ALL DAY がいちばん豪華</summary>
    public static readonly PassTypeDef[] Types = { Weekday, Weekend, AllDay };

    public static PassTypeDef FindType(string id)
    {
        foreach (var t in Types)
            if (t.Id == id) return t;
        return null;
    }

    /// <summary>"2026/11/05"</summary>
    public static string DateText(DateTime d) => d.ToString("yyyy/MM/dd");

    private static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;
    private static CollectionReference Col => Db.Collection(COLLECTION);

    private static string Uid
    {
        get
        {
            var m = UserDataManager.instance;
            return m != null ? m.UID : null;
        }
    }

    // ================================================================
    // お客様: 自分のパス
    // ================================================================
    private static readonly List<PassInfo> _mine = new List<PassInfo>();

    /// <summary>自分のパスを読み込んだか（読み込むまでは所持品にパスの欄を出さない）</summary>
    public static bool MineLoaded { get; private set; }

    /// <summary>自分のパスが増えた・読み込んだとき（メインスレッド）</summary>
    public static event Action Changed;

    /// <summary>
    /// 所持品に並べる自分のパス: 使えるもの（期限の近い順）→ 期限切れから30日以内のもの（新しい順）
    /// </summary>
    public static List<PassInfo> MineToShow(DateTime now)
    {
        var active = new List<PassInfo>();
        var expired = new List<PassInfo>();
        foreach (var p in _mine)
        {
            if (p.IsActive(now)) active.Add(p);
            else if ((now.Date - p.Expiry.Date).Days <= 30) expired.Add(p);
        }
        active.Sort((a, b) => a.Expiry.CompareTo(b.Expiry));
        expired.Sort((a, b) => b.Expiry.CompareTo(a.Expiry));
        active.AddRange(expired);
        return active;
    }

    /// <summary>使えるパスの枚数</summary>
    public static int ActiveCount(DateTime now)
    {
        int n = 0;
        foreach (var p in _mine)
            if (p.IsActive(now)) n++;
        return n;
    }

    /// <summary>自分が受け取ったパスを読み込む（受け取った枚数ぶんの読み取り）</summary>
    public static async UniTask LoadMineAsync()
    {
        string uid = Uid;
        if (string.IsNullOrEmpty(uid)) return;
        try
        {
            var snap = await Col.WhereEqualTo("claimed_by", uid).GetSnapshotAsync().AsUniTask();
            _mine.Clear();
            foreach (var doc in snap.Documents)
            {
                var p = Parse(doc);
                if (p != null && p.Claimed) _mine.Add(p);
            }
            MineLoaded = true;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Pass] 自分のパスの読み込みに失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// ほかの人が受け取ったパスを読み込む（管理者だけ。会員証QRを読んだとき。SpaceMemberView）。失敗したら null
    /// </summary>
    public static async UniTask<List<PassInfo>> LoadOfAsync(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return null;
        try
        {
            var snap = await Col.WhereEqualTo("claimed_by", uid).GetSnapshotAsync().AsUniTask();
            var list = new List<PassInfo>();
            foreach (var doc in snap.Documents)
            {
                var p = Parse(doc);
                if (p != null && p.Claimed) list.Add(p);
            }
            list.Sort((a, b) => b.Expiry.CompareTo(a.Expiry));
            return list;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Pass] パスの読み込みに失敗 ({uid}): {ex.Message}");
            return null;
        }
    }

    /// <summary>その日に使えるパスか（平日のみ＝月〜金、土日のみ＝土日、ALL DAY＝毎日。祝日は区別しない）</summary>
    public static bool UsableOn(PassInfo p, DateTime day)
    {
        if (p == null || !p.IsActive(day)) return false;
        bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
        if (p.Type == Weekday) return !weekend;
        if (p.Type == Weekend) return weekend;
        return true;
    }

    /// <summary>受け取りQRの中身からパスのIDを取り出す（形が違えば null）</summary>
    public static string ParseQr(string qrText)
    {
        if (string.IsNullOrEmpty(qrText) || !qrText.StartsWith(QR_PREFIX)) return null;
        string id = qrText.Substring(QR_PREFIX.Length).Trim();
        return Regex.IsMatch(id, "^[A-Za-z0-9]{10,40}$") ? id : null;
    }

    /// <summary>
    /// 受け取りQRを読んだ: まだだれも受け取っていなければ自分のパスにする（読み取り2回・書き込み1回）。
    /// 名前はサーバーの自分のユーザー情報から入れる（ルールで同じものか確かめている）
    /// </summary>
    public static async UniTask<(PassClaimResult result, PassInfo pass)> ClaimAsync(string qrText)
    {
        string id = ParseQr(qrText);
        if (id == null) return (PassClaimResult.NotFound, null);
        string uid = Uid;
        if (string.IsNullOrEmpty(uid)) return (PassClaimResult.Failed, null);

        var docRef = Col.Document(id);
        PassInfo pass;
        try
        {
            var snap = await docRef.GetSnapshotAsync(Source.Server).AsUniTask();
            if (!snap.Exists) return (PassClaimResult.NotFound, null); // 取り消された・正しくないQR
            pass = Parse(snap);
            if (pass == null) return (PassClaimResult.NotFound, null);
        }
        catch (Exception ex)
        {
            // ほかの人が受け取ったパスは読めない（ルール）
            if (IsPermissionDenied(ex)) return (PassClaimResult.Taken, null);
            Debug.LogWarning($"[Pass] パスの読み込みに失敗: {ex.Message}");
            return (PassClaimResult.Failed, null);
        }

        if (pass.Claimed)
            return pass.HolderUid == uid ? (PassClaimResult.AlreadyMine, pass) : (PassClaimResult.Taken, null);
        if (DateTime.Now > pass.IssuedAt.AddMinutes(CLAIM_MINUTES)) return (PassClaimResult.Expired, null);

        try
        {
            // ルールの users/{uid}.name（無ければ ""）と同じ値をそのまま入れる
            var me = await Db.Collection("users").Document(uid).GetSnapshotAsync(Source.Server).AsUniTask();
            object name = "";
            if (me.Exists && me.ToDictionary().TryGetValue("name", out var n)) name = n;
            await docRef.UpdateAsync(new Dictionary<string, object>
            {
                { "claimed_by", uid },
                { "claimed_at", FieldValue.ServerTimestamp },
                { "holder_name", name },
            }).AsUniTask();

            pass.Claimed = true;
            pass.HolderUid = uid;
            pass.HolderName = name as string ?? "";
            SetDates(pass, DateTime.Now);
            _mine.RemoveAll(p => p.Id == pass.Id);
            _mine.Add(pass);
            Changed?.Invoke();
            return (PassClaimResult.Ok, pass);
        }
        catch (Exception ex)
        {
            if (IsPermissionDenied(ex))
            {
                // 読んだあとに、ほかの人が先に受け取った／受け取れる時間を過ぎた
                bool late = DateTime.Now > pass.IssuedAt.AddMinutes(CLAIM_MINUTES - 2);
                return (late ? PassClaimResult.Expired : PassClaimResult.Taken, null);
            }
            if (HasError(ex, FirestoreError.NotFound)) return (PassClaimResult.NotFound, null); // 読んだあとに取り消された
            Debug.LogWarning($"[Pass] パスの受け取りに失敗: {ex.Message}");
            return (PassClaimResult.Failed, null);
        }
    }

    // ================================================================
    // 管理者
    // ================================================================
    /// <summary>受け取りQRを1枚発行する（受け取る前の形。1回だけ・発行から60分間使える）。発行したパスのID</summary>
    public static async UniTask<string> IssueAsync(PassTypeDef type)
    {
        var doc = Col.Document(); // ランダムなID（QRの中身になる）
        await doc.SetAsync(new Dictionary<string, object>
        {
            { "type", type.Id },
            { "issued_at", FieldValue.ServerTimestamp },
            { "issued_by", Uid ?? "" },
            { "claimed_by", null },
            { "claimed_at", null },
        }).AsUniTask();
        return doc.Id;
    }

    /// <summary>
    /// 受け取られなかった受け取りQRを取り消す（消す）。受け取られたあとはルールで消せないので、
    /// 閉じるのと受け取りがほぼ同時でも、受け取ったパスが消えることはない
    /// </summary>
    public static async UniTask CancelAsync(string id)
    {
        try
        {
            await Col.Document(id).DeleteAsync().AsUniTask();
        }
        catch (Exception ex)
        {
            // 受け取られていた（消せない）か、通信できなかった（60分で受け取れなくなる）
            Debug.Log($"[Pass] 受け取りQRを取り消せませんでした: {ex.Message}");
        }
    }

    /// <summary>発行した受け取りQRの様子を見張る（受け取られたら知らせる）。消されたら null を渡す</summary>
    public static ListenerRegistration Watch(string id, Action<PassInfo> onChange)
    {
        return Col.Document(id).Listen(snap => onChange?.Invoke(snap.Exists ? Parse(snap) : null));
    }

    /// <summary>受け取られたパスの一覧（購入者・期限・種類の確認用。新しく発行した順に最大500件）</summary>
    public static async UniTask<List<PassInfo>> LoadAllAsync()
    {
        var snap = await Col.OrderByDescending("issued_at").Limit(500).GetSnapshotAsync(Source.Server).AsUniTask();
        var list = new List<PassInfo>();
        foreach (var doc in snap.Documents)
        {
            var p = Parse(doc);
            if (p != null && p.Claimed) list.Add(p);
        }
        return list;
    }

    // ================================================================
    // 共通
    // ================================================================
    private static PassInfo Parse(DocumentSnapshot doc)
    {
        var d = doc.ToDictionary();
        var type = FindType(d.TryGetValue("type", out var t) ? t as string : null);
        if (type == null) return null;
        var p = new PassInfo { Id = doc.Id, Type = type };
        if (d.TryGetValue("issued_at", out var ia) && ia is Timestamp issued) p.IssuedAt = issued.ToDateTime().ToLocalTime();
        else p.IssuedAt = DateTime.Now; // 発行した直後（サーバーの時刻がまだ入っていない）
        p.HolderUid = d.TryGetValue("claimed_by", out var by) ? by as string : null;
        p.Claimed = !string.IsNullOrEmpty(p.HolderUid);
        p.HolderName = d.TryGetValue("holder_name", out var hn) ? hn as string ?? "" : "";
        if (p.Claimed)
        {
            var at = d.TryGetValue("claimed_at", out var ca) && ca is Timestamp claimed
                ? claimed.ToDateTime().ToLocalTime()
                : DateTime.Now;
            SetDates(p, at);
        }
        return p;
    }

    private static void SetDates(PassInfo p, DateTime claimedLocal)
    {
        p.Start = claimedLocal.Date;
        p.Expiry = p.Start.AddDays(DAYS - 1);
    }

    private static bool IsPermissionDenied(Exception ex) => HasError(ex, FirestoreError.PermissionDenied);

    private static bool HasError(Exception ex, FirestoreError code)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is FirestoreException fe && fe.ErrorCode == code) return true;
            if (e is AggregateException ae)
                foreach (var inner in ae.InnerExceptions)
                    if (HasError(inner, code)) return true;
        }
        return false;
    }
}
