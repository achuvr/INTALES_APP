using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using Firebase.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ゲーム画像の管理シーン（店舗の管理者専用。プログラマでなくても使える簡単UI）。
///
/// 募集ボード（RecruitBoardController）のカードに表示するゲーム画像を、
/// 端末のギャラリーから選んでアップロード・削除する。1ゲームにつき最大3枚。
///  ①ゲームをえらぶ（図鑑カタログから検索。持ち込みゲーム用に自由入力のタイトルも登録可）
///  ②しゃしんを追加（ギャラリーから最大3枚 → プレビュー表示。プレビューをタップで外す）
///  ③とうろくする（各画像を長辺512pxへ縮小 → Storage boardgame_images/ へアップロード →
///                  master/boardgame_images の images マップに {items:[{url,path}...]} を保存。
///                  再登録は差し替えで、古いStorageファイルは自動削除）
///
/// セキュリティ:
///  ・入口ボタン（AccountButton内）は admin クレームを持つアカウントにだけ表示されるが、
///    実際の保護は Firestore ルール（master/* は isAdmin のみ書き込み可）と
///    Storage ルール（書き込みは admin クレームのみ）で行う。
///  ・一般ユーザーのアプリには画像をアップロードする経路が存在しない
///    （卑猥画像などの持ち込み防止。募集ボードの画像は店側だけが登録できる）。
///
/// シーン構成は Zukan と同じ流儀: GameImageAdmin シーンにはこのコンポーネントを
/// 持つルート1個だけを置き、SceneLoader.MergeScene("GameImageAdmin") で Home に
/// 加算マージ → Home の Canvas 上に全画面UIをコード生成する。閉じると自分ごと破棄。
/// </summary>
public class GameImageAdminController : MonoBehaviour
{
    private const string STORAGE_BASE = "gs://intales-a0459.firebasestorage.app";
    private const string STORAGE_FOLDER = "boardgame_images";
    private const int UPLOAD_MAX_SIZE = 1024;  // アップロード画像の長辺（全画面ビューアでほぼ等倍になる大きさ）
    private const int CUSTOM_TITLE_MAX = 30;   // 持ち込みタイトルの最大文字数
    private const int MAX_IMAGES = 3;          // 1ゲームに登録できる画像の枚数

