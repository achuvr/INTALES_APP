using System.Collections;
using UnityEngine;

/// <summary>
/// タブレット（横長アスペクト比）でのみ、Homeキャラクターページ下部の6ボタングリッド
/// （ガチャ/フレンド/バッグ/情報/戦闘/装備）を 3行×2列 から 2行×3列 に組み替える。
///
/// 背景: 6ボタンは GachaController が定義するグリッド(列x=±221.23 / 行y=-488,-638,-788 /
/// 実効サイズ379.4×134.7)に並ぶ。縦長スマホは下に余白があり問題ないが、9:16より横長な
/// タブレットでは CanvasScaler(Expand) で参照高さ1920が画面ぴったりになり、最下行(戦闘/装備)が
/// 画面下端アンカーのフッターと重なって隠れる。
///
/// 対策: タブレット時のみ、横幅に余裕がある特性を活かして 2行×3列 に並べ替え、全体の高さを
/// 抑えてフッターの上に収める。スマホ(縦長)では一切変更しない。
///
/// ガチャ(__GachaButton)とフレンド(__FriendBtn)は実行時生成のため、生成を待ってから配置する。
/// HomeSceneInitializer から AddComponent で追加される（シーン編集不要）。
/// </summary>
public class TabletFooterLayout : MonoBehaviour
{
    private const string TAG = "[TabletFooterLayout]";

    // ポートレートのアスペクト比(幅/高さ)がこの値以上をタブレットとみなす。
    // 近年のスマホ ≈0.46〜0.50 / 旧16:9端末=0.5625 / タブレット ≈0.66(iPad)〜0.78。
    private const float TabletAspectThreshold = 0.6f;

    // 2行×3列の配置パラメータ（参照解像度px、中央アンカー基準）
    private const float ColX = 405f;     // 左右列のxオフセット（中央列=0）
    private const float UpperY = -490f;  // 上段の中心y
    private const float LowerY = -640f;  // 下段の中心y（フッター上端-723の上に収まる）

    // 配置順: [上段 左→中→右, 下段 左→中→右]。並べ替えはこの順序を変えるだけ。
    private static readonly string[] GridOrder =
    {
        "Button_Battle", "Button_Equipment", "Button_OpenBag",    // 上段: 戦闘 装備 バッグ
        "__GachaButton", "__FriendBtn",      "Button_OpenProfile",// 下段: ガチャ フレンド 情報
    };

    private void Start() => StartCoroutine(Apply());

    private IEnumerator Apply()
    {
        yield return null; // Simulator解像度・Canvas確定待ち

        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        if (aspect < TabletAspectThreshold) yield break; // スマホ(縦長)は現状維持

        // 実行時生成ボタン(__GachaButton/__FriendBtn)が出揃うのを待つ（最大~2秒）
        int frames = 0;
        while (frames++ < 120 && CountFound() < GridOrder.Length) yield return null;
        Canvas.ForceUpdateCanvases();

        FixReloadButton();

        int placed = 0;
        for (int i = 0; i < GridOrder.Length; i++)
        {
            var rt = FindInScene(GridOrder[i]);
            if (rt == null) { Debug.LogWarning($"{TAG} 未検出: {GridOrder[i]}"); continue; }
            int col = i % 3, row = i / 3;
            float x = (col - 1) * ColX;
            float y = (row == 0) ? UpperY : LowerY;
            rt.anchoredPosition = new Vector2(x, y);
            placed++;
        }
        Debug.Log($"{TAG} タブレット用に下部ボタンを2行3列へ再配置 ({placed}/{GridOrder.Length})");
    }

    /// <summary>
    /// 右上のリロードボタン(Image_Reload)を画面の右上に追従させる。
    /// このボタンは Page_Characters(100x100の中央パネル)の右上アンカー＋固定座標(354,978.6)で
    /// 配置されているため、実効位置が画面中央から +1028.6 と高く、画面高の低いiPad(参照高1920・
    /// 上端+960)では上にはみ出て見えなくなる。Canvasの実寸から右上隅基準で座標を再計算し、
    /// iPhoneと同じ「右端から61px・上端から65px」(ボタン150x150)に合わせる。
    /// </summary>
    private static void FixReloadButton()
    {
        var rt = FindInScene("Image_Reload");
        if (rt == null) { Debug.LogWarning($"{TAG} Image_Reload 未検出"); return; }
        var canvas = rt.GetComponentInParent<Canvas>(true);
        var canvasRt = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRt == null) { Debug.LogWarning($"{TAG} Reload: Canvas未検出"); return; }

        float halfW = canvasRt.rect.width * 0.5f;
        float halfH = canvasRt.rect.height * 0.5f;
        // 親(Page_Characters)右上アンカー=画面中央+50。そこから見て
        // 画面右端-61px(=halfW-136が中心) / 上端-65px(=halfH-140が中心) になるよう逆算。
        Vector2 old = rt.anchoredPosition;
        rt.anchoredPosition = new Vector2(halfW - 186f, halfH - 190f);
        Debug.Log($"{TAG} Image_Reload {old} -> {rt.anchoredPosition} (canvas {canvasRt.rect.width}x{canvasRt.rect.height})");
    }

    private static int CountFound()
    {
        int c = 0;
        foreach (var n in GridOrder) if (FindInScene(n) != null) c++;
        return c;
    }

    /// <summary>
    /// シーン内オブジェクトを名前で検索（非アクティブページ配下でも見つかるよう全走査、
    /// プレハブ/アセット由来は scene 無効で除外）。
    /// </summary>
    private static RectTransform FindInScene(string name)
    {
        foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (rt.name != name) continue;
            if (!rt.gameObject.scene.IsValid()) continue;
            return rt;
        }
        return null;
    }
}
