using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ボス討伐バトル（コード生成UI）。
/// Homeシーンの戦闘ボタン（Button_Battle）から全画面オーバーレイで開く。
///
/// 流れ:
///  1. ボスHP = 参加プレイヤー人数 × 4 + 補正値
///     （補正: 51Lv以上が誰もいない→1 / 51〜100Lvがいる→2 / 101Lv以上がいる→3）
///     参加人数・レベルはグループ参加中なら GroupSession.Members、未参加なら自分1人。
///  2. ダイスを選択（各ダイスに 1D100 での成功確率を表示）。
///     成功確率＝clamp(ダイス基礎＋自分Lv＋お得バフ＋装備ProbUp, 0, 90)。
///  3. 1D100 を振り、成功確率以下なら成功・超えたら失敗。
///     出目がプレイヤーのクリティカル率(%)以下ならクリティカル（与ダメージ＋クリティカルダメージ）、
///     96〜100でファンブル（攻撃力−1）。クリティカル率・ダメージは装備由来で
///     デフォルトはクリティカル率5%・クリティカルダメージ1。
///  4. 成功なら選んだダイス、失敗なら必ず 1D4 を振る。
///     その出目（＋クリティカルダメージ／ファンブル補正＋ATKクーポン）がプレイヤーの攻撃力。
///  5. 攻撃力ぶんボスHPを削る。0になったら討伐成功。
///
/// ダイスを振る演出は既存の Dice3DSimulator を流用する（Firestore通信なし）。
/// </summary>
public class BossBattleController : MonoBehaviour
{
    /// <summary>攻撃ダイス定義（表記・個数・面数・成功確率の初期値・解放レベル）。
    /// 成功確率 = clamp(probBase + 自分のレベル, 0, 90)。
    /// 1D4 は選択肢ではなく「失敗時に振るダイス」なのでここには含めない。</summary>
    private static readonly (string label, int count, int faces, int probBase, int reqLv)[] DICE =
    {
        ("1D6",  1, 6,  50,  0),
        ("1D8",  1, 8,  35,  0),
        ("1D10", 1, 10, 20,  0),
        ("2D6",  2, 6,  0,   0),
        ("2D8",  2, 8,  -70, 101), // Lv101以上で解放
    };

    private const int PROB_MAX = 90; // 成功確率の上限

    // 配色（ソシャゲ風のダーク＋金枠）
    private static readonly Color C_OVERLAY   = new Color(0.04f, 0.02f, 0.08f, 0.96f);
    private static readonly Color C_PANEL     = new Color(0.12f, 0.09f, 0.18f, 0.98f);
    private static readonly Color C_BORDER    = new Color(0.84f, 0.66f, 0.18f, 1.00f);
    private static readonly Color C_TITLE     = new Color(0.98f, 0.90f, 0.62f, 1.00f);
    private static readonly Color C_HP_BG     = new Color(0.20f, 0.05f, 0.05f, 1.00f);
    private static readonly Color C_HP_FILL   = new Color(0.86f, 0.16f, 0.16f, 1.00f);
    private static readonly Color C_DICE_BTN  = new Color(0.22f, 0.16f, 0.34f, 1.00f);
    private static readonly Color C_DICE_TXT  = new Color(0.98f, 0.94f, 0.80f, 1.00f);
    private static readonly Color C_PROB_TXT  = new Color(0.62f, 0.92f, 0.70f, 1.00f);
    private static readonly Color C_CLOSE_BTN = new Color(0.86f, 0.28f, 0.28f, 1.00f);
    private static readonly Color C_PHASE     = new Color(0.98f, 0.92f, 0.70f, 1.00f);
    private static readonly Color C_MUTED     = new Color(0.62f, 0.56f, 0.46f, 0.90f);

    private Canvas _canvas;
    private GameObject _overlay;
    private GameObject _selectPanel;
    private Transform _diceGrid;
    private GameObject _diceViewBg;
    private RawImage _diceView;
    private Dice3DSimulator _sim;

    private RectTransform _bossVisual;   // 被弾シェイク用（親）
    private Image _bossImage;            // ボス絵
    private RectTransform _bossImageRt;  // アイドル浮遊用（子）
    private RectTransform _hpFillRt;    // 幅でHPを増減させる
    private TextMeshProUGUI _hpText;
    private TextMeshProUGUI _bossName;
    private TextMeshProUGUI _phaseText;
    private TextMeshProUGUI _weakText;
    private string _bossElement = "fire"; // ボスの属性
    private string _weakness = "fire";    // ボスの弱点属性（=今日お得な属性）

    private GameObject _actionArea;      // 「振る」ボタンの置き場
    private GameObject _actionButton;
    private TextMeshProUGUI _actionLabel;
    private GameObject _skillButton;     // 「スキル発動」（ダイス選択後・1D100前に表示）
    private TextMeshProUGUI _skillLabel;
    private GameObject _cancelButton;    // 「選び直す」
    private GameObject _closeButton;     // ✕（ダイスを振り始めたら隠す）

    // 選択中ダイス（「振る」待ち）
    private (string label, int count, int faces, int prob) _pending;
    // 1D100の判定結果（攻撃ダイス「振る」待ち）
    private bool _d100Success;
    private int _d100AtkMod;        // ファンブル時のペナルティ（-1。クリティカルなら0）
    private bool _isCritical;       // 今回のロールがクリティカルだったか
    private int _critBonus;         // クリティカル時に与ダメージへ加算する量（クリティカルダメージ）

    // 戦闘開始時にキャッシュする装備由来ステータス（装備は戦闘中変わらない）
    private int _equipCritRate = 5;
    private int _equipCritDamage = 1;
    private int _equipProbBonus = 0; // 成功確率への加算（装備 ProbUp）
    private int _critRateMultiplier = 1; // クリティカル率の倍率（ウイングスパン等。装備＋一時バフの合計に掛かる）

    // 装備中スキルブックの任意発動スキル（1戦闘につき各1回）
    private readonly List<BattleSkillDef> _battleSkills = new List<BattleSkillDef>();
    private readonly HashSet<string> _usedSkills = new HashSet<string>();
    // スキル発動で今回の1D100にだけ乗る一時バフ（ダイス選択し直しでリセット）
    private int _skillCritRate, _skillCritDamage;
    private int _armedSubtractFaces; // >0 なら次の1D100でこの面数のダイスを振り出目を差し引く
    private int _coopAtkPenalty;     // 協力スキルを自分が発動した場合の攻撃力ペナルティ（この挑戦のみ）

