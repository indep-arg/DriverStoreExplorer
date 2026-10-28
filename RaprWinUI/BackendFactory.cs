using Rapr.Lang;
using Rapr.Utils;

namespace Rapr.WinUI
{
    /// <summary>
    /// Picks the driver store backend. The WinUI app needs Windows 10, where the native
    /// driver store API and pnputil are always there; DISM may be missing.
    /// </summary>
    internal static class BackendFactory
    {
        public const string Native = "Native";
        public const string Dism = "DISM";
        public const string PnpUtil = "PnpUtil";

        public static string Validate(string? option)
        {
            switch (option)
            {
                case Dism:
                    return DismUtil.IsDismAvailable ? Dism : Native;
                case PnpUtil:
                    return PnpUtil;
                default:
                    return Native;
            }
        }

        public static IDriverStore CreateOnline(string option)
        {
            switch (option)
            {
                case Dism:
                    return new DismUtil();
                case PnpUtil:
                    return new Utils.PnpUtil();
                default:
                    return new NativeDriverStore();
            }
        }

        // pnputil can't open an offline image, so it falls back to DISM there, as in the WinForms app.
        public static IDriverStore CreateOffline(string option, string imagePath) =>
            option == Native ? new NativeDriverStore(imagePath) : (IDriverStore)new DismUtil(imagePath);

        public static string DisplayName(string option)
        {
            switch (option)
            {
                case Dism:
                    return Strings.Get("Backend_Dism");
                case PnpUtil:
                    return Strings.Get("Backend_PnpUtil");
                default:
                    return Strings.Get("Backend_Native");
            }
        }

        public static string MenuText(string option)
        {
            switch (option)
            {
                case Dism:
                    return Language.Menu_Options_UseDISM;
                case PnpUtil:
                    return Language.Menu_Options_UsePnpUtil;
                default:
                    return Language.Menu_Options_UseNativeDriverStore;
            }
        }
    }
}
