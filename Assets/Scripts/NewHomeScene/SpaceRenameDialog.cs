using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユーザー名の変更（ホームの歯車メニューから。2026-11-09 フレンド機能をなくしたので、フレンドの画面から移した）。
/// 12文字まで・変更は1週間に1回まで（旧ホームの2つの変更画面の記録 name_edited_at・name_changed_at の新しいほうから）
/// </summary>
public static class SpaceRenameDialog
{
    public const int NAME_MAX = 12;
    private const int RENAME_COOLDOWN_DAYS = 7;

    public const string T_RENAME = "ユーザー名を変更";
    private const string T_HINT = "相席募集などで表示される名前です\n（12文字まで・変更は1週間に1回まで）";
    private const string T_WAIT_A = "ユーザー名は変更したばかりです\n（あと";
    private const string T_WAIT_B = "日で変更できます）";
    private const string T_PLACEHOLDER = "ユーザー名を入力";
    private const string T_OK = "変更する";
    private const string T_CANCEL = "キャンセル";
    private const string T_RENAMING = "変更しています…";
    private const string T_RENAMED_A = "ユーザー名を「";
    private const string T_RENAMED_B = "」に変更しました";
    private const string T_EMPTY = "ユーザー名を入力してください";
    private const string T_LONG = "ユーザー名は12文字までです";
    private const string T_EMOJI = "絵文字はユーザー名に使えません";
    private const string T_FAILED = "変更に失敗しました\n通信環境を確認して再度お試しください";

    public static string GlyphText => T_RENAME + T_HINT + T_WAIT_A + T_WAIT_B + T_PLACEHOLDER + T_OK + T_CANCEL + T_RENAMING
                                      + T_RENAMED_A + T_RENAMED_B + T_EMPTY + T_LONG + T_EMOJI + T_FAILED + "0123456789";

    /// <summary>ユーザー名を変えたとき（ホームのカードの名前を新しくする）</summary>
    public static event Action NameChanged;

    private static UserDataManager Manager => UserDataManager.instance;

    private static DateTime NextRenameUtc(UserData d)
    {
        var a = d.NameEditedAt.ToDateTime();
        var b = d.NameChangedAt.ToDateTime();
        return (a > b ? a : b).AddDays(RENAME_COOLDOWN_DAYS);
    }

    /// <summary>名前を入れ直すカード（キーボードで隠れないよう上のほうに出す）。開けなければ null</summary>
    public static GameObject Open(Canvas canvas)
    {
        if (canvas == null) canvas = SpaceTheme.MainCanvas();
        var m = Manager;
        if (m == null || string.IsNullOrEmpty(m.UID)) return null;
        var data = m.UserData;
        var next = NextRenameUtc(data);
        bool canEdit = DateTime.UtcNow >= next;

        var root = SpaceTheme.Rect("__RenameDialog", canvas.transform);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var go = root.gameObject;
        void Close()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);
        var safe = SpaceTheme.Rect("Safe", root);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();

        const float w = 920f, h = 600f;
        var card = SpaceTheme.Rect("Card", safe, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(w, h),
            new Vector2(0.5f, 1f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, null, true);
        SpaceTheme.Text(card, "Title", T_RENAME, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -84f),
            new Vector2(w - 80f, 60f), FontStyles.Bold);
        string hint = canEdit
            ? T_HINT
            : T_WAIT_A + Mathf.Max(1, Mathf.CeilToInt((float)(next - DateTime.UtcNow).TotalDays)) + T_WAIT_B;
        var hintText = SpaceTheme.Text(card, "Hint", hint, 26, canEdit ? SpaceTheme.TEXT_SUB : SpaceTheme.GOLD,
            new Vector2(0.5f, 1f), new Vector2(0f, -164f), new Vector2(w - 100f, 80f), FontStyles.Bold);
        hintText.textWrappingMode = TextWrappingModes.Normal;

        var input = NameInput(card, new Vector2(0f, -276f), new Vector2(w - 100f, 104f), data.Username ?? "");
        input.interactable = canEdit;

