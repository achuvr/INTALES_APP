using UnityEngine;

/// <summary>
/// ボス討伐の連勝数を端末ローカル（PlayerPrefs）に記録するユーティリティ。
/// 勝てば +1、負ければ 0 にリセット。最高連勝数（自己ベスト）も併せて保持する。
/// グループ戦でも自分視点の連勝として個別に積み上がる。
/// </summary>
public static class BattleStreak
{
    private const string KeyCurrent = "boss_win_streak";      // 現在の連勝数
    private const string KeyBest    = "boss_win_streak_best";  // 最高連勝数

    /// <summary>現在の連勝数。</summary>
    public static int Current => PlayerPrefs.GetInt(KeyCurrent, 0);

    /// <summary>最高連勝数（自己ベスト）。</summary>
    public static int Best => PlayerPrefs.GetInt(KeyBest, 0);

    /// <summary>勝利を記録して連勝数を +1 し、更新後の連勝数を返す。</summary>
    public static int RecordWin()
    {
        int next = Current + 1;
        PlayerPrefs.SetInt(KeyCurrent, next);
        if (next > Best) PlayerPrefs.SetInt(KeyBest, next);
        PlayerPrefs.Save();
        return next;
    }

    /// <summary>敗北を記録して連勝数を 0 にリセットする。</summary>
    public static void RecordLoss()
    {
        PlayerPrefs.SetInt(KeyCurrent, 0);
        PlayerPrefs.Save();
    }
}
