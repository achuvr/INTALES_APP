using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using UnityEngine;
using UnityEngine.Networking;
using System.Globalization;

/// <summary>
/// bodoge.hoobby.net のスペース（店舗）ページから全ボードゲームを取得し、
/// アプリ同梱用の JSON（Resources/BoardGames/boardgames.json）を生成するツール。
///
/// タイトル・英名・人数・時間・発売年を一覧ページから、ジャンル（テーマ/フレーバー＋
/// メカニクス）を各ゲームの詳細ページから取得する。BoardGameCatalog が読み込む形式に合わせて出力。
///
/// 使い方: 空のGameObjectにこのスクリプトを付けて Play するか、Inspectorの右クリックメニュー
/// 「Fetch and Export JSON」を実行する。在庫が変わったら再実行してJSONを差し替える。
/// エディタ実行時は Assets/Resources/BoardGames/ に直接書き出す。
/// </summary>
public class BodogeSpaceJsonExporter : MonoBehaviour
{
    [Header("Target space games url (no ?page=)")]
    [SerializeField] private string spaceUrl = "https://bodoge.hoobby.net/spaces/intales-cafe/games";

    [Header("Request interval (sec)")]
    [SerializeField] private float sleepSec = 0.5f;

    [Header("Fetch genre from each game's detail page (slow but complete)")]
    [SerializeField] private bool fetchGenre = true;

    [Header("Output resource path (under Assets/Resources, no extension)")]
    [SerializeField] private string resourcePath = "BoardGames/boardgames";

    [SerializeField] private bool runOnStart = false;

    private const string BASE = "https://bodoge.hoobby.net";

    private void Start()
    {
        if (runOnStart) FetchAndExportJson();
    }

    [ContextMenu("Fetch and Export JSON")]
    public void FetchAndExportJson()
    {
        StartCoroutine(FetchAndExportJsonCoroutine());
    }

    private IEnumerator FetchAndExportJsonCoroutine()
    {
        var results = new List<BoardGameEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int page = 1;
        while (true)
        {
            string url = $"{spaceUrl}?page={page}";
            Debug.Log($"[Bodoge] Fetch list: {url}");

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (compatible; bodoge-space-scraper/1.1)");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Bodoge] List request failed: {req.error} ({url})");
                yield break;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(req.downloadHandler.text);

            int added = 0;
            var rows = doc.DocumentNode.SelectNodes("//a[contains(@class,'name') and starts-with(@href,'/games/')]");
            if (rows != null)
            {
                foreach (var a in rows)
                {
                    string href = a.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    string path = href.Split('?', 2)[0];
                    string gameUrl = new Uri(new Uri(BASE), path).ToString();
                    if (!seen.Add(gameUrl)) continue;

                    var jpNode = a.SelectSingleNode(".//span[contains(@class,'jp')]");
                    var enNode = a.SelectSingleNode(".//span[contains(@class,'en')]");
                    var dataNode = a.SelectSingleNode(".//span[contains(@class,'data')]");
                    string data = dataNode != null ? Clean(dataNode.InnerText) : "";

                    var entry = new BoardGameEntry
                    {
                        title = jpNode != null ? Clean(jpNode.InnerText) : "",
                        title_en = enNode != null ? Clean(enNode.InnerText).Trim('（', '）', '(', ')') : "",
                        players = Match(data, @"([0-9]+人(?:～[0-9]+人)?)"),
                        time = Match(data, @"([0-9]+分(?:～[0-9]+分)?|[0-9]+分前後|分前後)"),
                        year = Match(data, @"([0-9]{4}年)"),
                        url = gameUrl,
                        genre = Array.Empty<string>(),
                    };
                    results.Add(entry);
                    added++;
                }
            }

            bool hasNext = doc.DocumentNode.SelectNodes("//a")
                ?.Any(n => HtmlEntity.DeEntitize(n.InnerText ?? "").Trim() == "次へ") ?? false;
            Debug.Log($"[Bodoge] page={page}, added={added}, total={results.Count}, hasNext={hasNext}");
            if (!hasNext || added == 0) break;

            page++;
            if (sleepSec > 0f) yield return new WaitForSeconds(sleepSec);
        }

        // ジャンルを各詳細ページから取得
        if (fetchGenre)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var g = results[i];
                using var req = UnityWebRequest.Get(g.url);
                req.SetRequestHeader("User-Agent", "Mozilla/5.0 (compatible; bodoge-space-scraper/1.1)");
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                    g.genre = ParseGenre(req.downloadHandler.text);
                if ((i + 1) % 50 == 0) Debug.Log($"[Bodoge] genre {i + 1}/{results.Count}");
                if (sleepSec > 0f) yield return new WaitForSeconds(sleepSec);
            }
        }

        // 五十音順にソート
        var ja = new CultureInfo("ja-JP");
        results.Sort((a, b) => string.Compare(a.title, b.title, ja, CompareOptions.StringSort));

        WriteJson(results);
    }

    /// <summary>詳細ページの「テーマ/フレーバー」「メカニクス」見出し配下のタグを集める。</summary>
    private static string[] ParseGenre(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var lis = doc.DocumentNode.SelectNodes("//ul[contains(@class,'credit')]//li");
        if (lis == null) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var li in lis)
        {
            var head = li.SelectSingleNode(".//span[contains(@class,'headline')]");
            string label = head != null ? Clean(head.InnerText) : "";
            if (!(label.Contains("テーマ") || label.Contains("フレーバー") || label.Contains("メカニクス")))
                continue;
            var anchors = li.SelectNodes(".//a");
            if (anchors == null) continue;
            foreach (var a in anchors)
            {
                string t = Clean(a.InnerText);
                if (!string.IsNullOrEmpty(t) && !result.Contains(t)) result.Add(t);
            }
        }
        return result.ToArray();
    }

    private void WriteJson(List<BoardGameEntry> games)
    {
        var data = new BoardGameCatalogData
        {
            count = games.Count,
            source = spaceUrl,
            updated = DateTime.Now.ToString("yyyy-MM-dd"),
            games = games.ToArray(),
        };
        string json = JsonUtility.ToJson(data, true);

        string outPath;
#if UNITY_EDITOR
        outPath = Path.Combine(Application.dataPath, "Resources", resourcePath + ".json");
#else
        outPath = Path.Combine(Application.persistentDataPath, Path.GetFileName(resourcePath) + ".json");
#endif
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllText(outPath, json, new UTF8Encoding(false));
        Debug.Log($"[Bodoge] DONE. saved: {outPath} (count={games.Count})");

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    private static string Clean(string s) =>
        HtmlEntity.DeEntitize(Regex.Replace(s ?? "", @"\s+", " ")).Trim();

    private static string Match(string s, string pattern)
    {
        var m = Regex.Match(s ?? "", pattern);
        return m.Success ? m.Groups[1].Value : "";
    }
}
