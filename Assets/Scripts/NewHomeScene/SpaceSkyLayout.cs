using System;
using System.Collections.Generic;

/// <summary>
/// ホームの空に星座を置く場所の計算（2026-11-14 ユーザー指定「名前などが載ってるUIの下など空きスペースが気になるので、
/// 良い感じに散らばるように配置し直して」）。
/// それまでは、押せる星座6つが左右の列（お知らせ・料金表〜図鑑・履歴の間）に固まり、押せない星座はカードの下の1列と、
/// 決まった割合の場所（見出し・所持品ボタン・図鑑の名前と重なっていた）だったので、星座がまだそろっていない人には
/// カードの下や下のほうが空いて見えた。
/// ホームの部品のあいだの空いている所を3つに分け、16の星座を左右対称に置く（星座の形はそれぞれ違うので、対称でも堅く見えない）:
///  ・カードの下の帯（カードの下端〜お知らせ・料金表の押せる範囲の上端。どの端末でも 168 の帯）に6つ。
///    金貨座・さいころ座は両はしから2つめ（カードの下寄り）。中央の惑星の押せる範囲にかかってそこでは小さくなる端末だけ、両はし。
///    両はしは、特に役割のない切り札座（左）・山札座（右）（2026-11-14 ユーザー指定「端っこに寄せて」。両はしに金貨座・さいころ座が
///    入る端末では、左右の列のいちばん上の画面のふち）
///  ・左右の列（お知らせ・料金表の名前の下〜図鑑・履歴の上、画面のふち〜中央の惑星の押せる範囲）に、外・内・外…のジグザグ。
///    きずな座・ピース座は上から2つめ
///  ・下（図鑑・履歴の名前の下〜所持品の上）が広い端末は4つ（両はしは宝石座・積み木座、内側の2つはカメラの下の高さまで上げる）。
///    狭ければ図鑑と履歴の間・カメラの下に2つ、それもなければ下には置かず、そのぶん左右の列を増やす（宝石座・積み木座は列のいちばん下）
/// 大きさは置き場ごとに入るだけ（押せる星座は 0.86 倍、ほかは 0.8 倍まで。0.42 倍より小さくはしない）。
/// どの星座がどこかは、完成していてもいなくても同じ（あとから完成した星座が出ても、ほかの星座は動かない）。
/// 確かめる道具: scratchpad の sky2/proto.py（6端末 × INTALES WORLD のあり・なしを描く）。Unity を使わない計算だけのクラス
/// </summary>
public static class SpaceSkyLayout
{
    /// <summary>ホームの部品の位置（どれもキャンバスの左下からの距離）。NewHomeController が作って SpaceBackground に渡す</summary>
    public struct Frame
    {
        public float Width;           // キャンバスの幅
        public float CardBottom;      // ユーザー情報カードの下端
        public float MoonTop;         // お知らせ・料金表の小さな惑星の押せる範囲の上端
        public float MoonLabelBottom; // 〃 名前の下端
        public float CameraTop;       // 中央の惑星の押せる範囲の上端
        public float CameraBottom;    // 〃 下端
        public float CameraLeft;      // 〃 左端（右端は左右対称）
        public float SideTop;         // 図鑑・履歴の小さな惑星の押せる範囲の上端
        public float SideLabelBottom; // 〃 名前の下端
        public float SideLabelRight;  // 図鑑の名前の右端（いちばん大きい文字のとき。履歴の名前は短いので、左右対称の位置より内に収まる）
        public float BagTop;          // 所持品ボタンの上端
    }

    /// <summary>星座1つの置き場（中心。キャンバスの左下からの距離）と大きさ（星座の絵の大きさに対する倍率）</summary>
    public struct Spot
    {
        public float X, Y, Scale;
    }

    private const float FIG = 190f;          // 星座の絵の大きさ（SpaceBackground.SKY_FIG と同じ）
    private const float M = 12f;             // 部品・画面のふちとのすき間
    private const float M_CARD = 10f;        // カードとのすき間
    private const float G = 16f;             // 星座どうしのすき間
    private const float PAD = 1.04f;         // 星座を少し傾けるぶんの余裕
    private const float TAP_MAX = 0.86f;     // 押せる星座の大きさの上限
    private const float OTHER_MAX = 0.8f;    // ほかの星座の大きさの上限
    private const float MIN = 0.42f;         // 大きさの下限
    private const float BOTTOM_MIN = 118f;   // 下に置くのに要る高さ
    private const float WAVE = 14f;          // 帯の星座を上下に少しずらす（一直線に並ばないように）
    private const float BOTTOM_EDGE = 30f;   // 下の両はしの星座を、画面のふちから内へ寄せる
    private const float BOTTOM_INNER = 10f;  // 下の内側の2つの、まん中からのすき間（G の半分に足す）

    private static float Half(float scale) => FIG * 0.5f * scale * PAD;
    private static float ScaleOfHalf(float half) => half / (FIG * 0.5f * PAD);

