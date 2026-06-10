using System;
using System.Collections.Generic;
using Firebase.Firestore;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 在店状況（チェックイン中かどうか）の共有サービス。
///
/// Firestore 構造: presence/store の1ドキュメントに
///   { {uid}: Timestamp(チェックイン時刻), ... }
/// のフラットマップで全員分を持つ。
/// フレンド全員の在店確認が1回の読み取りで済む。
///
/// - 「ログイン情報を公開する」(UserData.SharePresence) がONのユーザーだけ書き込む
/// - チェックアウトでエントリ削除
/// - チェックアウト忘れは、表示側で「営業終了時刻(LocalVisitLog.ClosingDeadlineFor)を
///   過ぎたら在店扱いしない」ことで自動的に消える
/// </summary>
public static class PresenceService
{
    private static DocumentReference Doc =>
        FirebaseFirestore.DefaultInstance.Collection("presence").Document("store");

    /// <summary>
    /// 自分の在店状態を共有する。公開設定がOFFなら何もしない（書き込みゼロ）。
    /// </summary>
    public static async UniTask SetCheckedInAsync(bool checkedIn)
    {
        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID)) return;
        if (!manager.UserData.SharePresence) return;
        await WriteAsync(checkedIn);
    }

    /// <summary>
    /// 公開設定に関わらず自分のエントリを書き込み/削除する
    /// （公開OFFへの切り替え時に既存エントリを消すために使う）。
    /// </summary>
    public static async UniTask WriteAsync(bool checkedIn)
    {
        var manager = UserDataManager.instance;
        if (manager == null || string.IsNullOrEmpty(manager.UID)) return;

        try
        {
            if (checkedIn)
            {
                // ドキュメントが無ければ作られるよう SetAsync(MergeAll) を使う
                await Doc.SetAsync(
                    new Dictionary<string, object> { { manager.UID, Timestamp.GetCurrentTimestamp() } },
                    SetOptions.MergeAll).AsUniTask();
                Debug.Log("[Presence] 在店状態を共有しました");
            }
            else
            {
                try
                {
                    await Doc.UpdateAsync(new Dictionary<FieldPath, object>
                    {
                        { new FieldPath(manager.UID), FieldValue.Delete }
                    }).AsUniTask();
                    Debug.Log("[Presence] 在店状態を解除しました");
                }
                catch
                {
                    // ドキュメント未作成（誰も一度も共有していない）なら消すものもない
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Presence] 在店状態の更新エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 全員の在店状況を取得する（1ドキュメント読み取り）。
    /// 戻り値: uid → チェックイン時刻
    /// </summary>
    public static async UniTask<Dictionary<string, Timestamp>> GetPresenceAsync()
    {
        var result = new Dictionary<string, Timestamp>();
        try
        {
            var snap = await Doc.GetSnapshotAsync().AsUniTask();
            if (!snap.Exists) return result;
            foreach (var kv in snap.ToDictionary())
            {
                if (kv.Value is Timestamp ts) result[kv.Key] = ts;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Presence] 在店状況の取得エラー: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// チェックイン時刻から見て「今もまだ在店中の扱いか」。
    /// 営業終了時刻（深夜26時）を過ぎていたらチェックアウト忘れとみなして false。
    /// </summary>
    public static bool IsStillPresent(Timestamp checkinAt)
    {
        var checkinLocal = checkinAt.ToDateTime().ToLocalTime();
        return DateTime.Now < LocalVisitLog.ClosingDeadlineFor(checkinLocal);
    }
}
