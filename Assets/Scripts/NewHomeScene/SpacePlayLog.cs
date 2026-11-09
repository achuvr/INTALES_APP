using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ================================================================
// 遊んだ記録（2026-11-09 ユーザー指定）
//   図鑑のゲームごとに「日付・一緒に遊んだプレイヤー・メモ・写真」を1件ずつ残す。
//   プレイヤーは「プレイヤー管理」で一度登録した人から選ぶ。
//   写真と同じく、記録もプレイヤーの名前もこの端末の中だけに保存する（サーバーには送らない）。
//     記録: persistentDataPath/play_logs.json
//     プレイヤー: persistentDataPath/play_members.json
//     写真: BoardGamePhotoStore のゲームのフォルダ（記録にはファイル名だけを書く）
// ================================================================

[Serializable]
public sealed class PlayLogEntry
{
    public string id;
    public string key;                                // ゲームのキー（BoardGameMarks.KeyOf）
    public string date;                               // 遊んだ日 "yyyy-MM-dd"
    public List<string> players = new List<string>(); // PlayMember.id
    public string memo = "";
    public List<string> photos = new List<string>();  // 写真のファイル名（ゲームのフォルダの中）
    /// <summary>勝った人（PlayMember.id。自分は ME。同点なら複数）。協力ゲームのときは空</summary>
    public List<string> winners = new List<string>();
    /// <summary>協力ゲームの結果（COOP_SUCCESS / COOP_FAIL。勝ち負けのゲームは空）</summary>
    public string coop = "";

    public const string ME = "me";
    public const string COOP_SUCCESS = "success";
    public const string COOP_FAIL = "fail";

    public DateTime Date =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : DateTime.Today;
}

[Serializable]
public sealed class PlayMember
{
    public string id;
    public string name;
}

/// <summary>遊んだ記録とプレイヤーの保存（端末の中だけ）</summary>
public static class PlayLogStore
{
    public const int NAME_MAX = 12;
    public const int MEMO_MAX = 200;

    [Serializable] private sealed class LogFile { public List<PlayLogEntry> logs = new List<PlayLogEntry>(); }
    [Serializable] private sealed class MemberFile { public List<PlayMember> members = new List<PlayMember>(); }

    private static LogFile _logs;
    private static MemberFile _members;
    private static string LogPath => Path.Combine(Application.persistentDataPath, "play_logs.json");
    private static string MemberPath => Path.Combine(Application.persistentDataPath, "play_members.json");

    private static T Load<T>(string path) where T : class, new()
    {
        try
        {
            if (File.Exists(path)) return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? new T();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayLog] 読み込みに失敗 ({path}): {ex.Message}");
        }
        return new T();
    }

    private static void Save(string path, object data)
    {
        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(data));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayLog] 保存に失敗 ({path}): {ex.Message}");
        }
    }

    private static LogFile Logs => _logs ?? (_logs = Load<LogFile>(LogPath));
    private static MemberFile MembersFile => _members ?? (_members = Load<MemberFile>(MemberPath));

    public static string NewId() => DateTime.Now.ToString("yyyyMMddHHmmssfff") + UnityEngine.Random.Range(100, 999);

    // ---- 記録 ----
    /// <summary>遊んだ日の新しい順（同じ日なら後から残したものが先）</summary>
    private static int CompareNewest(PlayLogEntry a, PlayLogEntry b)
    {
        int c = string.CompareOrdinal(b.date, a.date);
        return c != 0 ? c : string.CompareOrdinal(b.id, a.id);
    }

    /// <summary>そのゲームの記録（遊んだ日の新しい順）</summary>
    public static List<PlayLogEntry> LogsOf(string key)
    {
        var list = Logs.logs.FindAll(l => l.key == key);
        list.Sort(CompareNewest);
        return list;
    }

    /// <summary>すべてのゲームの記録（遊んだ日の新しい順。図鑑の「記録」のカレンダー用）</summary>
    public static List<PlayLogEntry> All()
    {
        var list = new List<PlayLogEntry>(Logs.logs);
        list.Sort(CompareNewest);
        return list;
    }

    /// <summary>そのゲームの記録が使っている写真のファイル名</summary>
    public static HashSet<string> PhotoNamesOf(string key)
    {
        var set = new HashSet<string>();
        foreach (var l in Logs.logs)
            if (l.key == key && l.photos != null)
                foreach (var p in l.photos) set.Add(p);
        return set;
    }

    public static void Put(PlayLogEntry log)
    {
        int i = Logs.logs.FindIndex(l => l.id == log.id);
        if (i >= 0) Logs.logs[i] = log;
        else Logs.logs.Add(log);
        Save(LogPath, Logs);
    }

    public static void Remove(PlayLogEntry log)
    {
        Logs.logs.RemoveAll(l => l.id == log.id);
        Save(LogPath, Logs);
    }

    // ---- プレイヤー ----
    public static List<PlayMember> Members => MembersFile.members;

    public static PlayMember FindMember(string id) => MembersFile.members.Find(m => m.id == id);

    public static PlayMember AddMember(string name)
    {
        var m = new PlayMember { id = NewId(), name = name };
        MembersFile.members.Add(m);
        Save(MemberPath, MembersFile);
        return m;
    }

    /// <summary>プレイヤーを消す（記録に残っている名前は「削除したプレイヤー」になる）</summary>
    public static void RemoveMember(PlayMember m)
    {
        MembersFile.members.RemoveAll(x => x.id == m.id);
        Save(MemberPath, MembersFile);
    }

    public static string NameOf(string id)
    {
        var m = FindMember(id);
        return m != null ? m.name : null;
    }
}

