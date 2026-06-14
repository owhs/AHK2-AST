using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public static class PipelineLogger
{
    [ThreadStatic]
    private static List<string> _logs;

    public static List<string> Logs
    {
        get
        {
            if (_logs == null) _logs = new List<string>();
            return _logs;
        }
    }

    public static void Log(string format, params object[] args)
    {
        if (args != null && args.Length > 0)
        {
            Logs.Add(string.Format(format, args));
        }
        else
        {
            Logs.Add(format);
        }
    }

    public static void Clear()
    {
        Logs.Clear();
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
[Guid("B8C4D2E3-F5A6-7890-BCDE-F01234567890")]
[ProgId("Ahk2Ast.AstEngine")]
public class AhkAstEngine
{
    private AhkLexer _lexer;
    private AhkParser _parser;
    private GrammarRules _grammar;

    public AstPluginManager PluginManager { get; private set; }
    public Func<string, string, bool> OnMissingPlugin { get; set; }
    public Dictionary<string, string> ExternalProperties { get; set; }

    public void SetProperty(string key, string value)
    {
        if (ExternalProperties == null)
        {
            ExternalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        ExternalProperties[key] = value;
    }

    public AhkAstEngine()
    {
        _grammar = new GrammarRules();
        PluginManager = new AstPluginManager(this);
        ExternalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string GetLogsString()
    {
        return string.Join("\r\n", PipelineLogger.Logs.ToArray());
    }

    /// <summary>Load grammar rules from a JSON file for hot-swapping.</summary>
    public void LoadGrammar(string jsonPath)
    {
        if (File.Exists(jsonPath))
            _grammar = GrammarRules.LoadFromJson(File.ReadAllText(jsonPath));
    }

    /// <summary>Parse AHK2 source code into an AST. Never throws.</summary>
    public AstNode Parse(string source)
    {
        try
        {
            _lexer = new AhkLexer(source);
            List<Token> tokens = _lexer.Tokenize();
            _parser = new AhkParser(tokens, _grammar);
            return ExecutePlugins(_parser.ParseProgram());
        }
        catch (Exception ex)
        {
            // Absolute last resort - wrap in error node
            var errNode = new AstNode("Program", 0, 0);
            errNode.AddChild(new AstNode("Error", 0, 0) { Value = ex.Message });
            return errNode;
        }
    }

    private AstNode ExecutePlugins(AstNode root)
    {
        PluginManager.ExecutePlugins(root);
        return root;
    }

    /// <summary>
    /// Executes a flow pipeline given the source code and a JSON definition of the flow.
    /// Returns the final emitted string (if the flow produces text) or the emitted AHK code of the modified AST.
    /// </summary>
    public string ExecuteFlow(string source, object flowConfig, bool preserveIncludes = false, string currentFilePath = null, bool followIncludes = false)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        PipelineLogger.Clear();
        PipelineLogger.Log("Starting Flow execution...");

        if (ExternalProperties == null)
        {
            ExternalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        ExternalProperties["WorkspaceDir"] = !string.IsNullOrEmpty(currentFilePath) ? Path.GetDirectoryName(currentFilePath) : AppDomain.CurrentDomain.BaseDirectory;
        ExternalProperties["Workspace"] = ExternalProperties["WorkspaceDir"];
        ExternalProperties["OutputDir"] = !string.IsNullOrEmpty(currentFilePath) ? Path.GetDirectoryName(currentFilePath) : AppDomain.CurrentDomain.BaseDirectory;
        ExternalProperties["CurrentDir"] = !string.IsNullOrEmpty(currentFilePath) ? Path.GetDirectoryName(currentFilePath) : Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(currentFilePath))
        {
            ExternalProperties["InputFile"] = currentFilePath;
            ExternalProperties["InputFileName"] = Path.GetFileName(currentFilePath);
            ExternalProperties["InputFileNameNoExt"] = Path.GetFileNameWithoutExtension(currentFilePath);
        }
        else
        {
            ExternalProperties["InputFile"] = "";
            ExternalProperties["InputFileName"] = "";
            ExternalProperties["InputFileNameNoExt"] = "";
        }
        ExternalProperties["Date"] = DateTime.Now.ToString("yyyy-MM-dd");
        ExternalProperties["Time"] = DateTime.Now.ToString("HH-mm-ss");

        bool emitDiagnostics = true;

        AstNode root = null;
        try
        {
            PipelineLogger.Log("Parsing source code...");
            var parseSw = System.Diagnostics.Stopwatch.StartNew();
            root = Parse(source);
            if (followIncludes && !string.IsNullOrEmpty(currentFilePath))
            {
                var activeStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allIncluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                activeStack.Add(Path.GetFileName(currentFilePath));
                activeStack.Add(currentFilePath);
                allIncluded.Add(Path.GetFileName(currentFilePath));
                allIncluded.Add(currentFilePath);
                ProcessIncludes(root, Path.GetDirectoryName(currentFilePath), activeStack, allIncluded, false);
            }
            parseSw.Stop();
            PipelineLogger.Log("Parsing complete in {0}ms.", parseSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            PipelineLogger.Log("❌ ERROR parsing source: {0}", ex.Message);
            return FormatLogsAsComments() + source;
        }

        if (flowConfig == null)
        {
            PipelineLogger.Log("No flow configuration provided. Emitting original AST.");
            return (emitDiagnostics ? FormatLogsAsComments() : "") + Emit(root);
        }

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<string, object> flowData = null;
        List<object> stepsList = new List<object>();
        bool isRawObject = false;

        string flowJsonConfig = flowConfig as string;
        if (flowJsonConfig != null)
        {
            if (string.IsNullOrEmpty(flowJsonConfig))
            {
                PipelineLogger.Log("No flow configuration provided. Emitting original AST.");
                return (emitDiagnostics ? FormatLogsAsComments() : "") + Emit(root);
            }
            try
            {
                flowData = serializer.Deserialize<Dictionary<string, object>>(flowJsonConfig);
                if (flowData != null && flowData.ContainsKey("Meta"))
                {
                    var metaDict = flowData["Meta"] as Dictionary<string, object>;
                    if (metaDict != null)
                    {
                        if (metaDict.ContainsKey("EmitDiagnosticsComments"))
                        {
                            object val = metaDict["EmitDiagnosticsComments"];
                            if (val is bool)
                            {
                                emitDiagnostics = (bool)val;
                            }
                        }
                        if (metaDict.ContainsKey("CustomProperties"))
                        {
                            string customPropsStr = metaDict["CustomProperties"] as string;
                            if (!string.IsNullOrEmpty(customPropsStr))
                            {
                                var lines = customPropsStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    var idx = line.IndexOf('=');
                                    if (idx > 0)
                                    {
                                        string key = line.Substring(0, idx).Trim();
                                        string val = line.Substring(idx + 1).Trim();
                                        ExternalProperties[key] = val;
                                    }
                                }
                            }
                        }
                    }
                }
                if (flowData != null && flowData.ContainsKey("Steps"))
                {
                    var stepsObj = flowData["Steps"] as System.Collections.ArrayList;
                    if (stepsObj != null)
                    {
                        foreach (var step in stepsObj)
                        {
                            if (step != null) stepsList.Add(step);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PipelineLogger.Log("❌ ERROR deserializing flow JSON: {0}", ex.Message);
                return (emitDiagnostics ? FormatLogsAsComments() : "") + Emit(root);
            }
        }
        else
        {
            isRawObject = true;
            object metaObj = GetPropValue(flowConfig, "Meta");
            if (metaObj != null)
            {
                object emitDiagVal = GetPropValue(metaObj, "EmitDiagnosticsComments");
                if (emitDiagVal is bool)
                {
                    emitDiagnostics = (bool)emitDiagVal;
                }
                else if (emitDiagVal != null)
                {
                    bool.TryParse(emitDiagVal.ToString(), out emitDiagnostics);
                }

                object customPropsVal = GetPropValue(metaObj, "CustomProperties");
                if (customPropsVal != null)
                {
                    string customPropsStr = customPropsVal.ToString();
                    if (!string.IsNullOrEmpty(customPropsStr))
                    {
                        var lines = customPropsStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var idx = line.IndexOf('=');
                            if (idx > 0)
                            {
                                string key = line.Substring(0, idx).Trim();
                                string val = line.Substring(idx + 1).Trim();
                                ExternalProperties[key] = val;
                            }
                        }
                    }
                }
            }

            object stepsObj = GetPropValue(flowConfig, "Steps");
            if (stepsObj != null)
            {
                System.Collections.IEnumerable enumerable = stepsObj as System.Collections.IEnumerable;
                if (enumerable != null && !(stepsObj is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item != null) stepsList.Add(item);
                    }
                }
                else if (System.Runtime.InteropServices.Marshal.IsComObject(stepsObj))
                {
                    int length = 0;
                    try
                    {
                        object lenObj = GetPropValue(stepsObj, "Length");
                        if (lenObj != null) length = Convert.ToInt32(lenObj);
                    }
                    catch {}

                    if (length == 0)
                    {
                        try
                        {
                            object countObj = GetPropValue(stepsObj, "Count");
                            if (countObj != null) length = Convert.ToInt32(countObj);
                        }
                        catch {}
                    }

                    for (int i = 1; i <= length; i++)
                    {
                        object stepObj = null;
                        try
                        {
                            stepObj = stepsObj.GetType().InvokeMember("Item",
                                System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                null, stepsObj, new object[] { i });
                        }
                        catch
                        {
                            try
                            {
                                stepObj = stepsObj.GetType().InvokeMember(i.ToString(),
                                    System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                    null, stepsObj, null);
                            }
                            catch {}
                        }
                        if (stepObj != null)
                        {
                            stepsList.Add(stepObj);
                        }
                    }
                }
            }
        }

        var localPluginManager = new AstPluginManager(this);
        int stepIdx = 1;

        foreach (var step in stepsList)
        {
            string configTypeStr = "";
            string title = "";
            string icon = "";
            object configJsonObj = null;

            if (isRawObject)
            {
                object tVal = GetPropValue(step, "ConfigType");
                if (tVal != null) configTypeStr = tVal.ToString();

                object titleVal = GetPropValue(step, "Title");
                if (titleVal != null) title = titleVal.ToString();

                object iconVal = GetPropValue(step, "Icon");
                if (iconVal != null) icon = iconVal.ToString();

                configJsonObj = GetPropValue(step, "Config") ?? GetPropValue(step, "ConfigJson");
            }
            else
            {
                var stepDict = step as Dictionary<string, object>;
                if (stepDict == null) continue;

                configTypeStr = stepDict.ContainsKey("ConfigType") ? stepDict["ConfigType"].ToString() : "";
                title = stepDict.ContainsKey("Title") ? stepDict["Title"].ToString() : "";
                icon = stepDict.ContainsKey("Icon") ? stepDict["Icon"].ToString() : "";
                configJsonObj = stepDict.ContainsKey("Config") ? stepDict["Config"] : (stepDict.ContainsKey("ConfigJson") ? stepDict["ConfigJson"] : null);
            }

            if (string.IsNullOrEmpty(title))
            {
                title = configTypeStr;
                int lastDot = title.LastIndexOf('.');
                if (lastDot >= 0) title = title.Substring(lastDot + 1);
                if (title.EndsWith("Config")) title = title.Substring(0, title.Length - 6);
            }

            PipelineLogger.Log("[Step {0}] {1} {2} ({3})", stepIdx++, icon, title, configTypeStr);

            IAstPlugin plugin = null;
            var flowPlugin = AHK2AST.Plugins.PluginRegistry.CreatePlugin(configTypeStr);
            if (flowPlugin != null)
            {
                if (configJsonObj != null)
                {
                    string json = configJsonObj as string;
                    if (json != null && !string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            object config = serializer.Deserialize(json, flowPlugin.ConfigType);
                            flowPlugin.SetConfig(config);
                        }
                        catch (Exception ex)
                        {
                            PipelineLogger.Log("  ❌ ERROR deserializing step config: {0}", ex.Message);
                        }
                    }
                    else
                    {
                        try
                        {
                            object config = Activator.CreateInstance(flowPlugin.ConfigType);
                            PopulateObjectFromDynamic(config, configJsonObj);
                            flowPlugin.SetConfig(config);
                        }
                        catch (Exception ex)
                        {
                            PipelineLogger.Log("  ❌ ERROR populating step config object: {0}", ex.Message);
                        }
                    }
                }
                plugin = flowPlugin;
            }
            else
            {
                bool skip = false;
                if (OnMissingPlugin != null)
                {
                    skip = OnMissingPlugin(title, configTypeStr);
                }

                if (skip)
                {
                    PipelineLogger.Log("  ⚠️ WARNING: Plugin for step '{0}' (Type: {1}) is missing but was skipped.", title, configTypeStr);
                    continue;
                }
                else
                {
                    throw new AHK2AST.Plugins.MissingPluginException(title, configTypeStr);
                }
            }

            if (plugin != null)
            {
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    plugin.Target = currentFilePath;
                }
                localPluginManager.RegisterPlugin(plugin);
            }
        }

        PipelineLogger.Log("Running pipeline...");
        var results = localPluginManager.RunPipeline(root);
        PipelineLogger.Log("Finished pipeline execution.");

        string finalEmitted = "";
        var emitOpts = new EmitOptions { PreserveIncludes = preserveIncludes };
        if (results.Count > 0)
        {
            var lastResult = results[results.Count - 1].Output;
            if (lastResult is string) finalEmitted = (string)lastResult;
            else if (lastResult is AstNode) finalEmitted = Emit((AstNode)lastResult, emitOpts);
        }
        else
        {
            finalEmitted = Emit(root, emitOpts);
        }

        totalSw.Stop();

        int origLines = source.Replace("\r\n", "\n").Split('\n').Length;
        int origLength = source.Length;
        int origBytes = System.Text.Encoding.UTF8.GetByteCount(source);

        int finalLines = finalEmitted.Replace("\r\n", "\n").Split('\n').Length;
        int finalLength = finalEmitted.Length;
        int finalBytes = System.Text.Encoding.UTF8.GetByteCount(finalEmitted);

        int lineDiff = finalLines - origLines;
        double linePct = origLines > 0 ? (lineDiff * 100.0 / origLines) : 0.0;

        int byteDiff = finalBytes - origBytes;
        double bytePct = origBytes > 0 ? (byteDiff * 100.0 / origBytes) : 0.0;

        int linesSaved = origLines - finalLines;
        double linesSavedPct = origLines > 0 ? (linesSaved * 100.0 / origLines) : 0.0;

        int bytesSaved = origBytes - finalBytes;
        double bytesSavedPct = origBytes > 0 ? (bytesSaved * 100.0 / origBytes) : 0.0;

        PipelineLogger.Log("=========================================================");
        PipelineLogger.Log("PIPELINE SUMMARY METRICS");
        PipelineLogger.Log("=========================================================");
        PipelineLogger.Log("  Total Duration:      {0}ms", totalSw.ElapsedMilliseconds);
        PipelineLogger.Log("  Original Size:       {0} lines ({1} bytes)", origLines, origBytes);
        PipelineLogger.Log("  Transformed Size:    {0} lines ({1} bytes)", finalLines, finalBytes);
        PipelineLogger.Log("  Lines Saved:         {0} ({1:F1}%)", linesSaved, linesSavedPct);
        PipelineLogger.Log("  Size Saved:          {0} bytes ({1:F1}%)", bytesSaved, bytesSavedPct);
        PipelineLogger.Log("=========================================================");

        string commentPrefix = ";";
        if (finalEmitted.Contains("import AhkStdLib") || finalEmitted.StartsWith("# --- DllCall Bindings ---") || finalEmitted.Contains("import std/"))
        {
            commentPrefix = "#";
        }
        return (emitDiagnostics ? FormatLogsAsComments(commentPrefix) : "") + finalEmitted;
    }

    private static object GetPropValue(object obj, string key)
    {
        if (obj == null) return null;

        System.Collections.IDictionary dict = obj as System.Collections.IDictionary;
        if (dict != null)
        {
            if (dict.Contains(key)) return dict[key];
            foreach (var k in dict.Keys)
            {
                if (string.Equals(k.ToString(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return dict[k];
                }
            }
            return null;
        }

        if (System.Runtime.InteropServices.Marshal.IsComObject(obj))
        {
            try
            {
                return obj.GetType().InvokeMember(key,
                    System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, obj, null);
            }
            catch
            {
                try
                {
                    return obj.GetType().InvokeMember("Item",
                        System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, obj, new object[] { key });
                }
                catch {}
            }
        }

        try
        {
            var prop = obj.GetType().GetProperty(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null) return prop.GetValue(obj, null);

            var field = obj.GetType().GetField(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (field != null) return field.GetValue(obj);
        }
        catch {}

        return null;
    }

    private static void PopulateObjectFromDynamic(object target, object source)
    {
        if (target == null || source == null) return;

        Type targetType = target.GetType();
        var props = targetType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (!prop.CanWrite) continue;
            object value = GetPropValue(source, prop.Name);
            if (value != null)
            {
                try
                {
                    object coerced = CoerceValue(prop.PropertyType, value);
                    prop.SetValue(target, coerced, null);
                }
                catch {}
            }
        }

        var fields = targetType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var field in fields)
        {
            object value = GetPropValue(source, field.Name);
            if (value != null)
            {
                try
                {
                    object coerced = CoerceValue(field.FieldType, value);
                    field.SetValue(target, coerced);
                }
                catch {}
            }
        }
    }

    private static object CoerceValue(Type targetType, object value)
    {
        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        if (targetType.IsInstanceOfType(value))
            return value;
        if (targetType.IsEnum)
            return Enum.Parse(targetType, Convert.ToString(value), true);

        try { return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture); }
        catch { return value; }
    }

    private string FormatLogsAsComments(string prefix = ";")
    {
        var sb = new StringBuilder();
        sb.AppendLine(prefix + " =========================================================================");
        sb.AppendLine(prefix + " AHK2 AST PIPELINE EXECUTION DIAGNOSTICS");
        sb.AppendLine(prefix + " =========================================================================");
        foreach (var line in PipelineLogger.Logs)
        {
            sb.AppendLine(prefix + " " + line);
        }
        sb.AppendLine(prefix + " =========================================================================\n");
        return sb.ToString();
    }


    /// <summary>Parse a file.</summary>
    public AstNode ParseFile(string path)
    {
        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>Regenerate AHK2 source from AST.</summary>
    public string Emit(AstNode node)
    {
        return AstEmitter.Emit(node);
    }

    /// <summary>Regenerate AHK2 source from AST with options.</summary>
    public string Emit(AstNode node, EmitOptions options)
    {
        return AstEmitter.Emit(node, options);
    }

    /// <summary>Query AST nodes by type.</summary>
    public AstNode[] QueryByType(AstNode root, string nodeType)
    {
        var results = new List<AstNode>();
        QueryRecursive(root, nodeType, results);
        return results.ToArray();
    }

    /// <summary>Get all errors/warnings from AST.</summary>
    public AstNode[] GetErrors(AstNode root)
    {
        return QueryByType(root, "Error");
    }

    /// <summary>Get a human-readable tree dump.</summary>
    public string DumpTree(AstNode root, int indent)
    {
        var sb = new StringBuilder();
        DumpRecursive(root, indent, sb);
        return sb.ToString();
    }

    /// <summary>Save AST tree to binary file format quickly.</summary>
    public void SaveAst(AstNode root, string filePath)
    {
        AstSerializer.Save(root, filePath);
    }

    /// <summary>Load AST tree from binary file format quickly.</summary>
    public AstNode LoadAst(string filePath)
    {
        return AstSerializer.Load(filePath);
    }

    // -- #Include Following ------------------------------------------------

    /// <summary>Parse a file and follow #Include directives recursively.</summary>
    public AstNode ParseFileWithIncludes(string path, bool throwOnMissing)
    {
        string fullPath = Path.GetFullPath(path);
        string mainScriptDir = Path.GetDirectoryName(fullPath);
        var activeStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allIncluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ParseFileRecursive(fullPath, activeStack, allIncluded, throwOnMissing, mainScriptDir);
    }

    private AstNode ParseFileRecursive(string fullPath, HashSet<string> activeStack, HashSet<string> allIncluded, bool throwOnMissing, string mainScriptDir = null)
    {
        if (string.IsNullOrEmpty(mainScriptDir))
        {
            mainScriptDir = Path.GetDirectoryName(fullPath);
        }
        string fileName = Path.GetFileName(fullPath);
        if (allIncluded.Contains(fileName) || allIncluded.Contains(fullPath))
        {
            if (activeStack.Contains(fileName) || activeStack.Contains(fullPath))
            {
                var prog = new AstNode("Program", 0, 0);
                prog.AddChild(new AstNode("Warning", 0, 0) { Value = "Circular include: " + fullPath });
                return prog;
            }
            else
            {
                // Silent skip of already included diamond dependency file
                var prog = new AstNode("Program", 0, 0);
                prog.Metadata = "duplicate";
                return prog;
            }
        }

        if (!File.Exists(fullPath))
        {
            if (throwOnMissing)
                throw new FileNotFoundException("Include not found: " + fullPath);
            var prog = new AstNode("Program", 0, 0);
            prog.AddChild(new AstNode("Error", 0, 0) { Value = "Include not found: " + fullPath });
            return prog;
        }

        activeStack.Add(fileName);
        activeStack.Add(fullPath);
        allIncluded.Add(fileName);
        allIncluded.Add(fullPath);
        try
        {
            AstNode ast = AstFileCache.GetOrAdd(fullPath, (path) =>
            {
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            });

            // Process #Include directives
            string fileDir = Path.GetDirectoryName(fullPath);
            ProcessIncludes(ast, fileDir, activeStack, allIncluded, throwOnMissing, mainScriptDir);

            return ast;
        }
        finally
        {
            activeStack.Remove(fileName);
            activeStack.Remove(fullPath);
        }
    }

    private void ProcessIncludes(AstNode node, string baseDir, HashSet<string> activeStack, HashSet<string> allIncluded, bool throwOnMissing, string mainScriptDir = null)
    {
        if (string.IsNullOrEmpty(mainScriptDir))
        {
            mainScriptDir = baseDir;
        }
        string includeDir = baseDir;
        for (int i = 0; i < node.ChildCount; i++)
        {
            AstNode child = node.GetChild(i);
            if (child.NodeType != "Directive") continue;

            string val = child.Value;
            if (val == null) continue;
            string trimVal = val.TrimStart();
            if (!trimVal.StartsWith("#Include", StringComparison.OrdinalIgnoreCase)) continue;

            // Extract path from directive
            int idx = trimVal.IndexOf("Include", StringComparison.OrdinalIgnoreCase);
            string rest = trimVal.Substring(idx + 7).Trim();

            // 1. Strip comments
            int commentIdx = rest.IndexOf(" ;");
            if (commentIdx > 0) rest = rest.Substring(0, commentIdx).TrimEnd();

            // 2. Strip surrounding quotes first (e.g. for `#Include "*i file.ahk"`)
            if (rest.Length > 2 && ((rest[0] == '\'' && rest[rest.Length - 1] == '\'')
                || (rest[0] == '"' && rest[rest.Length - 1] == '"')))
                rest = rest.Substring(1, rest.Length - 2).Trim();

            // 3. Detect and strip optional marker *i
            bool optional = false;
            if (rest.StartsWith("*i", StringComparison.OrdinalIgnoreCase) &&
                (rest.Length == 2 || rest[2] == ' ' || rest[2] == '\t'))
            {
                optional = true;
                rest = rest.Substring(2).Trim();
            }

            // 4. Strip surrounding quotes again (e.g. for `#Include *i "file.ahk"`)
            if (rest.Length > 2 && ((rest[0] == '\'' && rest[rest.Length - 1] == '\'')
                || (rest[0] == '"' && rest[rest.Length - 1] == '"')))
                rest = rest.Substring(1, rest.Length - 2).Trim();

            bool isLibInclude = false;
            string libName = "";
            if (rest.StartsWith("<") && rest.EndsWith(">") && rest.Length > 2)
            {
                isLibInclude = true;
                libName = rest.Substring(1, rest.Length - 2).Trim();
            }

            string filePath = null;
            if (isLibInclude)
            {
                string fileName = libName;
                if (!fileName.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".ahk";
                }

                var searchDirs = new List<string>();
                if (!string.IsNullOrEmpty(mainScriptDir))
                {
                    searchDirs.Add(Path.Combine(mainScriptDir, "Lib"));
                }
                if (!string.IsNullOrEmpty(includeDir) && includeDir != mainScriptDir)
                {
                    searchDirs.Add(Path.Combine(includeDir, "Lib"));
                }

                try
                {
                    string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (!string.IsNullOrEmpty(myDocs))
                    {
                        searchDirs.Add(Path.Combine(myDocs, "AutoHotkey", "Lib"));
                    }
                }
                catch { }

                try
                {
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    if (!string.IsNullOrEmpty(programFiles))
                    {
                        searchDirs.Add(Path.Combine(programFiles, "AutoHotkey", "v2", "Lib"));
                        searchDirs.Add(Path.Combine(programFiles, "AutoHotkey", "Lib"));
                    }
                }
                catch { }

                try
                {
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (!string.IsNullOrEmpty(programFilesX86))
                    {
                        searchDirs.Add(Path.Combine(programFilesX86, "AutoHotkey", "v2", "Lib"));
                        searchDirs.Add(Path.Combine(programFilesX86, "AutoHotkey", "Lib"));
                    }
                }
                catch { }

                searchDirs.Add(@"C:\Program Files\AutoHotkey\v2\Lib");
                searchDirs.Add(@"C:\Program Files\AutoHotkey\Lib");

                try
                {
                    string envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    foreach (string dir in envPath.Split(';'))
                    {
                        string trimmedDir = dir.Trim();
                        if (string.IsNullOrEmpty(trimmedDir)) continue;
                        if (File.Exists(Path.Combine(trimmedDir, "AutoHotkey.exe")) || 
                            File.Exists(Path.Combine(trimmedDir, "AutoHotkey64.exe")) ||
                            File.Exists(Path.Combine(trimmedDir, "AutoHotkey32.exe")))
                        {
                            searchDirs.Add(Path.Combine(trimmedDir, "Lib"));
                            searchDirs.Add(Path.Combine(trimmedDir, "..", "Lib"));
                        }
                    }
                }
                catch { }

                foreach (var sDir in searchDirs)
                {
                    try
                    {
                        if (Directory.Exists(sDir))
                        {
                            string candidate = Path.Combine(sDir, fileName);
                            if (File.Exists(candidate))
                            {
                                filePath = Path.GetFullPath(candidate);
                                break;
                            }
                        }
                    }
                    catch { }
                }

                if (filePath == null)
                {
                    if (!optional && throwOnMissing)
                    {
                        throw new FileNotFoundException("Library include not found: <" + libName + ">");
                    }
                    var errNode = new AstNode("Error", child.Line, child.Column);
                    errNode.Value = "Library include not found: <" + libName + ">";
                    node.ReplaceChild(i, errNode);
                    continue;
                }
            }
            else
            {
                // Handle directory include: #Include dir\
                if (rest.EndsWith("\\") || rest.EndsWith("/"))
                {
                    string dirPath = Path.IsPathRooted(rest) ? rest : Path.Combine(includeDir, rest);
                    try { if (Directory.Exists(dirPath)) includeDir = Path.GetFullPath(dirPath); } catch { }
                    continue;
                }

                // Resolve file path relative to includeDir (not main script dir!)
                filePath = Path.IsPathRooted(rest) ? rest : Path.Combine(includeDir, rest);
                try { filePath = Path.GetFullPath(filePath); } catch { continue; }
            }

            try
            {
                AstNode includeAst = ParseFileRecursive(filePath, activeStack, allIncluded, !optional && throwOnMissing, mainScriptDir);
                var includeNode = new AstNode("Include", child.Line, child.Column);
                includeNode.Value = filePath;
                if (includeAst.Metadata == "duplicate")
                {
                    includeNode.Metadata = "; duplicate include: " + filePath;
                }
                else
                {
                    includeNode.Metadata = child.Value; // original directive text
                    foreach (var ic in includeAst.ChildNodes)
                    {
                        includeNode.AddChild(ic);
                    }
                }
                // Tag errors/warnings with source file for better diagnostics
                string shortName = Path.GetFileName(filePath);
                TagIncludeErrors(includeNode, shortName);
                node.ReplaceChild(i, includeNode);
            }
            catch (Exception ex)
            {
                if (!optional && throwOnMissing) throw;
                var errNode = new AstNode("Error", child.Line, child.Column);
                errNode.Value = "Include failed: " + filePath + " \u2014 " + ex.Message;
                node.ReplaceChild(i, errNode);
            }
        }
    }

    /// <summary>Tag Error/Warning nodes inside an include with the source filename.</summary>
    private void TagIncludeErrors(AstNode node, string fileName)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == "Error" || child.NodeType == "Warning")
            {
                if (!child.Value.StartsWith("["))
                    child.Value = "[" + fileName + "] " + child.Value;
            }
            TagIncludeErrors(child, fileName);
        }
    }


    private void QueryRecursive(AstNode node, string type, List<AstNode> results)
    {
        if (node.NodeType == type) results.Add(node);
        foreach (var child in node.ChildNodes)
            QueryRecursive(child, type, results);
    }

    private void DumpRecursive(AstNode node, int indent, StringBuilder sb)
    {
        sb.Append(new string(' ', indent * 2));
        sb.Append(node.NodeType);
        if (!string.IsNullOrEmpty(node.Value))
            sb.Append(" = ").Append(node.Value);
        sb.AppendLine();
        foreach (var child in node.ChildNodes)
            DumpRecursive(child, indent + 1, sb);
    }

    public void ResolveConfigProperties(object config)
    {
        if (config == null || ExternalProperties == null) return;

        var type = config.GetType();
        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (prop.PropertyType == typeof(string) && prop.CanRead && prop.CanWrite)
            {
                string val = (string)prop.GetValue(config, null);
                if (!string.IsNullOrEmpty(val))
                {
                    string newVal = ExpandPlaceholders(val, ExternalProperties);
                    if (newVal != val)
                    {
                        prop.SetValue(config, newVal, null);
                    }
                }
            }
        }
    }

    private string ExpandPlaceholders(string input, Dictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        string result = input;
        bool expanded = true;
        int depth = 0;
        while (expanded && depth < 5)
        {
            expanded = false;
            foreach (var kvp in properties)
            {
                string placeholder1 = "${" + kvp.Key + "}";
                string placeholder2 = "%" + kvp.Key + "%";
                if (result.IndexOf(placeholder1, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = ReplaceCaseInsensitive(result, placeholder1, kvp.Value);
                    expanded = true;
                }
                if (result.IndexOf(placeholder2, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = ReplaceCaseInsensitive(result, placeholder2, kvp.Value);
                    expanded = true;
                }
            }
            depth++;
        }
        return result;
    }

    private string ReplaceCaseInsensitive(string str, string find, string replace)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            str, 
            System.Text.RegularExpressions.Regex.Escape(find), 
            replace ?? "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
    }
}

