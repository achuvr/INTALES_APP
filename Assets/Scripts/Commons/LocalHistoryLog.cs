using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 端末ローカルの行動履歴（ダイスの出目・ガチャ結果・フレンド登録・滞在時間・クーポン使用）。
/// Application.persistentDataPath/history_log.json に保存する（Firestore不使用）。
/// 最大50件まで蓄積し、超えた分は古いものから削除する。
/// </summary>
public static class LocalHistoryLog
{
    public const int MAX_ENTRIES = 50;

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, "history_log.json");

    [Serializable]
    public class HistoryEntry
    {
        public string type; // dice / gacha / friend / visit / coupon
        public string text;
        public string date; // "yyyy/MM/dd HH:mm"
    }

    [Serializable]
    private class Wrapper
    {
        public List<HistoryEntry> entries = new List<HistoryEntry>();
    }

    private static Wrapper _cache;

    /// <summary>履歴を1件追記する（50件を超えたら古いものから削除）</summary>
    public static void Add(string type, string text)
    {
        try
        {
            var w = Load();
            w.entries.Add(new HistoryEntry
            {
                type = type,
                text = text,
                date = DateTime.Now.ToString("yyyy/MM/dd HH:mm"),
            });
            while (w.entries.Count > MAX_ENTRIES)
                w.entries.RemoveAt(0);
            File.WriteAllText(FilePath, JsonUtility.ToJson(w));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[History] 履歴の保存に失敗: {ex.Message}");
        }
    }

    /// <summary>履歴一覧（新しい順）</summary>
    public static List<HistoryEntry> GetAllNewestFirst()
    {
        var list = new List<HistoryEntry>(Load().entries);
        list.Reverse();
        return list;
    }

    private static Wrapper Load()
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = File.Exists(FilePath)
                ? JsonUtility.FromJson<Wrapper>(File.ReadAllText(FilePath)) ?? new Wrapper()
                : new Wrapper();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[History] 履歴の読み込みに失敗: {ex.Message}");
            _cache = new Wrapper();
        }
        return _cache;
    }
}
