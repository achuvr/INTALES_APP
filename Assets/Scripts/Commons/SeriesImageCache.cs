using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

/// <summary>
/// シリーズのセット発動中キャラ画像（職業別）のダウンロード・キャッシュ。
/// 保存先: persistentDataPath/series_images/{seriesId}_{job}_{urlハッシュ}.png
/// 画像を差し替えるとURLが変わる（アップロード時にタイムスタンプ付きパスになる）ため、
/// ファイル名にURLハッシュを含めて変更を検知し、同じシリーズ×職業の古いキャッシュは削除する。
/// </summary>
public static class SeriesImageCache
{
    /// <summary>生成済みSpriteのメモリキャッシュ（キー=ファイル名）。</summary>
    private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

    private static string Dir => Path.Combine(Application.persistentDataPath, "series_images");

    /// <summary>
    /// セット画像のSpriteを返す（未設定・取得失敗はnull）。
    /// ローカルキャッシュがあれば読み取り、なければURLからダウンロードして保存する。
    /// </summary>
    public static async UniTask<Sprite> GetAsync(CachedSeries series, string job)
    {
        string url = series?.GetImageUrl(job);
        if (string.IsNullOrEmpty(url)) return null;

        string key = $"{series.series_id}_{job}_{Fnv1a(url):x8}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, key + ".png");
            if (!File.Exists(path))
            {
                using var req = UnityWebRequest.Get(url);
                await req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[SeriesImage] DL失敗: {series.series_id}/{job} - {req.error}");
                    return null; // 失敗はキャッシュしない（次回再試行）
                }
                // 画像差し替えで残った古いキャッシュを掃除してから保存
                foreach (var old in Directory.GetFiles(Dir, $"{series.series_id}_{job}_*.png"))
                    File.Delete(old);
                File.WriteAllBytes(path, req.downloadHandler.data);
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); // mipmapなし
            tex.LoadImage(File.ReadAllBytes(path));
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _cache[key] = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SeriesImage] 読み込み失敗: {series.series_id}/{job} - {ex.Message}");
            return null;
        }
    }

    /// <summary>URLの安定ハッシュ（FNV-1a。string.GetHashCodeは実行ごとに変わり得るため使わない）。</summary>
    private static uint Fnv1a(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }
    }
}
