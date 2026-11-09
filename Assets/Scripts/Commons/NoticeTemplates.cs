using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// お知らせ通知の定型文（2026-11-10 ユーザー指定）。アプリの送信画面（SpaceNoticeComposer）で、入力欄の下にボタンで並べる。
/// 増やしたいときはここに書き足す（ボタンは文の長さに合わせた幅で並び、入りきらなければ折り返す。画面もそのぶん下へ伸びる）。
///  ・タイトル … 押すとタイトルが置き換わる
///  ・本文     … 押すと本文の最後に足す（すでに文があれば改行してから）。「」があれば、その中にカーソルを置いて
///               入力をはじめるので、続けてゲームの名前などを打てる。何回か押せば、何行か並べられる
/// </summary>
public static class NoticeTemplates
{
    public static readonly string[] Titles =
    {
        "新しいボードゲームが届きました！",
        "相席募集のお客様がいらっしゃいます！",
        "本日臨時休業です。申し訳ございません。",
    };

    public static readonly string[] Bodies =
    {
        "「」が新しく棚に入りました！",
    };

    /// <summary>本文の定型文の中で、カーソルを置く場所（この2文字のあいだ）</summary>
    private const string HOLE = "「」";

    public const string T_BODY_FULL = "本文の文字数の上限をこえるので、定型文を足せません";

    public static string GlyphText => string.Concat(Titles) + string.Concat(Bodies) + T_BODY_FULL;

    /// <summary>
    /// 本文 current の最後に定型文を足した文と、カーソルを置く位置（「」があればその中、なければ最後）。
    /// 文字数の上限 max をこえるなら null
    /// </summary>
    public static (string text, int caret)? AppendBody(string current, string template, int max)
    {
        current ??= "";
        string sep = current.Length == 0 || current.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n";
        string text = current + sep + template;
        if (text.Length > max) return null;
        int hole = template.IndexOf(HOLE, StringComparison.Ordinal);
        int caret = current.Length + sep.Length + (hole >= 0 ? hole + 1 : template.Length);
        return (text, caret);
    }

    /// <summary>
    /// 定型文のボタンを、幅 width に収まるよう左から並べて折り返す（ボタンの幅は文の長さに合わせる）。
    /// topLeft は親の anchor から見た、並びの左上。戻り値は最後の行の下端の y
    /// </summary>
    public static float BuildChips(RectTransform parent, Vector2 anchor, Vector2 topLeft, float width,
        IReadOnlyList<string> items, float fontSize, float height, float gap, Action<string> onPick)
    {
        const float PAD = 20f;
        float x = 0f, y = topLeft.y;
        for (int i = 0; i < items.Count; i++)
        {
            string item = items[i];
            var rt = SpaceTheme.Rect("Template_" + i, parent, anchor, Vector2.zero, new Vector2(width, height),
                new Vector2(0f, 1f));
            var face = SpaceTheme.Rounded(rt, new Color(0.07f, 0.09f, 0.24f, 0.85f), height * 0.5f, true);
            var edgeRt = SpaceTheme.Rect("Edge", rt);
            SpaceTheme.Stretch(edgeRt);
            SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.A(SpaceTheme.CYAN, 0.35f), height * 0.5f);
            var label = SpaceTheme.Text(rt, "Label", item, fontSize, SpaceTheme.TEXT_SUB, FontStyles.Bold);
            SpaceTheme.Stretch(label.rectTransform, PAD, 0f, PAD, 0f);

            float w = Mathf.Min(width, Mathf.Ceil(label.GetPreferredValues(item).x) + PAD * 2f);
            if (x > 0f && x + w > width)
            {
                x = 0f;
                y -= height + gap;
            }
            rt.sizeDelta = new Vector2(w, height);
            rt.anchoredPosition = new Vector2(topLeft.x + x, y);
            x += w + gap;
            SpaceTheme.Clickable(rt.gameObject, face, () => onPick(item));
        }
        return items.Count == 0 ? topLeft.y : y - height;
    }
}
