using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;

namespace AHK2AST.Plugins
{
    public class AutoFixConfig
    {
        [Category("Autofix Options"), DisplayName("Fix Same-Line If"), Description("Convert single-line/same-line if statements like 'if (cond) stmt' to proper blocks 'if (cond) {\n    stmt\n}'.")]
        public bool FixSameLineIf { get; set; }

        [Category("Autofix Options"), DisplayName("Fix Same-Line While"), Description("Convert same-line while statements like 'while (cond) stmt' to proper blocks.")]
        public bool FixSameLineWhile { get; set; }

        [Category("Autofix Options"), DisplayName("Fix Same-Line For"), Description("Convert same-line for loops like 'for k,v in obj stmt' to proper blocks.")]
        public bool FixSameLineFor { get; set; }

        [Category("Autofix Options"), DisplayName("Fix Same-Line Loop"), Description("Convert same-line loop statements to proper blocks.")]
        public bool FixSameLineLoop { get; set; }

        [Category("Autofix Options"), DisplayName("Fix Equals Assignment"), Description("Convert standalone equality comparison statements like 'var = expr' to assignment 'var := expr'.")]
        public bool FixEqualsAssignment { get; set; }

        [Category("Autofix Options"), DisplayName("Fix MsgBox Legacy Style"), Description("Fix legacy MsgBox parameter order like 'MsgBox, Options, Title, Text' to v2 style 'MsgBox(Text, Title, Options)'.")]
        public bool FixMsgBoxLegacyStyle { get; set; }

        [Category("Autofix Options"), DisplayName("Fix ComObjCreate"), Description("Convert legacy 'ComObjCreate(clsid)' to v2 'ComObject(clsid)'.")]
        public bool FixComObjCreate { get; set; }

        [Category("Autofix Options"), DisplayName("Fix ComObjUnwrap"), Description("Convert legacy 'ComObjUnwrap(obj)' to v2 'ComObjValue(obj)'.")]
        public bool FixComObjUnwrap { get; set; }

        [Category("Autofix Options"), DisplayName("Fix ComObjParameter"), Description("Convert legacy 'ComObjParameter(type, val)' to v2 'ComValue(type, val)'.")]
        public bool FixComObjParameter { get; set; }

        [Category("Autofix Options"), DisplayName("Fix A_LoopFileLongPath"), Description("Convert legacy 'A_LoopFileLongPath' to v2 'A_LoopFileFullPath'.")]
        public bool FixLoopFileLongPath { get; set; }

        public AutoFixConfig()
        {
            FixSameLineIf = true;
            FixSameLineWhile = true;
            FixSameLineFor = true;
            FixSameLineLoop = true;
            FixEqualsAssignment = true;
            FixMsgBoxLegacyStyle = true;
            FixComObjCreate = true;
            FixComObjUnwrap = true;
            FixComObjParameter = true;
            FixLoopFileLongPath = true;
        }
    }

    public class AutoFixPlugin : IFlowPlugin
    {
        public string Name { get { return "Transform.Auto-Fixes"; } }
        public string Target { get; set; }

