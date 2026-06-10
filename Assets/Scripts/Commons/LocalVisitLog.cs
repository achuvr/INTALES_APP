using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 来店（入店）記録を端末ローカルにJSONで保存するユーティリティ。
/// Firestoreは使わず Application.persistentDataPath に追記していく。
/// 保存先: {persistentDataPath}/visits/visit_log.json
/// </summary>
public static class LocalVisitLog
{
    private static string Dir      => Path.Combine(Application.persistentDataPath, "visits");
    private static string FilePath => Path.Combine(Dir, "visit_log.json");

    /// <summary>
    /// 営業終了時刻（深夜0:00を基準とした分数）。チェックアウト忘れの自動クローズに使う。
    /// 例: 22:00 → 22*60、26:00（＝翌2:00の深夜閉店）→ 26*60。店舗に合わせて変更可。
    /// </summary>
    private const int ClosingMinutesFromMidnight = 26 * 60; // 26:00 = 翌2:00

    /// <summary>現在時刻を入店時間として1件追記</summary>
    public static VisitRecord Record() => Record(DateTime.Now);

    /// <summary>指定時刻を入店時間として1件追記</summary>
    public static VisitRecord Record(DateTime time)
    {
        var record = new VisitRecord
        {
            checkedInAt = time.ToString("o"),                       // ISO 8601 (ラウンドトリップ)
            unixTime    = ((DateTimeOffset)time).ToUnixTimeSeconds(), // UNIX秒
        };

        var log = Load();
        log.visits.Add(record);
        Save(log);

        Debug.Log($"[LocalVisitLog] 入店時間を記録: {time}（累計 {log.visits.Count} 件）");
        return record;
    }

    /// <summary>現在時刻でチェックアウトを記録（最新の未チェックアウト来店に滞在時間を計算して紐付ける）</summary>
    public static VisitRecord CheckOut() => CheckOut(DateTime.Now);

    /// <summary>指定時刻でチェックアウトを記録。チェックイン記録が無ければ null を返す。</summary>
    public static VisitRecord CheckOut(DateTime time)
    {
        var log = Load();

        // 最新の「未チェックアウト」のチェックイン記録を探す
        VisitRecord open = null;
        for (int i = log.visits.Count - 1; i >= 0; i--)
        {
            if (!log.visits[i].IsCheckedOut) { open = log.visits[i]; break; }
        }

        if (open == null)
        {
            Debug.LogWarning("[LocalVisitLog] チェックイン記録が見つかりません（先にチェックインしてください）");
            return null;
        }

        open.checkedOutAt     = time.ToString("o");
        open.checkoutUnixTime = ((DateTimeOffset)time).ToUnixTimeSeconds();

        long seconds = open.checkoutUnixTime - open.unixTime;
        if (seconds < 0) seconds = 0;          // 時刻の巻き戻り対策
        open.stayMinutes = seconds / 60;
        open.stayText    = FormatStay(open.stayMinutes);

        Save(log);

        Debug.Log($"[LocalVisitLog] チェックアウトを記録: 滞在 {open.stayText}（{time}）");
        return open;
    }

    /// <summary>
    /// チェックアウト忘れ（営業終了時刻を過ぎても開いたままの来店）を自動クローズする。
    /// 退店時刻はその来店の営業終了時刻に設定し、autoClosed フラグを立てる。
    /// 次のQR読み取り時とアプリ起動時に呼ぶ。戻り値は自動クローズした件数。
    /// </summary>
    public static int AutoCloseStaleVisits() => AutoCloseStaleVisits(DateTime.Now);

