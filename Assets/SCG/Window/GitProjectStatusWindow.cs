using UnityEditor;
using UnityEngine;

namespace SCG.UnityGitStatus
{
    /// <summary>
    /// Displays the current Git status snapshot inside a compact Unity Editor window.
    /// The window exposes repository diagnostics, visible changed paths, and deleted files in a single place.
    /// It stays intentionally minimal and delegates all Git work to the shared cache.
    /// </summary>
    internal sealed partial class GitProjectStatusWindow : EditorWindow
    {
        #region Constants

        private const string ProjectOverlaysLabel = "Project overlays";
        private const string ProjectOverlaysTooltip = "Draw status badges in the Project window for changed assets and folders under Assets/.";
        private const string InspectorBadgeLabel = "Inspector badge";
        private const string InspectorBadgeTooltip = "Draw a Git status badge in the primary Inspector header for the inspected persistent asset or folder.";
        private const string RefreshModeLabel = "Refresh Mode";
        private const string RefreshModeTooltip = "Choose how Git status refreshes inside the editor.";
        private const string ShowMetaFilesLabel = "Show meta files";
        private const string ShowMetaFilesTooltip = "Include Unity .meta files in the Changed Paths list.";
        private const string DiffContextLinesLabel = "Context lines";
        private const string DiffContextLinesTooltip = "Number of unchanged lines shown before and after each diff change.";
        private const string ShowDeletedFilesInProjectLabel = "Deleted files in Project";
        private const string ShowDeletedFilesInProjectTooltip = "Show or hide the deleted files footer at the bottom of the Project window.";
        private const string RightAlignedBadgesLabel = "Right-aligned badges";
        private const string RightAlignedBadgesTooltip = "Draw status badges at the right edge of Project rows. Disable this to use Icon-corner badges near the object icon.";
        private const string CalcModeLabel = "Calc Mode";
        private const string CalcModeTooltip = "Use symbols instead of letters for status badges in both the Project window and the Inspector badge: * + - ± / ? ! X";
        private const string ShowLeftPaneOverlaysInTwoColumnLabel = "Left pane overlays";
        private const string ShowLeftPaneOverlaysInTwoColumnTooltip = "Draw status badges in the left tree pane when the Project window uses Two Column layout.";
        private const string BadgeAppearanceLabel = "Badge Appearance";
        private const string BadgeAppearanceTooltip = "Options that affect badge symbols and shared visual presentation.";
        private const string ProjectOverlaySettingsLabel = "Project Overlay Settings";
        private const string ProjectOverlaySettingsTooltip = "Options that affect only Project window badges and Project-only footer UI.";
        private const string TwoColumnSettingsLabel = "Two Column Settings";
        private const string TwoColumnSettingsTooltip = "Options that affect the two-column Project window layout.";
        private const string ManualRefreshModeLabel = "Manual only";
        private const string ManualRefreshModeTooltip = "Disables automatic refresh. Git status updates only when you press Refresh or use the menu command.";
        private const string TimedRefreshModeLabel = "Timed";
        private const string TimedRefreshModeTooltip = "Refreshes Git status on a timer. The interval can be configured from 1 to 30 seconds.";
        private const string EventDrivenRefreshModeLabel = "Event-driven";
        private const string EventDrivenRefreshModeTooltip = "Refreshes Git status on editor startup, when the editor becomes active again, and after compilation finishes.";
        private const string RefreshButtonText = "Refresh";
        private const string RefreshIntervalLabel = "Refresh interval (seconds)";
        private const string ChangesSectionLabel = "Changes";
        private const string ChangedFilesLabel = "Changed files";
        private const string DeletedFilesLabel = "Deleted files";
        private const string ChangedPathsSectionLabel = "Changed Paths";
        private const string FileSearchLabel = "File search";
        private const string NoChangedPathsText = "No changed paths.";
        private const string NoChangedPathsMatchSearchText = "No changed paths match the current search.";
        private const string MetaFileSuffix = ".meta";
        private const string TotalCountSeparator = " = ";
        private const float RefreshButtonWidth = 90f;
        private const float SectionLabelWidth = 140f;
        private const float CalcModeToggleWidth = 100f;
        private const float RightAlignedBadgesToggleWidth = 170f;
        private const float ShowDeletedFilesToggleWidth = 190f;
        private const float ShowLeftPaneOverlaysToggleWidth = 220f;
        private const float RefreshModePopupMaxWidth = 144f;
        private const float SortModePopupMaxWidth = 149f;
        private const float PreChangesSpacing = 8f;
        private const int ChangesSectionHeaderFontSizeDelta = 3;
        private const int ChangedPathsSectionHeaderFontSizeDelta = 1;
        private const int EntriesPerPage = 50;

