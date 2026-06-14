using System;
using System.Reflection;

public static class UiLoader
{
    private static readonly string[] EmbeddedAssemblies = new string[]
    {
        "DiffPlex",
        "FastColoredTextBox",
        "WeifenLuo.WinFormsUI.Docking",
        "WeifenLuo.WinFormsUI.Docking.ThemeVS2015"
    };

    public static void Initialize()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        foreach (var embed in EmbeddedAssemblies)
        {
            if (name.Equals(embed, StringComparison.OrdinalIgnoreCase))
            {
                var resourceName = "ui.resources." + name + ".dll";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    var data = new byte[stream.Length];
                    int bytesRead = 0;
                    int offset = 0;
                    while (offset < data.Length && (bytesRead = stream.Read(data, offset, data.Length - offset)) > 0)
                    {
                        offset += bytesRead;
                    }
                    return Assembly.Load(data);
                }
            }
        }
        return null;
    }
}
