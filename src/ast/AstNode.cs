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
        _children.Clear();
        if (children != null)
        {
            foreach (var child in children)
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
            EndLine = EndLine, EndColumn = EndColumn
        };
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
