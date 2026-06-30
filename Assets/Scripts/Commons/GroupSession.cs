using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// グループ機能のFusionセッション管理（Shared Mode）。
/// A〜Dのルーム名で StartGame すると、ルームが無ければ作成・あれば参加になる
/// （Shared Modeの既定動作）。最後の1人が抜けるとPhoton側でルームは自動消滅する。
///
/// メンバーのキャラクター名は Runner.SendReliableDataToPlayer で共有する:
/// 自分が参加したとき・誰かが参加したとき・キャラ切替時に、自分の名前を
/// 全メンバーへ送信し、各クライアントが OnReliableDataReceived で
/// Members（PlayerId→キャラ名）を組み立てる。
/// （RPCではなくReliableDataを使うのは、ILウィービング（weave設定）に
/// 依存せず確実に動かすため。NetworkObjectのプレハブも不要。）
/// </summary>
public class GroupSession : MonoBehaviour, INetworkRunnerCallbacks
{
    /// <summary>参加可能なルーム名</summary>
    public static readonly string[] ROOMS = { "A", "B", "C", "D" };

    /// <summary>現在参加中のセッション（未参加ならnull）</summary>
    public static GroupSession Active { get; private set; }

    /// <summary>接続・切断処理中か</summary>
    public static bool IsBusy { get; private set; }

    /// <summary>参加中のルーム名（"A"〜"D"。未参加ならnull）</summary>
    public static string CurrentRoom => Active != null ? Active._room : null;

    /// <summary>メンバー1人分の共有情報（選択中キャラクターの名前・職業・属性・レベル・最新の出目）</summary>
    public class MemberInfo
    {
        public string Name;
        public string Job;
        public string Element;
        public int Level;
        /// <summary>最後に振ったダイスの出目（-1 = 未ロール）</summary>
        public int Dice = -1;
        /// <summary>ボス戦の戦闘ウィンドウを開いている（参戦中）か</summary>
        public bool InBattle;
        /// <summary>このボス戦で自分のターンのダイスを振り終えたか</summary>
        public bool HasRolled;
    }

    /// <summary>メンバー一覧（PlayerId → キャラクター情報）。参加順で安定するようIDでソート</summary>
    public static readonly SortedDictionary<int, MemberInfo> Members = new SortedDictionary<int, MemberInfo>();

    /// <summary>参加状態・メンバーが変わったとき（UI更新用）</summary>
    public static event System.Action Changed;

    private string _room;
    private NetworkRunner _runner;

    /// <summary>自分の参加処理が完了したか（これ以前のOnPlayerJoinedは「先にいた人」なので履歴に書かない）</summary>
    private bool _joinCompleted;

    /// <summary>入室を検知したがまだ名前が届いていないメンバー（名前が届いたら履歴に記録する）</summary>
    private readonly HashSet<int> _pendingJoinLog = new HashSet<int>();

    // ================================================================
    // 参加・脱退
    // ================================================================

