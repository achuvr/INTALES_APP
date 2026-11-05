using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 退会（アカウント削除）機能の旧ホーム用UI。Google Playの「アカウント作成があるアプリは
/// アプリ内に削除手段を提供すること」という要件に対応する。
///
/// 入口: アカウントモーダル（AccountButton）内の「アカウント削除」リンク
/// 流れ: 警告モーダル → 最終確認モーダル → 削除実行
/// 削除処理の本体（削除する内容の一覧も）は AccountSession.DeleteAccountAsync（新ホームと共通）。
/// </summary>
public class AccountDeletionController : MonoBehaviour
{
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DANGER    = new Color(0.78f, 0.16f, 0.16f, 1.00f);
    private static readonly Color C_CANCEL    = new Color(0.48f, 0.26f, 0.06f, 1.00f);

    private Canvas _canvas;
    private GameObject _modal;
    private bool _deleting;

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        // 入口ボタンは置かない（アカウントモーダル内の「アカウント削除」リンクから開く）
    }

    /// <summary>アカウント削除フローを開く（アカウントモーダルの「アカウント削除」リンクから呼ばれる）</summary>
    public void OpenDeletionFlow()
    {
        if (_canvas == null)
        {
            var canvasGO = GameObject.Find("Canvas");
            _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
            if (_canvas == null) return;
        }
        ShowWarningModal();
    }

    // ================================================================
    // 確認モーダル（2段階）
    // ================================================================
    private void ShowWarningModal()
    {
        CloseModal();
        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 800, 640);

        MakeLabel(panel.transform, "アカウント削除", jp, 46, FontStyles.Bold, C_DANGER,
            700, 80, new Vector2(0, 240));
        MakeLabel(panel.transform,
            "アカウントを削除すると、\nキャラクター・レベル・クーポン・フレンドなど\n全てのデータが完全に削除されます。\n\nこの操作は元に戻せません。",
            jp, 34, FontStyles.Normal, C_TITLE, 720, 280, new Vector2(0, 30));

        MakeButton(panel.transform, C_DANGER, "削除をすすめる", jp, 36, Color.white,
            340, 100, new Vector2(-185, -230), ShowFinalConfirmModal);
        MakeButton(panel.transform, C_CANCEL, "キャンセル", jp, 36, Color.white,
            340, 100, new Vector2(185, -230), CloseModal);
    }

    private void ShowFinalConfirmModal()
    {
        CloseModal();
        var jp = GetJpFont();
        _modal = BuildModalBase(out var panel, 800, 480);

        MakeLabel(panel.transform, "最終確認", jp, 46, FontStyles.Bold, C_DANGER,
            700, 80, new Vector2(0, 160));
        MakeLabel(panel.transform, "本当にアカウントを削除しますか？",
            jp, 38, FontStyles.Bold, C_TITLE, 720, 100, new Vector2(0, 40));

        MakeButton(panel.transform, C_DANGER, "完全に削除する", jp, 36, Color.white,
            340, 100, new Vector2(-185, -140), () => DeleteAccountAsync().Forget());
        MakeButton(panel.transform, C_CANCEL, "キャンセル", jp, 36, Color.white,
            340, 100, new Vector2(185, -140), CloseModal);
    }

    private void CloseModal()
    {
        if (_modal != null) Destroy(_modal);
        _modal = null;
    }

    // ================================================================
    // 削除実行
    // ================================================================
    private async UniTask DeleteAccountAsync()
    {
        if (_deleting) return;
        _deleting = true;
        CloseModal();
        AssetsDatabase.instance?.LoadingPanel?.SetActive(true);

        try
        {
            // 削除処理の本体は新ホームと共通（AccountSession）
            await AccountSession.DeleteAccountAsync();
            AccountSession.ReturnToStart();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AccountDeletion] 削除エラー: {ex.Message}");
            AssetsDatabase.instance?.LoadingPanel?.SetActive(false);
            FriendMenuController.ShowToast("削除に失敗しました。通信環境を確認して再度お試しください");
            _deleting = false;
        }
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private GameObject BuildModalBase(out GameObject panel, float w, float h)
    {
        var dim = new GameObject("__DeleteModal");
        dim.transform.SetParent(_canvas.transform, false);
        var rt = dim.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = UITheme.DIM;
        var dimBtn = dim.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseModal);

        var border = MakeRect("__ModalBorder", dim.transform, C_BORDER, w + 16, h + 16);
        UITheme.ElevateCard(border, 18f, 10f, 0.35f);
        panel = MakeRect("__ModalPanel", border.transform, C_PARCHMENT, w, h);
        panel.AddComponent<Button>().transition = Selectable.Transition.None;
        return dim;
    }

    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return go;
    }

    private void MakeButton(Transform parent, Color bg, string text, TMP_FontAsset font,
        float fontSize, Color textColor, float w, float h, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect("__Btn", parent, bg, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        // デザイン基盤: 背景の明るさで白ボタン／濃色ボタンの磨き分け（透明ヒットエリアは対象外）
        if (bg.a >= 0.5f)
        {
            var img = go.GetComponent<Image>();
            if (bg.r + bg.g + bg.b >= 2.4f) UITheme.PolishButton(img);
            else UITheme.PolishDarkButton(img);
        }
    }
}