    // バフカード（戦闘1回限り。OpenBattleで手札を1枚消費して適用）
    private string _cardName;                 // 適用中カード名（null/空=なし）
    private int _cardAtkBonus;                // 攻撃力に常時加算
    private int _cardAtkBonusOnSuccess;       // 1D100成功時のみ攻撃力に加算
    private HashSet<string> _cardDiceLabels;  // 指定ラベルのダイスのみ成功確率+（ソフトドリンク）
    private int _cardDiceBonus;

    private int _maxHp, _hp;
    private bool _busy;
    private bool _battleEnded;
    private bool _waitingForRolls; // 自分は振り終え、仲間の番を待っている
    private string _selectMsg = "どのダイスで挑む？";

    /// <summary>自分の users/{uid} ドキュメント参照（GP・レベルアップ書き込み用）。</summary>
    private static DocumentReference UserDocRef =>
        FirebaseFirestore.DefaultInstance.Collection("users").Document(UserDataManager.instance.UID);

    private void OnEnable()
    {
        GroupSession.BossDamaged += OnRemoteBossDamage;
        GroupSession.Changed += OnGroupChanged;
        GroupSession.CoopChanged += OnCoopChanged;
    }
    private void OnDisable()
    {
        GroupSession.BossDamaged -= OnRemoteBossDamage;
        GroupSession.Changed -= OnGroupChanged;
        GroupSession.CoopChanged -= OnCoopChanged;
    }

    /// <summary>協力バフが付与/変化したとき、選択中なら確率表示を更新し、誰の協力かを伝える。</summary>
    private void OnCoopChanged()
    {
        if (_overlay == null || !_overlay.activeSelf || _battleEnded || _busy) return;
        if (GroupSession.CoopProbBonus <= 0) return;

        if (_selectPanel != null && _selectPanel.activeSelf)
            RebuildDiceGrid(); // ダイス選択中: 各ダイスの成功確率表示を更新
        _phaseText.text = $"{GroupSession.CoopGranter}の協力！ 成功確率＋{GroupSession.CoopProbBonus}";
    }

    /// <summary>グループの参戦状況が変わったら、選択フェーズ中ならダイスの有効/無効を更新する。</summary>
    private void OnGroupChanged()
    {
        if (_overlay == null || !_overlay.activeSelf || _battleEnded) return;

        // 全員が振り終えたら勝敗判定（各クライアントで独立に実行）
        if (GroupSession.Active != null && GroupSession.AllRolled())
        {
            ResolveBattleAsync().Forget();
            return;
        }

        if (_selectPanel != null && _selectPanel.activeSelf)
            RefreshReadyState();
        else if (_waitingForRolls)
            _phaseText.text = $"仲間の攻撃を待っています…（{GroupSession.MembersRolledCount()}/{GroupSession.MemberCount()}人）";
    }

    /// <summary>グループの他メンバーがボスに与えたダメージを自分のボスにも反映（みんなで1体を倒す）。</summary>
    private void OnRemoteBossDamage(int dmg)
    {
        // 戦闘ウィンドウを開いている間だけ反映（参戦していない人は無視）
        if (_overlay == null || !_overlay.activeSelf || _battleEnded) return;
        ApplyDamageAsync(dmg).Forget();
    }

    private void Start()
    {
        var canvasGO = GameObject.Find("Canvas");
        _canvas = canvasGO != null ? canvasGO.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (_canvas == null) return;

        BuildUI();
        HookBattleButton();
    }

    /// <summary>戦闘ボタンを「ボス戦を開く」に差し替える（DiceRollController と同じ方式）。</summary>
    private void HookBattleButton()
    {
        var battleBtn = Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(g => g.name == "Button_Battle" && g.scene.IsValid());
        if (battleBtn == null)
        {
            Debug.LogWarning("[Boss] Button_Battle が見つかりません");
            return;
        }
        var b = battleBtn.GetComponent<Button>();
        if (b == null) return;

        // インスペクタ設定済みのonClick（バッグを開く等）を無効化してから差し替える
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            b.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(OpenBattle);
    }

    // ================================================================
    // 参加者・ボスHP計算
    // ================================================================

    /// <summary>自分の選択中キャラクターのレベル（確率計算用）。</summary>
    private static int SelfLevel()
    {
        var m = UserDataManager.instance;
        var cs = m?.UserData?.Characters;
        int idx = m != null ? m.CurrentSelectCharacterNumber : 0;
        if (cs != null && idx < cs.Count) return cs[idx].Level;
        return 1;
    }

    /// <summary>自分の選択中キャラクターの名前（協力スキルの発動者表示用）。</summary>
    private static string SelfName()
    {
        var m = UserDataManager.instance;
        var cs = m?.UserData?.Characters;
        int idx = m != null ? m.CurrentSelectCharacterNumber : 0;
        if (cs != null && idx < cs.Count && !string.IsNullOrEmpty(cs[idx].Name))
            return cs[idx].Name;
        return "ななしさん";
    }

    /// <summary>自分の選択中キャラクターの職業と属性（ダイス演出用）。</summary>
    private static (string job, string element) SelfJobElement()
    {
        var m = UserDataManager.instance;
        var cs = m?.UserData?.Characters;
        int idx = m != null ? m.CurrentSelectCharacterNumber : 0;
        if (cs != null && idx < cs.Count)
        {
            var c = cs[idx];
            return (string.IsNullOrEmpty(c.Job) ? "warrior" : c.Job,
                    string.IsNullOrEmpty(c.Element) ? "fire" : c.Element);
        }
        return ("warrior", "fire");
    }

    /// <summary>
    /// 自分の選択中キャラの装備由来ステータスを返す（ローカルキャッシュから同期取得）。
    ///  - critRate: クリティカル率(%) デフォルト5＋装備 CriticalRateUp
    ///  - critDamage: クリティカル時の与ダメージ加算 デフォルト1＋装備 CriticalDamageUp
    ///  - probBonus: ボス戦の成功確率(%)への加算 装備 ProbUp（デフォルト0）
    /// </summary>
    private static (int critRate, int critDamage, int probBonus) SelfEquipStats()
    {
        int rate = 5, dmg = 1, prob = 0; // デフォルト値

        var m = UserDataManager.instance;
        var cs = m?.UserData?.Characters;
        int idx = m != null ? m.CurrentSelectCharacterNumber : 0;
        var cache = ItemCacheManager.instance;
        if (cs == null || idx >= cs.Count || cache == null)
            return (rate, dmg, prob);

        foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
        {
            string itemId = LocalEquipSave.Load(idx, slot);
            if (string.IsNullOrEmpty(itemId)) continue;
            var ci = cache.FindById(itemId);
            if (ci?.effects == null) continue;
            foreach (var fx in ci.effects)
            {
                if (!System.Enum.TryParse(fx.effectType, out EffectType type)) continue;
                switch (type)
                {
                    case EffectType.CriticalRateUp:   rate += fx.value; break;
                    case EffectType.CriticalDamageUp: dmg  += fx.value; break;
                    case EffectType.ProbUp:           prob += fx.value; break;
                }
            }
        }
        return (rate, dmg, prob);
    }

