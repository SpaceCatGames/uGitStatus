using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SCG.UnityGitStatus
{
    /// <inheritdoc/>
    internal sealed partial class GitProjectStatusWindow
    {
        #region Constants

        private const float SplitterWidth = 5f;
        private const float DefaultDiffPanelRatio = 0.5f;
        private const float MaximumDiffPanelRatio = 0.75f;
        private const float MinimumPaneWidth = 180f;
        private const float DiffLineNumberWidth = 42f;
        private const float DiffSelectionWidth = 20f;
        private const double OperationSuccessMessageDurationSeconds = 5d;

        #endregion

        #region Fields

        private GitStatusEntry selectedEntry;
        private GitFileDiff stagedDiff;
        private GitFileDiff unstagedDiff;
        private Vector2 diffScrollPosition;
        private string selectedPath = string.Empty;
        private string operationMessage = string.Empty;
        private double operationMessageClearAt;
        private bool splitterDragging;
        private float diffPanelWidth;

        #endregion

        #region Workspace

        private void DrawChangesWorkspace(IReadOnlyList<GitStatusEntry> entries, bool preferDisplayPath, bool noEntries)
        {
            var hasSelection = selectedEntry != null && !string.IsNullOrEmpty(selectedPath);
            var availableWidth = Mathf.Max(0f, position.width - 12f);

            if (hasSelection && diffPanelWidth <= 0f)
            {
                diffPanelWidth = EditorPrefs.GetFloat(Constants.DiffPanelWidthKey, availableWidth * DefaultDiffPanelRatio);
            }

            if (hasSelection)
            {
                diffPanelWidth = Mathf.Clamp(
                    diffPanelWidth,
                    Mathf.Min(MinimumPaneWidth, availableWidth * DefaultDiffPanelRatio),
                    availableWidth * MaximumDiffPanelRatio);
            }

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                var leftWidth = hasSelection
                    ? Mathf.Max(MinimumPaneWidth, availableWidth - diffPanelWidth - SplitterWidth)
                    : availableWidth;

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(leftWidth), GUILayout.ExpandHeight(true)))
                {
                    DrawEntries(entries, preferDisplayPath, noEntries);
                }

                if (!hasSelection) return;
                DrawSplitter(availableWidth);

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(diffPanelWidth), GUILayout.ExpandHeight(true)))
                {
                    DrawDiffPanel();
                }
            }
        }

        private void DrawEntries(IReadOnlyList<GitStatusEntry> entries, bool preferDisplayPath, bool noEntries)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            var startIndex = currentPage * EntriesPerPage;
            var endIndex = Mathf.Min(startIndex + EntriesPerPage, entries.Count);

            for (var i = startIndex; i < endIndex; i++)
            {
                DrawEntry(entries[i], preferDisplayPath);
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.LabelField(noEntries ? NoChangedPathsText : NoChangedPathsMatchSearchText);
            }

            EditorGUILayout.EndScrollView();

            var pageCount = Mathf.Max(1, (entries.Count + EntriesPerPage - 1) / EntriesPerPage);
            if (pageCount <= 1) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(currentPage == 0))
                {
                    if (GUILayout.Button("<"))
                    {
                        currentPage--;
                        scrollPosition = Vector2.zero;
                    }
                }

                GUILayout.Label($"{currentPage + 1} / {pageCount}", EditorStyles.centeredGreyMiniLabel);

                using (new EditorGUI.DisabledScope(currentPage >= pageCount - 1))
                {
                    // ReSharper disable once InvertIf
                    if (GUILayout.Button(">"))
                    {
                        currentPage++;
                        scrollPosition = Vector2.zero;
                    }
                }
            }
        }

        private void DrawSplitter(float availableWidth)
        {
            var rect = GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.35f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            var currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && rect.Contains(currentEvent.mousePosition))
            {
                splitterDragging = true;
                currentEvent.Use();
            }
            else if (splitterDragging && currentEvent.type == EventType.MouseDrag)
            {
                diffPanelWidth = Mathf.Clamp(
                    position.width - currentEvent.mousePosition.x,
                    MinimumPaneWidth,
                    availableWidth * MaximumDiffPanelRatio);
                Repaint();
                currentEvent.Use();
            }
            else if (splitterDragging && currentEvent.rawType == EventType.MouseUp)
            {
                splitterDragging = false;
                EditorPrefs.SetFloat(Constants.DiffPanelWidthKey, diffPanelWidth);
                currentEvent.Use();
            }
        }

        #endregion

        #region Diff Drawing

        private void DrawDiffPanel()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(selectedPath, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Constants.ReloadDiffButtonText, EditorStyles.toolbarButton, GUILayout.Width(54f)))
                {
                    LoadSelectedDiffs();
                }
            }

            if (!string.IsNullOrEmpty(operationMessage))
            {
                EditorGUILayout.HelpBox(operationMessage, MessageType.Info);
            }

            diffScrollPosition = EditorGUILayout.BeginScrollView(diffScrollPosition, GUILayout.ExpandHeight(true));
            DrawDiffSection(stagedDiff, Constants.StagedDiffLabel);
            DrawDiffSection(unstagedDiff, Constants.UnstagedDiffLabel);
            EditorGUILayout.EndScrollView();
            DrawDiffActions();
        }

        private void DrawDiffSection(GitFileDiff diff, string label)
        {
            if (diff == null) return;
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            if (diff.RequiresWholeFileAction)
            {
                EditorGUILayout.HelpBox(GetWholeFileDescription(diff), MessageType.None);
                return;
            }

            DrawDiffColumnHeaders(diff);

            foreach (var hunk in diff.Hunks)
            {
                EditorGUILayout.LabelField(hunk.Header, EditorStyles.miniBoldLabel);
                foreach (var line in hunk.Lines) DrawDiffLine(line);
            }
        }

        private void DrawDiffActions()
        {
            if (stagedDiff == null && unstagedDiff == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (stagedDiff != null)
                {
                    if (stagedDiff.RequiresWholeFileAction)
                    {
                        if (GUILayout.Button(Constants.UnstageFileButtonText))
                        {
                            var originalRepositoryPath = selectedEntry.Kind == GitStatusKind.Renamed
                                ? selectedEntry.OriginalRepositoryPath
                                : string.Empty;
                            CompleteOperation(GitDiffService.UnstageFile(
                                selectedEntry.RepositoryPath,
                                originalRepositoryPath));
                        }
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(!stagedDiff.HasSelection))
                        {
                            if (GUILayout.Button(Constants.UnstageSelectedButtonText))
                            {
                                CompleteOperation(GitDiffService.UnstageSelected(stagedDiff));
                            }
                        }
                    }
                }

                if (unstagedDiff == null) return;

                if (unstagedDiff.RequiresWholeFileAction)
                {
                    if (GUILayout.Button(Constants.StageFileButtonText))
                    {
                        CompleteOperation(GitDiffService.StageFile(selectedEntry.RepositoryPath));
                    }

                    // ReSharper disable once InvertIf
                    if (!unstagedDiff.IsConflicted &&
                        GUILayout.Button(Constants.RevertFileButtonText) &&
                        ConfirmRevert(Constants.EntireFileRevertTarget))
                    {
                        var result = selectedEntry.Kind == GitStatusKind.Untracked
                            ? GitDiffService.DeleteUntrackedFile(selectedEntry.RepositoryPath)
                            : GitDiffService.RevertFile(selectedEntry.RepositoryPath);
                        CompleteOperation(result);
                    }

                    return;
                }

                using (new EditorGUI.DisabledScope(!unstagedDiff.HasSelection))
                {
                    if (GUILayout.Button(Constants.StageSelectedButtonText))
                    {
                        CompleteOperation(GitDiffService.StageSelected(unstagedDiff));
                    }

                    if (GUILayout.Button(Constants.RevertSelectedButtonText) &&
                        ConfirmRevert(Constants.SelectedLinesRevertTarget))
                    {
                        CompleteOperation(GitDiffService.RevertSelected(unstagedDiff));
                    }
                }
            }
        }

        private static void DrawDiffLine(GitDiffLine line)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            EditorGUI.DrawRect(rect, GetLineColor(line.Kind));
            var selectionRect = new Rect(rect.x, rect.y, DiffSelectionWidth, rect.height);
            var oldNumberRect = new Rect(selectionRect.xMax, rect.y, DiffLineNumberWidth, rect.height);
            var newNumberRect = new Rect(oldNumberRect.xMax, rect.y, DiffLineNumberWidth, rect.height);
            var textRect = new Rect(newNumberRect.xMax, rect.y, rect.xMax - newNumberRect.xMax, rect.height);

            if (line.IsSelectable) line.IsSelected = EditorGUI.Toggle(selectionRect, line.IsSelected);
            EditorGUI.LabelField(oldNumberRect, line.OldLineNumber == 0 ? string.Empty : line.OldLineNumber.ToString(), EditorStyles.miniLabel);
            EditorGUI.LabelField(newNumberRect, line.NewLineNumber == 0 ? string.Empty : line.NewLineNumber.ToString(), EditorStyles.miniLabel);
            EditorGUI.LabelField(textRect, GetLinePrefix(line.Kind) + line.Content, EditorStyles.miniLabel);
        }

        private static void DrawDiffColumnHeaders(GitFileDiff diff)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var selectionRect = new Rect(rect.x, rect.y, DiffSelectionWidth, rect.height);
            var oldNumberRect = new Rect(rect.x + DiffSelectionWidth, rect.y, DiffLineNumberWidth, rect.height);
            var newNumberRect = new Rect(oldNumberRect.xMax, rect.y, DiffLineNumberWidth, rect.height);
            var selectableLines = diff.Hunks
                .SelectMany(hunk => hunk.Lines)
                .Where(line => line.IsSelectable)
                .ToList();
            var selectedCount = selectableLines.Count(line => line.IsSelected);
            var allSelected = selectableLines.Count > 0 && selectedCount == selectableLines.Count;
            var previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = selectedCount > 0 && !allSelected;
            var nextAllSelected = EditorGUI.Toggle(selectionRect, allSelected);
            EditorGUI.showMixedValue = previousMixedValue;

            if (nextAllSelected != allSelected)
            {
                foreach (var line in selectableLines) line.IsSelected = nextAllSelected;
            }

            EditorGUI.LabelField(oldNumberRect, Constants.BeforeDiffColumnLabel, EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(newNumberRect, Constants.AfterDiffColumnLabel, EditorStyles.miniBoldLabel);
        }

        private static Color GetLineColor(GitDiffLineKind kind) => kind switch
        {
            GitDiffLineKind.Added => new Color(0.1f, 0.5f, 0.15f, 0.25f),
            GitDiffLineKind.Removed => new Color(0.65f, 0.1f, 0.1f, 0.25f),
            GitDiffLineKind.Metadata => new Color(0.3f, 0.3f, 0.3f, 0.2f),
            _ => Color.clear
        };

        private static string GetLinePrefix(GitDiffLineKind kind) => kind switch
        {
            GitDiffLineKind.Added => Constants.AddedDiffLinePrefix,
            GitDiffLineKind.Removed => Constants.RemovedDiffLinePrefix,
            _ => Constants.UnchangedDiffLinePrefix
        };

        #endregion

        #region Actions

        private void LoadSelectedDiffs(bool preserveOperationMessage = false)
        {
            if (selectedEntry == null) return;
            var (sDiff, sError) = GitDiffService.Load(
                selectedPath,
                selectedEntry.RepositoryPath,
                GitDiffSide.Staged,
                selectedEntry.Kind);
            var (unsDiff, unsError) = GitDiffService.Load(
                selectedPath,
                selectedEntry.RepositoryPath,
                GitDiffSide.Unstaged,
                selectedEntry.Kind);
            stagedDiff = sDiff;
            unstagedDiff = unsDiff;
            var loadError = string.Join(
                "\n",
                new[] { sError, unsError }.Where(value => !string.IsNullOrEmpty(value)));

            if (!string.IsNullOrEmpty(loadError) || !preserveOperationMessage)
            {
                operationMessage = loadError;
                operationMessageClearAt = 0d;
            }

            diffScrollPosition = Vector2.zero;
        }

        private void CompleteOperation(GitOperationResult result)
        {
            if (!result.Success)
            {
                operationMessage = result.Error;
                operationMessageClearAt = 0d;
                Repaint();
                return;
            }

            operationMessage = Constants.GitOperationCompletedMessage;
            operationMessageClearAt = EditorApplication.timeSinceStartup + OperationSuccessMessageDurationSeconds;
            LoadSelectedDiffs(true);
            GitStatusCache.RefreshNow();
            GUIUtility.ExitGUI();
        }

        private bool ConfirmRevert(string target) => EditorUtility.DisplayDialog(
            Constants.RevertConfirmationTitle,
            string.Format(Constants.RevertConfirmationMessageFormat, target, selectedPath),
            Constants.RevertConfirmationButtonText,
            Constants.RevertCancelButtonText);

        private static string GetWholeFileDescription(GitFileDiff diff) =>
            diff.IsBinary ? Constants.BinaryWholeFileDescription :
            diff.IsConflicted ? Constants.ConflictedWholeFileDescription :
            diff.IsRenameOrCopy ? Constants.RenameOrCopyWholeFileDescription :
            diff.IsNewFile ? Constants.NewFileWholeFileDescription :
            diff.IsDeletedFile ? Constants.DeletedFileWholeFileDescription :
            Constants.NoLineLevelDiffDescription;

        #endregion
    }
}
