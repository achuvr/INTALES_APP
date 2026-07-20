using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// アプリ全体のデザイン基盤（デザイントークン＋品質向上エフェクト）。
///
/// 「羊皮紙×ファンタジー」の世界観はそのままに、見た目のチープさの原因だった
/// 3つの欠落をアセット不要のプロシージャル実装で補う:
///   1. 奥行き（ソフトドロップシャドウ）  … AddSoftShadow / ElevateCard
///   2. 面の立体感（上光グラデーション）    … UIVerticalGradient / AddGradient
///   3. 触感（押下時の沈み込み）            … UIPressEffect / AddPressEffect
///
/// 使い方: 各画面の MakeButton / BuildPanel / カード生成箇所から
///   UITheme.PolishButton(img) …… ボタン（グラデ＋影＋押下）
///   UITheme.ElevateCard(go)   …… カード・パネル（影のみ）
/// を呼ぶだけ。色は下のトークンを参照して画面間で統一する。
/// </summary>
public static class UITheme
{
    // ================================================================
    // デザイントークン（配色。全画面でこの値に寄せる）
    // ================================================================
    public static readonly Color INK        = new Color(0.26f, 0.15f, 0.05f, 1f); // 本文
    public static readonly Color TITLE      = new Color(0.36f, 0.15f, 0.04f, 1f); // 見出し
    public static readonly Color MUTED      = new Color(0.52f, 0.38f, 0.22f, 1f); // 補足
    public static readonly Color PARCHMENT  = new Color(0.99f, 0.95f, 0.84f, 1f); // パネル地
    public static readonly Color CARD       = new Color(0.97f, 0.92f, 0.78f, 1f); // カード地
    public static readonly Color GOLD       = new Color(0.84f, 0.66f, 0.18f, 1f); // アクセント
    public static readonly Color GOLD_DEEP  = new Color(0.66f, 0.49f, 0.10f, 1f); // アクセント濃
    public static readonly Color TITLEBAR   = new Color(0.35f, 0.20f, 0.07f, 0.95f);
    public static readonly Color DANGER     = new Color(0.78f, 0.16f, 0.16f, 1f);
    public static readonly Color DIM        = new Color(0f, 0f, 0f, 0.65f);       // モーダルの暗幕

    // ボタン面のグラデーション（白ボタン用。element色に乗算される）
    private static readonly Color BTN_GRAD_TOP    = new Color(1f, 1f, 1f, 1f);
    private static readonly Color BTN_GRAD_BOTTOM = new Color(0.93f, 0.89f, 0.80f, 1f);
    // 濃色（タイトル帯・金ボタンなど）用の控えめグラデーション
    private static readonly Color DARK_GRAD_TOP    = new Color(1.08f, 1.08f, 1.08f, 1f);
    private static readonly Color DARK_GRAD_BOTTOM = new Color(0.88f, 0.88f, 0.88f, 1f);

    // ================================================================
    // 合成ヘルパー
    // ================================================================

    /// <summary>
    /// ボタンを磨く: 上光グラデーション＋ソフトシャドウ＋押下の沈み込み。
    /// uiScale はシーン側ボタンのように transform.scale がかかっている場合の補正
    /// （影の広がりを見た目で一定に保つ）。二重適用は自動で防ぐ。
    /// </summary>
    public static void PolishButton(Image img, float uiScale = 1f)
    {
        if (img == null || img.GetComponent<UIVerticalGradient>() != null) return;
        float s = Mathf.Max(1f, uiScale);
        AddGradient(img.gameObject, BTN_GRAD_TOP, BTN_GRAD_BOTTOM);
        AddSoftShadow(img.rectTransform, 10f / s, 5f / s, 0.30f);
        AddPressEffect(img.gameObject);
    }

    /// <summary>濃い色のボタン（金・赤など）を磨く。白グラデだと色が飛ぶため控えめ版。</summary>
    public static void PolishDarkButton(Image img, float uiScale = 1f)
    {
        if (img == null || img.GetComponent<UIVerticalGradient>() != null) return;
        float s = Mathf.Max(1f, uiScale);
        AddGradient(img.gameObject, DARK_GRAD_TOP, DARK_GRAD_BOTTOM);
        AddSoftShadow(img.rectTransform, 10f / s, 5f / s, 0.32f);
        AddPressEffect(img.gameObject);
    }

