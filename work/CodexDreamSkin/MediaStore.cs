using System.Security.Cryptography;

namespace CodexDreamSkin;

// 只保留当前文件路径和随机能力令牌，文件内容由 CDP 按块读取。
internal sealed class MediaStore
{
    internal sealed record Entry(string Path, string Mime, string Source);
    private Entry? current;

    internal string SetMedia(string path, string mime)
    {
        var entry = new Entry(path, mime, "dream-media:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
        Volatile.Write(ref current, entry); return entry.Source;
    }

    internal Entry? Find(string source)
    {
        var entry = Volatile.Read(ref current);
        return entry?.Source == source ? entry : null;
    }

    internal void Clear() => Volatile.Write(ref current, null);
}