/// <summary>
/// 遊んだ記録の画面（図鑑のゲームの詳細の「遊んだ記録」から）。
///   ・入力（新しく残す／直す）: 日付・一緒に遊んだプレイヤー・メモ・写真（カメラ・アルバム）
///   ・見る: 1件の中身（写真を押すと大きく。直す・消す）
///   ・プレイヤー管理: 名前の登録と削除
/// </summary>
public class SpacePlayLog : MonoBehaviour
{
    private const float W = 940f;
    private static readonly Color ACCENT = SpaceTheme.CYAN;
    private static readonly Color FIELD_FILL = new Color(0.04f, 0.06f, 0.17f, 0.92f);
    private static readonly Color CHIP_FILL = new Color(0.07f, 0.09f, 0.24f, 0.85f);

    private const string T_NEW = "遊んだ記録を残す";
    private const string T_EDIT = "遊んだ記録を編集";
    private const string T_DATE = "遊んだ日";
    private const string T_TODAY = "今日";
    private const string T_PLAYERS = "一緒に遊んだ人";
    private const string T_NO_MEMBERS = "まだ登録していません。「プレイヤー管理」から登録すると選べます";
    private const string T_MANAGE = "プレイヤー管理";
    private const string T_RESULT = "結果";
    private const string T_RESULT_HINT = "勝った人を選んでください（同点なら複数）。協力ゲームは成功・失敗";
    private const string T_ME = "自分";
    private const string T_COOP_SUCCESS = "協力で成功";
    private const string T_COOP_FAIL = "協力で失敗";
    private const string T_WINNER = "の勝ち";
    private const string T_MEMO = "メモ";
    private const string T_PH_MEMO = "例: 最後の1手で逆転！";
    private const string T_PHOTOS = "写真";
    private const string T_CAMERA = "撮る";
    private const string T_ALBUM = "アルバム";
    private const string T_SAVE = "保存する";
    private const string T_CANCEL = "キャンセル";
    private const string T_CLOSE = "とじる";
    private const string T_DELETE = "消す";
    private const string T_EDIT_BTN = "編集";
    private const string T_DELETE_TITLE = "この記録を消しますか？";
    private const string T_DELETE_BODY = "記録の写真も一緒に消えます\nこの操作は元に戻せません";
    private const string T_REMOVED_MEMBER = "（削除したプレイヤー）";
    private const string T_NONE = "なし";
    private const string T_LOCAL = "記録とプレイヤーの名前は、このスマホの中だけに保存されます";
    private const string T_MANAGE_HINT = "一緒に遊んだ人の名前を登録しておくと、記録のときに選べます";
    private const string T_PH_NAME = "名前を入力（12文字まで）";
    private const string T_ADD = "追加";
    private const string T_MEMBER_DELETE_TITLE = "このプレイヤーを消しますか？";
    private const string T_MEMBER_DELETE_BODY = "これまでの記録からは名前が消えます";
    private const string T_NAME_EMPTY = "名前を入力してください";
    private const string T_NAME_DUP = "同じ名前のプレイヤーがいます";
    private const string T_PHOTO_FAILED = "写真を読み込めませんでした";

    public static string GlyphText => T_NEW + T_EDIT + T_DATE + T_TODAY + T_PLAYERS + T_NO_MEMBERS + T_MANAGE + T_MEMO
                                      + T_PH_MEMO + T_PHOTOS + T_CAMERA + T_ALBUM + T_SAVE + T_CANCEL + T_CLOSE + T_DELETE
                                      + T_EDIT_BTN + T_DELETE_TITLE + T_DELETE_BODY + T_REMOVED_MEMBER + T_NONE + T_LOCAL
                                      + T_MANAGE_HINT + T_PH_NAME + T_ADD + T_MEMBER_DELETE_TITLE + T_MEMBER_DELETE_BODY
                                      + T_NAME_EMPTY + T_NAME_DUP + T_PHOTO_FAILED + T_RESULT + T_RESULT_HINT + T_ME + T_COOP_SUCCESS + T_COOP_FAIL
                                      + T_WINNER + "年月日（）◀▶✕人曜0123456789"
                                      + StoreCalendar.GlyphText;

    private BoardGameEntry _game;
    private string _key;
    private Action _onChanged;
    private readonly List<Texture2D> _textures = new List<Texture2D>();
    private GameObject _sub; // 上に重ねた画面（プレイヤー管理・確認）

    // 入力中の内容
    private PlayLogEntry _editing;   // 直している記録（新しく残すときは null）
    private DateTime _date;
    private readonly List<string> _selected = new List<string>();
    private readonly List<string> _winners = new List<string>();
    private string _coop = "";
    private readonly List<string> _photos = new List<string>();      // いま記録に入っている写真
    private readonly List<string> _added = new List<string>();       // この入力で新しく加えた写真（やめたら消す）
    private readonly List<string> _removed = new List<string>();     // この入力で外した写真（保存したら消す）
    private TMP_InputField _memo;
    private string _memoText = "";
    private RectTransform _card;

    // ================================================================
    // 開く
    // ================================================================
    /// <summary>新しく残す（editing=null）／直す</summary>
    public static SpacePlayLog OpenForm(Transform parent, BoardGameEntry g, PlayLogEntry editing, Action onChanged)
    {
        var p = Create(parent, g, onChanged);
        p._editing = editing;
        p._date = editing != null ? editing.Date : DateTime.Today;
        if (editing != null)
        {
            p._selected.AddRange(editing.players);
            if (editing.winners != null) p._winners.AddRange(editing.winners);
            p._coop = editing.coop ?? "";
            p._photos.AddRange(editing.photos);
            p._memoText = editing.memo ?? "";
        }
        p.BuildForm();
        return p;
    }

    /// <summary>1件の記録を見る</summary>
    public static SpacePlayLog OpenView(Transform parent, BoardGameEntry g, PlayLogEntry log, Action onChanged)
    {
        var p = Create(parent, g, onChanged);
        p.BuildView(log);
        return p;
    }

    private static SpacePlayLog Create(Transform parent, BoardGameEntry g, Action onChanged)
    {
        var root = SpaceTheme.Rect("__PlayLog", parent);
        SpaceTheme.Stretch(root);
        root.SetAsLastSibling();
        var p = root.gameObject.AddComponent<SpacePlayLog>();
        p._game = g;
        p._key = BoardGameMarks.KeyOf(g);
        p._onChanged = onChanged;
        return p;
    }