        #endregion

        #region Fields

        private static readonly GUIContent s_projectOverlaysContent = new(
            ProjectOverlaysLabel,
            ProjectOverlaysTooltip);

        private static readonly GUIContent s_inspectorBadgeContent = new(
            InspectorBadgeLabel,
            InspectorBadgeTooltip);

        private static readonly GUIContent s_refreshModeContent = new(
            RefreshModeLabel,
            RefreshModeTooltip);

        private static readonly GUIContent s_showMetaFilesContent = new(
            ShowMetaFilesLabel,
            ShowMetaFilesTooltip);

        private static readonly GUIContent s_diffContextLinesContent = new(
            DiffContextLinesLabel,
            DiffContextLinesTooltip);

        private static readonly GUIContent s_showDeletedFilesInProjectContent = new(
            ShowDeletedFilesInProjectLabel,
            ShowDeletedFilesInProjectTooltip);

        private static readonly GUIContent s_rightAlignedBadgesContent = new(
            RightAlignedBadgesLabel,
            RightAlignedBadgesTooltip);

        private static readonly GUIContent s_calcModeContent = new(
            CalcModeLabel,
            CalcModeTooltip);

        private static readonly GUIContent s_showLeftPaneOverlaysInTwoColumnContent = new(
            ShowLeftPaneOverlaysInTwoColumnLabel,
            ShowLeftPaneOverlaysInTwoColumnTooltip);

        private static readonly GUIContent s_badgeAppearanceContent = new(
            BadgeAppearanceLabel,
            BadgeAppearanceTooltip);

        private static readonly GUIContent s_projectOverlaySettingsContent = new(
            ProjectOverlaySettingsLabel,
            ProjectOverlaySettingsTooltip);

        private static readonly GUIContent s_twoColumnSettingsContent = new(
            TwoColumnSettingsLabel,
            TwoColumnSettingsTooltip);

        private static readonly GUIContent[] s_refreshModeOptions =
        {
            new(
                ManualRefreshModeLabel,
                ManualRefreshModeTooltip),
            new(
                TimedRefreshModeLabel,
                TimedRefreshModeTooltip),
            new(
                EventDrivenRefreshModeLabel,
                EventDrivenRefreshModeTooltip)
        };

        private static readonly GUIContent[] s_sortModeOptions =
        {
            new(Constants.SortByPathAscendingLabel, Constants.SortModeTooltip),
            new(Constants.SortByPathDescendingLabel, Constants.SortModeTooltip),
            new(Constants.SortByFileNameAscendingLabel, Constants.SortModeTooltip),
            new(Constants.SortByFileNameDescendingLabel, Constants.SortModeTooltip),
            new(Constants.SortByFileStatusLabel, Constants.SortModeTooltip)
        };

        private Vector2 scrollPosition;
        private GUIStyle changesSectionHeaderStyle;
        private GUIStyle changedPathsSectionHeaderStyle;
        private string pathSearch = string.Empty;
        private bool scrollToSelectedEntry;
        private int currentPage;

        #endregion

        #region Unity Events

        private void OnEnable()
        {
            minSize = new Vector2(Constants.WindowMinWidth, Constants.WindowMinHeight);
            UpdateWindowTitle(GitStatusCache.Snapshot.RepositoryDetected);
            GitStatusCache.StatusChanged += Repaint;
        }

        private void OnDisable()
        {
            GitStatusCache.StatusChanged -= Repaint;
        }

        private void OnInspectorUpdate()
        {
            if (operationMessageClearAt <= 0d ||
                EditorApplication.timeSinceStartup < operationMessageClearAt)
            {
                return;
            }

            operationMessage = string.Empty;
            operationMessageClearAt = 0d;
            Repaint();
        }