    public static int AutoCloseStaleVisits(DateTime now)
    {
        var log = Load();
        int closed = 0;

        foreach (var v in log.visits)
        {
            if (v.IsCheckedOut) continue;
            if (!DateTime.TryParse(v.checkedInAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var checkinTime))
                continue;

            var deadline = ClosingDeadlineFor(checkinTime);
            if (now < deadline) continue; // まだ営業時間内 → 開いたままでOK

            // 営業終了時刻でクローズ（推定値）
            v.checkedOutAt     = deadline.ToString("o");
            v.checkoutUnixTime = ((DateTimeOffset)deadline).ToUnixTimeSeconds();
            long seconds = v.checkoutUnixTime - v.unixTime;
            if (seconds < 0) seconds = 0;
            v.stayMinutes = seconds / 60;
            v.stayText    = FormatStay(v.stayMinutes);
            v.autoClosed  = true;
            closed++;
        }

        if (closed > 0)
        {
            Save(log);
            Debug.LogWarning($"[LocalVisitLog] チェックアウト忘れ {closed} 件を営業終了時刻で自動クローズしました");
        }
        return closed;
    }

    /// <summary>
    /// 指定チェックイン時刻に対する「退店期限（営業終了時刻）」を返す。
    /// チェックイン以降で最初に来る営業終了時刻を求める（深夜閉店=24時超にも対応）。
    /// 在店状況の共有（PresenceService）の「まだ店にいる扱いか」の判定にも使う。
    /// </summary>
    public static DateTime ClosingDeadlineFor(DateTime checkinTime)
    {
        var close = checkinTime.Date.AddMinutes(ClosingMinutesFromMidnight);
        // チェックイン以降で最初の営業終了時刻に正規化
        while (close < checkinTime)          close = close.AddDays(1);
        while (close.AddDays(-1) >= checkinTime) close = close.AddDays(-1);
        return close;
    }

    /// <summary>分数を「○時間○分」表記に変換</summary>
    public static string FormatStay(long minutes)
    {
        long h = minutes / 60;
        long m = minutes % 60;
        return $"{h}時間{m}分";
    }

    /// <summary>未チェックアウトの来店が存在するか（＝今チェックイン中か）</summary>
    public static bool HasOpenVisit()
    {
        var log = Load();
        for (int i = log.visits.Count - 1; i >= 0; i--)
            if (!log.visits[i].IsCheckedOut) return true;
        return false;
    }

    /// <summary>保存済みの全来店記録を読み込む（古い順）</summary>
    public static VisitLog Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonUtility.FromJson<VisitLog>(File.ReadAllText(FilePath)) ?? new VisitLog();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LocalVisitLog] 読み込みエラー: {ex.Message}");
        }
        return new VisitLog();
    }

    /// <summary>最後の来店記録（なければ null）</summary>
    public static VisitRecord Latest()
    {
        var log = Load();
        return log.visits.Count > 0 ? log.visits[log.visits.Count - 1] : null;
    }

    /// <summary>累計の来店回数</summary>
    public static int Count() => Load().visits.Count;

    /// <summary>全記録を消去</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            Debug.Log("[LocalVisitLog] 来店記録をクリアしました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LocalVisitLog] クリアエラー: {ex.Message}");
        }
    }

    private static void Save(VisitLog log)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonUtility.ToJson(log, true));
    }
}

[Serializable]
public class VisitLog
{
    public List<VisitRecord> visits = new List<VisitRecord>();
}

[Serializable]
public class VisitRecord
{
    public string checkedInAt;      // チェックイン時刻 ISO 8601 文字列（ローカル時刻）
    public long   unixTime;         // チェックイン UNIX秒

    public string checkedOutAt;     // チェックアウト時刻 ISO 8601（未チェックアウトは空）
    public long   checkoutUnixTime; // チェックアウト UNIX秒（未チェックアウトは0）
    public long   stayMinutes;      // 滞在時間（分）
    public string stayText;         // 滞在時間の表示用「○時間○分」
    public bool   autoClosed;       // チェックアウト忘れを営業終了時刻で自動クローズした推定値か

    /// <summary>チェックアウト済みかどうか</summary>
    public bool IsCheckedOut => !string.IsNullOrEmpty(checkedOutAt);
}
