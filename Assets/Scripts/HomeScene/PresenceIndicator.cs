using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自分が現在チェックイン中（入店中）かどうかを画面左上に表示するバッジ。
/// 入店中は「● 滞在 ○時間○分」と現在の滞在時間を表示する。
/// 滞在時間は5分刻み（切り捨て）で、丸めた値が変わったときだけテキストを更新する
/// （30秒ごとのポーリングだが、TMPの書き換えは5分に1回しか起きない）。
///
/// 判定は端末ローカルの来店記録（LocalVisitLog.GetOpenCheckInTime）で行うため、
/// 「ログイン情報を公開する」設定がOFFでも自分にだけは表示される。
/// チェックイン/チェックアウトQR読み取り後に PresenceIndicator.Refresh() で更新する。
/// </summary>
public class PresenceIndicator : MonoBehaviour
{
    private static PresenceIndicator _instance;
    private GameObject _badge;
    private TextMeshProUGUI _label;
    private System.DateTime? _openCheckIn;
    private long _shownMinutes = -1; // 表示中の丸め済み分数（-1=未表示）

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
        StartCoroutine(PollLoop());
    }

    /// <summary>チェックイン/チェックアウト後に表示を更新する</summary>
    public static void Refresh()
    {
        if (_instance != null) _instance.UpdateBadge();
    }

    /// <summary>バックグラウンド復帰時に滞在時間を追いつかせる</summary>
    private void OnApplicationFocus(bool focus)
    {
        if (focus) UpdateBadge();
    }

    private void UpdateBadge()
    {
        if (_badge == null) return;
        _openCheckIn = LocalVisitLog.GetOpenCheckInTime();
        _badge.SetActive(_openCheckIn != null);
        _shownMinutes = -1; // 次のUpdateStayTextで必ず書き直す
        UpdateStayText();
    }

    /// <summary>
    /// 滞在時間の表示を更新する。経過分数を5分単位に切り捨て、
    /// 前回表示した値と変わったときだけテキストを書き換える。
    /// </summary>
    private void UpdateStayText()
    {
        if (_label == null || _openCheckIn == null) return;
        double elapsed = (System.DateTime.Now - _openCheckIn.Value).TotalMinutes;
        long floored = System.Math.Max(0L, (long)(elapsed / 5.0) * 5);
        if (floored == _shownMinutes) return;
        _shownMinutes = floored;
        long h = floored / 60, m = floored % 60;
        _label.text = h > 0 ? $"● 滞在 {h}時間{m}分" : $"● 滞在 {m}分";
    }

    /// <summary>30秒ごとに滞在時間表示を確認する（実時間基準。5分境界を跨いだときだけ表示が変わる）</summary>
    private System.Collections.IEnumerator PollLoop()
    {
        var wait = new WaitForSecondsRealtime(30f);
        while (true)
        {
            yield return wait;
            if (_badge != null && _badge.activeSelf) UpdateStayText();
        }
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
        rt.sizeDelta = new Vector2(310, 76);
        _badge.AddComponent<Image>().color = C_BORDER;

        var inner = new GameObject("__Inner");
        inner.transform.SetParent(_badge.transform, false);
        inner.AddComponent<RectTransform>().sizeDelta = new Vector2(302, 68);
        inner.AddComponent<Image>().color = C_GREEN;

        var label = new GameObject("__Label");
        label.transform.SetParent(inner.transform, false);
        label.AddComponent<RectTransform>().sizeDelta = new Vector2(302, 68);
        var tmp = label.AddComponent<TextMeshProUGUI>();
        if (jp != null) tmp.font = jp;
        tmp.text = "● ログイン中";
        tmp.fontSize = 34;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = 34;
        tmp.fontSizeMin = 22;
        _label = tmp;
    }
}
