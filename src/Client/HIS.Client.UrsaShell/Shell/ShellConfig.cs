using System;

namespace HIS.Client.UrsaShell.Shell;

public static class ShellConfig
{
    /// <summary>
    /// Controls whether login and startup launch the redesigned MainShellWindow or legacy MainWindow.
    /// Set environment variable GHIS_LEGACY_SHELL=1 to revert to the legacy shell at runtime.
    /// </summary>
    public static bool UseNewShell =>
        !string.Equals(Environment.GetEnvironmentVariable("GHIS_LEGACY_SHELL"), "1", StringComparison.OrdinalIgnoreCase);
}