    /// <summary>
    /// 置き場を計算する。leftTap は左に置く押せる星座（帯・列・下の順＝金貨座・きずな座・宝石座）、rightTap は右の同じ並び
    /// （さいころ座・ピース座・積み木座）。others はそれ以外の星座（Constellations.All の順。入るだけ置く）。
    /// edgeLeft・edgeRight は others のうち、端っこ（帯の左右のはし。そこに金貨座・さいころ座が入る端末では、左右の列のいちばん上の
    /// 画面のふち）に置く星座（2026-11-14 ユーザー指定「特に役割のない切り札座と山札座は端っこに寄せて」）
    /// </summary>
    public static Dictionary<string, Spot> Compute(Frame f, IList<string> leftTap, IList<string> rightTap, IList<string> others,
        string edgeLeft = null, string edgeRight = null)
    {
        float W = f.Width, cx = W * 0.5f;
        float hTapCap = Half(TAP_MAX), hCap = Half(OTHER_MAX), hMin = Half(MIN);
        var res = new Dictionary<string, Spot>();
        var pairs = new List<Slot>(); // ほかの星座の置き場（左側。右はその鏡）

        void Put(string id, float x, float y, float h)
        {
            if (!string.IsNullOrEmpty(id)) res[id] = new Spot { X = x, Y = y, Scale = ScaleOfHalf(h) };
        }

        void PutPair(int index, float x, float y, float h)
        {
            Put(index < leftTap.Count ? leftTap[index] : null, x, y, h);
            Put(index < rightTap.Count ? rightTap[index] : null, W - x, y, h);
        }

        // ---- 下: 図鑑・履歴の名前の下〜所持品の上が広ければ4つ、狭ければ図鑑と履歴の間・カメラの下に2つ
        float band = (f.SideLabelBottom - M) - (f.BagTop + M);
        float center = (f.CameraBottom - M) - (f.BagTop + M);
        int nBottom = band >= BOTTOM_MIN ? 4 : center >= BOTTOM_MIN ? 2 : 0;
        float innerByLabel = (cx - G * 0.5f - BOTTOM_INNER - (f.SideLabelRight + M)) * 0.5f; // 図鑑の名前より内に収まる大きさ
        bool bottomTap = false;
        float bx = 0f, by = 0f, bh = 0f;   // 下の両はし（押せる星座）
        bool bottomInner = false;
        float ix = 0f, iy = 0f, ih = 0f;   // 下の内側（カメラの下の高さまで上げる）
        if (nBottom == 4)
        {
            bh = Math.Min(hTapCap, band * 0.5f);
            bx = M + bh + BOTTOM_EDGE;
            by = (f.BagTop + M + f.SideLabelBottom - M) * 0.5f;
            bottomTap = true;
            ih = Math.Min(Math.Min(hCap, center * 0.5f), Math.Min(innerByLabel, (cx - G * 0.5f - (bx + bh + G)) * 0.5f));
        }
        else if (nBottom == 2)
        {
            ih = Math.Min(Math.Min(hCap, center * 0.5f), innerByLabel);
        }
        if (nBottom > 0)
        {
            ix = cx - G * 0.5f - ih - BOTTOM_INNER;
            iy = (f.BagTop + M + f.CameraBottom - M) * 0.5f;
            bottomInner = true;
        }

        // ---- カードの下の帯: 6つ（左の3つを決めて、右は鏡）
        float ty0 = f.MoonTop + M, ty1 = f.CardBottom - M_CARD;
        float hTop = Math.Min(hTapCap, (ty1 - ty0) * 0.5f);
        float span = W - 2f * M - 2f * hTop;
        var top = new Slot[3];
        for (int k = 0; k < 3; k++)
        {
            float x = M + hTop + span * k / 5f;
            float h = k < 2 ? hTop : Math.Min(hCap, hTop); // 1つめ・2つめは押せる星座も入るので、押せる星座の大きさまで
            float yc = (ty0 + ty1) * 0.5f;
            float wave = Math.Min(WAVE, (ty1 - ty0) * 0.5f - h);
            float y = yc + (k == 0 ? 0f : k == 1 ? wave : -wave);
            // まん中寄りは、中央の惑星の押せる範囲（の上端）にかからないように（かかる端末では小さく）
            if (x + h > f.CameraLeft)
            {
                float room = (ty1 - (f.CameraTop + M)) * 0.5f;
                if (room < h) h = Math.Max(hMin, room);
                y = Math.Min(y, ty1 - h);
                y = Math.Max(y, f.CameraTop + M + h);
            }
            top[k] = new Slot(x, y, h);
        }
        // 金貨座・さいころ座は帯の2つめ（はしと同じ大きさで入るなら）。入らない端末は帯のはし
        int tapTopK = top[1].H >= top[0].H - 1e-3f ? 1 : 0;
        // 端っこの組（edgeLeft・edgeRight を置く）: 帯の両はし。そこに金貨座・さいころ座が入る端末では、左右の列のいちばん上（外側）
        bool wantEdge = !string.IsNullOrEmpty(edgeLeft) || !string.IsNullOrEmpty(edgeRight);
        int edgePair = -1;
        for (int k = 0; k < 3; k++)
        {
            if (k == tapTopK)
            {
                PutPair(0, top[k].X, top[k].Y, top[k].H);
                continue;
            }
            if (wantEdge && k == 0) edgePair = pairs.Count;
            pairs.Add(top[k]);
        }

        // ---- 左右の列: お知らせ・料金表の名前の下〜図鑑・履歴の上。数は、ほかの星座が入りきるだけ
        bool gemInColumn = !bottomTap;
        int bottomOthers = bottomInner ? 2 : 0;
        int need = Math.Max(0, others.Count - (top.Length - 1) * 2 - bottomOthers); // 列に入れる、ほかの星座の数（左右合わせて）
        int nSide = 1 + (gemInColumn ? 1 : 0) + (need + 1) / 2;
        var col = new List<Slot>();
        float hSide = FitColumn(nSide, M, f.CameraLeft - M, f.SideTop + M, f.MoonLabelBottom - M, hTapCap, col);
        int bondK = nSide >= 2 ? 1 : 0;
        int gemK = gemInColumn ? nSide - 1 : -1;
        for (int k = 0; k < col.Count; k++)
        {
            if (k == bondK) PutPair(1, col[k].X, col[k].Y, hSide);
            else if (k == gemK) PutPair(2, col[k].X, col[k].Y, hSide);
            else
            {
                if (wantEdge && edgePair < 0 && k == 0) edgePair = pairs.Count;
                pairs.Add(new Slot(col[k].X, col[k].Y, hSide));
            }
        }

        // ---- 下
        if (bottomTap) PutPair(2, bx, by, bh);
        if (bottomInner) pairs.Add(new Slot(ix, iy, ih));

        // ---- 端っこの組に edgeLeft・edgeRight、残りの組にほかの星座を順に（左・右）
        if (edgePair >= 0)
        {
            var e = pairs[edgePair];
            Put(edgeLeft, e.X, e.Y, e.H);
            Put(edgeRight, W - e.X, e.Y, e.H);
        }
        int next = 0;
        for (int p = 0; p < pairs.Count; p++)
        {
            if (p == edgePair) continue;
            for (int side = 0; side < 2; side++)
            {
                while (next < others.Count && edgePair >= 0 && (others[next] == edgeLeft || others[next] == edgeRight)) next++;
                if (next >= others.Count) break;
                Put(others[next++], side == 0 ? pairs[p].X : W - pairs[p].X, pairs[p].Y, pairs[p].H);
            }
        }
        return res;
    }

