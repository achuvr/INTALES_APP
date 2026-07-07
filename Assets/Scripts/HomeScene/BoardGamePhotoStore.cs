using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// ボードゲームごとの「遊んだ記録」写真のローカル保存。
///
/// 保存先は Application.persistentDataPath/boardgame_photos/{ゲームキー}/。
/// 本体（最大1280px・JPG）と一覧表示用サムネイル（最大256px・～_thumb.jpg）を書き出す。
///
/// 写真は意図的に Firestore 等へアップロードしない（不適切な画像が他ユーザーへ
/// 共有されるのを防ぐため、この端末の中だけに保存する）。
/// </summary>
public static class BoardGamePhotoStore
{
    private const string ROOT_DIR = "boardgame_photos";
    private const string THUMB_SUFFIX = "_thumb";
    private const int THUMB_MAX = 256;

    /// <summary>このゲームの写真（フルサイズ）のパス一覧（撮影順）。</summary>
    public static List<string> PhotosOf(BoardGameEntry g)
    {
        var result = new List<string>();
        string dir = DirOf(g);
        if (!Directory.Exists(dir)) return result;
        foreach (var f in Directory.GetFiles(dir, "*.jpg"))
            if (!Path.GetFileNameWithoutExtension(f).EndsWith(THUMB_SUFFIX))
                result.Add(f);
        result.Sort(StringComparer.Ordinal); // ファイル名がタイムスタンプなので撮影順になる
        return result;
    }

    /// <summary>
    /// 撮影テクスチャを保存し、フルサイズのパスを返す（失敗時null）。
    /// tex は読み取り可能（readable）であること。
    /// </summary>
    public static string AddPhoto(BoardGameEntry g, Texture2D tex)
    {
        try
        {
            string dir = DirOf(g);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".jpg");
            File.WriteAllBytes(path, tex.EncodeToJPG(85));

            var thumb = Downscale(tex, THUMB_MAX);
            File.WriteAllBytes(ThumbPathOf(path), thumb.EncodeToJPG(70));
            if (thumb != tex) UnityEngine.Object.Destroy(thumb);
            return path;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BGPhoto] 保存エラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>写真をローカルから削除する（サムネイルも一緒に消す）。</summary>
    public static void Delete(string photoPath)
    {
        try
        {
            if (File.Exists(photoPath)) File.Delete(photoPath);
            string thumb = ThumbPathOf(photoPath);
            if (File.Exists(thumb)) File.Delete(thumb);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BGPhoto] 削除エラー: {ex.Message}");
        }
    }

    /// <summary>一覧用サムネイルを読み込む（無ければフルサイズで代替。失敗時null）。</summary>
    public static Texture2D LoadThumbTexture(string photoPath)
    {
        string thumb = ThumbPathOf(photoPath);
        return LoadTexture(File.Exists(thumb) ? thumb : photoPath);
    }

    /// <summary>画像ファイルをTexture2Dとして読み込む（失敗時null）。呼び出し側がDestroyすること。</summary>
    public static Texture2D LoadTexture(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return tex;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BGPhoto] 読み込みエラー ({path}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 撮影日時（ファイル名のタイムスタンプから復元）。
    /// 復元できない場合はファイルの更新日時、それも取れなければ DateTime.MinValue。
    /// </summary>
    public static DateTime TakenAtOf(string photoPath)
    {
        string name = Path.GetFileNameWithoutExtension(photoPath);
        if (name.EndsWith(THUMB_SUFFIX))
            name = name.Substring(0, name.Length - THUMB_SUFFIX.Length);
        if (DateTime.TryParseExact(name, "yyyyMMdd_HHmmss_fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        try { return File.GetLastWriteTime(photoPath); }
        catch { return DateTime.MinValue; }
    }

    private static string ThumbPathOf(string photoPath) =>
        Path.Combine(Path.GetDirectoryName(photoPath),
            Path.GetFileNameWithoutExtension(photoPath) + THUMB_SUFFIX + ".jpg");

    private static string DirOf(BoardGameEntry g) =>
        Path.Combine(Application.persistentDataPath, ROOT_DIR, SanitizeKey(BoardGameMarks.KeyOf(g)));

    /// <summary>ゲームキーをフォルダ名に使える形にする（URL由来のスラッグは基本そのまま）。</summary>
    private static string SanitizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "unknown";
        var sb = new System.Text.StringBuilder(key.Length);
        foreach (var c in key)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.ToString();
    }

    /// <summary>長辺が maxSize を超えるテクスチャを縮小する（超えない場合は元をそのまま返す）。</summary>
    private static Texture2D Downscale(Texture2D src, int maxSize)
    {
        int longer = Mathf.Max(src.width, src.height);
        if (longer <= maxSize) return src;

        float k = (float)maxSize / longer;
        int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k));
        int h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));

        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var result = new Texture2D(w, h, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }
}