    /// <summary>カード・パネルを持ち上げる（ソフトシャドウのみ）。二重適用は自動で防ぐ。</summary>
    public static void ElevateCard(GameObject go, float spread = 14f, float drop = 7f, float alpha = 0.28f)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null || go.transform.Find("__Shadow") != null) return;
        AddSoftShadow(rt, spread, drop, alpha);
    }

    /// <summary>タイトル帯を磨く（グラデーション＋下辺に金の飾り線）。</summary>
    public static void PolishTitleBar(GameObject bar)
    {
        if (bar.GetComponent<UIVerticalGradient>() == null)
            AddGradient(bar, DARK_GRAD_TOP, DARK_GRAD_BOTTOM);
        if (bar.transform.Find("__GoldRule") != null) return;
        var rule = new GameObject("__GoldRule");
        rule.transform.SetParent(bar.transform, false);
        var rrt = rule.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0f, 0f); rrt.anchorMax = new Vector2(1f, 0f);
        rrt.pivot = new Vector2(0.5f, 0f);
        rrt.offsetMin = new Vector2(0f, -3f); rrt.offsetMax = new Vector2(0f, 3f);
        var img = rule.AddComponent<Image>();
        img.color = GOLD;
        img.raycastTarget = false;
    }

    // ================================================================
    // 個別エフェクト
    // ================================================================

    /// <summary>上光の縦グラデーションを付ける（Imageの色に乗算される）。</summary>
    public static void AddGradient(GameObject go, Color top, Color bottom)
    {
        var g = go.GetComponent<UIVerticalGradient>();
        if (g == null) g = go.AddComponent<UIVerticalGradient>();
        g.Top = top;
        g.Bottom = bottom;
    }

    /// <summary>
    /// ソフトドロップシャドウ。対象の先頭子として、リング状（面の内側は完全透明・
    /// 面の縁で最大→外側へ減衰）の角丸スプライトを敷く。
    ///
    /// uGUIは親→子の順で描画するため、子に「中まで塗った影」を置くと面自体が
    /// 暗く汚れてしまう。そこで pixelsPerUnitMultiplier でスプライトのぼかし幅を
    /// spread に正確に一致させ、面が覆う領域のアルファを0にしている。
    /// </summary>
    public static void AddSoftShadow(RectTransform target, float spread, float drop, float alpha)
    {
        spread = Mathf.Max(4f, spread);
        var go = new GameObject("__Shadow");
        go.transform.SetParent(target, false);
        go.transform.SetAsFirstSibling();
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        // 上・左右は面の縁で影がピークになるよう spread ぶんだけ広げ、
        // 下は drop ぶん余分に伸ばして「下に落ちる影」を作る
        rt.offsetMin = new Vector2(-spread, -(spread + drop));
        rt.offsetMax = new Vector2(spread, spread);
        var img = go.AddComponent<Image>();
        img.sprite = RoundedShadowSprite.Get();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.20f, 0.10f, 0.02f, alpha); // 世界観に合わせた茶系の影
        // ぼかし幅（RAMP_PX）が見た目 spread ぴったりになるよう slice を拡縮する
        img.pixelsPerUnitMultiplier = RoundedShadowSprite.RAMP_PX / spread;
        img.raycastTarget = false;
        // 親が LayoutGroup 持ちでも影をレイアウト対象にさせない（挿入位置が先頭子のため必須）
        go.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    /// <summary>押下時にわずかに沈み込むフィードバックを付ける。</summary>
    public static void AddPressEffect(GameObject go)
    {
        if (go.GetComponent<UIPressEffect>() == null) go.AddComponent<UIPressEffect>();
    }
}

/// <summary>
/// 縦方向グラデーション（頂点カラー乗算）。Imageの上に足すだけで面が立体的に見える。
/// </summary>
public class UIVerticalGradient : BaseMeshEffect
{
    public Color Top = Color.white;
    public Color Bottom = Color.white;

