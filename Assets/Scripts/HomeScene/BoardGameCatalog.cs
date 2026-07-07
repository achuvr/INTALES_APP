using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// 店舗に置いてあるボードゲーム1件ぶんの情報。
/// boardgames.json（bodoge.hoobby.net のスペースページから生成）を
/// JsonUtility でそのままデシリアライズできるフィールド名にしている。
/// </summary>
[Serializable]
public class BoardGameEntry
{
    public string title;      // 和名（例: ポイント・シティ）
    public string title_en;   // 英名（例: Point City）
    public string players;    // 遊べる人数（例: 1人～4人）
    public string time;       // プレイ時間（例: 15分～30分）
    public string year;       // 発売年（例: 2023年）
    public string[] genre;    // ジャンル（テーマ/フレーバー＋メカニクスのタグ）
    public string url;        // ボドゲーマの詳細ページURL
}

/// <summary>boardgames.json のルート。</summary>
[Serializable]
public class BoardGameCatalogData
{
    public int count;
    public string source;
    public string updated;
    public int version;       // master/config.boardgames_version と比較する同期用バージョン
    public BoardGameEntry[] games;
}

/// <summary>
/// 店舗のボードゲーム一覧データを読み込んでキャッシュする。
///
/// データの流れ（在庫の自動反映）:
///   tools/firestore/sync-boardgames.js（GitHub Actions が毎日実行）が
///   bodoge.hoobby.net をスクレイプし、在庫が変わったときだけ
///   master/boardgames と master/config.boardgames_version を更新する。
///   クライアントは SyncAsync() でバージョンを比較し、上がっていた場合のみ
///   master/boardgames を1ドキュメント読んでローカルに保存する
///   （変わっていなければ追加の読み取り・通信はゼロ）。
///
/// Load() の優先順: 同期済みローカルキャッシュ → アプリ同梱の Resources JSON。
/// </summary>
public static class BoardGameCatalog
{
    private const string ResourcePath = "BoardGames/boardgames"; // 拡張子なし
    private static string CacheDir  => Path.Combine(Application.persistentDataPath, "board_games");
    private static string CachePath => Path.Combine(CacheDir, "boardgames.json");

    private static BoardGameCatalogData _cache;
    private static bool _syncing;

    public static BoardGameCatalogData Load()
    {
        if (_cache != null) return _cache;

        // 1) Firestore から同期済みのローカルキャッシュ
        try
        {
            if (File.Exists(CachePath))
                _cache = Parse(File.ReadAllText(CachePath), "ローカルキャッシュ");
        }
        catch (Exception e)
        {
            Debug.LogError("[BoardGame] キャッシュの読み込みに失敗しました: " + e.Message);
            _cache = null;
        }

        // 2) アプリ同梱の Resources JSON
        if (_cache == null)
        {
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null)
            {
                Debug.LogError($"[BoardGame] データが見つかりません: Resources/{ResourcePath}.json");
                _cache = Empty();
                return _cache;
            }
            _cache = Parse(ta.text, "同梱JSON") ?? Empty();
        }

        return _cache;
    }

    /// <summary>
    /// master/config.boardgames_version とローカルのバージョンを比較し、
    /// 上がっていた場合のみ master/boardgames を1ドキュメント読んで保存する。
    /// ItemCacheManager の起動時同期から呼ばれる。
    /// </summary>
    public static async UniTask SyncAsync(Action<string> onProgress = null)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            // master/config はセッション中どのみち1回読む → ここまで追加読み取りゼロ
            var config = await MasterData.GetConfigAsync();
            if (config.BoardgamesVersion <= 0)
            {
                onProgress?.Invoke("master/boardgames 未配信 → 同梱データを使用");
                return;
            }

            int local = Load().version;
            if (local >= config.BoardgamesVersion)
            {
                onProgress?.Invoke($"変更なし (v{local}) → 読み取りスキップ");
                return;
            }

            onProgress?.Invoke($"master/boardgames を取得中... (v{local} → v{config.BoardgamesVersion})");
            var snap = await FirebaseFirestore.DefaultInstance
                .Collection("master").Document("boardgames")
                .GetSnapshotAsync().AsUniTask();

            if (!snap.Exists || !snap.ContainsField("json"))
            {
                onProgress?.Invoke("master/boardgames が存在しません");
                return;
            }

            string json = snap.GetValue<string>("json");
            var parsed = Parse(json, "Firestore");
            if (parsed == null || parsed.games.Length == 0)
            {
                onProgress?.Invoke("取得データが不正のため破棄（現行データを維持）");
                return;
            }

            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(CachePath, json);
            _cache = parsed;
            onProgress?.Invoke($"更新完了 v{parsed.version} ({parsed.count}件)");
        }
        catch (Exception e)
        {
            Debug.LogError("[BoardGame] 同期エラー: " + e.Message);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static BoardGameCatalogData Parse(string json, string label)
    {
        try
        {
            var data = JsonUtility.FromJson<BoardGameCatalogData>(json);
            if (data == null) return null;
            if (data.games == null) data.games = Array.Empty<BoardGameEntry>();
            Debug.Log($"[BoardGame] {label}読み込み: {data.games.Length}件 (v{data.version})");
            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[BoardGame] JSONの解析に失敗しました（{label}）: " + e.Message);
            return null;
        }
    }

    private static BoardGameCatalogData Empty() =>
        new BoardGameCatalogData { count = 0, games = Array.Empty<BoardGameEntry>() };
}
