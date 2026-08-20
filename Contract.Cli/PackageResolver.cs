using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Contract.Cli;

/// <summary>
/// Manages Purr package cache and installation.
/// Packages are cached at ~/.purr/cache/{name}/{version}/
/// and linked into projects at .purr/packages/{name}/
/// </summary>
public class PackageResolver
{
    private readonly PurrClient _client;

    /// <summary>Global cache directory (~/.purr/cache).</summary>
    public string CacheDir { get; }

    /// <summary>Local packages directory inside a project (.purr/packages).</summary>
    public string? ProjectPackagesDir { get; }

    public PackageResolver(PurrClient client, string? projectRoot = null)
    {
        _client = client;
        CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".purr", "cache");
        if (projectRoot != null)
            ProjectPackagesDir = Path.Combine(projectRoot, ".purr", "packages");
    }

    /// <summary>
    /// Installs a package by name (latest) or name@version.
    /// Downloads to cache, then links into the project.
    /// Returns the installed package info.
    /// </summary>
    public async Task<PurrClient.PackageInfo?> InstallAsync(string name, string? version = null)
    {
        // Parse name@version syntax
        if (name.Contains('@'))
        {
            var parts = name.Split('@', 2);
            name = parts[0];
            version = parts[1];
        }

        Console.Write($"Resolving {name}...");
        PurrClient.PackageInfo? info;
        if (version != null)
            info = await _client.GetPackageVersionAsync(name, version);
        else
            info = await _client.GetPackageAsync(name);

        if (info == null)
        {
            Console.WriteLine($" not found");
            return null;
        }

        version = info.Version;
        Console.WriteLine($" {info.Name}@{version}");

        // Check if already cached
        string cachePath = Path.Combine(CacheDir, info.Name, info.Version);
        if (!Directory.Exists(cachePath))
        {
            await DownloadToCacheAsync(info, cachePath);
        }
        else
        {
            Console.WriteLine("  (cached)");
        }

        // Link into project
        if (ProjectPackagesDir != null)
        {
            LinkPackage(info.Name, cachePath);
        }

        // Report download
        await _client.ReportDownloadAsync(info.Name);

        return info;
    }

    /// <summary>
    /// Removes a package from the project (unlinks it).
    /// Does not remove from global cache.
    /// </summary>
    public bool Remove(string name)
    {
        if (ProjectPackagesDir == null) return false;
        string linkPath = Path.Combine(ProjectPackagesDir, name);
        if (!Directory.Exists(linkPath)) return false;

        // Remove junction/symlink
        var info = new DirectoryInfo(linkPath);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            // It's a symlink/junction
            Directory.Delete(linkPath, false);
        }
        else
        {
            Directory.Delete(linkPath, true);
        }
        return true;
    }

    /// <summary>
    /// Lists installed packages in the project.
    /// </summary>
    public List<(string Name, string Version)> ListInstalled()
    {
        var result = new List<(string, string)>();
        if (ProjectPackagesDir == null || !Directory.Exists(ProjectPackagesDir))
            return result;

        foreach (var dir in Directory.GetDirectories(ProjectPackagesDir))
        {
            string name = Path.GetFileName(dir);
            string versionFile = Path.Combine(dir, ".purr-version");
            string version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
            result.Add((name, version));
        }
        return result;
    }

    /// <summary>
    /// Resolves a package import path. Returns the directory containing .oil files,
    /// or null if not found.
    /// </summary>
    public string? ResolveImport(string packageName)
    {
        if (ProjectPackagesDir == null) return null;

        string pkgDir = Path.Combine(ProjectPackagesDir, packageName);
        if (!Directory.Exists(pkgDir)) return null;

        // Look for bin/ directory with compiled .oil files
        string binDir = Path.Combine(pkgDir, "bin");
        if (Directory.Exists(binDir)) return binDir;

        // Fall back to the package root
        return pkgDir;
    }

    private async Task DownloadToCacheAsync(PurrClient.PackageInfo info, string cachePath)
    {
        Console.Write("  downloading...");

        if (string.IsNullOrEmpty(info.GitUrl))
        {
            Console.WriteLine(" no download URL available");
            return;
        }

        // Clone the git repo to cache
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var psi = new System.Diagnostics.ProcessStartInfo("git", $"clone --depth 1 \"{info.GitUrl}\" \"{cachePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null)
        {
            Console.WriteLine(" failed to start git");
            return;
        }

        string stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Console.WriteLine($" failed: {stderr.Trim()}");
            return;
        }

        Console.WriteLine(" ok");
    }

    private void LinkPackage(string name, string cachePath)
    {
        if (ProjectPackagesDir == null) return;
        Directory.CreateDirectory(ProjectPackagesDir);

        string linkPath = Path.Combine(ProjectPackagesDir, name);
        if (Directory.Exists(linkPath)) return; // already linked

        // Check if git is available for symlinks, otherwise copy
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{linkPath}\" \"{cachePath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch
        {
            // Fallback: copy the directory
            CopyDirectory(cachePath, linkPath);
        }

        // Write version marker
        string versionFile = Path.Combine(linkPath, ".purr-version");
        if (File.Exists(Path.Combine(cachePath, ".purr-version")))
            File.Copy(Path.Combine(cachePath, ".purr-version"), versionFile, true);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
