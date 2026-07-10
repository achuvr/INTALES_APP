using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ゲームタイトル選択用の軽量な仮想スクロールリスト。
/// 行を全件生成せず、見えているぶんのプール（十数行）を使い回す
/// （図鑑リストと同じ考え方の簡易版）。カタログは約740件あるため、
/// 全行生成するとTMPの大量生成で実機OOMの恐れがあり、この作りは必須。
///
/// 使う側: RecruitBoardController（募集作成のゲーム選択・管理者のタイトル設定）、
///         GameImageAdminController（画像を登録するゲームの選択）。
/// </summary>
public class TitlePickerList : MonoBehaviour
{
    private const float ROW_H = 96f;
    private const float ROW_STEP = 108f;

    private static readonly Color C_ROW = new Color(1.00f, 0.99f, 0.93f, 1.00f);
    private static readonly Color C_INK = new Color(0.30f, 0.18f, 0.06f, 1.00f);

    private ScrollRect _scroll;
    private RectTransform _content;
    private TMP_FontAsset _jp;
    private Action<string> _onPick;
    private List<string> _items = new List<string>();
    private int _generation; // SetItemsのたびに進める（行の再バインド判定用）

    private class Row
    {
        public GameObject Go;
        public RectTransform Rt;
        public TextMeshProUGUI Label;
        public int BoundIndex = -1;
        public int BoundGeneration = -1;
        public string BoundTitle;
    }
    private readonly List<Row> _pool = new List<Row>();
    private int _poolTarget;

    /// <summary>parent の中に、中心 center・大きさ width×height のスクロールリストを作る。</summary>
    public static TitlePickerList Create(Transform parent, Vector2 center, float width, float height,
        TMP_FontAsset jp, Action<string> onPick)
    {
        var svGO = new GameObject("__TitlePicker");
        svGO.transform.SetParent(parent, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.sizeDelta = new Vector2(width, height);
        svrt.anchoredPosition = center;

        var list = svGO.AddComponent<TitlePickerList>();
        list._jp = jp;
        list._onPick = onPick;

        var scroll = svGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        list._scroll = scroll;

        var vpGO = new GameObject("__VP");
        vpGO.transform.SetParent(svGO.transform, false);
        var vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = vprt.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();
        scroll.viewport = vprt;

        var ctGO = new GameObject("__Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        var ct = ctGO.AddComponent<RectTransform>();
        ct.anchorMin = new Vector2(0f, 1f); ct.anchorMax = new Vector2(1f, 1f);
        ct.pivot = new Vector2(0.5f, 1f);
        ct.offsetMin = ct.offsetMax = Vector2.zero;
        scroll.content = ct;
        list._content = ct;

        list._poolTarget = Mathf.CeilToInt(height / ROW_STEP) + 2;
        scroll.onValueChanged.AddListener(_ => list.Refresh());
        list.StartCoroutine(list.FillPool());
        return list;
    }

    /// <summary>表示するタイトル一覧を差し替える（検索のたびに呼ぶ。行の生成は起きない）。</summary>
    public void SetItems(List<string> items)
    {
        _items = items ?? new List<string>();
        _generation++;
        _content.sizeDelta = new Vector2(0f, _items.Count * ROW_STEP + 12f);
        _content.anchoredPosition = Vector2.zero;      // 先頭へ戻す
        if (_scroll != null) _scroll.velocity = Vector2.zero; // 慣性スクロールも止める
        Refresh();
    }

    // ================================================================
    // グリフの事前登録（実機OOM対策。詳細は BoardGameListController 参照）
    // ================================================================
    private static readonly HashSet<char> _warmed = new HashSet<char>();

    /// <summary>全タイトルの文字を40文字/フレームでフォントアトラスへ登録しておく。</summary>
    public void PrewarmAsync(IEnumerable<string> titles)
    {
        StartCoroutine(PrewarmCo(titles));
    }

    private IEnumerator PrewarmCo(IEnumerable<string> titles)
    {
        if (_jp == null) yield break;
        var sb = new System.Text.StringBuilder();
        foreach (var t in titles)
        {
            if (t == null) continue;
            foreach (var c in t) if (_warmed.Add(c)) sb.Append(c);
        }
        string all = sb.ToString();
        for (int i = 0; i < all.Length; i += 40)
        {
            _jp.TryAddCharacters(all.Substring(i, Mathf.Min(40, all.Length - i)), out _);
            yield return null;
        }
    }

    // ================================================================
    // 行プールと再バインド
    // ================================================================

    /// <summary>行プールを1フレーム4行まで生成（TMP大量生成のOOM対策）。</summary>
    private IEnumerator FillPool()
    {
        while (_pool.Count < _poolTarget)
        {
            for (int i = 0; i < 4 && _pool.Count < _poolTarget; i++)
                _pool.Add(CreateRow());
            Refresh();
            yield return null;
        }
    }

    private Row CreateRow()
    {
        var go = new GameObject("__Row");
        go.transform.SetParent(_content, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        var img = go.AddComponent<Image>();
        RoundedRectSprite.Apply(img);
        img.color = C_ROW;

        var lgo = new GameObject("__Label");
        lgo.transform.SetParent(go.transform, false);
        var lrt = lgo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(24f, 0f); lrt.offsetMax = new Vector2(-24f, 0f);
        var tmp = lgo.AddComponent<TextMeshProUGUI>();
        if (_jp != null) tmp.font = _jp;
        tmp.fontSize = 30;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = C_INK;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 20; tmp.fontSizeMax = 30;

        var row = new Row { Go = go, Rt = rt, Label = tmp };
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => { if (row.BoundTitle != null) _onPick?.Invoke(row.BoundTitle); });
        go.SetActive(false);
        return row;
    }

    /// <summary>スクロール位置に合わせて見えている行だけを配置・バインドし直す。</summary>
    private void Refresh()
    {
        if (_content == null) return;
        float y = Mathf.Max(0f, _content.anchoredPosition.y);
        int first = Mathf.Max(0, Mathf.FloorToInt(y / ROW_STEP) - 1);
        for (int i = 0; i < _pool.Count; i++)
        {
            var row = _pool[i];
            int idx = first + i;
            bool on = idx < _items.Count;
            if (row.Go.activeSelf != on) row.Go.SetActive(on);
            if (!on) { row.BoundIndex = -1; continue; }

            row.Rt.offsetMin = new Vector2(6f, -(6f + idx * ROW_STEP) - ROW_H);
            row.Rt.offsetMax = new Vector2(-6f, -(6f + idx * ROW_STEP));
            if (row.BoundIndex == idx && row.BoundGeneration == _generation) continue;

            string title = _items[idx];
            if (_jp != null) _jp.TryAddCharacters(title, out _); // 未登録グリフの保険（通常はPrewarm済み）
            row.Label.text = title;
            row.BoundTitle = title;
            row.BoundIndex = idx;
            row.BoundGeneration = _generation;
        }
    }
}