    /// <summary>装備中スキルブック(A/B)の skill_id を解決して _battleSkills に積む。</summary>
    private void ResolveBattleSkills()
    {
        _battleSkills.Clear();

        var m = UserDataManager.instance;
        var cs = m?.UserData?.Characters;
        int idx = m != null ? m.CurrentSelectCharacterNumber : 0;
        var cache = ItemCacheManager.instance;
        if (cs == null || idx >= cs.Count || cache == null) return;

        foreach (var slot in new[] { EquipmentSlot.SkillBookA, EquipmentSlot.SkillBookB })
        {
            string itemId = LocalEquipSave.Load(idx, slot);
            if (string.IsNullOrEmpty(itemId)) continue;
            var ci = cache.FindById(itemId);
            var def = BattleSkillRegistry.Get(ci?.skill_id);
            if (def != null) _battleSkills.Add(def);
        }
    }

    /// <summary>未使用の「手動発動」スキル（StatBuff／協力）があれば最初の1つを返す。ボタン表示用。</summary>
    private BattleSkillDef NextUnusedSkill()
        => _battleSkills.FirstOrDefault(s =>
            (s.Kind == SkillKind.StatBuff || s.Kind == SkillKind.CoopProbBuff)
            && !_usedSkills.Contains(s.Id));

    /// <summary>未使用の「自動発動」スキル（D100Subtract系）があれば最初の1つを返す。</summary>
    private BattleSkillDef NextAutoSubtractSkill()
        => _battleSkills.FirstOrDefault(s => s.Kind == SkillKind.D100Subtract && !_usedSkills.Contains(s.Id));

    /// <summary>
    /// 手札のバフカードを1枚消費して、この戦闘だけのバフを適用する。
    /// 効果は装備ProbUp(_equipProbBonus)・カード専用フィールド・協力バフ(全員)に反映する。
    /// </summary>
    private void ApplyHeldBuffCard()
    {
        // カード用フィールドをリセット
        _cardName = null;
        _cardAtkBonus = 0;
        _cardAtkBonusOnSuccess = 0;
        _cardDiceLabels = null;
        _cardDiceBonus = 0;

        if (!BuffCard.HasHeld || BuffCard.Held == null) return;
        var card = BuffCard.Held.Value;
        BuffCard.ClearHeld(); // 1枚消費

        _cardName = BuffCard.DisplayName(card);
        switch (card)
        {
            case BuffCardType.SoftDrink: // 1D8・2D6・2D8 の確率+5
                _cardDiceLabels = new HashSet<string> { "1D8", "2D6", "2D8" };
                _cardDiceBonus = 5;
                break;
            case BuffCardType.Beer:      // 1D10を振り、出目ぶん全ダイス確率+
                _equipProbBonus += Random.Range(1, 11);
                break;
            case BuffCardType.OtherDrink: // 1D6を振り、出目ぶん全ダイス確率+／攻撃力+1
                _equipProbBonus += Random.Range(1, 7);
                _cardAtkBonus += 1;
                break;
            case BuffCardType.SideDish:  // 全ダイス確率+5
                _equipProbBonus += 5;
                break;
            case BuffCardType.StapleFood: // 1D100成功時 攻撃力+1
                _cardAtkBonusOnSuccess += 1;
                break;
            case BuffCardType.Sweets:    // 自分含む全員 確率+5（協力バフ機構を流用）
                GroupSession.BroadcastCoopBuff(5, SelfName());
                break;
            case BuffCardType.SP1:
            case BuffCardType.SP2: // 1D10を振り、出目ぶん全ダイス確率+／攻撃力+1
                _equipProbBonus += Random.Range(1, 11);
                _cardAtkBonus += 1;
                break;
        }
    }

    /// <summary>所持品（アカウント共有）にあるアクティブスキル本（skill_id付き）の冊数を数える（item_id重複は1冊）。</summary>
    private static int CountOwnedActiveSkillBooks()
    {
        var inv = UserDataManager.instance?.UserData?.Inventory;
        var cache = ItemCacheManager.instance;
        if (inv == null || cache == null) return 0;

        var seen = new HashSet<string>();
        foreach (var r in inv)
        {
            if (r == null || string.IsNullOrEmpty(r.ItemId) || seen.Contains(r.ItemId)) continue;
            var ci = cache.FindById(r.ItemId);
            if (BattleSkillRegistry.Get(ci?.skill_id) != null) seen.Add(r.ItemId);
        }
        return seen.Count;
    }

    /// <summary>
    /// 本日のお得職業・属性に一致するパーティメンバーがいれば、成功確率の上昇バフ(%)を返す。
    ///  - お得「職業」と一致するメンバーがいる            → +5%
    ///  - お得「職業」と「属性」の両方が一致するメンバーがいる → +10%
    /// バフはパーティ全員に適用される（誰か1人が条件を満たせば全員が恩恵を受ける）ため、
    /// 全メンバーを走査して一番高いバフを採用する（複数人満たしても重複加算はしない）。
    /// グループ未参加なら自分1人（選択中キャラ）で判定する。
    /// </summary>
    private static int LuckyJobBonus()
    {
        var today = HomeSceneInitializer.TodayData;
        if (today == null || string.IsNullOrEmpty(today.Job)) return 0;

        int best = 0;
        if (GroupSession.Active != null && GroupSession.Members.Count > 0)
        {
            foreach (var m in GroupSession.Members.Values)
                best = Mathf.Max(best, MemberLuckyBonus(m.Job, m.Element, today.Job, today.Element));
        }
        else
        {
            var mgr = UserDataManager.instance;
            var cs = mgr?.UserData?.Characters;
            int idx = mgr != null ? mgr.CurrentSelectCharacterNumber : 0;
            if (cs != null && idx < cs.Count)
                best = MemberLuckyBonus(cs[idx].Job, cs[idx].Element, today.Job, today.Element);
        }
        return best;
    }

    /// <summary>1メンバー分のお得バフ判定: 職業＋属性一致=10 / 職業のみ一致=5 / 不一致=0。</summary>
    private static int MemberLuckyBonus(string job, string element, string todayJob, string todayElement)
    {
        if (string.IsNullOrEmpty(job) || job != todayJob) return 0;
        if (!string.IsNullOrEmpty(todayElement) && element == todayElement) return 10;
        return 5;
    }

