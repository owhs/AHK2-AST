using System;
using System.Collections.Generic;

/// <summary>
/// Interface for any plugin that wishes to hook into the AST engine.
/// Plugins can be used for beautification, minification, tree shaking,
/// code obfuscation, optimisations, tracing, or debugging.
/// </summary>
public interface IAstPlugin
{
    string Name { get; }
    string Target { get; set; }
    
    /// <summary>
    /// Called when the plugin is loaded into the engine.
    /// </summary>
    void Initialize(AhkAstEngine engine);
    
    /// <summary>
    /// Executes the plugin. It can return an AstNode to continue the AST pipeline,
    /// a string to output emitted code/text, or arbitrary data for queries.
    /// </summary>
    object Execute(AstNode root);
}

/// <summary>
/// Interface for traversing and visiting nodes in the AST tree.
/// </summary>
public interface IAstVisitor
{
    void Visit(AstNode node);
}

/// <summary>
/// Base class for implementing custom AST visitors.
/// Override Visit() to implement custom traversal logic (e.g., finding specific nodes).
/// </summary>
public abstract class AstVisitor : IAstVisitor
{
    public virtual void Visit(AstNode node)
    {
        // Default behaviour: Pre-order traversal
        foreach (var child in node.ChildNodes)
        {
            Visit(child);
        }
    }
}

/// <summary>
/// Manages loading and executing plugins against the AST.
/// </summary>
public class AstPluginManager
{
    private List<IAstPlugin> _plugins = new List<IAstPlugin>();
    private AhkAstEngine _engine;

    public AstPluginManager(AhkAstEngine engine)
    {
        _engine = engine;
    }

    public void RegisterPlugin(IAstPlugin plugin)
    {
        plugin.Initialize(_engine);
        _plugins.Add(plugin);
    }

    public void ExecutePlugins(AstNode root)
    {
        AstNode currentRoot = root;
        foreach (var plugin in _plugins)
        {
            try
            {
                object result = plugin.Execute(currentRoot);
                if (result is AstNode)
                {
                    currentRoot = (AstNode)result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Plugin " + plugin.Name + " failed: " + ex.Message);
            }
        }
    }

    public List<PipelineStepResult> RunPipeline(AstNode root)
    {
        var results = new List<PipelineStepResult>();
        results.Add(new PipelineStepResult { StepName = "Original", Snapshot = root.Clone(), Output = root });

        object currentData = root;

        foreach (var plugin in _plugins)
        {
            try
            {
                if (currentData is string && _engine != null)
                {
                    currentData = _engine.Parse((string)currentData);
                }

                if (currentData is AstNode)
                {
                    AstNode currentNode = (AstNode)currentData;
                    if (plugin is AHK2AST.Plugins.IFlowPlugin && _engine != null)
                    {
                        var flowPlugin = (AHK2AST.Plugins.IFlowPlugin)plugin;
                        object config = flowPlugin.GetConfig();
                        if (config != null)
                        {
                            _engine.ResolveConfigProperties(config);
                        }
                    }
                    PipelineLogger.Log("  Executing plugin: {0}", plugin.Name);
                    var stepSw = System.Diagnostics.Stopwatch.StartNew();
                    currentData = plugin.Execute(currentNode);
                    stepSw.Stop();
                    PipelineLogger.Log("  Plugin {0} completed successfully in {1}ms.", plugin.Name, stepSw.ElapsedMilliseconds);
                    var result = new PipelineStepResult { StepName = plugin.Name, Output = currentData };
                    if (currentData is AstNode) 
                    {
                        result.Snapshot = ((AstNode)currentData).Clone();
                    }
                    results.Add(result);
                }
                else
                {
                    PipelineLogger.Log("  Plugin {0} returned non-AST output (length: {1}). Skipping subsequent steps.", plugin.Name, currentData != null ? currentData.ToString().Length : 0);
                    break;
                }
            }
            catch (Exception ex)
            {
                PipelineLogger.Log("  ❌ ERROR: Plugin {0} failed: {1}", plugin.Name, ex.ToString());
                Console.WriteLine("Plugin " + plugin.Name + " failed: " + ex.Message);
                results.Add(new PipelineStepResult { StepName = plugin.Name, Error = ex.Message });
                break;
            }
        }
        return results;
    }
}

public class PipelineStepResult
{
    public string StepName { get; set; }
    public AstNode Snapshot { get; set; }
    public object Output { get; set; }
    public string Error { get; set; }
}

namespace AHK2AST.Plugins
{
    public interface IFlowPlugin : IAstPlugin
    {
        string Category { get; }
        string StepTitle { get; }
        string Icon { get; }
        string Version { get; }
        Type ConfigType { get; }
        object GetConfig();
        void SetConfig(object config);
    }

    public static class PluginRegistry
    {
        private static readonly List<Type> _pluginTypes = new List<Type>();

        static PluginRegistry()
        {
            try
            {
                foreach (var type in typeof(AhkAstEngine).Assembly.GetTypes())
                {
                    if (typeof(IFlowPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                    {
                        _pluginTypes.Add(type);
                    }
                }
            }
            catch { }
        }

        public static IEnumerable<Type> RegisteredPluginTypes { get { return _pluginTypes; } }

        public static IFlowPlugin CreatePlugin(string configTypeName)
        {
            foreach (var type in _pluginTypes)
            {
                try
                {
                    var instance = Activator.CreateInstance(type) as IFlowPlugin;
                    if (instance != null && (instance.ConfigType.FullName == configTypeName || instance.ConfigType.Name == configTypeName || type.FullName == configTypeName || type.Name == configTypeName))
                    {
                        return instance;
                    }
                }
                catch { }
            }
            return null;
        }
    }

    public class MissingPluginException : Exception
    {
        public string StepTitle { get; private set; }
        public string ConfigType { get; private set; }

        public MissingPluginException(string stepTitle, string configType)
            : base(string.Format("Plugin for flow step '{0}' (Config Type: {1}) is not included in this build of AstEngine.dll.", stepTitle, configType))
        {
            StepTitle = stepTitle;
            ConfigType = configType;
        }
    }
}
