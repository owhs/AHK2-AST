using System;
using System.Collections.Generic;
using System.IO;

public static class AstSerializer
{
    private const int FORMAT_VERSION = 1;
    private const int MAGIC_NUMBER = 0x41535431; // "AST1"

    /// <summary>
    /// Saves an AST tree to a binary file at blazing speeds.
    /// </summary>
    public static void Save(AstNode root, string filePath)
    {
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(MAGIC_NUMBER);
            bw.Write(FORMAT_VERSION);
            WriteNode(bw, root);
        }
    }

    /// <summary>
    /// Loads an AST tree from a binary file at blazing speeds.
    /// </summary>
    public static AstNode Load(string filePath)
    {
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
        using (var br = new BinaryReader(fs))
        {
            int magic = br.ReadInt32();
            if (magic != MAGIC_NUMBER)
                throw new InvalidDataException("Invalid AST file format.");
            
            int version = br.ReadInt32();
            if (version != FORMAT_VERSION)
                throw new InvalidDataException("Unsupported AST file version.");

            return ReadNode(br);
        }
    }

    private static void WriteNode(BinaryWriter bw, AstNode node)
    {
        bw.Write(node.NodeType ?? "");
        bw.Write(node.Value ?? "");
        bw.Write(node.Line);
        bw.Write(node.Column);
        bw.Write(node.EndLine);
        bw.Write(node.EndColumn);
        bw.Write(node.Metadata ?? "");
        bw.Write(node.IsHealed);
        
        bw.Write(node.ChildCount);
        foreach (var child in node.ChildNodes)
        {
            WriteNode(bw, child);
        }
    }

    private static AstNode ReadNode(BinaryReader br)
    {
        string type = br.ReadString();
        string val = br.ReadString();
        int line = br.ReadInt32();
        int col = br.ReadInt32();
        
        var node = new AstNode(type, line, col);
        node.Value = val;
        node.EndLine = br.ReadInt32();
        node.EndColumn = br.ReadInt32();
        node.Metadata = br.ReadString();
        node.IsHealed = br.ReadBoolean();
        
        int childCount = br.ReadInt32();
        for (int i = 0; i < childCount; i++)
        {
            node.AddChild(ReadNode(br));
        }
        
        return node;
    }
}
