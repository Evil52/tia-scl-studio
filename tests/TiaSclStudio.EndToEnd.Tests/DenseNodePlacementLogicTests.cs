using System;
using System.Collections.Generic;
using System.Windows;
using Xunit;

namespace TiaSclStudio.App
{
    /// <summary>
    /// Placement for a newly dropped node. The search is a deterministic ring walk
    /// outwards from the requested point, so the same drop on the same sheet always
    /// lands in the same place, and the sheet is only ever grown, never shrunk.
    /// </summary>
    public sealed class DenseNodePlacementLogicTests
    {
        private const double NodeWidth = 272.0;
        private const double NodeHeight = 140.0;

        [Fact]
        public void AFreeRequestedPointIsUsedUnchanged()
        {
            var placement = Place(400.0, 300.0, new Rect[0]);

            Assert.Equal(400.0, placement.X);
            Assert.Equal(300.0, placement.Y);
            Assert.Equal(DenseNodePlacementLogic.DefaultSheetWidth, placement.SheetWidth);
            Assert.Equal(DenseNodePlacementLogic.DefaultSheetHeight, placement.SheetHeight);
        }

        [Fact]
        public void AnOccupiedRequestedPointMovesToTheNearestFreeSlot()
        {
            var occupied = new[] { new Rect(100.0, 100.0, NodeWidth, NodeHeight) };

            var placement = Place(100.0, 100.0, occupied);

            Assert.NotEqual(100.0, placement.X);
            Assert.False(DenseNodePlacementLogic.OverlapsWithGap(
                new Rect(placement.X, placement.Y, NodeWidth, NodeHeight),
                occupied[0],
                DenseNodePlacementLogic.NodeGap));
        }

        [Fact]
        public void TheSameRequestOnTheSameSheetAlwaysLandsInTheSamePlace()
        {
            var occupied = new[]
            {
                new Rect(100.0, 100.0, NodeWidth, NodeHeight),
                new Rect(100.0, 268.0, NodeWidth, NodeHeight)
            };

            var first = Place(100.0, 100.0, occupied);
            var second = Place(100.0, 100.0, occupied);

            Assert.Equal(first.X, second.X);
            Assert.Equal(first.Y, second.Y);
            Assert.Equal(first.SheetWidth, second.SheetWidth);
            Assert.Equal(first.SheetHeight, second.SheetHeight);
        }

        [Fact]
        public void EveryPlacementKeepsTheFullGapFromEveryOccupiedRectangle()
        {
            var occupied = new List<Rect>();
            for (var column = 0; column < 4; column++)
            {
                for (var row = 0; row < 3; row++)
                {
                    occupied.Add(new Rect(
                        24.0 + column * (NodeWidth + DenseNodePlacementLogic.NodeGap),
                        24.0 + row * (NodeHeight + DenseNodePlacementLogic.NodeGap),
                        NodeWidth,
                        NodeHeight));
                }
            }

            var placement = Place(24.0, 24.0, occupied);
            var placed = new Rect(placement.X, placement.Y, NodeWidth, NodeHeight);

            Assert.All(occupied, item => Assert.False(DenseNodePlacementLogic.OverlapsWithGap(
                placed,
                item,
                DenseNodePlacementLogic.NodeGap)));
        }

        [Fact]
        public void APlacementPastTheRightEdgeGrowsTheSheetInWholeSteps()
        {
            var placement = Place(1100.0, 300.0, new Rect[0]);

            Assert.Equal(1536.0, placement.SheetWidth);
            Assert.Equal(DenseNodePlacementLogic.DefaultSheetHeight, placement.SheetHeight);
            Assert.True(placement.X + NodeWidth <= placement.SheetWidth);
        }

        [Fact]
        public void APlacementPastTheBottomEdgeGrowsTheSheetInWholeSteps()
        {
            var placement = Place(300.0, 620.0, new Rect[0]);

            Assert.Equal(1024.0, placement.SheetHeight);
            Assert.True(placement.Y + NodeHeight <= placement.SheetHeight);
        }

        [Fact]
        public void ASheetSmallerThanTheDefaultIsNeverReportedSmallerThanTheDefault()
        {
            var placement = DenseNodePlacementLogic.FindNearestFreePlacement(
                100.0,
                100.0,
                NodeWidth,
                NodeHeight,
                new Rect[0],
                300.0,
                200.0);

            Assert.Equal(DenseNodePlacementLogic.DefaultSheetWidth, placement.SheetWidth);
            Assert.Equal(DenseNodePlacementLogic.DefaultSheetHeight, placement.SheetHeight);
        }

        [Theory]
        [InlineData(double.NaN, 100.0)]
        [InlineData(100.0, double.NaN)]
        [InlineData(double.PositiveInfinity, 100.0)]
        [InlineData(-4000.0, -4000.0)]
        public void AnUnusableRequestedPointFallsBackToTheMargin(double x, double y)
        {
            var placement = Place(x, y, new Rect[0]);

            Assert.True(placement.X >= DenseNodePlacementLogic.NodeMargin);
            Assert.True(placement.Y >= DenseNodePlacementLogic.NodeMargin);
        }

        [Fact]
        public void AnUnusableSheetExtentFallsBackToTheDefaultOne()
        {
            var placement = DenseNodePlacementLogic.FindNearestFreePlacement(
                100.0,
                100.0,
                NodeWidth,
                NodeHeight,
                new Rect[0],
                double.NaN,
                0.0);

            Assert.Equal(DenseNodePlacementLogic.DefaultSheetWidth, placement.SheetWidth);
            Assert.Equal(DenseNodePlacementLogic.DefaultSheetHeight, placement.SheetHeight);
        }

