using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AHK2AST.Plugins
{
    public enum TreeShakingProfile
    {
        Off,
        Safe,
        Aggressive,
        Custom
    }

    public class TreeShakerConfig
    {
        private TreeShakingProfile _profile = TreeShakingProfile.Safe;

        private bool _traceStringReferences = true;
        private bool _shakeMainFileDeclarations = false;
        private bool _shakeLibraryDeclarations = true;
        private bool _shakeUnusedMethods = false;
        private bool _shakeDeadBranches = true;
        private bool _optimizeEmptyBlocks = true;
        private bool _shakeUnusedGlobals = true;
        private bool _shakeUnusedAssignments = false;
        private bool _foldConstantExpressions = true;

        [Category("Tree Shaker"), DisplayName("Shaking Profile"), Description("Select a preset profile for tree shaking. Safe preserves most things. Aggressive prunes all unused items.")]
        [RefreshProperties(RefreshProperties.All)]
        public TreeShakingProfile Profile
        {
            get { return _profile; }
            set
            {
                _profile = value;
                ApplyProfileDefaults();
            }
        }

        [Category("Tree Shaker"), DisplayName("Trace String References"), Description("Scan string literals for function or class names to prevent them from being shaken.")]
        public bool TraceStringReferences
        {
            get { return _traceStringReferences; }
            set { if (_traceStringReferences != value) { _traceStringReferences = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Shake Main File Declarations"), Description("If true, unused functions and classes in the main file will also be removed. If false, they are preserved.")]
        public bool ShakeMainFileDeclarations
        {
            get { return _shakeMainFileDeclarations; }
            set { if (_shakeMainFileDeclarations != value) { _shakeMainFileDeclarations = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Shake Library Declarations"), Description("If true, unused functions and classes in included library files will also be removed. If false, they are preserved.")]
        public bool ShakeLibraryDeclarations
        {
            get { return _shakeLibraryDeclarations; }
            set { if (_shakeLibraryDeclarations != value) { _shakeLibraryDeclarations = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Shake Unused Methods"), Description("Remove unused methods and properties from classes that are otherwise used.")]
        public bool ShakeUnusedMethods
        {
            get { return _shakeUnusedMethods; }
            set { if (_shakeUnusedMethods != value) { _shakeUnusedMethods = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Shake Dead Branches"), Description("Statically evaluate constant conditions and prune unreachable If/Else branches.")]
        public bool ShakeDeadBranches
        {
            get { return _shakeDeadBranches; }
            set { if (_shakeDeadBranches != value) { _shakeDeadBranches = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Optimize Empty Blocks"), Description("Remove redundant empty code blocks.")]
        public bool OptimizeEmptyBlocks
        {
            get { return _optimizeEmptyBlocks; }
            set { if (_optimizeEmptyBlocks != value) { _optimizeEmptyBlocks = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Shake Unused Globals"), Description("Remove global variable declarations that are never referenced.")]
        public bool ShakeUnusedGlobals
        {
            get { return _shakeUnusedGlobals; }
            set { if (_shakeUnusedGlobals != value) { _shakeUnusedGlobals = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Shake Unused Assignments"), Description("Remove variable assignments or declarations where the target variable is never read.")]
        public bool ShakeUnusedAssignments
        {
            get { return _shakeUnusedAssignments; }
            set { if (_shakeUnusedAssignments != value) { _shakeUnusedAssignments = value; SetCustomProfileIfChanged(); } }
        }

        [Category("Tree Shaker"), DisplayName("Fold Constant Expressions"), Description("Statically evaluate constant operations (e.g., arithmetic, string concatenation) and replace them with their computed literals.")]
        public bool FoldConstantExpressions
        {
            get { return _foldConstantExpressions; }
            set { if (_foldConstantExpressions != value) { _foldConstantExpressions = value; SetCustomProfileIfChanged(); } }
        }

        public TreeShakerConfig()
        {
            ApplyProfileDefaults();
        }

        private void SetCustomProfileIfChanged()
        {
            _profile = TreeShakingProfile.Custom;
        }

        private void ApplyProfileDefaults()
        {
            if (_profile == TreeShakingProfile.Off)
            {
                _traceStringReferences = true;
                _shakeMainFileDeclarations = false;
                _shakeLibraryDeclarations = false;
                _shakeUnusedMethods = false;
                _shakeDeadBranches = false;
                _optimizeEmptyBlocks = false;
                _shakeUnusedGlobals = false;
                _shakeUnusedAssignments = false;
                _foldConstantExpressions = false;
            }
            else if (_profile == TreeShakingProfile.Safe)
            {
                _traceStringReferences = true;
                _shakeMainFileDeclarations = false;
                _shakeLibraryDeclarations = true;
                _shakeUnusedMethods = false;
                _shakeDeadBranches = true;
                _optimizeEmptyBlocks = true;
                _shakeUnusedGlobals = true;
                _shakeUnusedAssignments = false;
                _foldConstantExpressions = true;
            }
            else if (_profile == TreeShakingProfile.Aggressive)
            {
                _traceStringReferences = true;
                _shakeMainFileDeclarations = true;
                _shakeLibraryDeclarations = true;
                _shakeUnusedMethods = true;
                _shakeDeadBranches = true;
                _optimizeEmptyBlocks = true;
                _shakeUnusedGlobals = true;
                _shakeUnusedAssignments = true;
                _foldConstantExpressions = true;
            }
        }
    }

    public class TreeShakerPlugin : IFlowPlugin
    {
        public string Name { get { return "Transform.Tree-shake"; } }
        public string Target { get; set; }

        public string Category { get { return "Transform"; } }
        public string StepTitle { get { return "Tree-shake"; } }
        public string Icon { get { return "🌳"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(TreeShakerConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (TreeShakerConfig)config; }

        public TreeShakerConfig Config { get; set; }

        public TreeShakerPlugin()
        {
            Config = new TreeShakerConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            var engine = new LogicFollowerEngine(Config);
            engine.Analyze(root);
            engine.Shake(root);

            PipelineLogger.Log("  🌳 Transform.Tree-shake Summary:");
            PipelineLogger.Log("    - Profile: {0}", Config.Profile);
            if (engine.PrunedClassesCount > 0)
                PipelineLogger.Log("    - Pruned {0} unused classes.", engine.PrunedClassesCount);
            if (engine.PrunedMethodsCount > 0)
                PipelineLogger.Log("    - Pruned {0} unused methods/properties.", engine.PrunedMethodsCount);
            if (engine.PrunedGlobalsCount > 0)
                PipelineLogger.Log("    - Pruned {0} unused global variables.", engine.PrunedGlobalsCount);
            if (engine.PrunedAssignmentsCount > 0)
                PipelineLogger.Log("    - Pruned {0} unused local variables/assignments.", engine.PrunedAssignmentsCount);
            if (engine.PrunedDeadBranchesCount > 0)
                PipelineLogger.Log("    - Pruned {0} dead code branches.", engine.PrunedDeadBranchesCount);
            if (engine.PrunedEmptyBlocksCount > 0)
                PipelineLogger.Log("    - Pruned {0} empty code blocks.", engine.PrunedEmptyBlocksCount);
            if (engine.FoldedConstantsCount > 0)
                PipelineLogger.Log("    - Folded {0} constant expressions.", engine.FoldedConstantsCount);

            return root;
        }
    }

    public class ConstantEvaluator
    {
        private Dictionary<string, object> _constants = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public void RegisterConstant(string name, object value)
        {
            _constants[name] = value;
        }

        /// <summary>A condition's truth value when it is decided at compile time (a bool), else null.</summary>
        public object Evaluate(AstNode node)
        {
            object v = Value(node);
            bool? t = v != null ? AhkConst.Truthy(v) : null;
            return t.HasValue ? (object)t.Value : null;
        }

        // AhkConst values (Integer/Float/String); null when unknown.
        private object Value(AstNode node)
        {
            if (node == null) return null;
            switch (node.NodeType)
            {
                case "Grouped":
                    return node.ChildCount == 1 ? Value(node.GetChild(0)) : null;
                case "Identifier":
                {
                    object c = AhkConst.Get(node);
                    if (c != null) return c;
                    object val;
                    return node.Value != null && _constants.TryGetValue(node.Value, out val) ? val : null;
                }
                case "Number":
                case "String":
                case "Literal":
                    return AhkConst.Get(node);
                case "LogicalNot":
                case "UnaryExpr":
                {
                    if (node.NodeType == "UnaryExpr" && node.Value != "!" && !string.Equals(node.Value, "not", StringComparison.OrdinalIgnoreCase)) return null;
                    bool? t = node.ChildCount > 0 ? AhkConst.Truthy(Value(node.GetChild(0))) : null;
                    return t.HasValue ? (object)(t.Value ? 0L : 1L) : null;
                }
                case "BinaryExpr":
                {
                    if (node.ChildCount != 2) return null;
                    string op = node.Value ?? "";
                    object l = Value(node.GetChild(0));
                    if (op == "&&" || op == "||" || op.Equals("and", StringComparison.OrdinalIgnoreCase) || op.Equals("or", StringComparison.OrdinalIgnoreCase))
                    {
                        bool? lt = l != null ? AhkConst.Truthy(l) : null;
                        if (lt == null) return null;
                        bool isOr = op == "||" || op.Equals("or", StringComparison.OrdinalIgnoreCase);
                        return isOr == lt.Value ? l : Value(node.GetChild(1)); // the operand that decides is the result
                    }
                    object r = Value(node.GetChild(1));
                    if (l == null || r == null) return null;
                    bool? cmp = AhkConst.Compare(op, l, r);
                    if (cmp != null) return cmp.Value ? 1L : 0L;
                    if ((op == "<" || op == ">" || op == "<=" || op == ">=") && AhkConst.IsNumber(l) && AhkConst.IsNumber(r))
                    {
                        double a = l is long ? (long)l : (double)l, b = r is long ? (long)r : (double)r;
                        bool res = op == "<" ? a < b : op == ">" ? a > b : op == "<=" ? a <= b : a >= b;
                        return res ? 1L : 0L;
                    }
                    return AhkConst.Arith(op, l, r);
                }
            }
            return null;
        }
    }
    public class LogicFollowerEngine
    {
        private TreeShakerConfig _config;
        private ConstantEvaluator _evaluator;

        private static readonly HashSet<string> ProtectedMetaMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "__New",
            "__Call",
            "__Get",
            "__Set",
            "__Delete",
            "__Enum",
            "__Item",
            "Call"
        };

        public Dictionary<string, AstNode> Functions { get; private set; }
        public Dictionary<string, ClassInfo> Classes { get; private set; }
        public Dictionary<string, List<AstNode>> Globals { get; private set; }

        public HashSet<AstNode> ActiveNodes { get; private set; }
        public HashSet<string> ReferencedSymbols { get; private set; }
        public HashSet<string> ReferencedMembers { get; private set; }
        public HashSet<string> ReferencedDynamicPrefixes { get; private set; }
        /// <summary>Literal prefixes of dynamic variable names (`b%n%` → "b"): any global starting with one stays.</summary>
        public HashSet<string> ExactDynamicPrefixes { get; private set; }

        public int PrunedClassesCount { get; set; }
        public int PrunedMethodsCount { get; set; }
        public int PrunedGlobalsCount { get; set; }
        public int PrunedAssignmentsCount { get; set; }
        public int PrunedEmptyBlocksCount { get; set; }
        public int PrunedDeadBranchesCount { get; set; }
        public int FoldedConstantsCount { get; set; }

        public class ClassInfo
        {
            public AstNode Node { get; set; }
            public string Name { get; set; }
            public string BaseClass { get; set; }
            public Dictionary<string, AstNode> Methods { get; set; }
            public Dictionary<string, AstNode> Properties { get; set; }
            public List<AstNode> StaticFields { get; set; }
            /// <summary>A second member with a name already taken (`__new(p)` + `static __new()`, a static and an
            /// instance `Show`): activated together with the first whenever that name is.</summary>
            public List<AstNode> SameNameMembers { get; set; }

            public ClassInfo()
            {
                SameNameMembers = new List<AstNode>();
                Methods = new Dictionary<string, AstNode>(StringComparer.OrdinalIgnoreCase);
                Properties = new Dictionary<string, AstNode>(StringComparer.OrdinalIgnoreCase);
                StaticFields = new List<AstNode>();
            }
        }

        public LogicFollowerEngine(TreeShakerConfig config)
        {
            _config = config;
            _evaluator = new ConstantEvaluator();
            Functions = new Dictionary<string, AstNode>(StringComparer.OrdinalIgnoreCase);
            Classes = new Dictionary<string, ClassInfo>(StringComparer.OrdinalIgnoreCase);
            Globals = new Dictionary<string, List<AstNode>>(StringComparer.OrdinalIgnoreCase);

            ActiveNodes = new HashSet<AstNode>();
            ReferencedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReferencedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReferencedDynamicPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExactDynamicPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            PrunedClassesCount = 0;
            PrunedMethodsCount = 0;
            PrunedGlobalsCount = 0;
            PrunedAssignmentsCount = 0;
            PrunedEmptyBlocksCount = 0;
            PrunedDeadBranchesCount = 0;
            FoldedConstantsCount = 0;
        }

        private bool NeedsParenthesesForReplacement(AstNode parent, AstNode child)
        {
            if (parent == null || child == null) return false;
            return (IsStatementContainer(parent) || parent.NodeType == "MultiStatement") && NeedsParenthesesForStatement(child);
        }

        private bool IsSymbolActive(string varName)
        {
            if (string.IsNullOrEmpty(varName)) return false;
            if (varName.IndexOf('%') >= 0) return true; // assignment through a dynamic name: unknown target, always kept
            if (ReferencedSymbols.Contains(varName)) return true;
            foreach (var exactPrefix in ExactDynamicPrefixes)
                if (varName.StartsWith(exactPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var prefix in ReferencedDynamicPrefixes)
            {
                if (prefix.Length >= 2 &&
                    (varName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                     varName.EndsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsStatementContainer(AstNode node)
        {
            if (node == null) return false;
            string type = node.NodeType;
            return type == "Program" || type == "Block" || type == "Include" || type == "CaseBody" || type == "DefaultBody" || type == "Class";
        }

        /// <summary>
        /// An item of a comma statement (`a := 0, b := 1`) whose value nobody reads: a side-effect-free item can be
        /// dropped outright (as long as one item remains) instead of leaving its bare value behind.
        /// </summary>
        private static bool IsDiscardedListItem(AstNode parent)
        {
            return parent != null && parent.NodeType == "MultiStatement" && parent.ChildCount > 1;
        }

        private bool NeedsParenthesesForStatement(AstNode node)
        {
            if (node == null) return false;
            string type = node.NodeType;
            if (type == "Call" || type == "Assign" || type == "ColonAssign" || type == "StaticAssign" ||
                type == "Increment" || type == "Decrement" || type == "Grouped" || type == "Block")
            {
                return false;
            }
            if (type == "PostfixExpr" && (node.Value == "++" || node.Value == "--"))
            {
                return false;
            }
            if (type == "UnaryExpr" && (node.Value == "++" || node.Value == "--"))
            {
                return false;
            }
            return true;
        }

        private void DetectConstants(AstNode node, Dictionary<string, int> assignmentCounts, Dictionary<string, object> potentialConstants)
        {
            if (node == null) return;

            if (node.NodeType == "ColonAssign" || node.NodeType == "Assign" || (node.NodeType == "BinaryExpr" && node.Value == ":="))
            {
                var left = node.ChildCount > 0 ? node.GetChild(0) : null;
                var right = node.ChildCount > 1 ? node.GetChild(1) : null;

                if (left != null && left.NodeType == "Identifier")
                {
                    string varName = left.Value;
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;

                    object val = EvaluateLiteral(right);
                    if (val != null)
                    {
                        potentialConstants[varName] = val;
                    }
                    else
                    {
                        potentialConstants.Remove(varName);
                    }
                }
            }
            else if (node.NodeType == "BinaryExpr" && (node.Value == "+=" || node.Value == "-=" || node.Value == "*=" || 
                node.Value == "/=" || node.Value == ".=" || node.Value == "&=" || node.Value == "|=" || 
                node.Value == "^=" || node.Value == "??=" || node.Value == "//=" || node.Value == ">>=" || node.Value == "<<="))
            {
                var operand = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (operand != null && operand.NodeType == "Identifier")
                {
                    string varName = operand.Value;
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                    potentialConstants.Remove(varName);
                }
            }
            else if (node.NodeType == "ForVars")
            {
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "Identifier" && !string.IsNullOrEmpty(child.Value))
                    {
                        string varName = child.Value;
                        assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                        potentialConstants.Remove(varName);
                    }
                }
            }
            else if (node.NodeType == "Catch")
            {
                if (!string.IsNullOrEmpty(node.Metadata))
                {
                    string varName = node.Metadata;
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                    potentialConstants.Remove(varName);
                }
            }
            else if (node.NodeType == "UnaryExpr" && node.Value == "&")
            {
                var operand = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (operand != null && operand.NodeType == "Identifier")
                {
                    string varName = operand.Value;
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                    potentialConstants.Remove(varName);
                }
            }
            else if (node.NodeType == "Increment" || node.NodeType == "Decrement" || (node.NodeType == "PostfixExpr" && (node.Value == "++" || node.Value == "--")) || (node.NodeType == "UnaryExpr" && (node.Value == "++" || node.Value == "--")))
            {
                var operand = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (operand != null && operand.NodeType == "Identifier")
                {
                    string varName = operand.Value;
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                    potentialConstants.Remove(varName);
                }
            }
            else if (node.NodeType == "Parameter")
            {
                string varName = node.Value;
                if (!string.IsNullOrEmpty(varName))
                {
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                    potentialConstants.Remove(varName);
                }
            }
            else if (node.NodeType == "Declaration")
            {
                string varName = node.Value;
                if (!string.IsNullOrEmpty(varName))
                {
                    assignmentCounts[varName] = assignmentCounts.ContainsKey(varName) ? assignmentCounts[varName] + 1 : 1;
                    if (node.ChildCount > 0)
                    {
                        object val = EvaluateLiteral(node.GetChild(0));
                        if (val != null)
                        {
                            potentialConstants[varName] = val;
                        }
                        else
                        {
                            potentialConstants.Remove(varName);
                        }
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                DetectConstants(child, assignmentCounts, potentialConstants);
            }
        }

        private object EvaluateLiteral(AstNode node)
        {
            return AhkConst.Get(node);
        }

        private void FlattenAllDeclarations(AstNode node)
        {
            if (node == null) return;

            if (node.NodeType == "Block" || node.NodeType == "Program" || node.NodeType == "Include" || node.NodeType == "Class")
            {
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child == null) continue;

                    if (child.NodeType == "Declaration" || child.NodeType == "StaticAssign")
                    {
                        var chained = new List<AstNode>();
                        for (int ci = child.ChildCount - 1; ci >= 0; ci--)
                        {
                            var sub = child.GetChild(ci);
                            if (sub != null && (sub.NodeType == "Declaration" || sub.NodeType == "StaticAssign"))
                            {
                                chained.Insert(0, sub);
                                child.RemoveChild(ci);
                            }
                        }

                        if (chained.Count > 0)
                        {
                            for (int k = 0; k < chained.Count; k++)
                            {
                                node.InsertChild(i + 1 + k, chained[k]);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < node.ChildCount; i++)
            {
                FlattenAllDeclarations(node.GetChild(i));
            }
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

        /// <summary>
        /// The only parts of Analyze that change the tree: constant folding and splitting chained declarations.
        /// For callers that want those alone (the minifier) instead of a whole reachability analysis.
        /// </summary>
        public void FoldAndFlatten(AstNode root)
        {
            if (_config.FoldConstantExpressions) FoldConstants(root);
            FlattenAllDeclarations(root);
        }

        public void Analyze(AstNode root)
        {
            if (_config.FoldConstantExpressions)
            {
                FoldConstants(root);
            }
            FlattenAllDeclarations(root);
            if (_config.ShakeDeadBranches)
            {
                var assignmentCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var potentialConstants = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                DetectConstants(root, assignmentCounts, potentialConstants);

                foreach (var kv in potentialConstants)
                {
                    if (assignmentCounts[kv.Key] == 1)
                    {
                        _evaluator.RegisterConstant(kv.Key, kv.Value);
                    }
                }
            }

            // 1. Collect all declarations in the AST
            CollectDeclarations(root, isInsideClass: false, currentClass: null);

            // 2. Identify initial active nodes (entry points)
            List<AstNode> entryPoints = new List<AstNode>();
            FindEntryPoints(root, entryPoints);
            FindClassesWithStaticNew(root, entryPoints);

            // 3. Iterative reachability analysis
            Queue<AstNode> scanQueue = new Queue<AstNode>();
            foreach (var ep in entryPoints)
            {
                if (ep.NodeType == "Class")
                {
                    ClassInfo classInfo;
                    if (Classes.TryGetValue(ep.Value, out classInfo))
                    {
                        ActivateClass(classInfo, scanQueue);
                    }
                    else
                    {
                        if (ActiveNodes.Add(ep))
                            scanQueue.Enqueue(ep);
                    }
                }
                else
                {
                    if (ActiveNodes.Add(ep))
                        scanQueue.Enqueue(ep);
                }
            }

            while (scanQueue.Count > 0)
            {
                AstNode current = scanQueue.Dequeue();
                ScanNodeForReferences(current, scanQueue);
            }
        }

        private bool IsLocalSymbolReferenced(string varName, HashSet<string> reads, HashSet<string> dynamicPrefixes)
        {
            if (!string.IsNullOrEmpty(varName) && varName.IndexOf('%') >= 0) return true; // dynamic target: always kept
            if (reads.Contains(varName)) return true;
            foreach (var prefix in dynamicPrefixes)
            {
                if (prefix.Length >= 2 &&
                    (varName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                     varName.EndsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
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

        /// <summary>`&(x := 0)`: the assignment's variable is handed out by reference (the callee writes it), so the
        /// assignment can never be dropped or collapsed to its value (`&0` is not valid).</summary>
        private static bool IsReferenceOperand(AstNode n)
        {
            var p = n == null ? null : n.Parent;
            while (p != null && p.NodeType == "Grouped") p = p.Parent;
            return p != null && p.NodeType == "UnaryExpr" && p.Value == "&";
        }

        /// <summary>
        /// An assignment that must stay even though its target is never read: its target is taken by reference, or
        /// it assigns `unset` where its value is used (`c ? x : (_ := unset)`): a bare `unset` is only allowed in
        /// a few places (arguments, `??` operands), so it can't stand in for the assignment.
        /// </summary>
        private static bool MustKeepAssignment(AstNode parent, AstNode assign)
        {
            if (IsReferenceOperand(assign)) return true;
            var rhs = assign.ChildCount > 1 ? assign.GetChild(1) : null;
            while (rhs != null && rhs.NodeType == "Grouped" && rhs.ChildCount > 0) rhs = rhs.GetChild(0);
            bool isUnset = rhs != null && rhs.NodeType == "Identifier" && string.Equals(rhs.Value, "unset", StringComparison.OrdinalIgnoreCase);
            if (!isUnset) return false;
            string pt = parent == null ? "" : parent.NodeType;
            bool discarded = pt == "Program" || pt == "Block" || pt == "Include" || pt == "CaseBody" || pt == "DefaultBody" || pt == "Class"
                || (pt == "MultiStatement" && parent.ChildCount > 1);
            return !discarded;
        }

        /// <summary>
        /// A dynamic variable name such as `CALG_%SID%` or `pre%a%mid%b%` (a single Identifier whose name is built at
        /// runtime): gives the literal text before the first `%` (any variable starting with it may be referenced)
        /// and the names read inside each `%...%`. The plain `%x%` form is not included (handled on its own).
        /// </summary>
        private static bool TryDynamicName(string value, out string prefix, out List<string> innerNames)
        {
            prefix = null;
            innerNames = null;
            if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0) return false;
            if (value.Length > 2 && value[0] == '%' && value.IndexOf('%', 1) == value.Length - 1) return false;
            innerNames = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in PercentDeref.Matches(value))
                innerNames.Add(m.Groups[1].Value.Trim());
            prefix = value.Substring(0, value.IndexOf('%'));
            return true;
        }

        /// <summary>
        /// Names read by the expression inside a `%...%` deref, which is any expression (`obj.%props[A_Index]%`
        /// reads props and A_Index). Words after a `.` and inside string literals are skipped; keywords may slip
        /// through, which only keeps a little more alive.
        /// </summary>
        static readonly System.Text.RegularExpressions.Regex PercentDeref = new System.Text.RegularExpressions.Regex("%([^%]*)%");
        static readonly System.Text.RegularExpressions.Regex QuotedText = new System.Text.RegularExpressions.Regex("\"(?:[^\"`]|`.)*\"|'(?:[^'`]|`.)*'");
        static readonly System.Text.RegularExpressions.Regex NameToken = new System.Text.RegularExpressions.Regex("(?<![\\w.$#@])[A-Za-z_$#@\u0080-\uFFFF][\\w$#@\u0080-\uFFFF]*");

        private static IEnumerable<string> DerefNames(string inner)
        {
            if (string.IsNullOrEmpty(inner)) yield break;
            string code = QuotedText.Replace(inner, " ");
            foreach (System.Text.RegularExpressions.Match m in NameToken.Matches(code))
                yield return m.Value;
        }

        /// <summary>Names read by the %...% derefs of a member name (On%names[1]% reads names).</summary>
        private static IEnumerable<string> MemberDerefNames(string memberName)
        {
            if (string.IsNullOrEmpty(memberName)) yield break;
            foreach (System.Text.RegularExpressions.Match m in PercentDeref.Matches(memberName))
                foreach (var d in DerefNames(m.Groups[1].Value)) yield return d;
        }

        private bool HasDynamicDeref(AstNode node)
        {
            if (node == null) return false;
            if (node.NodeType != "Member" && !string.IsNullOrEmpty(node.Value) &&
                node.Value.StartsWith("%") && node.Value.EndsWith("%") && node.Value.Length > 2)
            {
                string inner = node.Value.Substring(1, node.Value.Length - 2);
                return true; // %expr%: could be any variable
            }
            string dynPrefix;
            List<string> dynInner;
            if (node.NodeType == "Identifier" && TryDynamicName(node.Value, out dynPrefix, out dynInner) && dynPrefix.Length == 0)
                return true; // `%n%x`: could be any variable
            foreach (var child in node.ChildNodes)
            {
                if (HasDynamicDeref(child)) return true;
            }
            return false;
        }

        private static bool IsAssumeGlobal(AstNode node, AstNode root)
        {
            if (node == null) return false;
            if (node != root && (node.NodeType == "Method" || node.NodeType == "Class")) return false;
            if (node.NodeType == "Declaration" && node.Metadata == "global" && string.IsNullOrEmpty(node.Value)) return true;
            foreach (var c in node.ChildNodes)
                if (IsAssumeGlobal(c, root)) return true;
            return false;
        }

        private void ShakeLocalVariables(AstNode methodNode)
        {
            if (!_config.ShakeUnusedAssignments) return;
            if (HasDynamicDeref(methodNode)) return;
            if (IsAssumeGlobal(methodNode, methodNode)) return; // bare `global`: its "locals" are globals read elsewhere

            // 1. Collect all variable reads inside this method
            var reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dynamicPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectLocalReads(methodNode, reads, dynamicPrefixes);

            // 2. Find declared globals inside this method
            var declaredGlobals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectDeclaredGlobals(methodNode, declaredGlobals, methodNode);

            // 3. Construct protected variables set (globals + parameters)
            var protectedVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in declaredGlobals)
            {
                protectedVars.Add(g);
            }

            var paramsNode = methodNode.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
            if (paramsNode != null)
            {
                foreach (var param in paramsNode.ChildNodes)
                {
                    if (param != null && param.NodeType == "Parameter" && !string.IsNullOrEmpty(param.Value))
                    {
                        protectedVars.Add(param.Value);
                    }
                }
            }

            // 4. Prune unused locals recursively in the method body
            PruneUnusedLocals(methodNode, reads, dynamicPrefixes, protectedVars, methodNode);
        }

        private void CollectLocalReads(AstNode node, HashSet<string> reads, HashSet<string> dynamicPrefixes, bool isLhsOfAssign = false)
        {
            if (node == null) return;
            // `this` is a (normally implicit) local: `local this := GuiFromHwnd(hwnd)` in a static nested function.
            if (node.NodeType == "This") { reads.Add("this"); return; }

            if (node.NodeType == "Concat" || (node.NodeType == "BinaryExpr" && node.Value == "."))
            {
                for (int cIdx = 0; cIdx < node.ChildCount; cIdx++)
                {
                    var child = node.GetChild(cIdx);
                    if (child != null && (child.NodeType == "Identifier" || child.NodeType == "String"))
                    {
                        string val = child.Value;
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (child.NodeType == "String" && val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                            {
                                val = val.Substring(1, val.Length - 2);
                            }
                            if (!val.StartsWith("%") && !val.EndsWith("%"))
                            {
                                dynamicPrefixes.Add(val);
                            }
                        }
                    }
                }
            }

            if (node.NodeType == "Identifier")
            {
                string localPrefix;
                List<string> localInner;
                if (TryDynamicName(node.Value, out localPrefix, out localInner))
                {
                    foreach (var n in localInner) foreach (var d in DerefNames(n)) reads.Add(d);
                    if (localPrefix.Length > 0) dynamicPrefixes.Add(localPrefix);
                    return;
                }
                if (!isLhsOfAssign && !string.IsNullOrEmpty(node.Value))
                {
                    if (node.Value.StartsWith("%") && node.Value.EndsWith("%") && node.Value.Length > 2)
                    {
                        string inner = node.Value.Substring(1, node.Value.Length - 2);
                        foreach (var d in DerefNames(inner)) reads.Add(d);
                    }
                    else
                    {
                        reads.Add(node.Value);
                    }
                }
                return;
            }

            if (node.NodeType == "ColonAssign" || node.NodeType == "Assign" || (node.NodeType == "BinaryExpr" && node.Value == ":="))
            {
                var lhs = node.ChildCount > 0 ? node.GetChild(0) : null;
                var rhs = node.ChildCount > 1 ? node.GetChild(1) : null;
                
                if (lhs != null)
                {
                    if (lhs.NodeType == "Identifier")
                    {
                        CollectLocalReads(lhs, reads, dynamicPrefixes, isLhsOfAssign: true);
                    }
                    else
                    {
                        CollectLocalReads(lhs, reads, dynamicPrefixes, isLhsOfAssign: false);
                    }
                }
                if (rhs != null)
                {
                    CollectLocalReads(rhs, reads, dynamicPrefixes, isLhsOfAssign: false);
                }
                return;
            }

            if (node.NodeType == "Declaration" || node.NodeType == "StaticAssign")
            {
                if (node.ChildCount > 0)
                {
                    CollectLocalReads(node.GetChild(0), reads, dynamicPrefixes, isLhsOfAssign: false);
                }
                return;
            }

            if (node.NodeType == "Member")
            {
                var obj = node.ChildCount > 0 ? node.GetChild(0) : null;
                CollectLocalReads(obj, reads, dynamicPrefixes, isLhsOfAssign: false);

                foreach (var d in MemberDerefNames(node.Value)) reads.Add(d);
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                CollectLocalReads(child, reads, dynamicPrefixes, isLhsOfAssign);
            }
        }

        private void CollectDeclaredGlobals(AstNode node, HashSet<string> declaredGlobals, AstNode rootNode)
        {
            if (node == null) return;
            if (node.NodeType == "Method" && node != rootNode)
            {
                return;
            }

            if (node.NodeType == "Declaration" && node.Metadata == "global")
            {
                if (!string.IsNullOrEmpty(node.Value))
                {
                    declaredGlobals.Add(node.Value);
                }
            }

            foreach (var child in node.ChildNodes)
            {
                CollectDeclaredGlobals(child, declaredGlobals, rootNode);
            }
        }

        private void PruneUnusedLocals(AstNode node, HashSet<string> reads, HashSet<string> dynamicPrefixes, HashSet<string> protectedVars, AstNode rootNode)
        {
            if (node == null) return;
            if (node.NodeType == "Method" && node != rootNode)
            {
                return;
            }

            for (int i = node.ChildCount - 1; i >= 0; i--)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                bool isPruned = false;

                if (child.NodeType == "Declaration" || child.NodeType == "StaticAssign")
                {
                    string varName = child.Value;
                    if (!string.IsNullOrEmpty(varName) && !IsLocalSymbolReferenced(varName, reads, dynamicPrefixes) &&
                        !protectedVars.Contains(varName) &&
                        !Globals.ContainsKey(varName) &&
                        !Classes.ContainsKey(varName) &&
                        !Functions.ContainsKey(varName) &&
                        !ProtectedMetaMethods.Contains(varName))
                    {
                        isPruned = true;
                        PrunedAssignmentsCount++;
                        if (HasSideEffects(child))
                        {
                            if (child.ChildCount > 0)
                            {
                                var init = child.GetChild(0);
                                if (NeedsParenthesesForReplacement(node, init))
                                {
                                    var grp = new AstNode("Grouped", init.Line, init.Column);
                                    grp.AddChild(init);
                                    node.ReplaceChild(i, grp);
                                    PruneUnusedLocals(grp, reads, dynamicPrefixes, protectedVars, rootNode);
                                }
                                else
                                {
                                    node.ReplaceChild(i, init);
                                    PruneUnusedLocals(init, reads, dynamicPrefixes, protectedVars, rootNode);
                                }
                            }
                            else
                            {
                                if (IsStatementContainer(node))
                                {
                                    node.RemoveChild(i);
                                }
                                else
                                {
                                    node.ReplaceChild(i, new AstNode("Omitted", child.Line, child.Column));
                                }
                            }
                        }
                        else
                        {
                            if (IsStatementContainer(node) || IsDiscardedListItem(node))
                            {
                                node.RemoveChild(i);
                            }
                            else
                            {
                                if (child.ChildCount > 0)
                                {
                                    var init = child.GetChild(0);
                                    if (NeedsParenthesesForReplacement(node, init))
                                    {
                                        var grp = new AstNode("Grouped", init.Line, init.Column);
                                        grp.AddChild(init);
                                        node.ReplaceChild(i, grp);
                                        PruneUnusedLocals(grp, reads, dynamicPrefixes, protectedVars, rootNode);
                                    }
                                    else
                                    {
                                        node.ReplaceChild(i, init);
                                        PruneUnusedLocals(init, reads, dynamicPrefixes, protectedVars, rootNode);
                                    }
                                }
                                else
                                {
                                    node.ReplaceChild(i, new AstNode("Omitted", child.Line, child.Column));
                                }
                            }
                        }
                    }
                }

                if (!isPruned && (child.NodeType == "ColonAssign" || child.NodeType == "Assign" ||
                     (child.NodeType == "BinaryExpr" && child.Value == ":=")))
                {
                    var lhs = child.ChildCount > 0 ? child.GetChild(0) : null;
                    var rhs = child.ChildCount > 1 ? child.GetChild(1) : null;

                    if (lhs != null && lhs.NodeType == "Identifier" && !MustKeepAssignment(node, child))
                    {
                        string varName = lhs.Value;
                        if (!string.IsNullOrEmpty(varName) && !IsLocalSymbolReferenced(varName, reads, dynamicPrefixes) &&
                            !protectedVars.Contains(varName) &&
                            !Globals.ContainsKey(varName) &&
                            !Classes.ContainsKey(varName) &&
                            !Functions.ContainsKey(varName) &&
                            !ProtectedMetaMethods.Contains(varName))
                        {
                            isPruned = true;
                            PrunedAssignmentsCount++;
                            if (rhs != null && HasSideEffects(rhs))
                            {
                                if (NeedsParenthesesForReplacement(node, rhs))
                                {
                                    var grp = new AstNode("Grouped", rhs.Line, rhs.Column);
                                    grp.AddChild(rhs);
                                    node.ReplaceChild(i, grp);
                                    PruneUnusedLocals(grp, reads, dynamicPrefixes, protectedVars, rootNode);
                                }
                                else
                                {
                                    node.ReplaceChild(i, rhs);
                                    PruneUnusedLocals(rhs, reads, dynamicPrefixes, protectedVars, rootNode);
                                }
                            }
                            else
                            {
                                if (IsStatementContainer(node) || IsDiscardedListItem(node))
                                {
                                    node.RemoveChild(i);
                                }
                                else
                                {
                                    if (rhs != null)
                                    {
                                        if (NeedsParenthesesForReplacement(node, rhs))
                                        {
                                            var grp = new AstNode("Grouped", rhs.Line, rhs.Column);
                                            grp.AddChild(rhs);
                                            node.ReplaceChild(i, grp);
                                            PruneUnusedLocals(grp, reads, dynamicPrefixes, protectedVars, rootNode);
                                        }
                                        else
                                        {
                                            node.ReplaceChild(i, rhs);
                                            PruneUnusedLocals(rhs, reads, dynamicPrefixes, protectedVars, rootNode);
                                        }
                                    }
                                    else
                                    {
                                        node.ReplaceChild(i, new AstNode("Omitted", child.Line, child.Column));
                                    }
                                }
                            }
                        }
                    }
                }

                if (!isPruned)
                {
                    PruneUnusedLocals(child, reads, dynamicPrefixes, protectedVars, rootNode);
                }
            }
        }

        public void Shake(AstNode node, bool isInsideFunction = false)
        {
            if (node == null) return;

            if (node.NodeType == "Class")
            {
                for (int i = node.ChildCount - 1; i >= 0; i--)
                {
                    Shake(node.GetChild(i), isInsideFunction: false);
                }
                return;
            }

            // Property bodies (`Prop => (..., x := ..., x)`, get/set) and lambdas are function scopes too: an
            // assignment in them is local, never an unused *global* assignment.
            if (node.NodeType == "Method" || node.NodeType == "Property" || node.NodeType == "FatArrow" || node.NodeType == "FatArrowBody")
            {
                isInsideFunction = true;
            }

            for (int i = node.ChildCount - 1; i >= 0; i--)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                if (child.NodeType == "Method")
                {
                    if (isInsideFunction)
                    {
                        // It's a nested function! Do NOT prune it.
                        // But shake its local variables first!
                        ShakeLocalVariables(child);
                        // But recurse inside it.
                        Shake(child, isInsideFunction: true);
                    }
                    else
                    {
                        if (!ActiveNodes.Contains(child))
                        {
                            node.RemoveChild(i);
                            PrunedMethodsCount++;
                        }
                        else
                        {
                            ShakeLocalVariables(child);
                            Shake(child, isInsideFunction: true);
                        }
                    }
                }
                else if (child.NodeType == "Class")
                {
                    if (!ActiveNodes.Contains(child))
                    {
                        node.RemoveChild(i);
                        PrunedClassesCount++;
                    }
                    else
                    {
                        ShakeClassMembers(child);
                        Shake(child, isInsideFunction: false);
                    }
                }
                else if (child.NodeType == "Declaration" || child.NodeType == "StaticAssign")
                {
                    bool isUnused = false;
                    string varName = child.Value;
                    bool isStaticOrLocal = (child.Metadata == "static" || child.Metadata == "local");
                    bool isGlobal = (child.Metadata == "global") || (!isInsideFunction && !isStaticOrLocal && (node.NodeType == "Program" || node.NodeType == "Include"));
                    // (a bare `global` / `local` / `static` sets the function's scope mode and names nothing: always kept)
                    if (string.IsNullOrEmpty(varName) && child.NodeType == "Declaration") { Shake(child, isInsideFunction); continue; }
                    if (isGlobal && _config.ShakeUnusedGlobals && !ActiveNodes.Contains(child))
                    {
                        isUnused = true;
                    }
                    else if (!isInsideFunction && _config.ShakeUnusedAssignments && !string.IsNullOrEmpty(varName) && !IsSymbolActive(varName) && !ReferencedMembers.Contains(varName))
                    {
                        isUnused = true;
                    }

                    if (isUnused)
                    {
                        if (isGlobal) PrunedGlobalsCount++;
                        else PrunedAssignmentsCount++;

                        if (HasSideEffects(child))
                        {
                            if (node.NodeType == "Class")
                            {
                                node.RemoveChild(i);
                            }
                             else if (child.ChildCount > 0)
                             {
                                var init = child.GetChild(0);
                                if (NeedsParenthesesForReplacement(node, init))
                                {
                                    var grp = new AstNode("Grouped", init.Line, init.Column);
                                    grp.AddChild(init);
                                    node.ReplaceChild(i, grp);
                                    Shake(grp, isInsideFunction);
                                }
                                else
                                {
                                    node.ReplaceChild(i, init);
                                    Shake(init, isInsideFunction);
                                }
                            }
                            else
                            {
                                if (IsStatementContainer(node))
                                {
                                    node.RemoveChild(i);
                                }
                                else
                                {
                                    node.ReplaceChild(i, new AstNode("Omitted", child.Line, child.Column));
                                }
                            }
                        }
                        else
                        {
                            if (IsStatementContainer(node) || IsDiscardedListItem(node))
                            {
                                node.RemoveChild(i);
                            }
                            else
                            {
                                if (child.ChildCount > 0)
                                {
                                    var init = child.GetChild(0);
                                    if (NeedsParenthesesForReplacement(node, init))
                                    {
                                        var grp = new AstNode("Grouped", init.Line, init.Column);
                                        grp.AddChild(init);
                                        node.ReplaceChild(i, grp);
                                        Shake(grp, isInsideFunction);
                                    }
                                    else
                                    {
                                        node.ReplaceChild(i, init);
                                        Shake(init, isInsideFunction);
                                    }
                                }
                                else
                                {
                                    node.ReplaceChild(i, new AstNode("Omitted", child.Line, child.Column));
                                }
                            }
                        }
                    }
                    else
                    {
                        Shake(child, isInsideFunction);
                    }
                }
                else if (child.NodeType == "ColonAssign" || child.NodeType == "Assign" || 
                         (child.NodeType == "BinaryExpr" && child.Value == ":="))
                {
                    var lhs = child.ChildCount > 0 ? child.GetChild(0) : null;
                    var rhs = child.ChildCount > 1 ? child.GetChild(1) : null;

                    bool isUnused = false;
                    if (!isInsideFunction && _config.ShakeUnusedAssignments && lhs != null && lhs.NodeType == "Identifier" && !MustKeepAssignment(node, child))
                    {
                        string varName = lhs.Value;
                        if (!string.IsNullOrEmpty(varName) && !IsSymbolActive(varName))
                        {
                            isUnused = true;
                        }
                    }

                    if (isUnused)
                    {
                        PrunedAssignmentsCount++;

                        if (rhs != null && HasSideEffects(rhs))
                        {
                            if (node.NodeType == "Class")
                            {
                                node.RemoveChild(i);
                            }
                            else
                            {
                                if (NeedsParenthesesForReplacement(node, rhs))
                                {
                                    var grp = new AstNode("Grouped", rhs.Line, rhs.Column);
                                    grp.AddChild(rhs);
                                    node.ReplaceChild(i, grp);
                                    Shake(grp, isInsideFunction);
                                }
                                else
                                {
                                    node.ReplaceChild(i, rhs);
                                    Shake(rhs, isInsideFunction);
                                }
                            }
                        }
                        else
                        {
                            if (IsStatementContainer(node) || IsDiscardedListItem(node))
                            {
                                node.RemoveChild(i);
                            }
                            else
                            {
                                if (rhs != null)
                                {
                                    if (NeedsParenthesesForReplacement(node, rhs))
                                    {
                                        var grp = new AstNode("Grouped", rhs.Line, rhs.Column);
                                        grp.AddChild(rhs);
                                        node.ReplaceChild(i, grp);
                                        Shake(grp, isInsideFunction);
                                    }
                                    else
                                    {
                                        node.ReplaceChild(i, rhs);
                                        Shake(rhs, isInsideFunction);
                                    }
                                }
                                else
                                {
                                    node.ReplaceChild(i, new AstNode("Omitted", child.Line, child.Column));
                                }
                            }
                        }
                    }
                    else
                    {
                        Shake(child, isInsideFunction);
                    }
                }
                else if (child.NodeType == "If" && _config.ShakeDeadBranches)
                {
                    var condNode = child.ChildCount > 0 ? child.GetChild(0) : null;
                    object condVal = _evaluator.Evaluate(condNode);
                    if (condVal is bool)
                    {
                        bool isTrue = (bool)condVal;
                        PrunedDeadBranchesCount++;
                        if (isTrue)
                        {
                            if (child.ChildCount > 2)
                            {
                                child.RemoveChild(2);
                            }
                            Shake(child, isInsideFunction);
                        }
                        else
                        {
                            if (child.ChildCount > 2 && child.GetChild(2) != null)
                            {
                                var elseNode = child.GetChild(2);
                                var elseBody = elseNode.ChildCount > 0 ? elseNode.GetChild(0) : null;
                                if (elseBody != null)
                                {
                                    node.ReplaceChild(i, elseBody);
                                    Shake(elseBody, isInsideFunction);
                                }
                                else
                                {
                                    node.RemoveChild(i);
                                }
                            }
                            else
                            {
                                node.RemoveChild(i);
                            }
                        }
                    }
                    else
                    {
                        Shake(child, isInsideFunction);
                    }
                }
                else if (child.NodeType == "Include")
                {
                    Shake(child, isInsideFunction);
                    if (child.ChildCount == 0)
                    {
                        node.RemoveChild(i);
                    }
                }
                else if (child.NodeType == "Block" && _config.OptimizeEmptyBlocks && IsStatementContainer(node) && node.NodeType != "Method" && node.NodeType != "Class")
                {
                    if (child.ChildCount == 0)
                    {
                        node.RemoveChild(i);
                        PrunedEmptyBlocksCount++;
                    }
                    else
                    {
                        Shake(child, isInsideFunction);
                    }
                }
                else
                {
                    Shake(child, isInsideFunction);
                }
            }
        }

        private void ShakeClassMembers(AstNode classNode)
        {
            if (!_config.ShakeUnusedMethods) return;

            for (int i = classNode.ChildCount - 1; i >= 0; i--)
            {
                var member = classNode.GetChild(i);
                if (member == null || member.NodeType == "Extends") continue;

                if (member.NodeType == "Method" || member.NodeType == "Property" || member.NodeType == "StaticAssign" || member.NodeType == "Declaration")
                {
                    string memberName = member.Value;
                    if (!string.IsNullOrEmpty(memberName) && ProtectedMetaMethods.Contains(memberName))
                    {
                        continue;
                    }
                    // `static types := Map(), types.CaseSense := false`: a dotted item is an initializer statement on an
                    // existing object (this.types.CaseSense := false), not a field anyone refers to by name.
                    if (member.NodeType == "StaticAssign" && !string.IsNullOrEmpty(memberName) && memberName.IndexOf('.') >= 0)
                    {
                        continue;
                    }

                    if (!ActiveNodes.Contains(member))
                    {
                        classNode.RemoveChild(i);
                        PrunedMethodsCount++;
                    }
                }
            }
        }

        private void CollectDeclarations(AstNode node, bool isInsideClass, ClassInfo currentClass, bool isInsideFunction = false)
        {
            if (node == null) return;

            if (node.NodeType == "Class")
            {
                string className = node.Value;
                var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
                string baseClass = extendsNode != null ? extendsNode.Value : null;

                var classInfo = new ClassInfo
                {
                    Node = node,
                    Name = className,
                    BaseClass = baseClass
                };

                if (!string.IsNullOrEmpty(className))
                {
                    Classes[className] = classInfo;
                }

                foreach (var child in node.ChildNodes)
                {
                    if (child == null || child.NodeType == "Extends") continue;
                    
                    if (child.NodeType == "Method")
                    {
                        if (!string.IsNullOrEmpty(child.Value))
                        {
                            if (classInfo.Methods.ContainsKey(child.Value)) classInfo.SameNameMembers.Add(child);
                            else classInfo.Methods[child.Value] = child;
                        }
                    }
                    else if (child.NodeType == "Property")
                    {
                        if (!string.IsNullOrEmpty(child.Value))
                        {
                            if (classInfo.Properties.ContainsKey(child.Value)) classInfo.SameNameMembers.Add(child);
                            else classInfo.Properties[child.Value] = child;
                        }
                    }
                    else if (child.NodeType == "StaticAssign" || child.NodeType == "Declaration")
                    {
                        classInfo.StaticFields.Add(child);
                    }
                    
                    CollectDeclarations(child, isInsideClass: true, currentClass: classInfo, isInsideFunction: false);
                }
                return;
            }

            if (node.NodeType == "Method")
            {
                if (!isInsideFunction)
                {
                    if (!isInsideClass)
                    {
                        string funcName = node.Value;
                        if (!string.IsNullOrEmpty(funcName))
                        {
                            Functions[funcName] = node;
                        }
                    }
                }
                foreach (var child in node.ChildNodes)
                {
                    CollectDeclarations(child, isInsideClass: false, currentClass: null, isInsideFunction: true);
                }
                return;
            }

            // A hotkey / hotstring `{ ... }` body is a function: what it defines is local to it. (A function
            // definition written right after the label — `~ScrollLock::` + `Toggle(*) {` — stays global.)
            if ((node.NodeType == "Hotkey" || node.NodeType == "Hotstring") && !isInsideFunction)
            {
                foreach (var child in node.ChildNodes)
                    CollectDeclarations(child, isInsideClass, currentClass, isInsideFunction: child != null && child.NodeType == "Block");
                return;
            }

            if (node.NodeType == "Declaration" || node.NodeType == "StaticAssign")
            {
                bool isGlobal = (node.Metadata == "global") || (!isInsideClass && !isInsideFunction && node.Metadata != "static" && node.Metadata != "local");
                if (isGlobal)
                {
                    string varName = node.Value;
                    if (!string.IsNullOrEmpty(varName))
                    {
                        List<AstNode> list;
                        if (!Globals.TryGetValue(varName, out list))
                        {
                            list = new List<AstNode>();
                            Globals[varName] = list;
                        }
                        list.Add(node);
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                CollectDeclarations(child, isInsideClass, currentClass, isInsideFunction);
            }
        }

        private void FindEntryPoints(AstNode node, List<AstNode> entryPoints)
        {
            if (node == null) return;

            if (node.NodeType == "Program" || node.NodeType == "Include")
            {
                bool isMainFile = (node.NodeType == "Program");
                foreach (var child in node.ChildNodes)
                {
                    if (child == null) continue;

                    if (!_config.ShakeMainFileDeclarations && isMainFile)
                    {
                        if (child.NodeType == "Method" || child.NodeType == "Class")
                        {
                            entryPoints.Add(child);
                            continue;
                        }
                    }

                    if (!_config.ShakeLibraryDeclarations && !isMainFile)
                    {
                        if (child.NodeType == "Method" || child.NodeType == "Class")
                        {
                            entryPoints.Add(child);
                            continue;
                        }
                    }

                    if (child.NodeType != "Method" && child.NodeType != "Class")
                    {
                        entryPoints.Add(child);
                    }
                }
                
                foreach (var child in node.ChildNodes)
                {
                    FindEntryPoints(child, entryPoints);
                }
                return;
            }

            if (node.NodeType == "Hotkey" || node.NodeType == "Hotstring")
            {
                entryPoints.Add(node);
            }

            // Do not recurse into Method or Class bodies to collect entry points
            if (node.NodeType != "Method" && node.NodeType != "Class")
            {
                foreach (var child in node.ChildNodes)
                {
                    FindEntryPoints(child, entryPoints);
                }
            }
        }

        private void FindClassesWithStaticNew(AstNode node, List<AstNode> entryPoints)
        {
            if (node == null) return;

            if (node.NodeType == "Class")
            {
                bool hasStaticNew = false;
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "Method" &&
                        string.Equals(child.Value, "__New", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(child.Metadata, "static", StringComparison.OrdinalIgnoreCase))
                    {
                        hasStaticNew = true;
                        break;
                    }
                }
                if (hasStaticNew)
                {
                    entryPoints.Add(node);
                }

                // A static initializer that does something runs when the script loads, whether or not the class is
                // ever named again: `class AxRow { static _reg := AxRich.Register(...) }` registers itself. Such a
                // class and those initializers are entry points.
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "StaticAssign" && child.Metadata == "static"
                        && child.ChildCount > 0 && HasLoadTimeEffect(child.GetChild(0)))
                    {
                        if (!hasStaticNew) { entryPoints.Add(node); hasStaticNew = true; }
                        entryPoints.Add(child);
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                FindClassesWithStaticNew(child, entryPoints);
            }
        }

        // Built-ins that only compute a value: calling them in an initializer has no effect worth keeping a class for.
        private static readonly HashSet<string> PureBuiltins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Map", "Array", "Object", "Buffer", "Format", "StrSplit", "StrReplace", "SubStr", "StrLen", "StrLower",
            "StrUpper", "Trim", "LTrim", "RTrim", "InStr", "RegExMatch", "RegExReplace", "Chr", "Ord", "Round", "Abs",
            "Floor", "Ceil", "Min", "Max", "Mod", "Integer", "Float", "String", "Number", "Type", "IsSet", "IsObject",
            "IsNumber", "IsInteger", "IsFloat", "HasProp", "HasMethod", "ObjOwnPropCount", "Sqrt", "Exp", "Log", "Ln"
        };

        /// <summary>True when evaluating the expression does something beyond computing a value (calls anything but
        /// a pure built-in, assigns). Lambda bodies are not evaluated by the expression itself.</summary>
        private bool HasLoadTimeEffect(AstNode n)
        {
            if (n == null) return false;
            if (n.NodeType == "FatArrow" || n.NodeType == "Method") return false;
            if (n.NodeType == "Call")
            {
                var callee = n.ChildCount > 0 ? n.GetChild(0) : null;
                if (!(callee != null && callee.NodeType == "Identifier" && PureBuiltins.Contains(callee.Value ?? ""))) return true;
            }
            if (n.NodeType == "BinaryExpr" && TokenKinds_IsAssignOp(n.Value)) return true;
            if ((n.NodeType == "PostfixExpr" || n.NodeType == "UnaryExpr") && (n.Value == "++" || n.Value == "--")) return true;
            foreach (var c in n.ChildNodes)
                if (HasLoadTimeEffect(c)) return true;
            return false;
        }

        private static bool TokenKinds_IsAssignOp(string op)
        {
            switch (op)
            {
                case ":=": case "+=": case "-=": case "*=": case "/=": case "//=": case ".=": case "|=": case "&=":
                case "^=": case "<<=": case ">>=": case ">>>=": case "??=":
                    return true;
            }
            return false;
        }

        private void ScanNodeForReferences(AstNode node, Queue<AstNode> scanQueue)
        {
            if (node == null) return;
            ScanSubtree(node, node, scanQueue);
        }

        private void ScanSubtree(AstNode node, AstNode rootScan, Queue<AstNode> scanQueue)
        {
            if (node == null) return;

            if (node != rootScan)
            {
                if (node.NodeType == "Class")
                {
                    return;
                }
                if (node.NodeType == "Method")
                {
                    ActiveNodes.Add(node);
                    // Do not return; continue scanning nested function's body
                }
            }

            if (node.NodeType == "Concat" || (node.NodeType == "BinaryExpr" && node.Value == "."))
            {
                for (int cIdx = 0; cIdx < node.ChildCount; cIdx++)
                {
                    var child = node.GetChild(cIdx);
                    if (child != null && (child.NodeType == "Identifier" || child.NodeType == "String"))
                    {
                        string val = child.Value;
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (child.NodeType == "String" && val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                            {
                                val = val.Substring(1, val.Length - 2);
                            }
                            if (!val.StartsWith("%") && !val.EndsWith("%"))
                            {
                                AddDynamicPrefix(val, scanQueue);
                            }
                        }
                    }
                }
            }

            if (node.NodeType == "ColonAssign" || node.NodeType == "Assign" || (node.NodeType == "BinaryExpr" && node.Value == ":="))
            {
                var left = node.ChildCount > 0 ? node.GetChild(0) : null;
                var right = node.ChildCount > 1 ? node.GetChild(1) : null;

                if (left != null)
                {
                    // A plain target is a write, not a read. A dynamic target (`w%n% := 5`) still reads n and may be
                    // any variable starting with its prefix, so it is scanned like a reference.
                    if (left.NodeType != "Identifier" || (left.Value ?? "").IndexOf('%') >= 0)
                    {
                        ScanSubtree(left, rootScan, scanQueue);
                    }
                }
                if (right != null)
                {
                    ScanSubtree(right, rootScan, scanQueue);
                }
                return;
            }

            if (node.NodeType == "If" && _config.ShakeDeadBranches)
            {
                var cond = node.ChildCount > 0 ? node.GetChild(0) : null;
                var body = node.ChildCount > 1 ? node.GetChild(1) : null;
                var elseNode = node.ChildCount > 2 ? node.GetChild(2) : null;

                object evaluated = _evaluator.Evaluate(cond);
                if (evaluated is bool)
                {
                    // The condition stays in the output (`if dev` is not rewritten), so what it reads stays live even when
                    // constant propagation already knows its value; otherwise `dev := true` is pruned and dev is unset.
                    if (cond != null) ScanSubtree(cond, rootScan, scanQueue);
                    bool isTrue = (bool)evaluated;
                    if (isTrue)
                    {
                        if (body != null)
                            ScanSubtree(body, rootScan, scanQueue);
                    }
                    else
                    {
                        if (elseNode != null)
                            ScanSubtree(elseNode, rootScan, scanQueue);
                    }
                    return;
                }
            }

            if (node.NodeType == "Call")
            {
                var callee = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (callee != null)
                {
                    if (callee.NodeType == "Identifier" && (callee.Value ?? "").IndexOf('%') < 0)
                    {
                        AddReferencedSymbol(callee.Value, scanQueue);
                    }
                    else if (callee.NodeType == "Identifier")
                    {
                        ScanSubtree(callee, rootScan, scanQueue); // `%fnName%(...)`: reads fnName
                    }
                    else if (callee.NodeType == "Member")
                    {
                        var obj = callee.ChildCount > 0 ? callee.GetChild(0) : null;
                        ScanSubtree(obj, rootScan, scanQueue);
                        
                        string memberName = callee.Value;
                        if (!string.IsNullOrEmpty(memberName))
                        {
                            ReferenceMemberName(memberName, scanQueue);
                        }
                    }
                    else
                    {
                        ScanSubtree(callee, rootScan, scanQueue);
                    }
                }

                var args = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (args != null)
                {
                    ScanSubtree(args, rootScan, scanQueue);
                }
                return;
            }

            if (node.NodeType == "Member")
            {
                var obj = node.ChildCount > 0 ? node.GetChild(0) : null;
                ScanSubtree(obj, rootScan, scanQueue);

                string memberName = node.Value;
                if (!string.IsNullOrEmpty(memberName))
                {
                    ReferenceMemberName(memberName, scanQueue);
                }
                return;
            }

            if (node.NodeType == "Index")
            {
                for (int i = 0; i < node.ChildCount; i++)
                {
                    ScanSubtree(node.GetChild(i), rootScan, scanQueue);
                }
                AddReferencedMember("__Item", scanQueue);
                return;
            }

            if (node.NodeType == "Identifier")
            {
                string name = node.Value;
                string scanPrefix;
                List<string> scanInner;
                if (TryDynamicName(name, out scanPrefix, out scanInner))
                {
                    foreach (var n in scanInner) foreach (var d in DerefNames(n)) AddReferencedSymbol(d, scanQueue);
                    if (scanPrefix.Length > 0) AddDynamicPrefix(scanPrefix, scanQueue, exact: true);
                    return;
                }
                if (!string.IsNullOrEmpty(name))
                {
                    if (name.StartsWith("%") && name.EndsWith("%") && name.Length > 2)
                    {
                        string inner = name.Substring(1, name.Length - 2);
                        foreach (var d in DerefNames(inner)) AddReferencedSymbol(d, scanQueue);
                    }
                    else
                    {
                        AddReferencedSymbol(name, scanQueue);
                    }
                }
                return;
            }

            if (node.NodeType == "String" && _config.TraceStringReferences)
            {
                string val = node.Value;
                if (val != null)
                {
                    if (val.Length >= 2 && ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'"))))
                    {
                        val = val.Substring(1, val.Length - 2);
                    }
                    AddReferencedSymbol(val, scanQueue);
                }
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                ScanSubtree(child, rootScan, scanQueue);
            }
        }

        private void ActivateClass(ClassInfo classInfo, Queue<AstNode> scanQueue)
        {
            if (ActiveNodes.Add(classInfo.Node))
            {
                foreach (var metaName in ProtectedMetaMethods)
                {
                    AstNode metaMethod;
                    if (classInfo.Methods.TryGetValue(metaName, out metaMethod))
                    {
                        if (ActiveNodes.Add(metaMethod))
                            scanQueue.Enqueue(metaMethod);
                    }
                    if (classInfo.Properties.TryGetValue(metaName, out metaMethod))
                    {
                        if (ActiveNodes.Add(metaMethod))
                            scanQueue.Enqueue(metaMethod);
                    }
                    ActivateSameName(classInfo, metaName, scanQueue);
                }
                if (!string.IsNullOrEmpty(classInfo.BaseClass))
                {
                    AddReferencedSymbol(classInfo.BaseClass, scanQueue);
                }
                if (!_config.ShakeUnusedMethods)
                {
                    foreach (var method in classInfo.Methods.Values)
                    {
                        if (ActiveNodes.Add(method))
                            scanQueue.Enqueue(method);
                    }
                    foreach (var prop in classInfo.Properties.Values)
                    {
                        if (ActiveNodes.Add(prop))
                            scanQueue.Enqueue(prop);
                    }
                    foreach (var other in classInfo.SameNameMembers)
                    {
                        if (ActiveNodes.Add(other))
                            scanQueue.Enqueue(other);
                    }
                }
                foreach (var memberName in ReferencedMembers)
                {
                    ActivateClassMember(classInfo, memberName, scanQueue);
                }
            }
        }

        private void AddReferencedSymbol(string symbol, Queue<AstNode> scanQueue)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            if (!ReferencedSymbols.Add(symbol)) return;

            AstNode funcNode;
            if (Functions.TryGetValue(symbol, out funcNode))
            {
                if (ActiveNodes.Add(funcNode))
                {
                    scanQueue.Enqueue(funcNode);
                }
            }

            ClassInfo classInfo;
            if (Classes.TryGetValue(symbol, out classInfo))
            {
                ActivateClass(classInfo, scanQueue);
            }

            List<AstNode> globalNodes;
            if (Globals.TryGetValue(symbol, out globalNodes))
            {
                foreach (var globalNode in globalNodes)
                {
                    if (ActiveNodes.Add(globalNode))
                    {
                        scanQueue.Enqueue(globalNode);
                    }
                }
            }

            AddReferencedMember(symbol, scanQueue);
        }

        /// <summary>
        /// Keeps every global whose name could be produced from <paramref name="prefix"/>. Heuristic prefixes (string /
        /// identifier fragments seen in concatenations) need at least 2 characters and match either end; an exact prefix
        /// from a dynamic name (`b%n%` → "b") is real semantics, so any length counts and it matches the start only.
        /// </summary>
        private void AddDynamicPrefix(string prefix, Queue<AstNode> scanQueue, bool exact = false)
        {
            if (string.IsNullOrEmpty(prefix) || (!exact && prefix.Length < 2)) return;
            if (exact) { if (!ExactDynamicPrefixes.Add(prefix)) return; }
            else if (!ReferencedDynamicPrefixes.Add(prefix)) return;

            // Search Globals
            foreach (var kv in Globals)
            {
                string name = kv.Key;
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    (!exact && name.EndsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var globalNode in kv.Value)
                    {
                        if (ActiveNodes.Add(globalNode))
                        {
                            scanQueue.Enqueue(globalNode);
                        }
                    }
                }
            }

            // Search Functions
            foreach (var kv in Functions)
            {
                string name = kv.Key;
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (ActiveNodes.Add(kv.Value))
                    {
                        scanQueue.Enqueue(kv.Value);
                    }
                }
            }

            // Search Classes
            foreach (var kv in Classes)
            {
                string name = kv.Key;
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    ActivateClass(kv.Value, scanQueue);
                }
            }
        }

        private void AddReferencedMember(string memberName, Queue<AstNode> scanQueue)
        {
            if (string.IsNullOrEmpty(memberName)) return;
            if (!ReferencedMembers.Add(memberName)) return;

            // Activate top-level functions, classes, and globals with this name
            AstNode funcNode;
            if (Functions.TryGetValue(memberName, out funcNode))
            {
                if (ActiveNodes.Add(funcNode))
                {
                    scanQueue.Enqueue(funcNode);
                }
            }

            ClassInfo classInfo;
            if (Classes.TryGetValue(memberName, out classInfo))
            {
                ActivateClass(classInfo, scanQueue);
            }

            List<AstNode> globalNodes;
            if (Globals.TryGetValue(memberName, out globalNodes))
            {
                foreach (var globalNode in globalNodes)
                {
                    if (ActiveNodes.Add(globalNode))
                    {
                        scanQueue.Enqueue(globalNode);
                    }
                }
            }

            foreach (var kv in Classes)
            {
                var targetClassInfo = kv.Value;
                if (ActiveNodes.Contains(targetClassInfo.Node))
                {
                    ActivateClassMember(targetClassInfo, memberName, scanQueue);
                }
            }
        }

        /// <summary>
        /// A member name as written after a dot. A dynamic one (obj.%name%, obj.On%evt%) reads the variables in its
        /// derefs and may reach any member starting with its literal prefix (see ActivateClassMember).
        /// </summary>
        private void ReferenceMemberName(string memberName, Queue<AstNode> scanQueue)
        {
            if (memberName.IndexOf('%') >= 0)
                foreach (var d in MemberDerefNames(memberName)) AddReferencedSymbol(d, scanQueue);
            AddReferencedMember(memberName, scanQueue);
        }

        private void ActivateSameName(ClassInfo classInfo, string memberName, Queue<AstNode> scanQueue)
        {
            foreach (var other in classInfo.SameNameMembers)
                if (string.Equals(other.Value, memberName, StringComparison.OrdinalIgnoreCase) && ActiveNodes.Add(other))
                    scanQueue.Enqueue(other);
        }

        private void ActivateClassMember(ClassInfo classInfo, string memberName, Queue<AstNode> scanQueue)
        {
            ActivateSameName(classInfo, memberName, scanQueue);
            int pct = memberName.IndexOf('%');
            if (pct >= 0)
            {
                // Dynamic member name: keep everything its literal prefix could produce.
                string prefix = memberName.Substring(0, pct);
                foreach (var m in classInfo.Methods)
                    if (m.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && ActiveNodes.Add(m.Value)) scanQueue.Enqueue(m.Value);
                foreach (var p in classInfo.Properties)
                    if (p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && ActiveNodes.Add(p.Value)) scanQueue.Enqueue(p.Value);
                foreach (var o in classInfo.SameNameMembers)
                    if ((o.Value ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && ActiveNodes.Add(o)) scanQueue.Enqueue(o);
                foreach (var field in classInfo.StaticFields)
                    if ((field.Value ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && ActiveNodes.Add(field)) scanQueue.Enqueue(field);
                return;
            }
            AstNode methodNode;
            if (classInfo.Methods.TryGetValue(memberName, out methodNode))
            {
                if (ActiveNodes.Add(methodNode))
                {
                    scanQueue.Enqueue(methodNode);
                }
            }
            AstNode propNode;
            if (classInfo.Properties.TryGetValue(memberName, out propNode))
            {
                if (ActiveNodes.Add(propNode))
                {
                    scanQueue.Enqueue(propNode);
                }
            }
            foreach (var field in classInfo.StaticFields)
            {
                if (string.Equals(field.Value, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    if (ActiveNodes.Add(field))
                    {
                        scanQueue.Enqueue(field);
                    }
                }
            }
        }

        // Folding follows AhkConst: Integer/Float/String values with AHK's own rules; anything uncertain stays as written.
        private object GetConstantValue(AstNode node)
        {
            return AhkConst.Get(node);
        }

        private AstNode CreateConstantNode(object val, int line, int col)
        {
            return AhkConst.ToNode(val, line, col);
        }

        private void FoldConstants(AstNode node)
        {
            if (node == null) return;

            for (int i = 0; i < node.ChildCount; i++)
            {
                FoldConstants(node.GetChild(i));
            }

            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                if (child.NodeType == "Grouped" && child.ChildCount == 1)
                {
                    var inner = child.GetChild(0);
                    if (inner != null && (inner.NodeType == "Number" || inner.NodeType == "String" || inner.NodeType == "Identifier" || inner.NodeType == "Literal"))
                    {
                        node.ReplaceChild(i, inner);
                        child = inner;
                    }
                }

                AstNode replacement = null;
                if (child.NodeType == "BinaryExpr" && child.ChildCount == 2)
                {
                    object lVal = GetConstantValue(child.GetChild(0));
                    object rVal = GetConstantValue(child.GetChild(1));
                    if (lVal != null && rVal != null)
                    {
                        if (child.Value == ".")
                        {
                            string ls = AhkConst.ToText(lVal), rs = AhkConst.ToText(rVal);
                            if (ls != null && rs != null) replacement = CreateConstantNode(ls + rs, child.Line, child.Column);
                        }
                        else if (child.Value == "+" || child.Value == "-" || child.Value == "*" || child.Value == "/")
                        {
                            replacement = CreateConstantNode(AhkConst.Arith(child.Value, lVal, rVal), child.Line, child.Column);
                        }
                    }
                }
                else if (child.NodeType == "Concat")
                {
                    var parts = new List<string>();
                    for (int k = 0; k < child.ChildCount; k++)
                    {
                        string text = AhkConst.ToText(GetConstantValue(child.GetChild(k)));
                        if (text == null) { parts = null; break; }
                        parts.Add(text);
                    }
                    if (parts != null && parts.Count > 0) replacement = CreateConstantNode(string.Concat(parts), child.Line, child.Column);
                }
                else if (child.NodeType == "LogicalNot" || (child.NodeType == "UnaryExpr" && (child.Value == "!" || string.Equals(child.Value, "not", StringComparison.OrdinalIgnoreCase))))
                {
                    bool? truth = child.ChildCount > 0 ? AhkConst.Truthy(GetConstantValue(child.GetChild(0))) : null;
                    if (truth != null) replacement = CreateConstantNode(!truth.Value, child.Line, child.Column);
                }

                if (replacement != null)
                {
                    node.ReplaceChild(i, replacement);
                    FoldedConstantsCount++;
                }
            }
        }    }
}
