namespace SCG.UnityGitStatus
{
    /// <summary>
    /// Stores reflection names used to bridge Unity internal editor APIs.
    /// Keeping these names centralized reduces typo risk and makes version audits easier.
    /// </summary>
    internal static partial class Constants
    {
        /// <summary>Assembly-qualified type name used to resolve UnityEditor.ProjectBrowser through reflection.</summary>
        public const string ProjectBrowserTypeName = "UnityEditor.ProjectBrowser";

        /// <summary>Assembly-qualified type name used to resolve UnityEngine.GUIClip through reflection.</summary>
        public const string GuiClipTypeName = "UnityEngine.GUIClip";

        /// <summary>Assembly-qualified type name used to resolve UnityEditor.GUIView through reflection.</summary>
        public const string GuiViewTypeName = "UnityEditor.GUIView";

        /// <summary>Assembly-qualified type name used to resolve UnityEditor.HostView through reflection.</summary>
        public const string HostViewTypeName = "UnityEditor.HostView";

        /// <summary>Internal ProjectWindowUtil method name that resolves the current ProjectBrowser instance.</summary>
        public const string GetProjectBrowserIfExistsMethodName = "GetProjectBrowserIfExists";

        /// <summary>Internal ProjectBrowser method name that returns all alive Project Browser windows.</summary>
        public const string GetAllProjectBrowsersMethodName = "GetAllProjectBrowsers";

        /// <summary>Internal ProjectWindowUtil method name that directly resolves the active folder path.</summary>
        public const string GetActiveFolderPathMethodName = "GetActiveFolderPath";

        /// <summary>Internal ProjectBrowser instance method name used to detect the two-column layout mode.</summary>
        public const string IsTwoColumnsMethodName = "IsTwoColumns";

        /// <summary>Internal ProjectBrowser field name containing the main list area rect.</summary>
        public const string ListAreaRectFieldName = "m_ListAreaRect";
        /// <summary>Project Browser field holding its item list.</summary>
        public const string ListAreaFieldName = "m_ListArea";

        /// <summary>Internal ProjectBrowser field name containing the left tree pane rect.</summary>
        public const string TreeViewRectFieldName = "m_TreeViewRect";

        /// <summary>List field holding its content groups.</summary>
        public const string GroupsFieldName = "m_Groups";
        /// <summary>Group field holding its item grid.</summary>
        public const string GridFieldName = "m_Grid";

        /// <summary>Internal ProjectBrowser static field containing the last interacted Project Browser window.</summary>
        public const string LastInteractedProjectBrowserFieldName = "s_LastInteractedProjectBrowser";

        /// <summary>Internal GUIClip property name containing the current visible rect.</summary>
        public const string VisibleRectPropertyName = "visibleRect";

        /// <summary>Internal GUIView property name containing the currently rendering GUI view.</summary>
        public const string CurrentPropertyName = "current";

        /// <summary>Internal HostView property name containing the currently hosted editor window.</summary>
        public const string ActualViewPropertyName = "actualView";

        /// <summary>Internal Editor property name indicating whether the editor is the first inspected editor.</summary>
        public const string FirstInspectedEditorPropertyName = "firstInspectedEditor";

        /// <summary>Grid method returning an item rectangle.</summary>
        public const string CalcRectMethodName = "CalcRect";
        /// <summary>Group property returning its complete content height.</summary>
        public const string GroupHeightPropertyName = "Height";
    }
}
