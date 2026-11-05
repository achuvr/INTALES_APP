using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 新ホームのボードゲーム図鑑（使いたい人だけ）。
/// 右上の歯車のメニューの「ボードゲーム図鑑」からダウンロードした端末にだけ、
/// ホームの中央の惑星の左下に図鑑の小さな惑星（緑）が出て、押すと図鑑（SpaceBoardGameZukan。宇宙デザイン。
/// できることは旧ホームの BoardGameListController と同じ）が開く。
///  ・ダウンロード … いちばん新しいゲーム一覧をこの端末に用意して（BoardGameCatalog.DownloadAsync）、ダウンロード済みにする
///  ・削除         … ダウンロード済みを外して、ホームから図鑑の惑星を消す
///                  （遊んだ・お気に入りの記録と写真、相席募集でも使うゲーム一覧は残す）
/// ダウンロード済みかは端末に保存する（PlayerPrefs。退会で消える。機種変更したらもう一度ダウンロード）。
/// </summary>
public static class SpaceZukan
{
    private const string PREF_KEY = "zukan_installed";

    /// <summary>この端末で図鑑をダウンロード済みか</summary>
    public static bool Installed => PlayerPrefs.GetInt(PREF_KEY, 0) == 1;

    /// <summary>ダウンロード・削除したとき（新ホームが図鑑の惑星を出し入れする）</summary>
    public static event Action Changed;

    /// <summary>ダウンロードする。用意できたら true</summary>
    public static async UniTask<bool> DownloadAsync()
    {
        bool ok = await BoardGameCatalog.DownloadAsync();
        if (!ok) return false;
        PlayerPrefs.SetInt(PREF_KEY, 1);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>削除する（ダウンロード済みを外すだけ。記録・写真・ゲーム一覧は残す）</summary>
    public static void Remove()
    {
        PlayerPrefs.DeleteKey(PREF_KEY);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
