using System;
using System.Collections.Generic;

/// <summary>
/// 来店の記録（users/{uid}.journey のマップ）。惑星と来店のしくみ（公転・流れ星・惑星の解放・★特典）の状態。
/// 書き込みは JourneyService のトランザクションでまとめて行い、マップ全体を書き直す。
/// 公転の番号（period / star / perk）は StoreCalendar.PeriodOf の値で、0 は「記録なし」（実際の番号は 22 以上）。
/// Firestore の数値は long で返ってくるので、型変換に頼らず自分で読み書きする。
/// </summary>
public sealed class JourneyData
{
    public const int VERSION = 1;

    public int Visits;            // 来店回数（1営業日に1回まで数える）
    public string Day;            // 最後に来店を数えた営業日 "yyyy-MM-dd"
    public int Period;            // 最後に来店した公転
    public int Streak;            // 続いている公転の数
    public int Best;              // いちばん長く続いた公転の数
    public List<int> Rescues = new List<int>(); // 軌道修正（休んでも途切れない）を使った公転（直近だけ残す）
    public int Insurance;         // 休みの保険（オルビス。最大1）
    public int StarPeriod;        // 流れ星を引いた公転
    public List<string> Extra = new List<string>(); // その公転で引いた「もう1回」の種類（together/first/gift/rubra）
    public int PerkPeriod;        // ★お店の特典を使った公転
    public string PerkPlanet;     // そのとき使った惑星
    public long InAt;             // 最後に入店した時刻（UNIX秒。入店QRを読み取った時刻）
    public List<int> Days = new List<int>(); // 来店した曜日（System.DayOfWeek）
    public string MetDay;         // フレンドと一緒に来店した記録を最後に処理した営業日
    public List<string> MetUids = new List<string>(); // その日に処理済みのフレンド
    /// <summary>
    /// 惑星ごとのパワー（0〜6）の記録。パワー（同じ公転にもう一度来ると中央の惑星にたまり、MAX で流れ星の7%オフが
    /// 出やすい。2026-11-06）は 2026-11-10 にやめた（ユーザー指定「お客様に説明が難しい」）。
    /// 記録は消さずに残す（読み込んだ値をそのまま書き戻すだけで、増やさない・使わない）
    /// </summary>
    public Dictionary<string, int> Power = new Dictionary<string, int>();
    /// <summary>
    /// コマをもらった入店の惑星（まだ退店していない。2026-11-10）。退店すると Exhausted に移る。
    /// 退店しないまま次の営業日になったら、次の入店のときに移す（それまでも表示は力を失った扱い。JourneyRules.IsExhausted）
    /// </summary>
    public string Used;
    /// <summary>Used の入店の営業日 "yyyy-MM-dd"</summary>
    public string UsedDay;
    /// <summary>力を失った惑星（入店してもコマはもらえず、色が白く淡くなる。回復のしくみはまだない）</summary>
    public List<string> Exhausted = new List<string>();

    public static JourneyData From(object raw)
    {
        if (!(raw is Dictionary<string, object> m)) return null;
        var j = new JourneyData
        {
            Visits = Int(m, "visits"),
            Day = Str(m, "day"),
            Period = Int(m, "period"),
            Streak = Int(m, "streak"),
            Best = Int(m, "best"),
            Insurance = Int(m, "insurance"),
            StarPeriod = Int(m, "star"),
            PerkPeriod = Int(m, "perk"),
            PerkPlanet = Str(m, "perk_planet"),
            InAt = Long(m, "in_at"),
            MetDay = Str(m, "met_day"),
            Used = Str(m, "used"),
            UsedDay = Str(m, "used_day"),
        };
        if (j.Used == "") j.Used = null;
        IntList(m, "rescues", j.Rescues);
        IntList(m, "days", j.Days);
        StrList(m, "extra", j.Extra);
        StrList(m, "met_uids", j.MetUids);
        StrList(m, "exhausted", j.Exhausted);
        if (m.TryGetValue("power", out var pw) && pw is Dictionary<string, object> pm)
        {
            foreach (var kv in pm)
            {
                try { j.Power[kv.Key] = Convert.ToInt32(kv.Value); }
                catch { /* 壊れた値は読み飛ばす */ }
            }
        }
        return j;
    }

    public Dictionary<string, object> ToMap() => new Dictionary<string, object>
    {
        { "v", VERSION },
        { "visits", Visits },
        { "day", Day ?? "" },
        { "period", Period },
        { "streak", Streak },
        { "best", Best },
        { "rescues", new List<object>(Rescues.ConvertAll(x => (object)(long)x)) },
        { "insurance", Insurance },
        { "star", StarPeriod },
        { "extra", new List<object>(Extra) },
        { "perk", PerkPeriod },
        { "perk_planet", PerkPlanet ?? "" },
        { "in_at", InAt },
        { "days", new List<object>(Days.ConvertAll(x => (object)(long)x)) },
        { "met_day", MetDay ?? "" },
        { "met_uids", new List<object>(MetUids) },
        { "power", PowerMap() },
        { "used", Used ?? "" },
        { "used_day", UsedDay ?? "" },
        { "exhausted", new List<object>(Exhausted) },
    };

    private Dictionary<string, object> PowerMap()
    {
        var m = new Dictionary<string, object>();
        foreach (var kv in Power) m[kv.Key] = (long)kv.Value;
        return m;
    }

    public JourneyData Clone()
    {
        var c = (JourneyData)MemberwiseClone();
        c.Rescues = new List<int>(Rescues);
        c.Extra = new List<string>(Extra);
        c.Days = new List<int>(Days);
        c.MetUids = new List<string>(MetUids);
        c.Power = new Dictionary<string, int>(Power);
        c.Exhausted = new List<string>(Exhausted);
        return c;
    }

    /// <summary>今日（営業日）入店したか</summary>
    public bool VisitedOn(string dayKey) => !string.IsNullOrEmpty(Day) && Day == dayKey;

    private static int Int(Dictionary<string, object> m, string k) => (int)Long(m, k);

    private static long Long(Dictionary<string, object> m, string k)
    {
        if (!m.TryGetValue(k, out var v) || v == null) return 0;
        try { return Convert.ToInt64(v); }
        catch { return 0; }
    }

    private static string Str(Dictionary<string, object> m, string k) =>
        m.TryGetValue(k, out var v) && v != null ? v.ToString() : null;

    private static void IntList(Dictionary<string, object> m, string k, List<int> into)
    {
        if (!m.TryGetValue(k, out var v) || !(v is IEnumerable<object> list)) return;
        foreach (var o in list)
        {
            try { into.Add(Convert.ToInt32(o)); }
            catch { /* 壊れた値は読み飛ばす */ }
        }
    }

    private static void StrList(Dictionary<string, object> m, string k, List<string> into)
    {
        if (!m.TryGetValue(k, out var v) || !(v is IEnumerable<object> list)) return;
        foreach (var o in list)
            if (o != null) into.Add(o.ToString());
    }
}
