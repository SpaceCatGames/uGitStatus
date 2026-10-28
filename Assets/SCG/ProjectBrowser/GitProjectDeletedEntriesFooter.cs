using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SCG.UnityGitStatus
{
    /// <summary>
    /// Draws the deleted-files footer inside the Project window when space allows it.
    /// The footer is limited to Project Browser contexts where deleted children belong to the active folder.
    /// It stays hidden when the visible Project content could overlap the footer area.
    /// </summary>
    internal static class GitProjectDeletedEntriesFooter
    {
        private static GUIStyle s_deletedFooterItemStyle;
        private static GUIStyle s_deletedFooterBackgroundStyle;

        /// <summary>
        /// Draws the deleted-files footer for the current Project window callback when the active context qualifies.
        /// The method resolves the active folder, measures visible rows, and suppresses the footer when content overlap is possible.
        /// One-column layout and non-right-pane contexts are ignored intentionally.
        /// </summary>
        /// <param name="selectionRect">Current Project row rect provided by Unity.</param>
        /// <param name="hasProjectBrowserContext">Whether a valid Project Browser context was resolved for the row.</param>
        /// <param name="context">Resolved Project Browser geometry and pane metadata.</param>
        internal static void Draw(Rect selectionRect, bool hasProjectBrowserContext, ProjectBrowserContext context)
        {
            if (!UnityGitStatusSettings.ShowDeletedFilesInProject ||
                !IsRelevantProjectEvent(Event.current.type) ||
                !hasProjectBrowserContext ||
                !context.IsTwoColumns ||
                !context.IsRightPane)
                return;

            if (!ProjectBrowserContextResolver.TryGetDeletedFooterGeometry(context, selectionRect, out var contentBottom))
                return;

            var contextFolderPath = ProjectBrowserContextResolver.GetActiveFolderPath(context);

            if (string.IsNullOrEmpty(contextFolderPath))
                return;

            var deletedEntries = FilterProjectDeletedEntries(GitStatusCache.GetDeletedEntriesForFolder(contextFolderPath));

            if (deletedEntries.Count == 0)
            {
                return;
            }

            var isExpanded = UnityGitStatusSettings.ShowProjectDeletedEntries;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var availableHeight = GetAvailableFooterHeight(context.VisibleRect, contentBottom);
            CalculateFooterRows(
                deletedEntries.Count,
                isExpanded,
                availableHeight,
                lineHeight,
                out var visibleItemCount,
                out var showSummaryRow);
            var footerRect = GetDeletedFooterRect(
                context.VisibleRect,
                visibleItemCount,
                showSummaryRow,
                lineHeight);

            if (footerRect.height > availableHeight)
            {
                return;
            }

            var headerRect = new Rect(
                footerRect.x + Constants.ProjectDeletedFooterPadding,
                footerRect.y + Constants.ProjectDeletedFooterPadding,
                footerRect.width - Constants.ProjectDeletedFooterPadding * 2f,
                lineHeight);

            if (Event.current.type == EventType.Repaint)
            {
                GetDeletedFooterBackgroundStyle().Draw(footerRect, GUIContent.none, false, false, false, false);
            }

            var nextExpanded = EditorGUI.Foldout(
                headerRect,
                UnityGitStatusSettings.ShowProjectDeletedEntries,
                GetDeletedFooterHeaderText(contextFolderPath, deletedEntries.Count),
                true);

            if (nextExpanded != UnityGitStatusSettings.ShowProjectDeletedEntries)
            {
                UnityGitStatusSettings.ShowProjectDeletedEntries = nextExpanded;
            }

            if (!UnityGitStatusSettings.ShowProjectDeletedEntries)
            {
                return;
            }

            var y = headerRect.yMax + Constants.ProjectDeletedFooterRowSpacing;
            var itemStyle = GetDeletedFooterItemStyle();

            for (var index = 0; index < visibleItemCount; index++)
            {
                var itemRect = new Rect(headerRect.x + 14f, y, headerRect.width - 14f, lineHeight);
                EditorGUI.LabelField(itemRect, GetDeletedEntryLabel(contextFolderPath, deletedEntries[index]), itemStyle);
                y += lineHeight + Constants.ProjectDeletedFooterRowSpacing;
            }

            if (!showSummaryRow)
            {
                return;
            }

            var remainingRect = new Rect(headerRect.x + 14f, y, headerRect.width - 14f, lineHeight);
            EditorGUI.LabelField(
                remainingRect,
                $"+{deletedEntries.Count - visibleItemCount} more deleted file(s)",
                itemStyle);
        }

        private static Rect GetDeletedFooterRect(Rect visibleRect, int visibleItemCount, bool showSummaryRow, float lineHeight)
        {
            var height = GetDeletedFooterHeight(visibleItemCount, showSummaryRow, lineHeight);
            return new Rect(
                visibleRect.x + Constants.ProjectDeletedFooterPadding,
                visibleRect.yMax - height - Constants.ProjectDeletedFooterPadding,
                visibleRect.width - Constants.ProjectDeletedFooterPadding * 2f,
                height);
        }

        private static string GetDeletedFooterHeaderText(string activeFolderPath, int deletedEntryCount)
        {
            var folderName = GetFileName(activeFolderPath);
            var actionText = UnityGitStatusSettings.ShowProjectDeletedEntries ? "Hide" : "Show";
            return $"{actionText} deleted files in {folderName} ({deletedEntryCount})";
        }

        private static List<GitStatusEntry> FilterProjectDeletedEntries(IReadOnlyList<GitStatusEntry> deletedEntries)
        {
            var filteredEntries = new List<GitStatusEntry>(deletedEntries.Count);
            filteredEntries.AddRange(deletedEntries.Where(deletedEntry => !deletedEntry.IsMeta));
            return filteredEntries;
        }

        private static float GetAvailableFooterHeight(Rect visibleRect, float contentBottom) =>
            float.IsNaN(contentBottom) || float.IsInfinity(contentBottom) ||
            contentBottom < 0f || visibleRect.width <= 0f || visibleRect.height <= 0f
                ? 0f
                : Mathf.Max(0f, visibleRect.yMax - Mathf.Max(visibleRect.yMin, contentBottom) - Constants.ProjectDeletedFooterPadding);

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// Calculates footer placement for controlled geometry without drawing editor controls.
        /// </summary>
        /// <param name="visibleRect">Viewport in scroll-content coordinates.</param>
        /// <param name="contentBottom">Complete content bottom, or NaN when unknown.</param>
        /// <param name="count">Number of deleted files.</param>
        /// <param name="expanded">Whether file rows are expanded.</param>
        /// <param name="rect">Calculated footer rectangle.</param>
        /// <param name="visibleCount">Number of displayed file rows.</param>
        /// <param name="summary">Whether a remaining-files row is displayed.</param>
        /// <param name="lineHeight">Controlled text row height in pixels.</param>
        /// <returns>True when the footer fits in the unoccupied viewport.</returns>
        internal static bool TryGetLayoutForTests(Rect visibleRect, float contentBottom, int count, bool expanded,
            out Rect rect, out int visibleCount, out bool summary, float lineHeight = 20f)
        {
            var availableHeight = GetAvailableFooterHeight(visibleRect, contentBottom);
            CalculateFooterRows(count, expanded, availableHeight, lineHeight, out visibleCount, out summary);
            rect = GetDeletedFooterRect(visibleRect, visibleCount, summary, lineHeight);
            return count > 0 && rect.height <= availableHeight;
        }
#endif

        private static void CalculateFooterRows(
            int deletedEntryCount,
            bool isExpanded,
            float availableHeight,
            float lineHeight,
            out int visibleItemCount,
            out bool showSummaryRow)
        {
            visibleItemCount = 0;
            showSummaryRow = false;

            if (!isExpanded || deletedEntryCount <= 0)
                return;

            var collapsedHeight = GetDeletedFooterHeight(0, false, lineHeight);
            var rowHeight = lineHeight + Constants.ProjectDeletedFooterRowSpacing;
            var rowsCapacity = Mathf.Max(0, Mathf.FloorToInt((availableHeight - collapsedHeight) / rowHeight));

            if (rowsCapacity <= 0)
            {
                return;
            }

            var preferredVisibleItemCount = Mathf.Min(deletedEntryCount, Constants.ProjectDeletedFooterMaxVisibleItems);

            if (deletedEntryCount <= preferredVisibleItemCount && rowsCapacity >= deletedEntryCount)
            {
                visibleItemCount = Mathf.Min(rowsCapacity, deletedEntryCount);
                return;
            }

            if (rowsCapacity == 1)
            {
                showSummaryRow = true;
                return;
            }

            visibleItemCount = Mathf.Min(rowsCapacity - 1, preferredVisibleItemCount);
            showSummaryRow = deletedEntryCount > visibleItemCount;
        }

        private static float GetDeletedFooterHeight(int visibleItemCount, bool showSummaryRow, float lineHeight)
        {
            var contentLineCount = 1 + visibleItemCount + (showSummaryRow ? 1 : 0);
            return
                Constants.ProjectDeletedFooterPadding * 2f +
                contentLineCount * lineHeight +
                contentLineCount * Constants.ProjectDeletedFooterRowSpacing;
        }

        private static string GetDeletedEntryLabel(string activeFolderPath, GitStatusEntry entry)
        {
            var displayPath = GitStatusEntry.NormalizePath(entry.DisplayPath);
            var normalizedFolderPath = GitStatusEntry.NormalizePath(activeFolderPath);

            return displayPath.StartsWith(normalizedFolderPath + "/", StringComparison.Ordinal)
                ? displayPath[(normalizedFolderPath.Length + 1)..]
                : GetFileName(displayPath);
        }

        private static bool IsRelevantProjectEvent(EventType eventType) =>
            eventType is EventType.Layout or EventType.Repaint or EventType.MouseDown or EventType.MouseUp;


        private static string GetFileName(string path)
        {
            var normalizedPath = GitStatusEntry.NormalizePath(path);
            var slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex >= 0 ? normalizedPath[(slashIndex + 1)..] : normalizedPath;
        }

        private static GUIStyle GetDeletedFooterItemStyle()
        {
            if (s_deletedFooterItemStyle != null)
            {
                return s_deletedFooterItemStyle;
            }

            s_deletedFooterItemStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal =
                {
                    textColor = new Color(0.95f, 0.18f, 0.18f, 1f)
                }
            };

            return s_deletedFooterItemStyle;
        }

        private static GUIStyle GetDeletedFooterBackgroundStyle()
        {
            s_deletedFooterBackgroundStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            return s_deletedFooterBackgroundStyle;
        }
    }
}
