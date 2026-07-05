using System;
using UnityEngine;

/// <summary>
/// 店舗に置いてあるボードゲーム1件ぶんの情報。
/// Resources/BoardGames/boardgames.json（bodoge.hoobby.net のスペースページから生成）を
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
    public BoardGameEntry[] games;
}

/// <summary>
/// 店舗のボードゲーム一覧データ（Resources同梱JSON）を読み込んでキャッシュする。
/// データは bodoge.hoobby.net/spaces/intales-cafe/games から生成した静的ファイル。
/// 在庫が変わったら BodogeSpaceCsvExporter で JSON を作り直して差し替える。
/// </summary>
public static class BoardGameCatalog
{
    private const string ResourcePath = "BoardGames/boardgames"; // 拡張子なし
    private static BoardGameCatalogData _cache;

    public static BoardGameCatalogData Load()
    {
        if (_cache != null) return _cache;

        var ta = Resources.Load<TextAsset>(ResourcePath);
        if (ta == null)
        {
            Debug.LogError($"[BoardGame] データが見つかりません: Resources/{ResourcePath}.json");
            _cache = Empty();
            return _cache;
        }

        try
        {
            _cache = JsonUtility.FromJson<BoardGameCatalogData>(ta.text);
        }
        catch (Exception e)
        {
            Debug.LogError("[BoardGame] JSONの解析に失敗しました: " + e.Message);
            _cache = Empty();
        }

        if (_cache == null) _cache = Empty();
        if (_cache.games == null) _cache.games = Array.Empty<BoardGameEntry>();
        return _cache;
    }

    private static BoardGameCatalogData Empty() =>
        new BoardGameCatalogData { count = 0, games = Array.Empty<BoardGameEntry>() };
}
