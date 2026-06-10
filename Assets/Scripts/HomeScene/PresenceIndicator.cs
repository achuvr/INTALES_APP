using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自分が現在チェックイン中（入店中）かどうかを画面左上に表示するバッジ。
/// 判定は端末ローカルの来店記録（LocalVisitLog.HasOpenVisit）で行うため、
/// 「ログイン情報を公開する」設定がOFFでも自分にだけは表示される。
/// チェックイン/チェックアウトQR読み取り後に PresenceIndicator.Refresh() で更新する。
/// </summary>
public class PresenceIndicator : MonoBehaviour
{
    private static PresenceIndicator _instance;
    private GameObject _badge;

    private static readonly Color C_BORDER = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_GREEN  = new Color(0.13f, 0.52f, 0.21f, 0.96f);

    private void Awake()
    {
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        var canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>()
                                      : FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        BuildBadge(canvas);
        UpdateBadge();
    }

    /// <summary>チェックイン/チェックアウト後に表示を更新する</summary>
    public static void Refresh()
    {
        if (_instance != null) _instance.UpdateBadge();
    }

    private void UpdateBadge()
    {
        if (_badge != null) _badge.SetActive(LocalVisitLog.HasOpenVisit());
    }

    private void BuildBadge(Canvas canvas)
    {
        var jp = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name.ToLower() == "jp");

        // 画面左上に固定
        _badge = new GameObject("__PresenceBadge");
        _badge.transform.SetParent(canvas.transform, false);
        var rt = _badge.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(30, -100);
        rt.sizeDelta = new Vector2(250, 76);
        _badge.AddComponent<Image>().color = C_BORDER;

        var inner = new GameObject("__Inner");
        inner.transform.SetParent(_badge.transform, false);
        inner.AddComponent<RectTransform>().sizeDelta = new Vector2(242, 68);
        inner.AddComponent<Image>().color = C_GREEN;

        var label = new GameObject("__Label");
        label.transform.SetParent(inner.transform, false);
        label.AddComponent<RectTransform>().sizeDelta = new Vector2(242, 68);
        var tmp = label.AddComponent<TextMeshProUGUI>();
        if (jp != null) tmp.font = jp;
        tmp.text = "● ログイン中";
        tmp.fontSize = 34;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }
}
