using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Linq;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Firebase.Storage;
using Firebase.Firestore;
using Firebase.Extensions;
using Cysharp.Threading.Tasks;

/// <summary>
/// アイコンアップローダー（IconUploaderシーン用）
/// 画像を Firebase Storage にアップロードし、
/// アイテムデータを Firestore に保存する。
///
/// Storage パス: gs://intales-a0459.firebasestorage.app/item/{job}/
/// Firestore  : items コレクション
/// </summary>
public class IconUploaderManager : MonoBehaviour
{
    // ---- 職業 ----
    private static readonly (string label, string key)[] JOBS =
    {
        ("戦士",    "warrior"),
        ("魔法使い","magician"),
        ("弓使い",  "archer"),
        ("銃使い",  "gunner"),
    };

    // ---- カテゴリ ----
    private static readonly (string label, string key)[] CATS =
    {
        ("武器",    "weapon"),
        ("頭",      "head"),
        ("体",      "body"),
        ("足",      "feet"),
        ("スキルA", "skill_book_a"),
        ("スキルB", "skill_book_b"),
    };

    private const string STORAGE_BASE = "gs://intales-a0459.firebasestorage.app";

    // ---- UI 参照 ----
    private TMP_FontAsset _jp;
    private TMP_InputField _itemNameInput;
    private TMP_InputField _gameNameInput;
    private TMP_InputField _filePathInput;   // 画像ファイルパス
    private TMP_InputField _descriptionInput; // 説明
    private RawImage       _previewImage;

    private TextMeshProUGUI _statusLabel;
    private TextMeshProUGUI _urlLabel;       // アップロード後の URL 表示
    private GameObject[]   _jobBtns;
    private GameObject[]   _catBtns;
    private int _selectedJob = 0;
    private int _selectedCat = 0;

    // ---- 状態 ----
    private Texture2D   _selectedTex;
    private byte[]      _selectedBytes;
    private string      _selectedFileName;
    private bool        _uploading = false;
    private string      _lastDownloadUrl = "";
    private Button      _uploadBtn;

    // ---- 色 ----
    private static readonly Color C_BG    = new Color(0.10f,0.08f,0.06f);
    private static readonly Color C_PANEL = new Color(0.18f,0.14f,0.10f);
    private static readonly Color C_GOLD  = new Color(0.92f,0.72f,0.22f);
    private static readonly Color C_TEXT  = new Color(0.95f,0.90f,0.78f);
    private static readonly Color C_MUTED = new Color(0.55f,0.48f,0.38f);
    private static readonly Color C_BTN_PICK   = new Color(0.30f,0.20f,0.50f);
    private static readonly Color C_BTN_UPLOAD = new Color(0.15f,0.45f,0.20f);
    private static readonly Color C_BTN_COPY   = new Color(0.20f,0.35f,0.55f);
    private static readonly Color C_ERR   = new Color(0.85f,0.25f,0.25f);
    private static readonly Color C_OK    = new Color(0.28f,0.72f,0.28f);

    void Start()
    {
        // エディターでは直接ロード（シーン新規ロード時にメモリにない場合の対策）
#if UNITY_EDITOR
        _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/jp.asset");
        if (_jp == null)
            _jp = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts 1/jp.asset");
#endif
        // エディター以外 or 上記で取得できなかった場合のフォールバック
        if (_jp == null)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _jp = fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
        }
        BuildUI();
    }






    void ApplyFont(TextMeshProUGUI t) { if (_jp != null) t.font = _jp; }
    void SS(string s, Color c) { if (_statusLabel != null) { _statusLabel.text = s; _statusLabel.color = c; } }

    private void RefreshUploadBtn()
    {
        if (_uploadBtn == null) return;
        bool ready = _selectedBytes != null && _selectedBytes.Length > 0
                  && !string.IsNullOrWhiteSpace(_itemNameInput?.text)
                  && !string.IsNullOrWhiteSpace(_gameNameInput?.text)
                  && !string.IsNullOrWhiteSpace(_filePathInput?.text);
        _uploadBtn.interactable = ready;
        var img = _uploadBtn.GetComponent<Image>();
        if (img != null) img.color = ready ? C_BTN_UPLOAD : new Color(C_BTN_UPLOAD.r, C_BTN_UPLOAD.g, C_BTN_UPLOAD.b, 0.3f);
        var txt = _uploadBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.color = ready ? C_TEXT : new Color(C_TEXT.r, C_TEXT.g, C_TEXT.b, 0.3f);
    }

    // ================================================================
    // ファイル選択
    // ================================================================
    private void OnPickFile()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "画像を選択", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path))
        {
            _filePathInput.text = path;
            LoadImageFromPath(path);
        }
