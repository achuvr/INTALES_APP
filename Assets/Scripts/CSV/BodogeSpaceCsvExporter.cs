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


public class BodogeSpaceCsvExporter : MonoBehaviour
{
    [Header("Target space games url (no ?page=)")]
    [SerializeField] private string spaceUrl = "https://bodoge.hoobby.net/spaces/intales-cafe/games";

    [Header("Request interval (sec)")]
    [SerializeField] private float sleepSec = 1.0f;

    [Header("Output file name")]
    [SerializeField] private string outputFileName = "intales_space_games.csv";

    private const string BASE = "https://bodoge.hoobby.net";

    // Python版と同じ「行っぽい」判定
    private static readonly Regex RE_PLAYERS = new(@"\b\d+人", RegexOptions.Compiled);
    private static readonly Regex RE_TIME    = new(@"\b\d+分|分前後", RegexOptions.Compiled);
    private static readonly Regex RE_AGE     = new(@"\b\d+歳", RegexOptions.Compiled);
    private static readonly Regex RE_YEAR    = new(@"\b\d{4}年", RegexOptions.Compiled);

    // 抜き出し（age/rawは不要なので持たない）
    private static readonly Regex RE_PLAYERS_CAPTURE = new(@"(\d+人[^ ]*)", RegexOptions.Compiled);
    private static readonly Regex RE_TIME_CAPTURE    = new(@"(\d+分[^ ]*|分前後)", RegexOptions.Compiled);
    private static readonly Regex RE_YEAR_CAPTURE    = new(@"(\d{4}年[^ ]*)", RegexOptions.Compiled);

    private void Start()
    {
        FetchAndExportCsv();
    }

    [ContextMenu("Fetch and Export CSV")]
    public void FetchAndExportCsv()
    {
        StartCoroutine(FetchAndExportCsvCoroutine());
    }

    private IEnumerator FetchAndExportCsvCoroutine()
    {
        var results = new List<GameRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int page = 1;

        while (true)
        {
            string url = $"{spaceUrl}?page={page}";
            Debug.Log($"[Bodoge] Fetch: {url}");

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (compatible; bodoge-space-scraper/1.1)");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Bodoge] Request failed: {req.error} ({url})");
                yield break;
            }

            string html = req.downloadHandler.text;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            int added = 0;

            // a[href^="/games/"]
            var nodes = doc.DocumentNode.SelectNodes("//a[starts-with(@href, '/games/')]");
            if (nodes != null)
            {
                foreach (var a in nodes)
                {
                    string text = HtmlEntity.DeEntitize(a.InnerText ?? "").Trim();
                    text = Regex.Replace(text, @"\s+", " "); // Pythonの get_text(" ", strip=True) 相当

                    if (string.IsNullOrWhiteSpace(text) || !IsRealListRow(text))
                        continue;

                    string href = a.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(href))
                        continue;

                    string path = href.Split('?', 2)[0];
                    string gameUrl = new Uri(new Uri(BASE), path).ToString();

                    if (!seen.Add(gameUrl))
                        continue;

                    var row = ParseRow(text);
                    row.Url = gameUrl;
                    results.Add(row);
                    added++;
                }
            }

            // 「次へ」リンクが無い or 追加0 で終了（Python版踏襲）
            bool hasNext =
                doc.DocumentNode.SelectNodes("//a")
                    ?.Any(n => HtmlEntity.DeEntitize(n.InnerText ?? "").Trim() == "次へ")
                ?? false;

            Debug.Log($"[Bodoge] page={page}, added={added}, total={results.Count}, hasNext={hasNext}");

            if (!hasNext || added == 0)
                break;

            page++;

            if (sleepSec > 0f)
                yield return new WaitForSeconds(sleepSec);
        }

        // 五十音順（日本語カルチャ）でタイトルソート
        var ja = new CultureInfo("ja-JP");
        results.Sort((a, b) =>
            string.Compare(a.Title, b.Title, ja, CompareOptions.StringSort)
        );

        
        // CSV保存（persistentDataPath）
        string outPath = Path.Combine(Application.persistentDataPath, outputFileName);
        Debug.Log("path = " + Application.persistentDataPath);
        WriteCsv(outPath, results);

        Debug.Log($"[Bodoge] DONE. saved: {outPath} (count={results.Count})");
    }

    private static bool IsRealListRow(string text)
    {
        return RE_PLAYERS.IsMatch(text)
            && RE_TIME.IsMatch(text)
            && RE_AGE.IsMatch(text)
            && RE_YEAR.IsMatch(text);
    }

    private static GameRow ParseRow(string text)
    {
        string title = text.Split('（')[0].Trim();

        string players = null;
        var m = RE_PLAYERS_CAPTURE.Match(text);
        if (m.Success) players = m.Groups[1].Value;

        string playTime = null;
        m = RE_TIME_CAPTURE.Match(text);
        if (m.Success) playTime = m.Groups[1].Value;

        string year = null;
        m = RE_YEAR_CAPTURE.Match(text);
        if (m.Success) year = m.Groups[1].Value;

        return new GameRow
        {
            Title = title,
            Players = players,
            PlayTime = playTime,
            Year = year,
            Url = ""
        };
    }

    private static void WriteCsv(string filePath, List<GameRow> rows)
    {
        // Excelで開きたいならUTF-8 BOM付きが安定
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));
        writer.WriteLine("title,players,play_time,year,url");

        foreach (var r in rows)
        {
            writer.WriteLine(string.Join(",",
                CsvEscape(r.Title),
                CsvEscape(r.Players),
                CsvEscape(r.PlayTime),
                CsvEscape(r.Year),
                CsvEscape(r.Url)
            ));
        }
    }

    private static string CsvEscape(string s)
    {
        s ??= "";
        bool needQuote = s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"');
        if (s.Contains('"')) s = s.Replace("\"", "\"\"");
        return needQuote ? $"\"{s}\"" : s;
    }

    [Serializable]
    private class GameRow
    {
        public string Title;
        public string Players;
        public string PlayTime;
        public string Year;
        public string Url;
    }
}
