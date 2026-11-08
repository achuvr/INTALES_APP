using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 入店中の人（新ホームの中央の惑星のいちばん外の円を、入店中の人数ぶんの小さな球が、
/// それぞれが選んでいる惑星の色で回る）。
///
/// Firestore:
///   visitors/{ランダムなID} = { p: 惑星ID, at: 入店した時刻 } … ログインユーザーならだれでも読める
///     名前もユーザーIDも入れない（だれが入店中かはわからない。わかるのは人数と惑星の色だけ）。
///     フレンドに入店中を知らせる設定（presence/store）とは別で、入店した全員が1件ずつ作る
///   visitor_keys/{自分のUID} = { id: 自分の記録のID } … 本人しか読めない「鍵」
///     作る・惑星を変える・消すのは、自分の鍵が指している記録だけ（ほかの人の記録はさわれない）。
///     鍵を付け替える・消すときは前の記録も同じ書き込みで消す（1人が持てる記録は常に1件まで）
///   ・入店: 鍵を読み、前の記録があれば消して、新しいランダムなIDで作り直す。
///     サーバーの返事を待たずに自分の球を一覧の最後に出し、入店のQRからなら演出の合図（ArrivalId）も出す
///     （新ホームで、自分の惑星がいちばん外の円の自分の場所へ飛んでいく）
///   ・入店中に惑星を選び直した: p を書き換える（SpacePlanets.Current を変えたとき）
///   ・退店／退店し忘れの後始末／退会: 記録と鍵を消す（ログアウトでは消さない。お店にいることは変わらないため）
///   ・消し忘れの記録は、表示側で営業終了時刻（深夜26時）を過ぎたら数えず、
///     毎朝4時の自動処理でまとめて消す（tools/firestore/cleanup-visitors.js）
/// 書き込める形はルール（tools/firestore/firestore.rules の visitors / visitor_keys）で決めている。
/// </summary>
public static class StoreVisitors
{
    private const string COLLECTION = "visitors";
    private const string KEYS = "visitor_keys";
    // この端末が作った記録のID。入店中の記録があるかの目安（毎回サーバーに聞かないため）と、惑星の変更に使う
    private const string PREF_DOC = "visitor_doc";
    // 購読する範囲（入店した時刻がこれより新しい記録だけ）。営業は昼〜深夜26時なので、その日の入店はすべて入る
    private const double LISTEN_HOURS = 20;

    private static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;
    private static CollectionReference Col => Db.Collection(COLLECTION);

    // 書き込みは1つずつ順番に（入店と退店が重なっても、鍵の読み書きが食い違わないように）
    private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    private static ListenerRegistration _listener;
    private static bool _listening;
    private static readonly List<Entry> _entries = new List<Entry>(); // 購読中の記録（入店した順）

    // 入店した直後の自分。サーバーの一覧に入るまでは、ここから一覧の最後に足す
    private static string _selfId, _selfPlanet;

    // 入店のQRを読んだ直後の自分の記録のID（新ホームの演出待ち）。この時間を過ぎたら演出はせず、球は普通に出る
    private const float ARRIVAL_SECONDS = 120f;
    private static string _arrivalId;
    private static float _arrivalAt;

    private struct Entry
    {
        public string Id, Planet;
        public bool HasAt;
        public Timestamp At;
    }

    /// <summary>入店中の人ひとり（だれかはわからない）</summary>
    public struct Visitor
    {
        public string Id;     // 記録のID（ランダム）
        public string Planet; // 選んでいる惑星のID
    }

    /// <summary>入店中の人が増えた・減った・惑星を変えたとき（購読中。メインスレッド）</summary>
    public static event Action Changed;

    /// <summary>この端末が入店中の記録を持っているか</summary>
    public static bool HasRecord => !string.IsNullOrEmpty(PlayerPrefs.GetString(PREF_DOC, ""));