    private readonly struct Slot
    {
        public readonly float X, Y, H; // 中心と、四角の半分の大きさ

        public Slot(float x, float y, float h)
        {
            X = x;
            Y = y;
            H = h;
        }
    }

    /// <summary>
    /// 左の列に n 個を外・内・外…のジグザグに置く（右の列は鏡）。上下のすき間と、となりどうし・1つおきどうしの間が足りる
    /// いちばん大きい大きさ（四角の半分の大きさ。hCap まで）を返し、位置を outSlots に入れる（上から順）
    /// </summary>
    private static float FitColumn(int n, float x0, float x1, float y0, float y1, float hCap, List<Slot> outSlots)
    {
        float hMin = Half(MIN);
        for (float h = hCap; h >= hMin - 1e-4f; h -= 2f)
        {
            float xo = x0 + h, xi = Math.Max(xo, x1 - h); // 幅が足りなければまっすぐ縦に
            float yTop = y1 - h, yBot = y0 + h;
            if (n == 1)
            {
                outSlots.Add(new Slot((xo + xi) * 0.5f, (y0 + y1) * 0.5f, h));
                return h;
            }
            if (yBot > yTop) continue;
            float dy = (yTop - yBot) / (n - 1);
            bool okSame = n <= 2 || 2f * dy >= 2f * h + G;
            bool okNext = (float)Math.Sqrt((xi - xo) * (xi - xo) + dy * dy) >= 2f * h + G;
            if (!okSame || !okNext) continue;
            for (int k = 0; k < n; k++) outSlots.Add(new Slot(k % 2 == 0 ? xo : xi, yTop - k * dy, h));
            return h;
        }
        // 入りきらない（ふつうはない）: いちばん小さい大きさで、重なってもそのまま並べる
        {
            float h = hMin, xo = x0 + h, xi = Math.Max(xo, x1 - h);
            float yTop = y1 - h, yBot = y0 + h;
            float dy = (yTop - yBot) / Math.Max(1, n - 1);
            for (int k = 0; k < n; k++) outSlots.Add(new Slot(k % 2 == 0 ? xo : xi, yTop - k * dy, h));
            return h;
        }
    }
}
