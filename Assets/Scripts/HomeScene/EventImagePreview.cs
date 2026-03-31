using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Linq;

public class EventImagePreview : MonoBehaviour
{
    public static EventImagePreview instance { get; private set; }

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
    }

    private Canvas _canvas;
    private GameObject _overlay;
    private Image _previewImg;
    private RectTransform _imgRT;
    private bool _active;

    // ピンチズーム
    private float _pinchStart, _scaleStart, _scale = 1f;
    private const float MIN = 0.5f, MAX = 5f;

    // ドラッグ移動
    private bool _dragging;
    private Vector2 _dragStartPos;
    private Vector2 _imgStartAnchor;

    private Canvas GetMainCanvas()
    {
        var go = GameObject.Find("Canvas");
        return go != null ? go.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
    }

    // 日本語対応フォントを取得（jp.asset）
    private TMP_FontAsset GetJapaneseFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp")
            ?? fonts.FirstOrDefault(f => f.name.ToLower().Contains("jp"))
            ?? fonts.FirstOrDefault();
    }

    private void Start()
    {
        _canvas = GetMainCanvas();
        Build();
    }

    private void Build()
    {
        if (_canvas == null) _canvas = GetMainCanvas();
        if (_canvas == null) return;

        // 暗幕オーバーレイ
        _overlay = new GameObject("__PreviewOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero;
        ort.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);
        bg.raycastTarget = false;

        // 画像（ドラッグ対応）
        var ig = new GameObject("__PreviewImg");
        ig.transform.SetParent(_overlay.transform, false);
        _imgRT = ig.AddComponent<RectTransform>();
        _imgRT.anchorMin = new Vector2(0.5f, 0.5f);
        _imgRT.anchorMax = new Vector2(0.5f, 0.5f);
        _imgRT.pivot = new Vector2(0.5f, 0.5f);
        _imgRT.anchoredPosition = Vector2.zero;
        _previewImg = ig.AddComponent<Image>();
        _previewImg.preserveAspect = true;
        _previewImg.raycastTarget = true;

        var et = ig.AddComponent<EventTrigger>();

        var eDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        eDown.callback.AddListener((data) => {
            var pd = (PointerEventData)data;
            _dragging = false;
            _dragStartPos = pd.position;
            _imgStartAnchor = _imgRT.anchoredPosition;
        });
        et.triggers.Add(eDown);

        var eDrag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        eDrag.callback.AddListener((data) => {
            var pd = (PointerEventData)data;
            Vector2 delta = pd.position - _dragStartPos;
            if (delta.magnitude > 5f) _dragging = true;
            if (_dragging) _imgRT.anchoredPosition = _imgStartAnchor + delta;
        });
        et.triggers.Add(eDrag);

        var eUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        eUp.callback.AddListener((_) => { _dragging = false; });
        et.triggers.Add(eUp);

        // 下部フッター閉じるバー
        var footer = new GameObject("__CloseFooter");
        footer.transform.SetParent(_overlay.transform, false);
        var frt = footer.AddComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(1f, 0f);
        frt.pivot = new Vector2(0.5f, 0f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(0f, 240f);
        var fbg = footer.AddComponent<Image>();
        fbg.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        fbg.raycastTarget = true;
        footer.AddComponent<Button>().onClick.AddListener(Hide);

        // フッターテキスト（日本語フォント適用）
        var ftxtGO = new GameObject("__CloseFooterTxt");
        ftxtGO.transform.SetParent(footer.transform, false);
        var ftrt = ftxtGO.AddComponent<RectTransform>();
        ftrt.anchorMin = Vector2.zero;
        ftrt.anchorMax = Vector2.one;
        ftrt.offsetMin = Vector2.zero;
        ftrt.offsetMax = Vector2.zero;
        var ftxt = ftxtGO.AddComponent<TextMeshProUGUI>();
        ftxt.text = "閉じる";
        ftxt.fontSize = 48;
        ftxt.alignment = TextAlignmentOptions.Center;
        ftxt.color = Color.white;
        ftxt.raycastTarget = false;
        // 日本語フォントを設定
        var jpFont = GetJapaneseFont();
        if (jpFont != null) ftxt.font = jpFont;

        _overlay.SetActive(false);
    }

    public void Show(Sprite sprite)
    {
        if (_canvas == null) _canvas = GetMainCanvas();
        if (_overlay == null) Build();
        if (_overlay == null) return;
        _previewImg.sprite = sprite;
        float sw = Screen.width * 0.9f, sh = Screen.height * 0.7f;
        float asp = (float)sprite.texture.width / sprite.texture.height;
        float w = sw, h = sw / asp;
        if (h > sh) { h = sh; w = sh * asp; }
        _scale = 1f;
        _imgRT.sizeDelta = new Vector2(w, h);
        _imgRT.localScale = Vector3.one;
        _imgRT.anchoredPosition = new Vector2(0f, 120f);
        _overlay.GetComponent<Image>().raycastTarget = true;
        _overlay.transform.SetAsLastSibling();
        _overlay.SetActive(true);
        _active = true;
    }

    public void Hide()
    {
        _active = false;
        _dragging = false;
        if (_overlay == null) return;
        _overlay.GetComponent<Image>().raycastTarget = false;
        _overlay.SetActive(false);
    }

    private void Update()
    {
        if (!_active) return;

        if (Input.touchCount == 2)
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);
            if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
            {
                _pinchStart = Vector2.Distance(t0.position, t1.position);
                _scaleStart = _scale;
            }
            else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
            {
                float d = Vector2.Distance(t0.position, t1.position);
                if (_pinchStart > 0)
                {
                    _scale = Mathf.Clamp(_scaleStart * d / _pinchStart, MIN, MAX);
                    _imgRT.localScale = Vector3.one * _scale;
                }
            }
        }

        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) > 0.01f)
        {
            _scale = Mathf.Clamp(_scale + wheel * 2f, MIN, MAX);
            _imgRT.localScale = Vector3.one * _scale;
        }
    }
}
