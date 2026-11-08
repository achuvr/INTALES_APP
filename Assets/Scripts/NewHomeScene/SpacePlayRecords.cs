using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>戦績メモ1件（端末にだけ保存。ボードゲームの写真と同じ扱い）</summary>
[Serializable]
public sealed class PlayRecord
{
    public string key;    // ゲームのキー（BoardGameMarks.KeyOf）
    public string title;  // 記録したときのゲーム名
    public string at;     // 記録した日時 "yyyy-MM-dd HH:mm"
    public string result; // SpacePlayRecords.RESULTS のどれか
    public bool hasScore;
    public int score;
    public string memo;
}

/// <summary>
/// 戦績メモ（2026-11-07 ユーザー指定）。図鑑のゲームの詳細の「戦績メモ」から開く。
/// ゲームごとに、順位（1位〜5位以下）または協力ゲームの成功・失敗と、点数（任意）・ひとことを記録する。
/// 保存はこの端末だけ（persistentDataPath/play_records.json。サーバーには送らない）。記録を押すと消せる
/// </summary>
public class SpacePlayRecords : MonoBehaviour
{
    public static readonly string[] RESULTS = { "1位", "2位", "3位", "4位", "5位以下", "協力で成功", "協力で失敗" };
    private const string FILE = "play_records.json";
    private const int MEMO_MAX = 40;

    private const string T_TITLE = "戦績メモ";
    private const string T_EMPTY = "まだ記録がありません\n遊んだら「記録する」から残しましょう";
    private const string T_ADD = "記録する";
    private const string T_CLOSE = "とじる";
    private const string T_FORM_TITLE = "戦績を記録";
    private const string T_RESULT = "結果";
    private const string T_SCORE = "点数（任意）";
    private const string T_MEMO = "ひとこと（任意）";
    private const string T_PH_SCORE = "例: 85";
    private const string T_PH_MEMO = "例: 最後の1手で逆転！";
    private const string T_SAVE = "保存する";
    private const string T_CANCEL = "キャンセル";
    private const string T_PICK_RESULT = "結果を選んでください";
    private const string T_DELETE_TITLE = "この記録を消しますか？";
    private const string T_DELETE = "消す";
    private const string T_SUMMARY_A = "遊んだ回数 ";
    private const string T_SUMMARY_B = "回";
    private const string T_WINS = "1位 ";
    private const string T_COOP = "協力で成功 ";
    private const string T_BEST = "最高点 ";
    private const string T_LOCAL = "記録はこのスマホの中だけに保存されます";

    public static string GlyphText => T_TITLE + T_EMPTY + T_ADD + T_CLOSE + T_FORM_TITLE + T_RESULT + T_SCORE + T_MEMO
                                      + T_PH_SCORE + T_PH_MEMO + T_SAVE + T_CANCEL + T_PICK_RESULT + T_DELETE_TITLE + T_DELETE
                                      + T_SUMMARY_A + T_SUMMARY_B + T_WINS + T_COOP + T_BEST + T_LOCAL
                                      + string.Concat(RESULTS) + "0123456789/:-・点 ";

    // ================================================================
    // 保存
    // ================================================================
    [Serializable]
    private sealed class Store
    {
        public List<PlayRecord> records = new List<PlayRecord>();
    }

    private static Store _store;
    private static string FilePath => Path.Combine(Application.persistentDataPath, FILE);

