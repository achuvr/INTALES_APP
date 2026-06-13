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
///  3. 1D100 を振り、成功確率以下なら成功・超えたら失敗。
///     出目1〜5でクリティカル（攻撃力+1）、96〜100でファンブル（攻撃力−1）。
///  4. 成功なら選んだダイス、失敗なら必ず 1D4 を振る。
///     その出目（＋クリ/ファンブル補正＋ATKクーポン）がプレイヤーの攻撃力。
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
    private GameObject _cancelButton;    // 「選び直す」

    // 選択中ダイス（「振る」待ち）
    private (string label, int count, int faces, int prob) _pending;
    // 1D100の判定結果（攻撃ダイス「振る」待ち）
    private bool _d100Success;
    private int _d100AtkMod;

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
    }
    private void OnDisable()
    {
        GroupSession.BossDamaged -= OnRemoteBossDamage;
        GroupSession.Changed -= OnGroupChanged;
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
        GroupSession.SetInBattle(true);   // 自分の参戦を仲間へ通知
        GroupSession.SetHasRolled(false); // 新しい戦闘なのでターン状態をリセット

        // 今日お得な属性に対し、プレイヤーが相性有利になる属性をボスに設定する
        string today = HomeSceneInitializer.TodayData?.Element ?? "fire";
        _weakness = today;                        // ボスの弱点 = 今日お得な属性
        _bossElement = BossElementFromToday(today);
        _bossImage.sprite = BossPortrait.Get(_bossElement);
        _bossImage.color = Color.white;

        ResetBoss();
        _bossName.text = $"{ElementJp(_bossElement)}のボスモンスター";
        _weakText.text = $"弱点属性 → {ElementJp(_weakness)}（その属性で攻撃するとダメージ＋1）";
        ShowSelectPhase("どのダイスで挑む？");
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
        var available = DICE.Where(d => lv >= d.reqLv).ToList();

        const int COLS = 2;
        const float W = 410, H = 150, GAP_X = 24, GAP_Y = 22;
        for (int i = 0; i < available.Count; i++)
        {
            var dice = available[i];
            int prob = Mathf.Clamp(dice.probBase + lv, 0, PROB_MAX);
            int col = i % COLS, row = i / COLS;
            float x = (col - (COLS - 1) / 2f) * (W + GAP_X);
            float y = 130 - row * (H + GAP_Y);

            var btn = MakeButton($"__Dice_{dice.label}", _diceGrid, C_DICE_BTN, dice.label, jp, 58, C_DICE_TXT,
                W, H, new Vector2(x, y), () => OnSelectDice(dice.label, dice.count, dice.faces, prob));

            MakeLabel(btn.transform, $"成功 {prob}%", jp, 30, FontStyles.Bold, C_PROB_TXT,
                W, 40, new Vector2(0, -48));
        }
    }

    /// <summary>ダイスを選ぶと「振る」待ちになる（ここではまだ振らない）。</summary>
    private void OnSelectDice(string label, int count, int faces, int prob)
    {
        if (_busy) return;
        _pending = (label, count, faces, prob);
        _selectPanel.SetActive(false);
        _phaseText.text = $"{label}（成功 {prob}%）で挑む！";
        SetAction("1D100を振る", OnRollD100, showCancel: true);
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
        if (_bossVisual != null) _bossVisual.gameObject.SetActive(false);
        if (_diceViewBg != null) _diceViewBg.SetActive(true);

        if (_sim == null) _sim = Dice3DSimulator.Create(GetJpFont());
        _diceView.texture = _sim.Texture;
        var (_, element) = SelfJobElement();
        _sim.SetDiceElement(element);

        _phaseText.text = "運命の 1D100 …";
        int d100 = await _sim.RollPercentileAsync(); // 十の位(00..90)＋一の位(0..9)

        _d100Success = d100 <= _pending.prob;
        bool critical = d100 <= 5;
        bool fumble = d100 >= 96;
        _d100AtkMod = critical ? 1 : fumble ? -1 : 0;

        string judge = critical ? "クリティカル！！" : fumble ? "ファンブル…"
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

        // 攻撃力＝出目 ＋ クリ/ファンブル補正 ＋ ATKクーポン
        int atkCoupon = DiceAtkBonus.Consume();
        int atk = Mathf.Max(0, rollSum + _d100AtkMod + atkCoupon);

        // 弱点属性（=今日お得な属性）で攻撃するとダメージ＋1
        bool weakHit = !string.IsNullOrEmpty(element) && element == _weakness;
        if (weakHit) atk += 1;

        float denom = rc * rf - rc;
        float intensity = denom > 0 ? (rollSum - rc) / denom : 1f;
        _sim.PlayResultEffect(job, element, intensity);

        string detail = $"出目 {rollSum}";
        if (_d100AtkMod > 0) detail += $" ＋クリ{_d100AtkMod}";
        else if (_d100AtkMod < 0) detail += $" {_d100AtkMod}";
        if (atkCoupon > 0) detail += $" ＋ATK{atkCoupon}";
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
            _phaseText.text = "★ ボスモンスターを討伐した！ ★";
            AssetsDatabase.instance?.PlayLevelUpSE();
            LocalHistoryLog.Add("dice", overkill > 0 ? $"ボス討伐成功（GP+{overkill}）" : "ボス討伐成功");

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

            // オーバーキル分のGP付与＋選択キャラのレベルアップ（1回の書き込み）
            int oldLv = SelfLevel();
            await GrantRewardAsync(overkill);
            int newLv = SelfLevel();

            string lvLine = $"レベルアップ！\nLv{oldLv} → Lv{newLv}";
            string strong = overkill > 0
                ? $"GP {overkill}ポイントゲット！\n{lvLine}"
                : lvLine;
            InfoModal.Show("ボス討伐成功！", strong);
        }
        else
        {
            _phaseText.text = "討伐失敗…";
            LocalHistoryLog.Add("dice", "ボス討伐失敗");
            await UniTask.Delay(400);
            InfoModal.Show("討伐失敗…", "ボスを倒せなかった…");
        }

        await EndBattleAsync();
    }

    /// <summary>討伐報酬: オーバーキル分のGPと選択キャラのレベルアップを自分のドキュメントへ書き込む。</summary>
    private async UniTask GrantRewardAsync(int gp)
    {
        var manager = UserDataManager.instance;
        if (manager == null || manager.UserData == null) return;
        int idx = manager.CurrentSelectCharacterNumber;
        var chars = manager.UserData.Characters;
        if (chars == null || idx >= chars.Count) return;
        var chara = chars[idx];
        int newLevel = chara.Level + 1;

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
        MakeButton("__BossClose", _overlay.transform, C_CLOSE_BTN, "✕", jp, 48, Color.white,
            96, 96, new Vector2(410, 870), Hide);

        // ボス名
        _bossName = MakeLabel(_overlay.transform, "ボスモンスター", jp, 52, FontStyles.Bold, C_TITLE,
            900, 80, new Vector2(0, -688)).GetComponent<TextMeshProUGUI>();
        _weakText = MakeLabel(_overlay.transform, "", jp, 30, FontStyles.Bold, C_PROB_TXT,
            900, 46, new Vector2(0, -742)).GetComponent<TextMeshProUGUI>();

        // HPバー（枠 → 背景 → fill → 数値）
        MakeRect("__HpBorder", _overlay.transform, C_BORDER, 832, 56)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -800);
        MakeRect("__HpBg", _overlay.transform, C_HP_BG, 820, 44)
            .GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -800);
        var fillGO = MakeRect("__HpFill", _overlay.transform, C_HP_FILL, 820, 44);
        _hpFillRt = fillGO.GetComponent<RectTransform>();
        _hpFillRt.pivot = new Vector2(0f, 0.5f);               // 左端基準で伸縮
        _hpFillRt.anchoredPosition = new Vector2(-410f, -800f); // 背景の左端に合わせる
        _hpFillRt.sizeDelta = new Vector2(820f, 44f);
        _hpText = MakeLabel(_overlay.transform, "", jp, 30, FontStyles.Bold, Color.white,
            820, 44, new Vector2(0, -800)).GetComponent<TextMeshProUGUI>();

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

        _cancelButton = MakeButton("__CancelBtn", _overlay.transform, C_MUTED, "選び直す", jp, 34,
            Color.white, 300, 84, new Vector2(0, -460), OnCancel);
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
