namespace Argon;

internal static partial class NumberText
{
    /// <summary>Writes <paramref name="value"/> right-aligned into <paramref name="buffer"/>; returns its length.</summary>
    internal static int Write(int value, char[] buffer)
    {
        var position = buffer.Length;
        var magnitude = value < 0 ? -(long)value : value;
        do
        {
            buffer[--position] = (char)('0' + (int)(magnitude % 10));
            magnitude /= 10;
        }
        while (magnitude > 0);

        if (value < 0) buffer[--position] = '-';
        return buffer.Length - position;
    }
}
