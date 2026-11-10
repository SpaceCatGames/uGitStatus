using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SCG.UnityGitStatus
{
    /// <inheritdoc/>
    internal sealed partial class GitProjectStatusWindow
    {
        private const float FooterColumnCount = 3f;
        private const string BranchLabelPrefix = "Branch: ";
        private const string RefreshInProgressLabelPrefix = "Refresh in progress: ";
        private const string LastRefreshLabelPrefix = "Last refresh: ";
        private const string YesText = "Yes";
        private const string NoText = "No";
        private const string RepositoryDetectedIndicator = "✔";
        private const string RepositoryMissingIndicator = "✘";
        private const string EmptyBranchText = "-";
        private const string NeverRefreshText = "Never";
        private const string OutsideAssetsSuffix = "outside Assets";
        private const string PathTransitionSeparator = " -> ";

        #region Drawing

        private void UpdateWindowTitle(bool repositoryDetected)
        {
            titleContent = new GUIContent(
                Constants.WindowTitle +
                " (" +
                GetRepositoryIndicator(repositoryDetected) +
                ")");
        }

        private static void DrawFooter(GitStatusSnapshot currentSnapshot)
        {
            var footerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var columnWidth = footerRect.width / FooterColumnCount;
            var leftRect = new Rect(footerRect.x, footerRect.y, columnWidth, footerRect.height);
            var centerRect = new Rect(footerRect.x + columnWidth, footerRect.y, columnWidth, footerRect.height);
            var rightRect = new Rect(footerRect.x + columnWidth * 2f, footerRect.y, columnWidth, footerRect.height);

            EditorGUI.LabelField(
                leftRect,
                BranchLabelPrefix + GetBranchText(currentSnapshot),
                GetFooterStyle(TextAnchor.MiddleLeft, FontStyle.Bold));
            EditorGUI.LabelField(
                centerRect,
                RefreshInProgressLabelPrefix + GetBooleanText(GitStatusCache.IsRefreshInProgress),
                GetFooterStyle(TextAnchor.MiddleCenter, FontStyle.Normal));
            EditorGUI.LabelField(
                rightRect,
                LastRefreshLabelPrefix + FormatRefreshTime(currentSnapshot.LastRefreshTime),
                GetFooterStyle(TextAnchor.MiddleRight, FontStyle.Normal));
        }

        private void DrawEntry(GitStatusEntry entry, bool preferDisplayPath)
        {
            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var badgeSize = rowRect.height - Constants.WindowBadgeVerticalInset * 2f;
            var badgeRect = new Rect(
                rowRect.x,
                rowRect.y + Constants.WindowBadgeVerticalInset,
                badgeSize,
                badgeSize);
            var labelRect = new Rect(
                rowRect.x + badgeSize + Constants.WindowBadgeHorizontalSpacing,
                rowRect.y,
                rowRect.width - badgeSize - Constants.WindowBadgeHorizontalSpacing,
                rowRect.height);

            EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);

            if (GitPathComparer.AreEqual(selectedPath, entry.Path))
            {
                EditorGUI.DrawRect(rowRect, new Color(0.2f, 0.45f, 0.75f, 0.28f));

                if (scrollToSelectedEntry)
                {
                    GUI.ScrollTo(rowRect);
                    scrollToSelectedEntry = false;
                }
            }

            var currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                rowRect.Contains(currentEvent.mousePosition))
            {
                GUI.FocusControl(null);
                SelectEntry(entry);

                if (currentEvent.clickCount >= 2)
                {
                    SelectEntryInProject(entry);
                }

                currentEvent.Use();
            }

            var descriptor = GitStatusDescriptors.Get(entry.Kind);
            GitStatusBadgeGui.Draw(
                badgeRect,
                descriptor,
                UnityGitStatusSettings.CalcMode);
            EditorGUI.LabelField(labelRect, FormatEntryPath(entry, preferDisplayPath), EditorStyles.miniLabel);
        }

        private void SelectEntry(GitStatusEntry entry)
        {
            selectedEntry = entry;
            selectedPath = entry.Path;
            operationMessage = string.Empty;
            operationMessageClearAt = 0d;
            LoadSelectedDiffs();
        }

        private void HandleEntryKeyboardNavigation(List<GitStatusEntry> entries)
        {
            var currentEvent = Event.current;
            if (focusedWindow != this || currentEvent.type != EventType.KeyDown ||
                EditorGUIUtility.editingTextField || selectedEntry == null ||
                currentEvent.alt || currentEvent.control || currentEvent.command ||
                currentEvent.keyCode is not (KeyCode.UpArrow or KeyCode.DownArrow)) return;

            var selectedIndex = entries.FindIndex(entry => GitPathComparer.AreEqual(entry.Path, selectedPath));
            if (selectedIndex < 0) return;

            var offset = currentEvent.keyCode == KeyCode.UpArrow ? -1 : 1;
            var nextIndex = Mathf.Clamp(selectedIndex + offset, 0, entries.Count - 1);
            if (nextIndex == selectedIndex) return;

            SelectEntry(entries[nextIndex]);
            currentPage = nextIndex / EntriesPerPage;
            scrollToSelectedEntry = true;
            currentEvent.Use();
            Repaint();
        }

        private static void SelectEntryInProject(GitStatusEntry entry)
        {
            var selectionPath = GetSelectionPath(entry);
            if (string.IsNullOrEmpty(selectionPath)) return;

            var selectedObject = AssetDatabase.LoadMainAssetAtPath(selectionPath);
            if (selectedObject == null) return;

            Selection.activeObject = selectedObject;
            EditorGUIUtility.PingObject(selectedObject);
        }

        private static string GetSelectionPath(GitStatusEntry entry)
        {
            var path = GitStatusEntry.NormalizePath(entry.Path);

            return string.IsNullOrEmpty(path) || entry.IsDeleted
                ? string.Empty
                : entry.IsMeta && GitStatusEntry.IsMetaPath(path)
                    ? path[..^MetaFileSuffix.Length]
                    : path;
        }

        private GUIStyle GetChangesSectionHeaderStyle()
        {
            changesSectionHeaderStyle ??= CreateSectionHeaderStyle(ChangesSectionHeaderFontSizeDelta);
            return changesSectionHeaderStyle;
        }

        private GUIStyle GetChangedPathsSectionHeaderStyle()
        {
            changedPathsSectionHeaderStyle ??= CreateSectionHeaderStyle(ChangedPathsSectionHeaderFontSizeDelta);
            return changedPathsSectionHeaderStyle;
        }

        private static GUIStyle CreateSectionHeaderStyle(int fontSizeDelta) =>
            new(EditorStyles.boldLabel)
            {
                fontSize = EditorStyles.boldLabel.fontSize + fontSizeDelta
            };

        #endregion

        #region Formatting

        private static string GetBooleanText(bool value) => value ? YesText : NoText;

        private static string GetRepositoryIndicator(bool repositoryDetected) =>
            repositoryDetected ? RepositoryDetectedIndicator : RepositoryMissingIndicator;

        private static string GetBranchText(GitStatusSnapshot currentSnapshot) =>
            string.IsNullOrEmpty(currentSnapshot.Branch)
                ? EmptyBranchText
                : currentSnapshot.Branch;

        private static int GetRefreshModeIndex(GitRefreshMode refreshMode) =>
            refreshMode switch
            {
                GitRefreshMode.ManualOnly => 0,
                GitRefreshMode.Timed => 1,
                _ => 2
            };

        private static GitRefreshMode GetRefreshModeByIndex(int refreshModeIndex) =>
            refreshModeIndex <= 0
                ? GitRefreshMode.ManualOnly
                : refreshModeIndex == 1
                    ? GitRefreshMode.Timed
                    : GitRefreshMode.EventDriven;

        private static string FormatChangedFilesCount(GitStatusSnapshot currentSnapshot) =>
            FormatFilesCount(
                currentSnapshot.ChangedEntriesCount,
                currentSnapshot.ChangedMetaEntriesCount,
                currentSnapshot.OutsideAssetsChangedCount);

        private static string FormatDeletedFilesCount(GitStatusSnapshot currentSnapshot) =>
            FormatFilesCount(
                currentSnapshot.DeletedCount,
                currentSnapshot.DeletedMetaEntriesCount,
                currentSnapshot.OutsideAssetsDeletedCount);

        private static string FormatRefreshTime(DateTime refreshTime) =>
            refreshTime == default
                ? NeverRefreshText
                : refreshTime.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

        private static string FormatFilesCount(int totalCount, int metaCount, int outsideAssetsCount)
        {
            var nonMetaCount = Mathf.Max(0, totalCount - metaCount);
            var grandTotal = totalCount + outsideAssetsCount;
            var baseText = metaCount > 0
                ? $"{nonMetaCount} (+{metaCount} {MetaFileSuffix})"
                : nonMetaCount.ToString(CultureInfo.CurrentCulture);
            var expandedText = outsideAssetsCount > 0
                ? $"{baseText} / +{outsideAssetsCount} {OutsideAssetsSuffix}"
                : baseText;

            return grandTotal != nonMetaCount
                ? expandedText + TotalCountSeparator + grandTotal.ToString(CultureInfo.CurrentCulture)
                : expandedText;
        }

        private static string FormatEntryPath(GitStatusEntry entry, bool preferDisplayPath)
        {
            var path = GetEntryPath(entry, preferDisplayPath);
            var originalPath = entry.OriginalPath;

            return entry.Kind is GitStatusKind.Renamed or GitStatusKind.Copied &&
                   !string.IsNullOrEmpty(originalPath)
                ? $"{originalPath}{PathTransitionSeparator}{path}"
                : path;
        }

        private bool MatchesPathSearch(GitStatusEntry entry, bool preferDisplayPath)
        {
            var search = (pathSearch ?? string.Empty).Trim();
            var path = GetEntryPath(entry, preferDisplayPath);

            return string.IsNullOrEmpty(search) || FileNameMatches(path, search) ||
                   FileNameMatches(entry.OriginalPath, search);
        }

        private static bool ShouldShowEntry(GitStatusEntry entry) => UnityGitStatusSettings.ShowMetaFiles || !entry.IsMeta;

        private static List<GitStatusEntry> GetWindowEntries(
            GitStatusSnapshot currentSnapshot,
            bool preferDisplayPath)
        {
            var entries = UnityGitStatusSettings.ShowMetaFiles
                ? currentSnapshot.Entries
                : currentSnapshot.AssetStatuses.Values
                    .Where(entry => !entry.IsFolderAggregate && !entry.IsDeleted && !entry.IsMeta)
                    .Concat(currentSnapshot.DeletedEntries.Where(entry => !entry.IsMeta));
            return GitStatusEntrySorter.Sort(entries, UnityGitStatusSettings.StatusSortMode, preferDisplayPath);
        }

        private static string GetEntryPath(GitStatusEntry entry, bool preferDisplayPath) =>
            preferDisplayPath && !string.IsNullOrEmpty(entry.DisplayPath)
                ? entry.DisplayPath
                : entry.Path;

        private static bool FileNameMatches(string path, string search)
        {
            var normalized = GitStatusEntry.NormalizePath(path);

            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            var slashIndex = normalized.LastIndexOf('/');
            var fileName = slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
            return fileName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GUIStyle GetFooterStyle(TextAnchor alignment, FontStyle fontStyle) =>
            new(EditorStyles.miniLabel)
            {
                alignment = alignment,
                fontStyle = fontStyle
            };

        #endregion
    }
}
