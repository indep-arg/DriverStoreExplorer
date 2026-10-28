using System.Resources;

using Rapr.Lang;

namespace Rapr.WinUI
{
    /// <summary>
    /// Text for the WinUI front end. Its own strings live in Strings\WinUIStrings.resx; the ones it
    /// shares with the WinForms app come from Rapr\Lang\Language.resx. Falls back to the name.
    /// </summary>
    internal static class Strings
    {
        private static readonly ResourceManager Own =
            new ResourceManager("Rapr.WinUI.Strings.WinUIStrings", typeof(Strings).Assembly);

        public static string Get(string name)
        {
            return Own.GetString(name, Language.Culture)
                ?? Language.ResourceManager.GetString(name, Language.Culture)
                ?? name;
        }
    }
}
