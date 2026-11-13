using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 管理者アカウントのアプリから、ボードゲーム図鑑を更新する（2026-11-13 ユーザー指定「管理者画面で、ボドゲ図鑑の更新を
/// 行えるように」。画面は図鑑 SpaceBoardGameZukan の下の「ボドゲーマから図鑑を更新」）。
///
/// Firebase の Cloud Functions「syncBoardgames」（tools/functions/index.js・boardgames.js）を呼ぶ。サーバーがボドゲーマの
/// お店のページを読み、変わっていれば図鑑（master/boardgames）を配信する（30秒〜1分ほど）。権限はサーバーが IDトークンの
/// admin クレームで確かめる（画面の出し分け AdminClaim は見た目だけ）。呼び方は NoticeFunction と同じく onCall の決まり
/// （POST・本文 {"data": …}・Authorization: Bearer IDトークン）に合わせて UnityWebRequest で直接呼ぶ。
/// </summary>
public static class BoardGameSyncFunction
{
    private const string URL = "https://asia-northeast1-intales-a0459.cloudfunctions.net/syncBoardgames";
    // サーバーはふだん30秒〜1分。サーバーの時間切れ（9分）より短く、待ちすぎない長さ
    private const int TIMEOUT_SEC = 300;

    public const string T_NO_LOGIN = "ログインし直してから、もう一度お試しください";
    public const string T_OFFLINE = "インターネットにつながっていないようです";
    public const string T_TIMEOUT = "返事がありませんでした。更新できていることもあるので、アプリを開き直してから図鑑を確かめてください";
    public const string T_NOT_ADMIN = "このアカウントには図鑑を更新する権限がありません。管理者のアカウントでお試しください";
    public const string T_NOT_READY = "図鑑を更新する仕組みがまだ用意されていません（Cloud Functions が見つかりません）";
    public const string T_FAILED = "図鑑を更新できませんでした（HTTP ";

    public static string GlyphText =>
        T_NO_LOGIN + T_OFFLINE + T_TIMEOUT + T_NOT_ADMIN + T_NOT_READY + T_FAILED + "）0123456789";

    /// <summary>サーバーの結果</summary>
    [Serializable]
    public class Result
    {
        public bool changed;     // 図鑑の中身が変わって、新しい版を配信した
        public int version;      // 配信中の版（master/config.boardgames_version）
        public int count;        // 図鑑のゲームの数
        public string[] added;   // 新しく図鑑に入ったゲームの名前
        public string[] removed; // 図鑑からなくなったゲームの名前
        public int seconds;      // サーバーでかかった秒数
    }

    /// <summary>
    /// 図鑑を更新する。成功なら (結果, null)、失敗なら (null, そのまま画面に出せる文)
    /// </summary>
    public static async UniTask<(Result result, string error)> RunAsync()
    {
        string idToken;
        try
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) return (null, T_NO_LOGIN);
            // 管理者にしてもらった直後でも新しい印（admin クレーム）で呼べるよう、毎回取り直す
            idToken = await user.TokenAsync(true).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BoardGameSync] IDトークンを取れませんでした: {ex.Message}");
            return (null, T_OFFLINE);
        }

        using var req = new UnityWebRequest(URL, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"data\":{}}")),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = TIMEOUT_SEC,
        };
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", "Bearer " + idToken);
        var op = req.SendWebRequest();
        while (!op.isDone) await UniTask.Yield(); // await だと HTTP のエラーで例外になるので、終わるのを待ってから見る

        long code = req.responseCode;
        if (req.result == UnityWebRequest.Result.ConnectionError || code == 0)
        {
            bool timeout = (req.error ?? "").IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
            Debug.LogWarning($"[BoardGameSync] つながりませんでした: {req.error}");
            return (null, timeout ? T_TIMEOUT : T_OFFLINE);
        }

        var resp = Parse(req.downloadHandler?.text);
        // JsonUtility は無いクラスのフィールドも空の物で埋めるので、本物の結果か（版は1以上）も確かめる
        if (code == 200 && resp?.result != null && resp.result.version > 0)
        {
            var r = resp.result;
            r.added ??= Array.Empty<string>();
            r.removed ??= Array.Empty<string>();
            return (r, null);
        }

        string status = resp?.error?.status ?? "";
        string serverMessage = resp?.error?.message ?? "";
        Debug.LogWarning($"[BoardGameSync] 更新できませんでした: HTTP {code} {status} {serverMessage}");
        if (code == 401 || status == "UNAUTHENTICATED") return (null, T_NO_LOGIN);
        if (code == 403 || status == "PERMISSION_DENIED") return (null, T_NOT_ADMIN);
        // ボドゲーマを読めなかった・ほかの更新が動いている等は、サーバーの文（日本語）をそのまま出す
        if ((status == "FAILED_PRECONDITION" || status == "ALREADY_EXISTS") && serverMessage.Length > 0)
            return (null, serverMessage);
        if (code == 404) return (null, T_NOT_READY);
        return (null, T_FAILED + code + "）");
    }

    private static Resp Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            return JsonUtility.FromJson<Resp>(text);
        }
        catch (Exception)
        {
            return null; // HTML のエラーページなど
        }
    }

    [Serializable] private class Resp { public Result result; public Err error; }
    [Serializable] private class Err { public string message; public string status; }
}
