using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ElementEffectController : MonoBehaviour
{
    public static ElementEffectController instance { get; private set; }

    private Canvas _canvas;
    private readonly List<GameObject> _particles = new List<GameObject>();

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
    }

    private Canvas GetMainCanvas()
    {
        var go = GameObject.Find("Canvas");
        return go != null ? go.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
    }

    public void PlayEffect(string element)
    {
        if (_canvas == null) _canvas = GetMainCanvas();
        StopAllCoroutines();
        ClearParticles();
        switch (element)
        {
            case "fire":    StartCoroutine(FireEffect());    break;
            case "water":   StartCoroutine(WaterEffect());   break;
            case "nature":  StartCoroutine(NatureEffect());  break;
            case "thunder": StartCoroutine(ThunderEffect()); break;
        }
    }

    // ================================================================
    // 炎：画面下部から粒子が激しく噴き上がる爆発的な演出
    //   - 全粒子が画面下部(y=0~0.1)から発射
    //   - 速い・大きい・赤/橙/黄のみ
    //   - 強烈な2段フラッシュ
    // ================================================================
    private IEnumerator FireEffect()
    {
        // 第1撃フラッシュ（強）
        StartCoroutine(FlashScreen(new Color(1f, 0.05f, 0f, 0.55f), 0.2f));

        // 画面下部から一気に噴き上がる粒子（集中砲火）
        Color[] cols = {
            new Color(1f, 0.05f, 0f),  // 深紅
            new Color(1f, 0.3f, 0f),   // 赤橙
            new Color(1f, 0.55f, 0f),  // 橙
            new Color(1f, 0.8f, 0f),   // 黄橙
        };

        // 第1波：画面下部の固定ゾーンから真上へ噴き上がる（炎柱）
        for (int i = 0; i < 35; i++)
        {
            float sx = Random.Range(0.2f, 0.8f);     // 画面中央寄りから
            float sy = Random.Range(0f, 0.08f);       // 画面最下部から
            float ex = sx + Random.Range(-0.05f, 0.05f); // ほぼ垂直に上昇
            float ey = Random.Range(0.7f, 1.2f);     // 画面上部まで突き抜ける
            Color c = cols[Random.Range(0, cols.Length)];
            float size = Random.Range(18f, 40f);      // 大きい粒子
            float delay = Random.Range(0f, 0.25f);    // 遅延短め（素早く）
            StartCoroutine(AnimParticle(sx, sy, ex, ey, c, size, 0.7f - delay * 0.3f, delay, false, 0.5f));
            if (i % 5 == 0) yield return null;
        }

        // 第2波：左右端からも噴き上がる（広がり感）
        for (int i = 0; i < 20; i++)
        {
            float sx = i % 2 == 0 ? Random.Range(0f, 0.25f) : Random.Range(0.75f, 1f);
            float sy = Random.Range(0f, 0.12f);
            float ex = sx + (sx < 0.5f ? Random.Range(0f, 0.15f) : Random.Range(-0.15f, 0f));
            float ey = Random.Range(0.5f, 1.0f);
            Color c = cols[Random.Range(0, cols.Length)];
            float size = Random.Range(12f, 28f);
            float delay = Random.Range(0.05f, 0.35f);
            StartCoroutine(AnimParticle(sx, sy, ex, ey, c, size, 0.8f - delay * 0.2f, delay, false, 0.45f));
            if (i % 4 == 0) yield return null;
        }

        // 第2撃フラッシュ（橙・遅れて）
        yield return new WaitForSeconds(0.12f);
        StartCoroutine(FlashScreen(new Color(1f, 0.5f, 0f, 0.3f), 0.5f));

        yield return new WaitForSeconds(1.1f);
        ClearParticles();
    }

    // ================================================================
    // 水：青の雨粒が上から降り注ぐ + 波紋
    // ================================================================
    private IEnumerator WaterEffect()
    {
        StartCoroutine(FlashScreen(new Color(0f, 0.4f, 1f, 0.3f), 0.35f));
        Color[] cols = {
            new Color(0f, 0.5f, 1f),
            new Color(0.2f, 0.75f, 1f),
            new Color(0.5f, 0.9f, 1f),
            new Color(0f, 0.25f, 0.9f)
        };
        for (int i = 0; i < 50; i++)
        {
            float sx = Random.Range(0.02f, 0.98f);
            Color c = cols[Random.Range(0, cols.Length)];
            float w = Random.Range(3f, 8f);
            float h = Random.Range(12f, 30f);
            float delay = Random.Range(0f, 0.5f);
            StartCoroutine(AnimParticleRect(
                sx, Random.Range(0.85f, 1.1f),
                sx + Random.Range(-0.02f, 0.02f), Random.Range(-0.05f, 0.15f),
                c, w, h, 0.9f - delay * 0.2f, delay, 0.6f));
            if (i % 6 == 0) yield return null;
        }
        for (int i = 0; i < 4; i++)
        {
            StartCoroutine(Ripple(
                Random.Range(0.2f, 0.8f), Random.Range(0.2f, 0.7f),
                new Color(0.3f, 0.7f, 1f, 0.4f), Random.Range(0.2f, 0.5f)));
            yield return new WaitForSeconds(0.15f);
        }
        yield return new WaitForSeconds(1.0f);
        ClearParticles();
    }

    // ================================================================
    // 自然：画面全体からゆっくり粒子が浮遊・漂う穏やかな演出
    //   - 全粒子が画面全体（y=0~0.8）のランダム位置からスタート
    //   - 遅い・小さめ・菱形(葉)・左右に揺れながら上昇
    //   - 光の筋がゆっくり上昇
    //   - 薄い緑フラッシュ（穏やか）
    // ================================================================
    private IEnumerator NatureEffect()
    {
        // 薄い緑フラッシュ（炎より圧倒的に穏やか）
        StartCoroutine(FlashScreen(new Color(0.1f, 0.7f, 0.1f, 0.18f), 0.6f));

        Color[] cols = {
            new Color(0.1f, 0.75f, 0.1f),
            new Color(0.3f, 0.9f, 0.2f),
            new Color(0.5f, 1f, 0.35f),
            new Color(0f, 0.5f, 0.1f),
        };

        // 葉っぱ粒子（菱形）：画面全体からゆっくり漂う
        for (int i = 0; i < 40; i++)
        {
            // 画面全体のランダム位置からスタート
            float sx = Random.Range(0.03f, 0.97f);
            float sy = Random.Range(0.05f, 0.75f);  // 画面全体に分散
            // 左右にゆらゆら揺れながら少しだけ上昇
            float ex = sx + Random.Range(-0.2f, 0.2f);  // 大きく横に流れる
            float ey = sy + Random.Range(0.1f, 0.35f);  // 少しだけ上昇（炎より上昇量小）
            Color c = cols[Random.Range(0, cols.Length)];
            float size = Random.Range(8f, 20f);          // 小さめ（炎より小さい）
            float delay = Random.Range(0f, 0.7f);        // 遅延長め（ゆっくり順番に）
            float dur = Random.Range(0.9f, 1.3f);        // 長め（ゆっくり動く）
            StartCoroutine(AnimParticle(sx, sy, ex, ey, c, size, dur, delay, true, 0.6f));
            if (i % 5 == 0) yield return null;
        }

        // 光の筋：画面全体からゆっくり上昇（炎のような下部集中ではなく全体）
        for (int i = 0; i < 10; i++)
        {
            float sx = Random.Range(0.05f, 0.95f);
            float sy = Random.Range(0f, 0.5f);  // 全体から
            StartCoroutine(AnimParticleRect(
                sx, sy, sx + Random.Range(-0.04f, 0.04f), sy + Random.Range(0.4f, 0.7f),
                new Color(0.5f, 1f, 0.4f, 0.5f), 3f, 35f,
                Random.Range(0.8f, 1.2f), Random.Range(0f, 0.5f), 0.45f));
            yield return null;
        }

        yield return new WaitForSeconds(1.2f);
        ClearParticles();
    }

    // ================================================================
    // 雷：中央から四方に爆発、強烈フラッシュ2段
    // ================================================================
    private IEnumerator ThunderEffect()
    {
        StartCoroutine(FlashScreen(new Color(1f, 1f, 0.8f, 0.6f), 0.12f));
        Color[] cols = {
            new Color(1f, 0.95f, 0f),
            new Color(1f, 1f, 0.7f),
            Color.white,
            new Color(0.9f, 0.8f, 0.2f)
        };
        for (int i = 0; i < 70; i++)
        {
            float sx = 0.5f + Random.Range(-0.05f, 0.05f);
            float sy = 0.5f + Random.Range(-0.05f, 0.05f);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(0.3f, 0.7f);
            float ex = sx + Mathf.Cos(angle) * dist;
            float ey = sy + Mathf.Sin(angle) * dist * 1.5f;
            Color c = cols[Random.Range(0, cols.Length)];
            float size = Random.Range(4f, 18f);
            float delay = Random.Range(0f, 0.2f);
            float dur = Random.Range(0.3f, 0.7f);
            StartCoroutine(AnimParticle(sx, sy, ex, ey, c, size, dur, delay, false, 0.3f));
            if (i % 8 == 0) yield return null;
        }
        yield return new WaitForSeconds(0.1f);
        StartCoroutine(FlashScreen(new Color(1f, 1f, 1f, 0.4f), 0.1f));
        yield return new WaitForSeconds(0.15f);
        StartCoroutine(FlashScreen(new Color(1f, 0.9f, 0.2f, 0.25f), 0.35f));
        yield return new WaitForSeconds(1.0f);
        ClearParticles();
    }

    // ================================================================
    // ユーティリティ
    // ================================================================
    private IEnumerator FlashScreen(Color color, float duration)
    {
        if (_canvas == null) yield break;
        var go = new GameObject("__Flash");
        go.transform.SetParent(_canvas.transform, false);
        _particles.Add(go);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        go.transform.SetAsLastSibling();
        float elapsed = 0f;
        while (elapsed < duration && go != null)
        {
            img.color = new Color(color.r, color.g, color.b, color.a * (1f - elapsed / duration));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (go != null) { _particles.Remove(go); Destroy(go); }
    }

    private IEnumerator AnimParticle(
        float fx, float fy, float tx, float ty,
        Color color, float size, float dur, float delay, bool diamond, float fadeStart)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (_canvas == null) yield break;
        var go = new GameObject("__P");
        go.transform.SetParent(_canvas.transform, false);
        _particles.Add(go);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);
        rt.anchorMin = rt.anchorMax = new Vector2(fx, fy);
        rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        if (diamond) go.transform.rotation = Quaternion.Euler(0, 0, 45f);
        go.transform.SetAsLastSibling();
        Vector2 sa = new Vector2(fx, fy), ea = new Vector2(tx, ty);
        float elapsed = 0f;
        while (elapsed < dur && go != null)
        {
            float t = elapsed / dur;
            rt.anchorMin = rt.anchorMax = Vector2.Lerp(sa, ea, t);
            rt.anchoredPosition = Vector2.zero;
            float alpha = t < fadeStart ? 1f : 1f - (t - fadeStart) / (1f - fadeStart);
            img.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
            float sc = Mathf.Lerp(1f, 0.2f, Mathf.Max(0f, t - fadeStart));
            go.transform.localScale = Vector3.one * sc;
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (go != null) { _particles.Remove(go); Destroy(go); }
    }

    private IEnumerator AnimParticleRect(
        float fx, float fy, float tx, float ty,
        Color color, float w, float h, float dur, float delay, float fadeStart)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (_canvas == null) yield break;
        var go = new GameObject("__PR");
        go.transform.SetParent(_canvas.transform, false);
        _particles.Add(go);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchorMin = rt.anchorMax = new Vector2(fx, fy);
        rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        go.transform.SetAsLastSibling();
        Vector2 sa = new Vector2(fx, fy), ea = new Vector2(tx, ty);
        float elapsed = 0f;
        while (elapsed < dur && go != null)
        {
            float t = elapsed / dur;
            rt.anchorMin = rt.anchorMax = Vector2.Lerp(sa, ea, t);
            rt.anchoredPosition = Vector2.zero;
            float alpha = t < fadeStart ? 1f : 1f - (t - fadeStart) / (1f - fadeStart);
            img.color = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(alpha));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (go != null) { _particles.Remove(go); Destroy(go); }
    }

    private IEnumerator Ripple(float cx, float cy, Color color, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (_canvas == null) yield break;
        var go = new GameObject("__Ripple");
        go.transform.SetParent(_canvas.transform, false);
        _particles.Add(go);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(cx, cy);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(20f, 20f);
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        go.transform.SetAsLastSibling();
        float dur = 0.6f, elapsed = 0f;
        while (elapsed < dur && go != null)
        {
            float t = elapsed / dur;
            float size = Mathf.Lerp(20f, 200f, t);
            rt.sizeDelta = new Vector2(size, size);
            img.color = new Color(color.r, color.g, color.b, color.a * (1f - t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (go != null) { _particles.Remove(go); Destroy(go); }
    }

    private void ClearParticles()
    {
        foreach (var p in _particles)
            if (p != null) Destroy(p);
        _particles.Clear();
    }
}