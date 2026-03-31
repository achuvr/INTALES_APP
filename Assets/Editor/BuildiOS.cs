using UnityEditor;
using UnityEngine;

public static class BuildiOS
{
    private const string BUNDLE_IDENTIFIER = "com.intalescafe.intalescafeapp";
    private const string CAMERA_USAGE_DESCRIPTION = "このアプリではカメラを使用します";

    [MenuItem("Build/iOS Build")]
    public static void PerformBuild()
    {
        // Bundle Identifier を設定
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BUNDLE_IDENTIFIER);

        // Camera Usage Description を設定
        PlayerSettings.iOS.cameraUsageDescription = CAMERA_USAGE_DESCRIPTION;

        // Build Settings に登録されているシーンを取得
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                scenes.Add(scene.path);
            }
        }

        // 出力先
        string outputPath = "Builds/iOS";

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"iOS ビルド成功: {outputPath}");
        }
        else
        {
            Debug.LogError($"iOS ビルド失敗: {report.summary.result}");
        }
    }
}

#if false


{
    [MenuItem("Tools/Generate Event Image Scripts")]
    public static void Generate()
    {
        // EventImagePreview.cs
        string prev = @"using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EventImagePreview : MonoBehaviour
{
    public static EventImagePreview instance { get; private set; }

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(gameObject);
    }

    private Canvas _canvas;
    private GameObject _overlay;
    private GameObject _imgGO;
    private Image _previewImg;
    private RectTransform _imgRT;
    private bool _active;
    private float _pinchStart;
    private float _scaleStart;
    private float _scale = 1f;
    private const float MIN = 0.5f, MAX = 5f;

    private void Start()
    {
        _canvas = FindFirstObjectByType<Canvas>();
        BuildOverlay();
    }

    private void BuildOverlay()
    {
        // 暗幕（非表示時はraycastTarget=false）
        _overlay = new GameObject("__PreviewOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var rt = _overlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        bg.color = new Color(0,0,0,0.85f);
        bg.raycastTarget = false; // 普段は無効
        var btn = _overlay.AddComponent<Button>();
        btn.onClick.AddListener(Hide);
        var nav = new Navigation { mode = Navigation.Mode.None };
        btn.navigation = nav;

        // 画像
        _imgGO = new GameObject("__PreviewImg");
        _imgGO.transform.SetParent(_overlay.transform, false);
        _imgRT = _imgGO.AddComponent<RectTransform>();
        _imgRT.anchorMin = new Vector2(0.5f, 0.5f);
        _imgRT.anchorMax = new Vector2(0.5f, 0.5f);
        _imgRT.pivot    = new Vector2(0.5f, 0.5f);
        _imgRT.anchoredPosition = Vector2.zero;
        _previewImg = _imgGO.AddComponent<Image>();
        _previewImg.preserveAspect = true;
        _previewImg.raycastTarget = false; // 暗幕のクリックを通す

        _overlay.SetActive(false);
    }

    public void Show(Sprite sprite)
    {
        if (_overlay == null) BuildOverlay();
        _previewImg.sprite = sprite;
        // 画面の90%に収まるサイズ
        float sw = Screen.width * 0.9f, sh = Screen.height * 0.9f;
        float asp = (float)sprite.texture.width / sprite.texture.height;
        float w = sw, h = sw / asp;
        if (h > sh) { h = sh; w = sh * asp; }
        _scale = 1f;
        _imgRT.sizeDelta = new Vector2(w, h);
        _imgRT.anchoredPosition = Vector2.zero;
        var bg = _overlay.GetComponent<Image>();
        bg.raycastTarget = true; // 表示中だけ有効
        _overlay.transform.SetAsLastSibling();
        _overlay.SetActive(true);
        _active = true;
    }

    public void Hide()
    {
        _active = false;
        if (_overlay == null) return;
        _overlay.SetActive(false);
        var bg = _overlay.GetComponent<Image>();
        if (bg) bg.raycastTarget = false;
    }

    private void Update()
    {
        if (!_active) return;
        // ピンチズーム（スマホ2本指）
        if (Input.touchCount == 2)
        {
            var t0 = Input.GetTouch(0); var t1 = Input.GetTouch(1);
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
        // PC用マウスホイールズーム
        float wheel = Input.GetAxis(\"Mouse ScrollWheel\");
        if (Mathf.Abs(wheel) > 0.01f)
        {
            _scale = Mathf.Clamp(_scale + wheel * 2f, MIN, MAX);
            _imgRT.localScale = Vector3.one * _scale;
        }
    }
}";

        // EventImageTappable.cs
        string tapp = @"using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EventImageTappable : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler
{
    private Sprite _sprite;
    private Vector2 _downPos;
    private const float DRAG = 15f;

    public void Setup(Sprite sprite)
    {
        _sprite = sprite;
        var img = GetComponent<Image>();
        if (img) img.raycastTarget = true;
    }

    public void OnPointerDown(PointerEventData e) { _downPos = e.position; }

    public void OnPointerUp(PointerEventData e)
    {
        if (Vector2.Distance(_downPos, e.position) < DRAG
            && _sprite != null
            && EventImagePreview.instance != null)
            EventImagePreview.instance.Show(_sprite);
    }
}";

        System.IO.File.WriteAllText("Assets/Scripts/HomeScene/EventImagePreview.cs", prev);
        System.IO.File.WriteAllText("Assets/Scripts/HomeScene/EventImageTappable.cs", tapp);
        AssetDatabase.Refresh();
        Debug.Log("[EventImage] スクリプト生成完了！");
    }
}

    [MenuItem("Tools/Generate Event Image Scripts")]
    public static void Generate()
    {
        string previewPath = "Assets/Scripts/HomeScene/EventImagePreview.cs";
        string tappablePath = "Assets/Scripts/HomeScene/EventImageTappable.cs";

        string previewCode =
"using UnityEngine;\n" +
"using UnityEngine.UI;\n" +
"using UnityEngine.EventSystems;\n" +
"\n" +
"public class EventImagePreview : MonoBehaviour\n" +
"{\n" +
"    public static EventImagePreview instance { get; private set; }\n" +
"\n" +
"    private void Awake()\n" +
"    {\n" +
"        if (instance == null) instance = this;\n" +
"        else { Destroy(gameObject); return; }\n" +
"    }\n" +
"\n" +
"    private GameObject _overlay;\n" +
"    private Image _previewImage;\n" +
"    private RectTransform _imageRect;\n" +
"    private bool _isVisible;\n" +
"    private float _initialPinchDistance;\n" +
"    private Vector2 _initialSizeDelta;\n" +
"    private const float MIN_SCALE = 0.5f;\n" +
"    private const float MAX_SCALE = 5.0f;\n" +
"\n" +
"    private void Start() { BuildUI(); }\n" +
"\n" +
"    private void BuildUI()\n" +
"    {\n" +
"        var canvas = FindFirstObjectByType<Canvas>();\n" +
"        if (canvas == null) return;\n" +
"        _overlay = new GameObject(\"EventPreviewOverlay\");\n" +
"        _overlay.transform.SetParent(canvas.transform, false);\n" +
"        var or = _overlay.AddComponent<RectTransform>();\n" +
"        or.anchorMin = Vector2.zero; or.anchorMax = Vector2.one;\n" +
"        or.offsetMin = Vector2.zero; or.offsetMax = Vector2.zero;\n" +
"        var oi = _overlay.AddComponent<Image>();\n" +
"        oi.color = new Color(0f, 0f, 0f, 0.85f); oi.raycastTarget = true;\n" +
"        _overlay.AddComponent<Button>().onClick.AddListener(Hide);\n" +
"        var imgGO = new GameObject(\"PreviewImage\");\n" +
"        imgGO.transform.SetParent(_overlay.transform, false);\n" +
"        _imageRect = imgGO.AddComponent<RectTransform>();\n" +
"        _imageRect.anchorMin = new Vector2(0.5f,0.5f);\n" +
"        _imageRect.anchorMax = new Vector2(0.5f,0.5f);\n" +
"        _imageRect.pivot = new Vector2(0.5f,0.5f);\n" +
"        _imageRect.anchoredPosition = Vector2.zero;\n" +
"        _previewImage = imgGO.AddComponent<Image>();\n" +
"        _previewImage.preserveAspect = true; _previewImage.raycastTarget = true;\n" +
"        var trig = imgGO.AddComponent<EventTrigger>();\n" +
"        var ent = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };\n" +
"        ent.callback.AddListener((_) => { }); trig.triggers.Add(ent);\n" +
"        _overlay.SetActive(false);\n" +
"    }\n" +
"\n" +
"    public void Show(Sprite sprite)\n" +
"    {\n" +
"        if (_overlay == null) BuildUI();\n" +
"        _previewImage.sprite = sprite;\n" +
"        float sw = Screen.width * 0.9f, sh = Screen.height * 0.9f;\n" +
"        float asp = (float)sprite.texture.width / sprite.texture.height;\n" +
"        float w, h;\n" +
"        if (sw / asp <= sh) { w = sw; h = sw / asp; } else { h = sh; w = sh * asp; }\n" +
"        _imageRect.sizeDelta = new Vector2(w, h);\n" +
"        _imageRect.anchoredPosition = Vector2.zero;\n" +
"        _initialSizeDelta = _imageRect.sizeDelta;\n" +
"        _overlay.transform.SetAsLastSibling();\n" +
"        _overlay.SetActive(true); _isVisible = true;\n" +
"    }\n" +
"\n" +
"    public void Hide()\n" +
"    { if (_overlay != null) _overlay.SetActive(false); _isVisible = false; }\n" +
"\n" +
"    private void Update()\n" +
"    {\n" +
"        if (!_isVisible || Input.touchCount != 2) return;\n" +
"        var t0 = Input.GetTouch(0); var t1 = Input.GetTouch(1);\n" +
"        if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)\n" +
"        { _initialPinchDistance = Vector2.Distance(t0.position, t1.position); _initialSizeDelta = _imageRect.sizeDelta; }\n" +
"        else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)\n" +
"        {\n" +
"            float d = Vector2.Distance(t0.position, t1.position);\n" +
"            if (_initialPinchDistance == 0) return;\n" +
"            _imageRect.sizeDelta = _initialSizeDelta * Mathf.Clamp(d / _initialPinchDistance, MIN_SCALE, MAX_SCALE);\n" +
"        }\n" +
"    }\n" +
"}";

        string tappableCode =
"using UnityEngine;\n" +
"using UnityEngine.EventSystems;\n" +
"\n" +
"public class EventImageTappable : MonoBehaviour, IPointerClickHandler\n" +
"{\n" +
"    private Sprite _sprite;\n" +
"    public void Setup(Sprite sprite) { _sprite = sprite; }\n" +
"    public void OnPointerClick(PointerEventData eventData)\n" +
"    { if (_sprite != null) EventImagePreview.instance.Show(_sprite); }\n" +
"}";

        System.IO.File.WriteAllText(previewPath, previewCode);
        System.IO.File.WriteAllText(tappablePath, tappableCode);
        AssetDatabase.Refresh();
        Debug.Log("[EventImage] スクリプトを生成しました！");
    }
}
#endif
