#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;

public static class RevertCharacterButtons
{
    [MenuItem("Tools/Revert Character Buttons")]
    public static void Execute()
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();

        // ===== 1. Button_OpenBag を元に戻す =====
        var bag = all.FirstOrDefault(g => g.name == "Button_OpenBag" && g.scene.IsValid());
        if (bag != null)
        {
            // 位置・サイズをリセット
            var rt = bag.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(-221.23f, -754f);
            rt.sizeDelta = new Vector2(84.49f, 30f);
            // 背景色を白に戻す
            bag.GetComponent<Image>().color = Color.white;
            // Shadowを削除
            var s = bag.GetComponent<Shadow>(); if (s) Object.DestroyImmediate(s);
            // __Borderを削除
            var b = bag.transform.Find("__Border"); if (b) Object.DestroyImmediate(b.gameObject);
            // テキストを元に戻す
            var txt = bag.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.text = "バッグ";
                txt.color = new Color(0.196f, 0.196f, 0.196f, 1f);
                txt.fontSize = 17.9f;
                txt.lineSpacing = 0f;
            }
            EditorUtility.SetDirty(bag);
            Debug.Log("[Revert] Button_OpenBag reverted");
        }

        // ===== 2. Button_OpenProfile を元に戻す =====
        var profile = all.FirstOrDefault(g => g.name == "Button_OpenProfile" && g.scene.IsValid());
        if (profile != null)
        {
            var rt = profile.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(221.23f, -754f);
            rt.sizeDelta = new Vector2(84.49f, 30f);
            profile.GetComponent<Image>().color = Color.white;
            var s = profile.GetComponent<Shadow>(); if (s) Object.DestroyImmediate(s);
            var b = profile.transform.Find("__Border"); if (b) Object.DestroyImmediate(b.gameObject);
            var txt = profile.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
            {
                txt.text = "プロフィール";
                txt.color = new Color(0.196f, 0.196f, 0.196f, 1f);
                txt.fontSize = 17.9f;
                txt.lineSpacing = 0f;
            }
            EditorUtility.SetDirty(profile);
            Debug.Log("[Revert] Button_OpenProfile reverted");
        }

        // ===== 3. Button_Battle を削除 =====
        var battle = all.FirstOrDefault(g => g.name == "Button_Battle" && g.scene.IsValid());
        if (battle != null) { Object.DestroyImmediate(battle); Debug.Log("[Revert] Button_Battle deleted"); }

        // ===== 4. Button_Equipment を削除 =====
        var equip = all.FirstOrDefault(g => g.name == "Button_Equipment" && g.scene.IsValid());
        if (equip != null) { Object.DestroyImmediate(equip); Debug.Log("[Revert] Button_Equipment deleted"); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Revert] Done! All button changes reverted.");
    }
}
#endif