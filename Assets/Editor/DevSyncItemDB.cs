#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;

public static class DevSyncItemDB
{
    [MenuItem("Tools/[Dev] Item DB/全件同期 (Firestore → ローカル)")]
    public static async void SyncAll()
    {
        if (!CheckPlayMode()) return;
        var manager = ItemCacheManager.instance;
        if (manager == null) { Debug.LogError("[DevSyncItemDB] ItemCacheManager が見つかりません"); return; }

        EditorUtility.DisplayProgressBar("ItemDB 同期", "Firestore に接続中...", 0f);
        int step = 0;
        try
        {
            await manager.SyncAsync(msg =>
            {
                step++;
                EditorUtility.DisplayProgressBar("ItemDB 同期", msg, Mathf.Clamp01(step / 12f));
                Debug.Log($"[DevSyncItemDB] {msg}");
            });
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("同期完了", manager.GetSyncInfo(), "OK");
    }

    [MenuItem("Tools/[Dev] Item DB/アイコンを同期 (Storage → ローカル)")]
    public static async void SyncIcons()
    {
        if (!CheckPlayMode()) return;
        var manager = ItemCacheManager.instance;
        if (manager == null || !manager.IsLoaded)
        {
            EditorUtility.DisplayDialog("エラー",
                "先に「全件同期」でアイテムデータを取得してください。", "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("アイコン同期", "ダウンロード中...", 0f);
        int step = 0;
        try
        {
            await manager.SyncIconsAsync(msg =>
            {
                step++;
                EditorUtility.DisplayProgressBar("アイコン同期", msg, Mathf.Clamp01(step / (float)manager.GetAll().Count));
                Debug.Log($"[DevSyncItemDB] {msg}");
            });
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("アイコン同期完了",
            $"同期が完了しました。\n詳細は Console ログを確認してください。\n\n{manager.GetSyncInfo()}",
            "OK");
    }

    [MenuItem("Tools/[Dev] Item DB/キャッシュ情報を表示")]
    public static void ShowInfo()
    {
        if (!CheckPlayMode()) return;
        var manager = ItemCacheManager.instance;
        if (manager == null) { EditorUtility.DisplayDialog("情報", "ItemCacheManager が見つかりません", "OK"); return; }
        EditorUtility.DisplayDialog("ItemDB キャッシュ情報", manager.GetSyncInfo(), "OK");
    }

    [MenuItem("Tools/[Dev] Item DB/ローカルキャッシュをクリア")]
    public static void ClearCache()
    {
        if (!CheckPlayMode()) return;
        var manager = ItemCacheManager.instance;
        if (manager == null) return;
        if (!EditorUtility.DisplayDialog("確認", "アイテムDBキャッシュを削除します。\n次回同期時に全件取得が発生します。", "削除", "キャンセル")) return;
        manager.ClearCache();
        EditorUtility.DisplayDialog("完了", "DBキャッシュをクリアしました", "OK");
    }

    [MenuItem("Tools/[Dev] Item DB/アイコンキャッシュをクリア")]
    public static void ClearIconCache()
    {
        if (!CheckPlayMode()) return;
        var manager = ItemCacheManager.instance;
        if (manager == null) return;
        if (!EditorUtility.DisplayDialog("確認", "アイコン画像キャッシュを削除します。\n次回同期時に全件DLが発生します。", "削除", "キャンセル")) return;
        manager.ClearIconCache();
        EditorUtility.DisplayDialog("完了", "アイコンキャッシュをクリアしました", "OK");
    }

    [MenuItem("Tools/[Dev] Item DB/★ 選択中キャラに職業アイテムを全取得")]
    public static async void GiveAllJobEquipments()
    {
        if (!CheckPlayMode()) return;
        var cacheManager = ItemCacheManager.instance;
        if (cacheManager == null || !cacheManager.IsLoaded)
        {
            EditorUtility.DisplayDialog("エラー", "先に「全件同期」を実行してローカルDBを作成してください。", "OK");
            return;
        }
        var userManager = UserDataManager.instance;
        if (userManager == null) { EditorUtility.DisplayDialog("エラー", "UserDataManager が見つかりません", "OK"); return; }

        int charIdx    = userManager.CurrentSelectCharacterNumber;
        var characters = userManager.UserData?.Characters;
        if (characters == null || charIdx >= characters.Count)
        { EditorUtility.DisplayDialog("エラー", "キャラクターが見つかりません", "OK"); return; }

        var chara  = characters[charIdx];
        string job = chara.Job ?? "";

        var allItems = new List<CachedItem>();
        allItems.AddRange(cacheManager.GetByJob(job));
        allItems.AddRange(cacheManager.GetByJob("common"));

        if (allItems.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", $"職業「{job}」のアイテムがローカルDBにありません。\n先に「全件同期」を実行してください。", "OK");
            return;
        }

        var alreadyOwned = new HashSet<string>(chara.Inventory.Select(r => r.ItemId));
        var toAdd = allItems.Where(x => !alreadyOwned.Contains(x.item_id)).OrderBy(x => x.item_id).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"キャラクター  : {chara.Name}  ({job})");
        sb.AppendLine($"DB内アイテム数: {allItems.Count}件");
        sb.AppendLine($"既所持（スキップ）: {alreadyOwned.Count}件");
        sb.AppendLine($"新規追加: {toAdd.Count}件");
        sb.AppendLine("─────────────────────────");
        foreach (var item in toAdd) sb.AppendLine($"[{item.slot_type}] {item.name ?? item.item_id}");
        sb.AppendLine("─────────────────────────");
        sb.AppendLine("インベントリに追加しますか？\n※装備はしません。同一アイテムは追加しません。");

        if (!EditorUtility.DisplayDialog("職業アイテムを全取得", sb.ToString(), "追加する", "キャンセル")) return;
        if (toAdd.Count == 0) { EditorUtility.DisplayDialog("情報", "追加すべき新規アイテムはありません（全て所持済み）", "OK"); return; }

        foreach (var item in toAdd)
            chara.Inventory.Add(new InventoryRef { Job = item.job, ItemId = item.item_id });

        Debug.Log($"[DevEquip] {chara.Name}({job}) にインベントリ追加: {toAdd.Count}件");

        EditorUtility.DisplayProgressBar("Firestore 保存", "inventory を書き込み中...", 0.5f);
        try
        {
            // users/{uid}.characters.{idx}.inventory に書き込む（サブコレクション廃止後の新構造）
            await ItemSyncManager.SaveInventoryAsync(userManager.UID, charIdx, chara.Inventory);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("取得完了",
                $"{toAdd.Count}件をインベントリに追加し Firestore に保存しました。\n合計所持数: {chara.Inventory.Count}件", "OK");
            Debug.Log($"[DevEquip] Firestore 保存完了 ({toAdd.Count}件追加 / 合計{chara.Inventory.Count}件)");
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Firestore エラー", ex.Message, "OK");
            Debug.LogError($"[DevEquip] {ex}");
        }
    }

    [MenuItem("Tools/[Dev] Item DB/強制再同期 (バージョン無視)")]
    public static async void ForceSync()
    {
        if (!CheckPlayMode()) return;
        var manager = ItemCacheManager.instance;
        if (manager == null) { Debug.LogError("[DevSyncItemDB] ItemCacheManager が見つかりません"); return; }

        EditorUtility.DisplayProgressBar("ItemDB 強制再同期", "master/items を取得中...", 0.5f);
        try
        {
            await manager.SyncAsync(msg => Debug.Log($"[DevSyncItemDB] {msg}"), force: true);
        }
        finally { EditorUtility.ClearProgressBar(); }
        EditorUtility.DisplayDialog("再同期完了", manager.GetSyncInfo(), "OK");
    }

    private static bool CheckPlayMode()
    {
        if (Application.isPlaying) return true;
        EditorUtility.DisplayDialog("注意", "Playモード中に実行してください（Firebase接続が必要）", "OK");
        return false;
    }
}
#endif