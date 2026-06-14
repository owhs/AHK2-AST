using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using AHK2AST.UI;

public class NimBuildContent : DockContent
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    private Label _lblScoopStatus;
    private Label _lblNimStatus;
    private Label _lblGccStatus;
    private Label _lblWinimStatus;
    private Label _lblUpxStatus;
    
    private Button _btnInstall;
    private Button _btnBuild;
    
    private TextBox _txtOutputPath;
    private ThemedComboBox _cmbBuildMode;
    private CheckBox _chkUpx;
    private CheckBox _chkTreeShake;
    private CheckBox _chkDebug;
    private CheckBox _chkLto;
    private CheckBox _chkArc;
    private CheckBox _chkPanics;
    
    private TextBox _txtAppName;
    private TextBox _txtVersion;
    private TextBox _txtProduct;
    private TextBox _txtDescription;
    private TextBox _txtCopyright;
    private TextBox _txtIconPath;
    
    private RichTextBox _txtConsole;
    
    private Func<string> _getSourceCode;
    private Func<string> _getFilePath;
    private dynamic _engine;
    private string _lastActiveFilePath;
    private string _transpiledNimOverride = null;
    
    public NimBuildContent(Func<string> getSourceCode, Func<string> getFilePath, dynamic engine)
    {
        _getSourceCode = getSourceCode;
        _getFilePath = getFilePath;
        _engine = engine;
        
        Text = "Nim Build Manager";
        BackColor = WbTheme.Crust;
        
        InitializeComponents();
        RefreshStatus();
        LoadSettings();
    }

    private void RefreshStatus()
    {
        bool scoopOk = IsScoopInstalled();
        string nimPath;
        bool nimOk = IsNimInstalled(out nimPath);
        string gccPath;
        bool gccOk = IsGccInstalled(out gccPath);
        bool winimOk = IsWinimInstalled();
        string upxPath;
        bool upxOk = IsUpxInstalled(out upxPath);
        
        SetStatusLabel(_lblScoopStatus, scoopOk, "Scoop Package Manager");
        SetStatusLabel(_lblNimStatus, nimOk, "Nim Compiler");
        SetStatusLabel(_lblGccStatus, gccOk, "MinGW GCC");
        SetStatusLabel(_lblWinimStatus, winimOk, "winim Library");
        SetStatusLabel(_lblUpxStatus, upxOk, "UPX Packer (Optional)", true);
        
        _btnInstall.Enabled = !scoopOk || !nimOk || !gccOk || !winimOk || !upxOk;
        _btnBuild.Enabled = nimOk && gccOk;
    }

    private void SetStatusLabel(Label lbl, bool ok, string name, bool optional = false)
    {
        if (ok)
        {
            lbl.Text = "✔ " + name + ": Installed";
            lbl.ForeColor = WbTheme.Green;
        }
        else
        {
            if (optional)
            {
                lbl.Text = "⚠ " + name + ": Missing";
                lbl.ForeColor = WbTheme.Yellow;
            }
            else
            {
                lbl.Text = "✘ " + name + ": Missing";
                lbl.ForeColor = WbTheme.Red;
            }
        }
    }

    private bool IsScoopInstalled()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return File.Exists(Path.Combine(userProfile, "scoop\\shims\\scoop.cmd")) ||
               Directory.Exists(Path.Combine(userProfile, "scoop"));
    }

    private bool IsNimInstalled(out string path)
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string scoopNim = Path.Combine(userProfile, "scoop\\apps\\nim\\current\\bin\\nim.exe");
        if (File.Exists(scoopNim)) { path = scoopNim; return true; }
        
        path = FindInPath("nim.exe");
        return !string.IsNullOrEmpty(path);
    }

    private bool IsGccInstalled(out string path)
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string scoopGcc = Path.Combine(userProfile, "scoop\\apps\\mingw\\current\\bin\\gcc.exe");
        if (File.Exists(scoopGcc)) { path = scoopGcc; return true; }
        
        path = FindInPath("gcc.exe");
        return !string.IsNullOrEmpty(path);
    }

    private bool IsWinimInstalled()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pkgsDir = Path.Combine(userProfile, ".nimble\\pkgs2");
        if (Directory.Exists(pkgsDir))
        {
            if (Directory.GetDirectories(pkgsDir).Any(d => Path.GetFileName(d).StartsWith("winim", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        string pkgcacheDir = Path.Combine(userProfile, ".nimble\\pkgcache\\githubcom_khchenwinim");
        if (Directory.Exists(pkgcacheDir))
            return true;
        return false;
    }

    private bool IsUpxInstalled(out string path)
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string scoopUpx = Path.Combine(userProfile, "scoop\\apps\\upx\\current\\upx.exe");
        if (File.Exists(scoopUpx)) { path = scoopUpx; return true; }
        
        path = FindInPath("upx.exe");
        return !string.IsNullOrEmpty(path);
    }

    private static string FindInPath(string filename)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                string fullPath = Path.Combine(dir, filename);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch { }
        }
        return null;
    }

    private void InstallDependencies()
    {
        _txtConsole.Clear();
        AppendConsole("Commencing toolchain install process in background...\n", WbTheme.Sky);
        
        string script = @"
$ProgressPreference = 'SilentlyContinue'
Write-Host '---------------------------------------------'
Write-Host 'AHK# AST Nim Toolchain Installer'
Write-Host '---------------------------------------------'
if (!(Get-Command scoop -ErrorAction SilentlyContinue)) {
    Write-Host 'Scoop is not installed. Installing Scoop...'
    Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression
    # Refresh PATH in current session
    $env:PATH = ""$env:USERPROFILE\scoop\shims;"" + $env:PATH
} else {
    Write-Host 'Scoop is already installed.'
}
Write-Host 'Installing Nim, MinGW, and UPX via Scoop...'
scoop install nim mingw upx
Write-Host 'Checking nimble package manager...'
if (Get-Command nimble -ErrorAction SilentlyContinue) {
    Write-Host 'Installing winim community library...'
    nimble install -y winim
} else {
    Write-Host 'ERROR: nimble not found, cannot install winim.'
}
Write-Host '---------------------------------------------'
Write-Host 'Process finished!'
";
        
        string tempScript = Path.Combine(Path.GetTempPath(), "install_nim_toolchain.ps1");
        try
        {
            File.WriteAllText(tempScript, script, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppendConsole("ERROR saving setup script: " + ex.Message + "\n", WbTheme.Red);
            return;
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + tempScript + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        
        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                this.BeginInvoke(new Action(() => {
                    AppendConsole(e.Data + "\n", WbTheme.Text);
                }));
            }
        };
        
        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                this.BeginInvoke(new Action(() => {
                    AppendConsole("ERROR: " + e.Data + "\n", WbTheme.Red);
                }));
            }
        };
        
        proc.Exited += (s, e) =>
        {
            this.BeginInvoke(new Action(() => {
                AppendConsole("Installation process completed with exit code " + proc.ExitCode + "\n", proc.ExitCode == 0 ? WbTheme.Green : WbTheme.Red);
                RefreshStatus();
                try { File.Delete(tempScript); } catch {}
            }));
        };
        
        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            AppendConsole("ERROR starting installation process: " + ex.Message + "\n", WbTheme.Red);
        }
    }

    public void TriggerBuildFromTranspiler(string transpiledNimCode)
    {
        _transpiledNimOverride = transpiledNimCode;
        _txtConsole.Clear();
        AppendConsole("Pre-transpiled Nim source code loaded from workspace editor.\nReady to compile. Click 'Compile & Build Standalone EXE' to build.\n", WbTheme.Sky);
    }

    private void BuildExecutable(string transpiledNimOverride = null)
    {
        _txtConsole.Clear();
        UpdateActiveFilePath();
        string activeFilePath = _getFilePath();
        if (string.IsNullOrEmpty(activeFilePath))
        {
            MessageBox.Show("Please open or save an AHK script first before building.", "No Active File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        string ahkSource = _getSourceCode();
        if (string.IsNullOrEmpty(ahkSource) && transpiledNimOverride == null && _transpiledNimOverride == null)
        {
            MessageBox.Show("The active script is empty.", "Empty Script", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        SaveSettings();
        
        AppendConsole("=== STARTING NIM COMPILATION BUILD ===\n", WbTheme.Lavender);
        AppendConsole("Source file: " + activeFilePath + "\n", WbTheme.Overlay0);
        
        string transpiledNim = "";
        if (transpiledNimOverride != null)
        {
            AppendConsole("Using pre-transpiled Nim source from Pipeline Flow...\n", WbTheme.Sky);
            transpiledNim = transpiledNimOverride;
        }
        else if (_transpiledNimOverride != null)
        {
            AppendConsole("Using pre-transpiled Nim source code...\n", WbTheme.Sky);
            transpiledNim = _transpiledNimOverride;
        }
        else
        {
            try
            {
                AppendConsole("Parsing AHK AST...\n", WbTheme.Text);
                var rootNode = _engine.Parse(ahkSource);
                
                if (_chkTreeShake.Checked)
                {
                    AppendConsole("Running AST Tree Shaker...\n", WbTheme.Text);
                    dynamic shaker = Activator.CreateInstance(typeof(AhkAstEngine).Assembly.GetType("AHK2AST.Plugins.TreeShakerPlugin"));
                    dynamic config = Activator.CreateInstance(typeof(AhkAstEngine).Assembly.GetType("AHK2AST.Plugins.TreeShakerConfig"));
                    config.Profile = Enum.ToObject(typeof(AhkAstEngine).Assembly.GetType("AHK2AST.Plugins.TreeShakingProfile"), 1); // Aggressive
                    shaker.Config = config;
                    shaker.Execute(rootNode);
                }
                
                AppendConsole("Transpiling AHK AST to Nim...\n", WbTheme.Text);
                dynamic transpiler = Activator.CreateInstance(typeof(AhkAstEngine).Assembly.GetType("AHK2AST.Plugins.NimTranspilerPlugin"));
                dynamic transConfig = Activator.CreateInstance(typeof(AhkAstEngine).Assembly.GetType("AHK2AST.Plugins.NimTranspilerConfig"));
                transConfig.AddImports = true;
                transConfig.OutputPath = "";
                transConfig.IncludeDebugging = _chkDebug.Checked;
                transpiler.Config = transConfig;
                transpiler.Initialize(_engine);
                transpiledNim = (string)transpiler.Execute(rootNode);
            }
            catch (Exception ex)
            {
                AppendConsole("FATAL TRANSPILATION ERROR: " + ex.Message + "\n", WbTheme.Red);
                return;
            }
        }
        
        string scratchDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch");
        if (!Directory.Exists(scratchDir)) Directory.CreateDirectory(scratchDir);
        
        string tempNimPath = Path.Combine(scratchDir, "build_temp.nim");
        string tempRcPath = Path.Combine(scratchDir, "build_temp.rc");
        string tempResPath = Path.Combine(scratchDir, "build_temp.res");
        
        bool hasMeta = !string.IsNullOrEmpty(_txtAppName.Text) || 
                       !string.IsNullOrEmpty(_txtIconPath.Text) || 
                       !string.IsNullOrEmpty(_txtVersion.Text);
                       
        if (hasMeta)
        {
            AppendConsole("Generating Windows Resource Script (.rc)...\n", WbTheme.Text);
            var rcBuilder = new StringBuilder();
            
            if (!string.IsNullOrEmpty(_txtVersion.Text))
            {
                string cleanVer = _txtVersion.Text.Replace(".", ",");
                if (cleanVer.Split(',').Length < 4) cleanVer += ",0";
                
                rcBuilder.AppendLine("1 VERSIONINFO");
                rcBuilder.AppendLine("FILEVERSION " + cleanVer);
                rcBuilder.AppendLine("PRODUCTVERSION " + cleanVer);
                rcBuilder.AppendLine("FILEFLAGSMASK 0x3fL");
                rcBuilder.AppendLine("FILEFLAGS 0x0L");
                rcBuilder.AppendLine("FILEOS 0x40004L");
                rcBuilder.AppendLine("FILETYPE 0x1L");
                rcBuilder.AppendLine("FILESUBTYPE 0x0L");
                rcBuilder.AppendLine("BEGIN");
                rcBuilder.AppendLine("    BLOCK \"StringFileInfo\"");
                rcBuilder.AppendLine("    BEGIN");
                rcBuilder.AppendLine("        BLOCK \"040904b0\"");
                rcBuilder.AppendLine("        BEGIN");
                rcBuilder.AppendLine("            VALUE \"CompanyName\", \"" + EscapeRcString(_txtCopyright.Text) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"FileDescription\", \"" + EscapeRcString(_txtDescription.Text) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"FileVersion\", \"" + EscapeRcString(_txtVersion.Text) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"InternalName\", \"" + EscapeRcString(_txtAppName.Text) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"LegalCopyright\", \"" + EscapeRcString(_txtCopyright.Text) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"OriginalFilename\", \"" + EscapeRcString(Path.GetFileName(_txtOutputPath.Text)) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"ProductName\", \"" + EscapeRcString(_txtProduct.Text) + "\\0\"");
                rcBuilder.AppendLine("            VALUE \"ProductVersion\", \"" + EscapeRcString(_txtVersion.Text) + "\\0\"");
                rcBuilder.AppendLine("        END");
                rcBuilder.AppendLine("    END");
                rcBuilder.AppendLine("    BLOCK \"VarFileInfo\"");
                rcBuilder.AppendLine("    BEGIN");
                rcBuilder.AppendLine("        VALUE \"Translation\", 0x409, 1200");
                rcBuilder.AppendLine("    END");
                rcBuilder.AppendLine("END");
            }
            
            if (!string.IsNullOrEmpty(_txtIconPath.Text) && File.Exists(_txtIconPath.Text))
            {
                rcBuilder.AppendLine("1 ICON DISCARDABLE \"" + _txtIconPath.Text.Replace("\\", "\\\\") + "\"");
            }
            
            try
            {
                File.WriteAllText(tempRcPath, rcBuilder.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppendConsole("ERROR creating .rc file: " + ex.Message + "\n", WbTheme.Red);
                return;
            }
            
            string gccPath;
            IsGccInstalled(out gccPath);
            string gccDir = Path.GetDirectoryName(gccPath);
            string windresPath = Path.Combine(gccDir, "windres.exe");
            if (File.Exists(windresPath))
            {
                AppendConsole("Compiling Resource Script to COFF object...\n", WbTheme.Text);
                var rcProc = Process.Start(new ProcessStartInfo
                {
                    FileName = windresPath,
                    Arguments = "\"" + tempRcPath + "\" -O coff -o \"" + tempResPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                });
                rcProc.WaitForExit();
                if (rcProc.ExitCode != 0)
                {
                    string err = rcProc.StandardError.ReadToEnd();
                    AppendConsole("windres ERROR: " + err + "\n", WbTheme.Red);
                    return;
                }
                
                transpiledNim = "{.link: \"build_temp.res\".}\n" + transpiledNim;
            }
            else
            {
                AppendConsole("WARNING: windres.exe not found. Skipping metadata resource compiling.\n", WbTheme.Yellow);
            }
        }
        
        // Normalize line endings to Unix style (\n) to prevent mixed line endings and stray \r carriage returns
        transpiledNim = transpiledNim.Replace("\r\n", "\n").Replace("\r", "\n");

        // Print generated Nim code with line numbers to help debug
        AppendConsole("--- Generated Nim Code (build_temp.nim) ---\n", WbTheme.Lavender);
        string[] nimLines = transpiledNim.Split(new[] { '\n' }, StringSplitOptions.None);
        for (int i = 0; i < nimLines.Length; i++)
        {
            AppendConsole(string.Format("{0:D3}: {1}\n", i + 1, nimLines[i]), WbTheme.Subtext0);
        }
        AppendConsole("-------------------------------------------\n", WbTheme.Lavender);

        try
        {
            File.WriteAllText(tempNimPath, transpiledNim, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppendConsole("ERROR saving Nim temp file: " + ex.Message + "\n", WbTheme.Red);
            return;
        }
        
        string nimPath;
        IsNimInstalled(out nimPath);
        string gccPath2;
        IsGccInstalled(out gccPath2);
        string nimBinDir = Path.GetDirectoryName(nimPath);
        string gccBinDir = Path.GetDirectoryName(gccPath2);
        
        var buildArgs = new StringBuilder();
        buildArgs.Append("c ");
        
        object selectedItem = _cmbBuildMode.SelectedItem;
        string mode = (selectedItem != null) ? selectedItem.ToString() : "Debug";
        if (mode.Contains("Release Speed"))
        {
            buildArgs.Append("-d:release ");
            if (!_chkDebug.Checked)
                buildArgs.Append("--debuginfo:off --passL:-s ");
        }
        else if (mode.Contains("Release Size"))
        {
            buildArgs.Append("-d:release --opt:size ");
            if (!_chkDebug.Checked)
                buildArgs.Append("--debuginfo:off --passL:-s ");
        }
        else if (mode.Contains("Danger"))
        {
            buildArgs.Append("-d:danger ");
            if (!_chkDebug.Checked)
                buildArgs.Append("--debuginfo:off --passL:-s ");
        }

        if (_chkLto.Checked)
        {
            buildArgs.Append("--passC:-flto --passL:-flto ");
        }
        if (_chkArc.Checked)
        {
            buildArgs.Append("--mm:arc ");
        }
        if (_chkPanics.Checked)
        {
            buildArgs.Append("--panics:on ");
        }
            
        buildArgs.Append("-p:\"" + scratchDir.Replace("\\", "/") + "\" ");
        buildArgs.Append("--app:gui ");
        if (_chkDebug.Checked)
        {
            buildArgs.Append("--stackTrace:on --lineTrace:on ");
        }
        
        string finalExePath = _txtOutputPath.Text;
        buildArgs.Append("--out:\"" + finalExePath + "\" ");
        buildArgs.Append("\"" + tempNimPath + "\"");
        
        AppendConsole("Compiling Nim binary: nim " + buildArgs.ToString() + "\n", WbTheme.Sky);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = nimPath,
            Arguments = buildArgs.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        string currentPath = startInfo.EnvironmentVariables.ContainsKey("PATH") ? startInfo.EnvironmentVariables["PATH"] : Environment.GetEnvironmentVariable("PATH");
        startInfo.EnvironmentVariables["PATH"] = gccBinDir + ";" + currentPath;
        
        var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
        
        var collectedErrors = new List<string>();
        var errorLock = new object();
        var stdoutDone = new System.Threading.ManualResetEvent(false);
        var stderrDone = new System.Threading.ManualResetEvent(false);
        
        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null)
            {
                stdoutDone.Set();
            }
            else
            {
                if (e.Data.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Data.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lock (errorLock) { collectedErrors.Add(e.Data); }
                }
                this.BeginInvoke(new Action(() => {
                    AppendConsole(e.Data + "\n", WbTheme.Text);
                }));
            }
        };
        
        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null)
            {
                stderrDone.Set();
            }
            else
            {
                if (e.Data.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Data.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lock (errorLock) { collectedErrors.Add(e.Data); }
                }
                this.BeginInvoke(new Action(() => {
                    AppendConsole(e.Data + "\n", WbTheme.Subtext0);
                }));
            }
        };
        
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(state =>
        {
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                
                stdoutDone.WaitOne(1000);
                stderrDone.WaitOne(1000);
                
                this.BeginInvoke(new Action(() => {
                    AppendConsole("Nim compiler exited with code " + proc.ExitCode + "\n", proc.ExitCode == 0 ? WbTheme.Green : WbTheme.Red);
                    
                    if (proc.ExitCode == 0)
                    {
                        string upxPath;
                        if (_chkUpx.Checked && IsUpxInstalled(out upxPath))
                        {
                            AppendConsole("Compressing binary with UPX...\n", WbTheme.Sky);
                            var upxProc = Process.Start(new ProcessStartInfo
                            {
                                FileName = upxPath,
                                Arguments = "--best \"" + finalExePath + "\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true
                            });
                            upxProc.WaitForExit();
                            AppendConsole("UPX finished with exit code " + upxProc.ExitCode + "\n", WbTheme.Green);
                        }
                        
                        if (File.Exists(finalExePath))
                        {
                            long size = new FileInfo(finalExePath).Length;
                            AppendConsole("\n🎉 BUILD COMPLETED SUCCESSFULLY!\n", WbTheme.Green);
                            AppendConsole("Saved to: " + finalExePath + "\n", WbTheme.Text);
                            AppendConsole("File Size: " + (size / 1024.0).ToString("F2") + " KB\n", WbTheme.Yellow);
                            
                            var result = MessageBox.Show(
                                "Executable compiled successfully!\nSize: " + (size / 1024.0).ToString("F2") + " KB\n\nWould you like to run the compiled application now?",
                                "Success",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);
                            
                            if (result == DialogResult.Yes)
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = finalExePath,
                                        WorkingDirectory = Path.GetDirectoryName(finalExePath)
                                    });
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Failed to run executable: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                        }
                    }
                    else
                    {
                        string details = "";
                        lock (errorLock)
                        {
                            if (collectedErrors.Count > 0)
                            {
                                details = "\n\nError details:\n" + string.Join("\n", collectedErrors.Take(10).ToArray());
                            }
                        }
                        MessageBox.Show("Nim compilation failed." + details, "Build Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    
                    try { if (proc.ExitCode == 0 && File.Exists(tempNimPath)) File.Delete(tempNimPath); } catch {}
                    try { if (proc.ExitCode == 0 && File.Exists(tempRcPath)) File.Delete(tempRcPath); } catch {}
                    try { if (proc.ExitCode == 0 && File.Exists(tempResPath)) File.Delete(tempResPath); } catch {}
                }));
            }
            catch (Exception ex)
            {
                this.BeginInvoke(new Action(() => {
                    AppendConsole("ERROR launching Nim compiler: " + ex.Message + "\n", WbTheme.Red);
                }));
            }
        }));
    }

    private string EscapeRcString(string s)
    {
        if (s == null) return "";
        return s.Replace("\"", "\"\"");
    }

    private void InitializeComponents()
    {
        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = WbTheme.Crust
        };
        Controls.Add(mainPanel);
        
        var leftCol = new Panel
        {
            Location = new Point(10, 10),
            Width = 500,
            Height = 750,
            BackColor = Color.Transparent
        };
        mainPanel.Controls.Add(leftCol);
        
        var rightCol = new Panel
        {
            Location = new Point(520, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent
        };
        mainPanel.Controls.Add(rightCol);
        
        var cardStatus = CreateCard("Toolchain Status", 0, 180, leftCol);
        
        _lblScoopStatus = CreateStatusLabel("Scoop Package Manager", 10, 30, cardStatus);
        _lblNimStatus = CreateStatusLabel("Nim Compiler", 10, 50, cardStatus);
        _lblGccStatus = CreateStatusLabel("MinGW GCC", 10, 70, cardStatus);
        _lblWinimStatus = CreateStatusLabel("winim Library", 10, 90, cardStatus);
        _lblUpxStatus = CreateStatusLabel("UPX Packer", 10, 110, cardStatus);
        
        _btnInstall = new Button
        {
            Text = "Install Toolchain Dependencies",
            Location = new Point(10, 140),
            Size = new Size(220, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text
        };
        _btnInstall.FlatAppearance.BorderColor = WbTheme.Surface1;
        _btnInstall.Click += (s, e) => InstallDependencies();
        cardStatus.Controls.Add(_btnInstall);
        
        var btnRefresh = new Button
        {
            Text = "Refresh",
            Location = new Point(240, 140),
            Size = new Size(100, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text
        };
        btnRefresh.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnRefresh.Click += (s, e) => RefreshStatus();
        cardStatus.Controls.Add(btnRefresh);
        
        var cardOptions = CreateCard("Build Options", 190, 240, leftCol);
        
        CreateLabel("Output EXE Path:", 10, 30, cardOptions);
        _txtOutputPath = new TextBox
        {
            Location = new Point(10, 50),
            Width = 360,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            BorderStyle = BorderStyle.None,
            Font = WbTheme.UISmall,
            Height = 20
        };
        cardOptions.Controls.Add(_txtOutputPath);
        
        var btnBrowseOut = new Button
        {
            Text = "...",
            Location = new Point(380, 48),
            Size = new Size(30, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text
        };
        btnBrowseOut.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnBrowseOut.Click += (s, e) => BrowseOutput();
        cardOptions.Controls.Add(btnBrowseOut);
        
        CreateLabel("Build Mode:", 10, 80, cardOptions);
        _cmbBuildMode = new ThemedComboBox
        {
            Location = new Point(100, 78),
            Width = 200,
            Font = WbTheme.UISmall
        };
        _cmbBuildMode.Items.AddRange(new string[] { "Debug", "Release Speed (-d:release)", "Release Size (-d:release --opt:size)", "Danger (-d:danger)" });
        _cmbBuildMode.SelectedIndex = 0;
        cardOptions.Controls.Add(_cmbBuildMode);
        
        _chkUpx = new CheckBox
        {
            Text = "UPX Compress Binary",
            Location = new Point(10, 120),
            Width = 200,
            ForeColor = WbTheme.Text
        };
        cardOptions.Controls.Add(_chkUpx);
        
        _chkTreeShake = new CheckBox
        {
            Text = "Run AST Tree Shaker",
            Location = new Point(220, 120),
            Width = 200,
            ForeColor = WbTheme.Text,
            Checked = true
        };
        cardOptions.Controls.Add(_chkTreeShake);

        _chkDebug = new CheckBox
        {
            Text = "Include Debugging Info",
            Location = new Point(10, 150),
            Width = 200,
            ForeColor = WbTheme.Text,
            Checked = true
        };
        cardOptions.Controls.Add(_chkDebug);

        _chkArc = new CheckBox
        {
            Text = "Use ARC Memory Manager",
            Location = new Point(220, 150),
            Width = 200,
            ForeColor = WbTheme.Text,
            Checked = false
        };
        cardOptions.Controls.Add(_chkArc);

        _chkLto = new CheckBox
        {
            Text = "Link-Time Optimization (LTO)",
            Location = new Point(10, 180),
            Width = 200,
            ForeColor = WbTheme.Text,
            Checked = true
        };
        cardOptions.Controls.Add(_chkLto);

        _chkPanics = new CheckBox
        {
            Text = "Enable Panics (--panics:on)",
            Location = new Point(220, 180),
            Width = 200,
            ForeColor = WbTheme.Text,
            Checked = false
        };
        cardOptions.Controls.Add(_chkPanics);
        
        var cardMeta = CreateCard("Application Metadata", 440, 260, leftCol);
        
        CreateLabel("Icon Path (.ico):", 10, 30, cardMeta);
        _txtIconPath = new TextBox
        {
            Location = new Point(130, 28),
            Width = 240,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            BorderStyle = BorderStyle.None,
            Font = WbTheme.UISmall,
            Height = 20
        };
        cardMeta.Controls.Add(_txtIconPath);
        
        var btnBrowseIcon = new Button
        {
            Text = "...",
            Location = new Point(380, 26),
            Size = new Size(30, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text
        };
        btnBrowseIcon.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnBrowseIcon.Click += (s, e) => BrowseIcon();
        cardMeta.Controls.Add(btnBrowseIcon);
        
        CreateLabel("App / File Name:", 10, 60, cardMeta);
        _txtAppName = CreateTextBox(130, 58, 280, cardMeta);
        
        CreateLabel("Version:", 10, 90, cardMeta);
        _txtVersion = CreateTextBox(130, 88, 280, cardMeta);
        _txtVersion.Text = "1.0.0.0";
        
        CreateLabel("Product Name:", 10, 120, cardMeta);
        _txtProduct = CreateTextBox(130, 118, 280, cardMeta);
        
        CreateLabel("Description:", 10, 150, cardMeta);
        _txtDescription = CreateTextBox(130, 148, 280, cardMeta);
        
        CreateLabel("Legal Copyright:", 10, 180, cardMeta);
        _txtCopyright = CreateTextBox(130, 178, 280, cardMeta);
        _txtCopyright.Text = "Copyright (C) " + DateTime.Now.Year;
        
        var cardActions = CreateCard("Build Actions", 710, 70, leftCol);
        
        _btnBuild = new Button
        {
            Text = "Compile & Build Standalone EXE",
            Location = new Point(10, 25),
            Size = new Size(400, 35),
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Accent,
            ForeColor = WbTheme.Crust,
            Font = WbTheme.UIBold
        };
        _btnBuild.FlatAppearance.BorderColor = WbTheme.Accent;
        _btnBuild.Click += (s, e) => BuildExecutable();
        cardActions.Controls.Add(_btnBuild);
        
        CreateLabel("Build Process Output", 10, 0, rightCol);
        
        _txtConsole = new RichTextBox
        {
            Location = new Point(10, 25),
            Width = rightCol.Width - 10,
            Height = rightCol.Height - 35,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        string scrollbarTheme = WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer";
        _txtConsole.HandleCreated += (s, e) => SetWindowTheme(_txtConsole.Handle, scrollbarTheme, null);
        if (_txtConsole.IsHandleCreated) SetWindowTheme(_txtConsole.Handle, scrollbarTheme, null);
        rightCol.Controls.Add(_txtConsole);
        
        leftCol.Height = 790;
        rightCol.Height = leftCol.Height;
    }

    private Panel CreateCard(string title, int y, int height, Panel parent)
    {
        var card = new Panel
        {
            Location = new Point(0, y),
            Width = parent.Width,
            Height = height,
            BackColor = WbTheme.Mantle,
            Padding = new Padding(10)
        };
        card.Paint += (s, e) =>
        {
            using (var p = new Pen(WbTheme.Surface0, 1))
            {
                e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            }
        };
        
        var lblTitle = new Label
        {
            Text = title,
            ForeColor = WbTheme.Lavender,
            Font = WbTheme.UIBold,
            AutoSize = true,
            Location = new Point(10, 8)
        };
        card.Controls.Add(lblTitle);
        
        parent.Controls.Add(card);
        return card;
    }

    private void CreateLabel(string text, int x, int y, Panel parent)
    {
        var lbl = new Label
        {
            Text = text,
            ForeColor = WbTheme.Subtext0,
            Font = WbTheme.UISmall,
            AutoSize = true,
            Location = new Point(x, y)
        };
        parent.Controls.Add(lbl);
    }

    private Label CreateStatusLabel(string name, int x, int y, Panel parent)
    {
        var lbl = new Label
        {
            Text = "⏳ Checking " + name + "...",
            ForeColor = WbTheme.Subtext1,
            Font = WbTheme.UISmall,
            AutoSize = true,
            Location = new Point(x, y)
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    private TextBox CreateTextBox(int x, int y, int width, Panel parent)
    {
        var tb = new TextBox
        {
            Location = new Point(x, y),
            Width = width,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            BorderStyle = BorderStyle.None,
            Font = WbTheme.UISmall,
            Height = 20
        };
        parent.Controls.Add(tb);
        return tb;
    }

    private void BrowseOutput()
    {
        using (var dlg = new SaveFileDialog())
        {
            dlg.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
            dlg.Title = "Set Build Output Path";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtOutputPath.Text = dlg.FileName;
            }
        }
    }

    private void BrowseIcon()
    {
        using (var dlg = new OpenFileDialog())
        {
            dlg.Filter = "Icon Files (*.ico)|*.ico|All Files (*.*)|*.*";
            dlg.Title = "Select Application Icon";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtIconPath.Text = dlg.FileName;
            }
        }
    }

    private void UpdateActiveFilePath()
    {
        string activeFilePath = _getFilePath();
        if (!string.IsNullOrEmpty(activeFilePath) && activeFilePath != _lastActiveFilePath)
        {
            _txtOutputPath.Text = Path.ChangeExtension(activeFilePath, ".exe");
            _txtAppName.Text = Path.GetFileNameWithoutExtension(activeFilePath);
            _lastActiveFilePath = activeFilePath;
            _transpiledNimOverride = null;
        }
    }

    private void LoadSettings()
    {
        try
        {
            var state = WorkbenchState.Load();
            string activeFilePath = _getFilePath();
            if (!string.IsNullOrEmpty(activeFilePath))
            {
                _txtOutputPath.Text = Path.ChangeExtension(activeFilePath, ".exe");
                _txtAppName.Text = Path.GetFileNameWithoutExtension(activeFilePath);
                _lastActiveFilePath = activeFilePath;
            }
            else
            {
                _txtOutputPath.Text = state.BuildOutputPath;
                _txtAppName.Text = state.BuildAppName;
            }
            
            _cmbBuildMode.SelectedIndex = state.BuildModeIndex;
            _chkUpx.Checked = state.BuildUpxChecked;
            _chkTreeShake.Checked = state.BuildTreeShakeChecked;
            _chkDebug.Checked = state.BuildDebugChecked;
            _chkLto.Checked = state.BuildLtoChecked;
            _chkArc.Checked = state.BuildArcChecked;
            _chkPanics.Checked = state.BuildPanicsChecked;
            _txtIconPath.Text = state.BuildIconPath;
            
            _txtVersion.Text = string.IsNullOrEmpty(state.BuildVersion) ? "1.0.0.0" : state.BuildVersion;
            _txtProduct.Text = state.BuildProductName;
            _txtDescription.Text = state.BuildDescription;
            if (!string.IsNullOrEmpty(state.BuildCopyright))
                _txtCopyright.Text = state.BuildCopyright;
        }
        catch {}
    }

    private void SaveSettings()
    {
        try
        {
            var state = WorkbenchState.Load();
            state.BuildOutputPath = _txtOutputPath.Text;
            state.BuildModeIndex = _cmbBuildMode.SelectedIndex;
            state.BuildUpxChecked = _chkUpx.Checked;
            state.BuildTreeShakeChecked = _chkTreeShake.Checked;
            state.BuildDebugChecked = _chkDebug.Checked;
            state.BuildLtoChecked = _chkLto.Checked;
            state.BuildArcChecked = _chkArc.Checked;
            state.BuildPanicsChecked = _chkPanics.Checked;
            state.BuildIconPath = _txtIconPath.Text;
            state.BuildAppName = _txtAppName.Text;
            state.BuildVersion = _txtVersion.Text;
            state.BuildProductName = _txtProduct.Text;
            state.BuildDescription = _txtDescription.Text;
            state.BuildCopyright = _txtCopyright.Text;
            state.Save();
        }
        catch {}
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        UpdateActiveFilePath();
    }

    private void AppendConsole(string text, Color color)
    {
        _txtConsole.SelectionStart = _txtConsole.TextLength;
        _txtConsole.SelectionLength = 0;
        _txtConsole.SelectionColor = color;
        _txtConsole.AppendText(text);
        _txtConsole.ScrollToCaret();
    }
}
