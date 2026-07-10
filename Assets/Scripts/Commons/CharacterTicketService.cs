using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// キャラクターチケット（ガチャからは排出されないボーナスクーポン）の付与まわり。
///
/// 付与ルール: キャラクターのレベルが50に到達するごとに1枚。
/// 全キャラの floor(lv/50) の合計を「到達マイルストーン数」とし、付与済み数
/// （users/{uid}.char_ticket_milestones）との差分だけ character_ticket を加算する。
/// どのレベルアップ経路から何度呼ばれても二重付与しない（差分が0なら何もしない）。
///
/// 使用: バッグから1度に1枚だけ使え、新しいキャラクターを作成できる
/// （入口は ItemBagPageManager、消費は CreateNewCharacter が作成と同時に行う）。
/// </summary>
public static class CharacterTicketService
{
    public const int LEVELS_PER_TICKET = 50;

    /// <summary>
    /// 「チケットを使ってキャラ作成シーンを開いた」状態。
    /// CreateNewCharacter が作成の書き込みと同時に1枚消費してリセットする。
    /// （Home再ロード時にも念のためリセットされる）
    /// </summary>
    public static bool PendingUse;

    private static Sprite _icon;

    /// <summary>チケットのアイコン（Resources/ItemIcons/CHARA）。</summary>
    public static Sprite IconSprite
    {
        get
        {
            if (_icon == null) _icon = Resources.Load<Sprite>("ItemIcons/CHARA");
            return _icon;
        }
    }

    /// <summary>全キャラクターの到達マイルストーン数（floor(lv/50) の合計）。</summary>
    public static int TotalMilestones(UserData data)
        => data?.Characters == null ? 0 : data.Characters.Sum(c => c.Level / LEVELS_PER_TICKET);

    /// <summary>
    /// レベルの書き込み後に呼ぶ。新たに50の倍数へ到達したぶんのチケットを付与する。
    /// Home起動時にも呼ばれ、過去分の取りこぼしを精算する。
    /// 書き込みに失敗しても呼び出し元の流れは壊さない（次のレベルアップ時に再精算される）。
    /// </summary>
    public static async UniTask CheckAndGrantAsync()
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null || string.IsNullOrEmpty(manager.UID)) return;

        var data = manager.UserData;
        int total = TotalMilestones(data);
        int diff = total - data.CharTicketMilestones;
        if (diff <= 0) return;

        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(manager.UID)
                .UpdateAsync(new Dictionary<string, object>
                {
                    { "character_ticket", FieldValue.Increment(diff) },
                    { "char_ticket_milestones", total },
                }).AsUniTask();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CharTicket] 付与書き込みエラー: {ex.Message}");
            return;
        }

        data.CharacterTicket += diff;
        data.CharTicketMilestones = total;

        LocalHistoryLog.Add("coupon",
            $"レベル{LEVELS_PER_TICKET}到達ボーナス: キャラクターチケット を{diff}枚 入手");
        FriendMenuController.ShowToast(
            $"キャラクターチケットを{diff}枚入手！\nバッグから使うと新しいキャラクターを作れます");
        Debug.Log($"[CharTicket] {diff}枚付与（milestones: {total - diff} → {total}）");
    }
}
