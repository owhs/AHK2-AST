using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace AHK2AST.Plugins
{
    // =========================================================================
    // 1. LOAD FILE PLUGIN
    // =========================================================================

    public class LoadFileConfig
    {
        [Category("Load File"), DisplayName("File Path"), Description("The path of the file to load. Placeholders like ${WorkspaceDir} are supported.")]
        public string FilePath { get; set; }

        [Category("Load File"), DisplayName("Treat Missing As Error"), Description("If true, throws an error if the file is not found. Otherwise, continues with empty string.")]
        public bool TreatMissingAsError { get; set; }

        public LoadFileConfig()
        {
            FilePath = "";
            TreatMissingAsError = true;
        }
    }

    public class LoadFilePlugin : IFlowPlugin
    {
        public string Name { get { return "Input.Load-File"; } }
        public string Target { get; set; }
        public string Category { get { return "Input"; } }
        public string StepTitle { get { return "Load File"; } }
        public string Icon { get { return "📂"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(LoadFileConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (LoadFileConfig)config; }

        public LoadFileConfig Config { get; set; }

        public LoadFilePlugin()
        {
            Config = new LoadFileConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            string path = Config.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                if (Config.TreatMissingAsError)
                    throw new Exception("Load File path is empty.");
                return "";
            }

            if (!File.Exists(path))
            {
                if (Config.TreatMissingAsError)
                    throw new Exception("File not found: " + path);
                PipelineLogger.Log("  ⚠️ Load File: File not found: " + path);
                return "";
            }

            PipelineLogger.Log("  Loading file: {0}", path);
            string content = File.ReadAllText(path, Encoding.UTF8);
            return content;
        }
    }

    // =========================================================================
    // 2. LOAD STRING PLUGIN
    // =========================================================================

    public class LoadStringConfig
    {
        [Category("Load String"), DisplayName("Raw Content"), Description("The raw source code content.")]
        [Editor("System.ComponentModel.Design.MultilineStringEditor, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", typeof(System.Drawing.Design.UITypeEditor))]
        public string RawContent { get; set; }

        public LoadStringConfig()
        {
            RawContent = "";
        }
    }

    public class LoadStringPlugin : IFlowPlugin
    {
        public string Name { get { return "Input.Load-String"; } }
        public string Target { get; set; }
        public string Category { get { return "Input"; } }
        public string StepTitle { get { return "Load String"; } }
        public string Icon { get { return "📝"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(LoadStringConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (LoadStringConfig)config; }

        public LoadStringConfig Config { get; set; }

        public LoadStringPlugin()
        {
            Config = new LoadStringConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            return Config.RawContent ?? "";
        }
    }

    // =========================================================================
    // 3. LOAD ARGUMENT PLUGIN
    // =========================================================================

    public class LoadArgumentConfig
    {
        [Category("Load Argument"), DisplayName("Argument Name"), Description("Name of the variable/property passed from the caller.")]
        public string ArgumentName { get; set; }

        [Category("Load Argument"), DisplayName("Is File Path"), Description("If true, treats the resolved value as a file path and reads its content. Otherwise, treats as raw code.")]
        public bool IsFilePath { get; set; }

        [Category("Load Argument"), DisplayName("Default Value"), Description("Fallback value to use if the argument is not provided.")]
        public string DefaultValue { get; set; }

        public LoadArgumentConfig()
        {
            ArgumentName = "SourcePath";
            IsFilePath = true;
            DefaultValue = "";
        }
    }

    public class LoadArgumentPlugin : IFlowPlugin
    {
        public string Name { get { return "Input.Load-Argument"; } }
        public string Target { get; set; }
        public string Category { get { return "Input"; } }
        public string StepTitle { get { return "Load Argument"; } }
        public string Icon { get { return "📥"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(LoadArgumentConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (LoadArgumentConfig)config; }

        public LoadArgumentConfig Config { get; set; }
        private AhkAstEngine _engine;

        public LoadArgumentPlugin()
        {
            Config = new LoadArgumentConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
            _engine = engine;
        }

        public object Execute(AstNode root)
        {
            string key = Config.ArgumentName;
            string value = null;

            if (_engine != null && _engine.ExternalProperties != null && _engine.ExternalProperties.ContainsKey(key))
            {
                value = _engine.ExternalProperties[key];
            }

            if (string.IsNullOrEmpty(value))
            {
                value = Config.DefaultValue;
            }

            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (Config.IsFilePath)
            {
                if (!File.Exists(value))
                {
                    throw new FileNotFoundException("Argument file path not found: " + value);
                }
                PipelineLogger.Log("  Loading file from argument '{0}': {1}", key, value);
                return File.ReadAllText(value, Encoding.UTF8);
            }

            return value;
        }
    }

    // =========================================================================
    // 4. SAVE FILE PLUGIN
    // =========================================================================

    public class SaveFileConfig
    {
        [Category("Save File"), DisplayName("Output Path"), Description("Target file path to write to. Placeholders supported.")]
        public string OutputPath { get; set; }

        [Category("Execute Hook"), DisplayName("Execute Hook"), Description("Execute the saved file after writing.")]
        public bool ExecuteHook { get; set; }

        [Category("Execute Hook"), DisplayName("Execute Command"), Description("Command line pattern. E.g. 'AutoHotkey.exe \"{OutputPath}\"'. If blank, runs the output path directly.")]
        public string ExecuteCommand { get; set; }

        [Category("Execute Hook"), DisplayName("Run Headless"), Description("If true, executes the process hidden and logs output to the console.")]
        public bool RunHeadless { get; set; }

        public SaveFileConfig()
        {
            OutputPath = "";
            ExecuteHook = false;
            ExecuteCommand = "";
            RunHeadless = false;
        }
    }

    public class SaveFilePlugin : IFlowPlugin
    {
        public string Name { get { return "Output.Save-File"; } }
        public string Target { get; set; }
        public string Category { get { return "Output"; } }
        public string StepTitle { get { return "Save File"; } }
        public string Icon { get { return "💾"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(SaveFileConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (SaveFileConfig)config; }

        public SaveFileConfig Config { get; set; }
        private AhkAstEngine _engine;

        public SaveFilePlugin()
        {
            Config = new SaveFileConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
            _engine = engine;
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            string code = "";
            if (_engine != null)
            {
                code = _engine.Emit(root);
            }
            else
            {
                code = root.ToString();
            }

            string path = Config.OutputPath;
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("Save File path is empty.");
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            PipelineLogger.Log("  Saving code to: {0}", path);
            File.WriteAllText(path, code, Encoding.UTF8);

            if (Config.ExecuteHook)
            {
                ExecutePostHook(path);
            }

            return root;
        }

        private void ExecutePostHook(string savedPath)
        {
            bool isHeadless = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AHK2AST_HEADLESS")) ||
                              AppDomain.CurrentDomain.FriendlyName.IndexOf("VerifyTest", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isHeadless && !Config.RunHeadless)
            {
                PipelineLogger.Log("  Skipping non-headless hook execution in headless environment.");
                return;
            }

            string cmd = Config.ExecuteCommand;
            string exe = "";
            string args = "";

            if (string.IsNullOrEmpty(cmd))
            {
                exe = savedPath;
            }
            else
            {
                string formattedCmd = cmd;
                formattedCmd = formattedCmd.Replace("{OutputPath}", savedPath)
                                           .Replace("${OutputPath}", savedPath)
                                           .Replace("%OutputPath%", savedPath);

                formattedCmd = formattedCmd.Trim();
                if (formattedCmd.StartsWith("\""))
                {
                    int nextQuote = formattedCmd.IndexOf('"', 1);
                    if (nextQuote > 0)
                    {
                        exe = formattedCmd.Substring(1, nextQuote - 1);
                        args = formattedCmd.Substring(nextQuote + 1).Trim();
                    }
                    else
                    {
                        exe = formattedCmd;
                    }
                }
                else
                {
                    int firstSpace = formattedCmd.IndexOf(' ');
                    if (firstSpace > 0)
                    {
                        exe = formattedCmd.Substring(0, firstSpace);
                        args = formattedCmd.Substring(firstSpace + 1).Trim();
                    }
                    else
                    {
                        exe = formattedCmd;
                    }
                }
            }

            try
            {
                PipelineLogger.Log("  Executing hook: {0} {1}", exe, args);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = !Config.RunHeadless,
                    CreateNoWindow = Config.RunHeadless
                };
                if (Config.RunHeadless)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                }
                
                var proc = System.Diagnostics.Process.Start(psi);
                if (Config.RunHeadless && proc != null)
                {
                    proc.WaitForExit(10000);
                    string outStr = proc.StandardOutput.ReadToEnd();
                    string errStr = proc.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(outStr))
                        PipelineLogger.Log("    Output: " + outStr.Trim());
                    if (!string.IsNullOrEmpty(errStr))
                        PipelineLogger.Log("    Error: " + errStr.Trim());
                }
            }
            catch (Exception ex)
            {
                PipelineLogger.Log("  ❌ ERROR: Hook execution failed: {0}", ex.Message);
            }
        }
    }

    // =========================================================================
    // 5. EMIT STRING PLUGIN
    // =========================================================================

    public class EmitStringConfig
    {
        [Category("Emit String"), DisplayName("Property Name"), Description("Name of the variable/property to store the emitted code in.")]
        public string PropertyName { get; set; }

        public EmitStringConfig()
        {
            PropertyName = "EmittedCode";
        }
    }

    public class EmitStringPlugin : IFlowPlugin
    {
        public string Name { get { return "Output.Emit-String"; } }
        public string Target { get; set; }
        public string Category { get { return "Output"; } }
        public string StepTitle { get { return "Emit String"; } }
        public string Icon { get { return "✨"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(EmitStringConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (EmitStringConfig)config; }

        public EmitStringConfig Config { get; set; }
        private AhkAstEngine _engine;

        public EmitStringPlugin()
        {
            Config = new EmitStringConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
            _engine = engine;
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            string code = "";
            if (_engine != null)
            {
                code = _engine.Emit(root);
                if (_engine.ExternalProperties == null)
                    _engine.ExternalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(Config.PropertyName))
                {
                    _engine.ExternalProperties[Config.PropertyName] = code;
                    PipelineLogger.Log("  Stored emitted code in property: {0}", Config.PropertyName);
                }
            }

            return root;
        }
    }
}
