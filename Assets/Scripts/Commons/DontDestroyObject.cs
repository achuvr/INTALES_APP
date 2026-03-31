using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;


public class DontDestroyObject : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(this.gameObject);
    }
}

#if false
/// <summary>
/// イベント画像プレビューUI（ピンチズーム対応）
/// HomeシーンのCanvasに自動生成されます
/// </summary>
public class _DISABLED_EventImagePreview : MonoBehaviour
{
    public static _DISABLED_EventImagePreview instance { get; private set; }

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
    }

    private GameObject _overlay;
    private Image _previewImage;
    private RectTransform _imageRect;
    private bool _isVisible;
    private float _initialPinchDistance;
    private Vector2 _initialSizeDelta;
    private const float MIN_SCALE = 0.5f;
    private const float MAX_SCALE = 5.0f;

    private void Start()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // 暗幕オーバーレイ
        _overlay = new GameObject("EventPreviewOverlay");
        _overlay.transform.SetParent(canvas.transform, false);
        var overlayRect = _overlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        var overlayImg = _overlay.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.85f);
        overlayImg.raycastTarget = true;
        var overlayBtn = _overlay.AddComponent<Button>();
        overlayBtn.onClick.AddListener(Hide);

        // プレビュー画像（中央）
        var imgGO = new GameObject("PreviewImage");
        imgGO.transform.SetParent(_overlay.transform, false);
        _imageRect = imgGO.AddComponent<RectTransform>();
        _imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        _imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        _imageRect.pivot = new Vector2(0.5f, 0.5f);
        _imageRect.anchoredPosition = Vector2.zero;
        _previewImage = imgGO.AddComponent<Image>();
        _previewImage.preserveAspect = true;
        _previewImage.raycastTarget = true;
        // 画像タップはオーバーレイに伝播させない
        var trigger = imgGO.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener((_) => { });
        trigger.triggers.Add(entry);

        _overlay.SetActive(false);
    }

    public void Show(Sprite sprite)
    {
        if (_overlay == null) BuildUI();
        _previewImage.sprite = sprite;

        float screenW = Screen.width * 0.9f;
        float screenH = Screen.height * 0.9f;
        float aspect = (float)sprite.texture.width / sprite.texture.height;
        float w, h;
        if (screenW / aspect <= screenH) { w = screenW; h = screenW / aspect; }
        else { h = screenH; w = screenH * aspect; }
        _imageRect.sizeDelta = new Vector2(w, h);
        _imageRect.anchoredPosition = Vector2.zero;
        _initialSizeDelta = _imageRect.sizeDelta;

        _overlay.transform.SetAsLastSibling();
        _overlay.SetActive(true);
        _isVisible = true;
    }

    public void Hide()
    {
        if (_overlay != null) _overlay.SetActive(false);
        _isVisible = false;
    }

    private void Update()
    {
        if (!_isVisible || Input.touchCount != 2) return;
        var t0 = Input.GetTouch(0);
        var t1 = Input.GetTouch(1);
        if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
        {
            _initialPinchDistance = Vector2.Distance(t0.position, t1.position);
            _initialSizeDelta = _imageRect.sizeDelta;
        }
        else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
        {
            float dist = Vector2.Distance(t0.position, t1.position);
            if (_initialPinchDistance == 0) return;
            float ratio = Mathf.Clamp(dist / _initialPinchDistance, MIN_SCALE, MAX_SCALE);
            _imageRect.sizeDelta = _initialSizeDelta * ratio;
        }
    }
}

/// <summary>
/// イベント画像の各アイテムにアタッチ。タップでプレビューを開く。
/// </summary>
public class _DISABLED_EventImageTappable : MonoBehaviour, IPointerClickHandler
{
    private Sprite _sprite;

    public void Setup(Sprite sprite)
    {
        _sprite = sprite;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_sprite != null) { }

    }
}
#endif