#else
        SS("ファイルパスを直接入力してください", C_MUTED);
#endif
    }

    private void OnPathChanged(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            LoadImageFromPath(path);
    }

    private void LoadImageFromPath(string path)
    {
        try
        {
            _selectedBytes    = File.ReadAllBytes(path);
            _selectedFileName = Path.GetFileName(path);

            if (_selectedTex != null) Destroy(_selectedTex);
            _selectedTex = new Texture2D(2, 2);
            _selectedTex.LoadImage(_selectedBytes);

            _previewImage.texture = _selectedTex;
            _previewImage.color   = Color.white;
            SS($"画像読み込み完了: {_selectedFileName} ({_selectedBytes.Length/1024f:F1} KB)", C_OK);
            RefreshUploadBtn();
        }
        catch (System.Exception ex)
        {
            SS($"画像読み込みエラー: {ex.Message}", C_ERR);
        }
    }

    // ================================================================
    // アップロード＆Firestore保存
    // ================================================================
    private async UniTaskVoid UploadAndSave()
    {
        if (_uploading) return;

        // バリデーション
        string itemName = _itemNameInput.text.Trim();
        string gameName = _gameNameInput.text.Trim();
        if (_selectedBytes == null || _selectedBytes.Length == 0)
        {
            SS("画像が選択されていません", C_ERR); return;
        }
        if (string.IsNullOrEmpty(itemName))
        {
            SS("アイテム名を入力してください", C_ERR); return;
        }

        _uploading = true;
        SS("Firebase Storage にアップロード中...", C_MUTED);

        try
        {
            string jobKey  = JOBS[_selectedJob].key;
            string catKey  = CATS[_selectedCat].key;
            string ext     = Path.GetExtension(_selectedFileName).ToLower();
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            // 保存先パス: item/{jobKey}/{timestamp}_{itemName}{ext}
            string timestamp  = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeItem   = itemName.Replace(" ","_").Replace("/","_");
            string storagePath = $"item/{jobKey}/{timestamp}_{safeItem}{ext}";

            // ---- Storage アップロード ----
            var storage     = FirebaseStorage.DefaultInstance;
            var storageRef  = storage.GetReferenceFromUrl(STORAGE_BASE);
            var fileRef     = storageRef.Child(storagePath);

            SS($"アップロード中: {storagePath}", C_MUTED);

            var uploadMeta = new MetadataChange { ContentType = ext == ".png" ? "image/png" : "image/jpeg" };
            await fileRef.PutBytesAsync(_selectedBytes, uploadMeta).AsUniTask();

            SS("ダウンロード URL を取得中...", C_MUTED);

            // ---- ダウンロード URL 取得 ----
            Uri downloadUri = await fileRef.GetDownloadUrlAsync().AsUniTask();
            _lastDownloadUrl = downloadUri.ToString();

            SS("Firestore にアイテムデータを保存中...", C_MUTED);

            // ---- Firestore 保存 ----
            var db = FirebaseFirestore.DefaultInstance;
            var itemData = new Dictionary<string, object>
            {
                { "name",       itemName },
                { "game",       gameName },
                { "job",        jobKey   },
                { "slot_type",  catKey   },
                { "icon_url",   _lastDownloadUrl },
                { "storage_path", storagePath },
                { "created_at", Timestamp.GetCurrentTimestamp() },
                { "description", _descriptionInput?.text.Trim() ?? "" },
            };

            // item/{jobKey}/items/{autoId} に保存
            // item コレクション → 職業ドキュメント → items サブコレクション
            var docRef = await db
                .Collection("item")
                .Document(jobKey)
                .Collection("items")
                .AddAsync(itemData)
                .AsUniTask();

            // item/_metadata の last_updated を更新（同期判定用）
            await db.Collection("item").Document("_metadata")
                .SetAsync(new Dictionary<string, object>
                {
                    { "last_updated", Timestamp.GetCurrentTimestamp() }
                }, SetOptions.MergeAll)
                .AsUniTask();

            SS($"完了！ Firestore ID: {docRef.Id}", C_OK);
            if (_urlLabel != null)
            {
                _urlLabel.text  = _lastDownloadUrl;
                _urlLabel.color = new Color(0.55f,0.75f,1.00f);
            }
            Debug.Log($"[IconUploader] 保存完了 id={docRef.Id} url={_lastDownloadUrl}");
        }
        catch (System.Exception ex)
        {
            SS($"エラー: {ex.Message}", C_ERR);
            Debug.LogError($"[IconUploader] {ex}");
        }
        finally
        {
            _uploading = false;
        }
    }

    private void CopyUrl()
    {
        if (string.IsNullOrEmpty(_lastDownloadUrl))
        {
            SS("コピーするURLがありません", C_MUTED); return;
        }
        GUIUtility.systemCopyBuffer = _lastDownloadUrl;
        SS("URLをクリップボードにコピーしました", C_OK);
    }

    // ================================================================
    // UI 構築
    // ================================================================
    private void BuildUI()
    {
        var cGO = new GameObject("Canvas");
        var cv  = cGO.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        var cs = cGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1080, 1920);
        cGO.AddComponent<GraphicRaycaster>();
        var sys = new GameObject("EventSystem");
        sys.AddComponent<UnityEngine.EventSystems.EventSystem>();
        sys.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // 背景
        var bg = Go("BG", cGO.transform); Stretch(bg);
        bg.AddComponent<Image>().color = C_BG;

        float y = -70f;

        // タイトル
        var tGO = Go("Title", cGO.transform);
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0,1); tRT.anchorMax = new Vector2(1,1);
        tRT.pivot = new Vector2(.5f,1f);
        tRT.anchoredPosition = new Vector2(0,y); tRT.sizeDelta = new Vector2(0,100f);
        var tt = tGO.AddComponent<TextMeshProUGUI>();
        tt.text = "アイコン アップローダー";
        tt.fontSize = 52; tt.fontStyle = FontStyles.Bold;
        tt.alignment = TextAlignmentOptions.Center; tt.color = C_GOLD;
        ApplyFont(tt); y -= 120f;

        // ---- アイテム名 ----
        SectionLabel("アイテム名", cGO.transform, y); y -= 52f;
        _itemNameInput = MakeInput(cGO.transform, "例: ブロンズソード", y, ref y);
        _itemNameInput.onValueChanged.AddListener(_ => RefreshUploadBtn());

        // ---- ゲーム名 ----
        SectionLabel("ゲーム名", cGO.transform, y); y -= 52f;
        _gameNameInput = MakeInput(cGO.transform, "例: カタン、人狼", y, ref y);
        _gameNameInput.onValueChanged.AddListener(_ => RefreshUploadBtn());

        // ---- 説明 ----
        SectionLabel("説明", cGO.transform, y); y -= 52f;
        _descriptionInput = MakeInput(cGO.transform, "例: 防御力が上がる装備", y, ref y, true);

        // ---- 職業選択 ----
        SectionLabel("装備可能職業", cGO.transform, y); y -= 58f;
        _jobBtns = new GameObject[JOBS.Length];
        float jw = 225f, jh = 90f, jgx = 18f;
        float totalJW = jw * JOBS.Length + jgx * (JOBS.Length-1);
        for (int i = 0; i < JOBS.Length; i++)
        {
            float jx = -totalJW*.5f + jw*.5f + i*(jw+jgx);
            var jb = Go($"Job{i}", cGO.transform);
            var jrt = jb.GetComponent<RectTransform>();
            jrt.anchorMin = new Vector2(.5f,1f); jrt.anchorMax = new Vector2(.5f,1f);
            jrt.pivot = new Vector2(.5f,0f);
            jrt.anchoredPosition = new Vector2(jx,y); jrt.sizeDelta = new Vector2(jw,jh);
            jb.AddComponent<Image>().color = C_PANEL;
            var jbtn = jb.AddComponent<Button>();
            jbtn.navigation = new Navigation { mode = Navigation.Mode.None };
            int cap = i; jbtn.onClick.AddListener(() => SelectJob(cap));
            var lt = Go("L", jb.transform);
            var lrt = lt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var ltxt = lt.AddComponent<TextMeshProUGUI>();
            ltxt.text = JOBS[i].label; ltxt.fontSize = 38;
            ltxt.fontStyle = FontStyles.Bold;
            ltxt.alignment = TextAlignmentOptions.Center; ltxt.color = C_TEXT;
            ltxt.raycastTarget = false; ApplyFont(ltxt);
            _jobBtns[i] = jb;
        }
        y -= jh + 30f;

        // ---- カテゴリ選択 ----
        SectionLabel("種類", cGO.transform, y); y -= 58f;
        _catBtns = new GameObject[CATS.Length];
        float bw = 305f, bh = 90f, gx = 26f;
        for (int i = 0; i < CATS.Length; i++)
        {
            int col = i%2, row = i/2;
            float bx = col==0 ? -(bw*.5f+gx*.5f) : (bw*.5f+gx*.5f);
            float by = y - row*(bh+14f);
            var cb = Go($"Cat{i}", cGO.transform);
            var crt = cb.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(.5f,1f); crt.anchorMax = new Vector2(.5f,1f);
            crt.pivot = new Vector2(.5f,0f);
            crt.anchoredPosition = new Vector2(bx,by); crt.sizeDelta = new Vector2(bw,bh);
            cb.AddComponent<Image>().color = C_PANEL;
            var cbtn = cb.AddComponent<Button>();
            cbtn.navigation = new Navigation { mode = Navigation.Mode.None };
            int capc = i; cbtn.onClick.AddListener(() => SelectCat(capc));
            var lt = Go("L", cb.transform);
            var lrt = lt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var ltxt = lt.AddComponent<TextMeshProUGUI>();
            ltxt.text = CATS[i].label; ltxt.fontSize = 38;
            ltxt.fontStyle = FontStyles.Bold;
            ltxt.alignment = TextAlignmentOptions.Center; ltxt.color = C_TEXT;
            ltxt.raycastTarget = false; ApplyFont(ltxt);
            _catBtns[i] = cb;
        }
        y -= (Mathf.CeilToInt(CATS.Length/2f))*(bh+14f)+36f;

        // ---- 画像選択エリア ----
        SectionLabel("アイコン画像", cGO.transform, y); y -= 58f;

        // パスフィールド + Browseボタン
        var pathRow = Go("PathRow", cGO.transform);
        var prRT = pathRow.GetComponent<RectTransform>();
        prRT.anchorMin = new Vector2(.05f,1f); prRT.anchorMax = new Vector2(.95f,1f);
        prRT.pivot = new Vector2(.5f,1f);
        prRT.anchoredPosition = new Vector2(0,y); prRT.sizeDelta = new Vector2(0,88f);

        // パス入力
        var pathInGO = Go("PathIn", pathRow.transform);
        var piRT = pathInGO.GetComponent<RectTransform>();
        piRT.anchorMin = new Vector2(0,0); piRT.anchorMax = new Vector2(.72f,1f);
        piRT.offsetMin = new Vector2(0,0); piRT.offsetMax = new Vector2(-8f,0f);
        pathInGO.AddComponent<Image>().color = C_PANEL;
        var pathTF = pathInGO.AddComponent<TMP_InputField>();
        pathTF.characterLimit = 500;
        pathTF.onEndEdit.AddListener(OnPathChanged);
        pathTF.onValueChanged.AddListener(_ => RefreshUploadBtn());
        _filePathInput = pathTF;
        MakeInputInternal(pathTF, pathInGO, "画像ファイルのパス...");

        // Browse ボタン
        var browseGO = Go("Browse", pathRow.transform);
        var bwRT = browseGO.GetComponent<RectTransform>();
        bwRT.anchorMin = new Vector2(.74f,0f); bwRT.anchorMax = new Vector2(1f,1f);
        bwRT.offsetMin = bwRT.offsetMax = Vector2.zero;
        browseGO.AddComponent<Image>().color = C_BTN_PICK;
        var bwBtn = browseGO.AddComponent<Button>();
        bwBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        bwBtn.onClick.AddListener(OnPickFile);
        var bwL = Go("L", browseGO.transform);
        var bwLRT = bwL.GetComponent<RectTransform>();
        bwLRT.anchorMin = Vector2.zero; bwLRT.anchorMax = Vector2.one;
        bwLRT.offsetMin = bwLRT.offsetMax = Vector2.zero;
        var bwT = bwL.AddComponent<TextMeshProUGUI>();
        bwT.text = "選択"; bwT.fontSize = 38; bwT.fontStyle = FontStyles.Bold;
        bwT.alignment = TextAlignmentOptions.Center; bwT.color = C_TEXT;
        bwT.raycastTarget = false; ApplyFont(bwT);

        y -= 106f;

        // プレビュー
        var prevBG = Go("PrevBG", cGO.transform);
        var pvRT = prevBG.GetComponent<RectTransform>();
        pvRT.anchorMin = new Vector2(.5f,1f); pvRT.anchorMax = new Vector2(.5f,1f);
        pvRT.pivot = new Vector2(.5f,1f);
        pvRT.anchoredPosition = new Vector2(0,y); pvRT.sizeDelta = new Vector2(220,220);
        prevBG.AddComponent<Image>().color = C_PANEL;
        var pGO = Go("Prev", prevBG.transform);
        var pRT = pGO.GetComponent<RectTransform>();
        pRT.anchorMin = new Vector2(.08f,.08f); pRT.anchorMax = new Vector2(.92f,.92f);
        pRT.offsetMin = pRT.offsetMax = Vector2.zero;
        _previewImage = pGO.AddComponent<RawImage>();
        _previewImage.color = new Color(.15f,.12f,.1f);
        y -= 240f;

        // ---- アップロードボタン ----
        var upBtn = FlatBtn("UploadBtn", cGO.transform, C_BTN_UPLOAD,
            "アップロード & Firestore 保存", 44, 0, y, 880f, 128f);
        _uploadBtn = upBtn.GetComponent<Button>();
        _uploadBtn.onClick.AddListener(() => UploadAndSave().Forget());
        RefreshUploadBtn();
        y -= 152f;

        // ---- ステータス ----
        var sGO = Go("Status", cGO.transform);
        var sRT = sGO.GetComponent<RectTransform>();
        sRT.anchorMin = new Vector2(.04f,1f); sRT.anchorMax = new Vector2(.96f,1f);
        sRT.pivot = new Vector2(.5f,1f);
        sRT.anchoredPosition = new Vector2(0,y); sRT.sizeDelta = new Vector2(0,72f);
        _statusLabel = sGO.AddComponent<TextMeshProUGUI>();
        _statusLabel.text = "画像を選択して情報を入力し、アップロードしてください";
        _statusLabel.fontSize = 30; _statusLabel.alignment = TextAlignmentOptions.Center;
        _statusLabel.color = C_MUTED; ApplyFont(_statusLabel);
        y -= 86f;

        // ---- URL 表示 + コピーボタン ----
        SectionLabel("ダウンロード URL", cGO.transform, y); y -= 52f;
        var urlRow = Go("UrlRow", cGO.transform);
        var urRT = urlRow.GetComponent<RectTransform>();
        urRT.anchorMin = new Vector2(.04f,1f); urRT.anchorMax = new Vector2(.96f,1f);
        urRT.pivot = new Vector2(.5f,1f);
        urRT.anchoredPosition = new Vector2(0,y); urRT.sizeDelta = new Vector2(0,110f);

        var urlBG = Go("UrlBG", urlRow.transform);
        var ubRT = urlBG.GetComponent<RectTransform>();
        ubRT.anchorMin = new Vector2(0,0); ubRT.anchorMax = new Vector2(.75f,1f);
        ubRT.offsetMin = new Vector2(0,0); ubRT.offsetMax = new Vector2(-8f,0f);
        urlBG.AddComponent<Image>().color = new Color(.12f,.10f,.18f);
        var urlGO = Go("UrlText", urlBG.transform);
        var utRT = urlGO.GetComponent<RectTransform>();
        utRT.anchorMin = Vector2.zero; utRT.anchorMax = Vector2.one;
        utRT.offsetMin = new Vector2(12f,4f); utRT.offsetMax = new Vector2(-12f,-4f);
        _urlLabel = urlGO.AddComponent<TextMeshProUGUI>();
        _urlLabel.text = "（アップロード後に表示されます）";
        _urlLabel.fontSize = 24; _urlLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _urlLabel.color = C_MUTED; _urlLabel.overflowMode = TextOverflowModes.Truncate;
        ApplyFont(_urlLabel);

        var copyBtn = Go("CopyBtn", urlRow.transform);
        var cpRT = copyBtn.GetComponent<RectTransform>();
        cpRT.anchorMin = new Vector2(.77f,0f); cpRT.anchorMax = new Vector2(1f,1f);
        cpRT.offsetMin = cpRT.offsetMax = Vector2.zero;
        copyBtn.AddComponent<Image>().color = C_BTN_COPY;
        var cpBt = copyBtn.AddComponent<Button>();
        cpBt.navigation = new Navigation { mode = Navigation.Mode.None };
        cpBt.onClick.AddListener(CopyUrl);
        var cpL = Go("L", copyBtn.transform);
        var cpLRT = cpL.GetComponent<RectTransform>();
        cpLRT.anchorMin = Vector2.zero; cpLRT.anchorMax = Vector2.one;
        cpLRT.offsetMin = cpLRT.offsetMax = Vector2.zero;
        var cpT = cpL.AddComponent<TextMeshProUGUI>();
        cpT.text = "コピー"; cpT.fontSize = 36; cpT.fontStyle = FontStyles.Bold;
        cpT.alignment = TextAlignmentOptions.Center; cpT.color = C_TEXT;
        cpT.raycastTarget = false; ApplyFont(cpT);

        SelectJob(0);
        SelectCat(0);
    }

    // ================================================================
    // 選択状態
    // ================================================================
    private void SelectJob(int idx)
    {
        _selectedJob = idx;
        for (int i=0; i<_jobBtns.Length; i++)
        {
            bool sel = (i==idx);
            _jobBtns[i].GetComponent<Image>().color = sel
                ? new Color(.38f,.28f,.60f) : C_PANEL;
            var lbl = _jobBtns[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl) lbl.color = sel ? C_GOLD : C_TEXT;
        }
    }

    private void SelectCat(int idx)
    {
        _selectedCat = idx;
        for (int i=0; i<_catBtns.Length; i++)
        {
            bool sel = (i==idx);
            _catBtns[i].GetComponent<Image>().color = sel
                ? new Color(.45f,.28f,.10f) : C_PANEL;
            var lbl = _catBtns[i].GetComponentInChildren<TextMeshProUGUI>();
            if (lbl) lbl.color = sel ? C_GOLD : C_TEXT;
        }
    }

    // ================================================================
    // UI ヘルパー
    // ================================================================
    private GameObject Go(string n, Transform p)
    {
        var go = new GameObject(n);
        go.transform.SetParent(p, false);
        go.AddComponent<RectTransform>();
        return go;
    }
    private void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
    private void SectionLabel(string text, Transform p, float y)
    {
        var go = Go("SL_"+text, p);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(.04f,1f); rt.anchorMax = new Vector2(.96f,1f);
        rt.pivot = new Vector2(0f,1f);
        rt.anchoredPosition = new Vector2(0,y); rt.sizeDelta = new Vector2(0,50f);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = 32; t.fontStyle = FontStyles.Bold;
        t.color = C_MUTED; t.alignment = TextAlignmentOptions.MidlineLeft;
        ApplyFont(t);
    }
    private TMP_InputField MakeInput(Transform p, string ph, float y, ref float oy, bool multiline = false)
    {
        var go = Go("Input", p);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(.04f,1f); rt.anchorMax = new Vector2(.96f,1f);
        rt.pivot = new Vector2(.5f,1f);
        float height = multiline ? 180f : 90f;
        rt.anchoredPosition = new Vector2(0,y); rt.sizeDelta = new Vector2(0,height);
        go.AddComponent<Image>().color = C_PANEL;
        var tf = go.AddComponent<TMP_InputField>();
        tf.characterLimit = multiline ? 200 : 60;
        if (multiline)
        {
            tf.lineType = TMP_InputField.LineType.MultiLineNewline;
        }
        MakeInputInternal(tf, go, ph);
        oy -= (height + 22f); return tf;
    }











    private void MakeInputInternal(TMP_InputField tf, GameObject go, string ph)
    {
        var ta = Go("TA", go.transform);
        var tart = ta.GetComponent<RectTransform>();
        tart.anchorMin = Vector2.zero; tart.anchorMax = Vector2.one;
        tart.offsetMin = new Vector2(16f,4f); tart.offsetMax = new Vector2(-16f,-4f);
        ta.AddComponent<RectMask2D>();

        var phGO = Go("PH", ta.transform);
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = phRT.offsetMax = Vector2.zero;
        var pht = phGO.AddComponent<TextMeshProUGUI>();
        pht.text = ph; pht.fontSize = 30; pht.color = C_MUTED;
        pht.alignment = TextAlignmentOptions.MidlineLeft; ApplyFont(pht);

        var it = Go("IT", ta.transform);
        var itRT = it.GetComponent<RectTransform>();
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = itRT.offsetMax = Vector2.zero;
        var itt = it.AddComponent<TextMeshProUGUI>();
        itt.fontSize = 30; itt.color = C_TEXT;
        itt.alignment = TextAlignmentOptions.MidlineLeft; ApplyFont(itt);

        tf.textViewport = tart; tf.placeholder = pht; tf.textComponent = itt;
    }
    private GameObject FlatBtn(string n, Transform p, Color col, string lbl, float fs,
        float x, float y, float w, float h)
    {
        var go = Go(n, p);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(.5f,1f); rt.anchorMax = new Vector2(.5f,1f);
        rt.pivot = new Vector2(.5f,1f);
        rt.anchoredPosition = new Vector2(x,y); rt.sizeDelta = new Vector2(w,h);
        go.AddComponent<Image>().color = col;
        var btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(1f,1f,1f,.85f);
        cb.pressedColor = new Color(.65f,.65f,.65f,1f);
        btn.colors = cb;
        var tgo = Go("T", go.transform);
        var tRT = tgo.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        var t = tgo.AddComponent<TextMeshProUGUI>();
        t.text = lbl; t.fontSize = fs; t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center; t.color = C_TEXT;
        t.raycastTarget = false; ApplyFont(t);
        return go;
    }
}