        private void OnGUI()
        {
            var currentSnapshot = GitStatusCache.Snapshot;
            UpdateWindowTitle(currentSnapshot.RepositoryDetected);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(RefreshButtonText, GUILayout.Width(RefreshButtonWidth)))
                {
                    GitStatusCache.RefreshNow();
                }

                var projectOverlaysEnabled = UnityGitStatusSettings.ProjectOverlaysEnabled;
                var nextProjectOverlaysEnabled = EditorGUILayout.ToggleLeft(
                    s_projectOverlaysContent,
                    projectOverlaysEnabled,
                    GUILayout.Width(SectionLabelWidth));

                if (nextProjectOverlaysEnabled != projectOverlaysEnabled)
                {
                    UnityGitStatusSettings.ProjectOverlaysEnabled = nextProjectOverlaysEnabled;
                }

                var inspectorBadgeEnabled = UnityGitStatusSettings.InspectorBadgeEnabled;
                var nextInspectorBadgeEnabled = EditorGUILayout.ToggleLeft(
                    s_inspectorBadgeContent,
                    inspectorBadgeEnabled,
                    GUILayout.Width(SectionLabelWidth));

                if (nextInspectorBadgeEnabled != inspectorBadgeEnabled)
                {
                    UnityGitStatusSettings.InspectorBadgeEnabled = nextInspectorBadgeEnabled;
                }

                GUILayout.FlexibleSpace();
            }

            if (UnityGitStatusSettings.ProjectOverlaysEnabled || UnityGitStatusSettings.InspectorBadgeEnabled)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(s_badgeAppearanceContent, GUILayout.Width(SectionLabelWidth));

                    var nextCalcMode = EditorGUILayout.ToggleLeft(
                        s_calcModeContent,
                        UnityGitStatusSettings.CalcMode,
                        GUILayout.Width(CalcModeToggleWidth));

                    if (nextCalcMode != UnityGitStatusSettings.CalcMode)
                    {
                        UnityGitStatusSettings.CalcMode = nextCalcMode;
                    }

