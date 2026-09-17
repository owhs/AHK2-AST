using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public class AstNode
{
    private List<AstNode> _children = new List<AstNode>();

    public string NodeType { get; set; }
    public string Value { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string Metadata { get; set; }  // Warnings, healed flags, etc.
    public bool IsError { get { return NodeType == "Error"; } }
    public bool IsHealed { get; set; }
    public int ChildCount { get { return _children.Count; } }
    public AstNode Parent { get; set; }
    /// <summary>Set by the parser on a statement's leading operand: what followed it in the source (see
    /// StatementHead). The emitter leaves such a head exactly as written while that is still what follows it.</summary>
    public string SourceHeadFollower { get; set; }

    // Source range (see SourceRanges): the whole text the node spans in the ORIGINAL file it was parsed from,
    // [StartOffset, EndOffset) as character offsets, and the same as 1-based line/column (end = just past the last
    // character). -1 / 0 when unknown (nodes made by transforms). Line/Column above stay the anchor token's logical
    // position and EndLine/EndColumn the emitter's layout hints; neither is changed by ranges.
    public int StartOffset = -1;
    public int EndOffset = -1;
    public int RangeStartLine, RangeStartColumn, RangeEndLine, RangeEndColumn;
    public bool HasRange { get { return StartOffset >= 0 && EndOffset >= StartOffset; } }
    /// <summary>On an Include node the engine followed: the file its children were parsed from (their ranges are
    /// offsets in that file; the Include's own range is its #Include line). Null everywhere else.</summary>
    public string ChildFile;

    public void CopyRangeFrom(AstNode other)
    {
        if (other == null) return;
        StartOffset = other.StartOffset; EndOffset = other.EndOffset;
        RangeStartLine = other.RangeStartLine; RangeStartColumn = other.RangeStartColumn;
        RangeEndLine = other.RangeEndLine; RangeEndColumn = other.RangeEndColumn;
    }

    public AstNode(string nodeType, int line, int col)
    {
        NodeType = nodeType;
        Line = line;
        Column = col;
        Value = "";
        Metadata = "";
    }

    public void AddChild(AstNode child)
    {
        if (child != null)
        {
            child.Parent = this;
            _children.Add(child);
        }
    }
    public AstNode GetChild(int index) { return _children[index]; }
    // (a fresh copy each time: caching it was measured, bench --ab, and made no difference)
    public AstNode[] ChildNodes { get { return _children.ToArray(); } }

    public void RemoveChild(int index)
    {
        if (index >= 0 && index < _children.Count)
        {
            _children[index].Parent = null;
        }
        _children.RemoveAt(index);
    }
    public void InsertChild(int index, AstNode child)
    {
        if (child != null)
        {
            child.Parent = this;
        }
        _children.Insert(index, child);
    }
    public void ReplaceChild(int index, AstNode child)
    {
        if (index >= 0 && index < _children.Count)
        {
            _children[index].Parent = null;
        }
        if (child != null)
        {
            child.Parent = this;
        }
        _children[index] = child;
    }
    public void ClearChildren()
    {
        foreach (var child in _children)
        {
            if (child != null) child.Parent = null;
        }
        _children.Clear();
    }
    public void SetChildren(IEnumerable<AstNode> children)
    {
        var incoming = children != null ? children.ToList() : null; // may be this node's own snapshot
        _children.Clear();
        if (incoming != null)
        {
            foreach (var child in incoming)
            {
                if (child != null)
                {
                    child.Parent = this;
                    _children.Add(child);
                }
            }
        }
    }

    /// <summary>Deep clone this node and all children.</summary>
    public AstNode Clone()
    {
        var clone = new AstNode(NodeType, Line, Column)
        {
            Value = Value, Metadata = Metadata, IsHealed = IsHealed,
            EndLine = EndLine, EndColumn = EndColumn,
            SourceHeadFollower = SourceHeadFollower // (was dropped: cached include trees lost their source heads)
        };
        clone.CopyRangeFrom(this);
        clone.ChildFile = ChildFile;
        foreach (var child in _children)
            clone.AddChild(child.Clone());
        return clone;
    }

    public override string ToString()
    {
        return string.Format("{0}({1}:{2}){3}", NodeType, Line, Column,
            string.IsNullOrEmpty(Value) ? "" : " = " + Value);
    }
}
