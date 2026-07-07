using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// ボードゲームへの個人マーク（「遊んだ」「お気に入り」）。
/// users/{uid}.played_boardgames / favorite_boardgames（ゲームキーの配列）に保存する。
///
/// 通信量: ユーザードキュメントはセッション開始時にどのみち1回読むため追加の読み取りはゼロ。
/// 書き込みは切り替え1回につき ArrayUnion / ArrayRemove の1回だけ
/// （読み直し不要のアトミック操作なので端末間で競合しない）。
/// オフライン時は Firestore SDK が書き込みをキューして再接続時に反映する。
/// </summary>
public static class BoardGameMarks
{
    private class Mark
    {
        public readonly string Field;                       // Firestoreのフィールド名
        public readonly Func<UserData, List<string>> List;  // UserData内の対応リスト
        public HashSet<string> Set;
        public UserData BuiltFrom; // UserData が再取得されたらセットを作り直すための目印

        public Mark(string field, Func<UserData, List<string>> list)
        {
            Field = field;
            List = list;
        }
    }

    private static readonly Mark Played =
        new Mark("played_boardgames", u => u.PlayedBoardgames);
    private static readonly Mark Favorite =
        new Mark("favorite_boardgames", u => u.FavoriteBoardgames);

    /// <summary>ゲームの識別キー（ボドゲーマURL末尾のスラッグ。URL不明時はタイトル）。</summary>
    public static string KeyOf(BoardGameEntry g)
    {
        if (g == null) return "";
        if (!string.IsNullOrEmpty(g.url))
        {
            string trimmed = g.url.TrimEnd('/');
            int i = trimmed.LastIndexOf('/');
            if (i >= 0 && i < trimmed.Length - 1) return trimmed.Substring(i + 1);
        }
        return g.title ?? "";
    }

    public static bool IsPlayed(BoardGameEntry g) => SetOf(Played).Contains(KeyOf(g));
    public static bool IsFavorite(BoardGameEntry g) => SetOf(Favorite).Contains(KeyOf(g));

    public static void SetPlayed(BoardGameEntry g, bool on) => SetMark(Played, g, on);
    public static void SetFavorite(BoardGameEntry g, bool on) => SetMark(Favorite, g, on);

    /// <summary>マークを切り替える。ローカルへ即時反映し、Firestoreへは非同期で1回書き込む。</summary>
    private static void SetMark(Mark m, BoardGameEntry g, bool on)
    {
        string key = KeyOf(g);
        if (string.IsNullOrEmpty(key)) return;

        var set = SetOf(m);
        bool changed = on ? set.Add(key) : set.Remove(key);
        if (!changed) return;

        // メモリ上の UserData にも反映しておく（セッション内の再参照との整合のため）
        var list = m.List(UserDataManager.instance.UserData);
        if (on) { if (!list.Contains(key)) list.Add(key); }
        else list.Remove(key);

        SaveAsync(m.Field, key, on).Forget();
    }

    private static async UniTaskVoid SaveAsync(string field, string key, bool on)
    {
        try
        {
            string uid = UserDataManager.instance != null ? UserDataManager.instance.UID : null;
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogWarning($"[BGMarks] UID未設定のため保存できません（メモリ上のみ反映）: {field}");
                return;
            }

            var docRef = FirebaseFirestore.DefaultInstance.Collection("users").Document(uid);
            await docRef.UpdateAsync(field,
                on ? FieldValue.ArrayUnion(key) : FieldValue.ArrayRemove(key)).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BGMarks] 保存エラー ({field}/{key}): {ex.Message}");
        }
    }

    private static HashSet<string> SetOf(Mark m)
    {
        var ud = UserDataManager.instance != null ? UserDataManager.instance.UserData : null;
        if (m.Set == null || !ReferenceEquals(m.BuiltFrom, ud))
        {
            m.BuiltFrom = ud;
            m.Set = ud != null ? new HashSet<string>(m.List(ud)) : new HashSet<string>();
        }
        return m.Set;
    }
}
