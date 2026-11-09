using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// サービスアカウントの鍵で、Google の API のアクセストークンと引き換える署名つきの札（JWT・RS256）を作る
/// （2026-11-09。お知らせ通知の送信画面を Windows 用にビルドしても、node なしで送れるように。FcmNotice が使う）。
///
/// UnityEngine は使わない（Unity と同じ Mono で動くかを、エディタの外でも確かめられるように）。
/// 鍵の private_key は Google が配る PKCS#8 の PEM（-----BEGIN PRIVATE KEY-----）
/// </summary>
public static class GoogleJwt
{
    /// <summary>有効1時間の JWT。パソコンの時計が少し進んでいても通るよう、発行時刻を1分前にする</summary>
    public static string Create(string clientEmail, string privateKeyPem, string scope, string audience, DateTime nowUtc)
    {
        long iat = (long)(nowUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds - 60;
        string header = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        string claims = "{\"iss\":\"" + Escape(clientEmail) + "\",\"scope\":\"" + Escape(scope) + "\",\"aud\":\""
                        + Escape(audience) + "\",\"iat\":" + iat + ",\"exp\":" + (iat + 3600) + "}";
        string unsigned = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(claims));
        byte[] signature;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(ParsePkcs8(privateKeyPem));
            signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        return unsigned + "." + Base64Url(signature);
    }

    /// <summary>PKCS#8 の PEM から RSA の鍵を取り出す（PrivateKeyInfo の中の RSAPrivateKey を読む）</summary>
    public static RSAParameters ParsePkcs8(string pem)
    {
        if (string.IsNullOrEmpty(pem) || !pem.Contains("BEGIN PRIVATE KEY"))
            throw new FormatException("鍵の形式が違います（BEGIN PRIVATE KEY がありません）");
        var b64 = new StringBuilder();
        foreach (string line in pem.Replace("\r", "").Split('\n'))
            if (!line.StartsWith("-----")) b64.Append(line.Trim());
        var info = new Der(Convert.FromBase64String(b64.ToString()));
        info.Enter(0x30);  // PrivateKeyInfo
        info.Skip(0x02);   // version
        info.Skip(0x30);   // privateKeyAlgorithm（rsaEncryption）
        var key = new Der(info.Read(0x04));
        key.Enter(0x30);   // RSAPrivateKey
        key.Skip(0x02);    // version
        byte[] n = key.Integer(), e = key.Integer(), d = key.Integer(), p = key.Integer(), q = key.Integer(),
            dp = key.Integer(), dq = key.Integer(), qi = key.Integer();
        // 長さをそろえないと読み込めない実装がある（D は法と同じ長さ、残りはその半分）
        int k = n.Length, h = (k + 1) / 2;
        return new RSAParameters
        {
            Modulus = n, Exponent = e, D = Pad(d, k),
            P = Pad(p, h), Q = Pad(q, h), DP = Pad(dp, h), DQ = Pad(dq, h), InverseQ = Pad(qi, h),
        };
    }

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static byte[] Pad(byte[] b, int len)
    {
        if (b.Length >= len) return b;
        var r = new byte[len];
        Buffer.BlockCopy(b, 0, r, len - b.Length, b.Length);
        return r;
    }

    /// <summary>鍵を読むのに要るぶんだけの DER 読み取り</summary>
    private sealed class Der
    {
        private readonly byte[] _b;
        private int _i;

        public Der(byte[] b) { _b = b; }

        /// <summary>入れ物（SEQUENCE など）の中へ進む</summary>
        public void Enter(byte tag) { Header(tag); }

        public void Skip(byte tag)
        {
            int len = Header(tag); // 「_i += Header(tag)」だと Header が進める前の _i に足してしまう
            _i += len;
        }

        public byte[] Read(byte tag)
        {
            int len = Header(tag);
            var r = new byte[len];
            Buffer.BlockCopy(_b, _i, r, 0, len);
            _i += len;
            return r;
        }

        /// <summary>INTEGER（符号のための先頭の 0 は取る）</summary>
        public byte[] Integer()
        {
            byte[] v = Read(0x02);
            int s = 0;
            while (s < v.Length - 1 && v[s] == 0) s++;
            if (s == 0) return v;
            var r = new byte[v.Length - s];
            Buffer.BlockCopy(v, s, r, 0, r.Length);
            return r;
        }

        private int Header(byte tag)
        {
            if (_i >= _b.Length || _b[_i] != tag)
                throw new FormatException($"鍵を読めませんでした（{_i} バイト目）");
            _i++;
            int len = _b[_i++];
            if (len >= 0x80)
            {
                int bytes = len & 0x7F;
                len = 0;
                for (int j = 0; j < bytes; j++) len = (len << 8) | _b[_i++];
            }
            if (len < 0 || _i + len > _b.Length) throw new FormatException("鍵が途中で切れています");
            return len;
        }
    }
}