    /// <summary>この端末の入店中の記録のID（いちばん外の円の自分の球。入店中でなければ null）</summary>
    public static string SelfDocId
    {
        get
        {
            string id = PlayerPrefs.GetString(PREF_DOC, "");
            return string.IsNullOrEmpty(id) ? _selfId : id;
        }
    }

    /// <summary>
    /// 入店のQRを読んだ直後の自分の記録のID。新ホームが受け取って（ConsumeArrival）、自分の惑星が
    /// いちばん外の円の自分の場所へ飛んでいく演出をする。読んでから ARRIVAL_SECONDS を過ぎたら null
    /// </summary>
    public static string ArrivalId =>
        _arrivalId != null && Time.unscaledTime - _arrivalAt < ARRIVAL_SECONDS ? _arrivalId : null;

    public static void ConsumeArrival() => _arrivalId = null;

    private static string Uid
    {
        get
        {
            var m = UserDataManager.instance;
            return m != null ? m.UID : null;
        }
    }

    // ================================================================
    // 書き込み（入店・退店・惑星の変更）
    // ================================================================
    /// <summary>
    /// 入店した。前の記録が残っていれば消してから、新しいランダムなIDで作り直す。
    /// arrival: 入店のQRを読んだとき（新ホームで、自分の惑星が円へ飛んでいく演出をする）
    /// </summary>
    public static async UniTask CheckInAsync(string planetId, bool arrival = false)
    {
        string uid = Uid;
        if (string.IsNullOrEmpty(uid)) return;
        string id = Guid.NewGuid().ToString("N");
        // サーバーの返事を待たずに、自分の球を一覧の最後に出す（飛んでいく惑星の行き先になる）
        _selfId = id;
        _selfPlanet = planetId ?? "";
        if (arrival)
        {
            _arrivalId = id;
            _arrivalAt = Time.unscaledTime;
        }
        Changed?.Invoke();
        await _gate.WaitAsync();
        try
        {
            var keyRef = Db.Collection(KEYS).Document(uid);
            string old = await ReadKeyAsync(keyRef);
            var batch = Db.StartBatch();
            if (!string.IsNullOrEmpty(old)) batch.Delete(Col.Document(old));
            batch.Set(keyRef, new Dictionary<string, object> { { "id", id } });
            batch.Set(Col.Document(id), new Dictionary<string, object>
            {
                { "p", planetId ?? "" },
                { "at", FieldValue.ServerTimestamp },
            });
            await batch.CommitAsync().AsUniTask();
            PlayerPrefs.SetString(PREF_DOC, id);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            // 記録できなくても入店そのものには影響しない（入店中のまま次に新ホームを開いたときに作り直す）。
            // 先に出した自分の球は引っ込める
            Debug.LogWarning($"[Visitors] 入店の記録に失敗: {ex.Message}");
            if (_arrivalId == id) _arrivalId = null;
            if (_selfId == id)
            {
                _selfId = null;
                Changed?.Invoke();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>退店した（自分の記録と鍵を消す。記録がなければ何もしない）</summary>
    public static async UniTask CheckOutAsync()
    {
        string uid = Uid;
        if (string.IsNullOrEmpty(uid)) return;
        _arrivalId = null;
        if (_selfId != null)
        {
            _selfId = null; // サーバーの一覧に入る前に退店した（先に出していた自分の球を引っ込める）
            Changed?.Invoke();
        }
        await _gate.WaitAsync();
        try
        {
            var keyRef = Db.Collection(KEYS).Document(uid);
            string old = await ReadKeyAsync(keyRef);
            if (!string.IsNullOrEmpty(old))
            {
                var batch = Db.StartBatch();
                batch.Delete(Col.Document(old));
                batch.Delete(keyRef);
                await batch.CommitAsync().AsUniTask();
            }
            PlayerPrefs.DeleteKey(PREF_DOC);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            // 消せなくても、表示側で営業終了後は数えず、毎朝の自動処理で消える
            // （端末に記録のIDを残しておき、次に新ホームを開いたときにもう一度消す）
            Debug.LogWarning($"[Visitors] 退店の記録の削除に失敗: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>入店中に惑星を選び直した（入店中の記録がなければ何もしない）</summary>
    public static async UniTask SetPlanetAsync(string planetId)
    {
        bool checkingIn = _selfId != null; // 入店の記録を書いている途中かもしれない（書き終わるのを待ってから変える）
        if (checkingIn)
        {
            _selfPlanet = planetId ?? ""; // サーバーの一覧に入る前の自分の球も色を変える
            Changed?.Invoke();
        }
        if (!HasRecord && !checkingIn) return;
        await _gate.WaitAsync();
        try
        {
            string id = PlayerPrefs.GetString(PREF_DOC, ""); // 待っている間に退店していたら何もしない
            if (string.IsNullOrEmpty(id)) return;
            await Col.Document(id).UpdateAsync("p", planetId ?? "").AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Visitors] 惑星の変更の記録に失敗: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>自分の鍵が指している記録のID（なければ null）。古い控えで間違えないよう、必ずサーバーに聞く</summary>
    private static async UniTask<string> ReadKeyAsync(DocumentReference keyRef)
    {
        var snap = await keyRef.GetSnapshotAsync(Source.Server).AsUniTask();
        if (!snap.Exists) return null;
        var d = snap.ToDictionary();
        return d.TryGetValue("id", out var v) ? v as string : null;
    }

    // ================================================================
    // 読み取り（新ホームを開いている間のリアルタイム購読）
    // ================================================================
    /// <summary>
    /// 入店中の人の購読を始める。古い記録まで読まないよう、直近 LISTEN_HOURS 時間に入店した分だけ。
    /// 読み取りは最初に件数ぶんと、あとは変わった分だけ
    /// </summary>
    public static void StartListening()
    {
        if (_listening) return;
        _listening = true; // 最初の結果が Listen の戻りより先に届いても受け取れるよう、先に立てる
        var since = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-LISTEN_HOURS));
        _listener = Col.WhereGreaterThan("at", since).Listen(snap =>
        {
            if (!_listening) return;
            _entries.Clear();
            foreach (var doc in snap.Documents) // 入店した時刻の順（範囲で絞った項目の順に並ぶ）
            {
                var d = doc.ToDictionary();
                var e = new Entry { Id = doc.Id, Planet = d.TryGetValue("p", out var p) ? p as string ?? "" : "" };
                if (d.TryGetValue("at", out var a) && a is Timestamp at)
                {
                    e.HasAt = true;
                    e.At = at;
                }
                _entries.Add(e);
                if (e.Id == _selfId) _selfId = null; // 自分の記録がサーバーの一覧に入った（以後はこちらで数える）
            }
            Changed?.Invoke();
        });
    }

    public static void StopListening()
    {
        _listening = false;
        _listener?.Stop();
        _listener = null;
        _entries.Clear();
    }

    /// <summary>
    /// いま入店中の人（入店した順。新しく入った人は最後に並ぶ）。
    /// 営業終了時刻を過ぎた記録（退店し忘れ）は数えない。入店した直後でサーバーの一覧にまだない自分も最後に入れる。
    /// 購読前は空
    /// </summary>
    public static List<Visitor> Current()
    {
        var list = new List<Visitor>(_entries.Count + 1);
        if (!_listening) return list;
        foreach (var e in _entries)
        {
            // 自分が書いた直後はサーバーの時刻がまだ入っていない → 入ったばかりなので数える
            if (e.HasAt && !PresenceService.IsStillPresent(e.At)) continue;
            list.Add(new Visitor { Id = e.Id, Planet = e.Planet });
        }
        if (_selfId != null) list.Add(new Visitor { Id = _selfId, Planet = _selfPlanet });
        return list;
    }
}