        public string Category { get { return "Transform"; } }
        public string StepTitle { get { return "Auto-Fixes"; } }
        public string Icon { get { return "🔧"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(AutoFixConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (AutoFixConfig)config; }

        public AutoFixConfig Config { get; set; }

        private int _fixedSameLineIfCount = 0;
        private int _fixedEqualsAssignCount = 0;
        private int _fixedMsgBoxCount = 0;
        private int _fixedComObjCreateCount = 0;
        private int _fixedComObjUnwrapCount = 0;
        private int _fixedComObjParameterCount = 0;
        private int _fixedLoopFileLongPathCount = 0;

        public AutoFixPlugin()
        {
            Config = new AutoFixConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            _fixedSameLineIfCount = 0;
            _fixedEqualsAssignCount = 0;
            _fixedMsgBoxCount = 0;
            _fixedComObjCreateCount = 0;
            _fixedComObjUnwrapCount = 0;
            _fixedComObjParameterCount = 0;
            _fixedLoopFileLongPathCount = 0;

            ApplyAutoFixes(root);

            PipelineLogger.Log("  🔧 Transform.Auto-Fixes Summary:");
            if (_fixedSameLineIfCount > 0)
                PipelineLogger.Log("    - Wrapped {0} same-line control flow statements in blocks.", _fixedSameLineIfCount);
            if (_fixedEqualsAssignCount > 0)
                PipelineLogger.Log("    - Converted {0} standalone comparison (=) statements to assignments (:=).", _fixedEqualsAssignCount);
            if (_fixedMsgBoxCount > 0)
                PipelineLogger.Log("    - Fixed {0} legacy MsgBox statements.", _fixedMsgBoxCount);
            if (_fixedComObjCreateCount > 0)
                PipelineLogger.Log("    - Converted {0} ComObjCreate calls to ComObject.", _fixedComObjCreateCount);
            if (_fixedComObjUnwrapCount > 0)
                PipelineLogger.Log("    - Converted {0} ComObjUnwrap calls to ComObjValue.", _fixedComObjUnwrapCount);
            if (_fixedComObjParameterCount > 0)
                PipelineLogger.Log("    - Converted {0} ComObjParameter calls to ComValue.", _fixedComObjParameterCount);
            if (_fixedLoopFileLongPathCount > 0)
                PipelineLogger.Log("    - Replaced {0} occurrences of A_LoopFileLongPath with A_LoopFileFullPath.", _fixedLoopFileLongPathCount);

            return root;
        }

        private void ApplyAutoFixes(AstNode node)
        {
            if (node == null) return;

            // 1. Process children
            for (int i = 0; i < node.ChildCount; i++)
            {
                var child = node.GetChild(i);
                if (child == null) continue;

                // Autofix Same-Line If/While/For/Loop
                if (child.NodeType == "If" && Config.FixSameLineIf)
                {
                    WrapSameLineBody(child, 1); // child at index 1 is the then-body
                    // also check else branch if it exists and is same line
                    if (child.ChildCount > 2 && child.GetChild(2) != null && child.GetChild(2).NodeType == "Else")
                    {
                        WrapSameLineBody(child.GetChild(2), 0); // Else body is child 0
                    }
                }
                else if (child.NodeType == "While" && Config.FixSameLineWhile)
                {
                    WrapSameLineBody(child, 1); // child 1 is body
                }
                else if (child.NodeType == "For" && Config.FixSameLineFor)
                {
                    WrapSameLineBody(child, 2); // For has: child 0 (ForVars), child 1 (Collection), child 2 (Body)
                    if (child.ChildCount > 3 && child.GetChild(3) != null && child.GetChild(3).NodeType == "Else")
                    {
                        WrapSameLineBody(child.GetChild(3), 0); // Else body
                    }
                }
                else if (child.NodeType == "Loop" && Config.FixSameLineLoop)
                {
                    int bodyIdx = -1;
                    for (int j = child.ChildCount - 1; j >= 0; j--)
                    {
                        if (child.GetChild(j) != null && child.GetChild(j).NodeType != "Until")
                        {
                            bodyIdx = j;
                            break;
                        }
                    }
                    if (bodyIdx != -1)
                    {
                        WrapSameLineBody(child, bodyIdx);
                    }
                }

                // Autofix Standalone Equality expression at statement level
                if (IsStatementContainer(node))
                {
                    if (child.NodeType == "BinaryExpr" && child.Value == "=" && Config.FixEqualsAssignment)
                    {
                        var lhs = child.ChildCount > 0 ? child.GetChild(0) : null;
                        if (lhs != null && (lhs.NodeType == "Identifier" || lhs.NodeType == "Member" || lhs.NodeType == "Index"))
                        {
                            child.Value = ":=";
                            _fixedEqualsAssignCount++;
                        }
                    }
                    else if (child.NodeType == "Grouped" && child.ChildCount > 0 && child.GetChild(0).NodeType == "BinaryExpr" && child.GetChild(0).Value == "=" && Config.FixEqualsAssignment)
                    {
                        var binExpr = child.GetChild(0);
                        var lhs = binExpr.ChildCount > 0 ? binExpr.GetChild(0) : null;
                        if (lhs != null && (lhs.NodeType == "Identifier" || lhs.NodeType == "Member" || lhs.NodeType == "Index"))
                        {
                            binExpr.Value = ":=";
                            _fixedEqualsAssignCount++;
                        }
                    }
                }

                // Autofix Legacy MsgBox: MsgBox, Options, Title, Text or MsgBox(Options, Title, Text)
                if (Config.FixMsgBoxLegacyStyle)
                {
                    if (child.NodeType == "Call")
                    {
                        var funcName = child.GetChild(0);
                        if (funcName != null && funcName.NodeType == "Identifier" && funcName.Value.Equals("MsgBox", StringComparison.OrdinalIgnoreCase))
                        {
                            var argsNode = child.ChildCount > 1 ? child.GetChild(1) : null;
                            if (argsNode != null && argsNode.NodeType == "Arguments" && argsNode.ChildCount == 3)
                            {
                                var firstArg = argsNode.GetChild(0);
                                bool firstIsNumeric = false;
                                if (firstArg.NodeType == "Number") firstIsNumeric = true;
                                else if (firstArg.NodeType == "Identifier")
                                {
                                    double dummy;
                                    if (double.TryParse(firstArg.Value, out dummy)) firstIsNumeric = true;
                                }

                                if (firstIsNumeric)
                                {
                                    var options = argsNode.GetChild(0);
                                    var title = argsNode.GetChild(1);
                                    var text = argsNode.GetChild(2);

                                    argsNode.ReplaceChild(0, text);
                                    argsNode.ReplaceChild(1, title);
                                    argsNode.ReplaceChild(2, options);
                                    _fixedMsgBoxCount++;
                                }
                            }
                        }
                    }
                    else if (child.NodeType == "MultiStatement" && child.ChildCount == 3)
                    {
                        var firstPart = child.GetChild(0);
                        if (firstPart != null && firstPart.NodeType == "Concat" && firstPart.ChildCount == 2)
                        {
                            var funcName = firstPart.GetChild(0);
                            if (funcName != null && funcName.NodeType == "Identifier" && funcName.Value.Equals("MsgBox", StringComparison.OrdinalIgnoreCase))
                            {
                                var firstArg = firstPart.GetChild(1);
                                bool firstIsNumeric = false;
                                if (firstArg.NodeType == "Number") firstIsNumeric = true;
                                else if (firstArg.NodeType == "Identifier")
                                {
                                    double dummy;
                                    if (double.TryParse(firstArg.Value, out dummy)) firstIsNumeric = true;
                                }

                                if (firstIsNumeric)
                                {
                                    var options = firstPart.GetChild(1);
                                    var title = child.GetChild(1);
                                    var text = child.GetChild(2);

                                    var msgboxIdent = new AstNode("Identifier", child.Line, child.Column);
                                    msgboxIdent.Value = "MsgBox";

                                    var argsNode = new AstNode("Arguments", child.Line, child.Column);
                                    argsNode.AddChild(text);
                                    argsNode.AddChild(title);
                                    argsNode.AddChild(options);

                                    var callNode = new AstNode("Call", child.Line, child.Column);
                                    callNode.AddChild(msgboxIdent);
                                    callNode.AddChild(argsNode);

                                    node.ReplaceChild(i, callNode);
                                    _fixedMsgBoxCount++;
                                }
                            }
                        }
                    }
                }

                // Autofix ComObjCreate, ComObjUnwrap, ComObjParameter
                if (child.NodeType == "Call")
                {
                    var funcName = child.GetChild(0);
                    if (funcName != null && funcName.NodeType == "Identifier")
                    {
                        if (Config.FixComObjCreate && funcName.Value.Equals("ComObjCreate", StringComparison.OrdinalIgnoreCase))
                        {
                            funcName.Value = "ComObject";
                            _fixedComObjCreateCount++;
                        }
                        else if (Config.FixComObjUnwrap && funcName.Value.Equals("ComObjUnwrap", StringComparison.OrdinalIgnoreCase))
                        {
                            funcName.Value = "ComObjValue";
                            _fixedComObjUnwrapCount++;
                        }
                        else if (Config.FixComObjParameter && funcName.Value.Equals("ComObjParameter", StringComparison.OrdinalIgnoreCase))
                        {
                            funcName.Value = "ComValue";
                            _fixedComObjParameterCount++;
                        }
                    }
                }

                // Autofix A_LoopFileLongPath to A_LoopFileFullPath
                if (Config.FixLoopFileLongPath && child.NodeType == "Identifier" && child.Value.Equals("A_LoopFileLongPath", StringComparison.OrdinalIgnoreCase))
                {
                    child.Value = "A_LoopFileFullPath";
                    _fixedLoopFileLongPathCount++;
                }

                // Recurse into children
                ApplyAutoFixes(child);
            }
        }

        private bool IsStatementContainer(AstNode node)
        {
            if (node == null) return false;
            string type = node.NodeType;
            return type == "Program" || type == "Block" || type == "Include" || type == "CaseBody" || type == "DefaultBody";
        }

        private void WrapSameLineBody(AstNode parent, int bodyIndex)
        {
            if (parent == null || bodyIndex < 0 || bodyIndex >= parent.ChildCount) return;
            var body = parent.GetChild(bodyIndex);
            if (body == null || body.NodeType == "Block") return;

            if (body.Line == parent.Line)
            {
                var block = new AstNode("Block", body.Line, body.Column);
                block.AddChild(body);
                parent.ReplaceChild(bodyIndex, block);
                _fixedSameLineIfCount++;
            }
        }
    }
}
