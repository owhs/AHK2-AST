using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AHK2AST.Plugins
{
    // =========================================================================
    // 1. CUSTOM TRANSFORM PLUGIN
    // =========================================================================

    public class CustomTransformConfig
    {
        [Category("Comments"), DisplayName("Strip Comments"), Description("If true, all comment nodes will be stripped from the AST.")]
        public bool StripComments { get; set; }

        [Category("Comments"), DisplayName("Conditional Compilation"), Description("If true, enables preprocessor comments like ;AST-IF(var), ;AST-ELSE, and ;AST-ENDIF to conditionally include/exclude code.")]
        public bool ConditionalCompilation { get; set; }

        [Category("Comments"), DisplayName("Preprocessor Variables"), Description("Comma-separated list of variable definitions for conditional compilation (e.g. DEBUG=true, VERSION=2).")]
        public string PreprocessorVars { get; set; }

        [Category("Rename"), DisplayName("Rename From"), Description("The name of the identifier (variable/function) to search for.")]
        public string RenameFrom { get; set; }

        [Category("Rename"), DisplayName("Rename To"), Description("The new name to replace the target identifier with.")]
        public string RenameTo { get; set; }

        [Category("Variables"), DisplayName("Target Variable"), Description("The name of the variable whose assigned value should be modified.")]
        public string TargetVariable { get; set; }

        [Category("Variables"), DisplayName("New Value"), Description("The new literal value (string, number, or boolean) to assign to the target variable.")]
        public string NewValue { get; set; }

        public CustomTransformConfig()
        {
            StripComments = false;
            ConditionalCompilation = true;
            PreprocessorVars = "";
            RenameFrom = "";
            RenameTo = "";
            TargetVariable = "";
            NewValue = "";
        }
    }

    public class CustomTransformPlugin : IFlowPlugin
    {
        public string Name { get { return "Transform.Custom-Transform"; } }
        public string Target { get; set; }

        public string Category { get { return "Transform"; } }
        public string StepTitle { get { return "Custom Transform"; } }
        public string Icon { get { return "🛠️"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(CustomTransformConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (CustomTransformConfig)config; }

        public CustomTransformConfig Config { get; set; }

        private Dictionary<string, string> _preprocessorVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public CustomTransformPlugin()
        {
            Config = new CustomTransformConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            // Parse preprocessor variables
            _preprocessorVars.Clear();
            if (!string.IsNullOrEmpty(Config.PreprocessorVars))
            {
                var pairs = Config.PreprocessorVars.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var parts = pair.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        _preprocessorVars[parts[0].Trim()] = parts[1].Trim();
                    }
                    else if (parts.Length == 1)
                    {
                        _preprocessorVars[parts[0].Trim()] = "true";
                    }
                }
            }

            // Apply transformations
            ApplyTransform(root);

            return root;
        }

        private void ApplyTransform(AstNode node)
        {
            if (node == null) return;

            // 1. Process statement containers for conditional compilation (;AST-IF)
            if (Config.ConditionalCompilation && IsStatementContainer(node))
            {
                ProcessConditionalCompilation(node);
            }

            // 2. Process child transformations
            for (int i = node.ChildCount - 1; i >= 0; i--)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                // Strip comments
                if (Config.StripComments && child.NodeType == "Comment")
                {
                    node.RemoveChild(i);
                    continue;
                }

                // Global rename
                if (!string.IsNullOrEmpty(Config.RenameFrom) && !string.IsNullOrEmpty(Config.RenameTo))
                {
                    if (child.NodeType == "Identifier" && child.Value.Equals(Config.RenameFrom, StringComparison.OrdinalIgnoreCase))
                    {
                        child.Value = Config.RenameTo;
                    }
                }

                // Variable assignment replacement
                if (!string.IsNullOrEmpty(Config.TargetVariable) && Config.NewValue != null)
                {
                    if (child.NodeType == "ColonAssign" || child.NodeType == "Assign" || (child.NodeType == "BinaryExpr" && child.Value == ":="))
                    {
                        var lhs = child.ChildCount > 0 ? child.GetChild(0) : null;
                        if (lhs != null && lhs.NodeType == "Identifier" && lhs.Value.Equals(Config.TargetVariable, StringComparison.OrdinalIgnoreCase))
                        {
                            if (child.ChildCount > 1)
                            {
                                var newValueNode = ParseLiteralNode(Config.NewValue, child.Line, child.Column);
                                child.ReplaceChild(1, newValueNode);
                            }
                        }
                    }
                    else if (child.NodeType == "Declaration" && child.Value.Equals(Config.TargetVariable, StringComparison.OrdinalIgnoreCase))
                    {
                        if (child.ChildCount > 0)
                        {
                            var newValueNode = ParseLiteralNode(Config.NewValue, child.Line, child.Column);
                            child.ReplaceChild(0, newValueNode);
                        }
                    }
                }

                ApplyTransform(child);
            }
        }

        private bool IsStatementContainer(AstNode node)
        {
            if (node == null) return false;
            string type = node.NodeType;
            return type == "Program" || type == "Block" || type == "Include" || type == "CaseBody" || type == "DefaultBody";
        }

        private void ProcessConditionalCompilation(AstNode node)
        {
            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                if (child.NodeType == "Comment")
                {
                    string commentVal = child.Value.Trim();
                    if (commentVal.StartsWith(";AST-IF", StringComparison.OrdinalIgnoreCase) || commentVal.StartsWith(";@if", StringComparison.OrdinalIgnoreCase))
                    {
                        // Parse condition
                        string condStr = "";
                        int startParen = commentVal.IndexOf('(');
                        int endParen = commentVal.LastIndexOf(')');
                        if (startParen != -1 && endParen > startParen)
                        {
                            condStr = commentVal.Substring(startParen + 1, endParen - startParen - 1).Trim();
                        }
                        else
                        {
                            int spaceIdx = commentVal.IndexOf(' ');
                            if (spaceIdx != -1) condStr = commentVal.Substring(spaceIdx + 1).Trim();
                        }

                        bool conditionResult = EvaluateCondition(condStr);

                        // Find matching ELSE and ENDIF
                        int elseIndex = -1;
                        int endifIndex = -1;
                        int nestLevel = 1;

                        for (int k = i + 1; k < node.ChildCount; k++)
                        {
                            var nextChild = node.GetChild(k);
                            if (nextChild != null && nextChild.NodeType == "Comment")
                            {
                                string nextVal = nextChild.Value.Trim();
                                if (nextVal.StartsWith(";AST-IF", StringComparison.OrdinalIgnoreCase) || nextVal.StartsWith(";@if", StringComparison.OrdinalIgnoreCase))
                                {
                                    nestLevel++;
                                }
                                else if (nextVal.StartsWith(";AST-ENDIF", StringComparison.OrdinalIgnoreCase) || nextVal.StartsWith(";@endif", StringComparison.OrdinalIgnoreCase))
                                {
                                    nestLevel--;
                                    if (nestLevel == 0)
                                    {
                                        endifIndex = k;
                                        break;
                                    }
                                }
                                else if (nextVal.StartsWith(";AST-ELSE", StringComparison.OrdinalIgnoreCase) || nextVal.StartsWith(";@else", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (nestLevel == 1)
                                    {
                                        elseIndex = k;
                                    }
                                }
                            }
                        }

                        if (endifIndex != -1)
                        {
                            var toRemove = new HashSet<int>();
                            toRemove.Add(i);
                            toRemove.Add(endifIndex);
                            if (elseIndex != -1) toRemove.Add(elseIndex);

                            if (conditionResult)
                            {
                                if (elseIndex != -1)
                                {
                                    for (int r = elseIndex; r < endifIndex; r++)
                                        toRemove.Add(r);
                                }
                            }
                            else
                            {
                                int limit = elseIndex != -1 ? elseIndex : endifIndex;
                                for (int r = i + 1; r < limit; r++)
                                    toRemove.Add(r);
                            }

                            var sortedRemove = toRemove.OrderByDescending(idx => idx).ToList();
                            foreach (int idx in sortedRemove)
                            {
                                node.RemoveChild(idx);
                            }

                            i--; // Reprocess current index since children changed
                        }
                    }
                }
            }
        }

        private bool EvaluateCondition(string condStr)
        {
            if (string.IsNullOrEmpty(condStr)) return false;

            string val;
            if (_preprocessorVars.TryGetValue(condStr, out val))
            {
                if (val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1") return true;
                if (val.Equals("false", StringComparison.OrdinalIgnoreCase) || val == "0") return false;
                return !string.IsNullOrEmpty(val);
            }

            if (condStr.Equals("true", StringComparison.OrdinalIgnoreCase) || condStr == "1") return true;
            if (condStr.Equals("false", StringComparison.OrdinalIgnoreCase) || condStr == "0") return false;

            return false;
        }

        private AstNode ParseLiteralNode(string text, int line, int col)
        {
            double d;
            if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
            {
                return new AstNode("Number", line, col) { Value = text };
            }
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return new AstNode("Identifier", line, col) { Value = text.ToLower() };
            }
            // Assume string
            string strVal = text;
            if (strVal.StartsWith("\"") && strVal.EndsWith("\"")) strVal = strVal.Substring(1, strVal.Length - 2);
            else if (strVal.StartsWith("'") && strVal.EndsWith("'")) strVal = strVal.Substring(1, strVal.Length - 2);
            strVal = strVal.Replace("\"", "`\"");
            return new AstNode("String", line, col) { Value = "\"" + strVal + "\"" };
        }
    }

    // =========================================================================
    // 2. BEAUTIFIER PLUGIN
    // =========================================================================

    public class BeautifyConfig
    {
        [Category("Indentation"), DisplayName("Use Tabs"), Description("If true, uses tabs for indentation. Otherwise, uses spaces.")]
        public bool UseTabs { get; set; }

        [Category("Indentation"), DisplayName("Indent Size"), Description("Number of spaces per indentation level.")]
        public int IndentSize { get; set; }

        [Category("Spacing"), DisplayName("Emit Blank Lines"), Description("Preserve blank lines between statements.")]
        public bool EmitBlankLines { get; set; }

        [Category("Spacing"), DisplayName("Function Spacing"), Description("Number of blank lines to put between top-level functions and classes.")]
        public int FunctionSpacing { get; set; }

        [Category("Comments"), DisplayName("Emit Comments"), Description("Include comments in the formatted output.")]
        public bool EmitComments { get; set; }

        public BeautifyConfig()
        {
            UseTabs = false;
            IndentSize = 4;
            EmitBlankLines = true;
            FunctionSpacing = 1;
            EmitComments = true;
        }
    }

    public class BeautifyPlugin : IFlowPlugin
    {
        public string Name { get { return "Formatting.Beautify"; } }
        public string Target { get; set; }

        public string Category { get { return "Formatting"; } }
        public string StepTitle { get { return "Beautify"; } }
        public string Icon { get { return "✨"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(BeautifyConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (BeautifyConfig)config; }

        public BeautifyConfig Config { get; set; }

        public BeautifyPlugin()
        {
            Config = new BeautifyConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return "";

            var opts = new EmitOptions
            {
                EmitComments = Config.EmitComments,
                EmitBlankLines = Config.EmitBlankLines,
                PreserveIndent = false,
                UseTabs = Config.UseTabs,
                IndentSize = Config.IndentSize
            };

            var emitter = new BeautifierFormatter(opts, Config.FunctionSpacing);
            return emitter.Format(root);
        }
    }

    internal class BeautifierFormatter
    {
        private EmitOptions _opts;
        private int _functionSpacing;

        public BeautifierFormatter(EmitOptions opts, int functionSpacing)
        {
            _opts = opts;
            _functionSpacing = functionSpacing;
        }

        public string Format(AstNode node)
        {
            return FormatNode(node, 0);
        }

        private string MakePad(int indent)
        {
            if (indent <= 0) return "";
            if (_opts.UseTabs) return new string('\t', indent);
            return new string(' ', indent * _opts.IndentSize);
        }

        private string FormatNode(AstNode node, int indent)
        {
            if (node == null) return "";
            string pad = MakePad(indent);

            // Let the standard emitter handle standard single expression/statement emission if possible,
            // but we override block statement formatting and spacing at the top level.
            if (node.NodeType == "Program")
            {
                return FormatChildren(node.ChildNodes, indent, isTopLevel: true);
            }
            if (node.NodeType == "Include")
            {
                // Same shape as the standard emitter: an inlined file between its begin/end markers, so formatting the
                // output again sees the same file boundaries (and rebuilds the same Include from the markers).
                if (node.ChildCount > 0 && !_opts.PreserveIncludes)
                {
                    string fileName = !string.IsNullOrEmpty(node.Value) ? System.IO.Path.GetFileName(node.Value) : "unknown";
                    return pad + "; --- begin: " + fileName + " ---\n" + FormatChildren(node.ChildNodes, indent, isTopLevel: true)
                        + "\n" + pad + "; --- end: " + fileName + " ---";
                }
                return AstEmitter.Emit(node, _opts, indent);
            }
            if (node.NodeType == "Block")
            {
                string stmts = FormatChildren(node.ChildNodes, indent + 1, isTopLevel: false);
                if (string.IsNullOrEmpty(stmts))
                {
                    return "{\n" + pad + "}";
                }
                return "{\n" + stmts + "\n" + pad + "}";
            }

            // Fallback to standard AST emitter
            return AstEmitter.Emit(node, _opts, indent);
        }

        private string FormatChildren(AstNode[] children, int indent, bool isTopLevel)
        {
            if (children == null || children.Length == 0) return "";
            bool wantBlanks = _opts.EmitBlankLines;
            bool wantComments = _opts.EmitComments;
            // Without comments there are no begin/end markers, so an inlined file is formatted as part of the code
            // around it (formatting the output again then sees the same structure).
            while (!wantComments && children.Any(c => c != null && c.NodeType == "Include" && c.ChildCount > 0)) // nested includes too
            {
                var flat = new List<AstNode>();
                foreach (var c in children)
                {
                    if (c != null && c.NodeType == "Include" && c.ChildCount > 0) flat.AddRange(c.ChildNodes);
                    else flat.Add(c);
                }
                children = flat.ToArray();
            }

            var lines = new List<string>();
            int lastEmittedLine = -1;

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null) continue;

                if (!wantComments && child.NodeType == "Comment")
                    continue;

                // Custom Function Spacing at Top Level
                if (isTopLevel && lines.Count > 0 && (child.NodeType == "Method" || child.NodeType == "Class"))
                {
                    for (int s = 0; s < _functionSpacing; s++)
                    {
                        lines.Add("");
                    }
                }
                else if (wantBlanks && lastEmittedLine > 0 && child.Line > 0)
                {
                    int gap = child.Line - lastEmittedLine - 1;
                    if (gap > 1) gap = 1;
                    if (gap > 0) lines.Add("");
                }

                string emitted = FormatNode(child, indent);
                if (emitted == null) continue;

                if (indent > 0 && emitted.Length > 0)
                {
                    string expectedPad = MakePad(indent);
                    if (!emitted.StartsWith(expectedPad))
                        emitted = expectedPad + emitted;
                }

                // Track end line
                lastEmittedLine = GetMaxLine(child);

                // Inline comments: on the statement's first or last line (as in AstEmitter.EmitChildren)
                int stmtLast = lastEmittedLine;
                while (wantComments && i + 1 < children.Length
                    && children[i + 1] != null
                    && children[i + 1].NodeType == "Comment"
                    && children[i + 1].Line > 0
                    && (children[i + 1].Line == child.Line || children[i + 1].Line == stmtLast))
                {
                    i++;
                    emitted += "  " + children[i].Value;
                    int commentLine = children[i].Line;
                    if (commentLine > lastEmittedLine)
                        lastEmittedLine = commentLine;
                }

                lines.Add(emitted);
            }

            return string.Join("\n", lines);
        }

        private int GetMaxLine(AstNode node)
        {
            if (node == null) return 0;
            int max = node.EndLine > 0 ? node.EndLine : node.Line;
            foreach (var child in node.ChildNodes)
            {
                if (child == null) continue;
                int childMax = GetMaxLine(child);
                if (childMax > max) max = childMax;
            }
            return max;
        }
    }

    // =========================================================================
    // 3. MINIFIER PLUGIN
    // =========================================================================

    /// <summary>
    /// Minify presets, by what they promise:
    /// Safe — the output behaves exactly like the input for every script (layout, folding, literal inlining, local
    ///   renaming; functions using dynamic `%name%` access keep their locals).
    /// Aggressive — also renames globals, functions and classes and joins statements onto one line. Names the script
    ///   can reach at runtime (dynamic names, names written as strings, class names seen via Type()/__Class) are
    ///   detected and kept, so ordinary scripts stay correct; heavy runtime reflection may still need Exclude.
    /// Extreme — also renames properties and methods (and matching strings) and tries risky renames. Smallest
    ///   output, but not compatible with every script (e.g. `obj.%"Pre_" name%`, COM/JSON property names,
    ///   Gui event handlers given by name). Check the result; use Exclude Properties for the names that must stay.
    /// </summary>
    public enum MinifyLevel
    {
        Safe,
        Aggressive,
        Extreme,
        Custom
    }

    public class MinifyConfig
    {
        private MinifyLevel _level = MinifyLevel.Safe;
        private bool _inline, _escape, _fold, _renameLocals, _renameGlobals, _renameFunctions, _renameClasses;
        private bool _renameProperties, _renamePropStrings, _risky, _oneLine;

        [Category("Minify"), DisplayName("Level"), Description("Safe: always behaves the same. Aggressive: also renames globals/functions/classes and one-lines (runtime-reachable names are detected and kept). Extreme: also renames properties/methods; smallest, not compatible with every script. Changing an option below switches to Custom.")]
        [RefreshProperties(RefreshProperties.All)]
        public MinifyLevel Level
        {
            get { return _level; }
            set { _level = value; ApplyLevelDefaults(); }
        }

        [Category("Optimization"), DisplayName("Inline Single-Use Variables"), Description("If true, variables defined once and read exactly once will be replaced directly at their usage site.")]
        public bool InlineSingleUseVariables { get { return _inline; } set { Set(ref _inline, value); } }

        [Category("Optimization"), DisplayName("Escape Multiline Strings"), Description("If true, multiline continuation sections will be escaped into single-line strings with backtick escapes.")]
        public bool EscapeMultilineStrings { get { return _escape; } set { Set(ref _escape, value); } }

        [Category("Optimization"), DisplayName("Fold Constant Expressions"), Description("If true, performs compile-time evaluation of constant operations.")]
        public bool FoldConstants { get { return _fold; } set { Set(ref _fold, value); } }

        [Category("Renaming"), DisplayName("Rename Local Variables"), Description("Rename local variables inside functions to short names.")]
        public bool RenameLocalVariables { get { return _renameLocals; } set { Set(ref _renameLocals, value); } }

        [Category("Renaming"), DisplayName("Rename Global Variables"), Description("Rename global variables to short names.")]
        public bool RenameGlobalVariables { get { return _renameGlobals; } set { Set(ref _renameGlobals, value); } }

        [Category("Renaming"), DisplayName("Rename Functions"), Description("Rename user-defined functions to short names.")]
        public bool RenameFunctions { get { return _renameFunctions; } set { Set(ref _renameFunctions, value); } }

        [Category("Renaming"), DisplayName("Rename Classes"), Description("Rename user-defined classes to short names.")]
        public bool RenameClasses { get { return _renameClasses; } set { Set(ref _renameClasses, value); } }

        [Category("Renaming"), DisplayName("Rename Properties"), Description("Rename class properties and methods to short names (Extreme: not compatible with every script).")]
        public bool RenameProperties { get { return _renameProperties; } set { Set(ref _renameProperties, value); } }

        [Category("Renaming"), DisplayName("Rename Property String Literals"), Description("If true, renames string literals that match renamed properties/methods.")]
        public bool RenamePropertyStringLiterals { get { return _renamePropStrings; } set { Set(ref _renamePropStrings, value); } }

        [Category("Renaming"), DisplayName("Exclude Properties"), Description("Comma-separated list of properties, methods, or variables to exclude from renaming.")]
        public string ExcludeProperties { get; set; }

        [Category("Renaming"), DisplayName("Try Risky Renames"), Description("If true, renames even where the script may reach names at runtime (dynamic derefs, names in strings, Type()/__Class).")]
        public bool TryRiskyRenames { get { return _risky; } set { Set(ref _risky, value); } }

        [Category("Optimization"), DisplayName("Aggressive 1-Lining"), Description("If true, merges consecutive expression statements onto a single line separated by commas.")]
        public bool AggressiveOneLining { get { return _oneLine; } set { Set(ref _oneLine, value); } }

        public MinifyConfig()
        {
            ExcludeProperties = "";
            ApplyLevelDefaults();
        }

        // A flag that departs from the level's preset makes the configuration Custom.
        private void Set(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            _level = MinifyLevel.Custom;
        }

        private void ApplyLevelDefaults()
        {
            if (_level == MinifyLevel.Custom) return;
            bool aggressive = _level == MinifyLevel.Aggressive || _level == MinifyLevel.Extreme;
            bool extreme = _level == MinifyLevel.Extreme;
            _inline = true;
            _escape = true;
            _fold = true;
            _renameLocals = true;
            _renameGlobals = aggressive;
            _renameFunctions = aggressive;
            _renameClasses = aggressive;
            _oneLine = aggressive;
            _renameProperties = extreme;
            _renamePropStrings = extreme;
            _risky = extreme;
        }
    }
    public class MinifyPlugin : IFlowPlugin
    {
        public string Name { get { return "Formatting.Minify"; } }
        public string Target { get; set; }

        public string Category { get { return "Formatting"; } }
        public string StepTitle { get { return "Minify"; } }
        public string Icon { get { return "🤐"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(MinifyConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (MinifyConfig)config; }

        public MinifyConfig Config { get; set; }

        private int _renamedLocalsCount = 0;
        private int _renamedGlobalsCount = 0;
        private int _renamedFunctionsCount = 0;
        private int _renamedClassesCount = 0;
        private int _renamedPropertiesCount = 0;

        internal static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "if", "else", "loop", "while", "for", "in", "switch", "case", "default", "try", "catch", "finally", 
            "throw", "break", "continue", "return", "global", "local", "static", "class", "extends", "new", 
            "as", "until", "unset", "and", "or", "not", "is", "true", "false", "this", "super",
            
            
            "Prototype", "Base", "Name", "Length", "Count", "DefineProp", "DeleteProp", "GetOwnPropDesc", 
            "HasOwnProp", "OwnProps", "__New", "__Call", "__Get", "__Set", "__Delete", "__Enum", "__Item", "Call",
            "bind", "call", "has", "get", "set", "delete", "clear", "clone", "push", "pop", "insertat", "removeat",
            
            "Submit", "Destroy", "Show", "Minimize", "Maximize", "Restore", "Flash", "Hide", "Opt", "Add", "AddGB",
            "Title", "Hwnd", "MarginX", "MarginY", "BackColor", "Control", "FocusedCtrl", "Choose", "Focus", "GetPos",
            "Move", "SetFont", "UseTab", "ClassNN", "Enabled", "Focused", "Text", "Value", "Visible", "OnEvent",
            "Modify", "Insert", "SetFormat", "GetCount", "GetText", "Rename", "Check", "Uncheck", "ToggleCheck", "Redraw", "OnNotify", "OnCommand",
            "Default", "SetIcon", "SetColor", "wpfHwnd", "RestoreDocumentDlls", "RestoreAvalonEditDlls",
            "CopyRequiredDlls", "RunProcess", "CheckForCrashes", "Document", "Selection",
            
            // Expanded built-ins for Files, Buffers, InputHooks, arrays, menus, etc.
            "Read", "Write", "ReadLine", "WriteLine", "ReadNum", "WriteNum", "RawRead", "RawWrite", "Seek", "Pos", "Close", "AtEOF", "Encoding", "Handle",
            "Ptr", "Size",
            "Start", "Stop", "Wait", "Input", "EndReason", "EndKey", "EndMods", "BackspaceIsUndo", "CaseSensitive", "MinSendLevel", "NotifyNonText", "OnChar", "OnEnd", "OnKeyDown", "OnKeyUp", "Timeout", "VisibleText", "VisibleNonText",
            "Enable", "Disable", "Capacity", "CaseSense",
 
            // AHK GUI Control Types
            "TreeView", "ListView", "Checkbox", "Edit", "GroupBox", "StatusBar", "DDL", "DropDownList", "ComboBox", "ListBox", "Button", "Radio", "Link", "DateTime", "MonthCal", "Slider", "Progress", "Tab", "Tab2", "Tab3", "ActiveX", "Custom",
 
            // AHK GUI Events
            "Change", "Click", "DoubleClick", "ContextMenu", "Close", "Escape", "Size", "DropFiles", "Focus", "LoseFocus", "ItemSelect", "ItemFocus", "ItemCheck", "ItemExpand", "DragDrop",
 
            // AHK General Display / Field strings
            "Id", "Location", "Class(NN)", "Process", "PID",
 
            // UIA Standard Property names
            "Type", "LocalizedType", "Name", "Value", "AutomationId", "BoundingRectangle", "ClassName", "FullDescription", "HelpText", "AccessKey", "AcceleratorKey", "HasKeyboardFocus", "IsKeyboardFocusable", "ItemType", "ProcessId", "IsEnabled", "IsPassword", "IsOffscreen", "FrameworkId", "IsRequiredForForm", "ItemStatus", "RuntimeId", "NativeWindowHandle", "ProviderDescription", "IsDataValidForForm", "ControllerFor", "DescribedBy", "FlowsTo", "LandmarkType", "LocalizedLandmarkType", "LiveSetting", "OptimizeForVisualPeformance", "IsPeripheral", "PositionInSet", "SizeOfSet", "Level", "AnnotationTypes", "AnnotationObjects", "HeadingLevel", "IsDialog",
 
            // UIA Pattern Available properties
            "IsInvokePatternAvailable", "IsTogglePatternAvailable", "IsExpandCollapsePatternAvailable", "IsSelectionItemPatternAvailable", "IsLegacyIAccessiblePatternAvailable", "IsValuePatternAvailable", "IsRangeValuePatternAvailable", "IsScrollPatternAvailable", "IsScrollItemPatternAvailable", "IsSelectionPatternAvailable", "IsGridPatternAvailable", "IsGridItemPatternAvailable", "IsMultipleViewPatternAvailable", "IsWindowPatternAvailable", "IsTextPatternAvailable", "IsTextChildPatternAvailable", "IsTextEditPatternAvailable", "IsTextRangePatternAvailable", "IsCustomNavigationPatternAvailable", "IsDragPatternAvailable", "IsDropTargetPatternAvailable", "IsObjectModelPatternAvailable", "IsSpreadsheetPatternAvailable", "IsSpreadsheetItemPatternAvailable", "IsStylesPatternAvailable", "IsTransformPatternAvailable", "IsTransformPattern2Available", "IsVirtualizedItemPatternAvailable",
 
            // UIA Pattern Names
            "InvokePattern", "TogglePattern", "ExpandCollapsePattern", "SelectionItemPattern", "LegacyIAccessiblePattern", "ValuePattern", "RangeValuePattern", "ScrollPattern", "ScrollItemPattern", "SelectionPattern", "GridPattern", "GridItemPattern", "MultipleViewPattern", "WindowPattern", "TextPattern", "TextChildPattern", "TextEditPattern", "CustomNavigationPattern", "DragPattern", "DropTargetPattern", "ObjectModelPattern", "SpreadsheetPattern", "SpreadsheetItemPattern", "StylesPattern", "TransformPattern", "TransformPattern2", "VirtualizedItemPattern",
 
            // AHK-XAML framework built-ins and reflection keywords
            "StartInProcess", "Load", "InvokeMember", "InvokeMember_3", "Update", "Query", "Track", "Export", "Compile", "Bind", "DefineTemplate", "SetDefaults", "AddTab", "SetBottomBar", "ExportBAML", "ExportBundle", "LoadBAML", "Prewarm", "RunProcessWait", "ShowErrorDialog", "GetBundleDllName", "BindBaseEvents", "OnUIReady", "ThemeChanged", "ScaleChanged", "RadiusChanged", "OnSidebarClick", "OnInputFocus", "OnInputBlur", "HandleSpinnerButton", "InitKeyboardHooks", "SetupTemplates", "BuildSidebar", "BuildWindowControls", "CopyToClipboard", "ExportLog", "Gui_Size",
            
            // UIA Library API surface and methods
            "ElementFromHandle", "ElementFromPoint", "GetFocusedElement", "CreateCacheRequest", "WaitElement", "CompareElements", "FilterList", "ControlExist", "CreateOrCondition", "CreateAndCondition", "CreateNotCondition", "CreatePropertyCondition", "CreateTrueCondition", "CreateFalseCondition", "CreateTreeWalker", "ElementFromPath", "Dump", "DumpAll", "Walk", "RecurseTreeView", "ConstructTreeView", "PopulatePropsPatterns", "SafeCompareElements", "WaitElementNotExist", "ElementExist", "ElementFromPathExist", "CachedElementExist", "CachedElementFromPathExist", "CachedElementFromPath", "GetClickablePoint", "ShowContextMenu", "GetParentElement", "GetFirstChildElement", "GetLastChildElement", "GetNextSiblingElement", "GetPreviousSiblingElement", "GetChildren", "GetChildrenAsNativeArray", "FindAll", "FindFirst", "FindAllWithOptions", "FindFirstWithOptions", "WaitElementFromPath", "TreeViewSelectCapturedElement", "RefreshTVWins",

            // AHK Control and TreeView/ListView options
            "Expand", "Collapse", "Select", "Bold", "Vis", "Icon", "Sort", "ReadOnly", "Wrap", "Password", "Number", "Multi"
        };

        // Built-in functions / variables / classes: the shared table generated from the AutoHotkey docs (AhkBuiltins).
        static MinifyPlugin() { foreach (var name in AhkBuiltins.AllNames()) ReservedNames.Add(name); }

        public MinifyPlugin()
        {
            Config = new MinifyConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return "";

            _renamedLocalsCount = 0;
            _renamedGlobalsCount = 0;
            _renamedFunctionsCount = 0;
            _renamedClassesCount = 0;
            _renamedPropertiesCount = 0;

            // 1. AST optimizations
            if (Config.FoldConstants)
            {
                var shakerConfig = new TreeShakerConfig { FoldConstantExpressions = true };
                var follower = new LogicFollowerEngine(shakerConfig);
                using (Prof.Time("minify.fold")) follower.FoldAndFlatten(root); // folding only: no reachability analysis needed
            }

            if (Config.InlineSingleUseVariables)
            {
                using (Prof.Time("minify.inline")) InlineSingleUseVariablesInAst(root);
                if (Config.FoldConstants)
                {
                    var shakerConfig = new TreeShakerConfig { FoldConstantExpressions = true };
                    var follower = new LogicFollowerEngine(shakerConfig);
                    using (Prof.Time("minify.fold")) follower.FoldAndFlatten(root); // folding only: no reachability analysis needed
                }
            }

            using (Prof.Time("minify.rename")) RenameSymbolsInAst(root);

            if (Config.EscapeMultilineStrings)
            {
                EscapeMultilineStringsInAst(root);
            }

            PipelineLogger.Log("  🤐 Formatting.Minify Summary:");
            if (_renamedClassesCount > 0)
                PipelineLogger.Log("    - Renamed {0} classes.", _renamedClassesCount);
            if (_renamedFunctionsCount > 0)
                PipelineLogger.Log("    - Renamed {0} functions.", _renamedFunctionsCount);
            if (_renamedGlobalsCount > 0)
                PipelineLogger.Log("    - Renamed {0} global variables.", _renamedGlobalsCount);
            if (_renamedLocalsCount > 0)
                PipelineLogger.Log("    - Renamed {0} local variables.", _renamedLocalsCount);
            if (_renamedPropertiesCount > 0)
                PipelineLogger.Log("    - Renamed {0} class properties/methods.", _renamedPropertiesCount);

            // 2. Minified whitespace stripped emission
            var emitter = new MinifiedEmitter(Config);
            using (Prof.Time("minify.emit")) return emitter.Emit(root);
        }

        private void CollectAllIdentifiersInAst(AstNode node, HashSet<string> identifiers)
        {
            if (node == null) return;
            if (!string.IsNullOrEmpty(node.Value))
            {
                string val = node.Value;
                if (val.StartsWith("%") && val.EndsWith("%") && val.Length > 2)
                {
                    identifiers.Add(val.Substring(1, val.Length - 2));
                }
                else
                {
                    identifiers.Add(val);
                }
            }
            if (node.NodeType == "Catch" && !string.IsNullOrEmpty(node.Metadata))
            {
                identifiers.Add(node.Metadata);
            }
            foreach (var child in node.ChildNodes)
            {
                CollectAllIdentifiersInAst(child, identifiers);
            }
        }

        /// <summary>
        /// Renames what the `%expr%` derefs of a dynamic name read. Each expression is parsed and renamed as code —
        /// names through <paramref name="vars"/> (minus <paramref name="skipVars"/>, shadowed names), member names
        /// through <paramref name="props"/> — then written back; the literal name text around the derefs is kept.
        /// (Renaming by text also hit words after a dot and inside strings: `%this.valRef%` came out as `%b%`.)
        /// </summary>
        private string RenameDerefText(string text, Dictionary<string, string> vars, Dictionary<string, string> props, HashSet<string> skipVars)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('%') < 0) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, "%([^%]*)%", m =>
            {
                var expr = AhkParser.ParseExpressionText(m.Groups[1].Value);
                if (expr == null) return m.Value;
                return RenameInExpression(expr, vars, props, skipVars) ? "%" + AstEmitter.Emit(expr).Trim() + "%" : m.Value;
            });
        }

        private bool RenameInExpression(AstNode n, Dictionary<string, string> vars, Dictionary<string, string> props, HashSet<string> skipVars)
        {
            if (n == null) return false;
            bool changed = false;
            string v = n.Value;
            if (n.NodeType == "Identifier" && v != null)
            {
                string nv;
                if (v.IndexOf('%') >= 0) nv = RenameDerefText(v, vars, props, skipVars);
                else nv = vars != null && !(skipVars != null && skipVars.Contains(v)) && vars.ContainsKey(v) ? vars[v] : v;
                if (nv != v) { n.Value = nv; changed = true; }
            }
            else if (n.NodeType == "Member" && v != null)
            {
                string nv;
                if (v.IndexOf('%') >= 0) nv = RenameDerefText(v, vars, props, skipVars);
                else nv = props != null && props.ContainsKey(v) ? props[v] : v;
                if (nv != v) { n.Value = nv; changed = true; }
            }
            foreach (var c in n.ChildNodes)
                if (RenameInExpression(c, vars, props, skipVars)) changed = true;
            return changed;
        }
        private string GetRenamedIdentifier(string val, Dictionary<string, string> renameMap)
        {
            if (string.IsNullOrEmpty(val)) return val;
            // Dynamic name `pre%a%mid%b%`: rename what is read inside each %...%, keep the literal text as-is.
            int firstPct = val.IndexOf('%');
            if (firstPct >= 0 && !(firstPct == 0 && val.Length > 2 && val.IndexOf('%', 1) == val.Length - 1))
            {
                return PercentDeref.Replace(val, m => GetRenamedIdentifier(m.Value, renameMap));
            }
            if (val.StartsWith("%") && val.EndsWith("%") && val.Length > 2)
            {
                string inner = val.Substring(1, val.Length - 2);
                bool changed = false;
                foreach (var kv in renameMap)
                {
                    string key = kv.Key;
                    string replacement = kv.Value;
                    string pattern = @"\b" + System.Text.RegularExpressions.Regex.Escape(key) + @"\b";
                    string newInner = System.Text.RegularExpressions.Regex.Replace(inner, pattern, replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (newInner != inner)
                    {
                        inner = newInner;
                        changed = true;
                    }
                }
                if (changed)
                {
                    return "%" + inner + "%";
                }
            }
            else
            {
                if (renameMap.ContainsKey(val))
                {
                    return renameMap[val];
                }
            }
            return val;
        }

        private bool InheritsFromCSModule(string className, LogicFollowerEngine analyzer)
        {
            // Walk the base chain; `class Error extends Error` (a nested class named after its global base) would
            // otherwise loop forever, so each name is visited once.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(className) && seen.Add(className))
            {
                if (className.Equals("_CSModule", StringComparison.OrdinalIgnoreCase) ||
                    className.Equals("CSModule", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                LogicFollowerEngine.ClassInfo clInfo;
                if (!analyzer.Classes.TryGetValue(className, out clInfo)) return false;
                className = clInfo.BaseClass;
            }
            return false;
        }

        static readonly System.Text.RegularExpressions.Regex PlainName = new System.Text.RegularExpressions.Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        static readonly System.Text.RegularExpressions.Regex PercentDeref = new System.Text.RegularExpressions.Regex("%[^%]*%");

        /// <summary>The identifier-shaped string literals of the script (`"Counter"`, `'OnClick'`).</summary>
        private static void CollectIdentifierStrings(AstNode node, HashSet<string> names)
        {
            if (node == null) return;
            if (node.NodeType == "String" && node.Value != null && node.Value.Length >= 3)
            {
                string s = AhkStringHelper.UnescapeAhkString(node.Value);
                if (PlainName.IsMatch(s)) names.Add(s);
            }
            foreach (var c in node.ChildNodes)
                CollectIdentifierStrings(c, names);
        }

        /// <summary>
        /// A definition directly in the program, an included file, or under a hotkey / hotstring label
        /// (`~ScrollLock::` followed by `Toggle(*) { ... }` defines the global function Toggle) is global.
        /// </summary>
        private static bool IsGlobalScopeParent(AstNode parent)
        {
            // Up through statement blocks (`if x { F() { ... } }` still defines a global F) until the program, or
            // until a function / class / hotkey body, whose definitions are local to it.
            AstNode child = null;
            while (parent != null)
            {
                string t = parent.NodeType;
                if (t == "Program" || t == "Include") return true;
                if (t == "Method" || t == "Class" || t == "Property" || t == "FatArrow" || t == "FatArrowBody") return false;
                if ((t == "Hotkey" || t == "Hotstring") && child != null && child.NodeType == "Block") return false;
                child = parent;
                parent = parent.Parent;
            }
            return false;
        }
        private static bool ObservesClassNames(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "Member" && string.Equals(node.Value, "__Class", StringComparison.OrdinalIgnoreCase)) return true;
            if (node.NodeType == "Call" && node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Identifier"
                && string.Equals(node.GetChild(0).Value, "Type", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var c in node.ChildNodes)
                if (ObservesClassNames(c)) return true;
            return false;
        }

        private void RenameSymbolsInAst(AstNode root)
        {
            var analyzer = new LogicFollowerEngine(new TreeShakerConfig { Profile = TreeShakingProfile.Off });
            using (Prof.Time("rename.analyze")) analyzer.Analyze(root);

            var preservedGlobals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in ReservedNames) preservedGlobals.Add(r);
            
            // Proactively collect all original identifiers to prevent collisions
            using (Prof.Time("rename.identifiers")) CollectAllIdentifiersInAst(root, preservedGlobals);

            // Add user exclusions to preservedGlobals
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Config.ExcludeProperties))
            {
                var parts = Config.ExcludeProperties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    string trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        preservedGlobals.Add(trimmed);
                        excluded.Add(trimmed);
                    }
                }
            }
            
            // Dynamic names (`CALG_%x%`, `v%A_Index%`) can reach any variable whose name starts with their literal
            // prefix, so those names must keep their spelling; one starting with `%` could reach anything.
            var dynamicPrefixes = new List<string>();
            bool unboundedDynamic = false;
            CollectDynamicNamePrefixes(root, dynamicPrefixes, ref unboundedDynamic);
            if (dynamicPrefixes.Count > 0 || unboundedDynamic)
            {
                foreach (var name in analyzer.Globals.Keys.Concat(analyzer.Functions.Keys).Concat(analyzer.Classes.Keys))
                {
                    if (unboundedDynamic || dynamicPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        excluded.Add(name);
                        preservedGlobals.Add(name);
                    }
                }
            }

            // A global, function or class whose name the script also spells as a string may be reached through it at
            // runtime (`%"Counter"%`, `%prefix name%`, `HasMethod(x, "Run")`, a name read from an ini...). Unless risky
            // renames are on, those keep their names.
            if (!Config.TryRiskyRenames)
            {
                var spelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectIdentifierStrings(root, spelled);
                foreach (var name in analyzer.Globals.Keys.Concat(analyzer.Functions.Keys).Concat(analyzer.Classes.Keys))
                {
                    if (spelled.Contains(name))
                    {
                        excluded.Add(name);
                        preservedGlobals.Add(name);
                    }
                }
            }

            var csModuleClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Config.TryRiskyRenames)
            {
                foreach (var kv in analyzer.Classes)
                {
                    if (InheritsFromCSModule(kv.Key, analyzer))
                    {
                        csModuleClasses.Add(kv.Key);
                        preservedGlobals.Add(kv.Key);
                    }
                }
            }

            // `Type(obj)` and `obj.__Class` hand a class's name to the script at runtime, so renaming classes would
            // change what it sees (and compares against).
            if (!Config.RenameClasses || (!Config.TryRiskyRenames && ObservesClassNames(root)))
            {
                foreach (var k in analyzer.Classes.Keys) { preservedGlobals.Add(k); excluded.Add(k); }
            }
            if (!Config.RenameFunctions)
            {
                foreach (var k in analyzer.Functions.Keys) preservedGlobals.Add(k);
            }
            if (!Config.RenameGlobalVariables)
            {
                foreach (var k in analyzer.Globals.Keys) preservedGlobals.Add(k);
            }

            var globalRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int globalIndex = 0;

            if (Config.RenameClasses)
            {
                foreach (var k in analyzer.Classes.Keys)
                {
                    if (ReservedNames.Contains(k) || excluded.Contains(k) || csModuleClasses.Contains(k)) continue;
                    // A nested class (`Sock.Error`) is a member of its outer class, not a global: renaming it as a global
                    // would also rename every other use of that name (here the built-in Error).
                    var clsNode = analyzer.Classes[k].Node;
                    if (clsNode != null && clsNode.Parent != null && clsNode.Parent.NodeType != "Program" && clsNode.Parent.NodeType != "Include") continue;
                    
                    // Protect wrapper/runtime-checked classes from renaming
                    if (k.StartsWith("IUIAutomation", StringComparison.OrdinalIgnoreCase) ||
                        k.Contains("Condition") ||
                        k.Contains("Array") ||
                        k.Contains("IAccessible") ||
                        string.Equals(k, "ComVar", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k, "Variant", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    string newName = GetNextGlobalName(ref globalIndex, preservedGlobals);
                    globalRenames[k] = newName;
                    _renamedClassesCount++;
                }
            }
            if (Config.RenameFunctions)
            {
                foreach (var k in analyzer.Functions.Keys)
                {
                    if (ReservedNames.Contains(k) || globalRenames.ContainsKey(k) || excluded.Contains(k)) continue;
                    string newName = GetNextGlobalName(ref globalIndex, preservedGlobals);
                    globalRenames[k] = newName;
                    _renamedFunctionsCount++;
                }
            }
            if (Config.RenameGlobalVariables)
            {
                foreach (var k in analyzer.Globals.Keys)
                {
                    if (ReservedNames.Contains(k) || globalRenames.ContainsKey(k) || excluded.Contains(k)) continue;
                    string newName = GetNextGlobalName(ref globalIndex, preservedGlobals);
                    globalRenames[k] = newName;
                    _renamedGlobalsCount++;
                }
            }

            foreach (var val in globalRenames.Values)
            {
                preservedGlobals.Add(val);
            }

            var propertyRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int propIndex = 0;
            if (Config.RenameProperties)
            {
                var userProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cl in analyzer.Classes.Values)
                {
                    if (csModuleClasses.Contains(cl.Name)) continue;

                    foreach (var p in cl.Properties.Keys)
                    {
                        if (!ReservedNames.Contains(p) && !excluded.Contains(p)) userProps.Add(p);
                    }
                    foreach (var m in cl.Methods.Keys)
                    {
                        if (!ReservedNames.Contains(m) && !excluded.Contains(m)) userProps.Add(m);
                    }
                    foreach (var f in cl.StaticFields)
                    {
                        if (!string.IsNullOrEmpty(f.Value) && !ReservedNames.Contains(f.Value) && !excluded.Contains(f.Value))
                            userProps.Add(f.Value);
                    }
                }

                foreach (var p in userProps)
                {
                    if (analyzer.Classes.ContainsKey(p))
                    {
                        if (globalRenames.ContainsKey(p))
                        {
                            propertyRenames[p] = globalRenames[p];
                        }
                        continue;
                    }

                    // Properties also avoid clashing with preservedGlobals
                    string newName = GetNextGlobalName(ref propIndex, preservedGlobals);
                    propertyRenames[p] = newName;
                    _renamedPropertiesCount++;
                }
            }

            using (Prof.Time("rename.apply-global")) ApplyGlobalAndPropertyRenames(root, globalRenames, propertyRenames, csModuleClasses, analyzer);

            if (Config.RenameLocalVariables)
            {
                var methods = new List<AstNode>();
                CollectMethods(root, methods);
                foreach (var method in methods)
                {
                    if (!IsNestedMethod(method))
                    {
                        using (Prof.Time("rename.locals")) RenameLocalVariablesInMethod(method, analyzer, globalRenames);
                    }
                }
            }
        }

        private void CollectMethods(AstNode node, List<AstNode> list)
        {
            if (node == null) return;
            if (node.NodeType == "Method")
            {
                list.Add(node);
            }
            foreach (var child in node.ChildNodes)
            {
                CollectMethods(child, list);
            }
        }

        private string GetMangledName(int index)
        {
            string letters = "abcdefghijklmnopqrstuvwxyz";
            StringBuilder sb = new StringBuilder();
            int temp = index;
            while (temp >= 0)
            {
                sb.Insert(0, letters[temp % 26]);
                temp = (temp / 26) - 1;
            }
            return sb.ToString();
        }

        private string GetNextGlobalName(ref int index, HashSet<string> preserved)
        {
            while (true)
            {
                string name = GetMangledName(index++);
                if (!preserved.Contains(name)) return name;
            }
        }

        private string GetNextLocalName(ref int index, HashSet<string> preserved)
        {
            while (true)
            {
                string name = GetMangledName(index++);
                if (!preserved.Contains(name)) return name;
            }
        }

        private bool IsNestedMethod(AstNode node)
        {
            var p = node.Parent;
            while (p != null)
            {
                if (p.NodeType == "Class") return false;
                if (p.NodeType == "Method") return true;
                p = p.Parent;
            }
            return false;
        }

        private static readonly HashSet<string> ProtectedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Int", "UInt", "Int64", "UInt64", "Short", "UShort", "Char", "UChar", "Double", "Float", "Ptr", "UPtr", "Str", "AStr", "WStr", "HResult"
        };

        private static bool IsProtectedType(string val)
        {
            if (string.IsNullOrEmpty(val)) return false;
            string clean = val.TrimEnd('*', 'p', 'P');
            return ProtectedTypes.Contains(clean);
        }

        private bool IsComOrDllCallProtectedArgument(AstNode node)
        {
            if (node == null || node.NodeType != "String") return false;
            var parent = node.Parent;
            if (parent != null && parent.NodeType == "Arguments")
            {
                var callNode = parent.Parent;
                if (callNode != null && callNode.NodeType == "Call" && callNode.ChildCount > 0)
                {
                    var callee = callNode.GetChild(0);
                    if (callee != null && callee.NodeType == "Identifier")
                    {
                        string funcName = callee.Value;
                        if (string.Equals(funcName, "DllCall", StringComparison.OrdinalIgnoreCase))
                        {
                            if (parent.ChildCount > 0 && parent.GetChild(0) == node) return true;
                            for (int i = 1; i < parent.ChildCount; i += 2)
                            {
                                if (parent.GetChild(i) == node) return true;
                            }
                        }
                        else if (string.Equals(funcName, "ComCall", StringComparison.OrdinalIgnoreCase))
                        {
                            for (int i = 2; i < parent.ChildCount; i += 2)
                            {
                                if (parent.GetChild(i) == node) return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private void ApplyGlobalAndPropertyRenames(AstNode node, Dictionary<string, string> globalRenames, Dictionary<string, string> propertyRenames, HashSet<string> csModuleClasses, LogicFollowerEngine analyzer, HashSet<string> shadowedNames = null, string currentClass = null)
        {
            if (node == null) return;

            string nextClass = currentClass;
            HashSet<string> currentShadowed = shadowedNames;

            bool isDirectClassChild = (node.Parent != null && node.Parent.NodeType == "Class");

            if (node.NodeType == "KeyValue")
            {
                var keyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (keyNode != null)
                {
                    if (keyNode.NodeType == "Identifier" || keyNode.NodeType == "Member")
                    {
                        keyNode.Value = GetRenamedIdentifier(keyNode.Value, propertyRenames);
                    }
                    else if (keyNode.NodeType == "String")
                    {
                        string val = keyNode.Value;
                        if (val != null && val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                        {
                            string quote = val.Substring(0, 1);
                            string inner = val.Substring(1, val.Length - 2);
                            if (propertyRenames.ContainsKey(inner) && !IsProtectedType(inner))
                            {
                                keyNode.Value = quote + propertyRenames[inner] + quote;
                            }
                        }
                    }
                }
                var valNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (valNode != null)
                {
                    ApplyGlobalAndPropertyRenames(valNode, globalRenames, propertyRenames, csModuleClasses, analyzer, currentShadowed, nextClass);
                }
                return;
            }

            if (node.NodeType == "Class")
            {
                nextClass = node.Value;
                node.Value = GetRenamedIdentifier(node.Value, globalRenames);
                var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
                if (extendsNode != null && !string.IsNullOrEmpty(extendsNode.Value))
                {
                    if (extendsNode.Value.Contains("."))
                    {
                        var parts = extendsNode.Value.Split('.');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            parts[i] = GetRenamedIdentifier(parts[i], globalRenames);
                        }
                        extendsNode.Value = string.Join(".", parts);
                    }
                    else
                    {
                        extendsNode.Value = GetRenamedIdentifier(extendsNode.Value, globalRenames);
                    }
                }
            }
            else if (node.NodeType == "Method")
            {
                currentShadowed = shadowedNames != null ? new HashSet<string>(shadowedNames, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var localVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var declaredGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool isAssumeGlobal = IsAssumeGlobal(node, node);
                CollectAllLocals(node, localVars, declaredGlobal, node, isAssumeGlobal);
                localVars.ExceptWith(declaredGlobal);
                foreach (var v in localVars)
                {
                    currentShadowed.Add(v);
                }

                if (isDirectClassChild)
                {
                    node.Value = GetRenamedIdentifier(node.Value, propertyRenames);
                }
                else
                {
                    bool isTopLevel = IsGlobalScopeParent(node.Parent);
                    if (isTopLevel)
                    {
                        node.Value = GetRenamedIdentifier(node.Value, globalRenames);
                    }
                }
            }
            else if (node.NodeType == "Property")
            {
                node.Value = GetRenamedIdentifier(node.Value, propertyRenames);
            }
            else if (node.NodeType == "StaticAssign" || node.NodeType == "Declaration")
            {
                // Chained items (`static types := Map(), types.CaseSense := false`) are class members too.
                bool chainedClassItem = node.Parent != null && node.Parent.NodeType == "StaticAssign"
                    && node.Parent.Parent != null && node.Parent.Parent.NodeType == "Class";
                if (isDirectClassChild || chainedClassItem)
                {
                    // Dotted item `types.CaseSense`: every segment is a property name (built-ins stay reserved).
                    if (node.Value != null && node.Value.IndexOf('.') > 0)
                        node.Value = string.Join(".", node.Value.Split('.').Select(seg => GetRenamedIdentifier(seg, propertyRenames)));
                    else
                        node.Value = GetRenamedIdentifier(node.Value, propertyRenames);
                }
                else
                {
                    bool isTopLevel = IsGlobalScopeParent(node.Parent);
                    if (isTopLevel || (node.NodeType == "Declaration" && node.Metadata == "global"))
                    {
                        node.Value = GetRenamedIdentifier(node.Value, globalRenames);
                    }
                }
            }
            else if (node.NodeType == "Identifier")
            {
                if (node.Value != null && node.Value.IndexOf('%') >= 0)
                {
                    node.Value = RenameDerefText(node.Value, globalRenames, propertyRenames, currentShadowed);
                }
                else if (currentShadowed == null || !currentShadowed.Contains(node.Value))
                {
                    node.Value = GetRenamedIdentifier(node.Value, globalRenames);
                }
            }
            else if (node.NodeType == "Member")
            {
                var target = node.ChildCount > 0 ? node.GetChild(0) : null;
                bool skipRename = false;
                if (target != null)
                {
                    if (target.NodeType == "Identifier" && csModuleClasses.Contains(target.Value))
                    {
                        skipRename = true;
                    }
                    else if (target.NodeType == "This" && !string.IsNullOrEmpty(nextClass) && csModuleClasses.Contains(nextClass))
                    {
                        skipRename = true;
                    }
                }

                if (skipRename)
                {
                    // Do not rename
                }
                else
                {
                    if (node.Value != null && node.Value.IndexOf('%') >= 0)
                    {
                        // `obj.%expr%` / `obj.On%expr%`: the derefs are code (variables and members they read).
                        node.Value = RenameDerefText(node.Value, globalRenames, propertyRenames, currentShadowed);
                    }
                    else if (node.Value != null && globalRenames.ContainsKey(node.Value) && analyzer.Classes.ContainsKey(node.Value))
                    {
                        node.Value = globalRenames[node.Value];
                    }
                    else
                    {
                        node.Value = GetRenamedIdentifier(node.Value, propertyRenames);
                    }
                }
            }
            else if (node.NodeType == "String")
            {
                if (Config.RenamePropertyStringLiterals && !IsComOrDllCallProtectedArgument(node))
                {
                    string val = node.Value;
                    if (val != null && val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                    {
                        string quote = val.Substring(0, 1);
                        string inner = val.Substring(1, val.Length - 2);
                        if (inner.Length >= 3 && !IsProtectedType(inner))
                        {
                            if (globalRenames.ContainsKey(inner))
                            {
                                node.Value = quote + globalRenames[inner] + quote;
                            }
                            else if (propertyRenames.ContainsKey(inner))
                            {
                                node.Value = quote + propertyRenames[inner] + quote;
                            }
                        }
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                ApplyGlobalAndPropertyRenames(child, globalRenames, propertyRenames, csModuleClasses, analyzer, currentShadowed, nextClass);
            }
        }

        private bool IsAssumeGlobal(AstNode node, AstNode rootNode)
        {
            if (node == null) return false;
            if (node.NodeType == "Method" && node != rootNode) return false;
            if (node.NodeType == "Class") return false;

            if (node.NodeType == "Declaration" && node.Metadata == "global" && string.IsNullOrEmpty(node.Value))
            {
                return true;
            }
            foreach (var child in node.ChildNodes)
            {
                if (IsAssumeGlobal(child, rootNode)) return true;
            }
            return false;
        }

        private void RenameLocalVariablesInMethod(AstNode methodNode, LogicFollowerEngine analyzer, Dictionary<string, string> globalRenames)
        {
            if (!Config.TryRiskyRenames && HasDynamicDeref(methodNode)) return;

            var localVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var declaredGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool isAssumeGlobal = IsAssumeGlobal(methodNode, methodNode);
            CollectAllLocals(methodNode, localVars, declaredGlobal, methodNode, isAssumeGlobal);

            localVars.ExceptWith(declaredGlobal);
            localVars.ExceptWith(analyzer.Classes.Keys);
            localVars.ExceptWith(analyzer.Functions.Keys);
            localVars.ExceptWith(analyzer.Globals.Keys);
            localVars.ExceptWith(ReservedNames);

            if (localVars.Count == 0) return;

            var referencedExternals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectReferencedIdentifiers(methodNode, referencedExternals);

            referencedExternals.UnionWith(globalRenames.Values);
            referencedExternals.UnionWith(analyzer.Classes.Keys);
            referencedExternals.UnionWith(analyzer.Functions.Keys);
            referencedExternals.UnionWith(analyzer.Globals.Keys);
            referencedExternals.UnionWith(ReservedNames);

            var localMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int localIndex = 0;
            foreach (var v in localVars)
            {
                string newName = GetNextLocalName(ref localIndex, referencedExternals);
                localMap[v] = newName;
                _renamedLocalsCount++;
            }

            ApplyAllLocalRenames(methodNode, localMap, methodNode);
        }

        private void CollectAllLocals(AstNode node, HashSet<string> localVars, HashSet<string> declaredGlobal, AstNode rootNode, bool isAssumeGlobal)
        {
            if (node == null) return;

            if (node.NodeType == "Class")
            {
                return; // Do NOT traverse into nested classes
            }

            if ((node.NodeType == "Method" || node.NodeType == "FatArrow") && node != rootNode)
            {
                if (!string.IsNullOrEmpty(node.Value))
                {
                    localVars.Add(node.Value);
                }
                return; // Do NOT traverse into nested methods or lambdas
            }

            if (node.NodeType == "Declaration")
            {
                if (node.Metadata == "global")
                {
                    if (!string.IsNullOrEmpty(node.Value)) declaredGlobal.Add(node.Value);
                }
                else
                {
                    if (!string.IsNullOrEmpty(node.Value)) localVars.Add(node.Value);
                }
            }
            else if (node.NodeType == "StaticAssign")
            {
                if (!string.IsNullOrEmpty(node.Value)) localVars.Add(node.Value);
            }
            else if (!isAssumeGlobal)
            {
                if (node.NodeType == "ColonAssign" || node.NodeType == "Assign" || 
                         (node.NodeType == "BinaryExpr" && node.Value != null && node.Value.EndsWith("=") && 
                          node.Value != "==" && node.Value != "!=" && node.Value != ">=" && node.Value != "<=" && 
                          node.Value != "=" && node.Value != "~="))
                {
                    var lhs = node.ChildCount > 0 ? node.GetChild(0) : null;
                    if (lhs != null && lhs.NodeType == "Identifier")
                    {
                        if (!string.IsNullOrEmpty(lhs.Value))
                        {
                            string val = lhs.Value;
                            if (val.StartsWith("%") && val.EndsWith("%") && val.Length > 2)
                                localVars.Add(val.Substring(1, val.Length - 2));
                            else
                                localVars.Add(val);
                        }
                    }
                }
                else if (node.NodeType == "Increment" || node.NodeType == "Decrement" ||
                         (node.NodeType == "PostfixExpr" && (node.Value == "++" || node.Value == "--")) ||
                         (node.NodeType == "UnaryExpr" && (node.Value == "++" || node.Value == "--")))
                {
                    var target = node.ChildCount > 0 ? node.GetChild(0) : null;
                    if (target != null && target.NodeType == "Identifier" && !string.IsNullOrEmpty(target.Value))
                    {
                        string val = target.Value;
                        if (val.StartsWith("%") && val.EndsWith("%") && val.Length > 2)
                            localVars.Add(val.Substring(1, val.Length - 2));
                        else
                            localVars.Add(target.Value);
                    }
                }
            }

            if (node.NodeType == "Parameter")
            {
                if (!string.IsNullOrEmpty(node.Value)) localVars.Add(node.Value);
            }
            else if (node.NodeType == "Catch")
            {
                if (!string.IsNullOrEmpty(node.Metadata)) localVars.Add(node.Metadata);
            }
            else if (node.NodeType == "ForVars")
            {
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "Identifier" && !string.IsNullOrEmpty(child.Value))
                    {
                        string val = child.Value;
                        if (val.StartsWith("%") && val.EndsWith("%") && val.Length > 2)
                            localVars.Add(val.Substring(1, val.Length - 2));
                        else
                            localVars.Add(child.Value);
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                CollectAllLocals(child, localVars, declaredGlobal, rootNode, isAssumeGlobal);
            }
        }

        private void CollectReferencedIdentifiers(AstNode node, HashSet<string> refs)
        {
            if (node == null) return;
            if (node.NodeType == "Class") return; // Do NOT traverse into nested classes
            if (node.NodeType == "Identifier")
            {
                if (!string.IsNullOrEmpty(node.Value))
                {
                    string val = node.Value;
                    if (val.StartsWith("%") && val.EndsWith("%") && val.Length > 2)
                        refs.Add(val.Substring(1, val.Length - 2));
                    else
                        refs.Add(val);
                }
            }
            // Names bound in nested functions / lambdas (`(&a?, &b?) => ...`) are taken too: a local renamed to one
            // of them would be captured by it instead of the outer variable.
            else if ((node.NodeType == "Parameter" || node.NodeType == "Declaration" || node.NodeType == "StaticAssign" || node.NodeType == "Method")
                     && !string.IsNullOrEmpty(node.Value))
            {
                refs.Add(node.Value);
            }
            else if (node.NodeType == "Catch" && !string.IsNullOrEmpty(node.Metadata))
            {
                refs.Add(node.Metadata);
            }
            foreach (var child in node.ChildNodes)
            {
                CollectReferencedIdentifiers(child, refs);
            }
        }

        private bool IsExplicitlyDeclaredInMethod(AstNode methodNode, string name)
        {
            if (methodNode == null || string.IsNullOrEmpty(name)) return false;

            var paramNode = methodNode.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
            if (paramNode != null)
            {
                foreach (var p in paramNode.ChildNodes)
                {
                    if (p != null && string.Equals(p.Value, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            foreach (var child in methodNode.ChildNodes)
            {
                if (child != null && child.NodeType == "Parameter" && string.Equals(child.Value, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return HasExplicitLocalDeclaration(methodNode, name);
        }

        private bool HasExplicitLocalDeclaration(AstNode node, string name)
        {
            if (node == null) return false;
            if (node.NodeType == "Class" || node.NodeType == "Method" || node.NodeType == "FatArrow")
            {
                if (node.NodeType != "Method" && node.NodeType != "FatArrow") return false;
            }

            if (node.NodeType == "Declaration")
            {
                if (node.Metadata == "local" || string.IsNullOrEmpty(node.Metadata))
                {
                    if (string.Equals(node.Value, name, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            else if (node.NodeType == "StaticAssign")
            {
                if (string.Equals(node.Value, name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            foreach (var child in node.ChildNodes)
            {
                if (child != null && child.NodeType != "Method" && child.NodeType != "FatArrow" && child.NodeType != "Class")
                {
                    if (HasExplicitLocalDeclaration(child, name)) return true;
                }
            }
            return false;
        }

        private void ApplyAllLocalRenames(AstNode node, Dictionary<string, string> localMap, AstNode rootNode)
        {
            if (node == null) return;

            if (node.NodeType == "Class")
            {
                return; // Do NOT traverse into nested classes
            }

            if (node.NodeType == "KeyValue")
            {
                // Key child is a property key, so do NOT rename it with localMap!
                // Only process the value child.
                var valNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (valNode != null)
                {
                    ApplyAllLocalRenames(valNode, localMap, rootNode);
                }
                return;
            }

            if ((node.NodeType == "Method" || node.NodeType == "FatArrow") && node != rootNode)
            {
                // Collect all local variables/parameters of this inner method/lambda to shadow them
                var innerLocals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var innerGlobals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool innerAssumeGlobal = IsAssumeGlobal(node, node);
                CollectAllLocals(node, innerLocals, innerGlobals, node, innerAssumeGlobal);

                // Shadow these variables so they are not renamed by the outer map
                var nextLocalMap = new Dictionary<string, string>(localMap, StringComparer.OrdinalIgnoreCase);
                foreach (var loc in innerLocals)
                {
                    bool isExplicitShadow = IsExplicitlyDeclaredInMethod(node, loc);
                    if (isExplicitShadow || !localMap.ContainsKey(loc))
                    {
                        nextLocalMap.Remove(loc);
                    }
                }

                // Rename the method name itself if it's in localMap
                if (!string.IsNullOrEmpty(node.Value))
                {
                    node.Value = GetRenamedIdentifier(node.Value, localMap);
                }

                // Now traverse children of the nested method/lambda with the updated map
                foreach (var child in node.ChildNodes)
                {
                    ApplyAllLocalRenames(child, nextLocalMap, rootNode);
                }
                return;
            }

            if (node.NodeType == "Identifier")
            {
                node.Value = node.Value != null && node.Value.IndexOf('%') >= 0
                    ? RenameDerefText(node.Value, localMap, null, null)
                    : GetRenamedIdentifier(node.Value, localMap);
            }
            else if (node.NodeType == "Member")
            {
                if (node.Value != null && node.Value.IndexOf('%') >= 0) // `obj.%expr%`, `obj.On%expr%`
                {
                    node.Value = RenameDerefText(node.Value, localMap, null, null);
                }
            }
            else if (node.NodeType == "Declaration" || node.NodeType == "StaticAssign" || node.NodeType == "Parameter")
            {
                node.Value = GetRenamedIdentifier(node.Value, localMap);
            }
            else if (node.NodeType == "Catch")
            {
                if (!string.IsNullOrEmpty(node.Metadata))
                {
                    node.Metadata = GetRenamedIdentifier(node.Metadata, localMap);
                }
            }

            foreach (var child in node.ChildNodes)
            {
                ApplyAllLocalRenames(child, localMap, rootNode);
            }
        }

        private void EscapeMultilineStringsInAst(AstNode node)
        {
            if (node == null) return;
            if (node.NodeType == "String" && node.Value != null)
            {
                string val = node.Value;
                if (val.Contains("\n") || val.Contains("\r"))
                {
                    node.Value = AhkStringHelper.NormalizeAhkString(val, "\"");
                }
            }

            foreach (var child in node.ChildNodes)
            {
                EscapeMultilineStringsInAst(child);
            }
        }

        private void InlineSingleUseVariablesInAst(AstNode root)
        {
            // Count assignments and reads of all identifiers
            var assignmentCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var readCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lastAssignmentNode = new Dictionary<string, AstNode>(StringComparer.OrdinalIgnoreCase);
            var lastReadNode = new Dictionary<string, AstNode>(StringComparer.OrdinalIgnoreCase);
            var mutatedVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AnalyzeVariableUsage(root, assignmentCount, readCount, lastAssignmentNode, lastReadNode, mutatedVars, isWrite: false);

            // Variables reachable through a dynamic name (`v%i%`, `%name%`) have reads/writes we can't count.
            var dynPrefixes = new List<string>();
            bool unboundedDynamic = false;
            CollectDynamicNamePrefixes(root, dynPrefixes, ref unboundedDynamic);
            if (unboundedDynamic || HasPlainVariableDeref(root)) return;
            foreach (var name in assignmentCount.Keys)
                if (dynPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) mutatedVars.Add(name);

            // Identify variables that are assigned once and read once
            var singleUseVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in assignmentCount)
            {
                string varName = kv.Key;
                if (kv.Value == 1 && readCount.ContainsKey(varName) && readCount[varName] == 1 && !mutatedVars.Contains(varName))
                {
                    // Verify the assignment is to a simple literal node
                    var assignNode = lastAssignmentNode[varName];
                    if (assignNode != null)
                    {
                        var rhs = assignNode.NodeType == "Declaration"
                            ? (assignNode.ChildCount > 0 ? assignNode.GetChild(0) : null)
                            : (assignNode.ChildCount > 1 ? assignNode.GetChild(1) : null);
                        // Only literals: a variable's value can change between the assignment and the read
                        // (`tmp := p, p := q, q := tmp`; A_Index, A_TickCount). And the read must be in the same
                        // function as the assignment (or the assignment global), since the analysis is name-based.
                        var readNode = lastReadNode.ContainsKey(varName) ? lastReadNode[varName] : null;
                        var assignFn = EnclosingFunction(assignNode);
                        bool sameScope = assignFn == null || (readNode != null && EnclosingFunction(readNode) == assignFn);
                        if (rhs != null && sameScope && (rhs.NodeType == "String" || rhs.NodeType == "Number" || rhs.NodeType == "Literal"))
                        {
                            singleUseVars.Add(varName);
                        }
                    }
                }
            }

            if (singleUseVars.Count > 0)
            {
                ReplaceAndPruneVariables(root, singleUseVars, lastAssignmentNode);
            }
        }

        private bool IsSimpleIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char first = s[0];
            if (!char.IsLetter(first) && first != '_') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }

        /// <summary>True if the tree reads a variable by computed name: `%x%` as a value (not a member name).</summary>
        private static bool HasPlainVariableDeref(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "Identifier" && !string.IsNullOrEmpty(node.Value) && node.Value.Length > 2
                && node.Value[0] == '%' && node.Value.IndexOf('%', 1) == node.Value.Length - 1)
                return true;
            foreach (var c in node.ChildNodes) if (HasPlainVariableDeref(c)) return true;
            return false;
        }

        /// <summary>Literal prefixes of dynamic names (`pre%x%`) in the tree; `unbounded` if one starts with `%` (`%a%b`).</summary>
        private static void CollectDynamicNamePrefixes(AstNode node, List<string> prefixes, ref bool unbounded)
        {
            if (node == null) return;
            if (node.NodeType == "Identifier" && !string.IsNullOrEmpty(node.Value))
            {
                string v = node.Value;
                int p = v.IndexOf('%');
                bool pureDeref = p == 0 && v.Length > 2 && v.IndexOf('%', 1) == v.Length - 1;
                if (p > 0) { if (!prefixes.Contains(v.Substring(0, p))) prefixes.Add(v.Substring(0, p)); }
                else if (p == 0 && !pureDeref) unbounded = true;
            }
            foreach (var c in node.ChildNodes) CollectDynamicNamePrefixes(c, prefixes, ref unbounded);
        }

        private bool HasDynamicDeref(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "Identifier" && !string.IsNullOrEmpty(node.Value) && node.Value.IndexOf('%') >= 0
                && !(node.Value[0] == '%' && node.Value.Length > 2 && node.Value.IndexOf('%', 1) == node.Value.Length - 1))
                return true; // `pre%x%` / `%a%b`: locals may be read or written dynamically
            if (node.NodeType != "Member" && !string.IsNullOrEmpty(node.Value) &&
                node.Value.StartsWith("%") && node.Value.EndsWith("%") && node.Value.Length > 2)
            {
                string inner = node.Value.Substring(1, node.Value.Length - 2);
                if (IsSimpleIdentifier(inner))
                {
                    return true;
                }
            }
            foreach (var child in node.ChildNodes)
            {
                if (HasDynamicDeref(child)) return true;
            }
            return false;
        }

        private void AnalyzeVariableUsage(AstNode node, Dictionary<string, int> assignmentCount, Dictionary<string, int> readCount, 
            Dictionary<string, AstNode> lastAssignmentNode, Dictionary<string, AstNode> lastReadNode, HashSet<string> mutatedVars, bool isWrite)
        {
            if (node == null) return;

            if (node.NodeType == "Method")
            {
                if (HasDynamicDeref(node))
                {
                    var localVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var declaredGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    CollectAllLocals(node, localVars, declaredGlobal, node, isAssumeGlobal: false);
                    foreach (var v in localVars)
                    {
                        mutatedVars.Add(v);
                    }
                }
            }

            if (node.NodeType == "ColonAssign" || node.NodeType == "Assign" || (node.NodeType == "BinaryExpr" && (node.Value == "=" || node.Value == ":=")))
            {
                var lhs = node.ChildCount > 0 ? node.GetChild(0) : null;
                var rhs = node.ChildCount > 1 ? node.GetChild(1) : null;

                if (lhs != null && lhs.NodeType == "Identifier")
                {
                    string varName = lhs.Value;
                    assignmentCount[varName] = assignmentCount.ContainsKey(varName) ? assignmentCount[varName] + 1 : 1;
                    lastAssignmentNode[varName] = node;
                    // Only a plain statement in a statement list is a candidate: an assignment inside an expression
                    // (`if (x && q := 42)`, `&(p := 0)`) may not run, or runs mid-expression.
                    if (!IsListStatement(node)) mutatedVars.Add(varName);
                }
                else if (lhs != null)
                {
                    AnalyzeVariableUsage(lhs, assignmentCount, readCount, lastAssignmentNode, lastReadNode, mutatedVars, isWrite: false);
                }

                AnalyzeVariableUsage(rhs, assignmentCount, readCount, lastAssignmentNode, lastReadNode, mutatedVars, isWrite: false);
                return;
            }

            if (node.NodeType == "Declaration" || node.NodeType == "StaticAssign")
            {
                string varName = node.Value;
                if (!string.IsNullOrEmpty(varName))
                {
                    assignmentCount[varName] = assignmentCount.ContainsKey(varName) ? assignmentCount[varName] + 1 : 1;
                    lastAssignmentNode[varName] = node;
                    // Class variables are object members (read as this.x), and chained / nested declarations can't
                    // be removed on their own: never inline those.
                    bool classMember = node.Parent != null && (node.Parent.NodeType == "Class" || node.Parent.NodeType == "StaticAssign" || node.Parent.NodeType == "Declaration");
                    if (classMember || !IsListStatement(node) || node.ChildCount > 1) mutatedVars.Add(varName);
                }

                for (int ci = 0; ci < node.ChildCount; ci++)
                {
                    AnalyzeVariableUsage(node.GetChild(ci), assignmentCount, readCount, lastAssignmentNode, lastReadNode, mutatedVars, isWrite: false);
                }
                return;
            }

            if ((node.NodeType == "UnaryExpr" && (node.Value == "++" || node.Value == "--" || node.Value == "&")) ||
                (node.NodeType == "PostfixExpr" && (node.Value == "++" || node.Value == "--")))
            {
                // The target may be wrapped: `&(p := 0)` passes p itself to be written by the callee.
                var target = node.ChildCount > 0 ? node.GetChild(0) : null;
                while (target != null)
                {
                    if (target.NodeType == "Grouped" && target.ChildCount > 0) target = target.GetChild(0);
                    else if (target.NodeType == "BinaryExpr" && TokenLikeAssignment(target.Value) && target.ChildCount > 0) target = target.GetChild(0);
                    else break;
                }
                if (target != null && target.NodeType == "Identifier")
                {
                    mutatedVars.Add(target.Value);
                }
            }

            if (node.NodeType == "BinaryExpr" && (node.Value == "+=" || node.Value == "-=" || node.Value == "*=" || 
                node.Value == "/=" || node.Value == ".=" || node.Value == "&=" || node.Value == "|=" || 
                node.Value == "^=" || node.Value == "??=" || node.Value == "//=" || node.Value == ">>=" || node.Value == "<<="))
            {
                var lhs = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (lhs != null && lhs.NodeType == "Identifier")
                {
                    mutatedVars.Add(lhs.Value);
                }
            }

            if (node.NodeType == "ForVars")
            {
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "Identifier")
                    {
                        mutatedVars.Add(child.Value);
                    }
                }
            }

            if (node.NodeType == "Parameter" && node.Metadata != null && node.Metadata.Contains("byref"))
            {
                if (!string.IsNullOrEmpty(node.Value))
                {
                    mutatedVars.Add(node.Value);
                }
            }

            if (node.NodeType == "Identifier")
            {
                string varName = node.Value;
                if (!isWrite && !string.IsNullOrEmpty(varName))
                {
                    readCount[varName] = readCount.ContainsKey(varName) ? readCount[varName] + 1 : 1;
                    lastReadNode[varName] = node;
                }
                return;
            }

            if (node.NodeType == "Member")
            {
                var obj = node.ChildCount > 0 ? node.GetChild(0) : null;
                AnalyzeVariableUsage(obj, assignmentCount, readCount, lastAssignmentNode, lastReadNode, mutatedVars, isWrite: false);
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                AnalyzeVariableUsage(child, assignmentCount, readCount, lastAssignmentNode, lastReadNode, mutatedVars, isWrite);
            }
        }

        static bool TokenLikeAssignment(string op)
        {
            return op == ":=" || op == "+=" || op == "-=" || op == "*=" || op == "/=" || op == "//=" || op == ".=" || op == "|="
                || op == "&=" || op == "^=" || op == "<<=" || op == ">>=" || op == ">>>=" || op == "??=";
        }

        /// <summary>True for a statement directly inside a statement list (Program/Block/Include/case body, or a
        /// comma sequence that is itself such a statement): the only assignments that can be removed safely.</summary>
        static bool IsListStatement(AstNode n)
        {
            var p = n.Parent;
            while (p != null && p.NodeType == "MultiStatement") { n = p; p = p.Parent; }
            if (p == null) return true;
            return p.NodeType == "Program" || p.NodeType == "Block" || p.NodeType == "Include"
                || p.NodeType == "CaseBody" || p.NodeType == "DefaultBody";
        }

        /// <summary>The function (Method / fat-arrow) a node belongs to, or null at global level.</summary>
        static AstNode EnclosingFunction(AstNode n)
        {
            for (var p = n == null ? null : n.Parent; p != null; p = p.Parent)
                if (p.NodeType == "Method" || p.NodeType == "FatArrow") return p;
            return null;
        }

        private void ReplaceAndPruneVariables(AstNode node, HashSet<string> singleUseVars, Dictionary<string, AstNode> lastAssignmentNode)
        {
            if (node == null) return;

            for (int i = node.ChildCount - 1; i >= 0; i--)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                if (singleUseVars.Contains(child.Value) && (child.NodeType == "Declaration" || child.NodeType == "StaticAssign"))
                {
                    node.RemoveChild(i);
                    continue;
                }
                if (child.NodeType == "ColonAssign" || child.NodeType == "Assign" || (child.NodeType == "BinaryExpr" && child.Value == ":="))
                {
                    var lhs = child.ChildCount > 0 ? child.GetChild(0) : null;
                    if (lhs != null && lhs.NodeType == "Identifier" && singleUseVars.Contains(lhs.Value))
                    {
                        node.RemoveChild(i);
                        continue;
                    }
                }

                if (child.NodeType == "Identifier" && singleUseVars.Contains(child.Value))
                {
                    var assignNode = lastAssignmentNode[child.Value];
                    var rhs = assignNode.NodeType == "Declaration"
                        ? (assignNode.ChildCount > 0 ? assignNode.GetChild(0) : null)
                        : (assignNode.ChildCount > 1 ? assignNode.GetChild(1) : null);
                    if (rhs != null)
                    {
                        node.ReplaceChild(i, rhs.Clone());
                    }
                }

                ReplaceAndPruneVariables(child, singleUseVars, lastAssignmentNode);
            }
        }
    }

    internal class MinifiedEmitter
    {
        private MinifyConfig Config;

        public MinifiedEmitter(MinifyConfig config)
        {
            Config = config;
        }

        public MinifiedEmitter()
        {
            Config = new MinifyConfig();
        }
        private string SafeEmitChild(AstNode node, int index)
        {
            if (node == null || index < 0 || index >= node.ChildCount) return "";
            var child = node.GetChild(index);
            return child != null ? Emit(child) : "";
        }

        // Leading operands of statements that AHK would otherwise read as a function-call statement (see StatementHead).
        private HashSet<AstNode> _heads;

        public string Emit(AstNode node)
        {
            if (node == null) return "";
            bool outermost = _heads == null;
            if (outermost) _heads = StatementHead.Collect(node);
            try
            {
                string s = EmitCore(node);
                return _heads.Contains(node) ? "(" + s + ")" : s;
            }
            finally { if (outermost) _heads = null; }
        }

        private string EmitCore(AstNode node)
        {
            switch (node.NodeType)
            {
                case "Program":
                    return EmitChildren(node.ChildNodes);

                case "Directive":
                    if (AstEmitter.IsParsedHotIf(node)) return "#HotIf " + Emit(node.GetChild(0));
                    return node.Value;

                case "Class":
                    {
                        string ext = "";
                        var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
                        if (extendsNode != null) ext = " extends " + extendsNode.Value;
                        var bodyChildren = node.ChildNodes.Where(c => c != null && c.NodeType != "Extends").ToArray();
                        string body = EmitChildren(bodyChildren);
                        return "class " + node.Value + ext + " {\n" + body + "\n}";
                    }

                case "Method":
                    {
                        string stat = node.Metadata == "static" ? "static " : "";
                        string paramStr = "";
                        string body = "";
                        if (node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Parameters")
                        {
                            var paramNode = node.GetChild(0);
                            bool isAccessor = (node.Value == "get" || node.Value == "set");
                            if (isAccessor && paramNode.ChildCount == 0)
                            {
                                paramStr = "";
                            }
                            else
                            {
                                paramStr = Emit(paramNode);
                            }
                            body = node.ChildCount > 1 ? SafeEmitChild(node, 1) : "{\n}";
                        }
                        else
                        {
                            paramStr = "";
                            body = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "{\n}";
                        }
                        if (string.IsNullOrEmpty(body) || body.Trim() == "") body = "{\n}";
                        string bsep = (body.StartsWith("{") && (string.IsNullOrEmpty(paramStr) || !paramStr.EndsWith(")"))) ? " " : "";
                        return stat + node.Value + paramStr + bsep + body;
                    }

                case "Parameters":
                    return "(" + string.Join(",", node.ChildNodes.Select(c => Emit(c))) + ")";

                case "Block":
                    if (node.ChildCount == 0 || node.ChildNodes.All(c => c == null || c.NodeType == "Omitted" || c.NodeType == "Comment")) return "{\n}";
                    return "{\n" + EmitChildren(node.ChildNodes) + "\n}";

                case "If":
                    {
                        string cond = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "true";
                        string body = node.ChildCount > 1 ? SafeEmitChild(node, 1) : "";
                        if (string.IsNullOrEmpty(body) || body.Trim() == "") body = "{\n}";
                        string elseStr = "";
                        if (node.ChildCount > 2 && node.GetChild(2) != null && node.GetChild(2).NodeType == "Else")
                        {
                            var elseNode = node.GetChild(2);
                            string ebody = elseNode.ChildCount > 0 ? SafeEmitChild(elseNode, 0) : "";
                            if (string.IsNullOrEmpty(ebody) || ebody.Trim() == "") ebody = "{\n}";
                            string esep = (!string.IsNullOrEmpty(ebody) && !ebody.StartsWith("{")) ? "\n" : " ";
                            elseStr = "\nelse" + esep + ebody;
                        }
                        string sep = (!string.IsNullOrEmpty(body) && !body.StartsWith("{")) ? "\n" : " ";
                        return "if(" + cond + ")" + sep + body + elseStr;
                    }

                case "While":
                    {
                        string wcond = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "true";
                        string wbody = node.ChildCount > 1 ? SafeEmitChild(node, 1) : "";
                        if (string.IsNullOrEmpty(wbody) || wbody.Trim() == "") wbody = "{\n}";
                        string wsep = (!string.IsNullOrEmpty(wbody) && !wbody.StartsWith("{")) ? "\n" : " ";
                        return "while(" + wcond + ")" + wsep + wbody + LoopTail(node, 2);
                    }

                case "Return":
                    return "return" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0) : "");

                case "BinaryExpr":
                    {
                        string op = node.Value;
                        // Avoid stripping spaces if operator is word-based (like "and", "or", "is", "contains") or dot concatenation, or arithmetic plus/minus.
                        bool spaceNeeded = char.IsLetter(op[0]) || op == "." || op == "+" || op == "-";
                        string space = spaceNeeded ? " " : "";
                        return SafeEmitChild(node, 0) + space + op + space + SafeEmitChild(node, 1);
                    }

                case "UnaryExpr":
                    {
                        string op = node.Value;
                        bool wordOp = char.IsLetter(op[0]);
                        string space = wordOp ? " " : "";
                        return op + space + SafeEmitChild(node, 0);
                    }

                case "PostfixExpr":
                    return SafeEmitChild(node, 0) + node.Value;

                case "UnsetModifier":
                    return SafeEmitChild(node, 0) + "?";

                case "Call":
                    return SafeEmitChild(node, 0) + SafeEmitChild(node, 1);

                case "Arguments":
                    {
                        // Function-call statements stay paren-less (shorter); inside a one-line comma chain they
                        // need parens, or the call would swallow the following statements as arguments.
                        if (node.Metadata == "command" && !_parenCommandCalls)
                        {
                            if (node.ChildCount == 0) return "";
                            var csb = new StringBuilder(" ");
                            var cargs = node.ChildNodes;
                            for (int i = 0; i < cargs.Length; i++)
                            {
                                if (i > 0) csb.Append(",");
                                if (cargs[i] != null && cargs[i].NodeType != "Omitted") csb.Append(Emit(cargs[i]));
                            }
                            return csb.ToString();
                        }
                        return "(" + string.Join(",", node.ChildNodes.Select(c => Emit(c))) + ")";
                    }

                case "Member":
                    return SafeEmitChild(node, 0) + "." + node.Value;

                case "Index":
                    {
                        var indices = new List<string>();
                        for (int i = 1; i < node.ChildCount; i++)
                        {
                            indices.Add(SafeEmitChild(node, i));
                        }
                        return SafeEmitChild(node, 0) + "[" + string.Join(",", indices) + "]";
                    }

                case "Number":
                case "Identifier":
                    return node.Value;

                case "String":
                    {
                        string val = node.Value;
                        if (string.IsNullOrEmpty(val) || val.Length < 2) return val;
                        string quote = val.StartsWith("'") ? "'" : "\"";
                        return AhkStringHelper.NormalizeAhkString(val, quote);
                    }

                case "This":
                    return "this";

                case "Array":
                    return "[" + string.Join(",", node.ChildNodes.Select(c => Emit(c))) + "]";

                case "Object":
                    {
                        var kvs = new List<string>();
                        foreach (var kv in node.ChildNodes)
                        {
                            if (kv == null || kv.NodeType == "Omitted") continue;
                            kvs.Add(SafeEmitChild(kv, 0) + ":" + SafeEmitChild(kv, 1));
                        }
                        return "{" + string.Join(",", kvs) + "}";
                    }

                case "FatArrow":
                    return SafeEmitChild(node, 0) + "=>" + SafeEmitChild(node, 1);

                case "Ternary":
                    return SafeEmitChild(node, 0) + "?" + SafeEmitChild(node, 1) + ":" + SafeEmitChild(node, 2);

                case "Grouped":
                    return "(" + SafeEmitChild(node, 0) + ")";

                case "Sequence":
                    return string.Join(",", node.ChildNodes.Select(c => Emit(c)));

                case "Concat":
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < node.ChildCount; i++)
                        {
                            var child = node.GetChild(i);
                            if (child == null) continue;
                            string emitted = Emit(child);
                            if (sb.Length > 0)
                            {
                                string sep = node.Metadata == "nospace" ? "" : " ";
                                sb.Append(sep);
                            }
                            sb.Append(emitted);
                        }
                        return sb.ToString();
                    }

                case "Include":
                    if (node.ChildCount > 0)
                    {
                        return EmitChildren(node.ChildNodes);
                    }
                    return "";

                case "For":
                    {
                        string fvars = node.ChildCount > 0 ? Emit(node.GetChild(0)) : "";
                        string fcoll = node.ChildCount > 1 ? Emit(node.GetChild(1)) : "";
                        string fbody = node.ChildCount > 2 ? Emit(node.GetChild(2)) : "";
                        if (string.IsNullOrEmpty(fbody) || fbody.Trim() == "") fbody = "{\n}";
                        string fsep = (!string.IsNullOrEmpty(fbody) && !fbody.StartsWith("{")) ? "\n" : " ";
                        return "for " + fvars + " in " + fcoll + fsep + fbody + LoopTail(node, 3);
                    }

                case "ForVars":
                    return string.Join(",", node.ChildNodes.Select(c => Emit(c)));

                case "Loop":
                    {
                        string variant = !string.IsNullOrEmpty(node.Value) ? " " + node.Value : "";
                        var nonUntilChildren = node.ChildNodes.Where(c => c != null && c.NodeType != "Until" && c.NodeType != "Else").ToList();
                        AstNode lbody = null;
                        var args = new List<AstNode>();
                        if (nonUntilChildren.Count > 0)
                        {
                            lbody = nonUntilChildren[nonUntilChildren.Count - 1];
                            for (int j = 0; j < nonUntilChildren.Count - 1; j++)
                            {
                                args.Add(nonUntilChildren[j]);
                            }
                        }
                        string argsStr = args.Count > 0 ? " " + string.Join(",", args.Select(c => Emit(c))) : "";
                        string result = "loop" + variant + argsStr;
                        if (lbody != null)
                        {
                            string bStr = Emit(lbody);
                            if (string.IsNullOrEmpty(bStr) || bStr.Trim() == "") bStr = "{\n}";
                            string sep = (!string.IsNullOrEmpty(bStr) && !bStr.StartsWith("{")) ? "\n" : " ";
                            result += sep + bStr;
                        }
                        else
                        {
                            result += " {\n}";
                        }
                        return result + LoopTail(node, 0);
                    }

                case "MultiStatement":
                    return string.Join(",", node.ChildNodes.Select(c => Emit(c)));

                case "Omitted":
                case "Comment":
                case "Warning":
                case "Error":
                    return "";

                case "Until":
                    return "until " + (node.ChildCount > 0 ? SafeEmitChild(node, 0) : "");

                case "Switch":
                    {
                        var sexpr = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType != "Case" && c.NodeType != "Default");
                        string scases = string.Join("\n", node.ChildNodes
                            .Where(c => c != null && (c.NodeType == "Case" || c.NodeType == "Default"))
                            .Select(c => Emit(c)));
                        string csFlag = !string.IsNullOrEmpty(node.Metadata) ? "," + node.Metadata : "";
                        return "switch" + (sexpr != null ? " " + Emit(sexpr) : "") + csFlag + "{\n" + scases + "\n}";
                    }

                case "Case":
                    {
                        var values = new List<string>();
                        for (int ci = 0; ci < node.ChildCount - 1; ci++)
                        {
                            string valEmitted = SafeEmitChild(node, ci);
                            if (!string.IsNullOrEmpty(valEmitted)) values.Add(valEmitted);
                        }
                        string valStr = string.Join(",", values);
                        string bodyStr = node.ChildCount > 0 ? SafeEmitChild(node, node.ChildCount - 1) : "";
                        string sep = (!string.IsNullOrEmpty(bodyStr) && !bodyStr.StartsWith("\n")) ? "\n" : "" /* a block can't share the case line */;
                        return "case " + valStr + ":" + sep + bodyStr;
                    }

                case "Try":
                    {
                        string tbody = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "";
                        if (string.IsNullOrEmpty(tbody) || tbody.Trim() == "") tbody = "{\n}";
                        string trest = string.Join("\n", node.ChildNodes.Skip(1).Select(c => Emit(c)));
                        string tsep = (!string.IsNullOrEmpty(tbody) && !tbody.StartsWith("{")) ? "\n" : " ";
                        return "try" + tsep + tbody + (string.IsNullOrEmpty(trest) ? "" : "\n" + trest);
                    }

                case "Catch":
                    {
                        string ctype = !string.IsNullOrEmpty(node.Value) ? " " + node.Value : "";
                        string cvar = !string.IsNullOrEmpty(node.Metadata) ? " as " + node.Metadata : "";
                        string cbody = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "";
                        if (string.IsNullOrEmpty(cbody) || cbody.Trim() == "") cbody = "{\n}";
                        string csep = (!string.IsNullOrEmpty(cbody) && !cbody.StartsWith("{")) ? "\n" : " ";
                        return "catch" + ctype + cvar + csep + cbody;
                    }

                case "Finally":
                    {
                        string fbody = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "";
                        if (string.IsNullOrEmpty(fbody) || fbody.Trim() == "") fbody = "{\n}";
                        string fsep = (!string.IsNullOrEmpty(fbody) && !fbody.StartsWith("{")) ? "\n" : " ";
                        return "finally" + fsep + fbody;
                    }

                case "Throw":
                    return "throw " + (node.ChildCount > 0 ? SafeEmitChild(node, 0) : "");

                case "Break":
                    return "break" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0) : "");

                case "Continue":
                    return "continue" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0) : "");

                case "Goto":
                    return "goto " + node.Value;

                case "New":
                    return "new " + (node.ChildCount > 0 ? SafeEmitChild(node, 0) : "");

                case "Remap":
                    return node.Value + "::" + (node.ChildCount > 0 && node.GetChild(0) != null ? node.GetChild(0).Value : "");

                case "Hotkey":
                    {
                        // Only an inline action or a block may share the hotkey's line; anything else (a stacked
                        // hotkey `~*Shift::` + `~*Ctrl::f()`, a body on the next line) keeps its own line.
                        var kbody = node.ChildCount > 0 ? node.GetChild(0) : null;
                        if (kbody == null) return node.Value + "::";
                        string ks = Emit(kbody);
                        bool sameLine = (kbody.NodeType == "Block" && !(node.Value ?? "").EndsWith(":")) || node.Metadata == "inline";
                        return node.Value + "::" + (sameLine ? "" : "\n") + ks;
                    }

                case "Hotstring":
                    {
                        // The action (an X-option expression, an inline statement or a block) is the child.
                        var hbody = node.ChildCount > 0 ? node.GetChild(0) : null;
                        if (hbody == null) return node.Value;
                        string hs = Emit(hbody);
                        bool inlineAction = (node.Metadata ?? "").StartsWith("inline");
                        // `::btw::` ⏎ `{`: on the same line `{` would be replacement text
                        if (hbody.NodeType == "Block") return node.Value + (inlineAction ? " " : "\n") + hs;
                        if (inlineAction) return node.Value + hs;
                        return node.Value + "\n" + hs;
                    }

                case "StaticAssign":
                    {
                        string sstat = node.Metadata == "static" ? "static " : "";
                        var sb = new StringBuilder();
                        sb.Append(sstat).Append(node.Value);
                        if (node.ChildCount > 0 && node.GetChild(0) != null)
                        {
                            sb.Append(":=").Append(Emit(node.GetChild(0)));
                        }
                        for (int ci = 1; ci < node.ChildCount; ci++)
                        {
                            var chainChild = node.GetChild(ci);
                            if (chainChild != null)
                            {
                                if (chainChild.NodeType == "StaticAssign")
                                {
                                    sb.Append(",").Append(chainChild.Value);
                                    if (chainChild.ChildCount > 0 && chainChild.GetChild(0) != null)
                                    {
                                        sb.Append(":=").Append(Emit(chainChild.GetChild(0)));
                                    }
                                }
                                else if (chainChild.NodeType == "Declaration")
                                {
                                    sb.Append(",").Append(chainChild.Value); // chained bare name: no repeated `static`
                                }
                                else
                                {
                                    sb.Append(",").Append(Emit(chainChild));
                                }
                            }
                        }
                        return sb.ToString();
                    }

                case "Declaration":
                    {
                        string dscope = !string.IsNullOrEmpty(node.Metadata) ? node.Metadata + " " : "";
                        var sb = new StringBuilder();
                        sb.Append(dscope).Append(node.Value);
                        int startChainedIdx = 0;
                        if (node.ChildCount > 0 && node.GetChild(0).NodeType != "Declaration")
                        {
                            var dinit = node.GetChild(0);
                            if (AstEmitter.IsCompoundDeclarationInit(node, dinit))
                                sb.Append(dinit.Value).Append(Emit(dinit.GetChild(1)));
                            else
                                sb.Append(":=").Append(Emit(dinit));
                            startChainedIdx = 1;
                        }
                        for (int idx = startChainedIdx; idx < node.ChildCount; idx++)
                        {
                            var child = node.GetChild(idx);
                            if (child != null && child.NodeType == "Declaration")
                            {
                                // chained item of the same statement: `global a,b:=1`
                                string item = Emit(child);
                                string scope = string.IsNullOrEmpty(child.Metadata) ? "" : child.Metadata + " ";
                                if (scope.Length > 0 && item.StartsWith(scope)) item = item.Substring(scope.Length);
                                sb.Append(",").Append(item);
                            }
                        }
                        return sb.ToString();
                    }

                case "Property":
                    {
                        string pstat = node.Metadata == "static" ? "static " : "";
                        if (node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Parameters")
                        {
                            string indexParams = "[" + string.Join(",", node.GetChild(0).ChildNodes.Select(p => Emit(p))) + "]";
                            if (node.ChildCount > 1 && node.GetChild(1) != null && node.GetChild(1).NodeType == "Block")
                            {
                                string pbody = Emit(node.GetChild(1));
                                string psep = pbody.StartsWith("{") ? " " : "";
                                return pstat + node.Value + indexParams + psep + pbody;
                            }
                            if (node.ChildCount > 1)
                                return pstat + node.Value + indexParams + "=>" + Emit(node.GetChild(1));
                            return pstat + node.Value + indexParams;
                        }
                        if (node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Block")
                        {
                            string pbody = Emit(node.GetChild(0));
                            string psep = pbody.StartsWith("{") ? " " : "";
                            return pstat + node.Value + psep + pbody;
                        }
                        if (node.ChildCount > 0)
                            return pstat + node.Value + "=>" + Emit(node.GetChild(0));
                        return pstat + node.Value;
                    }

                case "FatArrowBody":
                    return "=>" + (node.ChildCount > 0 ? SafeEmitChild(node, 0) : "");

                case "Else":
                    {
                        string ebody = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "";
                        if (string.IsNullOrEmpty(ebody) || ebody.Trim() == "") ebody = "{\n}";
                        string esep = (!string.IsNullOrEmpty(ebody) && !ebody.StartsWith("{")) ? "\n" : " ";
                        return "else" + esep + ebody;
                    }

                case "CaseBody":
                case "DefaultBody":
                    return EmitChildren(node.ChildNodes);

                case "Default":
                    {
                        string bodyStr = node.ChildCount > 0 ? SafeEmitChild(node, 0) : "";
                        string sep = (!string.IsNullOrEmpty(bodyStr) && !bodyStr.StartsWith("\n")) ? "\n" : "" /* a block can't share the case line */;
                        return "default:" + sep + bodyStr;
                    }

                case "KeyValue":
                    return (node.ChildCount > 0 ? SafeEmitChild(node, 0) : "") + ":"
                        + (node.ChildCount > 1 ? SafeEmitChild(node, 1) : "");

                case "Variadic":
                    return (node.ChildCount > 0 ? SafeEmitChild(node, 0) : "") + "*";

                case "Extends":
                    return "extends " + node.Value;

                case "Parameter":
                    {
                        if (node.Value == "*") return "*";
                        string meta = node.Metadata ?? "";
                        string pref = meta.Contains("byref") ? "&" : "";
                        string suff = meta.Contains("variadic") ? "*" : (meta.Contains("optional") ? "?" : "");
                        string pdef = node.ChildCount > 0 ? ":=" + SafeEmitChild(node, 0) : "";
                        return pref + node.Value + suff + pdef;
                    }

                case "Super":
                    return "super";

                case "Label":
                    return node.Value + ":";

                default:
                    return "";
            }
        }

        // A loop's `until` / `else` children (from index `from`), each on its own line.
        private string LoopTail(AstNode loop, int from)
        {
            var sb = new StringBuilder();
            for (int i = from; i < loop.ChildCount; i++)
            {
                var c = loop.GetChild(i);
                if (c != null && (c.NodeType == "Until" || c.NodeType == "Else")) sb.Append("\n").Append(Emit(c));
            }
            return sb.ToString();
        }

        private bool ContainsFatArrow(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "FatArrow") return true;
            foreach (var child in node.ChildNodes)
            {
                if (ContainsFatArrow(child)) return true;
            }
            return false;
        }

        private bool _parenCommandCalls;

        private bool IsCommaChainable(AstNode node)
        {
            if (node == null) return false;
            if (ContainsFatArrow(node)) return false;
            // `Goto(expr)` is a control-flow statement, not a function: it can't sit in a comma chain.
            if (node.NodeType == "Call" && node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Identifier"
                && string.Equals(node.GetChild(0).Value, "goto", StringComparison.OrdinalIgnoreCase))
                return false;
            switch (node.NodeType)
            {
                case "Call":
                case "UnaryExpr":
                case "PostfixExpr":
                case "Ternary":
                case "Grouped":
                case "Sequence":
                    return true;
                case "BinaryExpr":
                    {
                        string op = node.Value;
                        if (op == "+" || op == "-")
                        {
                            var left = node.GetChild(0);
                            if (left != null && left.NodeType == "Identifier" && MinifyPlugin.ReservedNames.Contains(left.Value))
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                case "MultiStatement":
                    return node.ChildCount > 0 && IsCommaChainable(node.GetChild(0));
                default:
                    return false;
            }
        }

        private string EmitChildren(AstNode[] children)
        {
            if (children == null || children.Length == 0) return "";
            // A bare block right after `f(x)` would make that call a function definition; blocks have no scope.
            for (int bi = 0; bi < children.Length; bi++)
            {
                // (a paren-less `f x` can come out as `f(x)` when one-lined, so any call on a name counts here)
                var prev = bi > 0 ? children[bi - 1] : null;
                bool afterCall = prev != null && prev.NodeType == "Call" && prev.ChildCount > 0 && prev.GetChild(0) != null && prev.GetChild(0).NodeType == "Identifier";
                if (children[bi] != null && children[bi].NodeType == "Block" && (afterCall || AstEmitter.FollowsFunctionHeaderLike(children, bi)))
                {
                    var flat = new List<AstNode>(children.Take(bi));
                    flat.AddRange(children[bi].ChildNodes);
                    flat.AddRange(children.Skip(bi + 1));
                    children = flat.ToArray();
                    bi--;
                }
            }
            var lines = new List<string>();
            var currentChain = new List<string>();
            foreach (var child in children)
            {
                if (child == null) continue;
                bool isChainable = IsCommaChainable(child);
                if (isChainable && Config != null && Config.AggressiveOneLining)
                {
                    _parenCommandCalls = true;
                    string emitted;
                    try { emitted = Emit(child); }
                    finally { _parenCommandCalls = false; }
                    if (!string.IsNullOrEmpty(emitted))
                    {
                        currentChain.Add(emitted);
                    }
                }
                else
                {
                    if (currentChain.Count > 0)
                    {
                        lines.Add(string.Join(",", currentChain));
                        currentChain.Clear();
                    }
                    string emitted = Emit(child);
                    if (!string.IsNullOrEmpty(emitted))
                    {
                        lines.Add(emitted);
                    }
                }
            }
            if (currentChain.Count > 0)
            {
                lines.Add(string.Join(",", currentChain));
            }
            return string.Join("\n", lines);
        }
    }

    // =========================================================================
    // 4. OPTIMISE PLUGIN
    // =========================================================================

    public enum BuildStateOptimize
    {
        None,
        AssumeCompiled,
        AssumeUncompiled
    }

    public class OptimiseConfig
    {
        [Category("Optimization"), DisplayName("Remove Duplicate Directives"), Description("Remove duplicate directives (e.g., #Requires AutoHotkey v2.0).")]
        public bool RemoveDuplicateDirectives { get; set; }

        [Category("Optimization"), DisplayName("Convert If-Else to Switch"), Description("Convert chains of if-else checks on the same variable to switch statements.")]
        public bool ConvertIfToSwitch { get; set; }

        [Category("Optimization"), DisplayName("Fold Arithmetic Constants"), Description("Evaluate and fold constant math expressions (e.g., 60 * 1000 * 1000).")]
        public bool FoldMathConstants { get; set; }

        [Category("Optimization"), DisplayName("Fold Logical Constants"), Description("Evaluate and fold logical constant expressions (e.g., true || x).")]
        public bool FoldLogicalConstants { get; set; }

        [Category("Optimization"), DisplayName("Prune Dead Branches"), Description("Prune dead branch blocks (e.g. if (false)).")]
        public bool PruneDeadBranches { get; set; }

        [Category("Optimization"), DisplayName("Fold String Concatenations"), Description("Evaluate and fold string concatenation expressions (e.g. \"a\" . \"b\").")]
        public bool FoldStringConcats { get; set; }

        [Category("Optimization"), DisplayName("Fold Consecutive String Assigns"), Description("Join consecutive string appending assignments to the same variable.")]
        public bool FoldConsecutiveStringAssigns { get; set; }

        [Category("Optimization"), DisplayName("Fold A_IsCompiled"), Description("Evaluate A_IsCompiled statically as True or False, removing dead conditional branches.")]
        public BuildStateOptimize BuildStateOptimize { get; set; }

        [Category("Optimization"), DisplayName("Strip Comments"), Description("Remove all comment nodes from the AST.")]
        public bool StripComments { get; set; }

        [Category("Optimization"), DisplayName("Patch Include Self-Execution Guards"), Description("Statically evaluate 'A_LineFile = A_ScriptFullPath' comparison checks based on whether the code is inlined from an include file (evaluates to false inside includes, true in main script).")]
        public bool PatchIncludeSelfExecution { get; set; }

        public OptimiseConfig()
        {
            RemoveDuplicateDirectives = true;
            ConvertIfToSwitch = true;
            FoldMathConstants = true;
            FoldLogicalConstants = true;
            PruneDeadBranches = true;
            FoldStringConcats = true;
            FoldConsecutiveStringAssigns = true;
            BuildStateOptimize = BuildStateOptimize.None;
            StripComments = false;
            PatchIncludeSelfExecution = true;
        }
    }

    public class OptimisePlugin : IFlowPlugin
    {
        public string Name { get { return "Transform.Optimise"; } }
        public string Target { get; set; }

        public string Category { get { return "Transform"; } }
        public string StepTitle { get { return "Optimise"; } }
        public string Icon { get { return "⚡"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(OptimiseConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (OptimiseConfig)config; }

        public OptimiseConfig Config { get; set; }

        private int _foldedMathCount = 0;
        private int _foldedLogicalCount = 0;
        private int _foldedStringCount = 0;
        private int _prunedDeadBranchesCount = 0;
        private int _removedDirectivesCount = 0;
        private int _convertedIfToSwitchCount = 0;
        private int _strippedCommentsCount = 0;
        private int _patchedIncludeGuardsCount = 0;

        public OptimisePlugin()
        {
            Config = new OptimiseConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return root;

            _foldedMathCount = 0;
            _foldedLogicalCount = 0;
            _foldedStringCount = 0;
            _prunedDeadBranchesCount = 0;
            _removedDirectivesCount = 0;
            _convertedIfToSwitchCount = 0;
            _strippedCommentsCount = 0;
            _patchedIncludeGuardsCount = 0;

            _inlinedRanges.Clear();
            if (Config.PatchIncludeSelfExecution)
            {
                FindInlinedIncludeRanges(root, new Stack<InlinedIncludeRange>());

                if (root.NodeType == "Program" && !string.IsNullOrEmpty(root.Metadata) && root.Metadata.StartsWith("inlined_ranges:"))
                {
                    string rangesStr = root.Metadata.Substring("inlined_ranges:".Length);
                    foreach (string rangePart in rangesStr.Split('|'))
                    {
                        string[] parts = rangePart.Split(':');
                        if (parts.Length == 2)
                        {
                            string fileName = parts[0];
                            string[] lines = parts[1].Split('-');
                            if (lines.Length == 2)
                            {
                                int startLine, endLine;
                                if (int.TryParse(lines[0], out startLine) && int.TryParse(lines[1], out endLine))
                                {
                                    _inlinedRanges.Add(new InlinedIncludeRange
                                    {
                                        FileName = fileName,
                                        StartLine = startLine,
                                        EndLine = endLine
                                    });
                                }
                            }
                        }
                    }
                }
            }

            var seenDirectives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            root = OptimiseNode(root, seenDirectives);

            PipelineLogger.Log("  ⚡ Transform.Optimise Summary:");
            if (Config.RemoveDuplicateDirectives && _removedDirectivesCount > 0)
                PipelineLogger.Log("    - Removed {0} duplicate directives.", _removedDirectivesCount);
            if (Config.ConvertIfToSwitch && _convertedIfToSwitchCount > 0)
                PipelineLogger.Log("    - Converted {0} if-else chains to switches.", _convertedIfToSwitchCount);
            if (Config.FoldMathConstants && _foldedMathCount > 0)
                PipelineLogger.Log("    - Folded {0} math expressions.", _foldedMathCount);
            if (Config.FoldLogicalConstants && _foldedLogicalCount > 0)
                PipelineLogger.Log("    - Folded {0} logical expressions.", _foldedLogicalCount);
            if (Config.FoldStringConcats && _foldedStringCount > 0)
                PipelineLogger.Log("    - Folded {0} string concatenations.", _foldedStringCount);
            if (Config.PruneDeadBranches && _prunedDeadBranchesCount > 0)
                PipelineLogger.Log("    - Pruned {0} dead code branches.", _prunedDeadBranchesCount);
            if (Config.StripComments && _strippedCommentsCount > 0)
                PipelineLogger.Log("    - Stripped {0} comments.", _strippedCommentsCount);
            if (Config.PatchIncludeSelfExecution && _patchedIncludeGuardsCount > 0)
                PipelineLogger.Log("    - Patched {0} include self-execution protection guards.", _patchedIncludeGuardsCount);

            return root;
        }

        // Constant values follow AhkConst (Integer/Float/String, AHK truthiness); anything uncertain is left unfolded.
        private object GetConstantValue(AstNode node)
        {
            if (node == null) return null;
            if (node.NodeType == "Grouped" && node.ChildCount == 1)
            {
                return GetConstantValue(node.GetChild(0));
            }
            if ((node.NodeType == "Identifier" || node.NodeType == "Literal") && string.Equals(node.Value, "A_IsCompiled", StringComparison.OrdinalIgnoreCase))
            {
                if (Config.BuildStateOptimize == BuildStateOptimize.AssumeCompiled) return 1L;
                if (Config.BuildStateOptimize == BuildStateOptimize.AssumeUncompiled) return 0L;
            }
            return AhkConst.Get(node);
        }

        private AstNode CreateConstantNode(object val, int line, int col)
        {
            return AhkConst.ToNode(val, line, col);
        }

        /// <summary>
        /// Truth of a condition when it is decided at compile time. Only truthiness matters here, so a constant right
        /// side decides too (`x && false` is false) as long as the left side has no effects to keep.
        /// </summary>
        private bool? ConditionTruth(AstNode node)
        {
            if (node == null) return null;
            if (node.NodeType == "Grouped" && node.ChildCount == 1) return ConditionTruth(node.GetChild(0));
            if (node.NodeType == "LogicalNot" || (node.NodeType == "UnaryExpr" && (node.Value == "!" || string.Equals(node.Value, "not", StringComparison.OrdinalIgnoreCase))))
            {
                bool? inner = node.ChildCount > 0 ? ConditionTruth(node.GetChild(0)) : null;
                return inner.HasValue ? !inner.Value : (bool?)null;
            }
            if (node.NodeType == "BinaryExpr" && node.ChildCount == 2 && IsLogicalOp(node.Value))
            {
                var left = node.GetChild(0);
                bool? l = ConditionTruth(left), r = ConditionTruth(node.GetChild(1));
                bool isOr = node.Value == "||" || node.Value.Equals("or", StringComparison.OrdinalIgnoreCase);
                bool pure = !HasSideEffects(left);
                if (isOr)
                {
                    if (l == true || (r == true && pure)) return true;
                    if (l == false && r == false) return false;
                }
                else
                {
                    if (l == false || (r == false && pure)) return false;
                    if (l == true && r == true) return true;
                }
                return null;
            }
            object v = GetConstantValue(node);
            return v != null ? AhkConst.Truthy(v) : null;
        }

        private bool HasSideEffects(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType == "Call") return true;
            if (node.NodeType == "Increment" || node.NodeType == "Decrement") return true;
            if (node.NodeType == "PostfixExpr" && (node.Value == "++" || node.Value == "--")) return true;
            if (node.NodeType == "UnaryExpr" && (node.Value == "++" || node.Value == "--")) return true;
            if (node.NodeType == "ColonAssign" || node.NodeType == "Assign" || node.NodeType == "StaticAssign") return true;
            if (node.NodeType == "BinaryExpr" && (node.Value == ":=" || node.Value == "+=" || node.Value == "-=" || node.Value == "*=" || node.Value == "/=" || node.Value == ".=" || node.Value == "&=" || node.Value == "|=" || node.Value == "^=" || node.Value == "??=" || node.Value == "//=")) return true;

            foreach (var child in node.ChildNodes)
            {
                if (HasSideEffects(child)) return true;
            }
            return false;
        }

        private bool IsIdentifier(AstNode node, string name)
        {
            if (node == null) return false;
            if (node.NodeType == "Grouped" && node.ChildCount > 0)
            {
                return IsIdentifier(node.GetChild(0), name);
            }
            return (node.NodeType == "Identifier" || node.NodeType == "Literal") && 
                   string.Equals(node.Value, name, StringComparison.OrdinalIgnoreCase);
        }

        private class InlinedIncludeRange
        {
            public string FileName { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
        }

        private List<InlinedIncludeRange> _inlinedRanges = new List<InlinedIncludeRange>();

        private void FindInlinedIncludeRanges(AstNode node, Stack<InlinedIncludeRange> stack)
        {
            if (node == null) return;

            if (node.NodeType == "Comment" && node.Value != null)
            {
                string val = node.Value.Trim();
                if (val.StartsWith("; --- begin:") && val.EndsWith("---"))
                {
                    int colonIdx = val.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string fileName = val.Substring(colonIdx + 1).Replace("---", "").Trim();
                        var range = new InlinedIncludeRange
                        {
                            FileName = fileName,
                            StartLine = node.Line
                        };
                        stack.Push(range);
                    }
                }
                else if (val.StartsWith("; --- end:") && val.EndsWith("---"))
                {
                    if (stack.Count > 0)
                    {
                        var range = stack.Pop();
                        range.EndLine = node.Line;
                        _inlinedRanges.Add(range);
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                FindInlinedIncludeRanges(child, stack);
            }
        }

        private bool IsLineInInlinedInclude(int line)
        {
            foreach (var r in _inlinedRanges)
            {
                if (line > r.StartLine && line < r.EndLine)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsUnderInclude(AstNode node)
        {
            if (IsLineInInlinedInclude(node.Line)) return true;

            AstNode p = node.Parent;
            while (p != null)
            {
                if (p.NodeType == "Include") return true;
                p = p.Parent;
            }
            return false;
        }

        private static bool IsScriptWideDirective(string directive)
        {
            var m = System.Text.RegularExpressions.Regex.Match(directive ?? "", @"^\s*#(\w+)");
            if (!m.Success) return false;
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "requires": case "singleinstance": case "notrayicon": case "persistent": case "winactivateforce":
                case "errorstdout": case "dllload": case "include":
                    return true;
            }
            return false;
        }

        private AstNode OptimiseNode(AstNode node, HashSet<string> seenDirectives)
        {
            if (node == null) return null;

            if (Config.RemoveDuplicateDirectives && (node.NodeType == "Program" || node.NodeType == "Include"))
            {
                var newChildren = new List<AstNode>();
                foreach (var child in node.ChildNodes)
                {
                    if (child == null) continue;
                    // Only script-wide directives are deduplicated; positional ones (#HotIf, #InputLevel, #HotString,
                    // #UseHook, #MaxThreads*, #SuspendExempt, ...) apply to what follows and repeat on purpose.
                    if (child.NodeType == "Directive" && IsScriptWideDirective(child.Value))
                    {
                        string key = child.Value != null ? child.Value.Trim() : "";
                        if (seenDirectives.Contains(key))
                        {
                            _removedDirectivesCount++;
                            continue;
                        }
                        seenDirectives.Add(key);
                    }
                    newChildren.Add(child);
                }
                node.SetChildren(newChildren);
            }

            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node.GetChild(i);
                if (child == null) continue;
                var optChild = OptimiseNode(child, seenDirectives);
                if (optChild != child)
                {
                    node.ReplaceChild(i, optChild);
                }
            }

            if (Config.StripComments)
            {
                for (int i = node.ChildCount - 1; i >= 0; i--)
                {
                    var child = node.GetChild(i);
                    if (child != null && child.NodeType == "Comment")
                    {
                        node.RemoveChild(i);
                        _strippedCommentsCount++;
                    }
                }
            }

            if (IsStatementContainer(node))
            {
                var newStmts = new List<AstNode>();
                foreach (var child in node.ChildNodes)
                {
                    if (child == null || child.NodeType == "Omitted") continue;
                    // A bare block (typically a pruned branch's body) is spliced in: blocks have no scope in AHK, and
                    // `{` on the line after `Name(...)` would turn that call into a function definition.
                    if (child.NodeType == "Block" && node.NodeType != "Class")
                    {
                        foreach (var inner in child.ChildNodes)
                            if (inner != null && inner.NodeType != "Omitted") newStmts.Add(inner);
                        continue;
                    }
                    newStmts.Add(child);
                }
                if (Config.PruneDeadBranches && node.NodeType == "Block")
                {
                    newStmts = DropUnreachable(newStmts);
                }
                if (Config.FoldConsecutiveStringAssigns)
                {
                    newStmts = FoldConsecutiveStringAssigns(newStmts);
                }
                node.SetChildren(newStmts);
            }

            if (Config.PruneDeadBranches && node.NodeType == "If")
            {
                var condNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                bool? condTruth = ConditionTruth(condNode);
                if (condTruth != null)
                {
                    bool isTrue = condTruth.Value;
                    _prunedDeadBranchesCount++;
                    if (isTrue)
                    {
                        var body = node.ChildCount > 1 ? node.GetChild(1) : null;
                        if (body == null) return new AstNode("Omitted", node.Line, node.Column);
                        return body;
                    }
                    else
                    {
                        if (node.ChildCount > 2 && node.GetChild(2) != null && node.GetChild(2).NodeType == "Else")
                        {
                            var elseNode = node.GetChild(2);
                            var elseBody = elseNode.ChildCount > 0 ? elseNode.GetChild(0) : null;
                            if (elseBody == null) return new AstNode("Omitted", node.Line, node.Column);
                            return elseBody;
                        }
                        return new AstNode("Omitted", node.Line, node.Column);
                    }
                }
            }

            if (node.NodeType == "BinaryExpr")
            {
                var left = node.ChildCount > 0 ? node.GetChild(0) : null;
                var right = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (left != null && right != null)
                {
                    string op = node.Value;

                    if (Config.PatchIncludeSelfExecution)
                    {
                        if (op == "=" || op == "==" || op == "!=" || op == "!==" || op == "<>")
                        {
                            bool hasLineFile = IsIdentifier(left, "A_LineFile") || IsIdentifier(right, "A_LineFile")
                                // an inlined file's A_LineFile is pinned to its path (a string) by the include inliner
                                || (left.NodeType == "String" && IsIdentifier(right, "A_ScriptFullPath"))
                                || (right.NodeType == "String" && IsIdentifier(left, "A_ScriptFullPath"));
                            bool hasScriptPath = IsIdentifier(left, "A_ScriptFullPath") || IsIdentifier(right, "A_ScriptFullPath");
                            if (hasLineFile && hasScriptPath)
                            {
                                bool isUnderInclude = IsUnderInclude(node);
                                bool isEqualOp = (op == "=" || op == "==");
                                bool resultVal = isUnderInclude ? !isEqualOp : isEqualOp;
                                _patchedIncludeGuardsCount++;
                                return CreateConstantNode(resultVal, node.Line, node.Column);
                            }
                        }
                    }

                    object lVal = GetConstantValue(left);
                    object rVal = GetConstantValue(right);

                    if (Config.FoldMathConstants && IsMathOp(op))
                    {
                        var folded = CreateConstantNode(AhkConst.Arith(op, lVal, rVal), node.Line, node.Column);
                        if (folded != null)
                        {
                            _foldedMathCount++;
                            return folded;
                        }
                    }

                    // `a || b` / `a && b` yield an operand's value, so only a constant left side decides the result.
                    if (Config.FoldLogicalConstants && IsLogicalOp(op) && lVal != null)
                    {
                        bool? lt = AhkConst.Truthy(lVal);
                        if (lt != null)
                        {
                            _foldedLogicalCount++;
                            bool isOr = op == "||" || op.Equals("or", StringComparison.OrdinalIgnoreCase);
                            return isOr ? (lt.Value ? left : right) : (lt.Value ? right : left);
                        }
                    }

                    if (Config.FoldLogicalConstants && (op == "==" || op == "=" || op == "!=" || op == "!==" || op == "<>"))
                    {
                        bool? cmp = lVal != null && rVal != null ? AhkConst.Compare(op, lVal, rVal) : null;
                        if (cmp != null)
                        {
                            _foldedLogicalCount++;
                            return CreateConstantNode(cmp.Value, node.Line, node.Column);
                        }
                    }

                    if (Config.FoldStringConcats && op == ".")
                    {
                        string ls = AhkConst.ToText(lVal), rs = AhkConst.ToText(rVal);
                        if (ls != null && rs != null)
                        {
                            _foldedStringCount++;
                            return CreateConstantNode(ls + rs, node.Line, node.Column);
                        }
                    }
                }
            }

            if (Config.FoldStringConcats && node.NodeType == "Concat")
            {
                bool allConst = true;
                var parts = new List<string>();
                for (int k = 0; k < node.ChildCount; k++)
                {
                    string text = AhkConst.ToText(GetConstantValue(node.GetChild(k)));
                    if (text != null)
                    {
                        parts.Add(text);
                    }
                    else
                    {
                        allConst = false;
                        break;
                    }
                }
                if (allConst && parts.Count > 0)
                {
                    _foldedStringCount++;
                    string folded = string.Concat(parts);
                    return CreateConstantNode(folded, node.Line, node.Column);
                }
            }

            if (Config.FoldLogicalConstants && (node.NodeType == "LogicalNot" || (node.NodeType == "UnaryExpr" && (node.Value == "!" || string.Equals(node.Value, "not", StringComparison.OrdinalIgnoreCase)))))
            {
                var operand = node.ChildCount > 0 ? node.GetChild(0) : null;
                bool? truth = operand != null ? AhkConst.Truthy(GetConstantValue(operand)) : null;
                if (truth != null)
                {
                    _foldedLogicalCount++;
                    return CreateConstantNode(!truth.Value, node.Line, node.Column);
                }
            }
            if (node.NodeType == "Grouped")
            {
                var inner = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (inner != null && (inner.NodeType == "Number" || inner.NodeType == "String" || inner.NodeType == "Literal" || inner.NodeType == "Identifier"))
                {
                    return inner;
                }
            }

            if (Config.ConvertIfToSwitch && node.NodeType == "If")
            {
                var cases = new List<AstNode>();
                AstNode defaultBody = null;
                AstNode targetNode = null;
                if (TryGetSwitchCases(node, ref cases, ref defaultBody, ref targetNode))
                {
                    if (cases.Count >= 2)
                    {
                        _convertedIfToSwitchCount++;
                        var switchNode = new AstNode("Switch", node.Line, node.Column) { EndLine = node.EndLine };
                        var switchChildren = new List<AstNode>();
                        switchChildren.Add(targetNode.Clone());
                        foreach (var c in cases)
                        {
                            switchChildren.Add(c);
                        }
                        if (defaultBody != null)
                        {
                            var defaultNode = new AstNode("Default", defaultBody.Line, defaultBody.Column);
                            var defaultBodyWrapper = new AstNode("DefaultBody", defaultBody.Line, defaultBody.Column);
                            if (defaultBody.NodeType == "Block")
                            {
                                defaultBodyWrapper.SetChildren(defaultBody.ChildNodes);
                            }
                            else
                            {
                                defaultBodyWrapper.SetChildren(new AstNode[] { defaultBody });
                            }
                            defaultNode.SetChildren(new AstNode[] { defaultBodyWrapper });
                            switchChildren.Add(defaultNode);
                        }
                        switchNode.SetChildren(switchChildren);
                        return switchNode;
                    }
                }
            }

            return node;
        }

        /// <summary>
        /// Statements after an unconditional `return` / `throw` in a block never run (typically left by a folded
        /// `if !A_IsCompiled return`). They are dropped up to the next label (a goto target); definitions and
        /// declarations are kept.
        /// </summary>
        private static bool ContainsNodeType(AstNode n, string type)
        {
            if (n == null) return false;
            if (n.NodeType == type) return true;
            foreach (var c in n.ChildNodes)
                if (ContainsNodeType(c, type)) return true;
            return false;
        }

        private List<AstNode> DropUnreachable(List<AstNode> stmts)
        {
            var result = new List<AstNode>();
            bool dead = false;
            foreach (var s in stmts)
            {
                string t = s.NodeType;
                if (t == "Label" || (dead && ContainsNodeType(s, "Label"))) dead = false;
                if (dead && t != "Method" && t != "Class" && t != "Comment" && t != "Declaration" && t != "StaticAssign"
                    && t != "Hotkey" && t != "Hotstring" && t != "Directive" && t != "Include")
                {
                    _prunedDeadBranchesCount++;
                    continue;
                }
                result.Add(s);
                if (t == "Return" || t == "Throw") dead = true;
            }
            return result;
        }

        private bool IsMathOp(string op)
        {
            return op == "+" || op == "-" || op == "*" || op == "/" || op == "//" || op == "**";
        }

        private bool IsLogicalOp(string op)
        {
            return op == "&&" || op == "||" || op == "and" || op == "or";
        }

        private bool IsStatementContainer(AstNode node)
        {
            if (node == null) return false;
            string type = node.NodeType;
            return type == "Program" || type == "Block" || type == "Include" || type == "CaseBody" || type == "DefaultBody";
        }

        private List<AstNode> FoldConsecutiveStringAssigns(List<AstNode> stmts)
        {
            if (stmts.Count < 2) return stmts;
            var result = new List<AstNode>();
            
            AstNode currentAssign = null;
            string currentVarName = null;
            List<AstNode> currentRhsParts = null;

            Action commit = () => {
                if (currentAssign != null)
                {
                    if (currentRhsParts.Count > 1)
                    {
                        var concatNode = new AstNode("Concat", currentAssign.Line, currentAssign.Column);
                        concatNode.SetChildren(currentRhsParts);
                        currentAssign.ReplaceChild(1, concatNode);
                    }
                    else if (currentRhsParts.Count == 1)
                    {
                        currentAssign.ReplaceChild(1, currentRhsParts[0]);
                    }
                    currentAssign.Value = ".=";
                }
            };

            for (int i = 0; i < stmts.Count; i++)
            {
                var stmt = stmts[i];
                bool handled = false;
                
                if (stmt.NodeType == "BinaryExpr")
                {
                    var left = stmt.ChildCount > 0 ? stmt.GetChild(0) : null;
                    var right = stmt.ChildCount > 1 ? stmt.GetChild(1) : null;
                    if (left != null && left.NodeType == "Identifier" && right != null)
                    {
                        string varName = left.Value;
                        if (stmt.Value == ".=")
                        {
                            if (currentAssign != null && currentVarName.Equals(varName, StringComparison.OrdinalIgnoreCase))
                            {
                                currentRhsParts.Add(right);
                                handled = true;
                            }
                            else
                            {
                                commit();
                                currentAssign = stmt;
                                currentVarName = varName;
                                currentRhsParts = new List<AstNode> { right };
                                result.Add(stmt);
                                handled = true;
                            }
                        }
                        else if (stmt.Value == ":=" && right.NodeType == "Concat" && right.ChildCount > 0)
                        {
                            var concatFirst = right.GetChild(0);
                            if (concatFirst.NodeType == "Identifier" && concatFirst.Value.Equals(varName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (currentAssign != null && currentVarName.Equals(varName, StringComparison.OrdinalIgnoreCase))
                                {
                                    for (int k = 1; k < right.ChildCount; k++)
                                        currentRhsParts.Add(right.GetChild(k));
                                    handled = true;
                                }
                                else
                                {
                                    commit();
                                    currentAssign = stmt;
                                    currentVarName = varName;
                                    currentRhsParts = new List<AstNode>();
                                    for (int k = 1; k < right.ChildCount; k++)
                                        currentRhsParts.Add(right.GetChild(k));
                                    result.Add(stmt);
                                    handled = true;
                                }
                            }
                        }
                    }
                }

                if (!handled)
                {
                    commit();
                    currentAssign = null;
                    result.Add(stmt);
                }
            }
            commit();

            return result;
        }

        private AstNode UnwrapGrouped(AstNode node)
        {
            while (node != null && node.NodeType == "Grouped" && node.ChildCount > 0)
            {
                node = node.GetChild(0);
            }
            return node;
        }

        private bool TryGetSwitchCases(AstNode ifNode, ref List<AstNode> cases, ref AstNode defaultBody, ref AstNode targetNode)
        {
            if (ifNode == null || ifNode.NodeType != "If") return false;

            var cond = ifNode.ChildCount > 0 ? UnwrapGrouped(ifNode.GetChild(0)) : null;
            if (cond == null || cond.NodeType != "BinaryExpr" || (cond.Value != "==" && cond.Value != "="))
                return false;

            var left = cond.ChildCount > 0 ? UnwrapGrouped(cond.GetChild(0)) : null;
            var right = cond.ChildCount > 1 ? UnwrapGrouped(cond.GetChild(1)) : null;
            if (left == null || right == null)
                return false;

            bool leftIsLiteral = left.NodeType == "Number" || left.NodeType == "String" || left.NodeType == "Literal" || (left.NodeType == "Identifier" && (left.Value == "true" || left.Value == "false"));
            bool rightIsLiteral = right.NodeType == "Number" || right.NodeType == "String" || right.NodeType == "Literal" || (right.NodeType == "Identifier" && (right.Value == "true" || right.Value == "false"));
            
            if (leftIsLiteral == rightIsLiteral) return false;

            AstNode currentTarget = leftIsLiteral ? right : left;
            AstNode currentLiteral = leftIsLiteral ? left : right;

            if (targetNode == null)
            {
                targetNode = currentTarget;
            }
            else
            {
                if (AstEmitter.Emit(currentTarget) != AstEmitter.Emit(targetNode))
                    return false;
            }

            var caseNode = new AstNode("Case", ifNode.Line, ifNode.Column) { EndLine = ifNode.EndLine };
            var caseBody = new AstNode("CaseBody", ifNode.Line, ifNode.Column);
            var originalBody = ifNode.ChildCount > 1 ? ifNode.GetChild(1) : null;
            if (originalBody != null)
            {
                if (originalBody.NodeType == "Block")
                {
                    caseBody.SetChildren(originalBody.ChildNodes);
                }
                else
                {
                    caseBody.SetChildren(new AstNode[] { originalBody });
                }
            }
            caseNode.SetChildren(new AstNode[] { currentLiteral.Clone(), caseBody });
            cases.Add(caseNode);

            if (ifNode.ChildCount > 2 && ifNode.GetChild(2) != null && ifNode.GetChild(2).NodeType == "Else")
            {
                var elseNode = ifNode.GetChild(2);
                var elseBody = elseNode.ChildCount > 0 ? elseNode.GetChild(0) : null;
                if (elseBody != null)
                {
                    if (elseBody.NodeType == "If")
                    {
                        return TryGetSwitchCases(elseBody, ref cases, ref defaultBody, ref targetNode);
                    }
                    else
                    {
                        defaultBody = elseBody;
                        return true;
                    }
                }
            }

            return true;
        }
    }
}
