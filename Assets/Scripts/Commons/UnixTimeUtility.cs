using System;

public static class UnixTimeUtility
{
    private static DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);

    /// <summary>
    /// DateTimeからUnixTimeへ変換
    /// </summary>
    /// <param name="dateTime"></param>
    /// <returns></returns>
    public static long GetUnixTime(DateTime dateTime)
    {
        return (long)(dateTime - UnixEpoch).TotalSeconds;
    }
}
    