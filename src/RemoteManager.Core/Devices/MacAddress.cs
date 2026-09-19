namespace RemoteManager.Core.Devices;

public static class MacAddress
{
    public static string Normalize(string value)
    {
        var text = value.Trim();
        string compact;
        if (text.Length == 12) compact = text;
        else if (text.Length == 17 && (text[2] == ':' || text[2] == '-'))
        {
            var separator = text[2];
            if (new[] { 2, 5, 8, 11, 14 }.Any(i => text[i] != separator))
                throw new ArgumentException("Use six hexadecimal pairs separated by colons or hyphens.");
            compact = text.Replace(separator.ToString(), "");
        }
        else throw new ArgumentException("Enter a 12-digit MAC address, optionally separated by colons or hyphens.");
        if (compact.Length != 12 || !compact.All(Uri.IsHexDigit))
            throw new ArgumentException("MAC addresses must contain exactly six hexadecimal bytes.");
        return string.Join(":", Convert.FromHexString(compact).Select(b => b.ToString("X2")));
    }
}
