using System;

namespace BlazorServer.BffServiceOverrides
{
    // This is simply a copy of the internal class in Duende.Bff.  It likely isn't needed since it is only used to
    // make a check that configuration is valid, which could be left out, but is included here for direct compatibility
    // with the Default implementation in Duende.Bff.
    internal static class Util
    {
        internal static bool IsLocalUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            switch (url[0])
            {
                // Allows "/" or "/foo" but not "//" or "/\".
                // url is exactly "/"
                case '/' when url.Length == 1:
                    return true;
                // url doesn't start with "//" or "/\"
                case '/' when url[1] != '/' && url[1] != '\\':
                    return !HasControlCharacter(url.AsSpan(1));
                case '/':
                    return false;
                // Allows "~/" or "~/foo" but not "~//" or "~/\".
                case '~' when url.Length > 1 && url[1] == '/':
                {
                    // url is exactly "~/"
                    if (url.Length == 2)
                    {
                        return true;
                    }

                    // url doesn't start with "~//" or "~/\"
                    if (url[2] != '/' && url[2] != '\\')
                    {
                        return !HasControlCharacter(url.AsSpan(2));
                    }

                    return false;
                }
            }

            return false;


            static bool HasControlCharacter(ReadOnlySpan<char> readOnlySpan)
            {
                // URLs may not contain ASCII control characters.
                foreach (var t in readOnlySpan)
                {
                    if (char.IsControl(t))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}