    // 古い本パレット（RecruitBoardController と同じ）
    private static readonly Color C_PARCHMENT = new Color(0.99f, 0.95f, 0.84f, 1.00f);
    private static readonly Color C_CARD      = new Color(0.97f, 0.92f, 0.78f, 1.00f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLEBAR  = new Color(0.40f, 0.23f, 0.08f, 0.92f);
    private static readonly Color C_TITLETEXT = new Color(0.99f, 0.94f, 0.78f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.38f, 0.16f, 0.04f, 1.00f);
    private static readonly Color C_INK       = new Color(0.30f, 0.18f, 0.06f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.52f, 0.38f, 0.22f, 1.00f);
    private static readonly Color C_CLOSE     = new Color(0.90f, 0.32f, 0.32f, 1.00f);
    private static readonly Color C_GOLD      = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_WHITE     = new Color(1.00f, 0.99f, 0.93f, 1.00f);
    private static readonly Color C_DANGER    = new Color(0.78f, 0.16f, 0.16f, 1.00f);
    private static readonly Color C_DANGER_ARMED = new Color(0.55f, 0.08f, 0.08f, 1.00f);

    private const float TITLE_H = 264f;

    private Canvas _canvas;
    private TMP_FontAsset _jp;
    private GameObject _overlay;
    private GameObject _popup;

    // 選択状態（画像は最大 MAX_IMAGES 枚）
    private string _selectedTitle = "";
    private readonly List<Texture2D> _selectedTexs = new List<Texture2D>();
    private readonly List<Sprite> _previewSprites = new List<Sprite>();
    private readonly List<Image> _previewSlots = new List<Image>();   // 3つの表示枠
    private readonly List<GameObject> _previewSlotBgs = new List<GameObject>();
    private TextMeshProUGUI _gameBtnLabel;
    private TextMeshProUGUI _imageBtnLabel;
    private TextMeshProUGUI _previewHint;
    private bool _busy;

    // 登録済み一覧（タイトル → 画像[url/storage path]の一覧）
    private readonly Dictionary<string, List<(string url, string path)>> _registry
        = new Dictionary<string, List<(string, string)>>();
    private RectTransform _listContent;
    private TextMeshProUGUI _listEmptyLabel;
    private Coroutine _listBuild;
    private Button _armedDeleteBtn;      // 2度押し確認中の削除ボタン
    private string _armedDeleteTitle;

    private static FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;
    private static DocumentReference RegistryDoc => Db.Collection("master").Document("boardgame_images");

    // ================================================================
    // 起動・破棄
    // ================================================================
    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null)
        {
            Debug.LogError("[GameImageAdmin] Canvas が見つかりません");
            Destroy(gameObject);
            return;
        }
        _jp = GetJpFont();
        BuildUI();
        LoadRegistryAsync().Forget();
    }

    private void OnDestroy()
    {
        if (_popup != null) Destroy(_popup);
        if (_overlay != null) Destroy(_overlay);
        DestroyPreview();
    }

    private void Close()
    {
        Destroy(gameObject); // MergeScene 済みなのでシーンUnloadは不要（Zukanと同じ）
    }

    private void DestroyPreview()
    {
        foreach (var s in _previewSprites) if (s != null) Destroy(s);
        foreach (var t in _selectedTexs) if (t != null) Destroy(t);
        _previewSprites.Clear();
        _selectedTexs.Clear();
    }

    // ================================================================
    // 画面
    // ================================================================
    private void BuildUI()
    {
        _overlay = new GameObject("__GameImageAdminOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        var bg = _overlay.AddComponent<Image>();
        OldMapBackground.Apply(bg);
        bg.raycastTarget = true;

        // タイトル帯
        var titleBar = new GameObject("__TitleBar");
        titleBar.transform.SetParent(_overlay.transform, false);
        var trt = titleBar.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(0f, -TITLE_H); trt.offsetMax = Vector2.zero;
        titleBar.AddComponent<Image>().color = C_TITLEBAR;

        var title = MakeLabel(titleBar.transform, "ゲーム画像かんり", _jp, 80, FontStyles.Bold, C_TITLETEXT, 700, TITLE_H, Vector2.zero);
        var tlrt = title.GetComponent<RectTransform>();
        tlrt.anchorMin = Vector2.zero; tlrt.anchorMax = Vector2.one;
        tlrt.offsetMin = new Vector2(48f, 16f); tlrt.offsetMax = new Vector2(-150f, -16f);
        var titleTmp = title.GetComponent<TextMeshProUGUI>();
        titleTmp.alignment = TextAlignmentOptions.MidlineLeft;
        titleTmp.enableAutoSizing = true; titleTmp.fontSizeMax = 80; titleTmp.fontSizeMin = 48;

        var close = MakeButton(_overlay.transform, C_CLOSE, "✕", _jp, 56, Color.white, 120, 120, Vector2.zero, Close);
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-28, -TITLE_H / 2f);

        var hint = MakeLabel(_overlay.transform, "募集ボードに表示されるゲームの画像を登録できます", _jp, 28, FontStyles.Normal, C_MUTED, 0, 44, Vector2.zero);
        StretchTop(hint, 28, TITLE_H + 8, 44);

        // ① ゲームをえらぶ
        var gameBtn = MakeButton(_overlay.transform, C_WHITE, "", _jp, 32, C_INK, 0, 100, Vector2.zero, OpenGamePicker);
        StretchTop(gameBtn, 28, TITLE_H + 64, 100);
        _gameBtnLabel = gameBtn.GetComponentInChildren<TextMeshProUGUI>();
        AutoSize(_gameBtnLabel.gameObject, 22, 32, TextAlignmentOptions.Center);

        // ② しゃしんを追加（最大3枚）
        var imgBtn = MakeButton(_overlay.transform, C_WHITE, "", _jp, 32, C_INK, 0, 100, Vector2.zero, PickImage);
        StretchTop(imgBtn, 28, TITLE_H + 180, 100);
        _imageBtnLabel = imgBtn.GetComponentInChildren<TextMeshProUGUI>();

        // プレビュー枠（3枚ぶんのスロット。写真をタップすると外せる）
        var frame = MakeRect("__PreviewFrame", _overlay.transform, C_CARD, 0, 0);
        RoundedRectSprite.Apply(frame.GetComponent<Image>());
        StretchTop(frame, 28, TITLE_H + 296, 400);
        _previewHint = MakeLabel(frame.transform, "えらんだ しゃしんが ここに出ます（タップで外せます）", _jp, 26, FontStyles.Normal, C_MUTED, 800, 60, Vector2.zero)
            .GetComponent<TextMeshProUGUI>();
        for (int i = 0; i < MAX_IMAGES; i++)
        {
            int slot = i;
            var slotBg = MakeRect("__Slot" + i, frame.transform, new Color(0f, 0f, 0f, 0.06f), 310, 360);
            RoundedRectSprite.Apply(slotBg.GetComponent<Image>());
            slotBg.GetComponent<RectTransform>().anchoredPosition = new Vector2(-330 + i * 330, 0);
            var pv = new GameObject("__Preview");
            pv.transform.SetParent(slotBg.transform, false);
            pv.AddComponent<RectTransform>().sizeDelta = new Vector2(290, 340);
            var img = pv.AddComponent<Image>();
            img.preserveAspect = true;
            img.enabled = false;
            img.raycastTarget = false;
            var btn = slotBg.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => RemoveImage(slot));
            _previewSlots.Add(img);
            _previewSlotBgs.Add(slotBg);
        }

        // ③ とうろく
        var upBtn = MakeButton(_overlay.transform, C_GOLD, "③この画像でとうろくする", _jp, 38, C_INK, 0, 110, Vector2.zero,
            () => UploadAsync().Forget());
        StretchTop(upBtn, 28, TITLE_H + 712, 110);

        // 登録済み一覧
        var header = MakeLabel(_overlay.transform, "とうろく済みの画像", _jp, 30, FontStyles.Bold, C_MUTED, 0, 44, Vector2.zero);
        StretchTop(header, 28, TITLE_H + 850, 44);

        var svGO = new GameObject("__Scroll");
        svGO.transform.SetParent(_overlay.transform, false);
        var svrt = svGO.AddComponent<RectTransform>();
        svrt.anchorMin = new Vector2(0f, 0f); svrt.anchorMax = new Vector2(1f, 1f);
        svrt.offsetMin = new Vector2(24f, 32f);
        svrt.offsetMax = new Vector2(-24f, -(TITLE_H + 902f));
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
        _listContent = ctGO.AddComponent<RectTransform>();
        _listContent.anchorMin = new Vector2(0f, 1f); _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.offsetMin = _listContent.offsetMax = Vector2.zero;
        scroll.content = _listContent;

        _listEmptyLabel = MakeLabel(vpGO.transform, "読み込み中…", _jp, 28, FontStyles.Normal, C_MUTED, 0, 0, Vector2.zero)
            .GetComponent<TextMeshProUGUI>();
        var ert = _listEmptyLabel.GetComponent<RectTransform>();
        ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one;
        ert.offsetMin = ert.offsetMax = Vector2.zero;

        RefreshStepLabels();
    }

    private void RefreshStepLabels()
    {
        if (_gameBtnLabel != null)
            _gameBtnLabel.text = string.IsNullOrEmpty(_selectedTitle)
                ? "①ゲームをえらぶ（タップ）" : $"①『{_selectedTitle}』";
        if (_imageBtnLabel != null)
            _imageBtnLabel.text = _selectedTexs.Count == 0
                ? $"②しゃしんをえらぶ（まとめて{MAX_IMAGES}枚まで）"
                : $"②しゃしんを追加（{_selectedTexs.Count}/{MAX_IMAGES}枚）";
        if (_previewHint != null) _previewHint.gameObject.SetActive(_selectedTexs.Count == 0);

        // プレビュースロットを選択状態に合わせて更新
        foreach (var s in _previewSprites) if (s != null) Destroy(s);
        _previewSprites.Clear();
        for (int i = 0; i < _previewSlots.Count; i++)
        {
            bool on = i < _selectedTexs.Count;
            _previewSlots[i].enabled = on;
            if (!on) { _previewSlots[i].sprite = null; continue; }
            var tex = _selectedTexs[i];
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _previewSprites.Add(sp);
            _previewSlots[i].sprite = sp;
        }
    }

    /// <summary>プレビューのスロットをタップして選択済みの写真を外す。</summary>
    private void RemoveImage(int index)
    {
        if (index < 0 || index >= _selectedTexs.Count) return;
        if (_selectedTexs[index] != null) Destroy(_selectedTexs[index]);
        _selectedTexs.RemoveAt(index);
        RefreshStepLabels();
    }

    // ================================================================
    // ① ゲーム選択（カタログ検索＋持ち込みタイトルの自由入力）
    // ================================================================
    private void OpenGamePicker()
    {
        ClosePopupPanel();
        _popup = BuildDimmed("__GamePicker", ClosePopupPanel);
        var panel = BuildPanel(_popup.transform, 880, 1400);
        MakeLabel(panel.transform, "ゲームをえらぶ", _jp, 40, FontStyles.Bold, C_TITLE, 700, 60, new Vector2(0, 630));

        var games = BoardGameCatalog.Load()?.games ?? new BoardGameEntry[0];
        var input = BuildSearchInput(panel.transform, new Vector2(0, 540), 760, 90, "タイトルで検索…");

        // 候補リスト（仮想スクロール。全740件でも行は十数個しか作らない）
        var picker = TitlePickerList.Create(panel.transform, new Vector2(0, 25), 780, 910, _jp, SelectTitle);
        var allTitles = games.Select(g => g.title).Where(t => !string.IsNullOrEmpty(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        picker.PrewarmAsync(allTitles);

        // 持ち込みゲーム用: 検索で見つからないタイトルはそのまま登録できる
        var customBtn = MakeButton(panel.transform, C_BORDER, "", _jp, 28, C_TITLE, 760, 96, new Vector2(0, -480), null);
        var customLabel = customBtn.GetComponentInChildren<TextMeshProUGUI>();
        AutoSize(customLabel.gameObject, 20, 28, TextAlignmentOptions.Center);
        customBtn.SetActive(false);

        void Refresh(string q)
        {
            q = (q ?? "").Trim();
            string qLower = q.ToLowerInvariant();
            picker.SetItems(string.IsNullOrEmpty(qLower) ? allTitles
                : games.Where(g =>
                        (g.title != null && g.title.ToLowerInvariant().Contains(qLower))
                        || (g.title_en != null && g.title_en.ToLowerInvariant().Contains(qLower)))
                    .Select(g => g.title).Where(t => !string.IsNullOrEmpty(t))
                    .OrderBy(t => t, StringComparer.Ordinal).ToList());

            // 自由入力タイトル（持ち込みゲーム）: カタログに完全一致が無いときだけ出す
            bool custom = !string.IsNullOrEmpty(q)
                && q.Length <= CUSTOM_TITLE_MAX
                && !games.Any(g => g.title == q);
            customBtn.SetActive(custom);
            if (custom)
            {
                if (_jp != null) _jp.TryAddCharacters(q, out _);
                customLabel.text = $"持ち込みゲーム「{q}」として登録";
                var btn = customBtn.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                string picked = q;
                btn.onClick.AddListener(() => SelectTitle(picked));
            }
        }
        input.onValueChanged.AddListener(Refresh);
        Refresh("");

        MakeButton(panel.transform, C_WHITE, "とじる", _jp, 30, C_MUTED, 360, 84, new Vector2(0, -620), ClosePopupPanel);
    }

    private void SelectTitle(string title)
    {
        _selectedTitle = title;
        ClosePopupPanel();
        RefreshStepLabels();
        if (_registry.ContainsKey(title))
            FriendMenuController.ShowToast("このゲームは登録済みです\n登録すると画像を差し替えます");
    }

    // ================================================================
    // ② 画像選択（ギャラリー）
    // ================================================================
    private void PickImage()
    {
        if (_selectedTexs.Count >= MAX_IMAGES)
        {
            FriendMenuController.ShowToast($"写真は{MAX_IMAGES}枚までです\n（プレビューをタップすると外せます）");
            return;
        }
#if UNITY_EDITOR
        // エディタのファイルパネルは複数選択非対応なので1枚ずつ
        string path = UnityEditor.EditorUtility.OpenFilePanel("画像を選択", "", "png,jpg,jpeg");
        OnImagePicked(path);
#else
        // ギャラリーからまとめて選択（3枚まで。1枚だけ選んでもOK）
        NativeGallery.GetImagesFromGallery(OnImagesPicked, "ゲームの画像をえらんでください（3枚まで）");
#endif
    }

    /// <summary>複数選択の受け取り。空きスロットぶんだけ順に追加する。</summary>
    private void OnImagesPicked(string[] paths)
    {
        if (paths == null || paths.Length == 0) return; // キャンセル
        int room = MAX_IMAGES - _selectedTexs.Count;
        foreach (var path in paths.Take(room)) OnImagePicked(path);
        if (paths.Length > room)
            FriendMenuController.ShowToast($"写真は{MAX_IMAGES}枚までなので\nはじめの{room}枚だけ追加しました");
    }

    private void OnImagePicked(string path)
    {
        if (string.IsNullOrEmpty(path)) return; // キャンセル

        Texture2D tex;
#if UNITY_EDITOR
        var bytes = System.IO.File.ReadAllBytes(path);
        tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!tex.LoadImage(bytes)) { Destroy(tex); tex = null; }
#else
        // 読み込み時に長辺1024へ縮小。EncodeToJPG するので readable のまま読む
        tex = NativeGallery.LoadImageAtPath(path, 1024, markTextureNonReadable: false);
#endif
        if (tex == null)
        {
            FriendMenuController.ShowToast("画像を読み込めませんでした");
            return;
        }

        _selectedTexs.Add(tex);
        RefreshStepLabels();
    }

    // ================================================================
    // ③ アップロード
    // ================================================================
    private async UniTask UploadAsync()
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(_selectedTitle)) { FriendMenuController.ShowToast("先に①でゲームをえらんでください"); return; }
        if (_selectedTexs.Count == 0) { FriendMenuController.ShowToast("先に②でしゃしんをえらんでください"); return; }

        _busy = true;
        if (AssetsDatabase.instance != null) AssetsDatabase.instance.LoadingPanel.SetActive(true);
        try
        {
            // 各画像を縮小→JPG化→アップロード（IconUploaderManager と同じ流儀）
            string safe = SanitizeFileName(_selectedTitle);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var items = new List<object>();
            var newEntries = new List<(string url, string path)>();
            for (int i = 0; i < _selectedTexs.Count; i++)
            {
                var small = BoardGamePhotoStore.Downscale(_selectedTexs[i], UPLOAD_MAX_SIZE);
                byte[] jpg = small.EncodeToJPG(85);
                if (small != _selectedTexs[i]) Destroy(small);

                string storagePath = $"{STORAGE_FOLDER}/{ts}_{safe}_{i + 1}.jpg";
                var fileRef = FirebaseStorage.DefaultInstance
                    .GetReferenceFromUrl(STORAGE_BASE).Child(storagePath);
                await fileRef.PutBytesAsync(jpg, new MetadataChange { ContentType = "image/jpeg" }).AsUniTask();
                var uri = await fileRef.GetDownloadUrlAsync().AsUniTask();
                items.Add(new Dictionary<string, object> { { "url", uri.ToString() }, { "path", storagePath } });
                newEntries.Add((uri.ToString(), storagePath));
            }

            // Firestore へ登録。images.タイトル ごと上書きして旧形式のフィールドも残さない
            // （ドキュメントが無い初回に UpdateAsync が失敗しないよう、先に images を確実に作る）
            await RegistryDoc.SetAsync(new Dictionary<string, object>
            {
                { "images", new Dictionary<string, object>() },
            }, SetOptions.MergeAll).AsUniTask();
            await RegistryDoc.UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("images", _selectedTitle),
                  new Dictionary<string, object> { { "items", items } } },
            }).AsUniTask();

            // 差し替えの場合は古いStorageファイルを掃除（失敗しても登録は成立している）
            if (_registry.TryGetValue(_selectedTitle, out var olds))
            {
                foreach (var old in olds)
                {
                    if (string.IsNullOrEmpty(old.path)) continue;
                    try
                    {
                        await FirebaseStorage.DefaultInstance
                            .GetReferenceFromUrl(STORAGE_BASE).Child(old.path).DeleteAsync().AsUniTask();
                    }
                    catch (Exception ex) { Debug.LogWarning($"[GameImageAdmin] 旧画像の削除に失敗: {ex.Message}"); }
                }
            }

            _registry[_selectedTitle] = newEntries;
            FriendMenuController.ShowToast($"『{_selectedTitle}』の画像を{newEntries.Count}枚 登録しました！");

            // 次の登録に備えて選択をリセット
            _selectedTitle = "";
            DestroyPreview();
            RefreshStepLabels();
            RebuildList();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameImageAdmin] アップロードエラー: {ex.Message}");
            FriendMenuController.ShowToast("アップロードに失敗しました\n（管理者アカウントでログインしていますか？）");
        }
        finally
        {
            _busy = false;
            if (AssetsDatabase.instance != null) AssetsDatabase.instance.LoadingPanel.SetActive(false);
        }
    }

    private static string SanitizeFileName(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        var r = sb.ToString();
        return r.Length > 40 ? r.Substring(0, 40) : r;
    }

    // ================================================================
    // 登録済み一覧
    // ================================================================
    private async UniTask LoadRegistryAsync()
    {
        try
        {
            var snap = await RegistryDoc.GetSnapshotAsync().AsUniTask();
            _registry.Clear();
            if (snap.Exists)
            {
                var d = snap.ToDictionary();
                if (d.TryGetValue("images", out var io) && io is Dictionary<string, object> im)
                {
                    foreach (var kv in im)
                    {
                        if (!(kv.Value is Dictionary<string, object> v)) continue;
                        var list = new List<(string, string)>();
                        if (v.TryGetValue("items", out var itemsO) && itemsO is List<object> items)
                        {
                            foreach (var it in items)
                                if (it is Dictionary<string, object> m)
                                    list.Add((
                                        m.TryGetValue("url", out var u2) ? u2 as string ?? "" : "",
                                        m.TryGetValue("path", out var p2) ? p2 as string ?? "" : ""));
                        }
                        else if (v.TryGetValue("url", out var u)) // 旧形式（1枚）との互換
                        {
                            list.Add((u as string ?? "",
                                v.TryGetValue("path", out var pp) ? pp as string ?? "" : ""));
                        }
                        if (list.Count > 0) _registry[kv.Key] = list;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameImageAdmin] 一覧の読み込みエラー: {ex.Message}");
        }
        if (this == null || _overlay == null) return;
        RebuildList();
    }

    private void RebuildList()
    {
        if (_listBuild != null) StopCoroutine(_listBuild);
        _listBuild = StartCoroutine(BuildListRows());
    }

    /// <summary>一覧の行を1フレーム4行までで生成する（TMP大量生成のOOM対策）。</summary>
    private IEnumerator BuildListRows()
    {
        if (_listContent == null) yield break;
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);
        _armedDeleteBtn = null;
        _armedDeleteTitle = null;

        var titles = _registry.Keys.OrderBy(t => t, StringComparer.Ordinal).ToList();
        _listEmptyLabel.text = titles.Count == 0 ? "まだ登録がありません" : "";

        // タイトルのグリフを先に登録
        if (_jp != null)
        {
            var sb = new System.Text.StringBuilder("枚（）0123456789");
            foreach (var t in titles) sb.Append(t);
            string all = sb.ToString();
            for (int i = 0; i < all.Length; i += 40)
            {
                _jp.TryAddCharacters(all.Substring(i, Mathf.Min(40, all.Length - i)), out _);
                yield return null;
            }
        }

        const float ROW_H = 96f, GAP = 12f;
        _listContent.sizeDelta = new Vector2(0f, titles.Count * (ROW_H + GAP) + GAP);
        for (int i = 0; i < titles.Count; i++)
        {
            string t = titles[i];
            var row = MakeRect("__Row", _listContent, C_CARD, 0, ROW_H);
            RoundedRectSprite.Apply(row.GetComponent<Image>());
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(4f, -(GAP + i * (ROW_H + GAP)) - ROW_H);
            rt.offsetMax = new Vector2(-4f, -(GAP + i * (ROW_H + GAP)));

            int count = _registry.TryGetValue(t, out var entry) ? entry.Count : 0;
            var label = MakeLabel(row.transform, $"{t}（{count}枚）", _jp, 30, FontStyles.Bold, C_INK, 0, ROW_H, Vector2.zero);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(28f, 0f); lrt.offsetMax = new Vector2(-240f, 0f);
            AutoSize(label, 20, 30, TextAlignmentOptions.MidlineLeft);

            var del = MakeButton(row.transform, C_DANGER, "削除", _jp, 26, Color.white, 180, 72, Vector2.zero, null);
            var drt = del.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(1f, 0.5f);
            drt.anchoredPosition = new Vector2(-20f, 0f);
            var delBtn = del.GetComponent<Button>();
            delBtn.onClick.AddListener(() => OnDeletePressed(t, delBtn));

            if (i % 4 == 3) yield return null;
        }
        _listBuild = null;
    }

    /// <summary>削除は誤タップ防止のため2度押しで実行（ログアウトボタンと同じ流儀）。</summary>
    private void OnDeletePressed(string title, Button btn)
    {
        if (_armedDeleteTitle != title)
        {
            // 前に確認中だったボタンは元に戻す
            if (_armedDeleteBtn != null)
            {
                _armedDeleteBtn.image.color = C_DANGER;
                var oldLabel = _armedDeleteBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (oldLabel != null) oldLabel.text = "削除";
            }
            _armedDeleteTitle = title;
            _armedDeleteBtn = btn;
            btn.image.color = C_DANGER_ARMED;
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "ほんとうに？";
            return;
        }
        DeleteAsync(title).Forget();
    }

    private async UniTask DeleteAsync(string title)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await RegistryDoc.UpdateAsync(new Dictionary<FieldPath, object>
            {
                { new FieldPath("images", title), FieldValue.Delete },
            }).AsUniTask();

            if (_registry.TryGetValue(title, out var entries))
            {
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.path)) continue;
                    try
                    {
                        await FirebaseStorage.DefaultInstance
                            .GetReferenceFromUrl(STORAGE_BASE).Child(entry.path).DeleteAsync().AsUniTask();
                    }
                    catch (Exception ex) { Debug.LogWarning($"[GameImageAdmin] Storage削除に失敗: {ex.Message}"); }
                }
            }
            _registry.Remove(title);
            FriendMenuController.ShowToast($"『{title}』の画像を削除しました");
            RebuildList();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameImageAdmin] 削除エラー: {ex.Message}");
            FriendMenuController.ShowToast("削除に失敗しました");
        }
        finally { _busy = false; }
    }

    // ================================================================
    // ポップアップ・UI部品（RecruitBoardController と同じ流儀）
    // ================================================================
    private void ClosePopupPanel()
    {
        if (_popup != null) Destroy(_popup);
        _popup = null;
    }

    private GameObject BuildDimmed(string name, Action onTapOutside)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var dim = go.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onTapOutside());
        return go;
    }

    private GameObject BuildPanel(Transform parent, float w, float h)
    {
        var border = MakeRect("__Border", parent, C_BORDER, w + 24, h + 24);
        RoundedRectSprite.Apply(border.GetComponent<Image>());
        var panel = MakeRect("__Panel", border.transform, C_PARCHMENT, w, h);
        RoundedRectSprite.Apply(panel.GetComponent<Image>());
        panel.AddComponent<Button>().transition = Selectable.Transition.None;
        return panel;
    }

    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static void StretchTop(GameObject go, float x, float top, float h)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(x, -top - h);
        rt.offsetMax = new Vector2(-x, -top);
    }

    private static void AutoSize(GameObject labelGO, float min, float max, TextAlignmentOptions align)
    {
        var tmp = labelGO.GetComponent<TextMeshProUGUI>();
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = min; tmp.fontSizeMax = max;
        tmp.alignment = align;
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
        tmp.enableWordWrapping = false; // 幅0→アンカーで広げるラベル対策（RecruitBoard参照）
        return go;
    }

    private static GameObject MakeButton(Transform parent, Color bg, string text, TMP_FontAsset font,
        float fontSize, Color textColor, float w, float h, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect("__Btn_" + text, parent, bg, w, h);
        RoundedRectSprite.Apply(go.GetComponent<Image>());
        go.GetComponent<Image>().color = bg;
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        var label = MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        if (onClick != null) btn.onClick.AddListener(onClick);
        return go;
    }

    /// <summary>検索入力欄（RecruitBoardController と同じ構成）。</summary>
    private TMP_InputField BuildSearchInput(Transform parent, Vector2 pos, float w, float h, string placeholder)
    {
        var barGO = new GameObject("__SearchInput");
        barGO.transform.SetParent(parent, false);
        var brt = barGO.AddComponent<RectTransform>();
        brt.sizeDelta = new Vector2(w, h);
        brt.anchoredPosition = pos;
        var bimg = barGO.AddComponent<Image>();
        RoundedRectSprite.Apply(bimg);
        bimg.color = C_WHITE;

        var areaGO = new GameObject("__TextArea");
        areaGO.transform.SetParent(barGO.transform, false);
        var art = areaGO.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(28f, 6f); art.offsetMax = new Vector2(-28f, -6f);
        areaGO.AddComponent<RectMask2D>();

        var phGO = new GameObject("__Placeholder");
        phGO.transform.SetParent(areaGO.transform, false);
        var prt = phGO.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ptmp = phGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ptmp.font = _jp;
        ptmp.text = placeholder;
        ptmp.fontSize = 30; ptmp.color = C_MUTED; ptmp.fontStyle = FontStyles.Italic;
        ptmp.alignment = TextAlignmentOptions.Left;

        var txGO = new GameObject("__Text");
        txGO.transform.SetParent(areaGO.transform, false);
        var txrt = txGO.AddComponent<RectTransform>();
        txrt.anchorMin = Vector2.zero; txrt.anchorMax = Vector2.one;
        txrt.offsetMin = txrt.offsetMax = Vector2.zero;
        var ttmp = txGO.AddComponent<TextMeshProUGUI>();
        if (_jp != null) ttmp.font = _jp;
        ttmp.fontSize = 30; ttmp.color = C_INK;
        ttmp.alignment = TextAlignmentOptions.Left;

        var input = barGO.AddComponent<TMP_InputField>();
        input.textViewport = art;
        input.textComponent = ttmp;
        input.placeholder = ptmp;
        input.fontAsset = _jp;
        input.pointSize = 30;
        input.customCaretColor = true;
        input.caretColor = C_INK;
        input.selectionColor = new Color(0.84f, 0.66f, 0.18f, 0.4f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }
}
