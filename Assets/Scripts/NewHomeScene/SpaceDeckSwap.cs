using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 山札座の「1枚引いて1枚捨てる」（2026-11-13 ユーザー指定「山札座をアクティブにすると1日1回だけ、山札から1枚引いて、
/// 1枚捨てるをできるように」）。山札座（デッキ構築）が完成していて、トランプのカードを持てる人（宝石座か切り札座も完成）が使える
/// （CardDeck.SwapUnlocked。管理者は初めから）。1営業日に1回（朝4時切り替え。管理者も同じ）で、来店・ショップで引く1枚とは別に数える。
///  1) 確かめる（SpaceDialog）
///  2) 山札から1枚引く（CardDeck.SwapDrawAsync。cards.swap_day に今日の営業日、cards.swap_pending に true）
///  3) めくって見せる（SpaceNotice.CardDrawn）
///  4) 手札から捨てるカードを1枚選ぶ（SpaceNotice.CardDiscard。引いたカードも選べる。選ぶまで閉じない。
///     アプリを閉じても、次にホームを開いたときにもう一度出る＝NewHomeController.CheckCardDiscard）
/// 入口は所持品のトランプの欄の「山札座」の行と、ポーカーの画面のボタン。ホームの空の押せる星座の置き場は
/// いちばん狭い iPad で星座2つ分しかなく、左右とも埋まっている（SpaceBackground.TAPPABLE）ので、空の山札座は押せる星座にしていない
/// </summary>
public static class SpaceDeckSwap
{
    public const string T_CAPTION = "DECK";
    public const string T_NAME = "山札座";
    public const string T_ROW_DESC = "山札から1枚引いて、1枚捨てる（1日1回）";
    public const string T_READY = "使えます";
    public const string T_DONE = "今日は使いました";
    public const string T_PENDING = "1枚捨てる";
    public const string T_BUTTON = "山札座で1枚引いて1枚捨てる";
    public const string T_BUTTON_DONE = "山札座は今日使いました";
    public const string T_BUTTON_PENDING = "捨てるカードを選ぶ";
    public const string T_CONFIRM_TITLE = "山札から1枚引きますか？";
    public const string T_CONFIRM_BODY = "引いたあとに、手札から捨てるカードを1枚選びます\n山札座で引けるのは1日1回です";
    public const string T_CONFIRM_OK = "引く";
    public const string T_NOTE = "このあと、手札から捨てるカードを1枚選びます";
    public const string T_LOCKED = "山札座を完成させると使えます";

    /// <summary>この機能で表示しうる文字（CardDeck.GlyphText に含めて、NewHome の起動時にフォントへ登録する）</summary>
    public static string GlyphText => new StringBuilder()
        .Append(T_CAPTION).Append(T_NAME).Append(T_ROW_DESC).Append(T_READY).Append(T_DONE).Append(T_PENDING)
        .Append(T_BUTTON).Append(T_BUTTON_DONE).Append(T_BUTTON_PENDING).Append(T_CONFIRM_TITLE).Append(T_CONFIRM_BODY)
        .Append(T_CONFIRM_OK).Append(T_NOTE).Append(T_LOCKED).ToString();

    private static readonly Color FALLBACK = SpaceTheme.Hex(0x9B7BFF);

    /// <summary>山札座の決まり（Constellations.All の "deck"）</summary>
    public static ConstellationDef Def => Array.Find(Constellations.All, d => d.Id == CardDeck.DECK);

    /// <summary>山札座の色（すみれ色。お知らせの見出し・行の縁・ボタンの縁）</summary>
    public static Color Tint
    {
        get
        {
            var def = Def;
            return def != null ? def.Color : FALLBACK;
        }
    }

    private static bool _busy;

    /// <summary>
    /// 入口（所持品の行・ポーカーの画面のボタン）から始める: 捨てる前なら捨てるカードを選ぶ画面、
    /// 今日もう使ったら知らせるだけ、それ以外は確かめてから引く
    /// </summary>
    public static void Start(Canvas canvas)
    {
        if (_busy) return;
        if (!CardDeck.SwapUnlocked)
        {
            SpaceTheme.Toast(T_LOCKED, canvas);
            return;
        }
        var s = CardDeck.Current;
        if (CardRules.MustDiscard(s))
        {
            SpaceTheme.Toast(CardDeck.DiscardFirstText(s), canvas);
            SpaceNotice.CardDiscard();
            return;
        }
        if (s.SwapDay == StoreCalendar.TodayKey)
        {
            SpaceTheme.Toast(CardDeck.T_SWAP_DONE, canvas);
            return;
        }
        SpaceDialog.Confirm(canvas, T_CONFIRM_TITLE, T_CONFIRM_BODY, T_CONFIRM_OK, false, () => DrawAsync(canvas).Forget());
    }

    /// <summary>山札から1枚引いて、めくって見せてから、捨てるカードを選ぶ画面を出す</summary>
    private static async UniTaskVoid DrawAsync(Canvas canvas)
    {
        if (_busy) return;
        _busy = true;
        CardDeck.SwapPlay res;
        try
        {
            res = await CardDeck.SwapDrawAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpaceDeckSwap] 山札座で引けませんでした: {ex.Message}");
            res = new CardDeck.SwapPlay { Error = CardDeck.T_ERR_NETWORK };
        }
        _busy = false;
        if (res == null || !res.Ok)
        {
            SpaceTheme.Toast(res?.Error ?? CardDeck.T_ERR_NETWORK, canvas);
            if (CardDeck.MustDiscard) SpaceNotice.CardDiscard(); // ほかの端末で引いて、まだ捨てていなかった
            return;
        }
        AssetsDatabase.instance?.PlayLevelUpSE();
        int card = res.Card;
        SpaceNoticeQueue.Run(new List<Func<GameObject>>
        {
            () => SpaceNotice.CardDrawn(new List<int> { card }, T_CAPTION, false, Tint, T_NOTE),
            () => CardDeck.MustDiscard ? SpaceNotice.CardDiscard() : null,
        }, null);
    }
}
