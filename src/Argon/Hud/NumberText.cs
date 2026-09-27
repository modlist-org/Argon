using TMPro;

namespace Argon;

/// <summary>Writes integers into TMP text through a shared char buffer: no per-update string allocation.</summary>
internal static partial class NumberText
{
    private static readonly char[] Buffer = new char[16];

    internal static void Set(TMP_Text text, int value)
    {
        var length = Write(value, Buffer);
        text.SetText(Buffer, Buffer.Length - length, length);
    }
}