    private void OnDestroy()
    {
        foreach (var t in _textures) if (t != null) Destroy(t);
        _textures.Clear();
    }

    /// <summary>戻る操作。処理したら true</summary>
    public bool HandleBack()
    {
        if (_sub != null)
        {
            Destroy(_sub);
            _sub = null;
            if (_editing != null || _card != null) RebuildForm();
            return true;
        }
        CancelForm();
        return true;
    }

    private void Clear()
    {
        foreach (Transform c in transform) Destroy(c.gameObject);
        foreach (var t in _textures) if (t != null) Destroy(t);
        _textures.Clear();
    }

    private RectTransform Backdrop(Transform parent, Action onTap)
    {
        var dim = SpaceTheme.Rect("Dim", parent);
        SpaceTheme.Stretch(dim);
        var img = SpaceTheme.Img(dim, null, SpaceTheme.DIM, true);
        if (onTap != null) SpaceTheme.Clickable(dim.gameObject, img, () => onTap(), false);
        var safe = SpaceTheme.Rect("Safe", parent);
        SpaceTheme.Stretch(safe);
        safe.gameObject.AddComponent<SpaceSafeArea>();
        return safe;
    }

    // ================================================================
    // 入力
    // ================================================================
    private bool _formBuilt;

    private void RebuildForm()
    {
        if (_memo != null) _memoText = _memo.text;
        BuildForm();
    }

    private void BuildForm()
    {
        if (_memo != null) _memoText = _memo.text;
        Clear();
        _memo = null;
        var safe = Backdrop(transform, null); // 書いている途中で消えないよう、まわりのタップでは閉じない

        // 一緒に遊んだ人のチップを先に並べて、高さを決める
        var members = PlayLogStore.Members;
        var chipRows = LayoutChips(members, W - 100f);
        float chipsH = members.Count == 0 ? 60f : chipRows * 96f;
        var resultLabels = ResultLabels();
        float resultH = LayoutLabels(resultLabels, W - 100f) * 96f;
        float h = 120f + 50f + 70f + 110f + 40f + 70f + chipsH + 90f + 50f + 44f + resultH + 30f + 70f + 180f + 70f + 170f + 40f
                  + 50f + 112f + 50f;
        _card = SpaceTheme.Rect("Card", safe, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(W, h),
            new Vector2(0.5f, 1f));
        SpaceTheme.Glass(_card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.4f), true);

        float y = 70f;
        Label(_card, _editing == null ? T_NEW : T_EDIT, 44, SpaceTheme.TEXT, y, true);
        y += 56f;
        var game = Label(_card, SpaceTheme.NoParse(SpaceTheme.Safe(_game.title ?? "")), 28, ACCENT, y, true);
        game.enableAutoSizing = true;
        game.fontSizeMin = 18f;
        game.fontSizeMax = 28f;
        y += 70f;

        // ---- 遊んだ日 ----
        SectionLabel(_card, T_DATE, y);
        y += 50f;
        var dateRow = SpaceTheme.Rect("DateRow", _card, new Vector2(0.5f, 1f), new Vector2(0f, -y - 50f), new Vector2(W - 100f, 100f));
        SpaceTheme.Rounded(dateRow, FIELD_FILL, 30f);
        var dateText = SpaceTheme.Text(dateRow, "Date", DateLabel(_date), 36, SpaceTheme.TEXT, FontStyles.Bold);
        SpaceTheme.Stretch(dateText.rectTransform, 130f, 0f, 290f, 0f);
        SmallButton(dateRow, "◀", new Vector2(0f, 0.5f), new Vector2(64f, 0f), 96f, () =>
        {
            _date = _date.AddDays(-1);
            dateText.text = DateLabel(_date);
        });
        SmallButton(dateRow, "▶", new Vector2(1f, 0.5f), new Vector2(-230f, 0f), 96f, () =>
        {
            if (_date.Date >= DateTime.Today) return; // 先の日は選べない
            _date = _date.AddDays(1);
            dateText.text = DateLabel(_date);
        });
        SmallButton(dateRow, T_TODAY, new Vector2(1f, 0.5f), new Vector2(-84f, 0f), 140f, () =>
        {
            _date = DateTime.Today;
            dateText.text = DateLabel(_date);
        });
        y += 110f + 60f; // 遊んだ日と一緒に遊んだ人の間（2026-11-09 ユーザー指定で広げた）

        // ---- 一緒に遊んだ人 ----
        SectionLabel(_card, T_PLAYERS, y);
        var manage = SpaceTheme.Rect("Manage", _card, new Vector2(1f, 1f), new Vector2(-50f, -y - 4f), new Vector2(260f, 64f),
            new Vector2(1f, 0.5f));
        var manageFace = SpaceTheme.Glass(manage, 32f, SpaceTheme.A(ACCENT, 0.12f), SpaceTheme.A(ACCENT, 0.6f), true);
        SpaceTheme.Clickable(manage.gameObject, manageFace, OpenManager);
        var ml = SpaceTheme.Text(manage, "Label", T_MANAGE, 26, ACCENT, FontStyles.Bold);
        SpaceTheme.Stretch(ml.rectTransform);
        y += 50f;
        if (members.Count == 0)
        {
            var hint = Label(_card, T_NO_MEMBERS, 24, SpaceTheme.TEXT_MUTED, y + 20f, true);
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 16f;
            hint.fontSizeMax = 24f;
        }
        else BuildChips(_card, members, y);
        y += chipsH + 30f;

        // ---- 結果（勝った人・協力ゲームの成功／失敗。2026-11-09 ユーザー指定） ----
        SectionLabel(_card, T_RESULT, y);
        y += 44f;
        var rh = Label(_card, T_RESULT_HINT, 22, SpaceTheme.TEXT_MUTED, y, true);
        rh.enableAutoSizing = true;
        rh.fontSizeMin = 16f;
        rh.fontSizeMax = 22f;
        y += 44f;
        BuildResultChips(_card, resultLabels, y);
        y += resultH + 30f;