public static class AstFileCache
{
    private static readonly Dictionary<string, Tuple<DateTime, AstNode>> Cache = new Dictionary<string, Tuple<DateTime, AstNode>>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, FileSystemWatcher> Watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
    private static bool _watchersEnabled = false;

    public static bool EnableWatchers
    {
        get { return _watchersEnabled; }
        set
        {
            if (_watchersEnabled != value)
            {
                _watchersEnabled = value;
                if (!_watchersEnabled)
                {
                    ClearWatchers();
                }
            }
        }
    }

    public static void Clear()
    {
        lock (Cache)
        {
            Cache.Clear();
        }
        ClearWatchers();
    }

    private static void ClearWatchers()
    {
        lock (Watchers)
        {
            foreach (var w in Watchers.Values)
            {
                w.Dispose();
            }
            Watchers.Clear();
        }
    }

    public static AstNode GetOrAdd(string path, Func<string, AstNode> parseFunc)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return parseFunc(fullPath);
        }

        DateTime currentWriteTime = File.GetLastWriteTimeUtc(fullPath);

        lock (Cache)
        {
            Tuple<DateTime, AstNode> cached;
            if (Cache.TryGetValue(fullPath, out cached))
            {
                if (cached.Item1 == currentWriteTime)
                {
                    return cached.Item2.Clone();
                }
            }
        }

        // Parse and add to cache
        AstNode parsed = parseFunc(fullPath);

        lock (Cache)
        {
            Cache[fullPath] = Tuple.Create(currentWriteTime, parsed);
        }

        if (_watchersEnabled)
        {
            SetupWatcher(fullPath);
        }

        return parsed.Clone();
    }

    private static void SetupWatcher(string fullPath)
    {
        lock (Watchers)
        {
            if (Watchers.ContainsKey(fullPath)) return;

            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                string file = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                var watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                watcher.Changed += (s, e) => Invalidate(fullPath);
                watcher.Deleted += (s, e) => Invalidate(fullPath);
                watcher.Renamed += (s, e) => Invalidate(fullPath);
                watcher.EnableRaisingEvents = true;
                Watchers[fullPath] = watcher;
            }
            catch
            {
                // Ignore watcher creation issues
            }
        }
    }

    public static void Invalidate(string path)
    {
        string fullPath = Path.GetFullPath(path);
        lock (Cache)
        {
            Cache.Remove(fullPath);
        }
        lock (Watchers)
        {
            FileSystemWatcher w;
            if (Watchers.TryGetValue(fullPath, out w))
            {
                w.Dispose();
                Watchers.Remove(fullPath);
            }
        }
    }
}


