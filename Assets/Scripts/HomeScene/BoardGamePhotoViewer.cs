using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「遊んだ記録」写真の全画面ビューア。
///
/// ピンチで拡大縮小（1〜6倍）、拡大中はドラッグで移動できる
/// （エディタではマウスホイール＋左ドラッグ）。
/// 下部のボタンで「写真に保存」（NativeGalleryで端末のギャラリーへコピー）と
/// 「削除」（誤タップ防止のため2度押しで確定）ができる。
/// </summary>
public class BoardGamePhotoViewer : MonoBehaviour
{
    // BoardGameListController と揃えたパレット
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 1.00f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_DELETE    = new Color(0.90f, 0.32f, 0.32f, 1.00f);

    private const float BAR_H = 300f;   // 下部帯（ボタン＋ステータス＋撮影日時）の高さ
    private const float MAX_SCALE = 6f;

    private string _path;
    private TMP_FontAsset _jp;
    private Action _onDeleted;

    private Canvas _canvas;
    private Texture2D _tex;
    private RectTransform _zoomArea;
    private RectTransform _photoRt;
    private TextMeshProUGUI _status;
    private TextMeshProUGUI _deleteLabel;
    private Image _deleteBg;

    private Vector2 _fitSize;
    private float _scale = 1f;
    private Vector2 _pos = Vector2.zero;
    private bool _sized;
    private bool _pinching;
    private bool _dragging;
    private Vector2 _lastMouse;
    private bool _deleteArmed;

    /// <summary>ビューアを開く。onDeleted は「削除」確定時に呼ばれる（呼び出し側で一覧を更新する）。</summary>
    public static void Show(Transform parent, string photoPath, TMP_FontAsset font, Action onDeleted)
    {
        var go = new GameObject("__PhotoViewer");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var viewer = go.AddComponent<BoardGamePhotoViewer>();
        viewer._path = photoPath;
        viewer._jp = font;
        viewer._onDeleted = onDeleted;
        viewer.Build();
    }

    private void Build()
    {
        _canvas = GetComponentInParent<Canvas>();

        // 背景（ほぼ黒。下の詳細ウィンドウへのタップも遮る）
        var dim = gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.94f);

        _tex = BoardGamePhotoStore.LoadTexture(_path);

        // 写真の表示領域（下部のボタン帯を除く範囲。拡大時のはみ出しはマスクで切る）
        var areaGO = new GameObject("__ZoomArea");
        areaGO.transform.SetParent(transform, false);
        _zoomArea = areaGO.AddComponent<RectTransform>();
        _zoomArea.anchorMin = Vector2.zero; _zoomArea.anchorMax = Vector2.one;
        _zoomArea.offsetMin = new Vector2(0f, BAR_H); _zoomArea.offsetMax = new Vector2(0f, -104f);
        areaGO.AddComponent<RectMask2D>();

        // 撮影日時（下部・写真の直下。ステータスとボタンの上に重ならない位置）
        var taken = BoardGamePhotoStore.TakenAtOf(_path);
        if (taken != DateTime.MinValue)
        {
            var dtGO = new GameObject("__TakenAt");
            dtGO.transform.SetParent(transform, false);
            var dtrt = dtGO.AddComponent<RectTransform>();
            dtrt.anchorMin = new Vector2(0f, 0f); dtrt.anchorMax = new Vector2(1f, 0f);
            dtrt.pivot = new Vector2(0.5f, 0f);
            dtrt.offsetMin = new Vector2(24f, BAR_H - 68f); dtrt.offsetMax = new Vector2(-24f, BAR_H - 8f);
            var dtmp = dtGO.AddComponent<TextMeshProUGUI>();
            if (_jp != null) dtmp.font = _jp;
            dtmp.text = $"撮影日時　{taken:yyyy年M月d日 HH:mm}";
            dtmp.fontSize = 34; dtmp.fontStyle = FontStyles.Bold;
            dtmp.color = C_PARCHMENT;
            dtmp.alignment = TextAlignmentOptions.Center;
            dtmp.raycastTarget = false;
        }

        if (_tex != null)
        {
            var photoGO = new GameObject("__Photo");
            photoGO.transform.SetParent(areaGO.transform, false);
            _photoRt = photoGO.AddComponent<RectTransform>();
            _photoRt.anchorMin = _photoRt.anchorMax = new Vector2(0.5f, 0.5f);
            var raw = photoGO.AddComponent<RawImage>();
            raw.texture = _tex;
            raw.raycastTarget = false;
        }