        // ---- メモ ----
        SectionLabel(_card, T_MEMO, y);
        y += 50f;
        _memo = Input(_card, T_PH_MEMO, y, 160f, PlayLogStore.MEMO_MAX, true);
        _memo.text = _memoText;
        y += 180f + 20f;

        // ---- 写真 ----
        SectionLabel(_card, T_PHOTOS, y);
        y += 50f;
        BuildPhotoPicker(_card, y);
        y += 170f + 20f;

        var local = Label(_card, T_LOCAL, 22, SpaceTheme.TEXT_MUTED, y, true);
        local.enableAutoSizing = true;
        local.fontSizeMin = 16f;
        local.fontSizeMax = 22f;

        Button(_card, T_CANCEL, new Vector2(-220f, 50f), 400f, false, CancelForm);
        Button(_card, T_SAVE, new Vector2(220f, 50f), 400f, true, SaveForm);
        if (!_formBuilt)
        {
            _formBuilt = true;
            gameObject.AddComponent<SpacePopIn>().Target = _card;
        }
    }

    private static string DateLabel(DateTime d) => $"{d.Year}年" + StoreCalendar.MonthDayW(d);

    /// <summary>チップを横に詰めて並べたときの行数</summary>
    private static int LayoutChips(List<PlayMember> members, float width)
    {
        int rows = members.Count > 0 ? 1 : 0;
        float x = 0f;
        foreach (var m in members)
        {
            float w = ChipWidth(m.name);
            if (x > 0f && x + w > width)
            {
                rows++;
                x = 0f;
            }
            x += w + 14f;
        }
        return rows;
    }

    private static float ChipWidth(string name) => Mathf.Min(420f, 60f + (name?.Length ?? 0) * 32f);

    private void BuildChips(RectTransform card, List<PlayMember> members, float top)
    {
        float width = W - 100f, x = 0f, y = 0f;
        foreach (var m in members)
        {
            float w = ChipWidth(m.name);
            if (x > 0f && x + w > width)
            {
                x = 0f;
                y += 96f;
            }
            string id = m.id;
            bool on = _selected.Contains(id);
            var chip = SpaceTheme.Rect("Chip", card, new Vector2(0f, 1f), new Vector2(50f + x, -(top + y)), new Vector2(w, 82f),
                new Vector2(0f, 1f));
            var face = SpaceTheme.Rounded(chip, on ? SpaceTheme.A(ACCENT, 0.25f) : CHIP_FILL, 41f, true);
            var t = SpaceTheme.Text(chip, "Label", SpaceTheme.NoParse(SpaceTheme.Safe(m.name)), 30,
                on ? ACCENT : SpaceTheme.TEXT_SUB, FontStyles.Bold);
            SpaceTheme.Stretch(t.rectTransform, 12f, 0f, 12f, 0f);
            t.enableAutoSizing = true;
            t.fontSizeMin = 18f;
            t.fontSizeMax = 30f;
            SpaceTheme.Clickable(chip.gameObject, face, () =>
            {
                if (_selected.Contains(id))
                {
                    _selected.Remove(id);
                    _winners.Remove(id);
                }
                else _selected.Add(id);
                RebuildForm(); // 結果の欄（勝った人の候補）も変わる
            });
            x += w + 14f;
        }
    }

    // ---- 結果 ----
    /// <summary>結果の欄のチップ: 自分・選んだ一緒に遊んだ人（勝った人の候補）と、協力で成功・失敗</summary>
    private List<(string id, string label, bool coop)> ResultLabels()
    {
        var list = new List<(string, string, bool)> { (PlayLogEntry.ME, T_ME, false) };
        foreach (var m in PlayLogStore.Members)
            if (_selected.Contains(m.id)) list.Add((m.id, m.name, false));
        list.Add((PlayLogEntry.COOP_SUCCESS, T_COOP_SUCCESS, true));
        list.Add((PlayLogEntry.COOP_FAIL, T_COOP_FAIL, true));
        return list;
    }

    private static int LayoutLabels(List<(string id, string label, bool coop)> labels, float width)
    {
        int rows = labels.Count > 0 ? 1 : 0;
        float x = 0f;
        foreach (var l in labels)
        {
            float w = ChipWidth(l.label);
            if (x > 0f && x + w > width)
            {
                rows++;
                x = 0f;
            }
            x += w + 14f;
        }
        return rows;
    }

    private void BuildResultChips(RectTransform card, List<(string id, string label, bool coop)> labels, float top)
    {
        float width = W - 100f, x = 0f, y = 0f;
        foreach (var l in labels)
        {
            float w = ChipWidth(l.label);
            if (x > 0f && x + w > width)
            {
                x = 0f;
                y += 96f;
            }
            var item = l;
            bool on = item.coop ? _coop == item.id : _winners.Contains(item.id);
            // 勝った人は金色、協力の成功は水色、失敗は灰色で光らせる
            Color c = !item.coop ? SpaceTheme.GOLD : item.id == PlayLogEntry.COOP_SUCCESS ? SpaceTheme.MINT : SpaceTheme.TEXT_MUTED;
            var chip = SpaceTheme.Rect("Result", card, new Vector2(0f, 1f), new Vector2(50f + x, -(top + y)), new Vector2(w, 82f),
                new Vector2(0f, 1f));
            var face = SpaceTheme.Rounded(chip, on ? SpaceTheme.A(c, 0.25f) : CHIP_FILL, 41f, true);
            var t = SpaceTheme.Text(chip, "Label", SpaceTheme.NoParse(SpaceTheme.Safe(item.label)), 30,
                on ? c : SpaceTheme.TEXT_SUB, FontStyles.Bold);
            SpaceTheme.Stretch(t.rectTransform, 12f, 0f, 12f, 0f);
            t.enableAutoSizing = true;
            t.fontSizeMin = 18f;
            t.fontSizeMax = 30f;
            SpaceTheme.Clickable(chip.gameObject, face, () =>
            {
                if (item.coop)
                {
                    // 協力ゲーム: 成功か失敗のどちらか。選ぶと勝った人は外す（もう一度押すと外れる）
                    _coop = _coop == item.id ? "" : item.id;
                    if (_coop.Length > 0) _winners.Clear();
                }
                else
                {
                    if (_winners.Contains(item.id)) _winners.Remove(item.id);
                    else _winners.Add(item.id);
                    if (_winners.Count > 0) _coop = "";
                }
                RebuildForm();
            });
            x += w + 14f;
        }
    }

    /// <summary>一緒に遊んだ人の名前を「、」でつないだもの（消したプレイヤーは「（削除したプレイヤー）」）。いなければ null</summary>
    internal static string PlayersText(PlayLogEntry log)
    {
        if (log.players == null || log.players.Count == 0) return null;
        var names = new List<string>();
        foreach (var id in log.players) names.Add(PlayLogStore.NameOf(id) ?? T_REMOVED_MEMBER);
        return string.Join("、", names);
    }

    /// <summary>記録の結果の文: 「○○、自分の勝ち」「協力で成功」。なければ null（カレンダーの一覧でも使う）</summary>
    internal static string ResultText(PlayLogEntry log)
    {
        if (log.coop == PlayLogEntry.COOP_SUCCESS) return T_COOP_SUCCESS;
        if (log.coop == PlayLogEntry.COOP_FAIL) return T_COOP_FAIL;
        if (log.winners == null || log.winners.Count == 0) return null;
        var names = new List<string>();
        foreach (var id in log.winners)
            names.Add(id == PlayLogEntry.ME ? T_ME : PlayLogStore.NameOf(id) ?? T_REMOVED_MEMBER);
        return string.Join("、", names) + T_WINNER;
    }

    // ---- 写真 ----
    private RectTransform _photoContent;

    private void BuildPhotoPicker(RectTransform card, float top)
    {
        var row = SpaceTheme.Rect("Photos", card, new Vector2(0.5f, 1f), new Vector2(0f, -top - 75f), new Vector2(W - 100f, 150f));
        SpaceTheme.HitArea(row);
        row.gameObject.AddComponent<RectMask2D>();
        var sr = row.gameObject.AddComponent<ScrollRect>();
        sr.vertical = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.viewport = row;
        var content = SpaceTheme.Rect("Content", row);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 14f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = content;
        _photoContent = content;
        RefreshPhotoPicker();
    }

    private void RefreshPhotoPicker()
    {
        if (_photoContent == null) return;
        foreach (Transform c in _photoContent) Destroy(c.gameObject);
        PickerButton(_photoContent, SpaceSprites.Camera, T_CAMERA, TakePhoto);
        PickerButton(_photoContent, SpaceSprites.PlanetIcon, T_ALBUM, PickPhotos);
        foreach (var name in _photos)
        {
            string n = name;
            var cell = PhotoCell(_photoContent, BoardGamePhotoStore.PathOf(_game, n), 150f, null);
            var x = SpaceTheme.Rect("Remove", cell, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(44f, 44f));
            var xf = SpaceTheme.Img(x, SpaceSprites.Circle, new Color(0.02f, 0.03f, 0.1f, 0.85f), true);
            SpaceTheme.Bar(x, Vector2.zero, 20f, 4f, 45f, SpaceTheme.TEXT);
            SpaceTheme.Bar(x, Vector2.zero, 20f, 4f, -45f, SpaceTheme.TEXT);
            SpaceTheme.Clickable(x.gameObject, xf, () =>
            {
                _photos.Remove(n);
                if (_added.Remove(n)) BoardGamePhotoStore.Delete(BoardGamePhotoStore.PathOf(_game, n));
                else _removed.Add(n);
                RefreshPhotoPicker();
            });
        }
    }

    private static void PickerButton(RectTransform parent, Sprite icon, string label, Action onClick)
    {
        var b = SpaceTheme.Rect(label, parent);
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 150f;
        var face = SpaceTheme.Glass(b, 30f, CHIP_FILL, SpaceTheme.A(ACCENT, 0.45f), true);
        SpaceTheme.Clickable(b.gameObject, face, () => onClick());
        var i = SpaceTheme.Rect("Icon", b, SpaceTheme.CENTER, new Vector2(0f, 16f), new Vector2(60f, 60f));
        SpaceTheme.Img(i, icon, ACCENT);
        SpaceTheme.Text(b, "Label", label, 24, SpaceTheme.TEXT_SUB, new Vector2(0.5f, 0f), new Vector2(0f, 28f),
            new Vector2(140f, 34f), FontStyles.Bold);
    }

    private void AddTexture(Texture2D tex)
    {
        if (tex == null)
        {
            SpaceTheme.Toast(T_PHOTO_FAILED, SpaceTheme.MainCanvas());
            return;
        }
        string path = BoardGamePhotoStore.AddPhoto(_game, tex);
        Destroy(tex);
        if (string.IsNullOrEmpty(path)) return;
        string name = Path.GetFileName(path);
        _photos.Add(name);
        _added.Add(name);
    }

    private void TakePhoto()
    {
#if UNITY_EDITOR
        string picked = UnityEditor.EditorUtility.OpenFilePanel("テスト用の写真を選択", "", "jpg,jpeg,png");
        if (string.IsNullOrEmpty(picked)) return;
        AddTexture(BoardGamePhotoStore.LoadTexture(picked));
        RefreshPhotoPicker();
#else
        NativeCamera.TakePicture(path =>
        {
            if (string.IsNullOrEmpty(path) || this == null) return;
            AddTexture(NativeCamera.LoadImageAtPath(path, 1280, markTextureNonReadable: false));
            RefreshPhotoPicker();
        }, maxSize: 1280);
#endif
    }

    private void PickPhotos()
    {
#if UNITY_EDITOR
        TakePhoto();
#else
        NativeGallery.GetImagesFromGallery(paths =>
        {
            if (paths == null || this == null) return;
            foreach (var p in paths) AddTexture(NativeGallery.LoadImageAtPath(p, 1280, markTextureNonReadable: false));
            RefreshPhotoPicker();
        }, T_PHOTOS);
#endif
    }

    // ---- 保存・やめる ----
    private void SaveForm()
    {
        var log = _editing ?? new PlayLogEntry { id = PlayLogStore.NewId(), key = _key };
        log.date = _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        log.players = new List<string>(_selected);
        log.winners = new List<string>(_winners);
        log.coop = _coop ?? "";
        log.memo = (_memo != null ? _memo.text : _memoText ?? "").Trim();
        log.photos = new List<string>(_photos);
        PlayLogStore.Put(log);
        foreach (var n in _removed) BoardGamePhotoStore.Delete(BoardGamePhotoStore.PathOf(_game, n));
        _added.Clear();
        _removed.Clear();
        _onChanged?.Invoke();
        Destroy(gameObject);
    }

    private void CancelForm()
    {
        foreach (var n in _added) BoardGamePhotoStore.Delete(BoardGamePhotoStore.PathOf(_game, n));
        _added.Clear();
        if (_added.Count == 0 && _removed.Count > 0) _removed.Clear();
        Destroy(gameObject);
    }

    // ================================================================
    // 1件の記録を見る
    // ================================================================
    private void BuildView(PlayLogEntry log)
    {
        Clear();
        var safe = Backdrop(transform, () => Destroy(gameObject));
        // 写真が消えていたら記録から外す（写真を大きくした画面で消したとき）
        log.photos.RemoveAll(n => !File.Exists(BoardGamePhotoStore.PathOf(_game, n)));

        string players = PlayersText(log) ?? T_NONE;
        string memo = string.IsNullOrEmpty(log.memo) ? T_NONE : log.memo;

        string result = ResultText(log);
        float h = 120f + 56f + 70f + 50f + 130f + (result != null ? 50f + 70f : 0f) + 50f + 230f
                  + (log.photos.Count > 0 ? 50f + 200f : 0f) + 50f + 112f + 50f;
        var card = SpaceTheme.Rect("Card", safe, SpaceTheme.CENTER, Vector2.zero, new Vector2(W, h));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.4f), true);

        float y = 70f;
        Label(card, DateLabel(log.Date), 44, SpaceTheme.TEXT, y, true);
        y += 56f;
        var game = Label(card, SpaceTheme.NoParse(SpaceTheme.Safe(_game.title ?? "")), 28, ACCENT, y, true);
        game.enableAutoSizing = true;
        game.fontSizeMin = 18f;
        game.fontSizeMax = 28f;
        y += 70f;

        SectionLabel(card, T_PLAYERS, y);
        y += 50f;
        var p = SpaceTheme.Text(card, "Players", SpaceTheme.NoParse(SpaceTheme.Safe(players)), 30, SpaceTheme.TEXT,
            new Vector2(0.5f, 1f), new Vector2(0f, -y - 55f), new Vector2(W - 100f, 110f), FontStyles.Bold,
            TextAlignmentOptions.TopLeft);
        p.textWrappingMode = TextWrappingModes.Normal;
        p.overflowMode = TextOverflowModes.Ellipsis;
        y += 130f;

        if (result != null)
        {
            SectionLabel(card, T_RESULT, y);
            y += 50f;
            Color rc = log.coop == PlayLogEntry.COOP_FAIL ? SpaceTheme.TEXT_MUTED
                : log.coop == PlayLogEntry.COOP_SUCCESS ? SpaceTheme.MINT : SpaceTheme.GOLD;
            var rt = SpaceTheme.Text(card, "Result", SpaceTheme.NoParse(SpaceTheme.Safe(result)), 32, rc,
                new Vector2(0.5f, 1f), new Vector2(0f, -y - 25f), new Vector2(W - 100f, 50f), FontStyles.Bold,
                TextAlignmentOptions.Left);
            rt.enableAutoSizing = true;
            rt.fontSizeMin = 20f;
            rt.fontSizeMax = 32f;
            y += 70f;
        }

        SectionLabel(card, T_MEMO, y);
        y += 50f;
        var m = SpaceTheme.Text(card, "Memo", SpaceTheme.NoParse(SpaceTheme.Safe(memo)), 28,
            string.IsNullOrEmpty(log.memo) ? SpaceTheme.TEXT_MUTED : SpaceTheme.TEXT_SUB,
            new Vector2(0.5f, 1f), new Vector2(0f, -y - 105f), new Vector2(W - 100f, 210f), FontStyles.Normal,
            TextAlignmentOptions.TopLeft);
        m.textWrappingMode = TextWrappingModes.Normal;
        m.overflowMode = TextOverflowModes.Ellipsis;
        y += 230f;

        if (log.photos.Count > 0)
        {
            SectionLabel(card, T_PHOTOS, y);
            y += 50f;
            var row = SpaceTheme.Rect("Photos", card, new Vector2(0.5f, 1f), new Vector2(0f, -y - 95f), new Vector2(W - 100f, 190f));
            SpaceTheme.HitArea(row);
            row.gameObject.AddComponent<RectMask2D>();
            var sr = row.gameObject.AddComponent<ScrollRect>();
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.viewport = row;
            var content = SpaceTheme.Rect("Content", row);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var hl = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            hl.childAlignment = TextAnchor.MiddleLeft;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content;
            foreach (var n in log.photos)
            {
                string path = BoardGamePhotoStore.PathOf(_game, n);
                PhotoCell(content, path, 190f, () =>
                    BoardGamePhotoViewer.Show(transform, path, SpaceTheme.Font, () =>
                    {
                        // 大きくした画面で写真を消した: 記録から外して作り直す
                        log.photos.Remove(n);
                        PlayLogStore.Put(log);
                        _onChanged?.Invoke();
                        BuildView(log);
                    }));
            }
            y += 200f;
        }

        Button(card, T_DELETE, new Vector2(-300f, 50f), 240f, false, () => ConfirmDelete(log), SpaceTheme.DANGER);
        Button(card, T_EDIT_BTN, new Vector2(-20f, 50f), 280f, false, () =>
        {
            var parent = transform.parent;
            var g = _game;
            var onChanged = _onChanged;
            Destroy(gameObject);
            OpenForm(parent, g, log, onChanged);
        });
        Button(card, T_CLOSE, new Vector2(270f, 50f), 260f, true, () => Destroy(gameObject));
        gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    private void ConfirmDelete(PlayLogEntry log)
    {
        _sub = SpaceDialog.Confirm(null, T_DELETE_TITLE, T_DELETE_BODY, T_DELETE, true, () =>
        {
            _sub = null;
            foreach (var n in log.photos) BoardGamePhotoStore.Delete(BoardGamePhotoStore.PathOf(_game, n));
            PlayLogStore.Remove(log);
            _onChanged?.Invoke();
            Destroy(gameObject);
        }, () => _sub = null, T_CANCEL);
    }

    // ================================================================
    // プレイヤー管理
    // ================================================================
    private void OpenManager()
    {
        if (_memo != null) _memoText = _memo.text;
        if (_sub != null) Destroy(_sub);
        var root = SpaceTheme.Rect("Manager", transform);
        SpaceTheme.Stretch(root);
        _sub = root.gameObject;
        var safe = Backdrop(root, CloseManager);

        var members = PlayLogStore.Members;
        const float ROW = 96f;
        float listH = Mathf.Clamp(members.Count * ROW, 80f, 760f);
        float h = 120f + 70f + 130f + listH + 40f + 112f + 50f;
        var card = SpaceTheme.Rect("Card", safe, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(W, h),
            new Vector2(0.5f, 1f));
        SpaceTheme.Glass(card, 56f, SpaceTheme.GLASS_DEEP, SpaceTheme.A(ACCENT, 0.4f), true);
        float y = 70f;
        Label(card, T_MANAGE, 44, SpaceTheme.TEXT, y, true);
        y += 56f;
        var hint = Label(card, T_MANAGE_HINT, 24, SpaceTheme.TEXT_MUTED, y, true);
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 16f;
        hint.fontSizeMax = 24f;
        y += 50f;

        // 名前の入力と「追加」
        var input = Input(card, T_PH_NAME, y, 100f, PlayLogStore.NAME_MAX, false, W - 100f - 220f, -110f);
        var add = SpaceTheme.Rect("Add", card, new Vector2(1f, 1f), new Vector2(-50f, -y - 50f), new Vector2(200f, 100f),
            new Vector2(1f, 0.5f));
        var addFace = SpaceTheme.PrimaryFace(add);
        var addText = SpaceTheme.Text(add, "Label", T_ADD, 34, SpaceTheme.TEXT_DARK, FontStyles.Bold);
        SpaceTheme.Stretch(addText.rectTransform);
        SpaceTheme.Clickable(add.gameObject, addFace, () =>
        {
            string name = (input.text ?? "").Trim();
            if (name.Length == 0)
            {
                SpaceTheme.Toast(T_NAME_EMPTY, SpaceTheme.MainCanvas());
                return;
            }
            if (name.Length > PlayLogStore.NAME_MAX) name = name.Substring(0, PlayLogStore.NAME_MAX);
            if (PlayLogStore.Members.Exists(x => x.name == name))
            {
                SpaceTheme.Toast(T_NAME_DUP, SpaceTheme.MainCanvas());
                return;
            }
            PlayLogStore.AddMember(name);
            OpenManager();
        });
        y += 130f;

        // 登録した人の一覧（押すと消す）
        var list = SpaceTheme.Rect("List", card, new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(W - 100f, listH),
            new Vector2(0.5f, 1f));
        if (members.Count == 0)
        {
            var e = SpaceTheme.Text(list, "Empty", T_NONE, 28, SpaceTheme.TEXT_MUTED, FontStyles.Bold);
            SpaceTheme.Stretch(e.rectTransform);
        }
        else
        {
            SpaceTheme.HitArea(list);
            list.gameObject.AddComponent<RectMask2D>();
            var content = SpaceTheme.Rect("Content", list, new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(W - 100f, members.Count * ROW), new Vector2(0.5f, 1f));
            var sr = list.gameObject.AddComponent<ScrollRect>();
            sr.viewport = list;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = content.sizeDelta.y > listH + 1f;
            sr.movementType = ScrollRect.MovementType.Elastic;
            for (int i = 0; i < members.Count; i++)
            {
                var mem = members[i];
                var row = SpaceTheme.Rect("Row", content, new Vector2(0.5f, 1f), new Vector2(0f, -(i * ROW + ROW / 2f)),
                    new Vector2(W - 100f, ROW - 12f));
                SpaceTheme.Rounded(row, CHIP_FILL, 30f);
                SpaceTheme.Text(row, "Name", SpaceTheme.NoParse(SpaceTheme.Safe(mem.name)), 32, SpaceTheme.TEXT,
                    new Vector2(0f, 0.5f), new Vector2(32f, 0f), new Vector2(W - 400f, 60f), FontStyles.Bold,
                    TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
                var del = SpaceTheme.Rect("Delete", row, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(150f, 64f),
                    new Vector2(1f, 0.5f));
                var df = SpaceTheme.Glass(del, 32f, SpaceTheme.A(SpaceTheme.DANGER, 0.12f), SpaceTheme.A(SpaceTheme.DANGER, 0.6f), true);
                var dt = SpaceTheme.Text(del, "Label", T_DELETE, 26, SpaceTheme.DANGER, FontStyles.Bold);
                SpaceTheme.Stretch(dt.rectTransform);
                SpaceTheme.Clickable(del.gameObject, df, () =>
                {
                    SpaceDialog.Confirm(null, T_MEMBER_DELETE_TITLE, SpaceTheme.NoParse(SpaceTheme.Safe(mem.name)) + "\n"
                        + T_MEMBER_DELETE_BODY, T_DELETE, true, () =>
                    {
                        PlayLogStore.RemoveMember(mem);
                        _selected.Remove(mem.id);
                        _winners.Remove(mem.id);
                        OpenManager();
                    }, null, T_CANCEL);
                });
            }
        }

        Button(card, T_CLOSE, new Vector2(0f, 50f), 400f, true, CloseManager);
        root.gameObject.AddComponent<SpacePopIn>().Target = card;
    }

    private void CloseManager()
    {
        if (_sub != null) Destroy(_sub);
        _sub = null;
        RebuildForm();
    }

    // ================================================================
    // 部品
    // ================================================================
    private static TextMeshProUGUI Label(RectTransform card, string text, float size, Color color, float y, bool center)
    {
        return SpaceTheme.Text(card, "Label", text, size, color, new Vector2(0.5f, 1f), new Vector2(0f, -y),
            new Vector2(W - 80f, size * 1.5f), FontStyles.Bold, center ? TextAlignmentOptions.Center : TextAlignmentOptions.Left);
    }

    private static void SectionLabel(RectTransform card, string text, float y)
    {
        SpaceTheme.Text(card, "Section", text, 28, SpaceTheme.TEXT_SUB, new Vector2(0f, 1f), new Vector2(56f, -y),
            new Vector2(500f, 40f), FontStyles.Bold, TextAlignmentOptions.Left, new Vector2(0f, 0.5f));
    }

    private static void SmallButton(RectTransform parent, string label, Vector2 anchor, Vector2 pos, float w, Action onClick)
    {
        var b = SpaceTheme.Rect(label, parent, anchor, pos, new Vector2(w, 76f));
        var f = SpaceTheme.Rounded(b, SpaceTheme.A(ACCENT, 0.14f), 38f, true);
        SpaceTheme.Clickable(b.gameObject, f, () => onClick());
        var t = SpaceTheme.Text(b, "Label", label, 30, ACCENT, FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
    }

    private static void Button(RectTransform card, string label, Vector2 pos, float w, bool primary, Action onClick,
        Color? color = null)
    {
        var b = SpaceTheme.Rect(label, card, new Vector2(0.5f, 0f), pos, new Vector2(w, 112f), new Vector2(0.5f, 0f));
        Image face;
        if (primary) face = SpaceTheme.PrimaryFace(b);
        else face = SpaceTheme.Glass(b, 46f, new Color(0.09f, 0.12f, 0.30f, 0.63f),
            color.HasValue ? SpaceTheme.A(color.Value, 0.6f) : (Color?)null, true);
        SpaceTheme.Clickable(b.gameObject, face, () => onClick());
        var t = SpaceTheme.Text(b, "Label", label, 36, primary ? SpaceTheme.TEXT_DARK : (color ?? SpaceTheme.TEXT_SUB),
            FontStyles.Bold);
        SpaceTheme.Stretch(t.rectTransform);
    }

    private static TMP_InputField Input(RectTransform card, string placeholder, float top, float h, int limit, bool multiLine,
        float w = W - 100f, float x = 0f)
    {
        var box = SpaceTheme.Rect("Input", card, new Vector2(0.5f, 1f), new Vector2(x, -top), new Vector2(w, h),
            new Vector2(0.5f, 1f));
        var face = SpaceTheme.Rounded(box, FIELD_FILL, 30f, true);
        var edgeRt = SpaceTheme.Rect("Edge", box);
        SpaceTheme.Stretch(edgeRt);
        var edge = SpaceTheme.RoundedStroke(edgeRt, SpaceTheme.STROKE, 30f);
        var area = SpaceTheme.Rect("TextArea", box);
        SpaceTheme.Stretch(area, 28f, 14f, 28f, 14f);
        area.gameObject.AddComponent<RectMask2D>();
        var align = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
        var ph = SpaceTheme.Text(area, "Placeholder", placeholder, 30, SpaceTheme.TEXT_MUTED, FontStyles.Normal, align);
        SpaceTheme.Stretch(ph.rectTransform);
        var text = SpaceTheme.Text(area, "Text", "", 32, SpaceTheme.TEXT, FontStyles.Normal, align);
        SpaceTheme.Stretch(text.rectTransform);
        if (multiLine)
        {
            ph.textWrappingMode = TextWrappingModes.Normal;
            text.textWrappingMode = TextWrappingModes.Normal;
        }
        var input = box.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        input.fontAsset = SpaceTheme.Font;
        input.pointSize = 32;
        input.richText = false;
        input.customCaretColor = true;
        input.caretColor = ACCENT;
        input.caretWidth = 3;
        input.selectionColor = SpaceTheme.A(ACCENT, 0.3f);
        input.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        input.characterLimit = limit;
        input.targetGraphic = face;
        input.onSelect.AddListener(_ => edge.color = SpaceTheme.A(ACCENT, 0.85f));
        input.onDeselect.AddListener(_ => edge.color = SpaceTheme.STROKE);
        return input;
    }

    /// <summary>角丸の写真のマス（押せるときは onClick）。読み込んだ画像は画面を閉じるときに捨てる</summary>
    private RectTransform PhotoCell(RectTransform parent, string path, float size, Action onClick)
    {
        var cell = SpaceTheme.Rect("Photo", parent);
        var le = cell.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        var bg = SpaceTheme.Rounded(cell, SpaceTheme.A(SpaceTheme.LINE, 0.10f), 30f, onClick != null);
        cell.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        if (onClick != null) SpaceTheme.Clickable(cell.gameObject, bg, () => onClick());
        var tex = BoardGamePhotoStore.LoadThumbTexture(path);
        if (tex != null)
        {
            _textures.Add(tex);
            float k = size / Mathf.Min(tex.width, tex.height);
            var img = SpaceTheme.Rect("Img", cell, SpaceTheme.CENTER, Vector2.zero, new Vector2(tex.width * k, tex.height * k));
            var raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
        }
        return cell;
    }
}
