#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;

/// <summary>
/// 4つのキャラクターボタンをテーマカラー+アイコン漢字+ラベルの2段構成でスタイリング
/// バッグ=茶色/袋, 情報=青/書, 戦闘=赤/剣, 装備=鋼色/盾
/// </summary>
public static class StyleCharacterButtons
{
    // ボタン定義: (GameObjectName, アイコン漢字, ラベル, 背景色R,G,B, 境界線色R,G,B)
    static readonly (string go, string icon, string label, float r, float g, float b, float br, float bg, float bb)[] Defs =
    {
        ("Button_OpenBag",     "袋", "バッグ", 0.65f, 0.38f, 0.08f,  0.85f, 0.55f, 0.20f),
        ("Button_OpenProfile", "情", "情報",   0.08f, 0.28f, 0.68f,  0.30f, 0.55f, 0.90f),
        ("Button_Battle",      "剣", "戦闘",   0.68f, 0.08f, 0.08f,  0.90f, 0.30f, 0.30f),
        ("Button_Equipment",   "盾", "装備",   0.25f, 0.32f, 0.38f,  0.50f, 0.62f, 0.72f),
    };

    [MenuItem("Tools/Style Character Buttons")]
    public static void Execute()
    {
        var allGOs = Resources.FindObjectsOfTypeAll<GameObject>();

        // jp フォント取得
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        var jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();

        foreach (var def in Defs)
        {
            var go = allGOs.FirstOrDefault(g => g.name == def.go && g.scene.IsValid());
            if (go == null) { Debug.LogError($"[Style] Not found: {def.go}"); continue; }

            // ---- 1. ボタンを高く・丸みある形に ----
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(84.49f, 48f); // 高さを48に拡大（元30）

            // ---- 2. 背景色 ----
            var bg = go.GetComponent<Image>();
            bg.color = new Color(def.r, def.g, def.b, 1f);

            // ---- 3. 境界線（枠）を子オブジェクトで追加 ----
            // 既にあれば更新、なければ追加
            var border = go.transform.Find("__Border");
            if (border == null)
            {
                var bgo = new GameObject("__Border");
                bgo.transform.SetParent(go.transform, false);
                border = bgo.transform;
            }
            var brt = border.gameObject.GetComponent<RectTransform>()
                   ?? border.gameObject.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(-1.5f, -1.5f);
            brt.offsetMax = new Vector2( 1.5f,  1.5f);
            var bImg = border.gameObject.GetComponent<Image>()
                    ?? border.gameObject.AddComponent<Image>();
            bImg.sprite = bg.sprite;
            bImg.type = Image.Type.Sliced;
            bImg.color = new Color(def.br, def.bg, def.bb, 0.6f);
            bImg.raycastTarget = false;
            border.SetAsFirstSibling(); // 背景の後ろに

            // ---- 4. 既存テキスト子を2段構成に ----
            var txt = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                // アイコン（大）+ 改行 + ラベル（小）
                txt.text = $"<size=18>{def.icon}</size>\n<size=8>{def.label}</size>";
                txt.color = Color.white;
                txt.fontStyle = FontStyles.Bold;
                txt.alignment = TextAlignmentOptions.Center;
                txt.lineSpacing = -10f;
                txt.enableAutoSizing = false;
                txt.fontSize = 18f;
                if (jp != null) txt.font = jp;

                // テキストをフル充填
                var trt = txt.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(1f, 2f);
                trt.offsetMax = new Vector2(-1f, -2f);
                EditorUtility.SetDirty(txt.gameObject);
            }

            // ---- 5. 影エフェクト（Outline）を追加して読みやすく ----
            var shadow = go.GetComponent<Shadow>() ?? go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(2f, -2f);

            go.SetActive(true);
            EditorUtility.SetDirty(go);
            Debug.Log($"[Style] Styled: {def.go} -> {def.icon}/{def.label}");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Style] All buttons styled!");
    }
}
#endif