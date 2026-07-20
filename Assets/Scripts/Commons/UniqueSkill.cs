using System.Collections.Generic;

/// <summary>装備しているときだけ使えるユニークスキルの種類。</summary>
public enum UniqueSkillType
{
    /// <summary>カード除去: バフカードを引く前に、山札から好きなカードを1種類取り除ける（塔覇の本(A)・選択式）</summary>
    DeckPurge,

    /// <summary>カード変化: バフカードを引く前に、山札の1枚がランダムな別のカードに変化する（塔覇の本(B)・自動発動）</summary>
    DeckTransmute,

    /// <summary>財宝: 山札QRを読んだとき、山札が6枚以上なら3枚ごとにGP+1（ドミニオンのスキルブック(B)・自動発動）</summary>
    DeckTreasure,

    /// <summary>SPカード強化: 引いたSPカードのバフが発動する戦闘で攻撃力が追加で＋1（城壁都市のスキルブック(A)・自動発動）</summary>
    SpCardAtkUp,

    /// <summary>カード選択: 山札が「パーティ人数+3」枚以上なら、抽選の代わりに好きなカードを選んで引ける。SPカードは選べない（カタンのスキルブック(A)・選択式）</summary>
    DeckPick,

    /// <summary>アップグレード: バフカードを引く前に、山札のカードを1枚選んで1ランク上のカードに強化できる。SPカードは選べない（オーディンの術書(A)・選択式）</summary>
    DeckUpgrade,

    /// <summary>セット注文: 戦闘開始時に「本日セット商品を頼んだと店員に伝えてください」の案内モーダルを出す（セットコレクションのスキルブック(A)・自動発動）</summary>
    SetMenuNotice,

    /// <summary>ワーカー配置: 戦闘開始時に、参戦中の味方が装備するスキルブック(A)のスキルを1つ借りてこの戦闘で使える（ワーカープレイスメントのスキルブック(A)・選択式）</summary>
    SkillBorrow,

    /// <summary>ワーカー配置(B): 戦闘開始時に、参戦中の味方が装備するスキルブック(B)のスキルを1つ借りてこの戦闘で使える。A装備時はAの後に発動（ワーカープレイスメントのスキルブック(B)・選択式）</summary>
    SkillBorrowB,
}

/// <summary>
/// 特定の装備（主にスキルブック）を装備しているときだけ発動できるユニークスキルの定義と判定。
///
/// 対象装備は master/items の item_id または表示名のどちらかの一致で照合する。
/// item_id が安定だが、装備を削除→再登録するとIDが変わるため名前でも引けるようにしている
/// （逆に名前を変えてもIDで引き続き一致する）。
///
/// 新しいユニークスキルの追加手順:
///  1. UniqueSkillType に enum を追加
///  2. SKILLS に名前・説明を追加
///  3. ITEM_SKILLS に対象装備を追加
///  4. 発動箇所に IsActive(...) 判定と処理を書く（例: カード除去は CallMethodFromQR.DrawBuffCard）
/// </summary>
public static class UniqueSkill
{
    private class Def { public string Name; public string Description; }

    private static readonly Dictionary<UniqueSkillType, Def> SKILLS = new Dictionary<UniqueSkillType, Def>
    {
        { UniqueSkillType.DeckPurge, new Def {
            Name = "カード除去",
            Description = "バフカードを引く前に、山札から好きなカードを1種類取り除ける",
        } },
        { UniqueSkillType.DeckTransmute, new Def {
            Name = "カード変化",
            Description = "バフカードを引く前に、山札の1枚がランダムな別のカードに変化する（自動発動）",
        } },
        { UniqueSkillType.DeckTreasure, new Def {
            Name = "財宝",
            Description = "山札QRを読んだとき、山札が6枚以上なら3枚ごとにGPを1獲得する（自動発動）",
        } },
        { UniqueSkillType.SpCardAtkUp, new Def {
            Name = "SPカード強化",
            Description = "SPカードのバフが発動する戦闘で、攻撃力が追加で＋1される（自動発動）",
        } },
        { UniqueSkillType.DeckPick, new Def {
            Name = "カード選択",
            Description = "山札がパーティ人数+3枚以上あるとき、バフカードを選んで引ける（SPカードは選べない）",
        } },
        { UniqueSkillType.DeckUpgrade, new Def {
            Name = "アップグレード",
            Description = "バフカードを引く前に、山札のカードを1枚選んで1ランク上のカードに強化できる（SPカードは選べない）",
        } },
        { UniqueSkillType.SetMenuNotice, new Def {
            Name = "セット注文",
            Description = "戦闘開始時に、本日セット商品を頼んだと店員へ伝える案内が表示される（自動発動）",
        } },
        { UniqueSkillType.SkillBorrow, new Def {
            Name = "ワーカー配置",
            Description = "戦闘開始時に、参戦中の味方のスキルブック(A)のスキルを1つ借りて、この戦闘で使える（グループ参加中のみ）",
        } },
        { UniqueSkillType.SkillBorrowB, new Def {
            Name = "ワーカー配置",
            Description = "戦闘開始時に、参戦中の味方のスキルブック(B)のスキルを1つ借りて、この戦闘で使える（グループ参加中のみ）",
        } },
    };