        [Fact]
        public void ASheetAlreadyBeyondTheSafeMaximumIsRefused()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DenseNodePlacementLogic.FindNearestFreePlacement(
                    100.0,
                    100.0,
                    NodeWidth,
                    NodeHeight,
                    new Rect[0],
                    DenseNodePlacementLogic.MaximumSheetExtent + 1.0,
                    700.0));
        }

        [Theory]
        [InlineData(double.NaN, NodeHeight)]
        [InlineData(0.0, NodeHeight)]
        [InlineData(-10.0, NodeHeight)]
        [InlineData(NodeWidth, double.PositiveInfinity)]
        [InlineData(NodeWidth, 0.0)]
        public void AnUnusableNodeSizeIsRefused(double width, double height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DenseNodePlacementLogic.FindNearestFreePlacement(
                    100.0,
                    100.0,
                    width,
                    height,
                    new Rect[0],
                    1180.0,
                    700.0));
        }

        [Fact]
        public void ANodeTooLargeForTheSafeMaximumIsRefused()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DenseNodePlacementLogic.FindNearestFreePlacement(
                    100.0,
                    100.0,
                    DenseNodePlacementLogic.MaximumSheetExtent,
                    NodeHeight,
                    new Rect[0],
                    1180.0,
                    700.0));
        }

        [Fact]
        public void DegenerateOccupiedRectanglesAreIgnoredRatherThanBlockingTheDrop()
        {
            var occupied = new[]
            {
                Rect.Empty,
                new Rect(400.0, 300.0, 0.0, 0.0),
                new Rect(new Point(400.0, 300.0), new Size(0.0, NodeHeight))
            };

            var placement = Place(400.0, 300.0, occupied);

            Assert.Equal(400.0, placement.X);
            Assert.Equal(300.0, placement.Y);
        }

        [Fact]
        public void AMissingOccupiedCollectionIsTreatedAsAnEmptySheet()
        {
            var placement = DenseNodePlacementLogic.FindNearestFreePlacement(
                400.0,
                300.0,
                NodeWidth,
                NodeHeight,
                null,
                1180.0,
                700.0);

            Assert.Equal(400.0, placement.X);
            Assert.Equal(300.0, placement.Y);
        }

        [Fact]
        public void RectanglesExactlyOneGapApartDoNotOverlap()
        {
            var first = new Rect(0.0, 0.0, 100.0, 100.0);
            var second = new Rect(128.0, 0.0, 100.0, 100.0);

            Assert.False(DenseNodePlacementLogic.OverlapsWithGap(first, second, 28.0));
        }

        [Fact]
        public void RectanglesCloserThanTheGapOverlap()
        {
            var first = new Rect(0.0, 0.0, 100.0, 100.0);
            var second = new Rect(127.0, 0.0, 100.0, 100.0);

            Assert.True(DenseNodePlacementLogic.OverlapsWithGap(first, second, 28.0));
        }

        [Fact]
        public void TouchingRectanglesDoNotOverlapWhenNoGapIsRequired()
        {
            var first = new Rect(0.0, 0.0, 100.0, 100.0);
            var second = new Rect(100.0, 0.0, 100.0, 100.0);

            Assert.False(DenseNodePlacementLogic.OverlapsWithGap(first, second, 0.0));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(-50.0)]
        public void AnUnusableGapIsTreatedAsNoGapAtAll(double gap)
        {
            var first = new Rect(0.0, 0.0, 100.0, 100.0);

            Assert.True(DenseNodePlacementLogic.OverlapsWithGap(
                first,
                new Rect(50.0, 50.0, 100.0, 100.0),
                gap));
            Assert.False(DenseNodePlacementLogic.OverlapsWithGap(
                first,
                new Rect(100.0, 0.0, 100.0, 100.0),
                gap));
        }

        [Fact]
        public void AnEmptyRectangleNeverOverlaps()
        {
            Assert.False(DenseNodePlacementLogic.OverlapsWithGap(
                Rect.Empty,
                new Rect(0.0, 0.0, 100.0, 100.0),
                28.0));
            Assert.False(DenseNodePlacementLogic.OverlapsWithGap(
                new Rect(0.0, 0.0, 100.0, 100.0),
                Rect.Empty,
                28.0));
        }

        [Fact]
        public void ACompletelyOccupiedMaximumCanvasReportsExhaustion()
        {
            var occupied = new[]
            {
                new Rect(
                    0.0,
                    0.0,
                    DenseNodePlacementLogic.MaximumSheetExtent,
                    DenseNodePlacementLogic.MaximumSheetExtent)
            };

            Assert.Throws<InvalidOperationException>(() =>
                DenseNodePlacementLogic.FindNearestFreePlacement(
                    DenseNodePlacementLogic.NodeMargin,
                    DenseNodePlacementLogic.NodeMargin,
                    NodeWidth,
                    NodeHeight,
                    occupied,
                    DenseNodePlacementLogic.MaximumSheetExtent,
                    DenseNodePlacementLogic.MaximumSheetExtent));
        }

        private static DenseNodePlacementResult Place(
            double x,
            double y,
            IEnumerable<Rect> occupied)
        {
            return DenseNodePlacementLogic.FindNearestFreePlacement(
                x,
                y,
                NodeWidth,
                NodeHeight,
                occupied,
                1180.0,
                700.0);
        }
    }
}
