namespace InstallationSolution.Constants
{
    /// <summary>
    /// 应用程序配置常量
    /// </summary>
    public static class AppConfig
    {
        // ── 窗口配置 ──────────────────────────────────────────────
        public const int DefaultWindowWidth = 652;
        public const int DefaultWindowHeight = 414;
        public const int MinWindowWidth = 652;
        public const int MinWindowHeight = 414;

        // ── 重试配置 ──────────────────────────────────────────────
        public const int MaxRetryAttempts = 3;
        public const int FileOperationRetryDelayMs = 200;
        public const int FileDeleteRetryDelayMs = 500;
        public const int FileDeleteMaxRetries = 5;

        // ── 缓存配置 ──────────────────────────────────────────────
        public const long MinValidZipSizeBytes = 1_000_000; // 1 MB
        public const int StreamCopyBufferSize = 81920; // 80 KB

        // ── 超时配置 ──────────────────────────────────────────────
        public const int PowerShellTimeoutMs = 5000;
        public const int ProcessWaitTimeoutMs = 6000;

        // ── 临时目录名称 ──────────────────────────────────────────
        public const string TempBuildDirPrefix = "InstallationSolution_NonPackaged_";
        public const string TempSelfBuildDir = "InstallationSolution_SelfBuild";
        public const string TempGuardSourcePrefix = "GuardSource_";
        public const string TempGuardPublishPrefix = "GuardPublish_";

        // ── 文件名称 ──────────────────────────────────────────────
        public const string InstallerUIZipName = "InstallerUI.zip";
        public const string InstallerUIZipPattern = "InstallerUI_{0}.zip"; // {0} = GUID
        public const string DebugLogFileName = "dotnet_publish_log.txt";

        // ── 设置键 ────────────────────────────────────────────────
        public const string LastOutputPathKey = "LastOutputPath";
    }
}
