using System.Text;

namespace Lore.Services;

// Stable selection among authored alternatives. Never uses the runtime's salted
// string hashes, random numbers, a person's name, or the current date.
public static class AuthoredText
{
    public static string Pick(IReadOnlyList<string> choices, string seed, int treatment = 0)
    {
        if (choices.Count == 0 || choices.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("An authored text entry has no complete alternatives.");
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(seed)) hash = unchecked((hash ^ b) * 16777619);
        int at = (int)(((long)(hash % (uint)choices.Count) + treatment % choices.Count + choices.Count) % choices.Count);
        return choices[at];
    }
}
