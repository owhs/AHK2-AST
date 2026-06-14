using System;
using System.IO;
using System.Web.Script.Serialization;

namespace AHK2AST.UI
{
    public class WorkbenchState
    {
        public int MainSplitterDistance { get; set; }
        public int RightSplitterDistance { get; set; }
        public int DiffSplitterDistance { get; set; }
        public System.Collections.Generic.List<string> RecentFiles { get; set; }
        public bool InlineIncludes { get; set; }
        public bool FollowIncludes { get; set; }
        public string ThemeName { get; set; }
        public bool WordWrap { get; set; }
        public string BuildOutputPath { get; set; }
        public int BuildModeIndex { get; set; }
        public bool BuildUpxChecked { get; set; }
        public bool BuildTreeShakeChecked { get; set; }
        public bool BuildDebugChecked { get; set; }
        public bool BuildLtoChecked { get; set; }
        public bool BuildArcChecked { get; set; }
        public bool BuildPanicsChecked { get; set; }
        public string BuildIconPath { get; set; }
        public string BuildAppName { get; set; }
        public string BuildVersion { get; set; }
        public string BuildProductName { get; set; }
        public string BuildDescription { get; set; }
        public string BuildCopyright { get; set; }

        public WorkbenchState()
        {
            // Defaults
            MainSplitterDistance = 280;
            RightSplitterDistance = 350;
            DiffSplitterDistance = 400;
            RecentFiles = new System.Collections.Generic.List<string>();
            InlineIncludes = true;
            FollowIncludes = true;
            ThemeName = "Forest Midnight";
            WordWrap = false;
            BuildDebugChecked = true;
            BuildLtoChecked = true;
            BuildArcChecked = false;
            BuildPanicsChecked = false;
        }

        public static WorkbenchState Load()
        {
            string path = GetStatePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer();
                    var state = serializer.Deserialize<WorkbenchState>(json);
                    if (state != null)
                    {
                        if (state.RecentFiles == null)
                            state.RecentFiles = new System.Collections.Generic.List<string>();
                        return state;
                    }
                }
                catch { }
            }
            return new WorkbenchState();
        }

        public void Save()
        {
            try
            {
                string path = GetStatePath();
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private static string GetStatePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WorkbenchState.json");
        }
    }
}
