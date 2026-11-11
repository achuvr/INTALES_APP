using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// アプリ内の通貨（単位 G。2026-11-12 ユーザー指定）。
///  ・単位は最初「IG」だったが、「1IG」の I が 1 と見間違えやすいので、同日ユーザー指定で「G」に変えた。
///    コードやデータの名前（IgCurrency・users.ig・履歴の種類 ig・IgCoin.png）は IG のまま
///  ・金貨座（経済・経営の星座。Constellations の "coin"）を完成（Lv.1）させると使えるようになる。管理者は初めから
///    （解放条件のある機能は管理者だけ初めから。もらえる回数などの決まりは同じ）
///  ・使えるようになったあとは、来店するたびに 1G（来店回数と同じく1営業日に1回。入店しなおしても増えない。
///    判定は JourneyRules.ApplyVisit、書き込みは JourneyService の入店のトランザクションで FieldValue.Increment）
///  ・表示は「5G」のように数字のすぐあとに G（Format）。所持金は所持品のいちばん上の欄（SpaceInventoryPanel）
///  ・保存は users/{uid}.ig（int。UserData.Ig。users は本人が書けるのでルールの変更はなし）
///  ・絵は Resources/ItemIcon/IgCoin.png（「G」の金貨。tools/coin/make_ig_coin_icon.py で作る）
/// 今は貯まるだけで、使い道はまだない。
/// </summary>
public static class IgCurrency
{
    /// <summary>users のフィールド名</summary>
    public const string FIELD = "ig";
    /// <summary>単位（数字のすぐあとに付ける。2026-11-12 に IG から変更）</summary>
    public const string UNIT = "G";
    /// <summary>使えるようになる星座（金貨座）</summary>
    public const string CONSTELLATION = "coin";
    /// <summary>履歴の種類（SpaceHistoryPanel）</summary>
    public const string HISTORY = "ig";

    /// <summary>来店1回（1営業日）でもらえる数</summary>
    public static int PerVisit => JourneyRules.IG_PER_VISIT;

    /// <summary>
    /// 金貨座がまだ完成していない人が G を持ったとき（管理者の G のQRで受け取れる。2026-11-12）に添える一言
    /// </summary>
    public const string T_LOCKED_NOTE = "金貨座を完成させると、ショップで使えるようになります";

    /// <summary>「5G」「1,200G」のように書く</summary>
    public static string Format(int n) => n.ToString("N0", CultureInfo.InvariantCulture) + UNIT;

    /// <summary>使えるようになっているか（金貨座が完成している。管理者は初めから）。来店でもらえるのはこのときだけ</summary>
    public static bool Unlocked
    {
        get
        {
            if (AdminClaim.IsAdmin) return true;
            var p = Constellations.Progress().Find(x => x.Def.Id == CONSTELLATION);
            return p != null && p.Complete;
        }
    }

    /// <summary>所持金（G）</summary>
    public static int Balance
    {
        get
        {
            var d = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
            return d != null ? Mathf.Max(0, d.Ig) : 0;
        }
    }

    /// <summary>
    /// 所持品に所持金を出すか。使えるようになっていれば出す。ゲームのジャンルの見直しなどで金貨座が完成でなくなっても、
    /// 持っている分は見えるようにする（来店で増えるのは Unlocked のときだけ）
    /// </summary>
    public static bool Visible => Unlocked || Balance > 0;

    private static Sprite _icon;

    /// <summary>通貨の絵（「G」の金貨。背景は透明。1回だけ読む。見つからなければ null）</summary>
    public static Sprite Icon => _icon != null ? _icon : (_icon = Resources.Load<Sprite>("ItemIcon/IgCoin"));

    /// <summary>数字と単位（NewHome の起動時にまとめてフォントへ登録する）</summary>
    public static string GlyphText => UNIT + "0123456789,+" + T_LOCKED_NOTE;

#if UNITY_EDITOR
    /// <summary>
    /// エディタ専用（NewHome の G キー。2026-11-12）: ログイン中のアカウントに n G を足す（ショップを試すため。本当に書き込む）。
    /// 足せたら true
    /// </summary>
    public static async UniTask<bool> DebugAddAsync(int n)
    {
        var m = UserDataManager.instance;
        if (m == null || string.IsNullOrEmpty(m.UID) || m.UserData == null) return false;
        await FirebaseFirestore.DefaultInstance.Collection("users").Document(m.UID)
            .UpdateAsync(new Dictionary<string, object> { { FIELD, FieldValue.Increment(n) } }).AsUniTask();
        m.UserData.Ig += n;
        LocalHistoryLog.Add(HISTORY, Format(n) + " を入手（デバッグ）");
        return true;
    }
#endif
}