        // ステータス（保存結果の表示用。ボタンと撮影日時の間の段）
        var stGO = new GameObject("__Status");
        stGO.transform.SetParent(transform, false);
        var strt = stGO.AddComponent<RectTransform>();
        strt.anchorMin = new Vector2(0f, 0f); strt.anchorMax = new Vector2(1f, 0f);
        strt.pivot = new Vector2(0.5f, 0f);
        strt.offsetMin = new Vector2(24f, 144f); strt.offsetMax = new Vector2(-24f, 200f);
        _status = stGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) _status.font = _jp;
        _status.fontSize = 30; _status.fontStyle = FontStyles.Bold;
        _status.color = C_PARCHMENT;
        _status.alignment = TextAlignmentOptions.Center;
        _status.raycastTarget = false;
        _status.text = _tex == null ? "写真を読み込めませんでした" : "";

        // ボタン帯（写真に保存／削除／とじる）
        (Image bg, TextMeshProUGUI label) MakeButton(string name, string text, Color bgColor, Color textColor,
            float aMin, float aMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(aMin, 0f); rt.anchorMax = new Vector2(aMax, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(8f, 28f); rt.offsetMax = new Vector2(-8f, 128f);
            var img = go.AddComponent<Image>();
            RoundedRectSprite.Apply(img);
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var lgo = new GameObject("__Label");
            lgo.transform.SetParent(go.transform, false);
            var lrt = lgo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var tmp = lgo.AddComponent<TextMeshProUGUI>();
            if (_jp != null) tmp.font = _jp;
            tmp.text = text;
            tmp.fontSize = 32; tmp.fontStyle = FontStyles.Bold;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true; tmp.fontSizeMax = 32; tmp.fontSizeMin = 20;
            tmp.raycastTarget = false;
            return (img, tmp);
        }

        if (_tex != null)
        {
            MakeButton("__Save", "写真に保存", C_BORDER, C_TITLE, 0.02f, 0.34f, OnSave);
            (_deleteBg, _deleteLabel) = MakeButton("__Delete", "削除", C_PARCHMENT, C_DELETE, 0.34f, 0.66f, OnDelete);
            MakeButton("__Close", "とじる", C_PARCHMENT, C_TITLE, 0.66f, 0.98f, Close);
        }
        else
        {
            MakeButton("__Close", "とじる", C_PARCHMENT, C_TITLE, 0.30f, 0.70f, Close);
        }
    }

    private void Update()
    {
        if (_photoRt == null) return;
        EnsureSized();
        if (!_sized) return;

#if UNITY_EDITOR || UNITY_STANDALONE
        // エディタ確認用: ホイールで拡大縮小、左ドラッグで移動
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f) SetScale(_scale * (1f + wheel * 0.1f));
        if (Input.GetMouseButtonDown(0)) { _dragging = true; _lastMouse = Input.mousePosition; }
        if (!Input.GetMouseButton(0)) _dragging = false;
        else if (_dragging)
        {
            var d = (Vector2)Input.mousePosition - _lastMouse;
            _lastMouse = Input.mousePosition;
            Pan(d);
        }
#endif

        if (Input.touchCount == 2)
        {
            // ピンチ: 2本指の距離の変化率でスケール
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);
            float dist = Vector2.Distance(t0.position, t1.position);
            float prevDist = Vector2.Distance(t0.position - t0.deltaPosition, t1.position - t1.deltaPosition);
            if (prevDist > 10f) SetScale(_scale * (dist / prevDist));
            _pinching = true;
        }
        else if (Input.touchCount == 1 && !_pinching)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved) Pan(t.deltaPosition);
        }
        else if (Input.touchCount == 0)
        {
            _pinching = false; // 指が全部離れてからでないと1本指ドラッグに移らない（ピンチ解除時の飛び防止）
        }
    }

    /// <summary>レイアウト確定後に、表示領域へちょうど収まるサイズを計算する。</summary>
    private void EnsureSized()
    {
        if (_sized || _tex == null) return;
        var area = _zoomArea.rect;
        if (area.width < 1f || area.height < 1f) return;

        float k = Mathf.Min(area.width / _tex.width, area.height / _tex.height);
        _fitSize = new Vector2(_tex.width * k, _tex.height * k);
        _photoRt.sizeDelta = _fitSize;
        _sized = true;
        Apply();
    }

    private void SetScale(float s)
    {
        _scale = Mathf.Clamp(s, 1f, MAX_SCALE);
        ClampPos();
        Apply();
    }

    private void Pan(Vector2 screenDelta)
    {
        if (_scale <= 1.01f) return;
        float sf = _canvas != null ? _canvas.scaleFactor : 1f;
        _pos += screenDelta / sf;
        ClampPos();
        Apply();
    }

    /// <summary>拡大画像の端が表示領域の内側へ入らない範囲に移動を制限する。</summary>
    private void ClampPos()
    {
        Vector2 half = (_fitSize * _scale - _zoomArea.rect.size) * 0.5f;
        half = Vector2.Max(half, Vector2.zero);
        _pos.x = Mathf.Clamp(_pos.x, -half.x, half.x);
        _pos.y = Mathf.Clamp(_pos.y, -half.y, half.y);
    }

    private void Apply()
    {
        _photoRt.localScale = Vector3.one * _scale;
        _photoRt.anchoredPosition = _pos;
    }

    /// <summary>端末のギャラリー（写真アプリ）へコピーを保存する。元のローカル記録はそのまま。</summary>
    private void OnSave()
    {
        DisarmDelete();
        _status.text = "保存中…";
        NativeGallery.SaveImageToGallery(_path, "INTALES", Path.GetFileName(_path),
            (success, galleryPath) =>
            {
                if (_status != null)
                    _status.text = success ? "端末の写真に保存しました" : "保存に失敗しました（権限を確認してください）";
            });
    }

    /// <summary>削除（誤タップ防止のため1度目は確認表示、2度目で確定）。</summary>
    private void OnDelete()
    {
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            _deleteLabel.text = "本当に削除？";
            _deleteBg.color = C_DELETE;
            _deleteLabel.color = Color.white;
            _status.text = "もう一度タップすると削除します";
            return;
        }
        BoardGamePhotoStore.Delete(_path);
        _onDeleted?.Invoke();
        Close();
    }

    private void DisarmDelete()
    {
        if (!_deleteArmed) return;
        _deleteArmed = false;
        _deleteLabel.text = "削除";
        _deleteBg.color = C_PARCHMENT;
        _deleteLabel.color = C_DELETE;
    }

    private void Close() => Destroy(gameObject);

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }
}