    /// <summary>ユニークスキル付き装備（item_id または name の一致で判定）。</summary>
    private static readonly (string itemId, string itemName, UniqueSkillType skill)[] ITEM_SKILLS =
    {
        // Slay the Spire のスキルブックA「塔覇の本(A)」
        ("HAvSDcqEdCv3WWMCB8Rd", "塔覇の本(A)", UniqueSkillType.DeckPurge),
        // Slay the Spire のスキルブックB「塔覇の本(B)」
        ("BC5jZBHrrxMWFCThr3Nn", "塔覇の本(B)", UniqueSkillType.DeckTransmute),
        // ドミニオンのスキルブックB
        // （master/items 未登録のため現状は名前一致のみ。登録後にIDを追記するとより堅くなる）
        ("", "ドミニオンのスキルブック(B)", UniqueSkillType.DeckTreasure),
        // 城壁都市のスキルブックA
        ("YqEBGpt5RsOAbHpe7g7Q", "城壁都市のスキルブック(A)", UniqueSkillType.SpCardAtkUp),
        // カタンのスキルブックA
        // （master/items 未登録のため現状は名前一致のみ。登録後にIDを追記するとより堅くなる）
        ("", "カタンのスキルブック(A)", UniqueSkillType.DeckPick),
        // オーディンの術書A
        ("81zesJHYrlEh67bZ4wIz", "オーディンの術書(A)", UniqueSkillType.DeckUpgrade),
        // セットコレクションのスキルブックA
        ("2IyH2CPnLDv3I76b3ckw", "セットコレクションのスキルブック(A)", UniqueSkillType.SetMenuNotice),
        // ワーカープレイスメントのスキルブックA
        ("KbyJJRCs2Ljc7Xvu2SHT", "ワーカープレイスメントのスキルブック(A)", UniqueSkillType.SkillBorrow),
        // ワーカープレイスメントのスキルブックB
        ("yaeQX0XtsJf25LotLrL8", "ワーカープレイスメントのスキルブック(B)", UniqueSkillType.SkillBorrowB),
    };

    public static string DisplayName(UniqueSkillType t) => SKILLS[t].Name;
    public static string Description(UniqueSkillType t) => SKILLS[t].Description;

    /// <summary>この装備が持つユニークスキル（なければ null）。</summary>
    public static UniqueSkillType? FromItem(string itemId, string itemName)
    {
        foreach (var (id, name, skill) in ITEM_SKILLS)
        {
            if (!string.IsNullOrEmpty(itemId) && itemId == id) return skill;
            if (!string.IsNullOrEmpty(itemName) && itemName == name) return skill;
        }
        return null;
    }

    /// <summary>
    /// 選択中キャラクターが指定のユニークスキルを持つ装備を着けているか。
    /// 装備状態は端末ローカル（LocalEquipSave）、装備データは ItemCacheManager を見る。
    /// </summary>
    public static bool IsActive(UniqueSkillType skill)
    {
        var cache = ItemCacheManager.instance;
        var manager = UserDataManager.instance;
        if (cache == null || !cache.IsLoaded || manager == null) return false;

        int charIdx = manager.CurrentSelectCharacterNumber;
        foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
        {
            string itemId = LocalEquipSave.Load(charIdx, slot);
            if (string.IsNullOrEmpty(itemId)) continue;
            var item = cache.FindById(itemId);
            if (FromItem(itemId, item?.name) == skill) return true;
        }
        return false;
    }
}
