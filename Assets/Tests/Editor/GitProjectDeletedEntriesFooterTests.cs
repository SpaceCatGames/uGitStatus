using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SCG.UnityGitStatus.Tests
{
    /// <summary>
    /// Verifies that deleted-file panels occupy only confirmed empty space.
    /// </summary>
    public sealed class GitProjectDeletedEntriesFooterTests
    {
        #region Constants

        /// <summary>Controlled text height independent of Editor GUI initialization.</summary>
        private const float TestLineHeight = 20f;

        #endregion

        #region Resolver

        /// <summary>
        /// Resolves geometry once for a sorted grid traversal and again for the next repaint.
        /// </summary>
        [Test]
        public void Resolver_GridTraversal_SkipsSubsequentItems()
        {
            const int browserId = -101;
            var viewport = new Rect(0f, 0f, 300f, 500f);
            var first = new Rect(10f, 10f, 60f, 74f);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, first, EventType.Repaint), Is.True);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport,
                new Rect(80f, 10f, 60f, 74f), EventType.Repaint), Is.False);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport,
                new Rect(10f, 100f, 60f, 74f), EventType.Repaint), Is.False);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, first, EventType.Repaint), Is.True);
        }

        /// <summary>
        /// Keeps event changes, viewport changes, and separate windows independent.
        /// </summary>
        [Test]
        public void Resolver_ContextChanges_StartNewTraversal()
        {
            const int browserId = -102;
            var viewport = new Rect(0f, 0f, 300f, 500f);
            var item = new Rect(14f, 0f, 286f, 16f);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, item, EventType.Layout), Is.True);
            item.y += 16f;
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, item, EventType.Layout), Is.False);
            item.y += 16f;
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, item, EventType.Repaint), Is.True);
            item.y += 16f;
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId - 1, viewport, item, EventType.Repaint), Is.True);
            viewport.y += 100f;
            item.y += 100f;
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, item, EventType.Repaint), Is.True);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, item, EventType.Repaint), Is.True);
            item.y += 16f;
            item.size = new Vector2(60f, 74f);
            Assert.That(ProjectBrowserContextResolver.ShouldResolveFooterGeometryForTests(browserId, viewport, item, EventType.Repaint), Is.True);
        }

        /// <summary>
        /// Checks the external reflection contract against the editor executing this test assembly.
        /// </summary>
        [Test]
        public void Resolver_InstalledEditor_ProvidesRequiredGeometryMembers()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var browserType = typeof(Editor).Assembly.GetType(Constants.ProjectBrowserTypeName);
            var listField = browserType?.GetField(Constants.ListAreaFieldName, fields);
            Assert.That(listField, Is.Not.Null);
            var groupsField = listField.FieldType.GetField(Constants.GroupsFieldName, fields);
            Assert.That(groupsField, Is.Not.Null);
            var groupTypes = groupsField.FieldType.GenericTypeArguments;
            Assert.That(groupTypes.Length, Is.EqualTo(1));
            var gridField = groupTypes[0].GetField(Constants.GridFieldName);
            Assert.That(gridField, Is.Not.Null);
            Assert.That(gridField.FieldType.GetMethod(Constants.CalcRectMethodName,
                new[] { typeof(int), typeof(float) })?.ReturnType, Is.EqualTo(typeof(Rect)));
            Assert.That(groupTypes[0].GetProperty(Constants.GroupHeightPropertyName)?.PropertyType, Is.EqualTo(typeof(float)));
        }

        #endregion

        #region Placement

        /// <summary>
        /// Rejects unavailable content geometry and occupied viewports.
        /// </summary>
        /// <param name="bottom">Bottom of the list content.</param>
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1f)]
        [TestCase(300f)]
        [TestCase(400f)]
        public void Layout_UnknownOrOccupiedContent_IsHidden(float bottom)
        {
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                new Rect(0f, 0f, 200f, 300f), bottom, 1, true, out _, out _, out _), Is.False);
        }

        /// <summary>
        /// Allows a collapsed header at the exact space boundary.
        /// </summary>
        [Test]
        public void Layout_ExactBoundary_FitsWithoutOverlapping()
        {
            var viewport = new Rect(0f, 0f, 200f, 300f);
            var headerHeight = Constants.ProjectDeletedFooterPadding * 2f +
                TestLineHeight + Constants.ProjectDeletedFooterRowSpacing;
            var bottom = viewport.yMax - headerHeight - Constants.ProjectDeletedFooterPadding;

            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, bottom, 4, false, out var rect, out var count, out var summary), Is.True);
            Assert.That(rect.yMin, Is.EqualTo(bottom));
            Assert.That(count, Is.Zero);
            Assert.That(summary, Is.False);
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, bottom + 1f, 4, false, out _, out _, out _), Is.False);
        }

        /// <summary>
        /// Reserves a summary row whenever a short list does not fully fit.
        /// </summary>
        [Test]
        public void Layout_PartialList_ReportsRemainingFiles()
        {
            var rowHeight = TestLineHeight + Constants.ProjectDeletedFooterRowSpacing;
            var available = Constants.ProjectDeletedFooterPadding * 2f + rowHeight * 3f;
            var viewport = new Rect(0f, 0f, 200f, 300f);
            var bottom = viewport.yMax - available - Constants.ProjectDeletedFooterPadding;

            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, bottom, 4, true, out var rect, out var count, out var summary), Is.True);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(summary, Is.True);
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(bottom));
        }

        /// <summary>
        /// Keeps repeated calculations independent of other windows and earlier geometry.
        /// </summary>
        [Test]
        public void Layout_RepeatedAndInterleavedViewports_RemainsStable()
        {
            var viewport = new Rect(0f, 50f, 200f, 500f);
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, 100f, 20, true, out var expected, out var expectedCount, out _), Is.True);
            Assert.That(expectedCount, Is.EqualTo(Constants.ProjectDeletedFooterMaxVisibleItems));

            GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                new Rect(0f, 0f, 200f, 20f), 200f, 20, true, out _, out _, out _);

            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, 100f, 20, true, out var actual, out var actualCount, out var summary), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actualCount, Is.EqualTo(expectedCount));
            Assert.That(summary, Is.True);
        }

        /// <summary>
        /// Omits the remaining-files row when all entries fit.
        /// </summary>
        [Test]
        public void Layout_CompleteList_ShowsEveryFile()
        {
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                new Rect(0f, 0f, 200f, 500f), 30f, 3, true, out _, out var count, out var summary), Is.True);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(summary, Is.False);
        }

        /// <summary>
        /// Uses the only available expanded row to report all remaining files.
        /// </summary>
        [Test]
        public void Layout_OnlySummaryFits_ReportsAllFiles()
        {
            var rowHeight = TestLineHeight + Constants.ProjectDeletedFooterRowSpacing;
            var viewport = new Rect(0f, 0f, 200f, 300f);
            var bottom = viewport.yMax - Constants.ProjectDeletedFooterPadding * 3f - rowHeight * 2f;

            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, bottom, 4, true, out _, out var count, out var summary), Is.True);
            Assert.That(count, Is.Zero);
            Assert.That(summary, Is.True);
        }

        /// <summary>
        /// Recomputes placement after a viewport resize without reusing an earlier result.
        /// </summary>
        [Test]
        public void Layout_ViewportResized_HidesAndRestoresPanel()
        {
            var viewport = new Rect(0f, 0f, 200f, 300f);
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, 100f, 1, true, out _, out _, out _), Is.True);
            viewport.height = 110f;
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, 100f, 1, true, out _, out _, out _), Is.False);
            viewport.height = 300f;
            Assert.That(GitProjectDeletedEntriesFooter.TryGetLayoutForTests(
                viewport, 100f, 1, true, out _, out _, out _), Is.True);
        }

        #endregion
    }
}