    private static Store Data
    {
        get
        {
            if (_store != null) return _store;
            try
            {
                _store = File.Exists(FilePath) ? JsonUtility.FromJson<Store>(File.ReadAllText(FilePath)) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayRecords] 読み込みに失敗: {ex.Message}");
            }
            if (_store == null) _store = new Store();
            if (_store.records == null) _store.records = new List<PlayRecord>();
            return _store;
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayRecords] 保存に失敗: {ex.Message}");
        }
    }

    /// <summary>そのゲームの記録（新しい順）</summary>
    public static List<PlayRecord> Of(string key)
    {
        var list = Data.records.FindAll(r => r.key == key);
        list.Sort((a, b) => string.CompareOrdinal(b.at, a.at));
        return list;
    }

    public static int CountOf(string key) => Data.records.FindAll(r => r.key == key).Count;

    // ================================================================
    // 画面
    // ================================================================
    private const float W = 920f;
    private string _key, _title;
    private RectTransform _card;
    private GameObject _form, _confirm;

    /// <summary>そのゲームの戦績メモを開く（図鑑の詳細の上に重ねる）</summary>
    public static SpacePlayRecords Open(Transform parent, BoardGameEntry g)
    {
        var root = SpaceTheme.Rect("PlayRecords", parent);
        SpaceTheme.Stretch(root);
        var p = root.gameObject.AddComponent<SpacePlayRecords>();
        p._key = BoardGameMarks.KeyOf(g);
        p._title = g.title ?? "";
        p.Build();
        return p;
    }

    public void Close() => Destroy(gameObject);

    /// <summary>戻る操作。処理したら true</summary>
    public bool HandleBack()
    {
        if (_confirm != null)
        {
            Destroy(_confirm);
            return true;
        }
        if (_form != null)
        {
            Destroy(_form);
            return true;
        }
        Close();
        return true;
    }

    private void Build()
    {
        foreach (Transform c in transform) Destroy(c.gameObject);
        var root = (RectTransform)transform;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        var dimImg = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        SpaceTheme.Clickable(dim.gameObject, dimImg, Close, false);

        var records = Of(_key);
        const float ROW_H = 120f, ROW_GAP = 12f, TOP = 250f, LIST_MAX = 820f;
        float listH = records.Count == 0 ? 160f : Mathf.Min(LIST_MAX, records.Count * (ROW_H + ROW_GAP));
        float h = TOP + listH + 60f + 112f + 48f + 40f;
        _card = SpaceTheme.Rect("Card", root, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, h));
        SpaceTheme.Glass(_card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.GOLD, 0.45f), true);

        SpaceTheme.Text(_card, "Title", T_TITLE, 44, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -72f),
            new Vector2(W - 80f, 60f), FontStyles.Bold);
        var game = SpaceTheme.Text(_card, "Game", SpaceTheme.NoParse(_title), 28, SpaceTheme.A(SpaceTheme.GOLD, 0.95f),
            new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(W - 80f, 40f), FontStyles.Bold);
        game.enableAutoSizing = true;
        game.fontSizeMin = 18f;
        game.fontSizeMax = 28f;
        SpaceTheme.Text(_card, "Summary", Summary(records), 26, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 1f),
            new Vector2(0f, -172f), new Vector2(W - 80f, 40f), FontStyles.Bold);

        // 一覧（多ければスクロール）
        var list = SpaceTheme.Rect("List", _card, new Vector2(0.5f, 1f), new Vector2(0f, -TOP), new Vector2(W - 80f, listH),
            new Vector2(0.5f, 1f));
        if (records.Count == 0)
        {
            var e = SpaceTheme.Text(list, "Empty", T_EMPTY, 28, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
            e.textWrappingMode = TextWrappingModes.Normal;
            SpaceTheme.Stretch(e.rectTransform);
        }
        else
        {
            SpaceTheme.HitArea(list);
            list.gameObject.AddComponent<RectMask2D>();
            var content = SpaceTheme.Rect("Content", list, new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(W - 80f, records.Count * (ROW_H + ROW_GAP)), new Vector2(0.5f, 1f));
            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = list;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = content.sizeDelta.y > listH + 1f;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            for (int i = 0; i < records.Count; i++)
                BuildRow(content, records[i], -(i * (ROW_H + ROW_GAP) + ROW_H / 2f), ROW_H);
        }

        SpaceTheme.Text(_card, "Local", T_LOCAL, 22, SpaceTheme.TEXT_MUTED, new Vector2(0.5f, 0f), new Vector2(0f, 186f),
            new Vector2(W - 80f, 34f), FontStyles.Bold);
        var add = Button(_card, T_ADD, new Vector2(-200f, 48f), 380f, true, OpenForm);
        var close = Button(_card, T_CLOSE, new Vector2(200f, 48f), 380f, false, Close);
        if (GetComponent<SpacePopIn>() == null) gameObject.AddComponent<SpacePopIn>().Target = _card;
    }

    private static string Summary(List<PlayRecord> records)
    {
        if (records.Count == 0) return "";
        int wins = 0, coop = 0, best = int.MinValue;
        foreach (var r in records)
        {
            if (r.result == RESULTS[0]) wins++;
            if (r.result == RESULTS[5]) coop++;
            if (r.hasScore && r.score > best) best = r.score;
        }
        string s = T_SUMMARY_A + records.Count + T_SUMMARY_B;
        if (wins > 0) s += "・" + T_WINS + wins + T_SUMMARY_B;
        if (coop > 0) s += "・" + T_COOP + coop + T_SUMMARY_B;
        if (best != int.MinValue) s += "・" + T_BEST + best + "点";
        return s;
    }

    private void BuildRow(RectTransform content, PlayRecord r, float y, float h)
    {
        var row = SpaceTheme.Rect("Row", content, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(W - 80f, h));
        var face = SpaceTheme.Rounded(row, new Color(0.07f, 0.09f, 0.24f, 0.75f), 26f, true);
        SpaceTheme.Clickable(row.gameObject, face, () => ConfirmDelete(r));

        bool good = r.result == RESULTS[0] || r.result == RESULTS[5];
        bool bad = r.result == RESULTS[6];
        var badge = SpaceTheme.Rect("Result", row, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(190f, 64f),
            new Vector2(0f, 0.5f));
        var color = good ? SpaceTheme.GOLD : bad ? SpaceTheme.TEXT_MUTED : SpaceTheme.CYAN;
        SpaceTheme.Rounded(badge, SpaceTheme.A(color, 0.16f), 32f);
        var bt = SpaceTheme.Text(badge, "Label", r.result, 28, color, FontStyles.Bold);
        SpaceTheme.Stretch(bt.rectTransform, 8f, 0f, 8f, 0f);
        bt.enableAutoSizing = true;
        bt.fontSizeMin = 18f;
        bt.fontSizeMax = 28f;

        string date = r.at ?? "";
        if (DateTime.TryParseExact(r.at, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            date = $"{t.Year}/{t.Month}/{t.Day} {t:HH:mm}";
        string top = date + (r.hasScore ? $"　{r.score}点" : "");
        SpaceTheme.Text(row, "Top", top, 28, SpaceTheme.TEXT, new Vector2(0f, 1f), new Vector2(236f, -36f),
            new Vector2(W - 80f - 260f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        if (!string.IsNullOrEmpty(r.memo))
        {
            var m = SpaceTheme.Text(row, "Memo", SpaceTheme.NoParse(SpaceTheme.Safe(r.memo)), 24, SpaceTheme.TEXT_SUB,
                new Vector2(0f, 0f), new Vector2(236f, 34f), new Vector2(W - 80f - 260f, 36f), FontStyles.Normal,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
            m.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private static RectTransform Button(RectTransform parent, string label, Vector2 pos, float w, bool primary,
        UnityEngine.Events.UnityAction onClick)
    {
        var b = SpaceTheme.Rect(label, parent, new Vector2(0.5f, 0f), pos, new Vector2(w, 112f), new Vector2(0.5f, 0f));
        var face = primary ? SpaceTheme.PrimaryFace(b) : SpaceTheme.Glass(b, 46f, new Color(0.09f, 0.12f, 0.30f, 0.63f), null, true);
        SpaceTheme.Clickable(b.gameObject, face, onClick);
        var t = SpaceTheme.Text(b, "Label", label, 36, primary ? SpaceTheme.TEXT_DARK : SpaceTheme.TEXT_SUB, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
        return b;
    }

    private void ConfirmDelete(PlayRecord r)
    {
        if (_confirm != null || _form != null) return;
        _confirm = SpaceDialog.Confirm(null, T_DELETE_TITLE, $"{r.result}　{r.at}", T_DELETE, true, () =>
        {
            _confirm = null;
            Data.records.Remove(r);
            Save();
            Build();
        }, () => _confirm = null, T_CANCEL);
    }

    // ---------------------------------------------------------------- 記録する
    private void OpenForm()
    {
        if (_form != null) return;
        var root = SpaceTheme.Rect("Form", transform);
        SpaceTheme.Stretch(root);
        _form = root.gameObject;
        var dim = SpaceTheme.Rect("Dim", root);
        SpaceTheme.Stretch(dim);
        SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);

        const float FH = 1120f;
        var card = SpaceTheme.Rect("Card", root, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(W, FH),
            new Vector2(0.5f, 1f)); // 上寄せ（キーボードに隠れにくいように）
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(SpaceTheme.GOLD, 0.45f), true);
        SpaceTheme.Text(card, "Title", T_FORM_TITLE, 42, SpaceTheme.TEXT, new Vector2(0.5f, 1f), new Vector2(0f, -70f),
            new Vector2(W - 80f, 60f), FontStyles.Bold);

        // 結果（7つから1つ）
        SpaceTheme.Text(card, "ResultLabel", T_RESULT, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(52f, -140f),
            new Vector2(400f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        string picked = null;
        var chips = new List<(string label, Image face, TextMeshProUGUI text)>();
        const float CW = 196f, CH = 92f, GAP = 14f;
        for (int i = 0; i < RESULTS.Length; i++)
        {
            string label = RESULTS[i];
            int row = i < 5 ? 0 : 1;
            int col = i < 5 ? i : i - 5;
            float rowW = i < 5 ? 4 * (CW * 0.82f + GAP) + CW * 0.82f : 2 * (CW * 1.4f) + GAP;
            float cw = i < 5 ? CW * 0.82f : CW * 1.4f;
            float x = -rowW / 2f + cw / 2f + col * (cw + GAP);
            float y = -(180f + CH / 2f + row * (CH + GAP));
            var chip = SpaceTheme.Rect("Chip" + i, card, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(cw, CH));
            var face = SpaceTheme.Rounded(chip, new Color(0.07f, 0.09f, 0.24f, 0.85f), CH / 2f, true);
            var t = SpaceTheme.Text(chip, "Label", label, 28, SpaceTheme.TEXT_SUB, FontStyles.Bold);
            SpaceTheme.Stretch(t.rectTransform, 6f, 0f, 6f, 0f);
            t.enableAutoSizing = true;
            t.fontSizeMin = 18f;
            t.fontSizeMax = 28f;
            chips.Add((label, face, t));
            SpaceTheme.Clickable(chip.gameObject, face, () =>
            {
                picked = label;
                foreach (var c in chips)
                {
                    bool on = c.label == picked;
                    c.face.color = on ? SpaceTheme.A(SpaceTheme.GOLD, 0.25f) : new Color(0.07f, 0.09f, 0.24f, 0.85f);
                    c.text.color = on ? SpaceTheme.GOLD : SpaceTheme.TEXT_SUB;
                }
            });
        }

        var score = Field(card, T_SCORE, T_PH_SCORE, -420f, TMP_InputField.ContentType.IntegerNumber, 7);
        var memo = Field(card, T_MEMO, T_PH_MEMO, -600f, TMP_InputField.ContentType.Standard, MEMO_MAX);

        Button(card, T_CANCEL, new Vector2(-200f, 48f), 380f, false, () =>
        {
            Destroy(_form);
            _form = null;
        });
        Button(card, T_SAVE, new Vector2(200f, 48f), 380f, true, () =>
        {
            if (picked == null)
            {
                SpaceTheme.Toast(T_PICK_RESULT, SpaceTheme.MainCanvas());
                return;
            }
            var rec = new PlayRecord
            {
                key = _key,
                title = _title,
                at = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                result = picked,
                memo = (memo.text ?? "").Trim(),
            };
            if (int.TryParse(score.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
            {
                rec.hasScore = true;
                rec.score = s;
            }
            Data.records.Add(rec);
            Save();
            Destroy(_form);
            _form = null;
            Build();
        });
        root.gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    /// <summary>見出しと1行の入力欄（y は見出しの位置）</summary>
    private static TMP_InputField Field(RectTransform card, string label, string placeholder, float y,
        TMP_InputField.ContentType type, int limit)
    {
        SpaceTheme.Text(card, "Label_" + label, label, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(52f, y),
            new Vector2(600f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
        var box = SpaceTheme.Rect("Field_" + label, card, new Vector2(0.5f, 1f), new Vector2(0f, y - 36f),
            new Vector2(W - 100f, 100f), new Vector2(0.5f, 1f));
        var face = SpaceTheme.Rounded(box, new Color(0.04f, 0.06f, 0.17f, 0.92f), 30f, true);
        var edgeRt = SpaceTheme.Rect("Edge", box);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, 30f);
        var area = SpaceTheme.Rect("TextArea", box);
        SpaceTheme.Stretch(area, 30f, 8f, 30f, 8f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 32, SpaceTheme.TEXT_MUTED, FontStyles.Normal,
            TextAlignmentOptions.Left);
        SpaceTheme.Stretch(ph.rectTransform);
        var text = SpaceTheme.Text(area, "Text", "", 34, SpaceTheme.TEXT, FontStyles.Normal, TextAlignmentOptions.Left);
        SpaceTheme.Stretch(text.rectTransform);
        var input = box.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 34;
        input.customCaretColor = true;
        input.caretColor = SpaceTheme.GOLD;
        input.caretWidth = 3;
        input.selectionColor = SpaceTheme.A(SpaceTheme.GOLD, 0.3f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = type;
        input.characterLimit = limit;
        input.targetGraphic = face;
        input.onSelect.AddListener(_ => edge.color = SpaceTheme.A(SpaceTheme.GOLD, 0.85f));
        input.onDeselect.AddListener(_ => edge.color = SpaceTheme.STROKE);
        return input;
    }
}