                    GUILayout.FlexibleSpace();
                }
            }

            if (UnityGitStatusSettings.ProjectOverlaysEnabled)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(s_projectOverlaySettingsContent, GUILayout.Width(SectionLabelWidth));

                    var nextRightAlignedBadges = EditorGUILayout.ToggleLeft(
                        s_rightAlignedBadgesContent,
                        UnityGitStatusSettings.RightAlignedBadges,
                        GUILayout.Width(RightAlignedBadgesToggleWidth));

                    if (nextRightAlignedBadges != UnityGitStatusSettings.RightAlignedBadges)
                    {
                        UnityGitStatusSettings.RightAlignedBadges = nextRightAlignedBadges;
                    }

                    GUILayout.FlexibleSpace();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(s_twoColumnSettingsContent, GUILayout.Width(SectionLabelWidth));

                    var nextShowDeletedFilesInProject = EditorGUILayout.ToggleLeft(
                        s_showDeletedFilesInProjectContent,
                        UnityGitStatusSettings.ShowDeletedFilesInProject,
                        GUILayout.Width(ShowDeletedFilesToggleWidth));

                    if (nextShowDeletedFilesInProject != UnityGitStatusSettings.ShowDeletedFilesInProject)
                    {
                        UnityGitStatusSettings.ShowDeletedFilesInProject = nextShowDeletedFilesInProject;
                    }

                    var nextShowLeftPaneOverlaysInTwoColumn = EditorGUILayout.ToggleLeft(
                        s_showLeftPaneOverlaysInTwoColumnContent,
                        UnityGitStatusSettings.ShowLeftPaneOverlaysInTwoColumn,
                        GUILayout.Width(ShowLeftPaneOverlaysToggleWidth));

                    if (nextShowLeftPaneOverlaysInTwoColumn != UnityGitStatusSettings.ShowLeftPaneOverlaysInTwoColumn)
                    {
                        UnityGitStatusSettings.ShowLeftPaneOverlaysInTwoColumn = nextShowLeftPaneOverlaysInTwoColumn;
                    }

                    GUILayout.FlexibleSpace();
                }
            }

            var currentRefreshMode = UnityGitStatusSettings.RefreshMode;
            int nextRefreshModeIndex;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(s_refreshModeContent, GUILayout.Width(SectionLabelWidth));
                nextRefreshModeIndex = EditorGUILayout.Popup(
                    GetRefreshModeIndex(currentRefreshMode),
                    s_refreshModeOptions,
                    GUILayout.MaxWidth(RefreshModePopupMaxWidth),
                    GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
            }

            var nextRefreshMode = GetRefreshModeByIndex(nextRefreshModeIndex);

            if (nextRefreshMode != currentRefreshMode)
            {
                UnityGitStatusSettings.ApplyRefreshMode(nextRefreshMode);
            }

            if (nextRefreshMode == GitRefreshMode.Timed)
            {
                var currentInterval = UnityGitStatusSettings.TimedRefreshIntervalSeconds;
                var nextInterval = EditorGUILayout.IntSlider(
                    RefreshIntervalLabel,
                    currentInterval,
                    Constants.MinTimedRefreshIntervalSeconds,
                    Constants.MaxTimedRefreshIntervalSeconds);

                if (nextInterval != currentInterval)
                {
                    UnityGitStatusSettings.TimedRefreshIntervalSeconds = nextInterval;
                    GitStatusCache.ApplyRefreshSettingsChange(false);
                }
            }

            EditorGUILayout.Space(PreChangesSpacing);
            EditorGUILayout.LabelField(ChangesSectionLabel, GetChangesSectionHeaderStyle());
            EditorGUILayout.LabelField(ChangedFilesLabel, FormatChangedFilesCount(currentSnapshot));

            if (currentSnapshot.DeletedCount > 0)
            {
                EditorGUILayout.LabelField(DeletedFilesLabel, FormatDeletedFilesCount(currentSnapshot));
            }

            if (!string.IsNullOrEmpty(currentSnapshot.LastError))
            {
                EditorGUILayout.HelpBox(currentSnapshot.LastError, MessageType.Info);
            }

            EditorGUILayout.Space(Constants.WindowFooterTopSpacing * 2f);
            EditorGUILayout.LabelField(ChangedPathsSectionLabel, GetChangedPathsSectionHeaderStyle());
            pathSearch = EditorGUILayout.TextField(FileSearchLabel, pathSearch);
            using (new EditorGUILayout.HorizontalScope())
            {
                var currentSortMode = UnityGitStatusSettings.StatusSortMode;
                var nextSortMode = (GitStatusSortMode)EditorGUILayout.Popup(
                    (int)currentSortMode,
                    s_sortModeOptions,
                    GUILayout.MaxWidth(SortModePopupMaxWidth));

                if (nextSortMode != currentSortMode)
                {
                    UnityGitStatusSettings.StatusSortMode = nextSortMode;
                }

                var nextShowMetaFiles = EditorGUILayout.ToggleLeft(
                    s_showMetaFilesContent,
                    UnityGitStatusSettings.ShowMetaFiles,
                    GUILayout.Width(SectionLabelWidth));

                if (nextShowMetaFiles != UnityGitStatusSettings.ShowMetaFiles)
                {
                    UnityGitStatusSettings.ShowMetaFiles = nextShowMetaFiles;
                }

                EditorGUILayout.LabelField(s_diffContextLinesContent, GUILayout.Width(90f));
                var currentContextLines = UnityGitStatusSettings.DiffContextLines;
                var nextContextLines = EditorGUILayout.IntSlider(
                    currentContextLines,
                    Constants.MinDiffContextLines,
                    Constants.MaxDiffContextLines,
                    GUILayout.MaxWidth(220f));

                if (nextContextLines != currentContextLines)
                {
                    UnityGitStatusSettings.DiffContextLines = nextContextLines;
                    LoadSelectedDiffs();
                }

                GUILayout.FlexibleSpace();
            }

            var preferDisplayPath = !UnityGitStatusSettings.ShowMetaFiles;
            var windowEntries = GetWindowEntries(currentSnapshot, preferDisplayPath);
            var matchingEntries = windowEntries.FindAll(entry => ShouldShowEntry(entry) && MatchesPathSearch(entry, preferDisplayPath));
            currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, (matchingEntries.Count - 1) / EntriesPerPage));
            HandleEntryKeyboardNavigation(matchingEntries);
            DrawChangesWorkspace(matchingEntries, preferDisplayPath, windowEntries.Count == 0);

            EditorGUILayout.Space(Constants.WindowFooterTopSpacing);
            DrawFooter(currentSnapshot);
        }

        #endregion
    }
}
