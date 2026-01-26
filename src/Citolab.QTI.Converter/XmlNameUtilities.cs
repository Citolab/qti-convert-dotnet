using System.Text;

namespace Citolab.QTI.Converter;

internal static class XmlNameUtilities
{
    public static string Kabobify(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length + 8);
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (char.IsUpper(ch))
            {
                if (i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '-')
                {
                    sb.Append('-');
                }
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch == '_')
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
