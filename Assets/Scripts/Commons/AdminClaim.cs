using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ログイン中アカウントの管理者判定（IDトークンの admin カスタムクレームを復号して見る）。
///
/// これは「管理者向けボタンを表示するか」の出し分け専用。実際の保護は
/// Firestore / Storage のセキュリティルール側（request.auth.token.admin）で行うので、
/// この判定を偽装されても書き込みはルールで拒否される。
///
/// クレームの付与は tools/firestore/set-admin.js（付与後はアプリの再ログインが必要）。
/// </summary>
public static class AdminClaim
{
    public static bool IsAdmin { get; private set; }
    private static string _checkedUid; // 判定済みユーザー（アカウント切り替えで再判定する）

    public static async UniTask RefreshAsync()
    {
        try
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) { IsAdmin = false; return; }
            if (user.UserId == _checkedUid) return;

            // forceRefresh: true でクレーム付与直後（再ログイン後）も最新のトークンを見る
            string jwt = await user.TokenAsync(true).AsUniTask();
            var parts = jwt.Split('.');
            if (parts.Length < 2) return;
            string payload = DecodeBase64Url(parts[1]);
            IsAdmin = System.Text.RegularExpressions.Regex.IsMatch(payload, "\"admin\"\\s*:\\s*true");
            _checkedUid = user.UserId;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AdminClaim] 管理者判定に失敗: {ex.Message}");
        }
    }

    private static string DecodeBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
