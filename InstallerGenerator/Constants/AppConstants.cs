namespace InstallerGenerator.Constants;

/// <summary>
/// 应用程序全局常量
/// </summary>
public static class AppConstants
{
    public static class Settings
    {
        public const string Theme        = "AppTheme";
        public const string Material     = "AppMaterial";
        public const string Sound        = "EnableSound";
        public const string PanePosition = "PanePosition";
        public const string LastOutput   = "LastOutputPath";
    }

    public static class Build
    {
        public const string GuardSourceDir  = "GuardSource";
        public const string PayloadDir      = "Payload";
        public const string GuardCsproj     = "InstallerGuard.csproj";
        public const string GuardExe        = "InstallerGuard.exe";
        public const string InstallerSuffix = "_Installer.exe";
        public const string TempDirPrefix   = "GuardPublish_";
        public const string DotnetExe       = "dotnet";
        public const string PublishArgs     = "publish \"{0}\" -r {2} -c Release -o \"{1}\" --no-self-contained";
    }

    public static class FileExtensions
    {
        public static readonly string[] Package =
            [".msix", ".msixbundle", ".appx", ".appxbundle"];
    }

    public static class Win32
    {
        public const int    SW_RESTORE           = 9;
        public const int    SHCNE_ASSOCCHANGED   = 0x8000000;
        public const uint   SHCNF_IDLIST         = 0x1000;
    }
}
