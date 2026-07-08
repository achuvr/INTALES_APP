using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 店舗の食事・ドリンクメニュー画面（コード生成UI・古びた本のデザイン）。
///
/// お知らせページの「メニュー」ボタン（MenuButton）から
/// new GameObject("MenuViewerRoot").AddComponent&lt;MenuViewerController&gt;() で生成され、
/// Homeの Canvas 上に全画面のメニューを構築する。
///
/// 画像は master/config の menu_images（Firebase Storage の URL 一覧・表示順）から取得し、
/// イベント告知画像（FirebaseDataReceiver）と同じ方式で端末にキャッシュする
/// （URL一覧が前回と同じならローカルキャッシュを使い、変わったときだけ再ダウンロード）。
/// 画像をタップすると EventImagePreview の全画面プレビュー（ピンチズーム対応）で見られる。
/// 右上の×ボタンで閉じてHomeに戻る。
/// </summary>
public class MenuViewerController : MonoBehaviour
{
    private const string CACHE_FOLDER = "MenuImageCache";
    // 前回ダウンロードしたメニュー画像URLリスト（改行区切り）。これが変わったときだけ再取得する
    private const string CACHED_URLS_KEY = "CachedMenuImageUrls";

    // BoardGameListController と揃えた配色
    private static readonly Color C_TITLEBAR  = new Color(0.40f, 0.23f, 0.08f, 0.92f);
    private static readonly Color C_TITLETEXT = new Color(0.99f, 0.94f, 0.78f, 1.00f);
    private static readonly Color C_CLOSE     = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);

    private const float TITLE_H = 264f;
    private const float SIDE_PAD = 24f;

    private Canvas _canvas;
    private GameObject _overlay;
    private RectTransform _content;
    private TextMeshProUGUI _statusLabel;
    private TMP_FontAsset _jp;
    /// <summary>表示中のメニュー画像（閉じる時にテクスチャごと破棄する）。</summary>
    private readonly List<Sprite> _sprites = new List<Sprite>();

    private string CacheDir => Path.Combine(Application.persistentDataPath, CACHE_FOLDER);

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError("[Menu] Canvas が見つかりません");
            Destroy(gameObject);
            return;
        }
        _jp = GetJpFont();
        BuildUI();
        LoadImagesAsync().Forget();
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        // 全画面オーバーレイ（古地図テクスチャでHomeを覆う）
        _overlay = new GameObject("__MenuOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        OldMapBackground.Apply(bg);
        bg.raycastTarget = true; // タップがHome側へ抜けないように受ける

        // タイトル帯
        var titleBar = new GameObject("__TitleBar");
        titleBar.transform.SetParent(_overlay.transform, false);
        var trt = titleBar.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(0f, -TITLE_H); trt.offsetMax = new Vector2(0f, 0f);
        titleBar.AddComponent<Image>().color = C_TITLEBAR;

        var titleGO = new GameObject("__TitleLabel");
        titleGO.transform.SetParent(titleBar.transform, false);
        var tlrt = titleGO.AddComponent<RectTransform>();
        tlrt.anchorMin = Vector2.zero; tlrt.anchorMax = Vector2.one;
        tlrt.offsetMin = new Vector2(48f, 16f); tlrt.offsetMax = new Vector2(-150f, -16f);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) titleTmp.font = _jp;
        titleTmp.text = "メニュー";
        titleTmp.fontSize = 84;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.color = C_TITLETEXT;
        titleTmp.alignment = TextAlignmentOptions.MidlineLeft;
        titleTmp.raycastTarget = false;

        // ×ボタン（右上）
        var close = new GameObject("__Close");
        close.transform.SetParent(_overlay.transform, false);
        var crt = close.AddComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-28, -TITLE_H / 2f);
        crt.sizeDelta = new Vector2(120, 120);
        var cimg = close.AddComponent<Image>();
        RoundedRectSprite.Apply(cimg);
        cimg.color = C_CLOSE;
        var cbtn = close.AddComponent<Button>();
        cbtn.targetGraphic = cimg;
        cbtn.onClick.AddListener(Close);
        var clabel = new GameObject("__X");
        clabel.transform.SetParent(close.transform, false);
        var clrt = clabel.AddComponent<RectTransform>();
        clrt.anchorMin = Vector2.zero; clrt.anchorMax = Vector2.one;
        clrt.offsetMin = clrt.offsetMax = Vector2.zero;
        var ctmp = clabel.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ctmp.font = _jp;
        ctmp.text = "✕";
        ctmp.fontSize = 56;
        ctmp.fontStyle = FontStyles.Bold;
        ctmp.color = Color.white;
        ctmp.alignment = TextAlignmentOptions.Center;
        ctmp.raycastTarget = false;

        // 縦スクロール（タイトル帯の下いっぱい）
        var svGO = new GameObject("__Scroll");
        svGO.transform.SetParent(_overlay.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 1f);
        svrt.offsetMin = new Vector2(SIDE_PAD, SIDE_PAD);
        svrt.offsetMax = new Vector2(-SIDE_PAD, -(TITLE_H + 24f));
        var scroll = svGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var vpGO = new GameObject("__VP");
        vpGO.transform.SetParent(svGO.transform, false);
        var vprt = vpGO.AddComponent<RectTransform>();
        vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
        vprt.offsetMin = vprt.offsetMax = Vector2.zero;
        vpGO.AddComponent<RectMask2D>();
        scroll.viewport = vprt;

        var ctGO = new GameObject("__Content");
        ctGO.transform.SetParent(vpGO.transform, false);
        _content = ctGO.AddComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.offsetMin = _content.offsetMax = Vector2.zero;
        var vlg = ctGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 20f;
        vlg.padding = new RectOffset(0, 0, 8, 8);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = false; vlg.childControlHeight = false;
        var fit = ctGO.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _content;

        // 状態ラベル（読み込み中／準備中。画像が並んだら消す）
        var stGO = new GameObject("__Status");
        stGO.transform.SetParent(vpGO.transform, false);
        var strt = stGO.AddComponent<RectTransform>();
        strt.anchorMin = Vector2.zero; strt.anchorMax = Vector2.one;
        strt.offsetMin = strt.offsetMax = Vector2.zero;
        _statusLabel = stGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) _statusLabel.font = _jp;
        _statusLabel.text = "読み込み中…";
        _statusLabel.fontSize = 40; _statusLabel.color = C_MUTED;
        _statusLabel.alignment = TextAlignmentOptions.Center;
        _statusLabel.raycastTarget = false;
    }

    private void SetStatus(string text)
    {
        if (_statusLabel != null) _statusLabel.text = text ?? "";
    }

    // ================================================================
    // 画像の取得（キャッシュ優先。URL一覧が変わったときだけ再ダウンロード）
    // ================================================================
    private async UniTaskVoid LoadImagesAsync()
    {
        var config = await MasterData.GetConfigAsync();
        if (this == null || _overlay == null) return; // 待っている間に閉じられた

        var urls = config.MenuImages;
        if (urls == null || urls.Count == 0)
        {
            SetStatus("メニューは準備中です");
            return;
        }

        string signature = string.Join("\n", urls);
        if (PlayerPrefs.GetString(CACHED_URLS_KEY, "__none__") == signature && HasCache())
        {
            ShowFromCache();
            return;
        }

        ClearCache();
        // 旧署名もここで消す。部分DLのまま中断し、その後 menu_images が旧リストに
        // 戻されたとき、「旧署名＋部分キャッシュ」が完全なキャッシュ扱いされるのを防ぐ
        PlayerPrefs.DeleteKey(CACHED_URLS_KEY);
        PlayerPrefs.Save();
        int failed = 0;
        var storage = FirebaseStorage.DefaultInstance;
        for (int i = 0; i < urls.Count; i++)
        {
            try
            {
                if (string.IsNullOrEmpty(urls[i])) continue;

                // gs://（Storage参照）/ https / Storage内パス のいずれでも受ける（イベント画像と同様）
                string imageUrl;
                if (urls[i].StartsWith("gs://"))
                {
                    var gsUri = new System.Uri(urls[i]);
                    var reference = storage.RootReference.Child(gsUri.AbsolutePath.TrimStart('/'));
                    imageUrl = (await reference.GetDownloadUrlAsync().AsUniTask()).ToString();
                }
                else if (urls[i].StartsWith("http"))
                {
                    imageUrl = urls[i];
                }
                else
                {
                    imageUrl = (await storage.RootReference.Child(urls[i]).GetDownloadUrlAsync().AsUniTask()).ToString();
                }

                using (var www = UnityWebRequestTexture.GetTexture(imageUrl))
                {
                    await www.SendWebRequest().ToUniTask();
                    if (this == null || _overlay == null) return; // DL中に閉じられた

                    var tex = DownloadHandlerTexture.GetContent(www);
                    SaveToCache(i.ToString(), tex.EncodeToJPG());
                    AddImageCell(tex, i.ToString());
                }
            }
            catch (System.Exception e)
            {
                failed++;
                Debug.LogError($"[Menu] 画像の取得に失敗 (index={i}): {e.Message}");
                if (this == null || _overlay == null) return;
            }
        }

        if (failed == 0)
        {
            // 全画像のDLに成功したときだけURLリストを記録する
            // （失敗があれば記録せず、次回開いたときに再ダウンロードを試みる）
            PlayerPrefs.SetString(CACHED_URLS_KEY, signature);
            PlayerPrefs.Save();
        }

        SetStatus(_sprites.Count == 0 ? "メニューを読み込めませんでした" : "");
    }

    /// <summary>ローカルキャッシュの画像を番号順に表示する。</summary>
    private void ShowFromCache()
    {
        var files = Directory.GetFiles(CacheDir, "*.jpg")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => int.TryParse(n, out _))
            .OrderBy(int.Parse)
            .ToList();
        foreach (var name in files)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(CacheDir, name + ".jpg"));
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(data)) { Destroy(tex); continue; }
            AddImageCell(tex, name);
        }
        SetStatus(_sprites.Count == 0 ? "メニューは準備中です" : "");
    }

    /// <summary>画像1枚をコンテンツ末尾に追加する（幅いっぱい・タップで全画面プレビュー）。</summary>
    private void AddImageCell(Texture2D tex, string name)
    {
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
        sprite.name = name;
        _sprites.Add(sprite);

        float w = _content.rect.width;
        if (w <= 0f) w = 1080f - SIDE_PAD * 2f; // レイアウト未確定時のフォールバック

        var cell = new GameObject("__MenuImage_" + name);
        cell.transform.SetParent(_content, false);
        var rt = cell.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, w * tex.height / tex.width);
        var img = cell.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;

        // タップでピンチズーム対応の全画面プレビュー（イベント画像と同じビューア）
        var btn = cell.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() =>
        {
            if (EventImagePreview.instance == null)
                new GameObject("EventImagePreview").AddComponent<EventImagePreview>();
            EventImagePreview.instance.Show(sprite);
        });

        SetStatus("");
    }

    // ================================================================
    // キャッシュ
    // ================================================================
    private bool HasCache()
        => Directory.Exists(CacheDir) && Directory.GetFiles(CacheDir, "*.jpg").Length > 0;

    private void ClearCache()
    {
        if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true);
    }

    private void SaveToCache(string name, byte[] data)
    {
        if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
        File.WriteAllBytes(Path.Combine(CacheDir, name + ".jpg"), data);
    }

    // ================================================================
    // 閉じる
    // ================================================================
    private void Close()
    {
        if (_overlay != null) Destroy(_overlay);
        Destroy(gameObject); // MenuViewerRoot ごと破棄
    }

    private void OnDestroy()
    {
        foreach (var s in _sprites)
        {
            if (s == null) continue;
            Destroy(s.texture);
            Destroy(s);
        }
        _sprites.Clear();
    }

    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }
}
