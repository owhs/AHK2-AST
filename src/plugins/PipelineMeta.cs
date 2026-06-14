using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;

namespace AHK2AST.Plugins
{
    public class PipelineMeta
    {
        [Category("1. Identity"), DisplayName("Flow Name"), Description("The name of this pipeline/flow.")]
        public string Name { get; set; }

        [Category("1. Identity"), DisplayName("Description"), Description("A short description of what this flow does.")]
        public string Description { get; set; }

        [Category("1. Identity"), DisplayName("Icon"), Description("Emoji or icon for the flow.")]
        public string Icon { get; set; }

        [Category("2. Organization"), DisplayName("Folder"), Description("The folder path or category grouping for this flow.")]
        public string Folder { get; set; }

        [Category("2. Organization"), DisplayName("Tags"), Description("Comma separated list of tags.")]
        public string Tags { get; set; }

        [Category("3. Execution"), DisplayName("Emit Diagnostics Comments"), Description("If true, prepends the execution summary/diagnostics comments block at the top of the emitted code.")]
        public bool EmitDiagnosticsComments { get; set; }

        [Category("4. Parameters"), DisplayName("Custom Properties"), Description("Key-value properties (Key=Value, one per line) referenceable via ${Key} in step settings.")]
        [Editor("System.ComponentModel.Design.MultilineStringEditor, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", typeof(System.Drawing.Design.UITypeEditor))]
        public string CustomProperties { get; set; }

        public PipelineMeta()
        {
            Name = "New Flow";
            Description = "Custom processing pipeline";
            Icon = "⚡";
            Folder = "User Flows";
            Tags = "";
            EmitDiagnosticsComments = true;
            CustomProperties = "";
        }
    }
}
