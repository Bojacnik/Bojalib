namespace Bojalib.Collections.Internal;

/// <summary>
/// Case-insensitive string equality that folds exactly like <see cref="Trie{TValue}"/> in ignore-case mode
/// (<see cref="char.ToUpperInvariant"/> per UTF-16 unit), so its hash index and its tree always agree. Hashing uses
/// the runtime's vectorized ordinal-ignore-case hash: any two strings equal here are equal under that too, so they
/// hash alike.
/// </summary>
internal sealed class FoldingComparer : IEqualityComparer<string>, IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    public static readonly FoldingComparer Instance = new();

    public bool Equals(string? x, string? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && Equals(x.AsSpan(), y));

    public bool Equals(ReadOnlySpan<char> alternate, string other)
    {
        if (alternate.Length != other.Length)
        {
            return false;
        }

        if (alternate.SequenceEqual(other))
        {
            return true; // common case, vectorized
        }

        for (int i = 0; i < alternate.Length; i++)
        {
            if (alternate[i] != other[i] && char.ToUpperInvariant(alternate[i]) != char.ToUpperInvariant(other[i]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(string obj) => GetHashCode(obj.AsSpan());

    public int GetHashCode(ReadOnlySpan<char> alternate) => string.GetHashCode(alternate, StringComparison.OrdinalIgnoreCase);

    public string Create(ReadOnlySpan<char> alternate) => alternate.ToString();
}
