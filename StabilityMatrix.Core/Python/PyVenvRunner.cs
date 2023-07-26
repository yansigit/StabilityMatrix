﻿using System.Diagnostics.CodeAnalysis;
using System.Text;
using NLog;
using Salaros.Configuration;
using StabilityMatrix.Core.Exceptions;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Processes;

namespace StabilityMatrix.Core.Python;

/// <summary>
/// Python runner using a subprocess, mainly for venv support.
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public class PyVenvRunner : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public const string TorchPipInstallArgsCuda =
        "torch torchvision torchaudio --extra-index-url https://download.pytorch.org/whl/cu118"; 
    public const string TorchPipInstallArgsCpu =
        "torch torchvision torchaudio";
    public const string TorchPipInstallArgsDirectML = 
        "torch-directml";
    
    /// <summary>
    /// The process running the python executable.
    /// </summary>
    public AnsiProcess? Process { get; private set; }

    /// <summary>
    /// The path to the venv root directory.
    /// </summary>
    public DirectoryPath RootPath { get; }

    /// <summary>
    /// Name of the python binary folder.
    /// 'Scripts' on Windows, 'bin' on Unix.
    /// </summary>
    public static string RelativeBinPath => Compat.Switch(
        (PlatformKind.Windows, "Scripts"),
        (PlatformKind.Unix, "bin"));
    
    /// <summary>
    /// The relative path to the python executable.
    /// </summary>
    public static string RelativePythonPath => Compat.Switch(
        (PlatformKind.Windows, Path.Combine("Scripts", "python.exe")),
        (PlatformKind.Unix, Path.Combine("bin", "python3")));

    /// <summary>
    /// The full path to the python executable.
    /// </summary>
    public FilePath PythonPath => RootPath.JoinFile(RelativePythonPath);
    
    /// <summary>
    /// The relative path to the pip executable.
    /// </summary>
    public static string RelativePipPath => Compat.Switch(
        (PlatformKind.Windows, Path.Combine("Scripts", "pip.exe")),
        (PlatformKind.Unix, Path.Combine("bin", "pip3")));

    /// <summary>
    /// The full path to the pip executable.
    /// </summary>
    public FilePath PipPath => RootPath.JoinFile(RelativePipPath);
    
    /// <summary>
    /// List of substrings to suppress from the output.
    /// When a line contains any of these substrings, it will not be forwarded to callbacks.
    /// A corresponding Info log will be written instead.
    /// </summary>
    public List<string> SuppressOutput { get; } = new() { "fatal: not a git repository" };
    
    public PyVenvRunner(DirectoryPath path)
    {
        RootPath = path;
    }

    /// <returns>True if the venv has a Scripts\python.exe file</returns>
    public bool Exists() => PythonPath.Exists;

    /// <summary>
    /// Creates a venv at the configured path.
    /// </summary>
    public async Task Setup(bool existsOk = false)
    {
        if (!existsOk && Exists())
        {
            throw new InvalidOperationException("Venv already exists");
        }

        // Create RootPath if it doesn't exist
        RootPath.Create();

        // Create venv (copy mode if windows)
        var args = new string[] { "-m", "virtualenv", 
            Compat.IsWindows ? "--always-copy" : "", RootPath };
        var venvProc = ProcessRunner.StartAnsiProcess(PyRunner.PythonExePath, args);
        await venvProc.WaitForExitAsync();

        // Check return code
        var returnCode = venvProc.ExitCode;
        if (returnCode != 0)
        {
            var output = await venvProc.StandardOutput.ReadToEndAsync();
            output += await venvProc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Venv creation failed with code {returnCode}: {output}");
        }
    }

    /// <summary>
    /// Set current python path to pyvenv.cfg
    /// This should be called before using the venv, in case user moves the venv directory.
    /// </summary>
    private void SetPyvenvCfg(string pythonDirectory)
    {
        // Skip if we are not created yet
        if (!Exists()) return;

        // Path to pyvenv.cfg
        var cfgPath = Path.Combine(RootPath, "pyvenv.cfg");
        if (!File.Exists(cfgPath))
        {
            throw new FileNotFoundException("pyvenv.cfg not found", cfgPath);
        }
        
        Logger.Info("Updating pyvenv.cfg with embedded Python directory {PyDir}", pythonDirectory);
        
        // Insert a top section
        var topSection = "[top]" + Environment.NewLine;
        var cfg = new ConfigParser(topSection + File.ReadAllText(cfgPath));
        
        // Need to set all path keys - home, base-prefix, base-exec-prefix, base-executable
        cfg.SetValue("top", "home", pythonDirectory);
        cfg.SetValue("top", "base-prefix", pythonDirectory);
        
        cfg.SetValue("top", "base-exec-prefix", pythonDirectory);
        
        cfg.SetValue("top", "base-executable",
            Path.Combine(pythonDirectory, Compat.IsWindows ? "python.exe" : RelativePythonPath));
        
        // Convert to string for writing, strip the top section
        var cfgString = cfg.ToString()!.Replace(topSection, "");
        File.WriteAllText(cfgPath, cfgString);
    }

    /// <summary>
    /// Run a pip install command. Waits for the process to exit.
    /// workingDirectory defaults to RootPath.
    /// </summary>
    public async Task PipInstall(string args, string? workingDirectory = null, Action<ProcessOutput>? outputDataReceived = null)
    {
        if (!File.Exists(PipPath))
        {
            throw new FileNotFoundException("pip not found", PipPath);
        }
        
        // Record output for errors
        var output = new StringBuilder();
        
        var outputAction = outputDataReceived == null ? null : new Action<ProcessOutput>(s =>
        {
            Logger.Debug($"Pip output: {s.Text}");
            // Record to output
            output.Append(s.Text);
            // Forward to callback
            outputDataReceived(s);
        });
        
        SetPyvenvCfg(PyRunner.PythonDir);
        RunDetached($"-m pip install {args}", outputAction, workingDirectory: workingDirectory ?? RootPath);
        await Process.WaitForExitAsync();
        
        // Check return code
        if (Process.ExitCode != 0)
        {
            throw new ProcessException(
                $"pip install failed with code {Process.ExitCode}: {output.ToString().ToRepr()}");
        }
    }

    [MemberNotNull(nameof(Process))]
    public void RunDetached(
        string arguments, 
        Action<ProcessOutput>? outputDataReceived,
        Action<int>? onExit = null,
        bool unbuffered = true, 
        string workingDirectory = "")
    {
        if (!Exists())
        {
            throw new InvalidOperationException("Venv python process does not exist");
        }
        SetPyvenvCfg(PyRunner.PythonDir);
        
        Logger.Debug($"Launching RunDetached at {PythonPath} with args {arguments}");

        var filteredOutput = outputDataReceived == null ? null : new Action<ProcessOutput>(s =>
        {
            if (SuppressOutput.Any(s.Text.Contains))
            {
                Logger.Info("Filtered output: {S}", s);
                return;
            }
            outputDataReceived.Invoke(s);
        });

        var env = new Dictionary<string, string>
        {
            // Disable pip caching - uses significant memory for large packages like torch
            {"PIP_NO_CACHE_DIR", "true"} 
        };

        // On windows, add portable git 
        if (Compat.IsWindows)
        {
            var portableGit = GlobalConfig.LibraryDir.JoinDir("PortableGit", "bin");
            env["PATH"] = Compat.GetEnvPathWithExtensions(portableGit);
        }
        
        if (unbuffered)
        {
            env["PYTHONUNBUFFERED"] = "1";

            // If arguments starts with -, it's a flag, insert `u` after it for unbuffered mode
            if (arguments.StartsWith('-'))
            {
                arguments = arguments.Insert(1, "u");
            }
            // Otherwise insert -u at the beginning
            else
            {
                arguments = "-u " + arguments;
            }
        }

        Process = ProcessRunner.StartAnsiProcess(PythonPath, arguments, 
            workingDirectory: workingDirectory,
            outputDataReceived: filteredOutput,
            environmentVariables: env);

        if (onExit != null)
        {
            Process.EnableRaisingEvents = true;
            Process.Exited += (_, _) => onExit(Process.ExitCode);
        }
    }

    public void Dispose()
    {
        Process?.Kill();
        GC.SuppressFinalize(this);
    }
}