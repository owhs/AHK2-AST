using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace AHK2AST.Plugins
{
    public class NimTranspilerConfig
    {
        [Category("Transpiler"), DisplayName("Add Imports"), Description("Prepend standard library imports at the top of the file.")]
        public bool AddImports { get; set; }

        [Category("Transpiler"), DisplayName("Output Path"), Description("Where to save the transpiled Nim file. If blank, only returns string output.")]
        public string OutputPath { get; set; }

        [Category("Transpiler"), DisplayName("Include Debugging"), Description("Wrap execution and methods in try-catch crash reporters.")]
        public bool IncludeDebugging { get; set; }

        public NimTranspilerConfig()
        {
            AddImports = true;
            OutputPath = "";
            IncludeDebugging = true;
        }
    }

    public class NimTranspilerPlugin : IFlowPlugin
    {
        public string Name { get { return "Transform.Nim-Transpiler"; } }
        public string Target { get; set; }
        public string Category { get { return "Transform"; } }
        public string StepTitle { get { return "Nim Transpiler"; } }
        public string Icon { get { return "👑"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(NimTranspilerConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (NimTranspilerConfig)config; }

        public NimTranspilerConfig Config { get; set; }
        private AhkAstEngine _engine;

        public NimTranspilerPlugin()
        {
            Config = new NimTranspilerConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
            _engine = engine;
        }

        public object Execute(AstNode root)
        {
            if (root == null) return "";

            var emitter = new NimEmitter(Config);
            string nimCode = emitter.Transpile(root);

            if (!string.IsNullOrEmpty(Config.OutputPath))
            {
                string resolvedPath = Config.OutputPath;
                if (_engine != null && _engine.ExternalProperties != null)
                {
                    foreach (var kv in _engine.ExternalProperties)
                    {
                        resolvedPath = resolvedPath.Replace("{" + kv.Key + "}", kv.Value)
                                                   .Replace("${" + kv.Key + "}", kv.Value)
                                                   .Replace("%" + kv.Key + "%", kv.Value);
                    }
                }
                string dir = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(resolvedPath, nimCode, Encoding.UTF8);
                PipelineLogger.Log("  Saved transpiled Nim code to: {0}", resolvedPath);
            }

            return nimCode;
        }
    }

    public class NimEmitter
    {
        private NimTranspilerConfig _config;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static string ResolveDllExport(ref string dllName, string funcName, out bool resolved)
        {
            resolved = false;

            string resolvedName = ResolveInLibrary(dllName, funcName);
            if (resolvedName != null)
            {
                resolved = true;
                return resolvedName;
            }

            if (dllName.Equals("user32", StringComparison.OrdinalIgnoreCase))
            {
                resolvedName = ResolveInLibrary("kernel32", funcName);
                if (resolvedName != null)
                {
                    dllName = "kernel32";
                    resolved = true;
                    return resolvedName;
                }

                resolvedName = ResolveInLibrary("gdi32", funcName);
                if (resolvedName != null)
                {
                    dllName = "gdi32";
                    resolved = true;
                    return resolvedName;
                }
            }

            return funcName;
        }

        private static string ResolveInLibrary(string dllName, string funcName)
        {
            IntPtr hMod = LoadLibrary(dllName);
            if (hMod == IntPtr.Zero)
            {
                hMod = LoadLibrary(dllName + ".dll");
            }

            if (hMod != IntPtr.Zero)
            {
                try
                {
                    IntPtr pAddr = GetProcAddress(hMod, funcName);
                    if (pAddr != IntPtr.Zero)
                    {
                        return funcName;
                    }

                    pAddr = GetProcAddress(hMod, funcName + "W");
                    if (pAddr != IntPtr.Zero)
                    {
                        return funcName + "W";
                    }

                    pAddr = GetProcAddress(hMod, funcName + "A");
                    if (pAddr != IntPtr.Zero)
                    {
                        return funcName + "A";
                    }
                }
                finally
                {
                    FreeLibrary(hMod);
                }
            }
            return null;
        }
        private HashSet<string> _declaredVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _globalVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _userClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<AstNode> _classNodes = new List<AstNode>();
        private List<AstNode> _functionNodes = new List<AstNode>();
        private Dictionary<string, string> _dllImports = new Dictionary<string, string>();
        private Dictionary<string, string> _dllProcNames = new Dictionary<string, string>();
        private Dictionary<string, List<string>> _classFields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private List<string> _methodScopeStack = new List<string>();
        private List<HashSet<string>> _nestedMethodsScopeStack = new List<HashSet<string>>();


        private static readonly Dictionary<string, string> _commonDllMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MulDiv", "kernel32" },
            { "GetModuleHandle", "kernel32" },
            { "GetLastError", "kernel32" },
            { "VirtualAlloc", "kernel32" },
            { "VirtualFree", "kernel32" },
            { "LoadLibrary", "kernel32" },
            { "GetProcAddress", "kernel32" },
            { "FreeLibrary", "kernel32" },
            { "RtlMoveMemory", "kernel32" },
            { "Sleep", "kernel32" },
            { "QueryPerformanceCounter", "kernel32" },
            { "QueryPerformanceFrequency", "kernel32" },
            { "Beep", "kernel32" },
            { "AttachConsole", "kernel32" }
        };

        private static readonly HashSet<string> _noSuffixWin32Funcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Sleep", "MulDiv", "GetLastError", "VirtualAlloc", "VirtualFree", 
            "GetProcAddress", "FreeLibrary", "RtlMoveMemory", "QueryPerformanceCounter", 
            "QueryPerformanceFrequency", "Beep", "AttachConsole"
        };

        public NimEmitter(NimTranspilerConfig config)
        {
            _config = config;
        }

        private void PrePassCollect(AstNode node, bool inClass, bool isTopLevel)
        {
            if (node == null) return;
            bool currentInClass = inClass;
            bool childIsTopLevel = isTopLevel;

            if (node.NodeType == "Class" && !string.IsNullOrEmpty(node.Value))
            {
                _userClasses.Add(NormalizeIdentifier(node.Value));
                _classNodes.Add(node);
                currentInClass = true;
                childIsTopLevel = false;
            }
            else if (node.NodeType == "Method")
            {
                if (!inClass && isTopLevel)
                {
                    _functionNodes.Add(node);
                }
                childIsTopLevel = false;
            }

            if (node.NodeType == "Declaration" && node.Metadata == "global" && !string.IsNullOrEmpty(node.Value))
            {
                _globalVars.Add(NormalizeIdentifier(node.Value));
            }

            if (node.NodeType == "UnaryExpr" && node.Value == "&" && node.ChildCount > 0)
            {
                var child = node.GetChild(0);
                if (child != null && child.NodeType == "Identifier")
                {
                    if (isTopLevel)
                    {
                        _globalVars.Add(NormalizeIdentifier(child.Value));
                    }
                }
            }

            if (isTopLevel)
            {
                if (node.NodeType == "Assign" || node.NodeType == "ColonAssign")
                {
                    var lhs = node.GetChild(0);
                    if (lhs != null && lhs.NodeType == "Identifier")
                    {
                        _globalVars.Add(NormalizeIdentifier(lhs.Value));
                    }
                }
                else if (node.NodeType == "BinaryExpr" && (node.Value == ":=" || node.Value == "="))
                {
                    var lhs = node.GetChild(0);
                    if (lhs != null && lhs.NodeType == "Identifier")
                    {
                        _globalVars.Add(NormalizeIdentifier(lhs.Value));
                    }
                }
                else if (node.NodeType == "Declaration" && !string.IsNullOrEmpty(node.Value))
                {
                    _globalVars.Add(NormalizeIdentifier(node.Value));
                }
            }

            if (node.ChildNodes != null)
            {
                foreach (var child in node.ChildNodes)
                {
                    PrePassCollect(child, currentInClass, childIsTopLevel);
                }
            }
        }

        private void CollectLocalVars(AstNode node, HashSet<string> localVars)
        {
            if (node == null) return;
            if (node.NodeType == "Method" || node.NodeType == "Class") return; // Skip nested scopes
            
            if (node.NodeType == "Assign" || node.NodeType == "ColonAssign")
            {
                var lhs = node.GetChild(0);
                if (lhs != null && lhs.NodeType == "Identifier")
                {
                    localVars.Add(NormalizeIdentifier(lhs.Value));
                }
            }
            else if (node.NodeType == "BinaryExpr" && (node.Value == ":=" || node.Value == "="))
            {
                var lhs = node.GetChild(0);
                if (lhs != null && lhs.NodeType == "Identifier")
                {
                    localVars.Add(NormalizeIdentifier(lhs.Value));
                }
            }
            else if (node.NodeType == "Declaration" && !string.IsNullOrEmpty(node.Value))
            {
                localVars.Add(NormalizeIdentifier(node.Value));
            }
            else if (node.NodeType == "UnaryExpr" && node.Value == "&" && node.ChildCount > 0)
            {
                var child = node.GetChild(0);
                if (child != null && child.NodeType == "Identifier")
                {
                    localVars.Add(NormalizeIdentifier(child.Value));
                }
            }
            
            if (node.ChildNodes != null)
            {
                foreach (var c in node.ChildNodes)
                {
                    CollectLocalVars(c, localVars);
                }
            }
        }

        private void CollectAndRemoveNestedMethods(AstNode node, List<AstNode> nested)
        {
            if (node == null) return;
            if (node.NodeType == "Class") return;
            if (node.ChildNodes != null)
            {
                var newChildren = new List<AstNode>();
                foreach (var child in node.ChildNodes)
                {
                    if (child == null) continue;
                    if (child.NodeType == "Method")
                    {
                        nested.Add(child);
                    }
                    else if (child.NodeType == "Class")
                    {
                        newChildren.Add(child);
                    }
                    else
                    {
                        newChildren.Add(child);
                        CollectAndRemoveNestedMethods(child, nested);
                    }
                }
                node.SetChildren(newChildren);
            }
        }

        private string GenerateClassTypes()
        {
            var sb = new StringBuilder();
            foreach (var node in _classNodes)
            {
                string className = NormalizeIdentifier(node.Value);
                var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
                string baseClass = extendsNode != null ? NormalizeIdentifier(extendsNode.Value) : "RootObj";

                var instanceFields = new List<string>();
                var staticFields = new Dictionary<string, string>();

                foreach (var child in node.ChildNodes)
                {
                    if (child == null || child.NodeType == "Method" || child.NodeType == "Extends" || child.NodeType == "Comment") continue;
                    
                    if (child.NodeType == "StaticAssign" || child.NodeType == "Declaration")
                    {
                        bool isStatic = (child.Metadata == "static");
                        string fieldName = NormalizeIdentifier(child.Value);
                        string initVal = "nil";

                        if (child.NodeType == "StaticAssign" && child.ChildCount > 0)
                        {
                            initVal = EmitNode(child.GetChild(0), 0);
                        }

                        if (!string.IsNullOrEmpty(fieldName))
                        {
                            if (isStatic) staticFields[fieldName] = initVal;
                            else instanceFields.Add(fieldName);
                        }

                        if (child.ChildCount > 0)
                        {
                            for (int i = (child.NodeType == "StaticAssign" ? 1 : 0); i < child.ChildCount; i++)
                            {
                                var subChild = child.GetChild(i);
                                if (subChild != null && (subChild.NodeType == "StaticAssign" || subChild.NodeType == "Declaration"))
                                {
                                    bool subStatic = (subChild.Metadata == "static");
                                    string subName = NormalizeIdentifier(subChild.Value);
                                    string subInit = "nil";
                                    if (subChild.NodeType == "StaticAssign" && subChild.ChildCount > 0)
                                    {
                                        subInit = EmitNode(subChild.GetChild(0), 0);
                                    }

                                    if (!string.IsNullOrEmpty(subName))
                                    {
                                        if (subStatic) staticFields[subName] = subInit;
                                        else instanceFields.Add(subName);
                                    }
                                }
                            }
                        }
                    }
                }

                sb.AppendLine("type " + className + "* = ref object of " + baseClass);
                var uniqueInstanceFields = instanceFields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _classFields[className] = uniqueInstanceFields;
                foreach (var field in uniqueInstanceFields)
                {
                    sb.AppendLine("    " + field + "*: AhkVar");
                }
                sb.AppendLine();

                if (staticFields.Count > 0)
                {
                    foreach (var kv in staticFields)
                    {
                        sb.AppendLine("var " + className + "_" + kv.Key + "*: AhkVar = " + kv.Value);
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private AstNode FindConstructor(AstNode classNode)
        {
            if (classNode == null) return null;
            var methods = classNode.ChildNodes.Where(c => c != null && c.NodeType == "Method").ToArray();
            var newMethod = methods.FirstOrDefault(m => m.Value.Equals("__New", StringComparison.OrdinalIgnoreCase));
            if (newMethod != null) return newMethod;

            var extendsNode = classNode.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
            if (extendsNode == null || string.IsNullOrEmpty(extendsNode.Value)) return null;

            var baseClassNode = _classNodes.FirstOrDefault(c => NormalizeIdentifier(c.Value).Equals(NormalizeIdentifier(extendsNode.Value), StringComparison.OrdinalIgnoreCase));
            return FindConstructor(baseClassNode);
        }

        private int GetInheritanceDepth(string className)
        {
            var node = _classNodes.FirstOrDefault(c => NormalizeIdentifier(c.Value).Equals(className, StringComparison.OrdinalIgnoreCase));
            if (node == null) return 0;
            var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
            if (extendsNode == null || string.IsNullOrEmpty(extendsNode.Value)) return 0;
            return 1 + GetInheritanceDepth(NormalizeIdentifier(extendsNode.Value));
        }

        private string GenerateForwardDeclarations()
        {
            var sb = new StringBuilder();
            foreach (var funcNode in _functionNodes)
            {
                string funcName = NormalizeIdentifier(funcNode.Value);
                string paramStr = "()";
                if (funcNode.ChildCount > 0 && funcNode.GetChild(0).NodeType == "Parameters")
                {
                    var plist = new List<string>();
                    foreach (var param in funcNode.GetChild(0).ChildNodes)
                    {
                        plist.Add(FormatParameter(param));
                    }
                    paramStr = "(" + string.Join(", ", plist) + ")";
                }
                sb.AppendLine("proc " + funcName + "*" + paramStr + ": AhkVar");
            }

            foreach (var node in _classNodes)
            {
                string className = NormalizeIdentifier(node.Value);
                var methods = node.ChildNodes.Where(c => c != null && c.NodeType == "Method").ToArray();
                
                var newMethod = FindConstructor(node);
                string factoryParams = "";
                if (newMethod != null)
                {
                    var plist = new List<string>();
                    if (newMethod.ChildCount > 0 && newMethod.GetChild(0).NodeType == "Parameters")
                    {
                        foreach (var param in newMethod.GetChild(0).ChildNodes)
                        {
                            plist.Add(FormatParameter(param));
                        }
                    }
                    factoryParams = "(" + string.Join(", ", plist) + ")";
                }
                else
                {
                    factoryParams = "()";
                }
                sb.AppendLine("proc new" + className + "*" + factoryParams + ": " + className);

                foreach (var method in methods)
                {
                    string methodName = NormalizeIdentifier(method.Value);
                    var paramList = new List<string>();
                    paramList.Add("self: " + className);
                    if (method.ChildCount > 0 && method.GetChild(0).NodeType == "Parameters")
                    {
                        foreach (var param in method.GetChild(0).ChildNodes)
                        {
                            paramList.Add(FormatParameter(param));
                        }
                    }
                    string methodParamStr = "(" + string.Join(", ", paramList) + ")";
                    sb.AppendLine("proc " + methodName + "*" + methodParamStr + ": AhkVar");
                }
            }

            // Forward declarations for dispatchers
            var methodsByNameAndCount = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in _classNodes)
            {
                var methods = node.ChildNodes.Where(c => c != null && c.NodeType == "Method").ToArray();
                foreach (var method in methods)
                {
                    string methodName = NormalizeIdentifier(method.Value);
                    if (methodName.Equals("ahk_New", StringComparison.OrdinalIgnoreCase)) continue;

                    var plist = new List<string>();
                    if (method.ChildCount > 0 && method.GetChild(0).NodeType == "Parameters")
                    {
                        foreach (var param in method.GetChild(0).ChildNodes)
                        {
                            plist.Add(FormatParameter(param));
                        }
                    }

                    string key = methodName + "_" + plist.Count;
                    if (!methodsByNameAndCount.ContainsKey(key))
                    {
                        methodsByNameAndCount[key] = new List<dynamic>();
                    }
                    methodsByNameAndCount[key].Add(new { MethodName = methodName, ParamsList = plist });
                }
            }

            foreach (var kv in methodsByNameAndCount)
            {
                var list = kv.Value;
                var firstMethod = list.First();
                string methodName = firstMethod.MethodName;
                var plist = firstMethod.ParamsList;

                var dispParams = new List<string>();
                dispParams.Add("self: AhkVar");
                dispParams.AddRange(plist);

                string sigStr = "(" + string.Join(", ", dispParams) + ")";
                sb.AppendLine("proc " + methodName + "*" + sigStr + ": AhkVar");
            }

            return sb.ToString();
        }

        private string GenerateDispatchers()
        {
            if (_classNodes.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("# --- User Class Method Dispatchers ---");

            var methodsByNameAndCount = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in _classNodes)
            {
                string className = NormalizeIdentifier(node.Value);
                var methods = node.ChildNodes.Where(c => c != null && c.NodeType == "Method").ToArray();
                foreach (var method in methods)
                {
                    string methodName = NormalizeIdentifier(method.Value);
                    if (methodName.Equals("ahk_New", StringComparison.OrdinalIgnoreCase)) continue;

                    var plist = new List<string>();
                    var pnames = new List<string>();
                    if (method.ChildCount > 0 && method.GetChild(0).NodeType == "Parameters")
                    {
                        foreach (var param in method.GetChild(0).ChildNodes)
                        {
                            plist.Add(FormatParameter(param));
                            pnames.Add(NormalizeIdentifier(param.Value));
                        }
                    }

                    var methodInfo = new {
                        ClassName = className,
                        MethodName = methodName,
                        ParamsList = plist,
                        ParamNames = pnames
                    };

                    string key = methodName + "_" + plist.Count;
                    if (!methodsByNameAndCount.ContainsKey(key))
                    {
                        methodsByNameAndCount[key] = new List<dynamic>();
                    }
                    methodsByNameAndCount[key].Add(methodInfo);
                }
            }

            foreach (var kv in methodsByNameAndCount)
            {
                var list = kv.Value;
                var firstMethod = list.First();
                string methodName = firstMethod.MethodName;
                var plist = firstMethod.ParamsList;
                var pnames = firstMethod.ParamNames;

                var dispParams = new List<string>();
                dispParams.Add("self: AhkVar");
                dispParams.AddRange(plist);

                string sigStr = "(" + string.Join(", ", dispParams) + ")";
                sb.AppendLine("proc " + methodName + "*" + sigStr + ": AhkVar =");
                sb.AppendLine("  if self == nil or self.kind != akObject or self.oVal == nil: return nil");

                if (methodName.Equals("Add", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui:");
                    sb.AppendLine("    return AhkGui_Add(self" + GetArgsStr(pnames, 3) + ")");
                    sb.AppendLine("  elif self.oVal of AhkControl:");
                    sb.AppendLine("    return AhkControl_Add(self" + GetArgsStr(pnames, 99) + ")");
                    sb.AppendLine("  elif self.oVal of AhkMenu:");
                    sb.AppendLine("    return AhkMenu_Add(self" + GetArgsStr(pnames, 3) + ")");
                }
                else if (methodName.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkControl:");
                    sb.AppendLine("    return AhkControl_Delete(self" + GetArgsStr(pnames, 1) + ")");
                    sb.AppendLine("  elif self.oVal of AhkMenu:");
                    sb.AppendLine("    return AhkMenu_Delete(self" + GetArgsStr(pnames, 1) + ")");
                }
                else if (methodName.Equals("Insert", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkControl:");
                    sb.AppendLine("    return AhkControl_Insert(self" + GetArgsStr(pnames, 99) + ")");
                }
                else if (methodName.Equals("Modify", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkControl:");
                    sb.AppendLine("    return AhkControl_Modify(self" + GetArgsStr(pnames, 99) + ")");
                }
                else if (methodName.Equals("GetNext", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkControl:");
                    sb.AppendLine("    return AhkControl_GetNext(self" + GetArgsStr(pnames, 2) + ")");
                }
                else if (methodName.Equals("OnEvent", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui or self.oVal of AhkControl:");
                    sb.AppendLine("    return OnEvent(self" + GetArgsStr(pnames, 2) + ")");
                }
                else if (methodName.Equals("Submit", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui:");
                    sb.AppendLine("    return Submit(self" + GetArgsStr(pnames, 1) + ")");
                }
                else if (methodName.Equals("SetFont", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui:");
                    sb.AppendLine("    return SetFont(self" + GetArgsStr(pnames, 2) + ")");
                }
                else if (methodName.Equals("Show", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui:");
                    sb.AppendLine("    return Show(self" + GetArgsStr(pnames, 1) + ")");
                    sb.AppendLine("  elif self.oVal of AhkMenu:");
                    sb.AppendLine("    return AhkMenu_Show(self" + GetArgsStr(pnames, 2) + ")");
                }
                else if (methodName.Equals("Opt", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui or self.oVal of AhkControl:");
                    sb.AppendLine("    return Opt(self" + GetArgsStr(pnames, 1) + ")");
                }
                else if (methodName.Equals("Move", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui or self.oVal of AhkControl:");
                    sb.AppendLine("    return Move(self" + GetArgsStr(pnames, 4) + ")");
                }
                else if (methodName.Equals("Redraw", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  if self.oVal of AhkGui or self.oVal of AhkControl:");
                    sb.AppendLine("    return Redraw(self" + GetArgsStr(pnames, 0) + ")");
                }

                var sortedList = list.OrderByDescending(m => GetInheritanceDepth((string)m.ClassName)).ToList();

                foreach (var m in sortedList)
                {
                    sb.AppendLine("  if self.oVal of " + m.ClassName + ":");
                    var callArgs = new List<string>();
                    callArgs.Add(m.ClassName + "(self.oVal)");
                    callArgs.AddRange(pnames);
                    sb.AppendLine("    return " + m.MethodName + "(" + string.Join(", ", callArgs) + ")");
                }
                sb.AppendLine("  return nil");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string Transpile(AstNode root)
        {
            _declaredVars.Clear();
            _globalVars.Clear();
            _userClasses.Clear();
            _classNodes.Clear();
            _functionNodes.Clear();
            _dllImports.Clear();
            _dllProcNames.Clear();
            _classFields.Clear();
            _methodScopeStack.Clear();
            _nestedMethodsScopeStack.Clear();

            PrePassCollect(root, false, true);

            string classTypesCode = GenerateClassTypes();
            string forwardDecsCode = GenerateForwardDeclarations();

            _declaredVars.Clear();
            foreach (var gvar in _globalVars)
            {
                _declaredVars.Add(gvar);
            }

            var defLines = new List<string>();
            var stmtLines = new List<string>();

            if (root.NodeType == "Program" || root.NodeType == "Block")
            {
                ProcessProgramChildren(root.ChildNodes, defLines, stmtLines);
            }
            else
            {
                string emitted = EmitNode(root, 0, true);
                if (!string.IsNullOrEmpty(emitted))
                {
                    stmtLines.Add(emitted);
                }
            }

            string mainStatementsCode = string.Join("\n", stmtLines);
            if (_config.IncludeDebugging && stmtLines.Count > 0)
            {
                var mainSb = new StringBuilder();
                mainSb.AppendLine("try:");
                foreach (var stmt in stmtLines)
                {
                    if (string.IsNullOrEmpty(stmt))
                    {
                        mainSb.AppendLine();
                    }
                    else
                    {
                        string[] lines = stmt.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string l = lines[i];
                            if (i == lines.Length - 1 && string.IsNullOrEmpty(l)) continue;
                            mainSb.AppendLine("    " + l);
                        }
                    }
                }
                mainSb.AppendLine("except Exception as e:");
                mainSb.AppendLine("    discard MsgBox(toAhkVar(\"Unhandled Exception: \" & e.msg & \"\\n\\nStack Trace:\\n\" & e.getStackTrace()), toAhkVar(\"Application Crash\"), toAhkVar(0x10))");
                mainStatementsCode = mainSb.ToString();
            }

            var sb = new StringBuilder();
            if (_config.AddImports)
            {
                sb.AppendLine("import AhkStdLib");
                sb.AppendLine();
            }

            if (_globalVars.Count > 0)
            {
                sb.AppendLine("# --- Global Variables ---");
                foreach (var gvar in _globalVars)
                {
                    sb.AppendLine("var " + NormalizeIdentifier(gvar) + "*: AhkVar");
                }
                sb.AppendLine();
            }

            if (_dllImports.Count > 0)
            {
                sb.AppendLine("# --- DllCall Bindings ---");
                foreach (var import in _dllImports.Values)
                {
                    sb.AppendLine(import);
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(classTypesCode))
            {
                sb.AppendLine("# --- Class Types ---");
                sb.Append(classTypesCode);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(forwardDecsCode))
            {
                sb.AppendLine("# --- Forward Declarations ---");
                sb.Append(forwardDecsCode);
                sb.AppendLine();
            }

            string hooksCode = GenerateHooksCode();
            if (!string.IsNullOrEmpty(hooksCode))
            {
                sb.Append(hooksCode);
            }

            string dispatchersCode = GenerateDispatchers();
            if (!string.IsNullOrEmpty(dispatchersCode))
            {
                sb.Append(dispatchersCode);
            }

            sb.Append(string.Join("\n", defLines));
            sb.AppendLine();
            sb.Append(mainStatementsCode);
            return sb.ToString();
        }

        private void ProcessProgramChildren(AstNode[] children, List<string> defLines, List<string> stmtLines)
        {
            foreach (var child in children)
            {
                if (child == null) continue;

                if (child.NodeType == "Include")
                {
                    defLines.Add("# [Include] " + child.Value);
                    ProcessProgramChildren(child.ChildNodes, defLines, stmtLines);
                    continue;
                }

                bool isDefinition = (child.NodeType == "Method" || child.NodeType == "Class" || child.NodeType == "Directive");
                string emitted = EmitNode(child, 0, true);
                if (!string.IsNullOrEmpty(emitted))
                {
                    if (isDefinition)
                    {
                        defLines.Add(emitted);
                    }
                    else
                    {
                        if (IsExpressionNode(child))
                        {
                            string cleanEmitted = emitted.TrimStart();
                            string leadingPad = emitted.Substring(0, emitted.Length - cleanEmitted.Length);
                            emitted = leadingPad + "discard " + cleanEmitted;
                        }
                        stmtLines.Add(emitted);
                    }
                }
            }
        }

        private string GenerateHooksCode()
        {
            if (_classFields.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("# --- User Class Member Hooks ---");

            // Getter hook
            sb.AppendLine("userClassGetFieldHook = proc(o: RootRef, field: string): AhkVar =");
            sb.AppendLine("  if o == nil: return nil");
            sb.AppendLine("  let idx = field.toLowerAscii()");
            foreach (var kv in _classFields)
            {
                string className = kv.Key;
                var fields = kv.Value;
                if (fields.Count == 0) continue;

                sb.AppendLine("  if o of " + className + ":");
                sb.AppendLine("    let inst = " + className + "(o)");
                for (int i = 0; i < fields.Count; i++)
                {
                    string fName = fields[i];
                    string cond = (i == 0) ? "if" : "elif";
                    sb.AppendLine("    " + cond + " idx == \"" + fName.ToLowerInvariant() + "\": return inst." + fName);
                }
            }
            sb.AppendLine("  return nil");
            sb.AppendLine();

            // Setter hook
            sb.AppendLine("userClassSetFieldHook = proc(o: RootRef, field: string, val: AhkVar) =");
            sb.AppendLine("  if o == nil: return");
            sb.AppendLine("  let idx = field.toLowerAscii()");
            foreach (var kv in _classFields)
            {
                string className = kv.Key;
                var fields = kv.Value;
                if (fields.Count == 0) continue;

                sb.AppendLine("  if o of " + className + ":");
                sb.AppendLine("    let inst = " + className + "(o)");
                for (int i = 0; i < fields.Count; i++)
                {
                    string fName = fields[i];
                    string cond = (i == 0) ? "if" : "elif";
                    sb.AppendLine("    " + cond + " idx == \"" + fName.ToLowerInvariant() + "\": inst." + fName + " = val");
                }
            }
            sb.AppendLine();

            return sb.ToString();
        }

        private string GetArgsStr(List<string> pnames, int maxArgs)
        {
            var taken = pnames.Take(maxArgs).ToList();
            return taken.Count > 0 ? ", " + string.Join(", ", taken) : "";
        }

        private string MakePad(int indent)
        {
            return new string(' ', indent * 4);
        }

        private string EmitBody(AstNode bodyNode, int indent)
        {
            if (bodyNode == null) return MakePad(indent) + "discard";
            
            string emitted;
            if (bodyNode.NodeType != "Block")
            {
                emitted = EmitNode(bodyNode, 0, true);
                if (IsExpressionNode(bodyNode))
                {
                    emitted = "discard " + emitted;
                }
            }
            else
            {
                emitted = EmitNode(bodyNode, indent);
            }
            
            string pad = MakePad(indent);
            
            if (bodyNode.NodeType != "Block")
            {
                if (emitted.Contains("\n"))
                {
                    var lines = emitted.Split(new[] { "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(lines[i]))
                        {
                            lines[i] = pad + lines[i];
                        }
                    }
                    emitted = string.Join("\n", lines);
                }
                else
                {
                    emitted = pad + emitted;
                }
            }
            if (IsEmptyOrOnlyComments(emitted))
            {
                if (emitted.EndsWith("\n"))
                {
                    emitted += pad + "discard";
                }
                else
                {
                    emitted += "\n" + pad + "discard";
                }
            }
            return emitted;
        }

        private string EmitMethodBody(AstNode bodyNode, int indent)
        {
            if (bodyNode == null) return MakePad(indent) + "discard";

            if (bodyNode.NodeType != "Block")
            {
                string emitted = EmitNode(bodyNode, 0, true);
                if (IsExpressionNode(bodyNode))
                {
                    emitted = "discard " + emitted;
                }
                string pad1 = MakePad(indent);
                if (!emitted.StartsWith(pad1))
                {
                    emitted = pad1 + emitted;
                }
                return emitted;
            }

            var statements = bodyNode.ChildNodes.Where(c => c != null).ToList();
            var lines = new List<string>();
            string pad = MakePad(indent);

            foreach (var stmt in statements)
            {
                string emitted = EmitNode(stmt, indent, true);
                if (!string.IsNullOrEmpty(emitted))
                {
                    if (IsExpressionNode(stmt))
                    {
                        string cleanEmitted = emitted.TrimStart();
                        string leadingPad = emitted.Substring(0, emitted.Length - cleanEmitted.Length);
                        emitted = leadingPad + "discard " + cleanEmitted;
                    }

                    if (!emitted.StartsWith(pad))
                    {
                        emitted = pad + emitted;
                    }
                    lines.Add(emitted);
                }
            }

            string result = string.Join("\n", lines);
            if (IsEmptyOrOnlyComments(result))
            {
                if (result.EndsWith("\n"))
                {
                    result += pad + "discard";
                }
                else
                {
                    result += "\n" + pad + "discard";
                }
            }
            return result;
        }

        private bool IsEmptyOrOnlyComments(string code)
        {
            if (string.IsNullOrEmpty(code)) return true;
            var lines = code.Split(new[] { "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                {
                    return false;
                }
            }
            return true;
        }

        private string EmitNode(AstNode node, int indent, bool isStatement = false)
        {
            if (node == null) return "";
            string pad = MakePad(indent);

            switch (node.NodeType)
            {
                case "Program":
                    return EmitChildren(node.ChildNodes, indent);

                case "Block":
                    return EmitChildren(node.ChildNodes, indent);

                case "Comment":
                    {
                        string commentVal = node.Value.TrimStart();
                        if (commentVal.StartsWith(";"))
                        {
                            commentVal = commentVal.Substring(1);
                        }
                        else if (commentVal.StartsWith("/*") && commentVal.EndsWith("*/"))
                        {
                            commentVal = commentVal.Substring(2, commentVal.Length - 4).Trim();
                        }
                        
                        var commentLines = commentVal.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        var commentSb = new StringBuilder();
                        for (int i = 0; i < commentLines.Length; i++)
                        {
                            commentSb.Append(pad + "# " + commentLines[i]);
                            if (i < commentLines.Length - 1) commentSb.Append("\n");
                        }
                        return commentSb.ToString();
                    }

                case "Directive":
                    return pad + "# directive: " + node.Value;

                case "Number":
                    return node.Value;

                case "Identifier":
                    if (node.Value.Equals("this", StringComparison.OrdinalIgnoreCase))
                    {
                        return "self";
                    }
                    string idName = node.Value;
                    for (int i = _nestedMethodsScopeStack.Count - 1; i >= 0; i--)
                    {
                        if (_nestedMethodsScopeStack[i].Contains(idName))
                        {
                            return _methodScopeStack[i] + "_" + NormalizeIdentifier(idName);
                        }
                    }
                    return NormalizeIdentifier(node.Value);

                case "This":
                    return "self";

                case "String":
                    return FormatStringLiteral(node.Value);

                case "Call":
                    {
                        string target = EmitNode(node.GetChild(0), 0);
                        if (target.Equals("DllCall", StringComparison.OrdinalIgnoreCase))
                        {
                            return EmitDllCall(node, indent);
                        }
                        if (_userClasses.Contains(target))
                        {
                            string args = node.ChildCount > 1 ? EmitNode(node.GetChild(1), 0) : "()";
                            return "new" + target + args;
                        }
                        string argsDefault = node.ChildCount > 1 ? EmitNode(node.GetChild(1), 0) : "()";
                        return target + argsDefault;
                    }

                case "Member":
                    {
                        string obj = EmitNode(node.GetChild(0), 0);
                        string memberName = node.Value;
                        if (memberName.StartsWith("%") && memberName.EndsWith("%") && memberName.Length > 2)
                        {
                            string inner = memberName.Substring(1, memberName.Length - 2);
                            return obj + "[" + NormalizeIdentifier(inner) + "]";
                        }
                        memberName = NormalizeIdentifier(memberName);
                        if (_userClasses.Contains(obj))
                        {
                            return obj + "_" + memberName;
                        }
                        if (memberName.Equals("hwnd", StringComparison.OrdinalIgnoreCase) ||
                            memberName.Equals("size", StringComparison.OrdinalIgnoreCase))
                        {
                            return obj + "[\"" + memberName + "\"]";
                        }
                        return obj + "." + memberName;
                    }

                case "Arguments":
                case "Parameters":
                    {
                        var list = new List<string>();
                        foreach (var c in node.ChildNodes)
                        {
                            list.Add(EmitNode(c, 0));
                        }
                        return "(" + string.Join(", ", list) + ")";
                    }

                case "Parameter":
                    return NormalizeIdentifier(node.Value);

                case "BinaryExpr":
                    {
                        string op = node.Value;
                        string lhs = EmitNode(node.GetChild(0), 0);
                        string rhs = EmitNode(node.GetChild(1), 0);

                        if (op == ".=")
                        {
                            if (!isStatement)
                            {
                                return "(" + lhs + " = " + lhs + " & " + rhs + "; " + lhs + ")";
                            }
                            return lhs + " = " + lhs + " & " + rhs;
                        }

                        if (op == "+=" || op == "-=" || op == "*=" || op == "/=")
                        {
                            if (node.GetChild(0).NodeType != "Identifier")
                            {
                                string binOp = op.Substring(0, op.Length - 1);
                                if (!isStatement)
                                {
                                    return "(" + lhs + " = " + lhs + " " + binOp + " " + rhs + "; " + lhs + ")";
                                }
                                return lhs + " = " + lhs + " " + binOp + " " + rhs;
                            }
                            if (!isStatement)
                            {
                                return "(" + lhs + " " + op + " " + rhs + "; " + lhs + ")";
                            }
                        }

                        if (op == ":=")
                        {
                            string lhsNodeName = node.GetChild(0).NodeType == "Identifier" ? NormalizeIdentifier(node.GetChild(0).Value) : null;
                            if (lhsNodeName != null && !_declaredVars.Contains(lhsNodeName) && !_globalVars.Contains(lhsNodeName))
                            {
                                _declaredVars.Add(lhsNodeName);
                                if (!isStatement)
                                {
                                    return "(var " + lhs + " = " + rhs + "; " + lhs + ")";
                                }
                                return "var " + lhs + " = " + rhs;
                            }
                            if (!isStatement)
                            {
                                return "(" + lhs + " = " + rhs + "; " + lhs + ")";
                            }
                            return lhs + " = " + rhs;
                        }

                        if (op == ".")
                        {
                            string lhsNodeVal = NormalizeIdentifier(node.GetChild(0).Value);
                            if (lhsNodeVal != null && _userClasses.Contains(lhsNodeVal))
                            {
                                return lhs + "_" + rhs;
                            }
                            return lhs + " & " + rhs;
                        }

                        if (op == "=") op = "==";
                        if (op == "<>") op = "!=";
                        if (op == "!==") op = "!=";
                        if (op == "//") op = "div";
                        if (op == "&&") return "toBool(" + lhs + ") and toBool(" + rhs + ")";
                        if (op == "||") return "toBool(" + lhs + ") or toBool(" + rhs + ")";
                        if (op == "|") op = "or";
                        if (op == "&") op = "and";
                        if (op == "^") op = "xor";
                        if (op == "<<") op = "shl";
                        if (op == ">>") op = "shr";

                        return lhs + " " + op + " " + rhs;
                    }

                case "ColonAssign":
                case "Assign":
                    {
                        string lhs = EmitNode(node.GetChild(0), 0);
                        string rhs = EmitNode(node.GetChild(1), 0);
                        string lhsNodeName = node.GetChild(0).NodeType == "Identifier" ? NormalizeIdentifier(node.GetChild(0).Value) : null;
                        if (lhsNodeName != null && !_declaredVars.Contains(lhsNodeName) && !_globalVars.Contains(lhsNodeName))
                        {
                            _declaredVars.Add(lhsNodeName);
                            if (!isStatement)
                            {
                                return "(var " + lhs + " = " + rhs + "; " + lhs + ")";
                            }
                            return "var " + lhs + " = " + rhs;
                        }
                        if (!isStatement)
                        {
                            return "(" + lhs + " = " + rhs + "; " + lhs + ")";
                        }
                        return lhs + " = " + rhs;
                    }

                case "UnaryExpr":
                    {
                        string op = node.Value;
                        string val = EmitNode(node.GetChild(0), 0);
                        if (op == "!") op = "not ";
                        else if (op == "not") op = "not ";
                        return op + val;
                    }

                case "PostfixExpr":
                    {
                        string op = node.Value;
                        string val = EmitNode(node.GetChild(0), 0);
                        if (op == "++")
                        {
                            if (!isStatement)
                            {
                                return "(let old_" + val + " = " + val + "; " + val + " = " + val + " + 1; old_" + val + ")";
                            }
                            return val + " = " + val + " + 1";
                        }
                        if (op == "--")
                        {
                            if (!isStatement)
                            {
                                return "(let old_" + val + " = " + val + "; " + val + " = " + val + " - 1; old_" + val + ")";
                            }
                            return val + " = " + val + " - 1";
                        }
                        return val + op;
                    }

                case "If":
                    {
                        var condNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        if (condNode != null && condNode.NodeType == "Grouped")
                        {
                            condNode = condNode.GetChild(0);
                        }
                        string cond = condNode != null ? "toBool(" + EmitNode(condNode, 0) + ")" : "true";
                        var bodyNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                        
                        var sb = new StringBuilder();
                        sb.Append(pad + "if " + cond + ":\n");
                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }

                        if (node.ChildCount > 2)
                        {
                            var elseNode = node.GetChild(2);
                            sb.Append("\n" + EmitNode(elseNode, indent));
                        }
                        return sb.ToString();
                    }

                case "Else":
                    {
                        var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        var sb = new StringBuilder();
                        
                        if (bodyNode != null && bodyNode.NodeType == "If")
                        {
                            string cond = bodyNode.ChildCount > 0 ? "toBool(" + EmitNode(bodyNode.GetChild(0), 0) + ")" : "true";
                            var innerBody = bodyNode.ChildCount > 1 ? bodyNode.GetChild(1) : null;
                            sb.Append(pad + "elif " + cond + ":\n");
                            if (innerBody != null)
                            {
                                sb.Append(EmitBody(innerBody, indent + 1));
                            }
                            else
                            {
                                sb.AppendLine(MakePad(indent + 1) + "discard");
                            }

                            if (bodyNode.ChildCount > 2)
                            {
                                var innerElse = bodyNode.GetChild(2);
                                sb.Append("\n" + EmitNode(innerElse, indent));
                            }
                        }
                        else
                        {
                            sb.Append(pad + "else:\n");
                            if (bodyNode != null)
                            {
                                sb.Append(EmitBody(bodyNode, indent + 1));
                            }
                            else
                            {
                                sb.AppendLine(MakePad(indent + 1) + "discard");
                            }
                        }
                        return sb.ToString();
                    }

                case "Try":
                    {
                        var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        var sb = new StringBuilder();
                        sb.Append(pad + "try:\n");
                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }
                        bool hasHandler = false;
                        for (int i = 1; i < node.ChildCount; i++)
                        {
                            var child = node.GetChild(i);
                            if (child != null && (child.NodeType == "Catch" || child.NodeType == "Finally"))
                            {
                                hasHandler = true;
                            }
                        }
                        for (int i = 1; i < node.ChildCount; i++)
                        {
                            var child = node.GetChild(i);
                            if (child != null)
                            {
                                sb.Append("\n" + EmitNode(child, indent));
                            }
                        }
                        if (!hasHandler)
                        {
                            sb.Append("\n" + MakePad(indent) + "except Exception:\n" + MakePad(indent + 1) + "discard");
                        }
                        return sb.ToString();
                    }

                case "Catch":
                    {
                        var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        var sb = new StringBuilder();
                        string varName = !string.IsNullOrEmpty(node.Metadata) ? " as " + NormalizeIdentifier(node.Metadata) : "";
                        sb.Append(pad + "except Exception" + varName + ":\n");
                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }
                        return sb.ToString();
                    }

                case "Finally":
                    {
                        var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        var sb = new StringBuilder();
                        sb.Append(pad + "finally:\n");
                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }
                        return sb.ToString();
                    }

                case "Throw":
                    {
                        var valNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        if (valNode != null && valNode.NodeType == "Call")
                        {
                            var target = valNode.GetChild(0).Value;
                            if (string.Equals(target, "Error", StringComparison.OrdinalIgnoreCase) && valNode.ChildCount > 1)
                            {
                                var argsNode = valNode.GetChild(1);
                                if (argsNode.ChildCount > 0)
                                {
                                    var arg = argsNode.GetChild(0);
                                    string argStr = EmitNode(arg, 0);
                                    return pad + "raise newException(ValueError, " + argStr + ".toString())";
                                }
                            }
                        }
                        string val = valNode != null ? EmitNode(valNode, 0) : "";
                        return pad + "raise newException(ValueError, " + (string.IsNullOrEmpty(val) ? "\"Error\"" : val + ".toString()") + ")";
                    }

                case "Loop":
                    {
                        string variant = !string.IsNullOrEmpty(node.Value) ? node.Value.ToLowerInvariant() : "";
                        var bodyNode = node.ChildNodes.LastOrDefault();
                        
                        var args = new List<string>();
                        for (int i = 0; i < node.ChildCount - 1; i++)
                        {
                            var child = node.GetChild(i);
                            if (child != null && child.NodeType != "Until")
                            {
                                args.Add(EmitNode(child, 0));
                            }
                        }

                        var sb = new StringBuilder();
                        if (variant == "files")
                        {
                            string pattern = args.Count > 0 ? args[0] : "\"*\"";
                            string mode = args.Count > 1 ? args[1] : "nil";
                            sb.Append(pad + "for _ in loopFiles(" + pattern + ", " + mode + "):\n");
                        }
                        else if (variant == "parse")
                        {
                            string str = args.Count > 0 ? args[0] : "\"\"";
                            string delim = args.Count > 1 ? args[1] : "nil";
                            sb.Append(pad + "for _ in loopParse(" + str + ", " + delim + "):\n");
                        }
                        else if (variant == "reg")
                        {
                            string keyName = args.Count > 0 ? args[0] : "\"\"";
                            string mode = args.Count > 1 ? args[1] : "nil";
                            sb.Append(pad + "for _ in loopReg(" + keyName + ", " + mode + "):\n");
                        }
                        else
                        {
                            string count = args.Count > 0 ? args[0] : "-1";
                            sb.Append(pad + "for _ in loopCount(" + count + "):\n");
                        }

                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }
                        return sb.ToString();
                    }

                case "While":
                    {
                        var condNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        if (condNode != null && condNode.NodeType == "Grouped")
                        {
                            condNode = condNode.GetChild(0);
                        }
                        string cond = condNode != null ? "toBool(" + EmitNode(condNode, 0) + ")" : "true";
                        var bodyNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                        var sb = new StringBuilder();
                        sb.Append(pad + "while " + cond + ":\n");
                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }
                        return sb.ToString();
                    }

                case "For":
                    {
                        if (node.ChildCount > 0)
                        {
                            var fvarsNode = node.GetChild(0);
                            if (fvarsNode.NodeType == "ForVars")
                            {
                                foreach (var c in fvarsNode.ChildNodes)
                                {
                                    _declaredVars.Add(NormalizeIdentifier(c.Value));
                                }
                            }
                            else if (fvarsNode.NodeType == "Identifier")
                            {
                                _declaredVars.Add(NormalizeIdentifier(fvarsNode.Value));
                            }
                        }
                        string fvars = node.ChildCount > 0 ? EmitNode(node.GetChild(0), 0) : "k, v";
                        string fcoll = node.ChildCount > 1 ? EmitNode(node.GetChild(1), 0) : "obj";
                        var bodyNode = node.ChildCount > 2 ? node.GetChild(2) : null;
                        var sb = new StringBuilder();
                        sb.Append(pad + "for " + fvars + " in " + fcoll + ":\n");
                        if (bodyNode != null)
                        {
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }
                        return sb.ToString();
                    }

                case "ForVars":
                    {
                        var list = new List<string>();
                        foreach (var c in node.ChildNodes)
                        {
                            list.Add(EmitNode(c, 0));
                        }
                        return string.Join(", ", list);
                    }

                case "Return":
                    return pad + "return" + (node.ChildCount > 0 ? " " + EmitNode(node.GetChild(0), 0) : "");

                case "Break":
                    return pad + "break";

                case "Continue":
                    return pad + "continue";

                case "Class":
                    {
                        string className = NormalizeIdentifier(node.Value);
                        
                        var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
                        string baseClass = extendsNode != null ? NormalizeIdentifier(extendsNode.Value) : "RootObj";

                        var instanceFields = new List<string>();
                        var staticFields = new Dictionary<string, string>();

                        foreach (var child in node.ChildNodes)
                        {
                            if (child == null || child.NodeType == "Method" || child.NodeType == "Extends" || child.NodeType == "Comment") continue;
                            
                            if (child.NodeType == "StaticAssign" || child.NodeType == "Declaration")
                            {
                                bool isStatic = (child.Metadata == "static");
                                string fieldName = NormalizeIdentifier(child.Value);
                                string initVal = "nil";

                                if (child.NodeType == "StaticAssign" && child.ChildCount > 0)
                                {
                                    initVal = EmitNode(child.GetChild(0), 0);
                                }

                                if (!string.IsNullOrEmpty(fieldName))
                                {
                                    if (isStatic)
                                    {
                                        staticFields[fieldName] = initVal;
                                    }
                                    else
                                    {
                                        instanceFields.Add(fieldName);
                                    }
                                }

                                // Handle comma-separated fields
                                if (child.ChildCount > 0)
                                {
                                    for (int i = (child.NodeType == "StaticAssign" ? 1 : 0); i < child.ChildCount; i++)
                                    {
                                        var subChild = child.GetChild(i);
                                        if (subChild != null && (subChild.NodeType == "StaticAssign" || subChild.NodeType == "Declaration"))
                                        {
                                            bool subStatic = (subChild.Metadata == "static");
                                            string subName = NormalizeIdentifier(subChild.Value);
                                            string subInit = "nil";
                                            if (subChild.NodeType == "StaticAssign" && subChild.ChildCount > 0)
                                            {
                                                subInit = EmitNode(subChild.GetChild(0), 0);
                                            }

                                            if (!string.IsNullOrEmpty(subName))
                                            {
                                                if (subStatic)
                                                {
                                                    staticFields[subName] = subInit;
                                                }
                                                else
                                                {
                                                    instanceFields.Add(subName);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        var sb = new StringBuilder();

                        var methods = node.ChildNodes.Where(c => c != null && c.NodeType == "Method").ToArray();
                        
                        // Factory function
                        var newMethod = FindConstructor(node);
                        string factoryParams = "";
                        string factoryCallArgs = "";
                        if (newMethod != null)
                        {
                            var plist = new List<string>();
                            var clist = new List<string>();
                            if (newMethod.ChildCount > 0 && newMethod.GetChild(0).NodeType == "Parameters")
                            {
                                foreach (var param in newMethod.GetChild(0).ChildNodes)
                                {
                                    string normalizedName = NormalizeIdentifier(param.Value);
                                    plist.Add(FormatParameter(param));
                                    clist.Add(normalizedName);
                                }
                            }
                            factoryParams = "(" + string.Join(", ", plist) + ")";
                            factoryCallArgs = string.Join(", ", clist);
                        }
                        else
                        {
                            factoryParams = "()";
                            factoryCallArgs = "";
                        }

                        sb.AppendLine();
                        sb.AppendLine(pad + "proc new" + className + "*" + factoryParams + ": " + className + " =");
                        sb.AppendLine(pad + "    result = " + className + "()");
                        
                        foreach (var field in instanceFields.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            string initVal = "nil";
                            foreach (var child in node.ChildNodes)
                            {
                                if (child == null || child.NodeType == "Method" || child.NodeType == "Extends" || child.NodeType == "Comment") continue;
                                if (child.NodeType == "StaticAssign" || child.NodeType == "Declaration")
                                {
                                    if (NormalizeIdentifier(child.Value).Equals(field, StringComparison.OrdinalIgnoreCase) && child.Metadata != "static")
                                    {
                                        if (child.NodeType == "StaticAssign" && child.ChildCount > 0)
                                        {
                                            initVal = EmitNode(child.GetChild(0), 0);
                                        }
                                        break;
                                    }
                                }
                            }
                            sb.AppendLine(pad + "    result." + field + " = " + initVal);
                        }

                        if (newMethod != null)
                        {
                            sb.AppendLine(pad + "    discard result.ahk_New(" + factoryCallArgs + ")");
                        }

                        if (methods.Length > 0)
                        {
                            sb.AppendLine();
                            foreach (var method in methods)
                            {
                                sb.AppendLine(EmitClassMethod(className, method, indent));
                            }
                        }
                        return sb.ToString();
                    }

                case "Method":
                    {
                        var outerVars = _declaredVars;
                        _declaredVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        string originalFuncName = NormalizeIdentifier(node.Value);
                        string funcName = originalFuncName;
                        if (_methodScopeStack.Count > 0)
                        {
                            funcName = _methodScopeStack[_methodScopeStack.Count - 1] + "_" + originalFuncName;
                        }
                        _methodScopeStack.Add(funcName);

                        string paramStr = "()";
                        var bodyNode = node;
                        if (node.ChildCount > 0 && node.GetChild(0).NodeType == "Parameters")
                        {
                            paramStr = EmitNode(node.GetChild(0), 0);
                            bodyNode = node.ChildCount > 1 ? methodNode(node) : null;
                            foreach (var p in node.GetChild(0).ChildNodes)
                            {
                                _declaredVars.Add(NormalizeIdentifier(p.Value));
                            }
                        }
                        else
                        {
                            bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                        }

                        if (paramStr != "()")
                        {
                            var plist = new List<string>();
                            foreach (var param in node.GetChild(0).ChildNodes)
                            {
                                plist.Add(FormatParameter(param));
                            }
                            paramStr = "(" + string.Join(", ", plist) + ")";
                        }

                        var sb = new StringBuilder();
                        sb.Append(pad + "proc " + funcName + (indent > 0 ? "" : "*") + paramStr + ": AhkVar =\n");

                        var nestedMethods = new List<AstNode>();
                        CollectAndRemoveNestedMethods(bodyNode, nestedMethods);

                        var currentNestedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var nested in nestedMethods)
                        {
                            if (!string.IsNullOrEmpty(nested.Value))
                            {
                                currentNestedNames.Add(nested.Value);
                            }
                        }
                        _nestedMethodsScopeStack.Add(currentNestedNames);

                        var localVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        CollectLocalVars(bodyNode, localVars);
                        var declsBuilder = new StringBuilder();
                        foreach (var local in localVars)
                        {
                            if (!_declaredVars.Contains(local) && !_globalVars.Contains(local))
                            {
                                _declaredVars.Add(local);
                                declsBuilder.Append(MakePad(indent + 1) + "var " + NormalizeIdentifier(local) + ": AhkVar\n");
                            }
                        }

                        if (node.ChildCount > 0 && node.GetChild(0).NodeType == "Parameters")
                        {
                            foreach (var p in node.GetChild(0).ChildNodes)
                            {
                                if (p.Metadata == "byref") continue;
                                string pName = NormalizeIdentifier(p.Value);
                                declsBuilder.Append(MakePad(indent + 1) + "var " + pName + " = " + pName + "\n");
                            }
                        }

                        foreach (var nested in nestedMethods)
                        {
                            string nOriginalName = NormalizeIdentifier(nested.Value);
                            string nUniqueName = funcName + "_" + nOriginalName;
                            string nParamStr = "()";
                            if (nested.ChildCount > 0 && nested.GetChild(0).NodeType == "Parameters")
                            {
                                var plist = new List<string>();
                                foreach (var param in nested.GetChild(0).ChildNodes)
                                {
                                    plist.Add(FormatParameter(param));
                                }
                                nParamStr = "(" + string.Join(", ", plist) + ")";
                            }
                            declsBuilder.Append(MakePad(indent + 1) + "proc " + nUniqueName + nParamStr + ": AhkVar\n");
                        }

                        var nestedDefsBuilder = new StringBuilder();
                        foreach (var nested in nestedMethods)
                        {
                            string emitted = EmitNode(nested, indent + 1, true);
                            if (!string.IsNullOrEmpty(emitted))
                            {
                                string padNested = MakePad(indent + 1);
                                if (!emitted.StartsWith(padNested))
                                {
                                    emitted = padNested + emitted;
                                }
                                nestedDefsBuilder.Append(emitted + "\n");
                            }
                        }

                        if (bodyNode != null)
                        {
                            string bodyEmitted = EmitMethodBody(bodyNode, indent + 1);
                            if (_config.IncludeDebugging)
                            {
                                var trySb = new StringBuilder();
                                trySb.AppendLine(MakePad(indent + 1) + "try:");
                                var lines = bodyEmitted.Split(new[] { "\n" }, StringSplitOptions.None);
                                foreach (var line in lines)
                                {
                                    if (string.IsNullOrEmpty(line))
                                    {
                                        trySb.AppendLine();
                                    }
                                    else
                                    {
                                        trySb.AppendLine("    " + line);
                                    }
                                }
                                trySb.AppendLine(MakePad(indent + 1) + "except Exception as e:");
                                trySb.AppendLine(MakePad(indent + 2) + "discard MsgBox(toAhkVar(\"Unhandled Exception: \" & e.msg & \"\\n\\nStack Trace:\\n\" & e.getStackTrace()), toAhkVar(\"Application Crash\"), toAhkVar(0x10))");
                                bodyEmitted = trySb.ToString();
                            }
                            sb.Append(declsBuilder.ToString());
                            sb.Append(nestedDefsBuilder.ToString());
                            sb.Append(bodyEmitted);
                        }
                        else
                        {
                            sb.AppendLine(MakePad(indent + 1) + "discard");
                        }

                        _declaredVars = outerVars;
                        if (_methodScopeStack.Count > 0)
                        {
                            _methodScopeStack.RemoveAt(_methodScopeStack.Count - 1);
                        }
                        if (_nestedMethodsScopeStack.Count > 0)
                        {
                            _nestedMethodsScopeStack.RemoveAt(_nestedMethodsScopeStack.Count - 1);
                        }
                        return sb.ToString();
                    }

                case "Array":
                    {
                        var list = new List<string>();
                        for (int i = 0; i < node.ChildCount; i++)
                        {
                            list.Add(EmitNode(node.GetChild(i), 0));
                        }
                        return "AhkArray(" + string.Join(", ", list) + ")";
                    }

                case "Object":
                    {
                        var list = new List<string>();
                        for (int i = 0; i < node.ChildCount; i++)
                        {
                            var child = node.GetChild(i);
                            if (child == null || child.NodeType == "Omitted") continue;
                            
                            var keyNode = child.GetChild(0);
                            var valNode = child.ChildCount > 1 ? child.GetChild(1) : null;
                            
                            string key = EmitNode(keyNode, 0);
                            if (!key.StartsWith("\"") && !key.StartsWith("'"))
                            {
                                key = "\"" + key + "\"";
                            }
                            else
                            {
                                key = FormatStringLiteral(key);
                            }
                            
                            string val = valNode != null ? EmitNode(valNode, 0) : "nil";
                            
                            list.Add(key);
                            list.Add(val);
                        }
                        return "Map(" + string.Join(", ", list) + ")";
                    }

                case "Index":
                    {
                        string obj = EmitNode(node.GetChild(0), 0);
                        string idx = EmitNode(node.GetChild(1), 0);
                        return obj + "[" + idx + "]";
                    }

                case "FatArrow":
                    {
                        var paramNode = node.GetChild(0);
                        string paramStr = "()";
                        if (paramNode != null && (paramNode.NodeType == "Parameters" || paramNode.NodeType == "Parameter"))
                        {
                            if (paramNode.NodeType == "Parameter")
                            {
                                paramStr = "(" + FormatParameter(paramNode) + ")";
                            }
                            else
                            {
                                var plist = new List<string>();
                                if (paramNode.ChildNodes != null)
                                {
                                    foreach (var param in paramNode.ChildNodes)
                                    {
                                        plist.Add(FormatParameter(param));
                                    }
                                }
                                paramStr = "(" + string.Join(", ", plist) + ")";
                            }
                        }
                        var bodyNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                        string bodyEmitted = "nil";
                        if (bodyNode != null)
                        {
                            bodyEmitted = EmitNode(bodyNode, 0);
                        }
                        return "proc" + paramStr + ": AhkVar = return " + bodyEmitted;
                    }

                case "FatArrowBody":
                    return node.ChildCount > 0 ? EmitNode(node.GetChild(0), indent, isStatement) : "nil";

                case "Include":
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine(pad + "# [Include] " + node.Value);
                        sb.Append(EmitChildren(node.ChildNodes, indent));
                        return sb.ToString();
                    }

                case "Omitted":
                    return "nil";

                case "Concat":
                    {
                        if (node.Metadata == "space" && IsStatementNode(node))
                        {
                            return EmitCommandCall(node);
                        }
                        // Support dynamic GUI control references like ddl%k% or ddl%k%.Text
                        if (node.Metadata == "nospace" && node.ChildCount == 2 && node.GetChild(0).NodeType == "Identifier")
                        {
                            string prefix = node.GetChild(0).Value;
                            var rightNode = node.GetChild(1);
                            if (rightNode.NodeType == "Identifier" && rightNode.Value.StartsWith("%") && rightNode.Value.EndsWith("%") && rightNode.Value.Length > 2)
                            {
                                string inner = rightNode.Value.Substring(1, rightNode.Value.Length - 2);
                                return "myGui[\"" + prefix + "\" & " + NormalizeIdentifier(inner) + ".toString()]";
                            }
                            if (rightNode.NodeType == "Member" && rightNode.ChildCount > 0 && rightNode.GetChild(0).NodeType == "Identifier")
                            {
                                var target = rightNode.GetChild(0);
                                if (target.Value.StartsWith("%") && target.Value.EndsWith("%") && target.Value.Length > 2)
                                {
                                    string inner = target.Value.Substring(1, target.Value.Length - 2);
                                    string memberName = NormalizeIdentifier(rightNode.Value);
                                    return "myGui[\"" + prefix + "\" & " + NormalizeIdentifier(inner) + ".toString()]." + memberName;
                                }
                            }
                        }
                        var list = new List<string>();
                        for (int i = 0; i < node.ChildCount; i++)
                        {
                            list.Add(EmitNode(node.GetChild(i), 0));
                        }
                        return string.Join(" & ", list);
                    }

                case "Declaration":
                    {
                        string varName = NormalizeIdentifier(node.Value);
                        bool alreadyDeclared = _declaredVars.Contains(varName) || _globalVars.Contains(varName);
                        _declaredVars.Add(varName);
                        if (node.ChildCount > 0 && node.GetChild(0).NodeType == "Declaration")
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine(pad + "# declared " + varName);
                            for (int i = 0; i < node.ChildCount; i++)
                            {
                                sb.Append(EmitNode(node.GetChild(i), indent));
                            }
                            return sb.ToString();
                        }
                        if (node.ChildCount > 0)
                        {
                            string val = EmitNode(node.GetChild(0), 0);
                            if (alreadyDeclared)
                            {
                                return pad + varName + " = " + val;
                            }
                            return pad + "var " + varName + " = " + val;
                        }
                        return pad + "# declared " + varName;
                    }

                case "MultiStatement":
                    {
                        if (node.ChildCount > 0 && node.GetChild(0).NodeType == "Concat" && node.GetChild(0).Metadata == "space")
                        {
                            var concat = node.GetChild(0);
                            var leaves = new List<AstNode>();
                            FlattenConcat(concat, leaves);
                            if (leaves.Count > 0)
                            {
                                string target = EmitNode(leaves[0], 0);
                                var args = new List<string>();
                                var firstArgParts = new List<string>();
                                for (int i = 1; i < leaves.Count; i++)
                                {
                                    firstArgParts.Add(EmitNode(leaves[i], 0));
                                }
                                if (firstArgParts.Count > 0)
                                {
                                    args.Add(string.Join(" & ", firstArgParts));
                                }
                                for (int i = 1; i < node.ChildCount; i++)
                                {
                                    args.Add(EmitNode(node.GetChild(i), 0));
                                }
                                return "discard " + target + "(" + string.Join(", ", args) + ")";
                            }
                            return "";
                        }
                        else
                        {
                            var list = new List<string>();
                            string childPad = MakePad(indent);
                            for (int i = 0; i < node.ChildCount; i++)
                            {
                                var child = node.GetChild(i);
                                if (child == null) continue;
                                string emitted = EmitNode(child, indent, true);
                                if (!string.IsNullOrEmpty(emitted))
                                {
                                    if (IsExpressionNode(child))
                                    {
                                        string cleanEmitted = emitted.TrimStart();
                                        string leadingPad = emitted.Substring(0, emitted.Length - cleanEmitted.Length);
                                        emitted = leadingPad + "discard " + cleanEmitted;
                                    }
                                    if (!emitted.StartsWith(childPad))
                                    {
                                        emitted = childPad + emitted;
                                    }
                                    list.Add(emitted);
                                }
                            }
                            return string.Join("\n", list);
                        }
                    }

                case "Grouped":
                    return "(" + EmitNode(node.GetChild(0), 0) + ")";

                case "Ternary":
                    {
                        string cond = EmitNode(node.GetChild(0), 0);
                        string thenVal = EmitNode(node.GetChild(1), 0);
                        string elseVal = EmitNode(node.GetChild(2), 0);
                        return "(if toBool(" + cond + "): toAhkVar(" + thenVal + ") else: toAhkVar(" + elseVal + "))";
                    }

                case "Sequence":
                    {
                        var list = new List<string>();
                        for (int i = 0; i < node.ChildCount; i++)
                        {
                            var c = node.GetChild(i);
                            string code = EmitNode(c, 0);
                            if (i < node.ChildCount - 1 && ShouldDiscardSequenceElement(c))
                            {
                                list.Add("discard " + code);
                            }
                            else
                            {
                                list.Add(code);
                            }
                        }
                        return "(" + string.Join("; ", list.ToArray()) + ")";
                    }

                case "Switch":
                    {
                        var sexpr = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType != "Case" && c.NodeType != "Default");
                        var cases = node.ChildNodes.Where(c => c != null && (c.NodeType == "Case" || c.NodeType == "Default")).ToList();
                        bool caseInsensitive = string.Equals(node.Metadata, "Off", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(node.Metadata);
                        
                        string valVarName = "switch_val_" + node.Line + "_" + node.Column;
                        var sb = new StringBuilder();
                        
                        if (sexpr != null)
                        {
                            string sexprCode = EmitNode(sexpr, 0);
                            if (caseInsensitive)
                            {
                                sb.AppendLine(pad + "var " + valVarName + " = StrLower(" + sexprCode + ")");
                            }
                            else
                            {
                                sb.AppendLine(pad + "var " + valVarName + " = " + sexprCode);
                            }
                        }

                        var caseNodes = cases.Where(c => c.NodeType == "Case").ToList();
                        var defaultNode = cases.FirstOrDefault(c => c.NodeType == "Default");

                        bool isFirst = true;
                        foreach (var cNode in caseNodes)
                        {
                            var conditions = new List<string>();
                            for (int ci = 0; ci < cNode.ChildCount - 1; ci++)
                            {
                                var cExpr = cNode.GetChild(ci);
                                string cExprCode = EmitNode(cExpr, 0);
                                if (sexpr != null)
                                {
                                    if (caseInsensitive)
                                    {
                                        conditions.Add(valVarName + " == StrLower(" + cExprCode + ")");
                                    }
                                    else
                                    {
                                        conditions.Add(valVarName + " == " + cExprCode);
                                    }
                                }
                                else
                                {
                                    conditions.Add("toBool(" + cExprCode + ")");
                                }
                            }

                            string condStr = string.Join(" or ", conditions.ToArray());
                            if (string.IsNullOrEmpty(condStr)) condStr = "false";

                            string keyword = isFirst ? "if" : "elif";
                            if (!isFirst)
                            {
                                sb.AppendLine();
                            }
                            sb.AppendLine(pad + keyword + " " + condStr + ":");
                            
                            var bodyNode = cNode.GetChild(cNode.ChildCount - 1);
                            sb.Append(EmitBody(bodyNode, indent + 1));
                            isFirst = false;
                        }

                        if (defaultNode != null)
                        {
                            string keyword = isFirst ? "if true" : "else";
                            if (!isFirst)
                            {
                                sb.AppendLine();
                            }
                            sb.AppendLine(pad + keyword + ":");
                            var bodyNode = defaultNode.GetChild(0);
                            sb.Append(EmitBody(bodyNode, indent + 1));
                        }
                        
                        if (caseNodes.Count == 0 && defaultNode == null)
                        {
                            sb.Append(pad + "discard");
                        }
                        
                        return sb.ToString();
                    }

                case "Variadic":
                    return node.ChildCount > 0 ? EmitNode(node.GetChild(0), indent, isStatement) : "";

                default:
                    return pad + "# [" + node.NodeType + "]" + (string.IsNullOrEmpty(node.Value) ? "" : " " + node.Value);
            }
        }

        private AstNode methodNode(AstNode node)
        {
            return node.ChildCount > 1 ? node.GetChild(1) : null;
        }

        private string EmitClassMethod(string className, AstNode methodNode, int indent)
        {
            var outerVars = _declaredVars;
            _declaredVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _declaredVars.Add("self");

            string methodName = NormalizeIdentifier(methodNode.Value);
            string paramStr = "()";
            var bodyNode = methodNode;
            if (methodNode.ChildCount > 0 && methodNode.GetChild(0).NodeType == "Parameters")
            {
                paramStr = EmitNode(methodNode.GetChild(0), 0);
                bodyNode = methodNode.ChildCount > 1 ? this.methodNode(methodNode) : null;
                foreach (var param in methodNode.GetChild(0).ChildNodes)
                {
                    _declaredVars.Add(NormalizeIdentifier(param.Value));
                }
            }
            else
            {
                bodyNode = methodNode.ChildCount > 0 ? methodNode.GetChild(0) : null;
            }

            var paramList = new List<string>();
            paramList.Add("self: " + className);
            if (methodNode.ChildCount > 0 && methodNode.GetChild(0).NodeType == "Parameters")
            {
                foreach (var param in methodNode.GetChild(0).ChildNodes)
                {
                    paramList.Add(FormatParameter(param));
                }
            }
            paramStr = "(" + string.Join(", ", paramList) + ")";

            var sb = new StringBuilder();
            sb.Append(MakePad(indent) + "proc " + methodName + "*" + paramStr + ": AhkVar =\n");

            var nestedMethods = new List<AstNode>();
            CollectAndRemoveNestedMethods(bodyNode, nestedMethods);

            var localVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectLocalVars(bodyNode, localVars);
            var declsBuilder = new StringBuilder();
            foreach (var local in localVars)
            {
                if (!_declaredVars.Contains(local) && !_globalVars.Contains(local))
                {
                    _declaredVars.Add(local);
                    declsBuilder.Append(MakePad(indent + 1) + "var " + NormalizeIdentifier(local) + ": AhkVar\n");
                }
            }

            declsBuilder.Append(MakePad(indent + 1) + "var self = self\n");
            if (methodNode.ChildCount > 0 && methodNode.GetChild(0).NodeType == "Parameters")
            {
                foreach (var param in methodNode.GetChild(0).ChildNodes)
                {
                    if (param.Metadata == "byref") continue;
                    string pName = NormalizeIdentifier(param.Value);
                    declsBuilder.Append(MakePad(indent + 1) + "var " + pName + " = " + pName + "\n");
                }
            }

            foreach (var nested in nestedMethods)
            {
                string nName = NormalizeIdentifier(nested.Value);
                string nParamStr = "()";
                if (nested.ChildCount > 0 && nested.GetChild(0).NodeType == "Parameters")
                {
                    var plist = new List<string>();
                    foreach (var param in nested.GetChild(0).ChildNodes)
                    {
                        plist.Add(FormatParameter(param));
                    }
                    nParamStr = "(" + string.Join(", ", plist) + ")";
                }
                declsBuilder.Append(MakePad(indent + 1) + "proc " + nName + nParamStr + ": AhkVar\n");
            }

            var nestedDefsBuilder = new StringBuilder();
            foreach (var nested in nestedMethods)
            {
                string emitted = EmitNode(nested, indent + 1, true);
                if (!string.IsNullOrEmpty(emitted))
                {
                    string padNested = MakePad(indent + 1);
                    if (!emitted.StartsWith(padNested))
                    {
                        emitted = padNested + emitted;
                    }
                    nestedDefsBuilder.Append(emitted + "\n");
                }
            }

            if (bodyNode != null)
            {
                string bodyEmitted = EmitMethodBody(bodyNode, indent + 1);
                if (_config.IncludeDebugging)
                {
                    var trySb = new StringBuilder();
                    trySb.AppendLine(MakePad(indent + 1) + "try:");
                    var lines = bodyEmitted.Split(new[] { "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            trySb.AppendLine();
                        }
                        else
                        {
                            trySb.AppendLine("    " + line);
                        }
                    }
                    trySb.AppendLine(MakePad(indent + 1) + "except Exception as e:");
                    trySb.AppendLine(MakePad(indent + 2) + "discard MsgBox(toAhkVar(\"Unhandled Exception: \" & e.msg & \"\\n\\nStack Trace:\\n\" & e.getStackTrace()), toAhkVar(\"Application Crash\"), toAhkVar(0x10))");
                    bodyEmitted = trySb.ToString();
                }
                sb.Append(declsBuilder.ToString());
                sb.Append(nestedDefsBuilder.ToString());
                sb.Append(bodyEmitted);
            }
            else
            {
                sb.AppendLine(MakePad(indent + 1) + "discard");
            }

            _declaredVars = outerVars;
            return sb.ToString();
        }

        private bool IsExpressionNode(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "FatArrowBody" && node.ChildCount > 0)
            {
                return IsExpressionNode(node.GetChild(0));
            }
            switch (node.NodeType)
            {
                case "Call":
                case "Ternary":
                case "UnaryExpr":
                case "Identifier":
                case "Member":
                case "Number":
                case "String":
                case "Grouped":
                case "Concat":
                    return true;
                case "BinaryExpr":
                    {
                        string op = node.Value;
                        if (op == ":=" || op == "=" || op == "+=" || op == "-=" || op == "*=" || op == "/=" || op == ".=")
                        {
                            return false;
                        }
                        return true;
                    }
                default:
                    return false;
            }
        }

        private bool IsAssignmentNode(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "Grouped" && node.ChildCount > 0)
            {
                return IsAssignmentNode(node.GetChild(0));
            }
            if (node.NodeType == "Assign" || node.NodeType == "ColonAssign") return true;
            if (node.NodeType == "BinaryExpr")
            {
                string op = node.Value;
                if (op == ":=" || op == "=" || op == "+=" || op == "-=" || op == "*=" || op == "/=" || op == ".=")
                {
                    return true;
                }
            }
            return false;
        }

        private bool ShouldDiscardSequenceElement(AstNode node)
        {
            if (node == null) return false;
            return true;
        }

        private bool IsStatementNode(AstNode node)
        {
            if (node == null) return false;
            if (node.Parent == null) return true;
            
            var parent = node.Parent;
            string pType = parent.NodeType;
            
            if (pType == "Program" || pType == "Block" || pType == "MultiStatement" ||
                pType == "Try" || pType == "Catch" || pType == "Finally" || pType == "Else" ||
                pType == "CaseBody" || pType == "DefaultBody")
            {
                return true;
            }
            
            if (pType == "If" || pType == "While")
            {
                return parent.ChildCount > 0 && parent.GetChild(0) != node;
            }
            
            if (pType == "For")
            {
                return parent.ChildCount > 2 && parent.GetChild(2) == node;
            }
            
            if (pType == "Loop")
            {
                return parent.ChildCount > 0 && parent.GetChild(parent.ChildCount - 1) == node;
            }
            
            if (pType == "Case")
            {
                return parent.ChildCount > 0 && parent.GetChild(parent.ChildCount - 1) == node;
            }
            
            if (pType == "Default")
            {
                return parent.ChildCount > 0 && parent.GetChild(0) == node;
            }
            
            return false;
        }

        private string EmitCommandCall(AstNode concatNode)
        {
            var leaves = new List<AstNode>();
            FlattenConcat(concatNode, leaves);
            if (leaves.Count == 0) return "";
            
            string target = EmitNode(leaves[0], 0);
            var args = new List<string>();
            for (int i = 1; i < leaves.Count; i++)
            {
                args.Add(EmitNode(leaves[i], 0));
            }
            return target + "(" + string.Join(" & ", args) + ")";
        }

        private void FlattenConcat(AstNode node, List<AstNode> elements)
        {
            if (node.NodeType == "Concat")
            {
                FlattenConcat(node.GetChild(0), elements);
                FlattenConcat(node.GetChild(1), elements);
            }
            else
            {
                elements.Add(node);
            }
        }

        private string EmitChildren(AstNode[] children, int childIndent)
        {
            var lines = new List<string>();
            string pad = MakePad(childIndent);
            foreach (var child in children)
            {
                if (child == null) continue;
                string emitted = EmitNode(child, childIndent, true);
                if (!string.IsNullOrEmpty(emitted))
                {
                    if (IsExpressionNode(child))
                    {
                        string cleanEmitted = emitted.TrimStart();
                        string leadingPad = emitted.Substring(0, emitted.Length - cleanEmitted.Length);
                        emitted = leadingPad + "discard " + cleanEmitted;
                    }

                    if (childIndent > 0 && !emitted.StartsWith(pad))
                    {
                        emitted = pad + emitted;
                    }
                    lines.Add(emitted);
                }
            }
            return string.Join("\n", lines);
        }

        private string FormatStringLiteral(string originalVal)
        {
            if (string.IsNullOrEmpty(originalVal)) return "\"\"";
            
            bool isSingleQuote = originalVal.StartsWith("'");
            string inner = originalVal.Substring(1, originalVal.Length - 2);
            
            if (isSingleQuote)
            {
                inner = inner.Replace("\\", "\\\\")
                             .Replace("\"", "\\\"")
                             .Replace("`n", "\\n")
                             .Replace("`r", "\\r")
                             .Replace("`t", "\\t")
                             .Replace("`'", "'")
                             .Replace("``", "`");
            }
            else
            {
                inner = inner.Replace("\"\"", "\\\"");
                inner = inner.Replace("\\", "\\\\");
                inner = inner.Replace("`n", "\\n")
                             .Replace("`r", "\\r")
                             .Replace("`t", "\\t")
                             .Replace("`\"", "\\\"")
                             .Replace("`'", "'")
                             .Replace("``", "`");
            }
            
            return "\"" + inner + "\"";
        }

        private static readonly HashSet<string> NimKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "addr", "and", "as", "asm", "bind", "block", "break", "case", "cast", "concept", "const", "continue",
            "converter", "defer", "discard", "distinct", "div", "do", "elif", "else", "end", "enum", "except",
            "export", "finally", "for", "from", "func", "if", "import", "in", "include", "interface", "is", "isnot",
            "iterator", "let", "macro", "method", "mixin", "mod", "nil", "not", "notin", "object", "of", "or",
            "out", "proc", "ptr", "raise", "ref", "return", "shl", "shr", "static", "template", "try", "type",
            "using", "var", "when", "while", "xor", "yield", "result"
        };

        private string NormalizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name == "*") return "unused";
            string normalized = name;
            if (normalized.StartsWith("__"))
            {
                normalized = "ahk_" + normalized.Substring(2);
            }
            while (normalized.Contains("__"))
            {
                normalized = normalized.Replace("__", "_");
            }
            if (NimKeywords.Contains(normalized))
            {
                normalized = "ahk_" + normalized;
            }
            return normalized;
        }

        private string FormatParameter(AstNode paramNode)
        {
            if (paramNode == null) return "unused: AhkVar = nil";
            string name = NormalizeIdentifier(paramNode.Value);
            if (paramNode.Metadata == "byref")
            {
                return name + ": var AhkVar";
            }
            return name + ": AhkVar = nil";
        }

        private string GetStringValue(AstNode n)
        {
            if (n == null) return "";
            string val = n.Value;
            if (val == null) return "";
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                return val.Substring(1, val.Length - 2);
            if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                return val.Substring(1, val.Length - 2);
            return val;
        }

        private string MapAhkTypeToNim(string ahkType, out string convertFn)
        {
            string t = ahkType.Trim().ToLowerInvariant();
            bool isPtr = t.EndsWith("*") || t.EndsWith("p");
            if (isPtr)
            {
                t = t.Substring(0, t.Length - 1).Trim();
            }

            string nimBase;
            switch (t)
            {
                case "ptr":
                case "uptr":
                    nimBase = "pointer";
                    convertFn = "toPointer";
                    break;
                case "str":
                case "wstr":
                    nimBase = "LPCWSTR";
                    convertFn = "toWstr";
                    break;
                case "astr":
                    nimBase = "cstring";
                    convertFn = "toCstring";
                    break;
                case "int":
                    nimBase = "int32";
                    convertFn = "toInt32";
                    break;
                case "uint":
                    nimBase = "uint32";
                    convertFn = "toUInt32";
                    break;
                case "short":
                    nimBase = "int16";
                    convertFn = "toInt32";
                    break;
                case "ushort":
                    nimBase = "uint16";
                    convertFn = "toUInt32";
                    break;
                case "char":
                    nimBase = "int8";
                    convertFn = "toInt32";
                    break;
                case "uchar":
                    nimBase = "uint8";
                    convertFn = "toUInt32";
                    break;
                case "int64":
                    nimBase = "int64";
                    convertFn = "toInt64";
                    break;
                case "uint64":
                    nimBase = "uint64";
                    convertFn = "toUInt64";
                    break;
                case "float":
                    nimBase = "float32";
                    convertFn = "toFloat32";
                    break;
                case "double":
                    nimBase = "float64";
                    convertFn = "toFloat64";
                    break;
                case "void":
                    nimBase = "void";
                    convertFn = "";
                    break;
                default:
                    nimBase = "int32";
                    convertFn = "toInt32";
                    break;
            }

            if (isPtr)
            {
                return "ptr " + nimBase;
            }
            return nimBase;
        }

        private string EmitDllCall(AstNode node, int indent)
        {
            var argsNode = node.GetChild(1);
            int argCount = argsNode.ChildCount;
            if (argCount == 0) return "nil";

            var firstArg = argsNode.GetChild(0);
            string dllFunction = GetStringValue(firstArg);

            int remainingCount = argCount - 1;
            bool hasExplicitReturnType = (remainingCount % 2 == 1);
            string rawRetType = hasExplicitReturnType ? GetStringValue(argsNode.GetChild(argCount - 1)) : "int";

            string retTypeStr = "int";
            bool isCdecl = false;
            if (rawRetType.Contains("cdecl"))
            {
                isCdecl = true;
                retTypeStr = rawRetType.Replace("cdecl", "").Trim();
            }
            else
            {
                retTypeStr = rawRetType.Trim();
            }

            var paramTypes = new List<string>();
            var paramExprs = new List<AstNode>();
            int endLimit = argCount - (hasExplicitReturnType ? 1 : 0);
            for (int idx = 1; idx < endLimit; idx += 2)
            {
                if (idx + 1 >= argCount) break;
                paramTypes.Add(GetStringValue(argsNode.GetChild(idx)));
                paramExprs.Add(argsNode.GetChild(idx + 1));
            }

            string dllName = "user32";
            string funcName = dllFunction;
            if (dllFunction.Contains("\\"))
            {
                var parts = dllFunction.Split('\\');
                dllName = parts[0];
                funcName = parts[1];
            }
            else
            {
                string mappedDll;
                if (_commonDllMap.TryGetValue(funcName, out mappedDll))
                {
                    dllName = mappedDll;
                }
            }

            if (dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                dllName = dllName.Substring(0, dllName.Length - 4);
            }

            bool resolved;
            string resolvedFuncName = ResolveDllExport(ref dllName, funcName, out resolved);
            if (!resolved)
            {
                if ((dllName.Equals("user32", StringComparison.OrdinalIgnoreCase) ||
                     dllName.Equals("kernel32", StringComparison.OrdinalIgnoreCase) ||
                     dllName.Equals("gdi32", StringComparison.OrdinalIgnoreCase) ||
                     dllName.Equals("shell32", StringComparison.OrdinalIgnoreCase) ||
                     dllName.Equals("advapi32", StringComparison.OrdinalIgnoreCase) ||
                     dllName.Equals("comctl32", StringComparison.OrdinalIgnoreCase) ||
                     dllName.Equals("comdlg32", StringComparison.OrdinalIgnoreCase)) &&
                    !funcName.EndsWith("W", StringComparison.Ordinal) &&
                    !funcName.EndsWith("A", StringComparison.Ordinal) &&
                    !_noSuffixWin32Funcs.Contains(funcName))
                {
                    resolvedFuncName = funcName + "W";
                }
            }

            var keyBuilder = new StringBuilder();
            keyBuilder.Append(dllName.ToLowerInvariant());
            keyBuilder.Append("_");
            keyBuilder.Append(resolvedFuncName);
            foreach (var pt in paramTypes)
            {
                keyBuilder.Append("_");
                keyBuilder.Append(pt.Replace("*", "ptr").ToLowerInvariant());
            }
            keyBuilder.Append("_");
            keyBuilder.Append(retTypeStr.ToLowerInvariant());

            string importKey = keyBuilder.ToString();
            string procName;
            if (!_dllProcNames.TryGetValue(importKey, out procName))
            {
                procName = "dll_" + resolvedFuncName + "_" + (_dllImports.Count + 1);
                _dllProcNames[importKey] = procName;

                string convertRetFn;
                string nimRetType = MapAhkTypeToNim(retTypeStr, out convertRetFn);

                var paramDefs = new List<string>();
                for (int i = 0; i < paramTypes.Count; i++)
                {
                    string convertFn;
                    string nimType = MapAhkTypeToNim(paramTypes[i], out convertFn);
                    paramDefs.Add("p" + i + ": " + nimType);
                }
                string paramsStr = string.Join(", ", paramDefs);
                string callingConv = isCdecl ? "cdecl" : "stdcall";
                string retTypeSuffix = (nimRetType == "void") ? "" : ": " + nimRetType;
                string procDef = "proc " + procName + "(" + paramsStr + ")" + retTypeSuffix + " {." + callingConv + ", dynlib: \"" + dllName + "\", importc: \"" + resolvedFuncName + "\".}";
                _dllImports[importKey] = procDef;
            }

            var tempDecls = new List<string>();
            var callArgs = new List<string>();
            var writebacks = new List<string>();

            for (int i = 0; i < paramTypes.Count; i++)
            {
                string pType = paramTypes[i];
                bool isPtr = pType.EndsWith("*") || pType.EndsWith("p", StringComparison.OrdinalIgnoreCase);
                AstNode argNode = paramExprs[i];
                string argEmitted = EmitNode(argNode, 0);

                string convertFn;
                string nimType = MapAhkTypeToNim(pType, out convertFn);

                if (isPtr)
                {
                    string baseNimType = nimType.Replace("ptr ", "");
                    string tempVarName = "temp_" + i;

                    string initVal;
                    if (argNode.NodeType == "UnaryExpr" && argNode.Value == "&" && argNode.ChildCount > 0)
                    {
                        initVal = EmitNode(argNode.GetChild(0), 0);
                    }
                    else
                    {
                        initVal = argEmitted;
                    }

                    tempDecls.Add("var " + tempVarName + " = cast[" + baseNimType + "](" + convertFn + "(" + initVal + "))");
                    callArgs.Add("addr " + tempVarName);

                    if (argNode.NodeType == "UnaryExpr" && argNode.Value == "&" && argNode.ChildCount > 0)
                    {
                        string targetVar = EmitNode(argNode.GetChild(0), 0);
                        writebacks.Add(targetVar + " = toAhkVar(" + tempVarName + ")");
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(convertFn))
                    {
                        callArgs.Add(argEmitted);
                    }
                    else
                    {
                        callArgs.Add(convertFn + "(" + argEmitted + ")");
                    }
                }
            }

            string convertRetFn2;
            string nimRetType2 = MapAhkTypeToNim(retTypeStr, out convertRetFn2);
            bool isVoid = (nimRetType2 == "void");

            if (tempDecls.Count > 0 || isVoid)
            {
                var sb = new StringBuilder();
                sb.AppendLine("(block:");
                string pad1 = MakePad(indent + 1);
                foreach (var decl in tempDecls)
                {
                    sb.AppendLine(pad1 + decl);
                }

                string callStr = procName + "(" + string.Join(", ", callArgs) + ")";
                if (isVoid)
                {
                    sb.AppendLine(pad1 + callStr);
                }
                else
                {
                    sb.AppendLine(pad1 + "let res = " + callStr);
                }

                foreach (var wb in writebacks)
                {
                    sb.AppendLine(pad1 + wb);
                }

                if (isVoid)
                {
                    sb.AppendLine(pad1 + "nil");
                }
                else
                {
                    sb.AppendLine(pad1 + "toAhkVar(res)");
                }
                sb.Append(MakePad(indent) + ")");
                return sb.ToString();
            }
            else
            {
                return "toAhkVar(" + procName + "(" + string.Join(", ", callArgs) + "))";
            }
        }
    }
}
