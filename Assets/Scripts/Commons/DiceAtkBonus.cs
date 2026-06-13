using UnityEngine;

/// <summary>
/// ATKクーポンによるダイスの出目ボーナス。
/// バッグでATKクーポンを使うと1枚につき+1が積まれ、次のダイスロールで
/// 全量が出目に加算されて消費される。アプリを再起動しても消えないよう
/// 端末ローカル（PlayerPrefs）に保存する。
/// </summary>
public static class DiceAtkBonus
{
    private const string PREF_KEY = "dice_atk_bonus";

    /// <summary>未消費のボーナス値（次のロールに加算される分）</summary>
    public static int Pending => PlayerPrefs.GetInt(PREF_KEY, 0);

    /// <summary>ATKクーポン使用時に加算する（1枚 = +1）</summary>
    public static void Add(int count)
    {
        PlayerPrefs.SetInt(PREF_KEY, Pending + count);
        PlayerPrefs.Save();
    }

    /// <summary>ロール時に全量を消費して返す（未積みなら0）</summary>
    public static int Consume()
    {
        int value = Pending;
        if (value != 0)
        {
            PlayerPrefs.DeleteKey(PREF_KEY);
            PlayerPrefs.Save();
        }
        return value;
    }
}
