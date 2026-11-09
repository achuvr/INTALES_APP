using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 管理者アカウントのアプリから、お知らせ通知を送る（2026-11-10 ユーザー指定。画面は SpaceNoticeComposer）。
///
/// Firebase の Cloud Functions「sendNotice」（tools/functions/index.js）を呼ぶ。通知を送る権限はサーバーにだけあり、
/// アプリに鍵は入れない（アプリの中身は誰でも取り出せるため）。サーバーが IDトークンの admin クレームを確かめてから
/// FCM へ送るので、管理者でなければ断られる（この画面の出し分け AdminClaim は見た目だけ）。
/// 呼び方は onCall の決まり（POST・本文 {"data": …}・Authorization: Bearer IDトークン）に合わせて UnityWebRequest で直接呼ぶ
/// （Cloud Functions の SDK を足さずに済む）。
/// </summary>
public static class NoticeFunction
{
    private const string URL = "https://asia-northeast1-intales-a0459.cloudfunctions.net/sendNotice";
    /// <summary>文字数の上限（サーバー・パソコンの送信画面と同じ）</summary>
    public const int TITLE_MAX = 40, BODY_MAX = 150;
    private const int TIMEOUT_SEC = 30;

    public const string T_NO_LOGIN = "ログインし直してから送ってください";
    public const string T_OFFLINE = "インターネットにつながっていないようです";
    public const string T_TIMEOUT = "返事がありませんでした\n送れていることもあるので、まずテストで届くか確かめてください";
    public const string T_NOT_ADMIN = "このアカウントには送る権限がありません\n管理者のアカウントで送ってください";
    public const string T_NOT_READY = "送る仕組みがまだ用意されていません（Cloud Functions が見つかりません）";
    public const string T_FAILED = "送れませんでした（HTTP ";

    public static string GlyphText =>
        T_NO_LOGIN + T_OFFLINE + T_TIMEOUT + T_NOT_ADMIN + T_NOT_READY + T_FAILED + "）0123456789";

    /// <summary>
    /// 送る。topic は AppNotifications.TOPIC_STAFF（テスト）か TOPIC_ALL（全員）。
    /// 失敗したときの message はそのまま画面に出せる文
    /// </summary>
    public static async UniTask<(bool ok, string message)> SendAsync(string title, string body, string topic)
    {
        string idToken;
        try
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) return (false, T_NO_LOGIN);
            // 管理者にしてもらった直後でも新しい印（admin クレーム）で送れるよう、毎回取り直す
            idToken = await user.TokenAsync(true).AsUniTask();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NoticeFunction] IDトークンを取れませんでした: {ex.Message}");
            return (false, T_OFFLINE);
        }

        string json = JsonUtility.ToJson(new Req { data = new Data { title = title, body = body ?? "", topic = topic } });
        using var req = new UnityWebRequest(URL, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
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
            Debug.LogWarning($"[NoticeFunction] つながりませんでした: {req.error}");
            return (false, timeout ? T_TIMEOUT : T_OFFLINE);
        }
        if (code == 200) return (true, null);

        var err = Parse(req.downloadHandler?.text)?.error;
        string status = err?.status ?? "";
        string serverMessage = err?.message ?? "";
        Debug.LogWarning($"[NoticeFunction] 送れませんでした: HTTP {code} {status} {serverMessage}");
        if (code == 401 || status == "UNAUTHENTICATED") return (false, T_NO_LOGIN);
        if (code == 403 || status == "PERMISSION_DENIED") return (false, T_NOT_ADMIN);
        // 入力のまちがい・二重送信の防止は、サーバーの文（日本語）をそのまま出す
        if ((status == "INVALID_ARGUMENT" || status == "ALREADY_EXISTS") && serverMessage.Length > 0)
            return (false, serverMessage);
        if (code == 404) return (false, T_NOT_READY);
        return (false, T_FAILED + code + "）");
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

    [Serializable] private class Req { public Data data; }
    [Serializable] private class Data { public string title; public string body; public string topic; }
    [Serializable] private class Resp { public Err error; }
    [Serializable] private class Err { public string message; public string status; }
}
