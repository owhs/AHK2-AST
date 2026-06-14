using System;
using System.Collections.Generic;

namespace AHK2AST.Plugins
{
    public class FlowDefinition
    {
        public PipelineMeta Meta { get; set; }
        public List<FlowStepDefinition> Steps { get; set; }

        public FlowDefinition()
        {
            Meta = new PipelineMeta();
            Steps = new List<FlowStepDefinition>();
        }
    }

    public class FlowStepDefinition
    {
        public string Title { get; set; }
        public string Icon { get; set; }
        public string ConfigType { get; set; } // Fully qualified name or class name
        public string ConfigJson { get; set; } // The serialized config object for this step
        
        public FlowStepDefinition()
        {
        }
    }
}
