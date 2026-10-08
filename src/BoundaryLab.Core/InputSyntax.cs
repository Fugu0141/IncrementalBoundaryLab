namespace BoundaryLab.Core;

public static class InputSyntax
{
    private const string Binding = "._-+#@/:";
    private const string Hard = ",;!?。、！？";
    private const string Neutral = "()[]{}\"'";

    public static bool IsAllowed(char c) =>
        char.IsAsciiLetterOrDigit(c) ||
        IsSymbol(c) ||
        c == ' ';

    public static bool IsWordChar(char c) =>
        char.IsAsciiLetterOrDigit(c);

    public static bool IsBindingSymbol(char c) =>
        Binding.Contains(c);

    public static bool IsHardBoundary(char c) =>
        Hard.Contains(c) || c == ' ';

    public static bool IsNeutralSymbol(char c) =>
        Neutral.Contains(c);

    public static bool IsSymbol(char c) =>
        IsBindingSymbol(c) ||
        IsHardBoundary(c) ||
        IsNeutralSymbol(c);

    public static string Normalize(string input) =>
        new(input.Select(c => char.IsAsciiLetter(c)
            ? char.ToLowerInvariant(c)
            : c).ToArray());
}