    /// <summary>参加人数と参加者中の最大レベル（グループ未参加なら自分1人）。</summary>
    private static (int count, int maxLevel) Participants()
    {
        if (GroupSession.Active != null && GroupSession.Members.Count > 0)
        {
            int c = GroupSession.Members.Count;
            int mx = GroupSession.Members.Values.Max(v => v.Level);
            return (c, mx);
        }
        return (1, SelfLevel());
    }

    /// <summary>参加状況から最大HPを計算してHPをリセットする。</summary>
    private void ResetBoss()
    {
        var (count, maxLv) = Participants();
        int correction = maxLv >= 101 ? 3 : maxLv >= 51 ? 2 : 1;
        _maxHp = count * 4 + correction;
        _hp = _maxHp;
        UpdateHpBar();
    }

    private void UpdateHpBar()
    {
        float ratio = _maxHp > 0 ? Mathf.Clamp01((float)_hp / _maxHp) : 0f;
        if (_hpFillRt != null) _hpFillRt.sizeDelta = new Vector2(820f * ratio, 44f);
        _hpText.text = $"{Mathf.Max(0, _hp)} / {_maxHp}"; // 表示は0で止める（内部はマイナス可）
    }

    // ================================================================
    // 開閉
    // ================================================================
    private void OpenBattle()
    {
        _overlay.SetActive(true);
        _battleEnded = false;
        _waitingForRolls = false;
        if (_closeButton != null) _closeButton.SetActive(true); // 新しい戦闘では閉じられる
        GroupSession.SetInBattle(true);   // 自分の参戦を仲間へ通知
        GroupSession.SetHasRolled(false); // 新しい戦闘なのでターン状態をリセット

        // 装備由来ステータスを取得してキャッシュ（成功確率・クリ判定で使う）
        (_equipCritRate, _equipCritDamage, _equipProbBonus) = SelfEquipStats();

        // 装備中スキルブックの任意発動スキルを解決（1戦闘につき各1回まで）
        ResolveBattleSkills();
        _usedSkills.Clear();
        _coopAtkPenalty = 0;
        GroupSession.ResetCoopBuff(); // 協力バフは戦闘ごとにリセット

        // エンジンビルド系: 所持アクティブスキル本の数だけ成功確率を底上げ（自動・常時）
        foreach (var s in _battleSkills.Where(s => s.Kind == SkillKind.ProbPerOwnedSkillBook))
            _equipProbBonus += CountOwnedActiveSkillBooks() * s.Value;

        // ウイングスパン系: クリティカル率を倍にする（装備＋一時バフの合計に掛かる）
        _critRateMultiplier = 1;
        foreach (var s in _battleSkills.Where(s => s.Kind == SkillKind.CritRateMultiplier))
            _critRateMultiplier *= s.Value;

        // 連勝ボーナス: 現在の連勝数ぶん成功確率を底上げ（連勝数 × Value）
        foreach (var s in _battleSkills.Where(s => s.Kind == SkillKind.WinStreakBonus))
            _equipProbBonus += BattleStreak.Current * s.Value;

        // バフカード（手札を1枚消費して戦闘1回限りのバフを適用）
        ApplyHeldBuffCard();

        // 今日お得な属性に対し、プレイヤーが相性有利になる属性をボスに設定する
        string today = HomeSceneInitializer.TodayData?.Element ?? "fire";
        _weakness = today;                        // ボスの弱点 = 今日お得な属性
        _bossElement = BossElementFromToday(today);
        _bossImage.sprite = BossPortrait.Get(_bossElement);
        _bossImage.color = Color.white;

        ResetBoss();
        _bossName.text = $"{ElementJp(_bossElement)}のボスモンスター";
        _weakText.text = $"弱点属性 → {ElementJp(_weakness)}（その属性で攻撃するとダメージ＋1）";
        ShowSelectPhase(string.IsNullOrEmpty(_cardName)
            ? "どのダイスで挑む？"
            : $"🍴 {_cardName}カード発動中！ どのダイスで挑む？");
    }

    /// <summary>今日お得な属性に対し、プレイヤーが相性有利になる属性をボスへ割り当てる。</summary>
    private static string BossElementFromToday(string today) => today switch
    {
        "fire"    => "nature",
        "water"   => "fire",
        "nature"  => "thunder",
        "thunder" => "water",
        _         => "nature",
    };

    private static string ElementJp(string el) => el switch
    {
        "fire" => "炎", "water" => "水", "nature" => "自然", "thunder" => "雷", _ => "—",
    };

    private void Hide()
    {
        if (_busy) return;
        GroupSession.SetInBattle(false); // 戦闘から離脱したことを仲間へ通知
        _overlay.SetActive(false);
        if (_sim != null) _sim.SetVisible(false);
    }

