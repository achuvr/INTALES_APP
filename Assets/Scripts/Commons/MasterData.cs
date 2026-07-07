using System.Collections.Generic;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// master/config（today・achievements・イベント画像・アイテムDBバージョン）を
/// 1セッション1回だけ読み取り、メモリにキャッシュする。
///
/// 旧構造では today / achievements / events をそれぞれ別コレクションから
/// 読んでいた（計5ドキュメント読み取り）が、master/config 1ドキュメントに
/// 集約したことで、ホーム画面の表示に必要な読み取りは1回になる。
/// </summary>
public static class MasterData
{
    private static MasterConfig _config;
    private static UniTaskCompletionSource<MasterConfig> _loadingTcs;

    /// <summary>取得済みのconfig（未取得ならnull）</summary>
    public static MasterConfig CachedConfig => _config;

    /// <summary>
    /// master/config を取得する。2回目以降はキャッシュを返し、
    /// 同時に複数箇所から呼ばれても読み取りは1回だけ行う。
    /// </summary>
    public static async UniTask<MasterConfig> GetConfigAsync()
    {
        if (_config != null) return _config;
        if (_loadingTcs != null) return await _loadingTcs.Task;

        _loadingTcs = new UniTaskCompletionSource<MasterConfig>();
        try
        {
            var db = FirebaseFirestore.DefaultInstance;
            var snap = await db.Collection("master").Document("config")
                .GetSnapshotAsync().AsUniTask();

            _config = snap.Exists ? snap.ConvertTo<MasterConfig>() : new MasterConfig();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MasterData] master/config 取得エラー: {ex.Message}");
            _config = new MasterConfig();
        }

        _loadingTcs.TrySetResult(_config);
        return _config;
    }

    /// <summary>キャッシュを破棄して次回取得時に再読み込みさせる（デバッグ用）</summary>
    public static void Invalidate()
    {
        _config = null;
        _loadingTcs = null;
    }
}

/// <summary>master/config ドキュメントのモデル</summary>
[FirestoreData, System.Serializable]
public class MasterConfig
{
    public MasterConfig()
    {
        Achievements = new List<Achievement>();
        EventImages = new List<string>();
    }

    /// <summary>アイテムマスター（master/items）のバージョン。上がっていたら再同期する</summary>
    [FirestoreProperty("items_version")]
    public int ItemsVersion { get; set; }

    /// <summary>ボードゲーム在庫（master/boardgames）のバージョン。上がっていたら再同期する</summary>
    [FirestoreProperty("boardgames_version")]
    public int BoardgamesVersion { get; set; }

    /// <summary>本日のラッキー職業・属性</summary>
    [FirestoreProperty("today")]
    public Today Today { get; set; }

    /// <summary>ボードゲーム実績一覧</summary>
    [FirestoreProperty("achievements")]
    public List<Achievement> Achievements { get; set; }

    /// <summary>イベント告知画像のURL一覧（表示順）</summary>
    [FirestoreProperty("event_images")]
    public List<string> EventImages { get; set; }
}
