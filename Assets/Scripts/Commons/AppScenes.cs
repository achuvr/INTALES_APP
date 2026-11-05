/// <summary>
/// シーン名の定数。ログイン後に開くホーム画面はここで一元管理する。
///
/// 2026-11: 店舗形態の変更（12月）に合わせて、ホームを宇宙デザインの NewHome に切り替えた。
/// 戦闘・装備・アチーブメントは再設計中のため、旧ホーム（Home）はシーンごと残したまま入口だけ外している。
/// 旧ホームに戻すときは Home の値を LegacyHome に変えるだけでよい。
/// </summary>
public static class AppScenes
{
    /// <summary>ログイン後・キャラクター作成後に開くホーム画面</summary>
    public const string Home = NewHome;

    /// <summary>新ホーム（最低限のユーザー情報・カメラ起動・所持品一覧のみ）</summary>
    public const string NewHome = "NewHome";

    /// <summary>旧ホーム（キャラクター・戦闘・装備・ガチャなどを含む従来の画面）</summary>
    public const string LegacyHome = "Home";
}