        float by = -(h - 104f);
        var cancel = SpaceTheme.Rect("Cancel", card, new Vector2(0.5f, 1f), new Vector2(-210f, by), new Vector2(380f, 116f));
        var cancelFace = SpaceTheme.Glass(cancel, 46f, SpaceTheme.A(new Color(0.09f, 0.12f, 0.30f), 0.63f), null, true);
        SpaceTheme.Clickable(cancel.gameObject, cancelFace, Close);
        var cancelText = SpaceTheme.Text(cancel, "Label", T_CANCEL, 36, SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(cancelText.rectTransform);

        var ok = SpaceTheme.Rect("Ok", card, new Vector2(0.5f, 1f), new Vector2(210f, by), new Vector2(380f, 116f));
        var okFace = SpaceTheme.PrimaryFace(ok);
        var okText = SpaceTheme.Text(ok, "Label", T_OK, 38, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(okText.rectTransform);
        if (canEdit) SpaceTheme.Clickable(ok.gameObject, okFace, () => Submit(canvas, input, Close));
        else ok.gameObject.AddComponent<CanvasGroup>().alpha = 0.35f;

        go.AddComponent<SpacePopIn>().Target = card;
        return go;
    }

    /// <summary>入力を確かめてから変更する（空・長すぎ・絵文字・同じ名前は書き込まない）</summary>
    private static void Submit(Canvas canvas, TMP_InputField input, Action close)
    {
        var data = Manager?.UserData;
        if (data == null || input == null) return;
        string name = (input.text ?? "").Trim();
        if (name.Length == 0)
        {
            SpaceTheme.Toast(T_EMPTY, canvas);
            return;
        }
        if (name.Length > NAME_MAX)
        {
            SpaceTheme.Toast(T_LONG, canvas);
            return;
        }
        // このフォントで表示できない絵文字（サロゲートペア・異体字セレクタ・ゼロ幅接合子）は名前に使わせない
        if (name.Any(c => char.IsSurrogate(c) || c == '️' || c == '‍' || char.IsControl(c)))
        {
            SpaceTheme.Toast(T_EMOJI, canvas);
            return;
        }
        close();
        if (name == data.Username) return;
        if (DateTime.UtcNow < NextRenameUtc(data)) return;
        RenameAsync(canvas, name).Forget();
    }

    private static async UniTaskVoid RenameAsync(Canvas canvas, string name)
    {
        var m = Manager;
        var data = m?.UserData;
        if (data == null) return;
        var busy = SpaceDialog.Busy(canvas, T_RENAMING);
        var now = Timestamp.GetCurrentTimestamp();
        bool ok = false;
        try
        {
            await FirebaseFirestore.DefaultInstance.Collection("users").Document(m.UID).UpdateAsync(
                new Dictionary<string, object>
                {
                    { "name", name },
                    { "name_edited_at", now },
                    { "name_changed_at", now },
                }).AsUniTask();
            data.Username = name;
            data.NameEditedAt = now;
            data.NameChangedAt = now;
            ok = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Rename] ユーザー名の変更に失敗: {ex.Message}");
        }
        if (busy != null) UnityEngine.Object.Destroy(busy);
        SpaceTheme.Toast(ok ? T_RENAMED_A + SpaceTheme.NoParse(SpaceTheme.SafeNow(name)) + T_RENAMED_B : T_FAILED, canvas);
        if (ok) NameChanged?.Invoke();
    }

    /// <summary>名前の入力欄（ガラスの枠。12文字まで。押すと全体が選ばれて、すぐ打ち直せる）</summary>
    private static TMP_InputField NameInput(RectTransform parent, Vector2 topCenter, Vector2 size, string value)
    {
        var bar = SpaceTheme.Rect("Input", parent, new Vector2(0.5f, 1f), topCenter, size, new Vector2(0.5f, 1f));
        var face = SpaceTheme.Glass(bar, 32f, new Color(0.03f, 0.04f, 0.12f, 0.9f), SpaceTheme.A(SpaceTheme.CYAN, 0.45f), true);
        var area = SpaceTheme.Rect("TextArea", bar);
        SpaceTheme.Stretch(area, 32f, 8f, 32f, 8f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = SpaceTheme.Text(area, "Placeholder", T_PLACEHOLDER, 40, SpaceTheme.TEXT_MUTED, FontStyles.Bold,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(ph.rectTransform);
        var tx = SpaceTheme.Text(area, "Text", "", 40, SpaceTheme.TEXT, FontStyles.Bold, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(tx.rectTransform);

        var input = bar.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = face;
        input.textViewport = area;
        input.textComponent = tx;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 40;
        input.richText = false;
        input.characterLimit = NAME_MAX;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onFocusSelectAll = true;
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.CYAN;
        input.selectionColor = SpaceTheme.A(SpaceTheme.CYAN, 0.35f);
        input.text = SpaceTheme.Safe(value);
        return input;
    }
}
