using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AHK2AST.Plugins
{
    public class TraceWrapperConfig
    {
        [Category("Scope"), DisplayName("Mode"), Description("What scope to wrap: 'Functions' or 'Lines' or 'Both'. (Obsolete: use specific checkboxes below)")]
        public string WrapMode { get; set; } // "Functions", "Lines", "Both"

        [Category("Scope"), DisplayName("Trace Functions"), Description("If true, wraps standard functions/methods.")]
        public bool TraceFunctions { get; set; }

        [Category("Scope"), DisplayName("Trace Lines"), Description("If true, wraps individual statements to trace line-by-line execution.")]
        public bool TraceLines { get; set; }

        [Category("Scope"), DisplayName("Trace Classes"), Description("If true, wraps class methods.")]
        public bool TraceClasses { get; set; }

        [Category("Scope"), DisplayName("Trace Events (All)"), Description("Enables/disables all event tracing (Hotkeys, Hotstrings, GUI, System Hooks).")]
        public bool TraceEvents
        {
            get { return TraceHotkeys || TraceHotstrings || TraceGuiEvents || TraceSystemHooks; }
            set
            {
                TraceHotkeys = value;
                TraceHotstrings = value;
                TraceGuiEvents = value;
                TraceSystemHooks = value;
            }
        }

        [Category("Scope"), DisplayName("Trace Hotkeys"), Description("If true, wraps Hotkey triggers.")]
        public bool TraceHotkeys { get; set; }

        [Category("Scope"), DisplayName("Trace Hotstrings"), Description("If true, wraps Hotstring triggers.")]
        public bool TraceHotstrings { get; set; }

        [Category("Scope"), DisplayName("Trace GUI Events"), Description("If true, wraps GUI OnEvent callbacks.")]
        public bool TraceGuiEvents { get; set; }

        [Category("Scope"), DisplayName("Trace System Hooks"), Description("If true, wraps OnExit, OnMessage, OnError, and OnClipboardChange callbacks.")]
        public bool TraceSystemHooks { get; set; }

        [Category("Scope"), DisplayName("Function Filter"), Description("Comma-separated list or regex of function/method names to wrap. If blank, wraps all.")]
        public string FunctionFilter { get; set; }

        [Category("Features"), DisplayName("Profile Speed"), Description("If true, measures execution time of functions/lines.")]
        public bool ProfileSpeed { get; set; }

        [Category("Features"), DisplayName("Trace Calls"), Description("If true, logs entry and exit of wrapped functions.")]
        public bool TraceCalls { get; set; }

        [Category("Features"), DisplayName("Wrap Try-Catch"), Description("If true, wraps calls/lines in a try-catch block to log exceptions.")]
        public bool WrapTryCatch { get; set; }

        [Category("Diagnostics"), DisplayName("Trace File Path"), Description("Where to save the JSON trace log file. Placeholders supported.")]
        public string TraceFilePath { get; set; }

        [Category("Diagnostics"), DisplayName("Auto Save Trace"), Description("If true, registers an OnExit hook to save the trace tree to disk.")]
        public bool AutoSaveTrace { get; set; }

        public TraceWrapperConfig()
        {
            WrapMode = "Functions";
            TraceFunctions = true;
            TraceLines = false;
            TraceClasses = true;
            TraceHotkeys = true;
            TraceHotstrings = true;
            TraceGuiEvents = true;
            TraceSystemHooks = true;
            ProfileSpeed = true;
            TraceCalls = true;
            WrapTryCatch = false;
            TraceFilePath = "${WorkspaceDir}/__trace_dump.json";
            AutoSaveTrace = true;
        }
    }

    public class TraceWrapperPlugin : IFlowPlugin
    {
        public string Name { get { return "Transform.Trace-Wrapper"; } }
        public string Target { get; set; }

        public string Category { get { return "Transform"; } }
        public string StepTitle { get { return "Trace Wrapper"; } }
        public string Icon { get { return "🔍"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(TraceWrapperConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (TraceWrapperConfig)config; }

        public TraceWrapperConfig Config { get; set; }
        private AhkAstEngine _engine;

        public TraceWrapperPlugin()
        {
            Config = new TraceWrapperConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
            _engine = engine;
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            // 1. Convert single-statement control flow bodies to Blocks
            EnsureBlocksForControlFlow(root);

            // 2. Wrap event callbacks (GUI OnEvent, OnExit, OnMessage, OnError, OnClipboardChange)
            WrapEventCalls(root);

            // 3. Wrap built-in function calls (DllCall, Run, File, Reg, Win)
            WrapBuiltinCalls(root);

            // 4. Wrap methods, hotkeys, hotstrings, and lines recursively
            WrapNodes(root);

            // 5. Inject __TraceHelper runtime classes
            InjectRuntimeHelper(root);

            return root;
        }

        private void EnsureBlocksForControlFlow(AstNode node)
        {
            if (node == null) return;

            if (node.NodeType == "If" || node.NodeType == "Else" || node.NodeType == "While" || node.NodeType == "For" || node.NodeType == "Loop")
            {
                int bodyIndex = -1;
                if (node.NodeType == "If" && node.ChildCount > 1) bodyIndex = 1;
                else if (node.NodeType == "Else" && node.ChildCount > 0) bodyIndex = 0;
                else if (node.NodeType == "While" && node.ChildCount > 1) bodyIndex = 1;
                else if (node.NodeType == "For" && node.ChildCount > 2) bodyIndex = 2;
                else if (node.NodeType == "Loop" && node.ChildCount > 0)
                {
                    var last = node.GetChild(node.ChildCount - 1);
                    if (last.NodeType == "Until")
                    {
                        if (node.ChildCount > 1) bodyIndex = node.ChildCount - 2;
                    }
                    else
                    {
                        bodyIndex = node.ChildCount - 1;
                    }
                }

                if (bodyIndex != -1)
                {
                    var body = node.GetChild(bodyIndex);
                    if (body != null && body.NodeType != "Block")
                    {
                        var block = new AstNode("Block", body.Line, body.Column);
                        block.AddChild(body);
                        node.ReplaceChild(bodyIndex, block);
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                EnsureBlocksForControlFlow(child);
            }
        }

        private void WrapNodes(AstNode node)
        {
            if (node == null) return;

            if (node.NodeType == "Block" || node.NodeType == "Program")
            {
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child == null) continue;

                    if (child.NodeType == "Method")
                    {
                        InstrumentMethod(child);
                    }
                    else if (child.NodeType == "Hotkey")
                    {
                        InstrumentHotkey(child);
                    }
                    else if (child.NodeType == "Hotstring")
                    {
                        InstrumentHotstring(child);
                    }
                    else if (Config.TraceLines || Config.WrapMode == "Lines" || Config.WrapMode == "Both")
                    {
                        if (IsLineInstrumentable(child))
                        {
                            var lineNum = child.Line;
                            // Prepend line trace call: __TraceHelper.Line(N, "Code", "File")
                            string codeText = "";
                            if (_engine != null)
                                codeText = _engine.Emit(child);
                            else
                                codeText = child.ToString();

                            string escapedCodeText = AhkStringHelper.EscapeAhkString(codeText);
                            string fileArg = GetNodeFilePath(child);
                            var lineCallCode = string.Format("__TraceHelper.Line({0}, {1}, \"{2}\")", lineNum, escapedCodeText, fileArg);
                            var lineCallNode = ParseStatement(lineCallCode);
                            if (lineCallNode != null)
                            {
                                node.InsertChild(i, lineCallNode);
                                i++; // Skip the newly inserted call
                            }
                        }
                    }

                    WrapNodes(child);
                }
            }
            else
            {
                for (int i = 0; i < node.ChildCount; i++)
                {
                    WrapNodes(node.GetChild(i));
                }
            }
        }

        private bool IsLineInstrumentable(AstNode node)
        {
            if (node == null) return false;
            string t = node.NodeType;

            if (t == "Comment" || t == "Warning" || t == "Error" || t == "Directive" || t == "Label" || t == "Include" || t == "Block") return false;
            if (t == "Method" || t == "Class") return false;
            if (t == "Return" || t == "Break" || t == "Continue" || t == "Throw") return true;

            return true;
        }

        private bool ShouldWrapMethod(string methodName)
        {
            // Do not wrap helper classes/functions
            if (methodName.StartsWith("__TraceHelper")) return false;

            if (string.IsNullOrEmpty(Config.FunctionFilter)) return true;

            var filters = Config.FunctionFilter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var filter in filters)
            {
                var f = filter.Trim();
                if (f.StartsWith("/") && f.EndsWith("/"))
                {
                    try
                    {
                        var pattern = f.Substring(1, f.Length - 2);
                        if (Regex.IsMatch(methodName, pattern, RegexOptions.IgnoreCase)) return true;
                    }
                    catch {}
                }
                else
                {
                    if (string.Equals(methodName, f, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private void InstrumentMethod(AstNode methodNode)
        {
            if (!ShouldWrapMethod(methodNode.Value)) return;

            // Check if it is a class method
            bool isClassMethod = false;
            string className = "";
            var parent = methodNode.Parent;
            while (parent != null)
            {
                if (parent.NodeType == "Class")
                {
                    isClassMethod = true;
                    className = parent.Value;
                    break;
                }
                parent = parent.Parent;
            }

            if (isClassMethod && !Config.TraceClasses) return;
            if (!isClassMethod && !Config.TraceFunctions) return;

            AstNode bodyNode = null;
            int bodyIndex = -1;
            if (methodNode.ChildCount > 0)
            {
                var first = methodNode.GetChild(0);
                if (first.NodeType == "Parameters")
                {
                    if (methodNode.ChildCount > 1)
                    {
                        bodyNode = methodNode.GetChild(1);
                        bodyIndex = 1;
                    }
                }
                else
                {
                    bodyNode = first;
                    bodyIndex = 0;
                }
            }

            if (bodyNode == null) return;

            if (bodyNode.NodeType == "FatArrowBody")
            {
                var expr = bodyNode.ChildCount > 0 ? bodyNode.GetChild(0) : null;
                var block = new AstNode("Block", bodyNode.Line, bodyNode.Column);
                if (expr != null)
                {
                    var retNode = new AstNode("Return", expr.Line, expr.Column);
                    retNode.AddChild(expr);
                    block.AddChild(retNode);
                }
                methodNode.ReplaceChild(bodyIndex, block);
                bodyNode = block;
            }

            if (bodyNode.NodeType != "Block") return;

            string methodName = methodNode.Value;
            if (isClassMethod && !string.IsNullOrEmpty(className))
            {
                methodName = className + "." + methodName;
            }

            var paramNamesCode = BuildParamNamesList(methodNode);
            string fileArg = GetNodeFilePath(methodNode);
            var traceInitCode = string.Format("__trace := __TraceHelper_Instance(\"{0}\", {1}, \"{2}\")", methodName, paramNamesCode, fileArg);
            var traceInitNode = ParseStatement(traceInitCode);

            if (traceInitNode == null) return;

            if (Config.WrapTryCatch)
            {
                var tryNode = new AstNode("Try", bodyNode.Line, bodyNode.Column);
                var tryBlock = new AstNode("Block", bodyNode.Line, bodyNode.Column);

                var origStmts = bodyNode.ChildNodes.ToList();
                bodyNode.ClearChildren();

                tryBlock.SetChildren(origStmts);
                tryNode.AddChild(tryBlock);

                var catchNode = new AstNode("Catch", bodyNode.Line, bodyNode.Column);
                catchNode.Metadata = "__trace_err";

                var catchBlock = new AstNode("Block", bodyNode.Line, bodyNode.Column);
                var errorCallCode = string.Format("__TraceHelper.Error(\"{0}\", __trace_err)", methodName);
                var errorCallNode = ParseStatement(errorCallCode);
                if (errorCallNode != null) catchBlock.AddChild(errorCallNode);

                var throwNode = new AstNode("Throw", bodyNode.Line, bodyNode.Column);
                throwNode.AddChild(new AstNode("Identifier", bodyNode.Line, bodyNode.Column) { Value = "__trace_err" });
                catchBlock.AddChild(throwNode);

                catchNode.AddChild(catchBlock);
                tryNode.AddChild(catchNode);

                bodyNode.AddChild(traceInitNode);
                bodyNode.AddChild(tryNode);
            }
            else
            {
                bodyNode.InsertChild(0, traceInitNode);
            }
        }

        private void InstrumentHotkey(AstNode hotkeyNode)
        {
            if (!Config.TraceHotkeys) return;
            if (hotkeyNode.ChildCount == 0) return;
            var bodyNode = hotkeyNode.GetChild(0);

            if (bodyNode.NodeType != "Block")
            {
                var block = new AstNode("Block", bodyNode.Line, bodyNode.Column);
                block.AddChild(bodyNode);
                hotkeyNode.ReplaceChild(0, block);
                bodyNode = block;
            }

            if (bodyNode.NodeType != "Block") return;

            string fileArg = GetNodeFilePath(hotkeyNode);
            var traceInitCode = string.Format("__trace := __TraceHelper_Instance(\"Hotkey:{0}\", \"\", \"{1}\")", hotkeyNode.Value, fileArg);
            var traceInitNode = ParseStatement(traceInitCode);
            if (traceInitNode != null)
            {
                bodyNode.InsertChild(0, traceInitNode);
            }
        }

        private void InstrumentHotstring(AstNode hotstringNode)
        {
            if (!Config.TraceHotstrings) return;
            if (hotstringNode.ChildCount == 0) return;
            var bodyNode = hotstringNode.GetChild(0);

            if (bodyNode.NodeType != "Block")
            {
                var block = new AstNode("Block", bodyNode.Line, bodyNode.Column);
                block.AddChild(bodyNode);
                hotstringNode.ReplaceChild(0, block);
                bodyNode = block;
            }

            if (bodyNode.NodeType != "Block") return;

            string fileArg = GetNodeFilePath(hotstringNode);
            var traceInitCode = string.Format("__trace := __TraceHelper_Instance(\"Hotstring:{0}\", \"\", \"{1}\")", hotstringNode.Value, fileArg);
            var traceInitNode = ParseStatement(traceInitCode);
            if (traceInitNode != null)
            {
                bodyNode.InsertChild(0, traceInitNode);
            }
        }

        private void WrapEventCalls(AstNode node)
        {
            if (node == null) return;

            if (node.NodeType == "Call" && node.ChildCount > 0)
            {
                var target = node.GetChild(0);
                var args = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (args != null && args.NodeType == "Arguments")
                {
                    if (target.NodeType == "Identifier")
                    {
                        string name = target.Value;
                        if (Config.TraceSystemHooks && (name == "OnExit" || name == "OnError" || name == "OnClipboardChange"))
                        {
                            if (args.ChildCount > 0)
                            {
                                var callbackArg = args.GetChild(0);
                                if (callbackArg != null && callbackArg.NodeType != "Omitted")
                                {
                                    var wrapped = WrapCallbackArg(name, callbackArg);
                                    args.ReplaceChild(0, wrapped);
                                }
                            }
                        }
                        else if (Config.TraceSystemHooks && name == "OnMessage")
                        {
                            if (args.ChildCount > 1)
                            {
                                var callbackArg = args.GetChild(1);
                                if (callbackArg != null && callbackArg.NodeType != "Omitted")
                                {
                                    var wrapped = WrapCallbackArg(name, callbackArg);
                                    args.ReplaceChild(1, wrapped);
                                }
                            }
                        }
                    }
                    else if (Config.TraceGuiEvents && target.NodeType == "Member" && target.Value == "OnEvent")
                    {
                        if (args.ChildCount > 1)
                        {
                            var callbackArg = args.GetChild(1);
                            if (callbackArg != null && callbackArg.NodeType != "Omitted")
                            {
                                string eventName = "Event";
                                var eventNameArg = args.GetChild(0);
                                if (eventNameArg != null && eventNameArg.NodeType == "String")
                                {
                                    eventName = eventNameArg.Value.Trim('"', '\'');
                                }
                                var wrapped = WrapCallbackArg(eventName, callbackArg);
                                args.ReplaceChild(1, wrapped);
                            }
                        }
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                WrapEventCalls(child);
            }
        }

        private void WrapBuiltinCalls(AstNode node)
        {
            if (node == null) return;

            if (node.NodeType == "Call" && node.ChildCount > 0)
            {
                var target = node.GetChild(0);
                if (target.NodeType == "Identifier")
                {
                    string name = target.Value;
                    if (name == "DllCall")
                    {
                        var memberNode = new AstNode("Member", target.Line, target.Column) { Value = "TraceDllCall" };
                        memberNode.AddChild(new AstNode("Identifier", target.Line, target.Column) { Value = "__TraceHelper" });
                        node.ReplaceChild(0, memberNode);
                    }
                    else if (name == "Run" || name == "RunWait" || name == "RegRead" || name == "RegWrite" || name == "RegDelete" ||
                             name == "FileAppend" || name == "FileRead" || name == "FileOpen" || name == "FileDelete" ||
                             name == "WinExist" || name == "WinActive")
                    {
                        var memberNode = new AstNode("Member", target.Line, target.Column) { Value = "Trace_" + name };
                        memberNode.AddChild(new AstNode("Identifier", target.Line, target.Column) { Value = "__TraceHelper" });
                        node.ReplaceChild(0, memberNode);
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                WrapBuiltinCalls(child);
            }
        }

        private AstNode WrapCallbackArg(string eventName, AstNode callbackArg)
        {
            string escapedEventName = AhkStringHelper.EscapeAhkString(eventName);
            string code = string.Format("__TraceHelper.WrapCallback({0}, __callback_placeholder)", escapedEventName);
            var callNode = ParseStatement(code);
            if (callNode != null)
            {
                var args = callNode.GetChild(1);
                if (args != null && args.NodeType == "Arguments")
                {
                    for (int i = 0; i < args.ChildCount; i++)
                    {
                        var arg = args.GetChild(i);
                        if (arg != null && arg.NodeType == "Identifier" && arg.Value == "__callback_placeholder")
                        {
                            args.ReplaceChild(i, callbackArg);
                            break;
                        }
                    }
                }
                return callNode;
            }
            return callbackArg;
        }

        private string BuildParamNamesList(AstNode methodNode)
        {
            var paramNames = new List<string>();

            if (methodNode.ChildCount > 0)
            {
                var first = methodNode.GetChild(0);
                if (first.NodeType == "Parameters")
                {
                    foreach (var param in first.ChildNodes)
                    {
                        if (param.NodeType == "Parameter" && !string.IsNullOrEmpty(param.Value) && param.Value != "*")
                        {
                            paramNames.Add(param.Value);
                        }
                    }
                }
            }

            if (paramNames.Count == 0) return "\"\"";

            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < paramNames.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var name = paramNames[i];
                sb.AppendFormat("\"{0}\", __TraceHelper_Str(IsSet({0}) ? {0} : \"__unset\")", name);
            }
            sb.Append("]");
            return sb.ToString();
        }

        private AstNode ParseStatement(string source)
        {
            try
            {
                var lexer = new AhkLexer(source);
                var tokens = lexer.Tokenize();
                var parser = new AhkParser(tokens, new GrammarRules());
                var prog = parser.ParseProgram();
                if (prog != null && prog.ChildCount > 0)
                {
                    return prog.GetChild(0);
                }
            }
            catch {}
            return null;
        }

        private string GetNodeFilePath(AstNode node)
        {
            var p = node.Parent;
            while (p != null)
            {
                if (p.NodeType == "Include")
                {
                    return Path.GetFileName(p.Value);
                }
                p = p.Parent;
            }
            return "Main";
        }

        private void InjectRuntimeHelper(AstNode root)
        {
            if (root == null || root.NodeType != "Program") return;

            string resolvedPath = Config.TraceFilePath;
            if (_engine != null && _engine.ExternalProperties != null)
            {
                foreach (var kv in _engine.ExternalProperties)
                {
                    resolvedPath = resolvedPath.Replace("{" + kv.Key + "}", kv.Value)
                                               .Replace("${" + kv.Key + "}", kv.Value)
                                               .Replace("%" + kv.Key + "%", kv.Value);
                }
            }
            resolvedPath = resolvedPath.Replace("\\", "\\\\").Replace("\"", "\\\"");

            var helperSource = string.Format(@"
class __TraceHelper {{
    static Stack := []
    static Root := []
    static Freq := 0
    static ProgramStart := 0
    static FilePath := ""{0}""
    static AutoSave := {1}
    static LastLineTime := 0

    static Init() {{
        if (!__TraceHelper.Freq) {{
            DllCall(""QueryPerformanceFrequency"", ""Int64*"", &freq := 0)
            __TraceHelper.Freq := freq
            DllCall(""QueryPerformanceCounter"", ""Int64*"", &tStart := 0)
            __TraceHelper.ProgramStart := tStart
            if (__TraceHelper.AutoSave) {{
                OnExit((*) => __TraceHelper.Save())
            }}
        }}
    }}

    static Enter(name, params := """", file := ""Main"") {{
        __TraceHelper.Init()
        DllCall(""QueryPerformanceCounter"", ""Int64*"", &t := 0)
        ts := FormatTime(, ""yyyy-MM-dd HH:mm:ss"") ""."" A_MSec
        node := {{ Name: name, Params: params, File: file, StartTicks: t, Start: 0, Elapsed: 0, Timestamp: ts, Children: [] }}
        if (__TraceHelper.Stack.Length > 0) {{
            __TraceHelper.Stack[__TraceHelper.Stack.Length].Children.Push(node)
        }} else {{
            __TraceHelper.Root.Push(node)
        }}
        __TraceHelper.Stack.Push(node)
    }}

    static Exit(name, retval := """") {{
        if (__TraceHelper.Stack.Length == 0) return
        DllCall(""QueryPerformanceCounter"", ""Int64*"", &t := 0)
        node := __TraceHelper.Stack.Pop()
        if (__TraceHelper.Freq > 0) {{
            node.Elapsed := (t - node.StartTicks) * 1000.0 / __TraceHelper.Freq
            node.Start := (node.StartTicks - __TraceHelper.ProgramStart) * 1000.0 / __TraceHelper.Freq
        }}
        if (retval !== """")
            node.RetVal := retval
    }}

    static Error(name, err) {{
        if (__TraceHelper.Stack.Length == 0) return
        node := __TraceHelper.Stack[__TraceHelper.Stack.Length]
        node.Error := err.Message . "" (Line "" . err.Line . "")""
    }}

    static WrapCallback(eventName, callback) {{
        if (!callback || !IsObject(callback))
            return callback
        Wrapper(params*) {{
            __trace := __TraceHelper_Instance(""Event:"" . eventName, """", ""Event"")
            try {{
                return callback(params*)
            }} catch as err {{
                __TraceHelper.Error(""Event:"" . eventName, err)
                throw err
            }}
        }}
        return Wrapper
    }}

    static TraceDllCall(fn, params*) {{
        __trace := __TraceHelper_Instance(""DllCall:"" . fn, """", ""DllCall"")
        return DllCall(fn, params*)
    }}
    static Trace_Run(params*) {{
        target := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""Run:"" . target, """", ""System"")
        return Run(params*)
    }}
    static Trace_RunWait(params*) {{
        target := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""RunWait:"" . target, """", ""System"")
        return RunWait(params*)
    }}
    static Trace_RegRead(params*) {{
        keyName := params.Length > 0 ? params[1] : """"
        valueName := params.Length > 1 ? params[2] : """"
        __trace := __TraceHelper_Instance(""RegRead:"" . keyName . (valueName !== """" ? ""\"" . valueName : """"), """", ""Registry"")
        return RegRead(params*)
    }}
    static Trace_RegWrite(params*) {{
        keyName := params.Length > 2 ? params[3] : """"
        valueName := params.Length > 3 ? params[4] : """"
        __trace := __TraceHelper_Instance(""RegWrite:"" . keyName . (valueName !== """" ? ""\"" . valueName : """"), """", ""Registry"")
        return RegWrite(params*)
    }}
    static Trace_RegDelete(params*) {{
        keyName := params.Length > 0 ? params[1] : """"
        valueName := params.Length > 1 ? params[2] : """"
        __trace := __TraceHelper_Instance(""RegDelete:"" . keyName . (valueName !== """" ? ""\"" . valueName : """"), """", ""Registry"")
        return RegDelete(params*)
    }}
    static Trace_FileAppend(params*) {{
        filename := params.Length > 1 ? params[2] : """"
        __trace := __TraceHelper_Instance(""FileAppend:"" . filename, """", ""FileIO"")
        return FileAppend(params*)
    }}
    static Trace_FileRead(params*) {{
        filename := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""FileRead:"" . filename, """", ""FileIO"")
        return FileRead(params*)
    }}
    static Trace_FileOpen(params*) {{
        filename := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""FileOpen:"" . filename, """", ""FileIO"")
        return FileOpen(params*)
    }}
    static Trace_FileDelete(params*) {{
        filePattern := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""FileDelete:"" . filePattern, """", ""FileIO"")
        return FileDelete(params*)
    }}
    static Trace_WinExist(params*) {{
        winTitle := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""WinExist:"" . winTitle, """", ""Window"")
        return WinExist(params*)
    }}
    static Trace_WinActive(params*) {{
        winTitle := params.Length > 0 ? params[1] : """"
        __trace := __TraceHelper_Instance(""WinActive:"" . winTitle, """", ""Window"")
        return WinActive(params*)
    }}

    static Line(lineNum, codeText, file := ""Main"") {{
        __TraceHelper.Init()
        DllCall(""QueryPerformanceCounter"", ""Int64*"", &t := 0)
        elapsed := 0.0
        if (__TraceHelper.LastLineTime > 0 && __TraceHelper.Freq > 0) {{
            elapsed := (t - __TraceHelper.LastLineTime) * 1000.0 / __TraceHelper.Freq
        }}
        __TraceHelper.LastLineTime := t
        
        relStart := (__TraceHelper.Freq > 0) ? (t - __TraceHelper.ProgramStart) * 1000.0 / __TraceHelper.Freq : 0.0
        ts := FormatTime(, ""yyyy-MM-dd HH:mm:ss"") ""."" A_MSec
        node := {{ Type: ""Line"", Line: lineNum, Code: codeText, File: file, Start: relStart, Elapsed: elapsed, Timestamp: ts }}
        if (__TraceHelper.Stack.Length > 0) {{
            __TraceHelper.Stack[__TraceHelper.Stack.Length].Children.Push(node)
        }} else {{
            __TraceHelper.Root.Push(node)
        }}
    }}

    static Save() {{
        try {{
            FileDelete(__TraceHelper.FilePath)
        }}
        json := __TraceHelper.ToJson(__TraceHelper.Root)
        try {{
            FileAppend(json, __TraceHelper.FilePath, ""UTF-8"")
        }}
    }}

    static ToJson(obj) {{
        if IsObject(obj) {{
            if obj.HasProp(""Length"") {{
                parts := []
                for val in obj {{
                    parts.Push(__TraceHelper.ToJson(val))
                }}
                res := ""[""
                for idx, part in parts {{
                    res .= (idx > 1 ? "","" : """") . part
                }}
                res .= ""]""
                return res
            }} else {{
                res := ""{{""
                first := true
                for propName in obj.OwnProps() {{
                    val := obj.%propName%
                    if (propName = ""StartTicks"")
                        continue
                    res .= (first ? """" : "","") . '""' . propName . '"":' . __TraceHelper.ToJson(val)
                    first := false
                }}
                res .= ""}}""
                return res
            }}
        }} else if IsNumber(obj) {{
            return String(obj)
        }} else {{
            escaped := StrReplace(obj, '\', '\\')
            escaped := StrReplace(escaped, '""', '\""')
            escaped := StrReplace(escaped, ""`n"", ""\n"")
            escaped := StrReplace(escaped, ""`r"", ""\r"")
            escaped := StrReplace(escaped, ""`t"", ""\t"")
            return '""' . escaped . '""'
        }}
    }}
}}

class __TraceHelper_Instance {{
    __New(name, params := """", file := ""Main"") {{
        this.Name := name
        __TraceHelper.Enter(name, params, file)
    }}
    __Delete() {{
        __TraceHelper.Exit(this.Name)
    }}
}}

__TraceHelper_Str(val) {{
    if !IsSet(val)
        return ""unset""
    if IsObject(val) {{
        if val.HasProp(""Length"")
            return ""Array["" . val.Length . ""]""
        if val.HasProp(""Count"")
            return ""Map["" . val.Count . ""]""
        return ""Object""
    }}
    return String(val)
}}
", resolvedPath, Config.AutoSaveTrace ? "true" : "false");

            try
            {
                var lexer = new AhkLexer(helperSource);
                var tokens = lexer.Tokenize();
                var parser = new AhkParser(tokens, new GrammarRules());
                var progNode = parser.ParseProgram();
                foreach (var child in progNode.ChildNodes)
                {
                    root.AddChild(child);
                }
            }
            catch {}
        }
    }
}
