using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// お店のお知らせ通知を、Firebase Cloud Messaging（HTTP v1）へ直接送る（2026-11-09 ユーザー指定。
/// 送信画面 NoticeSender を Windows 用にビルドして、このパソコンとノートパソコンの2台から送れるように。node は使わない）。
///
/// 鍵（サービスアカウントの JSON）は次の順に探す。公開リポジトリの中には置かない:
///   1. exe と同じフォルダ（Windows 用にビルドしたものだけ。エディタではプロジェクト直下になるので探さない）
///   2. C:\Users\(名前)\.secrets
/// 通知を送る権限だけの鍵（firebase-adminsdk 以外）があればそちらを使い、無ければ管理用（全権限）の鍵を使う。
/// 送る中身は tools/firestore/send-notice.js と同じ（Android は通知チャンネル intales_default、iPhone は音あり）
/// </summary>
public static class FcmNotice
{
    public const string PROJECT_ID = "intales-a0459";
    private const string SCOPE = "https://www.googleapis.com/auth/firebase.messaging";
    private const string TOKEN_URL = "https://oauth2.googleapis.com/token";
    private const string SEND_URL = "https://fcm.googleapis.com/v1/projects/" + PROJECT_ID + "/messages:send";
    private const string ANDROID_CHANNEL = "intales_default"; // AppNotifications が作る通知チャンネル
    private const string ADMIN_KEY_PREFIX = "firebase-adminsdk";
    private const long KEY_FILE_MAX = 64 * 1024;

    [Serializable]
    public class Key
    {
        public string type, project_id, private_key, client_email;
        [NonSerialized] public string path;

        /// <summary>全権限の管理用の鍵か（通知を送る権限だけの鍵なら false）</summary>
        public bool IsAdmin => client_email != null && client_email.StartsWith(ADMIN_KEY_PREFIX);
    }

