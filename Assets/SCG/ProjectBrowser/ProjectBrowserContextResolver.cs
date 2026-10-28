using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SCG.UnityGitStatus
{
    /// <summary>
    /// Resolves the active Project Browser instance and pane geometry for overlay callbacks.
    /// The resolver bridges Unity internal editor state through reflection so badge drawing can distinguish one-column and two-column panes.
    /// Failures are reported once and then treated as a soft disable for Project Browser-specific features.
    /// </summary>
    internal static class ProjectBrowserContextResolver
    {
        #region Fields

        private static readonly Type s_projectBrowserType = typeof(Editor).Assembly.GetType(Constants.ProjectBrowserTypeName);

        private static readonly Type s_guiClipType = typeof(GUI).Assembly.GetType(Constants.GuiClipTypeName);

        private static readonly Type s_guiViewType = typeof(Editor).Assembly.GetType(Constants.GuiViewTypeName);

        private static readonly Type s_hostViewType = typeof(Editor).Assembly.GetType(Constants.HostViewTypeName);

        private static readonly MethodInfo s_getProjectBrowserIfExistsMethod = typeof(ProjectWindowUtil)
            .GetMethod(Constants.GetProjectBrowserIfExistsMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo s_getAllProjectBrowsersMethod = s_projectBrowserType
            ?.GetMethod(Constants.GetAllProjectBrowsersMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo s_getActiveFolderPathMethod = typeof(ProjectWindowUtil)
            .GetMethod(Constants.GetActiveFolderPathMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo s_isTwoColumnsMethod = s_projectBrowserType
            ?.GetMethod(Constants.IsTwoColumnsMethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo s_projectBrowserGetActiveFolderPathMethod = s_projectBrowserType
            ?.GetMethod(Constants.GetActiveFolderPathMethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo s_listAreaRectField = s_projectBrowserType
            ?.GetField(Constants.ListAreaRectFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo s_treeViewRectField = s_projectBrowserType
            ?.GetField(Constants.TreeViewRectFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo s_lastInteractedProjectBrowserField = s_projectBrowserType
            ?.GetField(Constants.LastInteractedProjectBrowserFieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo s_visibleRectProperty = s_guiClipType
            ?.GetProperty(Constants.VisibleRectPropertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo s_currentGuiViewProperty = s_guiViewType
            ?.GetProperty(Constants.CurrentPropertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo s_hostViewActualViewProperty = s_hostViewType
            ?.GetProperty(Constants.ActualViewPropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool s_loggedProjectBrowserReflectionFailure;

        private static readonly Dictionary<int, (Rect Viewport, Rect Item, EventType Event)> s_footerPassByBrowser = new();

        private static readonly object[] s_firstItemRectArguments = { 0, 0f };

        private static readonly FieldInfo s_listAreaField = s_projectBrowserType
            ?.GetField(Constants.ListAreaFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo s_groupsField = s_listAreaField?.FieldType
            .GetField(Constants.GroupsFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Type s_groupType = s_groupsField?.FieldType.GenericTypeArguments is { Length: 1 } groupTypes
            ? groupTypes[0]
            : null;

        private static readonly FieldInfo s_gridField = s_groupType
            ?.GetField(Constants.GridFieldName, BindingFlags.Instance | BindingFlags.Public);

        private static readonly MethodInfo s_calcRectMethod = s_gridField?.FieldType
            .GetMethod(Constants.CalcRectMethodName, BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(int), typeof(float) }, null);

        private static readonly PropertyInfo s_groupHeightProperty = s_groupType
            ?.GetProperty(Constants.GroupHeightPropertyName, BindingFlags.Instance | BindingFlags.Public);

        #endregion

        #region Public API

        /// <summary>
        /// Resolves current list geometry on the first item callback of each GUI pass.
        /// Unknown layouts are suppressed rather than estimated from earlier repaints.
        /// </summary>
        /// <param name="context">Browser associated with the current callback.</param>
        /// <param name="itemRect">Current item rectangle in scroll-content coordinates.</param>
        /// <param name="contentBottom">Bottom of the complete list content.</param>
        /// <returns>True for the first item when all required geometry is available.</returns>
        internal static bool TryGetDeletedFooterGeometry(
            ProjectBrowserContext context, Rect itemRect, out float contentBottom)
        {
            contentBottom = float.NaN;
            if (!ShouldResolveFooterGeometry(context.InstanceId, context.VisibleRect, itemRect, Event.current.rawType))
                return false;

            try
            {
                var list = s_listAreaField?.GetValue(context.ProjectBrowser);
                if (list == null ||
                    s_groupsField?.GetValue(list) is not IList { Count: 1 } groups)
                    return false;

                var group = groups[0];
                var grid = s_gridField?.GetValue(group);
                if (grid == null ||
                    s_calcRectMethod?.Invoke(grid, s_firstItemRectArguments) is not Rect firstItemRect ||
                    !Mathf.Approximately(itemRect.y, firstItemRect.y) ||
                    itemRect.x < firstItemRect.xMin || itemRect.x >= firstItemRect.xMax ||
                    s_groupHeightProperty?.GetValue(group) is not float height)
                    return false;

                contentBottom = height;
                return true;
            }
            catch (Exception exception) when (
            exception is ArgumentException
                      or TargetException
                      or TargetInvocationException
                      or MemberAccessException)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// Evaluates callback traversal using controlled browser and event data.
        /// </summary>
        /// <param name="browserId">Browser identifier.</param>
        /// <param name="viewport">Current clipping rectangle.</param>
        /// <param name="itemRect">Current item rectangle.</param>
        /// <param name="eventType">Unconsumed GUI event type.</param>
        /// <returns>True when reflection should run for this callback.</returns>
        internal static bool ShouldResolveFooterGeometryForTests(int browserId, Rect viewport, Rect itemRect, EventType eventType) =>
            ShouldResolveFooterGeometry(browserId, viewport, itemRect, eventType);
#endif

        /// <summary>
        /// Attempts to resolve the Project Browser context associated with the current Project row callback.
        /// The method uses Unity internal geometry and active-view data to identify the correct browser instance and pane.
        /// </summary>
        /// <param name="itemRect">Visible Project row rect supplied by Unity.</param>
        /// <param name="context">Resolved Project Browser context when the operation succeeds.</param>
        /// <returns>True when a Project Browser context was resolved successfully; otherwise false.</returns>
        internal static bool TryGetContext(Rect itemRect, out ProjectBrowserContext context)
        {
            context = default;

            try
            {
                var visibleRect = s_visibleRectProperty?.GetValue(null, null) is Rect currentVisibleRect ? currentVisibleRect : default;

                if (visibleRect is not { width: > 0f, height: > 0f })
                {
                    return false;
                }

                if (!TryResolveProjectBrowser(itemRect, visibleRect, out var projectBrowser, out var listAreaRect, out var treeViewRect))
                {
                    return false;
                }

                var isTwoColumns = s_isTwoColumnsMethod?.Invoke(projectBrowser, null) is true;
                var pane = ResolvePane(
                    isTwoColumns,
                    itemRect,
                    listAreaRect,
                    treeViewRect,
                    visibleRect);
                context = new ProjectBrowserContext(
                    projectBrowser,
                    listAreaRect,
                    treeViewRect,
                    visibleRect,
                    isTwoColumns,
                    pane);
                return true;
            }
            catch (ArgumentException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (TargetException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (TargetInvocationException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (MethodAccessException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (InvalidCastException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
        }

        /// <summary>
        /// Resolves the active folder path for the Project window selected by Unity.
        /// This method relies on Unity internal ProjectWindowUtil helpers as a global fallback.
        /// An empty string is returned when no active Project folder can be resolved.
        /// </summary>
        /// <returns>Active Unity folder path, or an empty string when unavailable.</returns>
        internal static string GetActiveFolderPath()
        {
            var activeFolderPath = TryGetActiveFolderPathFromUnity();

            if (string.IsNullOrEmpty(activeFolderPath))
                return string.Empty;

            var normalizedPath = GitStatusEntry.NormalizePath(activeFolderPath);
            return string.Equals(normalizedPath, Constants.AssetsRoot, StringComparison.Ordinal) ||
                normalizedPath.StartsWith(Constants.AssetsRootWithSeparator, StringComparison.Ordinal)
                    ? normalizedPath
                    : string.Empty;
        }

        /// <summary>
        /// Resolves the active folder path for a specific Project Browser context.
        /// Instance-level resolution is preferred so multiple open Project windows do not leak state into one another.
        /// The method falls back to Unity global helpers when the instance path cannot be resolved.
        /// </summary>
        /// <param name="context">Project Browser context whose active folder should be resolved.</param>
        /// <returns>Active Unity folder path for the context, or an empty string when unavailable.</returns>
        internal static string GetActiveFolderPath(ProjectBrowserContext context)
        {
            var activeFolderPath = TryGetActiveFolderPathFromProjectBrowser(context.ProjectBrowser);

            if (string.IsNullOrEmpty(activeFolderPath))
            {
                activeFolderPath = TryGetActiveFolderPathFromUnity();
            }

            if (string.IsNullOrEmpty(activeFolderPath))
            {
                return string.Empty;
            }

            var normalizedPath = GitStatusEntry.NormalizePath(activeFolderPath);
            return string.Equals(normalizedPath, Constants.AssetsRoot, StringComparison.Ordinal) ||
                normalizedPath.StartsWith(Constants.AssetsRootWithSeparator, StringComparison.Ordinal)
                    ? normalizedPath
                    : string.Empty;
        }

        /// <summary>
        /// Determines whether two rects should be treated as equivalent for Project Browser pane matching.
        /// Minor coordinate differences are tolerated because Unity IMGUI geometry can vary by a few pixels between callbacks.
        /// This keeps pane resolution stable across repaints and minor layout jitter.
        /// </summary>
        /// <param name="left">First rect to compare.</param>
        /// <param name="right">Second rect to compare.</param>
        /// <returns>True when both rects are close enough to be treated as equivalent; otherwise false.</returns>
        internal static bool AreRectsSimilar(Rect left, Rect right)
        {
            const float tolerance = 6f;
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                Mathf.Abs(left.y - right.y) <= tolerance &&
                Mathf.Abs(left.width - right.width) <= tolerance &&
                Mathf.Abs(left.height - right.height) <= tolerance;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Tracks ordered item traversal separately for each browser and skips subsequent callbacks in a GUI pass.
        /// A changed event, viewport, or item size, or a return to an earlier item, starts a new traversal.
        /// </summary>
        /// <param name="browserId">Browser identifier.</param>
        /// <param name="viewport">Current clipping rectangle.</param>
        /// <param name="itemRect">Current item rectangle.</param>
        /// <param name="eventType">Unconsumed GUI event type.</param>
        /// <returns>True for the first callback of a traversal.</returns>
        private static bool ShouldResolveFooterGeometry(int browserId, Rect viewport, Rect itemRect, EventType eventType)
        {
            var continuesPass = s_footerPassByBrowser.TryGetValue(browserId, out var previous) &&
                previous.Event == eventType && previous.Viewport == viewport &&
                previous.Item.size == itemRect.size &&
                (itemRect.y > previous.Item.y || (Mathf.Approximately(itemRect.y, previous.Item.y) && itemRect.x > previous.Item.x));
            s_footerPassByBrowser[browserId] = (viewport, itemRect, eventType);
            return !continuesPass;
        }

        private static ProjectBrowserPane ResolvePane(
            bool isTwoColumns,
            Rect itemRect,
            Rect listAreaRect,
            Rect treeViewRect,
            Rect visibleRect) =>
            !isTwoColumns
                ? ProjectBrowserPane.OneColumn
                : TryResolvePaneFromVisibleRect(listAreaRect, treeViewRect, visibleRect, out var isRightPane)
                    ? isRightPane
                        ? ProjectBrowserPane.TwoColumnRightList
                        : ProjectBrowserPane.TwoColumnLeftTree
                    : TryResolvePaneRect(
                        itemRect,
                        visibleRect,
                        listAreaRect,
                        treeViewRect,
                        out var isListPane)
                        ? isListPane
                            ? ProjectBrowserPane.TwoColumnRightList
                            : ProjectBrowserPane.TwoColumnLeftTree
                        : IsCurrentRightPane(listAreaRect, treeViewRect, visibleRect)
                            ? ProjectBrowserPane.TwoColumnRightList
                            : ProjectBrowserPane.TwoColumnLeftTree;

        private static bool TryResolvePaneRect(
            Rect itemRect,
            Rect visibleRect,
            Rect listAreaRect,
            Rect treeViewRect,
            out bool isListPane)
        {
            var rawListScore = GetOverlapScore(itemRect, listAreaRect);
            var rawTreeScore = GetOverlapScore(itemRect, treeViewRect);
            var translatedItemRect = new Rect(
                itemRect.x + visibleRect.x,
                itemRect.y + visibleRect.y,
                itemRect.width,
                itemRect.height);
            var translatedListScore = GetOverlapScore(translatedItemRect, listAreaRect);
            var translatedTreeScore = GetOverlapScore(translatedItemRect, treeViewRect);
            var rawBestScore = Mathf.Max(rawListScore, rawTreeScore);
            var translatedBestScore = Mathf.Max(translatedListScore, translatedTreeScore);
            var useTranslatedItemRect = translatedBestScore > rawBestScore;
            var listScore = useTranslatedItemRect ? translatedListScore : rawListScore;
            var treeScore = useTranslatedItemRect ? translatedTreeScore : rawTreeScore;

            if (listScore <= 0f && treeScore <= 0f)
            {
                isListPane = false;
                return false;
            }

            isListPane = listScore >= treeScore;
            return true;
        }

        private static bool IsCurrentRightPane(Rect listAreaRect, Rect treeViewRect, Rect visibleRect) =>
            !TryResolvePaneFromVisibleRect(listAreaRect, treeViewRect, visibleRect, out var isRightPane) || isRightPane;

        private static bool TryResolvePaneFromVisibleRect(
            Rect listAreaRect,
            Rect treeViewRect,
            Rect visibleRect,
            out bool isRightPane)
        {
            var listScore = GetRectMatchScore(visibleRect, listAreaRect);
            var treeScore = GetRectMatchScore(visibleRect, treeViewRect);

            if (listScore > 0f || treeScore > 0f)
            {
                isRightPane = listScore >= treeScore;
                return true;
            }

            var listContained = IsRectContained(visibleRect, listAreaRect);
            var treeContained = IsRectContained(visibleRect, treeViewRect);

            if (listContained != treeContained)
            {
                isRightPane = listContained;
                return true;
            }

            isRightPane = false;
            return false;
        }

        private static float GetRectMatchScore(Rect source, Rect target) =>
            AreRectsSimilar(source, target)
                ? float.MaxValue
                : GetOverlapScore(source, target);

        private static float GetOverlapScore(Rect source, Rect target)
        {
            var xMin = Mathf.Max(source.xMin, target.xMin);
            var yMin = Mathf.Max(source.yMin, target.yMin);
            var xMax = Mathf.Min(source.xMax, target.xMax);
            var yMax = Mathf.Min(source.yMax, target.yMax);

            return xMax <= xMin || yMax <= yMin ? 0f : (xMax - xMin) * (yMax - yMin);
        }

        private static bool IsRectContained(Rect container, Rect target)
        {
            const float tolerance = 6f;
            return container.xMin <= target.xMin + tolerance &&
                container.xMax >= target.xMax - tolerance &&
                container.yMin <= target.yMin + tolerance &&
                container.yMax >= target.yMax - tolerance;
        }

        private static bool TryResolveProjectBrowser(
            Rect itemRect,
            Rect visibleRect,
            out object projectBrowser,
            out Rect listAreaRect,
            out Rect treeViewRect)
        {
            if (TryGetCurrentProjectBrowser(out projectBrowser, out listAreaRect, out treeViewRect))
                return true;

            var translatedItemRect = new Rect(
                itemRect.x + visibleRect.x,
                itemRect.y + visibleRect.y,
                itemRect.width,
                itemRect.height);
            var bestGeometryScore = float.MinValue;
            var bestAffinityScore = float.MinValue;

            foreach (var candidate in EnumerateProjectBrowsers())
            {
                if (!TryGetProjectBrowserRects(candidate, out var candidateListAreaRect, out var candidateTreeViewRect))
                {
                    continue;
                }

                var candidateGeometryScore = GetProjectBrowserGeometryScore(
                    visibleRect,
                    translatedItemRect,
                    candidateListAreaRect,
                    candidateTreeViewRect);
                var candidateAffinityScore = GetProjectBrowserWindowAffinityScore(candidate);

                if (candidateGeometryScore < bestGeometryScore ||
                    (Mathf.Approximately(candidateGeometryScore, bestGeometryScore) &&
                     candidateAffinityScore <= bestAffinityScore))
                    continue;

                bestGeometryScore = candidateGeometryScore;
                bestAffinityScore = candidateAffinityScore;
                projectBrowser = candidate;
                listAreaRect = candidateListAreaRect;
                treeViewRect = candidateTreeViewRect;
            }

            return projectBrowser != null;
        }

        private static bool TryGetCurrentProjectBrowser(out object projectBrowser, out Rect listAreaRect, out Rect treeViewRect)
        {
            projectBrowser = null;
            listAreaRect = default;
            treeViewRect = default;

            try
            {
                if (s_currentGuiViewProperty?.GetValue(null, null) is not { } currentGuiView ||
                    s_hostViewType == null ||
                    !s_hostViewType.IsInstanceOfType(currentGuiView) ||
                    s_hostViewActualViewProperty?.GetValue(currentGuiView, null) is not { } actualView ||
                    s_projectBrowserType == null ||
                    !s_projectBrowserType.IsInstanceOfType(actualView) ||
                    !TryGetProjectBrowserRects(actualView, out listAreaRect, out treeViewRect))
                {
                    return false;
                }

                projectBrowser = actualView;
                return true;
            }
            catch (ArgumentException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (TargetException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (TargetInvocationException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (MethodAccessException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
            catch (InvalidCastException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return false;
            }
        }

        private static IEnumerable<object> EnumerateProjectBrowsers()
        {
            var yieldedProjectBrowsers = new HashSet<int>();

            if (s_lastInteractedProjectBrowserField?.GetValue(null) is { } lastInteractedProjectBrowser &&
                yieldedProjectBrowsers.Add(lastInteractedProjectBrowser.GetHashCode()))
            {
                yield return lastInteractedProjectBrowser;
            }

            if (s_getAllProjectBrowsersMethod?.Invoke(null, null) is not IEnumerable projectBrowsers)
            {
                if (s_getProjectBrowserIfExistsMethod?.Invoke(null, null) is { } fallbackProjectBrowser &&
                    yieldedProjectBrowsers.Add(fallbackProjectBrowser.GetHashCode()))
                {
                    yield return fallbackProjectBrowser;
                }

                yield break;
            }

            foreach (var projectBrowser in projectBrowsers)
            {
                if (projectBrowser == null || !yieldedProjectBrowsers.Add(projectBrowser.GetHashCode()))
                {
                    continue;
                }

                yield return projectBrowser;
            }
        }

        private static bool TryGetProjectBrowserRects(object projectBrowser, out Rect listAreaRect, out Rect treeViewRect)
        {
            listAreaRect = s_listAreaRectField?.GetValue(projectBrowser) is Rect listRect ? listRect : default;
            treeViewRect = s_treeViewRectField?.GetValue(projectBrowser) is Rect treeRect ? treeRect : default;
            return listAreaRect is { width: > 0f, height: > 0f } &&
                treeViewRect is { width: > 0f, height: > 0f };
        }

        private static float GetProjectBrowserGeometryScore(
            Rect visibleRect,
            Rect translatedItemRect,
            Rect listAreaRect,
            Rect treeViewRect)
        {
            var listRectScore = GetRectMatchScore(visibleRect, listAreaRect);
            var treeRectScore = GetRectMatchScore(visibleRect, treeViewRect);
            var listItemScore = GetOverlapScore(translatedItemRect, listAreaRect);
            var treeItemScore = GetOverlapScore(translatedItemRect, treeViewRect);
            return Mathf.Max(listRectScore, treeRectScore, listItemScore, treeItemScore);
        }

        private static float GetProjectBrowserWindowAffinityScore(object projectBrowser)
        {
            const float mouseOverWindowBonus = 0.5f;
            const float focusedWindowBonus = 0.25f;
            var affinityScore = 0f;

            if (ReferenceEquals(projectBrowser, EditorWindow.mouseOverWindow))
            {
                affinityScore += mouseOverWindowBonus;
            }

            if (ReferenceEquals(projectBrowser, EditorWindow.focusedWindow))
            {
                affinityScore += focusedWindowBonus;
            }

            return affinityScore;
        }

        private static string TryGetActiveFolderPathFromUnity()
        {
            try
            {
                return s_getActiveFolderPathMethod?.Invoke(null, null) as string ?? string.Empty;
            }
            catch (ArgumentException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (TargetException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (TargetInvocationException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (MethodAccessException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
        }

        private static string TryGetActiveFolderPathFromProjectBrowser(object projectBrowser)
        {
            try
            {
                return projectBrowser != null &&
                    s_projectBrowserGetActiveFolderPathMethod?.Invoke(projectBrowser, null) is string activeFolderPath
                        ? activeFolderPath
                        : string.Empty;
            }
            catch (ArgumentException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (TargetException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (TargetInvocationException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (MethodAccessException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
            catch (InvalidCastException exception)
            {
                ReportProjectBrowserReflectionFailure(exception);
                return string.Empty;
            }
        }

        private static void ReportProjectBrowserReflectionFailure(Exception exception)
        {
            if (s_loggedProjectBrowserReflectionFailure)
            {
                return;
            }

            s_loggedProjectBrowserReflectionFailure = true;
            Debug.LogWarning("[" + nameof(UnityGitStatus) + "]" + ": ProjectBrowser reflection failed. " + exception.Message);
        }

        #endregion
    }
}