    /// <summary>ルームに参加する（無ければ作成される）</summary>
    public static async UniTask<bool> JoinAsync(string room)
    {
        if (Active != null || IsBusy) return false;
        IsBusy = true;
        Changed?.Invoke();

        var go = new GameObject("__GroupSession");
        DontDestroyOnLoad(go); // シーン再読み込みでも接続を維持する
        var session = go.AddComponent<GroupSession>();
        session._room = room;

        var runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;
        runner.AddCallbacks(session);
        session._runner = runner;

        StartGameResult result;
        try
        {
            result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared, // ルームが無ければ作成、あれば参加
                SessionName = $"group_{room}",
                SceneManager = go.AddComponent<NetworkSceneManagerDefault>(),
            }).AsUniTask();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Group] 接続エラー: {ex.Message}");
            Destroy(go);
            IsBusy = false;
            Changed?.Invoke();
            return false;
        }

        IsBusy = false;
        if (!result.Ok)
        {
            Debug.LogError($"[Group] 参加失敗: {result.ShutdownReason}");
            Destroy(go);
            Changed?.Invoke();
            return false;
        }

        Active = session;
        Members.Clear();
        session.AnnounceSelf();
        session._joinCompleted = true;
        Debug.Log($"[Group] {room}ルームに参加しました (LocalPlayer={runner.LocalPlayer.PlayerId})");

        // 履歴に記録（端末ローカル・最大50件）
        LocalHistoryLog.Add("group", $"{room}ルームに参加した");

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// ルームから脱退する。最後の1人だった場合、空になったルームは
    /// Photonクラウド側で自動的に削除される。
    /// </summary>
    public static async UniTask LeaveAsync()
    {
        if (Active == null || IsBusy) return;
        IsBusy = true;
        Changed?.Invoke();

        try
        {
            await Active._runner.Shutdown().AsUniTask();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Group] 切断時の警告: {ex.Message}");
        }

        IsBusy = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// グループ参加中なら自分のキャラクター名を再アナウンスする。
    /// キャラクター切り替え時に呼ばれ、メンバーリストの表示が即時更新される。
    /// </summary>
    public static void AnnounceSelfIfActive()
    {
        if (Active != null) Active.AnnounceSelf();
    }

    /// <summary>
    /// ダイスの出目を全メンバーのリスト表示へ共有する（グループ未参加なら何もしない）。
    /// 値は次のアナウンスにも含まれるため、後から入ったメンバーにも見える。
    /// </summary>
    public static void AnnounceDiceResult(int total)
    {
        if (Active == null) return;
        Active._lastDiceTotal = total;
        Active.AnnounceSelf();
    }

    /// <summary>自分が最後に振ったダイスの出目（-1 = 未ロール）</summary>
    private int _lastDiceTotal = -1;

    /// <summary>自分が戦闘ウィンドウを開いている（参戦中）か</summary>
    private bool _inBattle;

    /// <summary>戦闘ウィンドウの開閉に合わせて自分の参戦状態を全メンバーへ共有する。</summary>
    public static void SetInBattle(bool v)
    {
        if (Active == null) return;
        if (Active._inBattle == v) return;
        Active._inBattle = v;
        Active.AnnounceSelf();
    }

    /// <summary>グループ参加中、全メンバーが参戦中か（未参加ならソロ扱いで常にtrue）。</summary>
    public static bool AllMembersInBattle()
    {
        if (Active == null) return true;
        if (Members.Count == 0) return false;
        foreach (var m in Members.Values)
            if (!m.InBattle) return false;
        return true;
    }

    /// <summary>現在のメンバー数</summary>
    public static int MemberCount() => Members.Count;

    /// <summary>参戦中（戦闘ウィンドウを開いている）メンバー数</summary>
    public static int MembersInBattleCount()
    {
        int n = 0;
        foreach (var m in Members.Values)
            if (m.InBattle) n++;
        return n;
    }

    /// <summary>自分がこのターンに振り終えたか</summary>
    private bool _hasRolled;

    /// <summary>自分の「振り終えた」状態を全メンバーへ共有する。</summary>
    public static void SetHasRolled(bool v)
    {
        if (Active == null) return;
        if (Active._hasRolled == v) return;
        Active._hasRolled = v;
        Active.AnnounceSelf();
    }

    /// <summary>グループ参加中、全メンバーが振り終えたか（未参加ならソロ扱いで常にtrue）。</summary>
    public static bool AllRolled()
    {
        if (Active == null) return true;
        if (Members.Count == 0) return false;
        foreach (var m in Members.Values)
            if (!m.HasRolled) return false;
        return true;
    }

    /// <summary>振り終えたメンバー数</summary>
    public static int MembersRolledCount()
    {
        int n = 0;
        foreach (var m in Members.Values)
            if (m.HasRolled) n++;
        return n;
    }

    // 名前共有メッセージの識別子（ReliableKeyの先頭3要素 = "nam"）
    private const int KEY_0 = 110, KEY_1 = 97, KEY_2 = 109;

    // ボスダメージ共有メッセージの識別子（"dmg"）
    private const int DMG_0 = 100, DMG_1 = 109, DMG_2 = 103;

    // 協力スキルのバフ共有メッセージ（"cop"）
    private const int COP_0 = 99, COP_1 = 111, COP_2 = 112;

    /// <summary>協力スキルで全員に乗っている成功確率バフ(%)。ボス戦開始/終了時にリセットする。</summary>
    public static int CoopProbBonus { get; private set; }

    /// <summary>協力バフを付与した発動者の名前（複数なら「・」連結）。</summary>
    public static string CoopGranter { get; private set; }

    /// <summary>協力バフが変化したとき（戦闘UIの確率表示更新用）</summary>
    public static event System.Action CoopChanged;

    /// <summary>協力バフをリセットする（ボス戦の開始・終了時に各自で呼ぶ）。</summary>
    public static void ResetCoopBuff()
    {
        CoopProbBonus = 0;
        CoopGranter = null;
        CoopChanged?.Invoke();
    }

    /// <summary>
    /// 協力スキル発動: 全員（自分含む）に成功確率バフを配る。
    /// ローカルへ即適用し、グループ参加中なら他メンバーへも送信する。
    /// </summary>
    public static void BroadcastCoopBuff(int bonus, string granterName)
    {
        AddCoopBuffLocal(bonus, granterName);
        if (Active != null) Active.SendCoopBuff(bonus, granterName);
    }

    private static void AddCoopBuffLocal(int bonus, string granterName)
    {
        CoopProbBonus += bonus;
        if (string.IsNullOrEmpty(CoopGranter)) CoopGranter = granterName;
        else if (!CoopGranter.Contains(granterName)) CoopGranter += $"・{granterName}";
        CoopChanged?.Invoke();
    }

    private void SendCoopBuff(int bonus, string granterName)
    {
        if (_runner == null || !_runner.IsRunning) return;
        var data = System.Text.Encoding.UTF8.GetBytes($"{bonus}\n{granterName}");
        var key = ReliableKey.FromInts(COP_0, COP_1, COP_2, _runner.LocalPlayer.PlayerId);
        foreach (var player in _runner.ActivePlayers)
        {
            if (player == _runner.LocalPlayer) continue;
            _runner.SendReliableDataToPlayer(player, key, data);
        }
    }

    /// <summary>グループの誰かがボスに与えたダメージを受信したとき（みんなで1体を削る用）</summary>
    public static event System.Action<int> BossDamaged;

    /// <summary>自分がボスに与えたダメージをグループ全員へ共有する（未参加なら何もしない）</summary>
    public static void BroadcastBossDamage(int dmg)
    {
        if (Active != null) Active.SendBossDamage(dmg);
    }

    private void SendBossDamage(int dmg)
    {
        if (_runner == null || !_runner.IsRunning) return;
        var data = System.Text.Encoding.UTF8.GetBytes(dmg.ToString());
        var key = ReliableKey.FromInts(DMG_0, DMG_1, DMG_2, _runner.LocalPlayer.PlayerId);
        foreach (var player in _runner.ActivePlayers)
        {
            if (player == _runner.LocalPlayer) continue;
            _runner.SendReliableDataToPlayer(player, key, data);
        }
    }

    /// <summary>自分の選択中キャラクター情報を全メンバーに送信し、自分のローカル表示も更新する</summary>
    private void AnnounceSelf()
    {
        if (_runner == null || !_runner.IsRunning) return;

        var info = GetMyInfo();
        info.Dice = _lastDiceTotal;
        info.InBattle = _inBattle;
        info.HasRolled = _hasRolled;
        int myId = _runner.LocalPlayer.PlayerId;

        // 自分の分はローカルで直接反映
        Members[myId] = info;
        Changed?.Invoke();

        // 他のメンバーへ送信。
        // - 「誰の情報か」は受信側の引数に頼らず、ペイロード先頭に自分のPlayerIdを入れて伝える
        // - 複数人が同じキーで送ると転送が衝突して上書きされ得るため、キーにも送信者IDを混ぜる
        var payload = $"{myId}\n{info.Name}\n{info.Job}\n{info.Element}\n{info.Level}\n{info.Dice}\n{(info.InBattle ? 1 : 0)}\n{(info.HasRolled ? 1 : 0)}";
        var data = System.Text.Encoding.UTF8.GetBytes(payload);
        var key = ReliableKey.FromInts(KEY_0, KEY_1, KEY_2, myId);
        foreach (var player in _runner.ActivePlayers)
        {
            if (player == _runner.LocalPlayer) continue;
            _runner.SendReliableDataToPlayer(player, key, data);
        }
    }

    private static MemberInfo GetMyInfo()
    {
        var manager = UserDataManager.instance;
        var characters = manager != null ? manager.UserData?.Characters : null;
        int idx = manager != null ? manager.CurrentSelectCharacterNumber : 0;
        if (characters != null && idx < characters.Count)
        {
            var chara = characters[idx];
            return new MemberInfo
            {
                Name = string.IsNullOrEmpty(chara.Name) ? "ななしさん" : chara.Name,
                Job = chara.Job,
                Element = chara.Element,
                Level = chara.Level,
            };
        }
        return new MemberInfo { Name = "ななしさん", Job = "", Element = "", Level = 1 };
    }

    // ================================================================
    // INetworkRunnerCallbacks
    // ================================================================
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // 自分の参加完了後に入ってきた人は履歴の記録対象。
        // この時点では名前が届いていないので、キャラ情報の受信時に記録する
        if (_joinCompleted && player != runner.LocalPlayer && !Members.ContainsKey(player.PlayerId))
            _pendingJoinLog.Add(player.PlayerId);

        // 新しく入った人にも全員分の名前が届くよう、各自が自分を再アナウンスする
        AnnounceSelf();
        Changed?.Invoke();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Members.Remove(player.PlayerId);
        _pendingJoinLog.Remove(player.PlayerId);
        Changed?.Invoke();
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[Group] セッション終了: {shutdownReason}");

        // 履歴に記録（参加に成功していた場合のみ。切断による退室もここを通る）
        if (_joinCompleted)
            LocalHistoryLog.Add("group", $"{_room}ルームから退室した");

        Members.Clear();
        if (Active == this) Active = null;
        Changed?.Invoke();
        // GameObjectの破棄はここでは行わない。
        // Shutdown(destroyGameObject: true が既定)がテアダウン完了後に自動で破棄する。
        // ここでDestroyするとRunnerの切断手順が中断され、サーバー側からの
        // 強制切断（DisconnectMessage Code:104 "ServerLogic"）を誘発する。
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Group] サーバーから切断されました: {reason}");
    }

    /// <summary>
    /// 他メンバーから届いたキャラクター情報をメンバー一覧へ反映する。
    /// 「誰の情報か」はコールバックの player 引数ではなくペイロード先頭の
    /// PlayerId を使う（環境によって player 引数が送信者を指さず、
    /// 自分のエントリが上書きされるバグがあったため）。
    /// </summary>
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data)
    {
        key.GetInts(out int k0, out int k1, out int k2, out _);

        // ボスダメージ共有メッセージ
        if (k0 == DMG_0 && k1 == DMG_1 && k2 == DMG_2)
        {
            string ds = System.Text.Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
            if (int.TryParse(ds, out int dmg)) BossDamaged?.Invoke(dmg);
            return;
        }

        // 協力バフ共有メッセージ
        if (k0 == COP_0 && k1 == COP_1 && k2 == COP_2)
        {
            string cs = System.Text.Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
            var cp = cs.Split('\n');
            if (cp.Length >= 2 && int.TryParse(cp[0], out int bonus))
                AddCoopBuffLocal(bonus, cp[1]);
            return;
        }

        if (k0 != KEY_0 || k1 != KEY_1 || k2 != KEY_2) return;

        string payload = System.Text.Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
        var parts = payload.Split('\n');
        if (parts.Length < 5 || !int.TryParse(parts[0], out int senderId)) return;

        // 自分のエントリは自分のローカル反映が正なので上書きさせない
        if (_runner != null && senderId == _runner.LocalPlayer.PlayerId) return;

        Members[senderId] = new MemberInfo
        {
            Name = parts[1],
            Job = parts[2],
            Element = parts[3],
            Level = int.TryParse(parts[4], out int lv) ? lv : 1,
            Dice = parts.Length > 5 && int.TryParse(parts[5], out int dice) ? dice : -1,
            InBattle = parts.Length > 6 && parts[6] == "1",
            HasRolled = parts.Length > 7 && parts[7] == "1",
        };

        // 入室直後のメンバーの名前が届いたタイミングで履歴に記録する
        if (_pendingJoinLog.Remove(senderId))
            LocalHistoryLog.Add("group", $"{parts[1]} が{_room}ルームに入ってきた");

        Changed?.Invoke();
    }

    // 以下は未使用（インターフェースの実装のみ）
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}