    /// <summary>鍵を探すフォルダ（探す順）</summary>
    public static List<string> KeyFolders()
    {
        var list = new List<string>();
#if !UNITY_EDITOR
        list.Add(Path.GetDirectoryName(Application.dataPath)); // exe と同じフォルダ
#endif
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) list.Add(Path.Combine(home, ".secrets"));
        return list;
    }

    /// <summary>使う鍵を探す（無ければ null）</summary>
    public static Key FindKey()
    {
        Key admin = null;
        foreach (string dir in KeyFolders())
        {
            string[] files;
            try
            {
                if (!Directory.Exists(dir)) continue;
                files = Directory.GetFiles(dir, "*.json");
            }
            catch (Exception)
            {
                continue;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                var k = TryRead(f);
                if (k == null) continue;
                if (!k.IsAdmin) return k;
                admin ??= k;
            }
        }
        return admin;
    }

    private static Key TryRead(string path)
    {
        try
        {
            if (new FileInfo(path).Length > KEY_FILE_MAX) return null; // 鍵にしては大きすぎるファイルは読まない
            var k = JsonUtility.FromJson<Key>(File.ReadAllText(path));
            if (k == null || k.type != "service_account" || k.project_id != PROJECT_ID) return null;
            if (string.IsNullOrEmpty(k.private_key) || string.IsNullOrEmpty(k.client_email)) return null;
            k.path = path;
            return k;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ================================================================
    // 送る
    // ================================================================
    private static string _token, _tokenEmail;
    private static DateTime _tokenUntilUtc;

    /// <summary>
    /// 話題（topic）へ通知を送る。成功ならメッセージの ID、失敗なら理由を返す。
    /// validateOnly なら届けずに中身の確認だけをする（FCM の validate_only）
    /// </summary>
    public static async UniTask<(bool ok, string message)> SendAsync(Key key, string title, string body, string topic,
        bool validateOnly = false)
    {
        if (key == null) return (false, "鍵が見つかりません");
        var (token, error) = await TokenAsync(key);
        if (token == null) return (false, error);

        var req = new SendReq
        {
            validate_only = validateOnly,
            message = new Msg
            {
                topic = topic,
                notification = new Notif { title = title, body = body ?? "" },
                android = new AndroidCfg { notification = new AndroidNotif { channel_id = ANDROID_CHANNEL } },
                apns = new ApnsCfg { payload = new ApnsPayload { aps = new Aps { sound = "default" } } },
            },
        };
        var r = await PostAsync(SEND_URL, Encoding.UTF8.GetBytes(JsonUtility.ToJson(req)),
            "application/json; charset=utf-8", token);
        if (r.netError != null) return (false, NetErrorText(r.netError));
        var res = Parse<SendRes>(r.text);
        if (r.code == 200) return (true, res?.name ?? "");

        if (r.code == 401) _token = null; // 次はトークンを取り直す
        string detail = $"HTTP {r.code}: {res?.error?.status} {res?.error?.message}".Trim();
        if (r.code == 403) return (false, $"この鍵には通知を送る権限がありません（{detail}）");
        return (false, $"送れませんでした（{detail}）");
    }

    /// <summary>鍵で署名した JWT を、アクセストークン（1時間有効）と引き換える。取れたものは使い回す</summary>
    private static async UniTask<(string token, string error)> TokenAsync(Key key)
    {
        if (_token != null && _tokenEmail == key.client_email && DateTime.UtcNow < _tokenUntilUtc) return (_token, null);
        string jwt;
        try
        {
            jwt = GoogleJwt.Create(key.client_email, key.private_key, SCOPE, TOKEN_URL, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return (null, "鍵を読めませんでした: " + ex.Message);
        }
        string form = "grant_type=" + UnityWebRequest.EscapeURL("urn:ietf:params:oauth:grant-type:jwt-bearer")
                      + "&assertion=" + UnityWebRequest.EscapeURL(jwt);
        var r = await PostAsync(TOKEN_URL, Encoding.UTF8.GetBytes(form), "application/x-www-form-urlencoded", null);
        if (r.netError != null) return (null, NetErrorText(r.netError));
        var res = Parse<TokenRes>(r.text);
        if (r.code != 200 || string.IsNullOrEmpty(res?.access_token))
        {
            string why = !string.IsNullOrEmpty(res?.error_description) ? res.error_description : res?.error ?? r.text;
            return (null, $"鍵が使えませんでした。鍵が消されたか、パソコンの時計がずれているかもしれません（HTTP {r.code}: {why}）");
        }
        _token = res.access_token;
        _tokenEmail = key.client_email;
        _tokenUntilUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, res.expires_in - 300));
        return (_token, null);
    }

    private static async UniTask<(long code, string text, string netError)> PostAsync(string url, byte[] data,
        string contentType, string bearer)
    {
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(data) { contentType = contentType };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", contentType);
            if (bearer != null) req.SetRequestHeader("Authorization", "Bearer " + bearer);
            req.timeout = 30;
            var op = req.SendWebRequest();
            while (!op.isDone) await UniTask.Yield();
            string text = req.downloadHandler != null ? req.downloadHandler.text : null;
            string net = req.result == UnityWebRequest.Result.ConnectionError ? (req.error ?? "接続できません") : null;
            return (req.responseCode, text ?? "", net);
        }
    }

    private static string NetErrorText(string error) => $"インターネットにつながっていないようです（{error}）";

    private static T Parse<T>(string json) where T : class
    {
        try
        {
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<T>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // FCM と Google の認証の JSON（名前は API のまま）
    [Serializable] private class TokenRes { public string access_token; public int expires_in; public string error, error_description; }
    [Serializable] private class SendRes { public string name; public SendErr error; }
    [Serializable] private class SendErr { public int code; public string message, status; }
    [Serializable] private class SendReq { public bool validate_only; public Msg message; }
    [Serializable] private class Msg { public string topic; public Notif notification; public AndroidCfg android; public ApnsCfg apns; }
    [Serializable] private class Notif { public string title, body; }
    [Serializable] private class AndroidCfg { public AndroidNotif notification; }
    [Serializable] private class AndroidNotif { public string channel_id; }
    [Serializable] private class ApnsCfg { public ApnsPayload payload; }
    [Serializable] private class ApnsPayload { public Aps aps; }
    [Serializable] private class Aps { public string sound; }
}
