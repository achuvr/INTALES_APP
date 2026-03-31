#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;

/// <summary>
/// 各ボタンのテキスト左側にカラーアイコンバッジを追加する
/// ボタン背景は白のまま、左端に小さい色付き四角＋漢字1文字のアイコンを配置
/// </summary>
public static class AddButtonIcons
{
    // (GameObjectName, アイコン文字, アイコン背景色R,G,B, テキストラベル)
    static readonly (string go, string icon, float r, float g, float b, string label)[] Defs =
    {
        ("Button_OpenBag",     "袋", 0.65f, 0.38f, 0.08f, "バッグ"),
        ("Button_OpenProfile", "情", 0.10f, 0.30f, 0.72f, "情報"),
        ("Button_Battle",      "剣", 0.72f, 0.08f, 0.08f, "戦闘"),
        ("Button_Equipment",   "盾", 0.28f, 0.35f, 0.42f, "装備"),
    };

    [MenuItem("Tools/Add Button Icons")]
    public static void Execute()
    {
        var allGOs = Resources.FindObjectsOfTypeAll<GameObject>();

        // jp フォント取得
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        var jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();

        foreach (var def in Defs)
        {
            var go = allGOs.FirstOrDefault(g => g.name == def.go && g.scene.IsValid());
            if (go == null) { Debug.LogError($"[AddIcons] Not found: {def.go}"); continue; }

            // 既存アイコンがあれば削除（べき等性のため）
            var old = go.transform.Find("__Icon");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            // ---- テキストを右寄せに変更（左30%をアイコン用に空ける）----
            var txt = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.text = def.label;
                txt.color = new Color(0.196f, 0.196f, 0.196f, 1f);
                txt.fontSize = 17.9f;
                txt.fontStyle = FontStyles.Bold;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                if (jp != null) txt.font = jp;
                // テキストを右側（30%〜100%）に配置
                var trt = txt.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0.30f, 0f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.offsetMin = new Vector2(2f, 0f);
                trt.offsetMax = new Vector2(-3f, 0f);
                EditorUtility.SetDirty(txt.gameObject);
            }

            // ---- アイコンバッジ（左0%〜28%）----
            var iconGO = new GameObject("__Icon");
            iconGO.transform.SetParent(go.transform, false);
            iconGO.transform.SetAsFirstSibling();

            var irt = iconGO.AddComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0.08f);
            irt.anchorMax = new Vector2(0.28f, 0.92f);
            irt.offsetMin = new Vector2(3f, 0f);
            irt.offsetMax = new Vector2(-1f, 0f);

            var iImg = iconGO.AddComponent<Image>();
            iImg.sprite = go.GetComponent<Image>().sprite; // 同じ丸角スプライトを使用
            iImg.type = Image.Type.Sliced;
            iImg.color = new Color(def.r, def.g, def.b, 1f);
            iImg.raycastTarget = false;

            // アイコン内テキスト（漢字1文字）
            var iTxtGO = new GameObject("__IconTxt");
            iTxtGO.transform.SetParent(iconGO.transform, false);
            var itrt = iTxtGO.AddComponent<RectTransform>();
            itrt.anchorMin = Vector2.zero;
            itrt.anchorMax = Vector2.one;
            itrt.offsetMin = Vector2.zero;
            itrt.offsetMax = Vector2.zero;
            var itxt = iTxtGO.AddComponent<TextMeshProUGUI>();
            itxt.text = def.icon;
            itxt.color = Color.white;
            itxt.fontSize = 13f;
            itxt.fontStyle = FontStyles.Bold;
            itxt.alignment = TextAlignmentOptions.Center;
            itxt.raycastTarget = false;
            if (jp != null) itxt.font = jp;

            go.SetActive(true);
            EditorUtility.SetDirty(go);
            Debug.Log($"[AddIcons] {def.go}: icon={def.icon}, label={def.label}");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[AddIcons] Done!");
    }
}
#endif