    private static readonly List<UIVertex> _verts = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        _verts.Clear();
        vh.GetUIVertexStream(_verts);

        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < _verts.Count; i++)
        {
            float y = _verts[i].position.y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        float h = Mathf.Max(0.0001f, maxY - minY);

        for (int i = 0; i < _verts.Count; i++)
        {
            var v = _verts[i];
            float t = (v.position.y - minY) / h;
            var g = Color.Lerp(Bottom, Top, t);
            v.color = new Color32(
                (byte)Mathf.Clamp(v.color.r * g.r, 0f, 255f),
                (byte)Mathf.Clamp(v.color.g * g.g, 0f, 255f),
                (byte)Mathf.Clamp(v.color.b * g.b, 0f, 255f),
                v.color.a);
            _verts[i] = v;
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(_verts);
    }
}

/// <summary>
/// 押下時に0.965倍へ沈み、離すとすっと戻る触感エフェクト。
/// スクロール中の誤反応を避けるため沈み込みはごく浅くしてある。
/// </summary>
public class UIPressEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private const float PRESS_SCALE = 0.965f;
    private Vector3 _baseScale;
    private Coroutine _anim;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    public void OnPointerDown(PointerEventData e) => AnimateTo(_baseScale * PRESS_SCALE, 0.05f);
    public void OnPointerUp(PointerEventData e) => AnimateTo(_baseScale, 0.10f);
    public void OnPointerExit(PointerEventData e) => AnimateTo(_baseScale, 0.10f);

    private void AnimateTo(Vector3 target, float duration)
    {
        if (!gameObject.activeInHierarchy) { transform.localScale = target; return; }
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(Animate(target, duration));
    }

    private System.Collections.IEnumerator Animate(Vector3 target, float duration)
    {
        var from = transform.localScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = 1f - (1f - k) * (1f - k); // easeOutQuad
            transform.localScale = Vector3.LerpUnclamped(from, target, k);
            yield return null;
        }
        transform.localScale = target;
        _anim = null;
    }

    private void OnDisable()
    {
        transform.localScale = _baseScale;
        _anim = null;
    }
}

/// <summary>
/// リング状のソフトシャドウ用スプライト（9スライス・実行時生成）。
/// 外縁(アルファ0)→内側へ RAMP_PX かけてなめらかに立ち上がり、ピーク直後の
/// FALL_PX で0へ落ち、それより内側（親の面が覆う領域）は完全透明。
/// AddSoftShadow が pixelsPerUnitMultiplier=RAMP_PX/spread で貼ることで、
/// ピークがちょうど親の面の縁に一致する。
/// </summary>
public static class RoundedShadowSprite
{
    private const int SIZE = 128;
    private const float RADIUS = 52f;   // SDFの角丸半径
    public const float RAMP_PX = 44f;   // 外縁からピークまでの立ち上がり幅
    private const float FALL_PX = 8f;   // ピークから内側0までの幅
    private static Sprite _sprite;

    public static Sprite Get()
    {
        if (_sprite != null) return _sprite;

        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var px = new Color32[SIZE * SIZE];
        float half = SIZE / 2f;
        float b = half - 1f - RADIUS;

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                // 角丸矩形のSDF。d<0が内側。外縁(d=0)からの深さ e=-d で判定する
                float qx = Mathf.Abs(x + 0.5f - half) - b;
                float qy = Mathf.Abs(y + 0.5f - half) - b;
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - RADIUS;
                float e = -d; // 外縁からの深さ（内側ほど大）
                float a;
                if (e <= 0f) a = 0f;                                  // 完全に外
                else if (e <= RAMP_PX)                                 // 立ち上がり
                {
                    float t = e / RAMP_PX;
                    a = t * t * (3f - 2f * t);                        // smoothstep
                }
                else if (e <= RAMP_PX + FALL_PX)                       // ピーク直後の落ち
                    a = 1f - (e - RAMP_PX) / FALL_PX;
                else a = 0f;                                           // 面の内側は透明
                px[y * SIZE + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        float border = RAMP_PX + FALL_PX + 4f;
        _sprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        return _sprite;
    }
}