    /// <summary>ダイス選択フェーズへ戻す（ボス表示・ダイスビュー非表示・アクションボタン非表示）。</summary>
    private void ShowSelectPhase(string phaseMsg)
    {
        if (_sim != null) _sim.SetVisible(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(false);
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(true);
        HideActionButtons();
        RebuildDiceGrid();
        _selectMsg = phaseMsg;
        _selectPanel.SetActive(true);
        RefreshReadyState();
    }

    /// <summary>グループ全員が参戦するまでダイス選択を無効化し、待機メッセージを出す。</summary>
    private void RefreshReadyState()
    {
        if (_overlay == null || !_overlay.activeSelf || _selectPanel == null || !_selectPanel.activeSelf)
            return;
        bool canRoll = GroupSession.Active == null || GroupSession.AllMembersInBattle();
        SetDiceButtonsInteractable(canRoll);
        _phaseText.text = canRoll
            ? _selectMsg
            : $"仲間の参戦を待っています…（{GroupSession.MembersInBattleCount()}/{GroupSession.MemberCount()}人）";
    }

    private void SetDiceButtonsInteractable(bool on)
    {
        if (_diceGrid == null) return;
        foreach (var b in _diceGrid.GetComponentsInChildren<Button>())
            b.interactable = on;
    }

    private void HideActionButtons()
    {
        if (_actionButton != null) _actionButton.SetActive(false);
        if (_cancelButton != null) _cancelButton.SetActive(false);
        if (_skillButton != null) _skillButton.SetActive(false);
    }

    /// <summary>「振る」ボタンのラベルとコールバックを差し替えて表示する。</summary>
    private void SetAction(string text, UnityEngine.Events.UnityAction cb, bool showCancel)
    {
        _actionLabel.text = text;
        var btn = _actionButton.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(cb);
        _actionButton.SetActive(true);
        if (_cancelButton != null) _cancelButton.SetActive(showCancel);
    }

    // ================================================================
    // ダイス選択
    // ================================================================
    private void RebuildDiceGrid()
    {
        for (int i = _diceGrid.childCount - 1; i >= 0; i--)
            Destroy(_diceGrid.GetChild(i).gameObject);

        var jp = GetJpFont();
        int lv = SelfLevel();
        int luckyBonus = LuckyJobBonus(); // 本日のお得職業/属性が一致するメンバーがいれば全員にバフ
        var available = DICE.Where(d => lv >= d.reqLv).ToList();

        const int COLS = 2;
        const float W = 410, H = 150, GAP_X = 24, GAP_Y = 22;
        for (int i = 0; i < available.Count; i++)
        {
            var dice = available[i];
            // 指定ダイスのみ効くカードバフ（ソフトドリンク）
            int perDice = (_cardDiceLabels != null && _cardDiceLabels.Contains(dice.label)) ? _cardDiceBonus : 0;
            int bonus = luckyBonus + _equipProbBonus + perDice; // お得バフ＋装備ProbUp＋カード（_pendingに保持する基礎側）
            int prob = Mathf.Clamp(dice.probBase + lv + bonus, 0, PROB_MAX);
            int coop = GroupSession.CoopProbBonus; // 協力バフは別枠（判定時に加算）
            int shown = Mathf.Clamp(prob + coop, 0, PROB_MAX);
            int col = i % COLS, row = i / COLS;
            float x = (col - (COLS - 1) / 2f) * (W + GAP_X);
            float y = 130 - row * (H + GAP_Y);

            // _pending に渡すのは協力ぶんを含まない prob（協力は判定時に最新値で加算）
            var btn = MakeButton($"__Dice_{dice.label}", _diceGrid, C_DICE_BTN, dice.label, jp, 58, C_DICE_TXT,
                W, H, new Vector2(x, y), () => OnSelectDice(dice.label, dice.count, dice.faces, prob));

            string probLabel = $"成功 {shown}%";
            if (bonus > 0) probLabel += $"（補正+{bonus}）";
            if (coop > 0) probLabel += $"（{GroupSession.CoopGranter}の協力+{coop}）";
            MakeLabel(btn.transform, probLabel, jp, 30, FontStyles.Bold, C_PROB_TXT,
                W, 40, new Vector2(0, -48));
        }
    }

    /// <summary>ダイスを選ぶと「振る」待ちになる（ここではまだ振らない）。</summary>
    private void OnSelectDice(string label, int count, int faces, int prob)
    {
        if (_busy) return;
        _pending = (label, count, faces, prob);
        _skillCritRate = 0; _skillCritDamage = 0; _armedSubtractFaces = 0; // 挑戦用の一時バフをリセット
        _selectPanel.SetActive(false);
        int shown = Mathf.Clamp(prob + GroupSession.CoopProbBonus, 0, PROB_MAX);
        _phaseText.text = $"{label}（成功 {shown}%）で挑む！";
        SetAction("1D100を振る", OnRollD100, showCancel: true);
        RefreshSkillButton();
    }

    /// <summary>未使用の発動スキルがあれば「📖 スキル発動」ボタンを出す（なければ隠す）。</summary>
    private void RefreshSkillButton()
    {
        if (_skillButton == null) return;
        var next = NextUnusedSkill();
        if (next == null || _busy) { _skillButton.SetActive(false); return; }
        _skillLabel.text = $"📖 {next.Name}（{next.ShortHint}）";
        _skillButton.SetActive(true);
    }

    private void OnActivateSkill() { if (!_busy) ActivateSkillAsync().Forget(); }

    /// <summary>スキルのダイスを振り、当たり目なら効果を発動する（1戦闘につき各1回）。</summary>
    private async UniTask ActivateSkillAsync()
    {
        var def = NextUnusedSkill();
        if (def == null) return;

        _usedSkills.Add(def.Id);

        // 協力スキル: ダイスは振らず、自分のATK-1と引き換えに味方全員へ確率バフを配る
        if (def.Kind == SkillKind.CoopProbBuff)
        {
            _coopAtkPenalty += 1;
            string myName = SelfName();
            GroupSession.BroadcastCoopBuff(def.Value, myName);
            _phaseText.text = $"協力スキル発動！ 味方全員の成功確率＋{def.Value}（{myName}の攻撃力−1）";
            SetAction("1D100を振る", OnRollD100, showCancel: true);
            RefreshSkillButton();
            return;
        }

        _busy = true;
        HideActionButtons();

        if (_bossVisual != null) _bossVisual.gameObject.SetActive(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(true);
        if (_sim == null) _sim = Dice3DSimulator.Create(GetJpFont());
        _diceView.texture = _sim.Texture;
        var (_, element) = SelfJobElement();
        _sim.SetDiceElement(element);

        _phaseText.text = $"スキル「{def.Name}」発動！ 1D{def.DiceFaces} …";
        var rolls = await _sim.RollAsync(1, def.DiceFaces);
        int eye = rolls.Count > 0 ? rolls[0] : 0;
        bool win = def.WinFaces.Contains(eye);

        if (win) ApplySkillEffect(def);
        _phaseText.text = win
            ? $"スキル成功！（{def.DiceFaces}面→{eye}）{SkillEffectText(def)}"
            : $"スキル不発…（{def.DiceFaces}面→{eye}）";

        await UniTask.Delay(800);
        if (_sim != null) _sim.SetVisible(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(false);
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(true);

        _busy = false;
        // 1D100へ。残りの未使用スキルがあれば続けて発動可能。
        _phaseText.text = $"{_pending.label}（成功 {_pending.prob}%）で挑む！";
        SetAction("1D100を振る", OnRollD100, showCancel: true);
        RefreshSkillButton();
    }

    /// <summary>スキル成功時の効果を今回の挑戦に適用する。</summary>
    private void ApplySkillEffect(BattleSkillDef def)
    {
        switch (def.Effect)
        {
            case EffectType.ProbUp:
                int p = Mathf.Clamp(_pending.prob + def.Value, 0, PROB_MAX);
                _pending = (_pending.label, _pending.count, _pending.faces, p);
                break;
            case EffectType.CriticalRateUp:   _skillCritRate   += def.Value; break;
            case EffectType.CriticalDamageUp: _skillCritDamage += def.Value; break;
        }
    }

    private string SkillEffectText(BattleSkillDef def)
    {
        switch (def.Effect)
        {
            case EffectType.ProbUp:           return $"成功確率＋{def.Value} → {_pending.prob}%";
            case EffectType.CriticalRateUp:   return $"クリティカル率＋{def.Value}";
            case EffectType.CriticalDamageUp: return $"クリティカルダメージ＋{def.Value}";
            default:                          return "";
        }
    }

    private void OnRollD100() { if (!_busy) RollD100Async().Forget(); }
    private void OnRollAttack() { if (!_busy) RollAttackAsync().Forget(); }

    private void OnCancel()
    {
        if (_busy) return;
        ShowSelectPhase("どのダイスで挑む？");
    }

    // ================================================================
    // バトル本体（「振る」ボタン起点。1D100判定 → 攻撃ダイス → ダメージ）
    // ================================================================

    /// <summary>1D100を振って成功/失敗・クリ/ファンブルを判定する。</summary>
    private async UniTask RollD100Async()
    {
        // グループ中は全員が参戦している間だけ振れる
        if (GroupSession.Active != null && !GroupSession.AllMembersInBattle())
        {
            _phaseText.text = "仲間が戦闘から離れました。全員の参戦を待っています…";
            return;
        }

        _busy = true;
        HideActionButtons();
        // ダイスを振り始めたら以降は閉じられないように×ボタンを隠す
        if (_closeButton != null) _closeButton.SetActive(false);
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(true);

        if (_sim == null) _sim = Dice3DSimulator.Create(GetJpFont());
        _diceView.texture = _sim.Texture;
        var (_, element) = SelfJobElement();
        _sim.SetDiceElement(element);

        // 自動発動の1D100減算スキル（装填不要。1戦闘1回）を消費して装填する
        var autoSub = NextAutoSubtractSkill();
        if (autoSub != null) { _armedSubtractFaces = autoSub.DiceFaces; _usedSkills.Add(autoSub.Id); }

        _phaseText.text = "運命の 1D100 …";
        int d100 = await _sim.RollPercentileAsync(); // 十の位(00..90)＋一の位(0..9)

        // スキル発動中なら、一緒に指定面ダイスを振って出目を差し引く（低いほど有利）
        int rawD100 = d100;
        if (_armedSubtractFaces > 0)
        {
            _phaseText.text = $"1D100 → {rawD100}　{_armedSubtractFaces}面ダイスも振る…";
            await UniTask.Delay(400);
            var subRolls = await _sim.RollAsync(1, _armedSubtractFaces);
            int sub = subRolls.Count > 0 ? subRolls[0] : 0;
            d100 = Mathf.Max(1, rawD100 - sub);
            _phaseText.text = $"1D100 {rawD100} − {_armedSubtractFaces}面 {sub} → {d100}";
            await UniTask.Delay(700);
            _armedSubtractFaces = 0;
        }

        // 協力バフ（味方全員に乗る成功確率）を最新値で加算して判定
        int successProb = Mathf.Clamp(_pending.prob + GroupSession.CoopProbBonus, 0, PROB_MAX);
        _d100Success = d100 <= successProb;
        // 出目がクリティカル率以下ならクリティカル判定（(装備＋一時バフ)×倍率）
        _isCritical = d100 <= (_equipCritRate + _skillCritRate) * _critRateMultiplier;
        bool fumble = !_isCritical && d100 >= 96;
        _critBonus  = _isCritical ? _equipCritDamage + _skillCritDamage : 0;
        _d100AtkMod = fumble ? -1 : 0;

        string judge = _isCritical ? "クリティカル！！" : fumble ? "ファンブル…"
                     : _d100Success ? "成功！" : "失敗…";
        _phaseText.text = $"1D100 → {d100}　{judge}";

        _busy = false;
        SetAction(_d100Success ? $"{_pending.label} を振る" : "1D4 を振る（失敗）",
                  OnRollAttack, showCancel: false);
    }

    /// <summary>攻撃ダイス（成功＝選択ダイス／失敗＝1D4）を振り、ボスにダメージを与える。</summary>
    private async UniTask RollAttackAsync()
    {
        _busy = true;
        HideActionButtons();
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(true);

        var (job, element) = SelfJobElement();
        int rc = _d100Success ? _pending.count : 1;
        int rf = _d100Success ? _pending.faces : 4;
        _phaseText.text = _d100Success ? $"{_pending.label} を振る！" : "1D4 を振る…";

        var rolls = await _sim.RollAsync(rc, rf);
        int rollSum = rolls.Sum();

        // 攻撃力＝出目 ＋ クリティカルダメージ ＋ ファンブル補正 ＋ ATKクーポン − 協力ペナルティ ＋ カードバフ
        int atkCoupon = DiceAtkBonus.Consume();
        int cardAtk = _cardAtkBonus + (_d100Success ? _cardAtkBonusOnSuccess : 0);
        int atk = Mathf.Max(0, rollSum + _critBonus + _d100AtkMod + atkCoupon - _coopAtkPenalty + cardAtk);

        // 弱点属性（=今日お得な属性）で攻撃するとダメージ＋1
        bool weakHit = !string.IsNullOrEmpty(element) && element == _weakness;
        if (weakHit) atk += 1;

        float denom = rc * rf - rc;
        float intensity = denom > 0 ? (rollSum - rc) / denom : 1f;
        _sim.PlayResultEffect(job, element, intensity);

        string detail = $"出目 {rollSum}";
        if (_isCritical) detail += $" ＋クリ{_critBonus}";
        else if (_d100AtkMod < 0) detail += $" {_d100AtkMod}";
        if (atkCoupon > 0) detail += $" ＋ATK{atkCoupon}";
        if (_coopAtkPenalty > 0) detail += $" −協力{_coopAtkPenalty}";
        if (cardAtk > 0) detail += $" ＋カード{cardAtk}";
        if (weakHit) detail += " ＋弱点1";
        _phaseText.text = weakHit
            ? $"弱点を突いた！こうげき {atk}！（{detail}）"
            : $"こうげき {atk}！（{detail}）";

        LocalHistoryLog.Add("dice", $"ボス戦: {(_d100Success ? _pending.label : "1D4")} で攻撃力 {atk}");
        GroupSession.AnnounceDiceResult(atk);

        await UniTask.Delay(700);

        // ダイスビューを消してボスを見せてからダメージ
        if (_sim != null) _sim.SetVisible(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(false);
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(true);

        // グループ参加中は与ダメージを全員へ共有（みんなで1体のボスを削る）
        if (GroupSession.Active != null)
            GroupSession.BroadcastBossDamage(atk);
        await ApplyDamageAsync(atk);
        _busy = false;

        if (GroupSession.Active != null)
        {
            // 自分のターン終了を共有。全員が振り終えたら勝敗判定、まだなら待機。
            GroupSession.SetHasRolled(true);
            if (GroupSession.AllRolled())
                await ResolveBattleAsync();
            else
                ShowWaitingForRolls();
        }
        else
        {
            // ソロは自分が振った時点で判定
            await ResolveBattleAsync();
        }
    }

    /// <summary>自分は振り終え、グループの仲間が振るのを待つ表示。</summary>
    private void ShowWaitingForRolls()
    {
        _waitingForRolls = true;
        if (_sim != null) _sim.SetVisible(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(false);
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(true);
        HideActionButtons();
        if (_selectPanel != null) _selectPanel.SetActive(false);
        _phaseText.text = $"仲間の攻撃を待っています…（{GroupSession.MembersRolledCount()}/{GroupSession.MemberCount()}人）";
    }

    /// <summary>ボスにダメージを与え、HPバー更新・撃破処理を行う（自分の攻撃・グループ受信の共通処理）。</summary>
    private async UniTask ApplyDamageAsync(int dmg)
    {
        if (_battleEnded) return;
        _hp -= dmg; // オーバーダメージはマイナスまで許容（オーバーキル分はGPに還元）
        UpdateHpBar();
        await DamageFlashAsync();
    }

    /// <summary>被弾時の赤フラッシュ＋シェイク。</summary>
    private async UniTask DamageFlashAsync()
    {
        if (_bossVisual == null) return;
        var home = new Vector2(0, 330);
        if (_bossImage != null) _bossImage.color = new Color(1f, 0.45f, 0.45f);
        for (int i = 0; i < 8; i++)
        {
            _bossVisual.anchoredPosition = home + new Vector2((i % 2 == 0 ? 1 : -1) * 24f, 0);
            await UniTask.Delay(35);
        }
        _bossVisual.anchoredPosition = home;
        if (_bossImage != null) _bossImage.color = Color.white;
    }

    /// <summary>
    /// 全員が振り終えた後の勝敗判定。
    /// HP&lt;=0 なら討伐成功（オーバーキル分をGP付与＋レベルアップ＋成功モーダル）、
    /// HP&gt;0 なら討伐失敗（失敗モーダル）。最後にグループ解散＆ウィンドウを閉じる。
    /// </summary>
    private async UniTask ResolveBattleAsync()
    {
        if (_battleEnded) return;
        _battleEnded = true;
        _waitingForRolls = false;
        HideActionButtons();
        if (_selectPanel != null) _selectPanel.SetActive(false);

        bool success = _hp <= 0;
        int overkill = Mathf.Max(0, -_hp); // オーバーキル分（マイナスHPの絶対値）

        if (success)
        {
            // 獲得GP＝オーバーキル分＋スキルパッシブ（城壁都市B等のオーバーキルGPボーナス）
            int gp = overkill;
            foreach (var s in _battleSkills.Where(s => s.Kind == SkillKind.OverkillGpBonus))
                if (overkill >= s.DiceFaces) gp += s.Value;

            _phaseText.text = "★ ボスモンスターを討伐した！ ★";
            AssetsDatabase.instance?.PlayLevelUpSE();
            LocalHistoryLog.Add("dice", gp > 0 ? $"ボス討伐成功（GP+{gp}）" : "ボス討伐成功");

            // 撃破フェード
            if (_bossImage != null)
            {
                for (int i = 0; i < 12; i++)
                {
                    _bossImage.color = new Color(1f, 0.9f, 0.7f, 1f - i / 12f);
                    await UniTask.Delay(60);
                }
                _bossImage.color = Color.white;
            }

            // 連勝数を先に確定（連勝ボーナスのレベル加算判定に使う）
            int streak = BattleStreak.RecordWin();
            if (streak >= 2) LocalHistoryLog.Add("dice", $"{streak}連勝中！");

            // 連勝ボーナス: N連勝ごとに討伐時のレベルアップを＋1
            int extraLevels = 0;
            foreach (var s in _battleSkills.Where(s => s.Kind == SkillKind.WinStreakBonus))
                if (s.DiceFaces > 0 && streak > 0 && streak % s.DiceFaces == 0) extraLevels += 1;

            // GP付与＋選択キャラのレベルアップ（1回の書き込み。通常+1＋連勝ボーナス）
            int oldLv = SelfLevel();
            await GrantRewardAsync(gp, extraLevels);
            int newLv = SelfLevel();

            string lvLine = $"レベルアップ！\nLv{oldLv} → Lv{newLv}";
            string strong = gp > 0
                ? $"GP {gp}ポイントゲット！\n{lvLine}"
                : lvLine;
            string title = streak >= 2 ? $"ボス討伐成功！　{streak}連勝中！" : "ボス討伐成功！";
            InfoModal.Show(title, strong, strongYOffset: -50f);
        }
        else
        {
            _phaseText.text = "討伐失敗…";
            BattleStreak.RecordLoss();
            LocalHistoryLog.Add("dice", "ボス討伐失敗");
            await UniTask.Delay(400);
            InfoModal.Show("討伐失敗…", "ボスを倒せなかった…");
        }

        await EndBattleAsync();
    }

    /// <summary>討伐報酬: オーバーキル分のGPと選択キャラのレベルアップを自分のドキュメントへ書き込む。</summary>
    private async UniTask GrantRewardAsync(int gp, int extraLevels = 0)
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null) return;
        int idx = manager.CurrentSelectCharacterNumber;
        var chars = manager.UserData.Characters;
        if (chars == null || idx >= chars.Count) return;
        var chara = chars[idx];
        int newLevel = chara.Level + 1 + Mathf.Max(0, extraLevels);

        try
        {
            var updates = new Dictionary<FieldPath, object>
            {
                { new FieldPath("characters", idx.ToString(), "lv"), newLevel },
                { new FieldPath("lastDate"), Timestamp.GetCurrentTimestamp() },
            };
            if (gp > 0)
                updates[new FieldPath("gp")] = FieldValue.Increment(gp);

            await UserDocRef.UpdateAsync(updates).AsUniTask();

            chara.Level = newLevel;
            if (gp > 0) manager.UserData.GP += gp;
            Debug.Log($"[Boss] 討伐報酬: Lv{newLevel} / GP+{gp}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Boss] 報酬書き込みエラー: {ex.Message}");
        }
    }

    /// <summary>グループ解散＆戦闘ウィンドウを閉じる。</summary>
    private async UniTask EndBattleAsync()
    {
        if (GroupSession.Active != null)
            await GroupSession.LeaveAsync(); // 解散（Leave直前の不要な状態共有はしない）
        if (_sim != null) _sim.SetVisible(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(false);
        HideActionButtons();
        _overlay.SetActive(false);

        // ロールでATKクーポン分を消費済みなので、アイコン右下のバッジを更新する
        var cpm = FindObjectOfType<CharacterPageManager>();
        if (cpm != null) cpm.RefreshAtkBonusBadge();
    }

    private void Update()
    {
        // ボスのアイドル浮遊（表示中のみ）
        if (_bossImageRt != null && _bossVisual != null && _bossVisual.gameObject.activeInHierarchy)
            _bossImageRt.anchoredPosition = new Vector2(0, Mathf.Sin(Time.time * 1.6f) * 12f);
    }

    // ================================================================
    // UI構築
    // ================================================================
    private void BuildUI()
    {
        var jp = GetJpFont();

        _overlay = new GameObject("__BossOverlay");
        _overlay.transform.SetParent(_canvas.transform, false);
        var ort = _overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        _overlay.AddComponent<Image>().color = C_OVERLAY;
        _overlay.SetActive(false);

        // 閉じる
        _closeButton = MakeButton("__BossClose", _overlay.transform, C_CLOSE_BTN, "✕", jp, 48, Color.white,
            96, 96, new Vector2(410, 870), Hide);

        // ボス名
        _bossName = MakeLabel(_overlay.transform, "ボスモンスター", jp, 52, FontStyles.Bold, C_TITLE,
            900, 80, new Vector2(0, -738)).GetComponent<TextMeshProUGUI>();
        _weakText = MakeLabel(_overlay.transform, "", jp, 30, FontStyles.Bold, C_PROB_TXT,
            900, 46, new Vector2(0, -792)).GetComponent<TextMeshProUGUI>();

        // HPバー（枠 → 背景 → fill → 数値）
        MakeRect("__HpBorder", _overlay.transform, C_BORDER, 832, 56)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -850);
        MakeRect("__HpBg", _overlay.transform, C_HP_BG, 820, 44)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -850);
        var fillGO = MakeRect("__HpFill", _overlay.transform, C_HP_FILL, 820, 44);
        _hpFillRt = fillGO.GetComponent<RectTransform>();
        _hpFillRt.pivot = new Vector2(0f, 0.5f);               // 左端基準で伸縮
        _hpFillRt.anchoredPosition = new Vector2(-410f, -850f); // 背景の左端に合わせる
        _hpFillRt.sizeDelta = new Vector2(820f, 44f);
        _hpText = MakeLabel(_overlay.transform, "", jp, 30, FontStyles.Bold, Color.white,
            820, 44, new Vector2(0, -850)).GetComponent<TextMeshProUGUI>();

        // ボスのビジュアル（プロシージャル生成のボス絵）
        BuildBossVisual();

        // 3Dダイスの映像（攻撃時のみ表示）
        _diceViewBg = MakeRect("__BossDiceViewBg", _overlay.transform, new Color(0.03f, 0.02f, 0.05f), 720, 620);
        _diceViewBg.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 300);
        var viewGO = new GameObject("__BossDiceView");
        viewGO.transform.SetParent(_diceViewBg.transform, false);
        viewGO.AddComponent<RectTransform>().sizeDelta = new Vector2(710, 610);
        _diceView = viewGO.AddComponent<RawImage>();
        _diceViewBg.SetActive(false);

        // フェーズ表示（判定結果・攻撃力など）
        _phaseText = MakeLabel(_overlay.transform, "", jp, 44, FontStyles.Bold, C_PHASE,
            960, 80, new Vector2(0, -110)).GetComponent<TextMeshProUGUI>();

        // 「振る」アクションボタン（ダイス選択後に表示。任意タイミングで振る）
        _actionButton = MakeButton("__ActionBtn", _overlay.transform, C_BORDER, "", jp, 46,
            new Color(0.16f, 0.09f, 0.03f), 520, 132, new Vector2(0, -360), () => { });
        _actionLabel = _actionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        _actionButton.SetActive(false);

        // 「スキル発動」ボタン（ダイス選択後・1D100の前。任意発動。アクションボタンの上）
        _skillButton = MakeButton("__SkillBtn", _overlay.transform, new Color(0.40f, 0.20f, 0.52f), "", jp, 34,
            Color.white, 560, 92, new Vector2(0, -232), OnActivateSkill);
        _skillLabel = _skillButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        _skillButton.SetActive(false);

        _cancelButton = MakeButton("__CancelBtn", _overlay.transform, C_MUTED, "選び直す", jp, 34,
            Color.white, 300, 84, new Vector2(0, -520), OnCancel);
        _cancelButton.SetActive(false);

        // ダイス選択パネル（下部）
        _selectPanel = new GameObject("__BossSelectPanel");
        _selectPanel.transform.SetParent(_overlay.transform, false);
        var spRt = _selectPanel.AddComponent<RectTransform>();
        spRt.sizeDelta = new Vector2(900, 420);
        spRt.anchoredPosition = new Vector2(0, -340);

        var grid = new GameObject("__BossDiceGrid");
        grid.transform.SetParent(_selectPanel.transform, false);
        var grt = grid.AddComponent<RectTransform>();
        grt.sizeDelta = new Vector2(880, 360);
        grt.anchoredPosition = new Vector2(0, -40);
        _diceGrid = grid.transform;

        _selectPanel.SetActive(false);
    }

    /// <summary>プロシージャル生成のボス絵（炎を纏う角の魔王）。親=シェイク用 / 子=浮遊用。</summary>
    private void BuildBossVisual()
    {
        var go = new GameObject("__BossVisual");
        go.transform.SetParent(_overlay.transform, false);
        _bossVisual = go.AddComponent<RectTransform>();
        _bossVisual.sizeDelta = new Vector2(540, 590);
        _bossVisual.anchoredPosition = new Vector2(0, 330);

        var imgGO = new GameObject("__BossImage");
        imgGO.transform.SetParent(go.transform, false);
        _bossImageRt = imgGO.AddComponent<RectTransform>();
        _bossImageRt.sizeDelta = new Vector2(540, 590);
        _bossImage = imgGO.AddComponent<Image>();
        _bossImage.preserveAspect = true;
        _bossImage.raycastTarget = false;
        // スプライト（属性別の絵）は OpenBattle で設定する
    }

    // ================================================================
    // UI部品ヘルパー
    // ================================================================
    private static TMP_FontAsset GetJpFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        return fonts.FirstOrDefault(f => f.name.ToLower() == "jp") ?? fonts.FirstOrDefault();
    }

    private static GameObject MakeRect(string name, Transform parent, Color color, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        go.AddComponent<Image>().color = color;
        return go;
    }

    private static GameObject MakeLabel(Transform parent, string text, TMP_FontAsset font,
        float size, FontStyles style, Color color, float w, float h, Vector2 pos)
    {
        var go = new GameObject("__Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true; // 横幅(w)を超えたら改行して画面外に出ないようにする
        tmp.raycastTarget = false;
        return go;
    }

    private static GameObject MakeButton(string name, Transform parent, Color bg, string text,
        TMP_FontAsset font, float fontSize, Color textColor, float w, float h, Vector2 pos,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeRect(name, parent, bg, w, h);
        go.GetComponent<RectTransform>().anchoredPosition = pos;
        MakeLabel(go.transform, text, font, fontSize, FontStyles.Bold, textColor, w, h, Vector2.zero);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
        return go;
    }
}
