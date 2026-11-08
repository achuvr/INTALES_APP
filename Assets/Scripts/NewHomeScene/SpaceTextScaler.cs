using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 文字の大きさの設定（SpaceTheme.TextScale。2026-11-07 ユーザー依頼「お年寄りのお客様もいるので」）を、
/// SpaceTheme.Text で作った文字に反映する。SpaceTheme.Text が全部の文字に付ける。
///
/// 作った直後（Start の時点）の大きさを「標準」として覚え、設定の倍率だけ大きくする。
/// ただし枠の横幅からはみ出さないよう、TextMeshPro の自動サイズで「標準〜標準×倍率」の範囲に収める
/// （せまい枠の文字は標準のまま。標準より小さくはならない）。枠の高さも倍率ぶん広げる（中心はそのまま）。
/// 設定を変えたら ApplyAll で、いま出ている文字にもすぐ反映する。
/// </summary>
public class SpaceTextScaler : MonoBehaviour
{
    private static readonly List<SpaceTextScaler> s_all = new List<SpaceTextScaler>();

    private TMP_Text _t;
    private bool _captured;
    private float _size, _min, _max, _h;
    private bool _auto, _fixedH;
    private float _appliedH; // いま広げている高さ（もう一度反映するときに元へ戻すため）

    private void Awake()
    {
        _t = GetComponent<TMP_Text>();
        s_all.Add(this);
    }

    private void OnDestroy() => s_all.Remove(this);

    private void Start()
    {
        Capture();
        Apply();
    }

    private void Capture()
    {
        if (_t == null) return;
        _size = _t.fontSize;
        _auto = _t.enableAutoSizing;
        _min = _t.fontSizeMin;
        _max = _t.fontSizeMax;
        var rt = _t.rectTransform;
        _fixedH = Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y);
        _h = rt.sizeDelta.y;
        _appliedH = _h;
        _captured = true;
    }

    private void Apply()
    {
        if (!_captured || _t == null) return;
        float s = SpaceTheme.TextScale;

        // 枠の高さ: 中心の位置を変えずに広げる（あとから別の処理が高さを変えていたら、そちらに合わせる）
        var rt = _t.rectTransform;
        if (_fixedH && _h > 0f)
        {
            if (!Mathf.Approximately(rt.sizeDelta.y, _appliedH)) _h = rt.sizeDelta.y;
            float newH = _h * s;
            float delta = newH - rt.sizeDelta.y;
            rt.anchoredPosition += new Vector2(0f, delta * (rt.pivot.y - 0.5f));
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, newH);
            _appliedH = newH;
        }

        if (s <= 1.001f)
        {
            _t.enableAutoSizing = _auto;
            _t.fontSizeMin = _min;
            _t.fontSizeMax = _max;
            _t.fontSize = _size;
            return;
        }
        if (_auto)
        {
            _t.fontSizeMin = _min;
            _t.fontSizeMax = _max * s;
        }
        else
        {
            _t.fontSizeMin = _size;
            _t.fontSizeMax = _size * s;
            _t.fontSize = _size * s;
        }
        _t.enableAutoSizing = true;
    }

    /// <summary>あとから文字の大きさを変える（標準の大きさで指定する。設定の倍率はここで掛ける）</summary>
    public static void SetSize(TMP_Text t, float size)
    {
        if (t == null) return;
        var sc = t.GetComponent<SpaceTextScaler>();
        if (sc == null || !sc._captured)
        {
            t.fontSize = size;
            return;
        }
        sc._size = size;
        sc._max = size;
        sc.Apply();
    }

    /// <summary>倍率を掛けない文字にする（文字の大きさの設定画面の見本など）</summary>
    public static void Exclude(TMP_Text t)
    {
        if (t == null) return;
        var sc = t.GetComponent<SpaceTextScaler>();
        if (sc != null) Destroy(sc);
    }

    /// <summary>いま出ている文字すべてに設定を反映する</summary>
    public static void ApplyAll()
    {
        foreach (var s in s_all.ToArray())
            if (s != null) s.Apply();
    }
}
