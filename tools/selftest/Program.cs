using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiaSclStudio.App;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Storage;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Storage;
using TiaSclStudio.Diagram.Validation;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Legacy.V17;

namespace TiaSclStudio.SelfTest
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts\\selftest");
                Directory.CreateDirectory(outputDirectory);

                var document = CreateValveDemo();
                var sheet = document.Sheets[0];
                var generator = new DiagramSclGenerator();
                var result = generator.Generate(document.Plant, sheet);

                Assert(result.IsSuccess, FormatIssues(result.Issues));
                AssertEqual(ExpectedValveScl, NormalizeNewLines(result.Scl), "Generated valve SCL");

                var sourceNames = result.GeneratedSources.Select(source => source.FileName).ToArray();
                Assert(
                    sourceNames.SequenceEqual(new[] { "FB_Valve.scl", "90_Instances.scl", "99_FC_CallUnits.scl" }),
                    "Diagram bundle order is wrong: " + string.Join(", ", sourceNames));
                ProjectStorage.WriteSources(outputDirectory, result.GeneratedSources);
                foreach (var source in result.GeneratedSources)
                {
                    AssertHasUtf8Bom(Path.Combine(outputDirectory, source.FileName));
                }

                var projectPath = Path.Combine(outputDirectory, "ValveDemo.tiasclproj");
                var storage = new DiagramProjectStorage();
                storage.Save(projectPath, document);
                var reloaded = storage.Load(projectPath);
                var roundTrip = generator.Generate(reloaded.Plant, reloaded.Sheets[0]);
                Assert(roundTrip.IsSuccess, FormatIssues(roundTrip.Issues));
                AssertEqual(result.Scl, roundTrip.Scl, "Serialization round-trip");

                VerifyTypeMismatch(document);
                VerifyTopologicalOrder(document.Plant.Blocks[0]);
                VerifyCycleDetection(document.Plant.Blocks[0]);
                VerifyCoreGenerator(document.Plant.Blocks[0]);
                VerifyDefaultInput(document);
                VerifyStalePinContracts(document);
                VerifyFunctionReturnContract();
                VerifyLocalSymbolAllocation(document.Plant.Blocks[0]);
                VerifyLogicNodeFactoryContract();
                VerifyLogicOperationGeneration();
                VerifyNestedLogicAndFanOut();
                VerifyLogicValidationFailures();
                VerifyLogicCyclesAndPersistence(document.Plant.Blocks[0]);
                VerifyDiagramProjectCompilerInputs();
                VerifyDiagramProjectCompilerBundle();
                VerifyDiagramProjectCompilerCollisions();
                VerifyAtomicProjectSave(document);
                VerifyProjectFormatVersionCompatibility();
                VerifyIdentityAndSheetNameValidation(document);
                VerifyOnlyCycleMembersAreReported(document.Plant.Blocks[0]);
                VerifyOfflineGatewayCannotExport();
                VerifyOwnershipFamilyStamping();
                VerifyBlockEditorCollisionPreflight();
                VerifyBlockEditorStableSynchronization();
                VerifyBlockEditorFunctionTransitions();
                VerifyNodeEditorCancelIsolation();
                VerifyBlockCallNodeEditing();
                VerifyConstantNodeEditing();
                VerifyIncompatibleNodeEditIsAtomic();
                VerifyTagNodeEditingPropagation();
                VerifyNodeEditGlobalCollisions();
                VerifyDiagramCrossDomainSymbolCollisions();
                VerifyNodeEditApplyRevalidatesStaleResult();
                VerifyNodePropertyEditUndoIsByteEquivalent();
                VerifyNodeEditingHardeningIsAtomic();
                VerifyNodeCreationFactoriesAndApply();
                VerifyNodeCreationValidationAndAtomicity();
                VerifyNodeCreationGlobalCollisions();
                VerifyNodeCreationHistoryStorageAndCompiler();
                VerifyLogicNodeEditingPreservesIdentity();
                VerifyLogicNodeEditingRejectsConnectedChangesAtomically();
                VerifyNoteEditingAndUndoRoundTrip();
                VerifyDiagramGroups();
                VerifyDiagramViewportFitLogic();
                VerifyDenseNodePlacementLogic(outputDirectory);
                VerifyAutoLayoutFlowCrossingsAndSourcePreservation();
                VerifyAutoLayoutCyclesDisconnectedAndGroups();
                VerifyAutoLayoutDenseHundredAndHistory();
                VerifyAutoLayoutDeterminismAndAtomicity();
                VerifyDiagramProjectSnapshots();
                VerifyDiagramEditHistory();
                VerifyDiagramHistoryGuards();

                Console.WriteLine("TIA SCL Studio self-test passed.");
                Console.WriteLine("Artifacts: " + outputDirectory);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SELFTEST FAILED");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void VerifyDiagramViewportFitLogic()
        {
            var normalBounds = new System.Windows.Rect(100.0, 200.0, 800.0, 400.0);
            var normalZoom = DiagramViewportFitLogic.CalculateZoom(
                normalBounds,
                1000.0,
                600.0,
                50.0,
                0.15,
                2.0);
            Assert(
                Math.Abs(normalZoom - 1.125) < 0.000001,
                "Viewport fit did not select the limiting axis.");
            Assert(
                DiagramViewportFitLogic.FitsAtZoom(
                    normalBounds,
                    1000.0,
                    600.0,
                    50.0,
                    normalZoom),
                "Normal viewport fit was reported as clipped.");

            var maximumZoom = DiagramViewportFitLogic.CalculateZoom(
                new System.Windows.Rect(0.0, 0.0, 10.0, 10.0),
                1000.0,
                600.0,
                20.0,
                0.15,
                2.0);
            Assert(
                Math.Abs(maximumZoom - 2.0) < 0.000001,
                "Viewport fit did not apply the maximum zoom clamp.");

            var hugeBounds = new System.Windows.Rect(0.0, 0.0, 10000.0, 10000.0);
            var minimumZoom = DiagramViewportFitLogic.CalculateZoom(
                hugeBounds,
                1000.0,
                600.0,
                50.0,
                0.15,
                2.0);
            Assert(
                Math.Abs(minimumZoom - 0.15) < 0.000001,
                "Viewport fit did not apply the minimum zoom clamp.");
            Assert(
                !DiagramViewportFitLogic.FitsAtZoom(
                    hugeBounds,
                    1000.0,
                    600.0,
                    50.0,
                    minimumZoom),
                "A diagram larger than the minimum zoom viewport was reported as fully visible.");

            var invalidZoom = DiagramViewportFitLogic.CalculateZoom(
                System.Windows.Rect.Empty,
                double.NaN,
                600.0,
                50.0,
                0.15,
                2.0);
            Assert(
                Math.Abs(invalidZoom - 0.15) < 0.000001,
                "Viewport fit invalid-input fallback is not deterministic.");

            var centeredOffset = DiagramViewportFitLogic.CalculateCenteredOffset(
                normalBounds,
                normalZoom,
                1000.0,
                600.0);
            Assert(
                Math.Abs(centeredOffset.X - 62.5) < 0.000001 &&
                Math.Abs(centeredOffset.Y - 150.0) < 0.000001,
                "Viewport fit center offset is wrong.");
            var invalidOffset = DiagramViewportFitLogic.CalculateCenteredOffset(
                System.Windows.Rect.Empty,
                double.NaN,
                1000.0,
                600.0);
            Assert(
                invalidOffset.X == 0.0 && invalidOffset.Y == 0.0,
                "Viewport fit invalid offset fallback is not the origin.");

            bool fitSelection;
            Assert(
                DiagramViewportFitLogic.TryResolveShortcut(
                    System.Windows.Input.Key.D0,
                    System.Windows.Input.ModifierKeys.Control,
                    out fitSelection) &&
                !fitSelection,
                "Ctrl+0 was not resolved as Fit All.");
            Assert(
                DiagramViewportFitLogic.TryResolveShortcut(
                    System.Windows.Input.Key.NumPad0,
                    System.Windows.Input.ModifierKeys.Control |
                        System.Windows.Input.ModifierKeys.Shift,
                    out fitSelection) &&
                fitSelection,
                "Ctrl+Shift+NumPad0 was not resolved as Fit Selection.");
            Assert(
                !DiagramViewportFitLogic.TryResolveShortcut(
                    System.Windows.Input.Key.D0,
                    System.Windows.Input.ModifierKeys.Control |
                        System.Windows.Input.ModifierKeys.Alt,
                    out fitSelection),
                "Alt-modified Fit shortcut was accepted.");
        }

        private static void VerifyDenseNodePlacementLogic(string outputDirectory)
        {
            const double nodeWidth = 272.0;
            const double nodeHeight = 168.0;
            const double requestedX = 454.0;
            const double requestedY = 266.0;
            var occupied = new List<System.Windows.Rect>();
            var sheetWidth = DenseNodePlacementLogic.DefaultSheetWidth;
            var sheetHeight = DenseNodePlacementLogic.DefaultSheetHeight;
            var project = new DiagramProject();
            var sheet = new CallSheet
            {
                Name = "FC_Dense100",
                Width = sheetWidth,
                Height = sheetHeight
            };
            project.Sheets.Add(sheet);

            for (var index = 0; index < 100; index++)
            {
                var first = DenseNodePlacementLogic.FindNearestFreePlacement(
                    requestedX,
                    requestedY,
                    nodeWidth,
                    nodeHeight,
                    occupied,
                    sheetWidth,
                    sheetHeight);
                var repeated = DenseNodePlacementLogic.FindNearestFreePlacement(
                    requestedX,
                    requestedY,
                    nodeWidth,
                    nodeHeight,
                    occupied,
                    sheetWidth,
                    sheetHeight);
                Assert(
                    Math.Abs(first.X - repeated.X) < 0.000001 &&
                    Math.Abs(first.Y - repeated.Y) < 0.000001,
                    "Dense node placement is not deterministic at index " + index + ".");

                var candidate = new System.Windows.Rect(
                    first.X,
                    first.Y,
                    nodeWidth,
                    nodeHeight);
                Assert(
                    occupied.All(existing => !DenseNodePlacementLogic.OverlapsWithGap(
                        candidate,
                        existing,
                        DenseNodePlacementLogic.NodeGap)),
                    "Dense node placement overlapped an existing block at index " + index + ".");
                Assert(
                    candidate.Left >= DenseNodePlacementLogic.NodeMargin &&
                    candidate.Top >= DenseNodePlacementLogic.NodeMargin &&
                    candidate.Right + DenseNodePlacementLogic.NodeMargin <= first.SheetWidth &&
                    candidate.Bottom + DenseNodePlacementLogic.NodeMargin <= first.SheetHeight,
                    "Dense node placement escaped the grown sheet at index " + index + ".");
                Assert(
                    first.SheetWidth <= DenseNodePlacementLogic.MaximumSheetExtent &&
                    first.SheetHeight <= DenseNodePlacementLogic.MaximumSheetExtent &&
                    !double.IsNaN(first.SheetWidth) &&
                    !double.IsInfinity(first.SheetWidth) &&
                    !double.IsNaN(first.SheetHeight) &&
                    !double.IsInfinity(first.SheetHeight),
                    "Dense node placement produced an unsafe sheet extent.");

                occupied.Add(candidate);
                sheetWidth = first.SheetWidth;
                sheetHeight = first.SheetHeight;
                sheet.Nodes.Add(new BlockCallNode
                {
                    Title = "V" + (index + 1).ToString("000"),
                    InstanceName = "V" + (index + 1).ToString("000"),
                    X = first.X,
                    Y = first.Y
                });
            }

            sheet.Width = sheetWidth;
            sheet.Height = sheetHeight;
            Assert(
                sheet.Width > DenseNodePlacementLogic.DefaultSheetWidth &&
                sheet.Height > DenseNodePlacementLogic.DefaultSheetHeight,
                "A 100-block diagram did not grow beyond the default sheet extent.");
            Assert(
                occupied.Select(item => item.Location).Distinct().Count() == 100,
                "Dense node placement reused a block position.");

            var densePath = Path.Combine(outputDirectory, "Dense100.tiasclproj");
            var storage = new DiagramProjectStorage();
            storage.Save(densePath, project);
            var reloaded = storage.Load(densePath);
            var reloadedSheet = reloaded.Sheets.Single(item => item.Name == sheet.Name);
            Assert(
                Math.Abs(reloadedSheet.Width - sheet.Width) < 0.000001 &&
                Math.Abs(reloadedSheet.Height - sheet.Height) < 0.000001,
                "Dense sheet extent did not survive save/load.");
            Assert(reloadedSheet.Nodes.Count == 100, "Dense nodes did not survive save/load.");
            for (var index = 0; index < 100; index++)
            {
                Assert(
                    Math.Abs(reloadedSheet.Nodes[index].X - sheet.Nodes[index].X) < 0.000001 &&
                    Math.Abs(reloadedSheet.Nodes[index].Y - sheet.Nodes[index].Y) < 0.000001,
                    "Dense node coordinates did not survive save/load at index " + index + ".");
            }

            AssertThrows<ArgumentOutOfRangeException>(
                () => DenseNodePlacementLogic.FindNearestFreePlacement(
                    0.0,
                    0.0,
                    double.NaN,
                    nodeHeight,
                    occupied,
                    sheetWidth,
                    sheetHeight),
                "Dense placement invalid node width");
            AssertThrows<InvalidOperationException>(
                () => DenseNodePlacementLogic.FindNearestFreePlacement(
                    0.0,
                    0.0,
                    DenseNodePlacementLogic.MaximumSheetExtent,
                    nodeHeight,
                    occupied,
                    sheetWidth,
                    sheetHeight),
                "Dense placement oversized node");
        }

        private static void VerifyAutoLayoutFlowCrossingsAndSourcePreservation()
        {
            var project = CreateAutoLayoutPreservationProject();
            var sheet = project.Sheets.Single();
            var compiler = new DiagramProjectCompiler();
            var beforeCompilation = compiler.Compile(project);
            Assert(
                beforeCompilation.IsSuccess,
                "Auto-layout preservation fixture did not compile before layout: " +
                FormatProjectIssues(beforeCompilation.Issues));

            var beforeSources = beforeCompilation.GeneratedSources
                .Select(SourceFingerprint)
                .ToArray();
            var beforeSheetCompilation = beforeCompilation.Sheets.Single().Compilation;
            var beforeGeneratedOrder = beforeSheetCompilation.ExecutionOrder.ToArray();
            var beforeTopologicalOrder = TopologicalSorter.Sort(sheet).OrderedNodeIds.ToArray();
            var beforeNotes = GetAutoLayoutGeneratedNoteOrder(sheet);
            var beforeSemanticState = GetAutoLayoutSemanticState(sheet);
            var beforeWireState = GetWireState(project).ToArray();
            var beforeZoom = sheet.Zoom;
            var beforeWidth = sheet.Width;
            var beforeHeight = sheet.Height;

            var result = SheetAutoLayoutLogic.Apply(
                sheet,
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);

            Assert(result.Changed, "Auto-layout did not report a changed preservation fixture.");
            Assert(result.ExecutionOrderPreserved, "Auto-layout did not confirm execution-order preservation.");
            Assert(result.NodeCount == sheet.Nodes.Count, "Auto-layout result reported the wrong node count.");
            Assert(result.CyclicNodeCount == 0, "Acyclic preservation fixture was reported as cyclic.");
            Assert(
                result.DisconnectedNodeCount == sheet.Nodes.OfType<NoteNode>().Count(),
                "Only notes should be disconnected in the preservation fixture.");
            Assert(sheet.Width >= beforeWidth && sheet.Height >= beforeHeight,
                "Auto-layout shrank the call sheet.");
            Assert(Math.Abs(sheet.Zoom - beforeZoom) < 0.000001,
                "Auto-layout changed the user's zoom setting.");
            AssertEqual(beforeSemanticState, GetAutoLayoutSemanticState(sheet),
                "Auto-layout semantic state");
            Assert(GetWireState(project).SequenceEqual(beforeWireState),
                "Auto-layout changed a wire id or endpoint.");
            Assert(GetAutoLayoutGeneratedNoteOrder(sheet).SequenceEqual(beforeNotes),
                "Auto-layout changed generated note/comment order.");
            Assert(TopologicalSorter.Sort(sheet).OrderedNodeIds.SequenceEqual(beforeTopologicalOrder),
                "Auto-layout changed the complete executable-node order.");

            var afterCompilation = compiler.Compile(project);
            Assert(
                afterCompilation.IsSuccess,
                "Auto-layout made the preservation fixture fail compilation: " +
                FormatProjectIssues(afterCompilation.Issues));
            Assert(
                afterCompilation.GeneratedSources.Select(SourceFingerprint).SequenceEqual(beforeSources),
                "Auto-layout changed the full generated-source bundle bytes.");
            Assert(
                afterCompilation.Sheets.Single().Compilation.ExecutionOrder
                    .SequenceEqual(beforeGeneratedOrder),
                "Auto-layout changed generated FB/FC call execution order.");

            AssertAutoLayoutNodesDoNotOverlap(sheet, 20.0, "Preservation fixture");
            AssertAutoLayoutContentInsideSheet(sheet, "Preservation fixture");
            AssertAutoLayoutRoleColumns(sheet);
            AssertAutoLayoutEdgesMoveForward(sheet);

            var crossingSheet = CreateAutoLayoutCrossingSheet();
            var crossingWireState = GetAutoLayoutWireState(crossingSheet).ToArray();
            Assert(CountAutoLayoutWireCrossings(crossingSheet) == 1,
                "Crossing fixture did not start with one crossing.");
            SheetAutoLayoutLogic.Apply(
                crossingSheet,
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            Assert(CountAutoLayoutWireCrossings(crossingSheet) == 0,
                "Auto-layout did not remove the avoidable crossing.");
            Assert(GetAutoLayoutWireState(crossingSheet).SequenceEqual(crossingWireState),
                "Crossing reduction changed a wire id or endpoint.");
            AssertAutoLayoutNodesDoNotOverlap(crossingSheet, 20.0, "Crossing fixture");
        }

        private static void VerifyAutoLayoutCyclesDisconnectedAndGroups()
        {
            var project = CreateAutoLayoutCycleAndGroupProject();
            var sheet = project.Sheets.Single();
            var beforeSemanticState = GetAutoLayoutSemanticState(sheet);
            var beforeWireState = GetWireState(project).ToArray();
            var groupState = sheet.Groups
                .OrderBy(group => group.Id)
                .Select(GetAutoLayoutGroupSemanticState)
                .ToArray();

            var result = SheetAutoLayoutLogic.Apply(
                sheet,
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);

            Assert(result.Changed, "Cycle/group fixture was not laid out.");
            Assert(result.CyclicNodeCount == 3,
                "Auto-layout did not isolate exactly the two-node SCC and self-loop.");
            Assert(result.DisconnectedNodeCount == 4,
                "Auto-layout did not isolate exactly the four degree-zero nodes.");
            Assert(result.GroupCount == 2, "Auto-layout reported the wrong group count.");
            AssertEqual(beforeSemanticState, GetAutoLayoutSemanticState(sheet),
                "Cycle/group semantic state");
            Assert(GetWireState(project).SequenceEqual(beforeWireState),
                "Cycle/group auto-layout changed wire identity or endpoints.");
            Assert(
                sheet.Groups.OrderBy(group => group.Id)
                    .Select(GetAutoLayoutGroupSemanticState)
                    .SequenceEqual(groupState),
                "Auto-layout changed group identity, hierarchy, text, or direct membership.");

            var cycleNodes = sheet.Nodes.Where(node =>
                node.Title == "Cycle_A" ||
                node.Title == "Cycle_B" ||
                node.Title == "Self_Cycle").ToArray();
            var disconnectedNodes = sheet.Nodes.Where(node =>
                node.Title == "Disconnected_FB" ||
                node.Title == "Disconnected_Tag" ||
                node.Title == "T#17ms" ||
                node.Title == "Disconnected note").ToArray();
            var downstream = sheet.Nodes.Single(node => node.Title == "Downstream");
            var mainNodes = sheet.Nodes.Except(cycleNodes).Except(disconnectedNodes).ToArray();
            var mainBottom = mainNodes.Max(node => node.Y + GetAutoLayoutNodeHeight(node));
            var cycleTop = cycleNodes.Min(node => node.Y);
            var cycleBottom = cycleNodes.Max(node => node.Y + GetAutoLayoutNodeHeight(node));
            var disconnectedTop = disconnectedNodes.Min(node => node.Y);

            Assert(cycleTop > mainBottom,
                "Cyclic nodes were not moved to a zone below the main graph.");
            Assert(disconnectedTop > cycleBottom,
                "Disconnected nodes were not moved below the cyclic zone.");
            Assert(downstream.Y < cycleTop,
                "A downstream node outside the SCC was incorrectly quarantined as cyclic.");
            AssertAutoLayoutNodesDoNotOverlap(sheet, 20.0, "Cycle/group fixture");
            AssertAutoLayoutContentInsideSheet(sheet, "Cycle/group fixture");
            AssertAutoLayoutGroupsContainContents(sheet);
        }

        private static void VerifyAutoLayoutDenseHundredAndHistory()
        {
            var project = CreateAutoLayoutDenseProject();
            var before = CloneProject(project);
            var beforeBytes = SerializeProjectBytes(before);
            var sheet = project.Sheets.Single(item => item.Name == "FC_AutoLayoutDense100");
            var untouchedSheet = project.Sheets.Single(item => item.Name == "FC_AutoLayoutUntouched");
            var untouchedState = GetAutoLayoutFullState(untouchedSheet);
            var compiler = new DiagramProjectCompiler();
            var beforeCompilation = compiler.Compile(project);
            Assert(
                beforeCompilation.IsSuccess,
                "Dense auto-layout fixture did not compile before layout: " +
                FormatProjectIssues(beforeCompilation.Issues));
            var beforeSources = beforeCompilation.GeneratedSources
                .Select(SourceFingerprint)
                .ToArray();
            var beforeExecutionOrder = beforeCompilation.Sheets
                .Single(item => item.SheetId == sheet.Id)
                .Compilation.ExecutionOrder.ToArray();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = SheetAutoLayoutLogic.Apply(
                sheet,
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            watch.Stop();

            Assert(result.Changed, "Dense 100-FB layout reported no change.");
            Assert(result.NodeCount == 100, "Dense layout reported the wrong node count.");
            Assert(result.CyclicNodeCount == 0, "Independent dense FBs were reported as cyclic.");
            Assert(result.DisconnectedNodeCount == 100,
                "Independent dense FBs were not all placed in the disconnected zone.");
            Assert(watch.Elapsed < TimeSpan.FromSeconds(5.0),
                "Dense 100-FB auto-layout exceeded the five-second self-test budget.");
            Assert(sheet.Nodes.Select(node => node.X).Distinct().Count() >= 5,
                "Dense 100-FB auto-layout produced too few columns.");
            Assert(sheet.Nodes.Select(node => node.Y).Distinct().Count() >= 5,
                "Dense 100-FB auto-layout produced a vertical or horizontal strip.");
            Assert(sheet.Width <= DenseNodePlacementLogic.MaximumSheetExtent &&
                sheet.Height <= DenseNodePlacementLogic.MaximumSheetExtent,
                "Dense auto-layout exceeded the safe sheet extent.");
            AssertAutoLayoutNodesDoNotOverlap(sheet, 20.0, "Dense 100-FB fixture");
            AssertAutoLayoutContentInsideSheet(sheet, "Dense 100-FB fixture");

            var fitBounds = GetAutoLayoutContentBounds(sheet);
            fitBounds.Inflate(24.0, 24.0);
            Assert(
                DiagramViewportFitLogic.FitsAtZoom(
                    fitBounds,
                    900.0,
                    560.0,
                    44.0,
                    0.15),
                "Dense 100-FB layout cannot be covered by Fit All at 15% in a 900x560 viewport.");

            var afterCompilation = compiler.Compile(project);
            Assert(
                afterCompilation.IsSuccess,
                "Dense auto-layout made project compilation fail: " +
                FormatProjectIssues(afterCompilation.Issues));
            Assert(
                afterCompilation.GeneratedSources.Select(SourceFingerprint).SequenceEqual(beforeSources),
                "Dense auto-layout changed the complete generated-source bundle.");
            Assert(
                afterCompilation.Sheets.Single(item => item.SheetId == sheet.Id)
                    .Compilation.ExecutionOrder.SequenceEqual(beforeExecutionOrder),
                "Dense auto-layout changed independent FB execution order.");
            AssertEqual(untouchedState, GetAutoLayoutFullState(untouchedSheet),
                "Untouched sheet state");

            var afterBytes = SerializeProjectBytes(project);
            var history = new DiagramEditHistory();
            Assert(history.Record("Auto-layout", before, project),
                "Dense auto-layout was not recorded in history.");
            Assert(history.UndoCount == 1 && history.RedoCount == 0,
                "Auto-layout did not create exactly one Undo operation.");
            var undone = history.Undo();
            Assert(SerializeProjectBytes(undone).SequenceEqual(beforeBytes),
                "One Undo did not restore the complete pre-layout project byte-for-byte.");
            Assert(history.UndoCount == 0 && history.RedoCount == 1,
                "Undo did not expose exactly one Redo operation.");
            var redone = history.Redo();
            Assert(SerializeProjectBytes(redone).SequenceEqual(afterBytes),
                "One Redo did not restore the complete auto-layout project byte-for-byte.");

            var stableState = GetAutoLayoutFullState(sheet);
            var repeated = SheetAutoLayoutLogic.Apply(
                sheet,
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            Assert(!repeated.Changed, "Repeated dense auto-layout was not idempotent.");
            AssertEqual(stableState, GetAutoLayoutFullState(sheet),
                "Repeated dense auto-layout state");
        }

        private static void VerifyAutoLayoutDeterminismAndAtomicity()
        {
            var first = CreateAutoLayoutPreservationProject();
            var second = CloneProject(first);
            second.Sheets[0].Nodes.Reverse();
            second.Sheets[0].Wires.Reverse();
            second.Sheets[0].Groups.Reverse();

            SheetAutoLayoutLogic.Apply(
                first.Sheets[0],
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            SheetAutoLayoutLogic.Apply(
                second.Sheets[0],
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            AssertEqual(
                GetAutoLayoutPositionState(first.Sheets[0]),
                GetAutoLayoutPositionState(second.Sheets[0]),
                "Auto-layout deterministic position state");

            var stableState = GetAutoLayoutFullState(first.Sheets[0]);
            var repeated = SheetAutoLayoutLogic.Apply(
                first.Sheets[0],
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            Assert(!repeated.Changed, "Repeated deterministic auto-layout reported a change.");
            AssertEqual(stableState, GetAutoLayoutFullState(first.Sheets[0]),
                "Repeated deterministic auto-layout state");

            AssertThrows<ArgumentNullException>(
                () => SheetAutoLayoutLogic.Apply(
                    null,
                    GetAutoLayoutNodeWidth,
                    GetAutoLayoutNodeHeight),
                "Auto-layout null sheet");
            AssertThrows<ArgumentNullException>(
                () => SheetAutoLayoutLogic.Apply(
                    first.Sheets[0],
                    null,
                    GetAutoLayoutNodeHeight),
                "Auto-layout null width delegate");
            AssertThrows<ArgumentNullException>(
                () => SheetAutoLayoutLogic.Apply(
                    first.Sheets[0],
                    GetAutoLayoutNodeWidth,
                    null),
                "Auto-layout null height delegate");

            var invalidMetric = CreateAutoLayoutPreservationProject();
            var invalidMetricBytes = SerializeProjectBytes(invalidMetric);
            var invalidMetricId = invalidMetric.Sheets[0].Nodes[1].Id;
            AssertThrows<InvalidOperationException>(
                () => SheetAutoLayoutLogic.Apply(
                    invalidMetric.Sheets[0],
                    node => node.Id == invalidMetricId
                        ? double.NaN
                        : GetAutoLayoutNodeWidth(node),
                    GetAutoLayoutNodeHeight),
                "Auto-layout invalid visual size");
            Assert(SerializeProjectBytes(invalidMetric).SequenceEqual(invalidMetricBytes),
                "Invalid visual size partially mutated the project.");

            var duplicateId = CreateAutoLayoutPreservationProject();
            duplicateId.Sheets[0].Nodes[1].Id = duplicateId.Sheets[0].Nodes[0].Id;
            var duplicateBytes = SerializeProjectBytes(duplicateId);
            AssertThrows<InvalidOperationException>(
                () => SheetAutoLayoutLogic.Apply(
                    duplicateId.Sheets[0],
                    GetAutoLayoutNodeWidth,
                    GetAutoLayoutNodeHeight),
                "Auto-layout duplicate node id");
            Assert(SerializeProjectBytes(duplicateId).SequenceEqual(duplicateBytes),
                "Duplicate node id partially mutated the project.");

            var groupCycle = CreateAutoLayoutPreservationProject();
            var firstGroup = new DiagramGroup("Cycle group A", 100, 100, 300, 180);
            var secondGroup = new DiagramGroup("Cycle group B", 500, 100, 300, 180);
            firstGroup.ParentGroupId = secondGroup.Id;
            secondGroup.ParentGroupId = firstGroup.Id;
            groupCycle.Sheets[0].Groups.Add(firstGroup);
            groupCycle.Sheets[0].Groups.Add(secondGroup);
            var groupCycleBytes = SerializeProjectBytes(groupCycle);
            AssertThrows<InvalidOperationException>(
                () => SheetAutoLayoutLogic.Apply(
                    groupCycle.Sheets[0],
                    GetAutoLayoutNodeWidth,
                    GetAutoLayoutNodeHeight),
                "Auto-layout cyclic group hierarchy");
            Assert(SerializeProjectBytes(groupCycle).SequenceEqual(groupCycleBytes),
                "Cyclic group hierarchy partially mutated the project.");

            var excessiveExtent = CreateAutoLayoutPreservationProject();
            var excessiveExtentBytes = SerializeProjectBytes(excessiveExtent);
            var oversizedId = excessiveExtent.Sheets[0].Nodes[0].Id;
            AssertThrows<InvalidOperationException>(
                () => SheetAutoLayoutLogic.Apply(
                    excessiveExtent.Sheets[0],
                    node => node.Id == oversizedId
                        ? DenseNodePlacementLogic.MaximumSheetExtent + 1.0
                        : GetAutoLayoutNodeWidth(node),
                    GetAutoLayoutNodeHeight),
                "Auto-layout maximum extent guard");
            Assert(SerializeProjectBytes(excessiveExtent).SequenceEqual(excessiveExtentBytes),
                "Maximum extent failure partially mutated the project.");

            var emptySheet = new CallSheet
            {
                Name = "FC_EmptyAutoLayout",
                Width = 2400.0,
                Height = 1600.0,
                Zoom = 0.72
            };
            var emptyState = GetAutoLayoutFullState(emptySheet);
            var emptyResult = SheetAutoLayoutLogic.Apply(
                emptySheet,
                GetAutoLayoutNodeWidth,
                GetAutoLayoutNodeHeight);
            Assert(!emptyResult.Changed && emptyResult.NodeCount == 0,
                "Empty sheet auto-layout did not return a no-op result.");
            AssertEqual(emptyState, GetAutoLayoutFullState(emptySheet),
                "Empty sheet auto-layout state");
        }

        private static void VerifyOfflineGatewayCannotExport()
        {
            using (var gateway = new OfflineGateway())
            {
                var status = gateway.GetStatus();
                Assert(status.Mode == TiaGatewayMode.Offline, "Offline gateway reported an online mode.");
                Assert(!status.IsConnected, "Offline gateway reported a connection.");
                Assert(!status.CanExport, "Offline gateway allowed export.");

                var request = new TiaExportRequest(
                    "never-opened.ap17",
                    new[]
                    {
                        new TiaSourceFile("never-read.scl", 10, TiaSourceKind.Block)
                    },
                    false);
                var preview = gateway.PreviewExport(request);
                Assert(!preview.CanExecute, "Offline preview unexpectedly allowed execution.");
                Assert(
                    string.IsNullOrEmpty(preview.ConfirmationToken),
                    "Offline preview issued a confirmation token.");
                Assert(
                    preview.Diagnostics.Any(item =>
                        item.Code == OpennessDiagnosticCodes.ExportSkippedOffline),
                    "Offline preview diagnostic is missing.");

                var result = gateway.Export(request, "forged-token");
                Assert(
                    result.Outcome != TiaExportOutcome.Succeeded,
                    "Offline gateway accepted a forged confirmation token.");
                Assert(result.ImportedSourceCount == 0, "Offline gateway reported imported sources.");
                Assert(!result.CompilationAttempted, "Offline gateway attempted compilation.");
            }
        }

        private static void VerifyOwnershipFamilyStamping()
        {
            const string source =
                "TYPE \"UDT_Valve\"\r\n" +
                "   STRUCT\r\n" +
                "      Q : Bool;\r\n" +
                "   END_STRUCT;\r\n" +
                "END_TYPE\r\n\r\n" +
                "// FUNCTION_BLOCK \"Ignored_In_Comment\"\r\n" +
                "FUNCTION_BLOCK \"FB_Valve\"\r\n" +
                "{ S7_Optimized_Access := 'TRUE' }\r\n" +
                "VERSION : 0.1\r\n" +
                "VAR_OUTPUT\r\n" +
                "   Q : Bool;\r\n" +
                "END_VAR\r\n" +
                "BEGIN\r\n" +
                "END_FUNCTION_BLOCK\r\n\r\n" +
                "DATA_BLOCK \"DB_Valve\"\r\n" +
                "FAMILY : 'OLD'\r\n" +
                "VERSION : 0.1\r\n" +
                "NON_RETAIN\r\n" +
                "\"FB_Valve\"\r\n" +
                "BEGIN\r\n" +
                "END_DATA_BLOCK\r\n";

            var stamped = SclSourceInspector.StampOwnershipFamily(source, "TSABC123");
            var declarations = SclSourceInspector.FindDeclarations(source);
            Assert(
                declarations.Count == 3 &&
                declarations.All(item => item.Name != "Ignored_In_Comment"),
                "SCL declarations inside comments were not ignored.");
            AssertEqual(
                stamped,
                SclSourceInspector.StampOwnershipFamily(stamped, "TSABC123"),
                "Ownership FAMILY stamping must be deterministic and idempotent");
            Assert(
                CountOccurrences(stamped, "FAMILY : 'TSABC123'") == 2,
                "Ownership FAMILY was not stamped on every PLC block exactly once.");
            Assert(!stamped.Contains("FAMILY : 'OLD'"), "Existing FAMILY was not replaced.");
            Assert(
                stamped.Contains("TYPE \"UDT_Valve\""),
                "PLC data type was unexpectedly changed by FAMILY stamping.");
            Assert(
                !stamped.Contains("Ignored_In_Comment\"\r\nFAMILY"),
                "A declaration inside a comment was stamped.");

            var rejected = false;
            try
            {
                SclSourceInspector.StampOwnershipFamily(
                    "FUNCTION \"FC_Unsafe\" : Void\r\nBEGIN\r\nEND_FUNCTION\r\n",
                    "TSABC123");
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Assert(rejected, "A block without VERSION was stamped instead of being rejected.");
        }

        private static void VerifyBlockEditorCollisionPreflight()
        {
            var target = new BlockDefinition { Name = "FB_Target" };
            var other = new BlockDefinition { Name = "FB_Other" };
            var project = new DiagramProject();
            project.Plant.Name = "EditorCollisionTest";
            project.Plant.Blocks.Add(target);
            project.Plant.Blocks.Add(other);

            var tableCalls = new CallBlockDefinition
            {
                Name = "FC_TableCalls",
                Kind = BlockKind.Function
            };
            var stableUnit = new UnitDefinition("Table_Stable_DB", other);
            var fallbackUnit = new UnitDefinition
            {
                Name = "Table_Fallback_DB",
                BlockId = Guid.NewGuid(),
                BlockName = other.Name,
                UseMultiInstance = false
            };
            tableCalls.Units.Add(stableUnit);
            tableCalls.Units.Add(fallbackUnit);
            project.Plant.CallBlocks.Add(tableCalls);

            var sheet = new CallSheet { Name = "FC_EditorCalls" };
            var globalInstance = DiagramNodeFactory.CreateBlockCall(other, "Other_Global_DB", 10, 10);
            var multiInstance = DiagramNodeFactory.CreateBlockCall(other, "LocalOther", 30, 30);
            multiInstance.UseMultiInstance = true;
            sheet.Nodes.Add(globalInstance);
            sheet.Nodes.Add(multiInstance);
            project.Sheets.Add(sheet);

            var originalTargetName = target.Name;
            var originalNodeCount = sheet.Nodes.Count;

            AssertCandidateCollision(project, target, other.Name, "block name");
            AssertCandidateCollision(project, target, sheet.Name, "sheet name");
            AssertCandidateCollision(project, target, sheet.Name + "_DB", "sheet instance DB");
            AssertCandidateCollision(project, target, globalInstance.InstanceName, "global instance DB");
            AssertCandidateCollision(
                project,
                target,
                stableUnit.Name,
                "table-call unit instance DB resolved by stable BlockId");

            var newCandidate = CloneEditorBlock(target);
            newCandidate.Id = Guid.NewGuid();
            newCandidate.Name = sheet.Name;
            Assert(
                BlockLibraryEditorLogic.ValidateCandidateUsage(project, newCandidate).Count > 0,
                "New block collision with a sheet was not detected.");

            var fallbackCandidate = CloneEditorBlock(target);
            fallbackCandidate.Id = Guid.NewGuid();
            fallbackCandidate.Name = fallbackUnit.Name;
            Assert(
                BlockLibraryEditorLogic.ValidateCandidateUsage(project, fallbackCandidate).Count > 0,
                "New block collision with a table-call unit resolved by fallback BlockName was not detected.");

            var visualReservedInstance = DiagramNodeFactory.CreateBlockCall(
                target,
                stableUnit.Name,
                50,
                50);
            sheet.Nodes.Add(visualReservedInstance);
            originalNodeCount++;
            var visualCandidate = CloneEditorBlock(target);
            var visualErrors = BlockLibraryEditorLogic.ValidateCandidateUsage(project, visualCandidate);
            Assert(
                visualErrors.Any(error =>
                    error.IndexOf(stableUnit.Name, StringComparison.OrdinalIgnoreCase) >= 0),
                "Visual FB instance collision with a Plant.CallBlocks unit DB was not detected.");

            AssertEqual(originalTargetName, target.Name, "Collision preflight mutated the target block");
            Assert(sheet.Nodes.Count == originalNodeCount, "Collision preflight mutated sheet nodes.");
            Assert(
                globalInstance.InstanceName == "Other_Global_DB" && multiInstance.UseMultiInstance,
                "Collision preflight mutated an existing call node.");
        }

        private static void AssertCandidateCollision(
            DiagramProject project,
            BlockDefinition target,
            string candidateName,
            string subject)
        {
            var candidate = CloneEditorBlock(target);
            candidate.Name = candidateName;
            var errors = BlockLibraryEditorLogic.ValidateCandidateUsage(project, candidate);
            Assert(errors.Count > 0, "Editor preflight missed collision with " + subject + ".");
            Assert(
                errors.Any(error => error.IndexOf(candidateName, StringComparison.OrdinalIgnoreCase) >= 0),
                "Editor collision diagnostic does not identify " + subject + ".");
        }

        private static void VerifyBlockEditorStableSynchronization()
        {
            var input = new InterfaceMember("Enable", "Bool", InterfaceSection.Input);
            var output = new InterfaceMember("Ready", "Bool", InterfaceSection.Output);
            var block = new BlockDefinition { Name = "FB_Stable" };
            block.Interface.Add(input);
            block.Interface.Add(output);

            var project = new DiagramProject();
            project.Plant.Name = "EditorSynchronizationTest";
            project.Plant.Blocks.Add(block);

            var first = AddEditorSynchronizationSheet(project, block, "FC_First", true);
            var second = AddEditorSynchronizationSheet(project, block, "FC_Second", false);
            var originalBlockId = block.Id;
            var firstInputPinId = FindParameterPin(first.Call, input.Id).Id;
            var firstOutputPinId = FindParameterPin(first.Call, output.Id).Id;
            var secondInputPinId = FindParameterPin(second.Call, input.Id).Id;
            var secondOutputPinId = FindParameterPin(second.Call, output.Id).Id;
            var originalWireIds = project.Sheets.SelectMany(sheet => sheet.Wires).Select(wire => wire.Id).ToArray();

            var renamed = CloneEditorBlock(block);
            renamed.Name = "FB_Stable_Renamed";
            renamed.Interface = new List<InterfaceMember>
            {
                CloneEditorMember(output, "IsReady"),
                CloneEditorMember(input, "Run")
            };

            var stablePlan = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, renamed);
            Assert(stablePlan.NodeStates.Count == 2, "Editor did not synchronize every sheet.");
            Assert(stablePlan.WireRemovals.Count == 0, "Rename/reorder incorrectly removed compatible wires.");
            BlockLibraryEditorLogic.ApplySynchronization(block, renamed, stablePlan);

            Assert(block.Id == originalBlockId, "Block ID changed during editor synchronization.");
            Assert(
                block.Interface.Select(member => member.Id).SequenceEqual(new[] { output.Id, input.Id }),
                "Member IDs or order were not preserved during rename/reorder.");
            Assert(FindParameterPin(first.Call, input.Id).Id == firstInputPinId, "First-sheet input pin ID changed.");
            Assert(FindParameterPin(first.Call, output.Id).Id == firstOutputPinId, "First-sheet output pin ID changed.");
            Assert(FindParameterPin(second.Call, input.Id).Id == secondInputPinId, "Second-sheet input pin ID changed.");
            Assert(FindParameterPin(second.Call, output.Id).Id == secondOutputPinId, "Second-sheet output pin ID changed.");
            Assert(
                project.Sheets.SelectMany(sheet => sheet.Wires).Select(wire => wire.Id).SequenceEqual(originalWireIds),
                "Compatible wire IDs changed during rename/reorder.");
            Assert(
                first.Call.Title == renamed.Name && second.Call.Title == renamed.Name,
                "Call-node titles were not synchronized on every sheet.");

            var incompatible = CloneEditorBlock(block);
            var incompatibleOutput = incompatible.Interface.Single(member => member.Id == output.Id);
            incompatibleOutput.DataType = "Int";
            var requiredInput = incompatible.Interface.Single(member => member.Id == input.Id);
            requiredInput.Section = InterfaceSection.InOut;

            var firstInputWireId = first.InputWire.Id;
            var incompatiblePlan = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, incompatible);
            Assert(
                incompatiblePlan.WireRemovals.Count == 3,
                "Editor did not identify type/InOut-incompatible wires on all sheets.");
            BlockLibraryEditorLogic.ApplySynchronization(block, incompatible, incompatiblePlan);

            Assert(
                first.Sheet.Wires.Any(wire => wire.Id == firstInputWireId),
                "Writable-tag connection to InOut was incorrectly removed.");
            Assert(
                !second.Sheet.Wires.Any(wire => wire.Id == second.InputWire.Id),
                "Constant connection to InOut was not removed.");
            Assert(
                !first.Sheet.Wires.Any(wire => wire.Id == first.OutputWire.Id) &&
                !second.Sheet.Wires.Any(wire => wire.Id == second.OutputWire.Id),
                "Type-incompatible output wires were not removed.");
            Assert(FindParameterPin(first.Call, input.Id).Id == firstInputPinId, "InOut conversion changed pin ID.");
        }

        private static EditorSheetFixture AddEditorSynchronizationSheet(
            DiagramProject project,
            BlockDefinition block,
            string sheetName,
            bool tagSource)
        {
            var sheet = new CallSheet { Name = sheetName };
            var call = DiagramNodeFactory.CreateBlockCall(block, sheetName + "_Instance", 300, 100);
            CallNode source;
            if (tagSource)
            {
                var sourceTag = new TagDefinition
                {
                    Name = sheetName + "_Source",
                    DataType = "Bool",
                    Address = "%M10.0"
                };
                project.Plant.Tags.Add(sourceTag);
                source = DiagramNodeFactory.CreateTag(sourceTag, TerminalDirection.Source, 20, 100);
            }
            else
            {
                source = DiagramNodeFactory.CreateConstant("TRUE", "Bool", 20, 100);
            }

            var sinkTag = new TagDefinition
            {
                Name = sheetName + "_Sink",
                DataType = "Bool",
                Address = "%M10.1"
            };
            project.Plant.Tags.Add(sinkTag);
            var sink = DiagramNodeFactory.CreateTag(sinkTag, TerminalDirection.Sink, 650, 100);
            sheet.Nodes.Add(source);
            sheet.Nodes.Add(call);
            sheet.Nodes.Add(sink);

            var inputPin = call.Pins.Single(pin => pin.MemberId == block.Interface[0].Id);
            var outputPin = call.Pins.Single(pin => pin.MemberId == block.Interface[1].Id);
            var inputWire = new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = source.Pins[0].Id,
                TargetNodeId = call.Id,
                TargetPinId = inputPin.Id
            };
            var outputWire = new Wire
            {
                SourceNodeId = call.Id,
                SourcePinId = outputPin.Id,
                TargetNodeId = sink.Id,
                TargetPinId = sink.Pins[0].Id
            };
            sheet.Wires.Add(inputWire);
            sheet.Wires.Add(outputWire);
            project.Sheets.Add(sheet);
            return new EditorSheetFixture(sheet, call, inputWire, outputWire);
        }

        private static void VerifyBlockEditorFunctionTransitions()
        {
            var parameter = new InterfaceMember("Value", "DInt", InterfaceSection.Input);
            var block = new BlockDefinition { Name = "FB_ToFunction" };
            block.Interface.Add(parameter);
            var project = new DiagramProject();
            project.Plant.Name = "EditorFunctionTransitionTest";
            project.Plant.Blocks.Add(block);
            var sheet = new CallSheet { Name = "FB_FunctionCalls" };
            var call = DiagramNodeFactory.CreateBlockCall(block, "Nested", 100, 100);
            call.UseMultiInstance = true;
            sheet.Nodes.Add(call);
            project.Sheets.Add(sheet);
            var parameterPinId = call.Pins.Single().Id;

            var function = CloneEditorBlock(block);
            function.Name = "FC_Value";
            function.Kind = BlockKind.Function;
            function.ReturnType = "DInt";
            var toFunction = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, function);
            BlockLibraryEditorLogic.ApplySynchronization(block, function, toFunction);

            var returnPin = call.Pins.Single(pin => pin.Role == PinRole.FunctionReturn);
            var returnPinId = returnPin.Id;
            Assert(!call.UseMultiInstance, "FC transition did not reset UseMultiInstance.");
            Assert(FindParameterPin(call, parameter.Id).Id == parameterPinId, "FB→FC changed parameter pin ID.");
            Assert(returnPin.DataType == "DInt", "FB→FC did not create the function Return pin.");

            var sinkTag = new TagDefinition
            {
                Name = "FunctionResult",
                DataType = "DInt",
                Address = "%MD20"
            };
            project.Plant.Tags.Add(sinkTag);
            var sink = DiagramNodeFactory.CreateTag(sinkTag, TerminalDirection.Sink, 500, 100);
            sheet.Nodes.Add(sink);
            var returnWire = new Wire
            {
                SourceNodeId = call.Id,
                SourcePinId = returnPin.Id,
                TargetNodeId = sink.Id,
                TargetPinId = sink.Pins[0].Id
            };
            sheet.Wires.Add(returnWire);

            var renamedFunction = CloneEditorBlock(block);
            renamedFunction.Name = "FC_Value_Renamed";
            var stableReturnPlan = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, renamedFunction);
            Assert(stableReturnPlan.WireRemovals.Count == 0, "Stable FC Return wire was marked for removal.");
            BlockLibraryEditorLogic.ApplySynchronization(block, renamedFunction, stableReturnPlan);
            Assert(
                call.Pins.Single(pin => pin.Role == PinRole.FunctionReturn).Id == returnPinId,
                "Non-Void FC rename changed Return pin ID.");
            Assert(sheet.Wires.Single().Id == returnWire.Id, "Non-Void FC rename changed Return wire ID.");

            var backToBlock = CloneEditorBlock(block);
            backToBlock.Name = "FB_FromFunction";
            backToBlock.Kind = BlockKind.FunctionBlock;
            backToBlock.ReturnType = "Void";
            var toBlock = BlockLibraryEditorLogic.BuildSynchronizationPlan(project, backToBlock);
            Assert(toBlock.WireRemovals.Count == 1, "FC→FB did not stage Return wire removal.");
            BlockLibraryEditorLogic.ApplySynchronization(block, backToBlock, toBlock);
            Assert(!call.Pins.Any(pin => pin.Role == PinRole.FunctionReturn), "FC→FB retained Return pin.");
            Assert(sheet.Wires.Count == 0, "FC→FB retained Return wire.");
            Assert(!call.UseMultiInstance, "FC→FB unexpectedly restored stale UseMultiInstance.");
        }

        private static void VerifyNodeEditorCancelIsolation()
        {
            var project = CreateValveDemo();
            var before = SerializeProjectBytes(project);
            var originalCall = project.Sheets[0].Nodes.OfType<BlockCallNode>().Single();
            var originalTag = project.Plant.Tags[0];

            var workingCall = (BlockCallNode)NodeEditingLogic.CloneSupportedNode(originalCall);
            var workingTag = NodeEditingLogic.CloneTag(originalTag);
            Assert(!ReferenceEquals(originalCall, workingCall), "Node editor reused the original block-call node.");
            Assert(!ReferenceEquals(originalCall.Pins, workingCall.Pins), "Node editor reused the original pin list.");
            Assert(!ReferenceEquals(originalCall.Pins[0], workingCall.Pins[0]), "Node editor reused an original pin.");
            Assert(!ReferenceEquals(originalTag, workingTag), "Node editor reused the original tag definition.");

            workingCall.InstanceName = "Cancelled_Instance";
            workingCall.UseMultiInstance = true;
            workingCall.Pins[0].Name = "Cancelled_Pin";
            workingTag.Name = "Cancelled_Tag";
            workingTag.DataType = "DInt";
            workingTag.Address = "%MD100";
            workingTag.Comment = "Cancelled comment";
            var cancelledResult = NodeEditingLogic.CreateBlockCallResult(
                workingCall,
                workingCall.InstanceName,
                workingCall.UseMultiInstance);

            Assert(cancelledResult.NodeId == originalCall.Id, "Cancel-style result lost the stable node ID.");
            AssertEqual("V01", originalCall.InstanceName, "Cancelled block-call edit");
            Assert(!originalCall.UseMultiInstance, "Cancelled edit changed UseMultiInstance.");
            Assert(originalCall.Pins[0].Name != "Cancelled_Pin", "Cancelled edit changed an original pin.");
            AssertEqual("V01_Open_Cmd", originalTag.Name, "Cancelled tag edit");
            Assert(
                before.SequenceEqual(SerializeProjectBytes(project)),
                "Editing detached node/tag clones changed the project before Apply.");
        }

        private static void VerifyBlockCallNodeEditing()
        {
            var project = CreateValveDemo();
            var sheet = project.Sheets[0];
            var call = sheet.Nodes.OfType<BlockCallNode>().Single();
            var guidState = GetPersistentGuidState(project).ToArray();
            var wireState = GetWireState(project).ToArray();

            var valid = NodeEditingLogic.CreateBlockCallResult(call, "V02", true);
            Assert(
                NodeEditingLogic.ValidateCandidate(project, valid).Count == 0,
                "A valid FB rename/multi-instance edit was rejected.");
            NodeEditingLogic.Apply(project, valid);

            AssertEqual("V02", call.InstanceName, "FB instance rename");
            Assert(call.UseMultiInstance, "FB multi-instance mode was not applied.");
            Assert(
                GetPersistentGuidState(project).SequenceEqual(guidState),
                "FB property edit changed a node, pin, wire, or referenced model ID.");
            Assert(
                GetWireState(project).SequenceEqual(wireState),
                "FB property edit changed a wire ID or endpoint.");

            var generated = new DiagramSclGenerator().Generate(project.Plant, sheet);
            Assert(generated.IsSuccess, "Edited FB call did not generate: " + FormatIssues(generated.Issues));
            Assert(generated.Scl.Contains("#V02("), "Generated SCL did not use the edited multi-instance name.");

            var collision = NodeEditingLogic.CreateBlockCallResult(call, "V01_Sol", false);
            Assert(
                NodeEditingLogic.ValidateCandidate(project, collision).Count > 0,
                "Block-call rename collision with a global tag was not detected.");
            var beforeRejectedApply = SerializeProjectBytes(project);
            AssertThrows<InvalidOperationException>(
                () => NodeEditingLogic.Apply(project, collision),
                "Block-call global collision Apply");
            Assert(
                beforeRejectedApply.SequenceEqual(SerializeProjectBytes(project)),
                "Rejected block-call collision partially changed the project.");

            var function = new BlockDefinition
            {
                Name = "FC_NodeEdit",
                Kind = BlockKind.Function,
                ReturnType = "Void",
                SclBody = "// FC node-edit fixture"
            };
            var functionProject = new DiagramProject();
            functionProject.Plant.Name = "FunctionNodeEdit";
            functionProject.Plant.Blocks.Add(function);
            var functionSheet = new CallSheet { Name = "FC_FunctionNodeEditCalls" };
            var functionCall = DiagramNodeFactory.CreateBlockCall(function, "FunctionAlias", 10, 10);
            functionSheet.Nodes.Add(functionCall);
            functionProject.Sheets.Add(functionSheet);

            var invalidMulti = NodeEditingLogic.CreateBlockCallResult(functionCall, "FunctionAlias", true);
            Assert(
                NodeEditingLogic.ValidateCandidate(functionProject, invalidMulti).Count > 0,
                "FC multi-instance mode was accepted.");
            AssertThrows<InvalidOperationException>(
                () => NodeEditingLogic.Apply(functionProject, invalidMulti),
                "FC multi-instance Apply");
            Assert(!functionCall.UseMultiInstance, "Rejected FC edit enabled multi-instance mode.");
        }

        private static void VerifyConstantNodeEditing()
        {
            var project = CreateValveDemo();
            var sheet = project.Sheets[0];
            var constant = sheet.Nodes.OfType<ConstantNode>().Single();
            var nodeId = constant.Id;
            var pinId = constant.Pins.Single().Id;
            var wireState = GetWireState(project).ToArray();

            var candidate = NodeEditingLogic.CreateConstantResult(constant, "T#5s", "Time");
            Assert(
                NodeEditingLogic.ValidateCandidate(project, candidate).Count == 0,
                "A compatible constant edit was rejected.");
            NodeEditingLogic.Apply(project, candidate);

            Assert(constant.Id == nodeId, "Constant edit changed the node ID.");
            Assert(constant.Pins.Single().Id == pinId, "Constant edit changed the pin ID.");
            Assert(GetWireState(project).SequenceEqual(wireState), "Constant edit changed a wire ID or endpoint.");
            AssertEqual("T#5s", constant.Literal, "Constant literal edit");
            AssertEqual("T#5s", constant.Title, "Constant title synchronization");
            AssertEqual("Time", constant.DataType, "Constant data-type edit");
            AssertEqual("Time", constant.Pins.Single().DataType, "Constant pin data-type synchronization");

            var generated = new DiagramSclGenerator().Generate(project.Plant, sheet);
            Assert(generated.IsSuccess, "Edited constant did not generate: " + FormatIssues(generated.Issues));
            Assert(
                generated.Scl.Contains("TravelTime := T#5s"),
                "Generated SCL did not use the edited constant literal.");
        }

        private static void VerifyIncompatibleNodeEditIsAtomic()
        {
            var project = CreateValveDemo();
            var constant = project.Sheets[0].Nodes.OfType<ConstantNode>().Single();
            var before = SerializeProjectBytes(project);
            var incompatible = NodeEditingLogic.CreateConstantResult(constant, "123", "DInt");

            Assert(
                NodeEditingLogic.ValidateCandidate(project, incompatible).Count > 0,
                "A type-incompatible wired constant edit was accepted.");
            AssertThrows<InvalidOperationException>(
                () => NodeEditingLogic.Apply(project, incompatible),
                "Type-incompatible constant Apply");
            Assert(
                before.SequenceEqual(SerializeProjectBytes(project)),
                "Rejected incompatible wire edit partially changed the project.");
        }

        private static void VerifyTagNodeEditingPropagation()
        {
            var project = CreateValveDemo();
            var block = project.Plant.Blocks.Single();
            var tag = project.Plant.Tags.Single(item => item.Name == "V01_Open_Cmd");
            var firstNode = project.Sheets[0].Nodes
                .OfType<TagNode>()
                .Single(node => node.TagDefinitionId == tag.Id);

            var secondSheet = new CallSheet { Name = "FC_SecondaryCalls" };
            var secondNode = DiagramNodeFactory.CreateTag(tag, TerminalDirection.Source, 20, 80);
            var secondCall = DiagramNodeFactory.CreateBlockCall(block, "V02", 300, 80);
            secondSheet.Nodes.Add(secondNode);
            secondSheet.Nodes.Add(secondCall);
            Connect(secondSheet, secondNode, tag.Name, secondCall, "Open");
            project.Sheets.Add(secondSheet);

            var guidState = GetPersistentGuidState(project).ToArray();
            var wireState = GetWireState(project).ToArray();
            var workingTag = NodeEditingLogic.CloneTag(tag);
            workingTag.Name = "Valve_Open_Request";
            workingTag.DataType = "BOOL";
            workingTag.Address = "%M20.0";
            workingTag.Comment = "Shared command, edited once";
            var candidate = NodeEditingLogic.CreateTagResult(firstNode, workingTag);

            Assert(
                NodeEditingLogic.ValidateCandidate(project, candidate).Count == 0,
                "A compatible shared-tag edit was rejected.");
            NodeEditingLogic.Apply(project, candidate);

            Assert(
                ReferenceEquals(tag, project.Plant.Tags.Single(item => item.Id == tag.Id)),
                "Tag edit replaced the real TagDefinition instead of updating it.");
            AssertEqual("Valve_Open_Request", tag.Name, "Shared tag name");
            AssertEqual("BOOL", tag.DataType, "Shared tag data type");
            AssertEqual("%M20.0", tag.Address, "Shared tag address");
            AssertEqual("Shared command, edited once", tag.Comment, "Shared tag comment");

            var linkedNodes = project.Sheets
                .SelectMany(item => item.Nodes)
                .OfType<TagNode>()
                .Where(node => node.TagDefinitionId == tag.Id)
                .ToArray();
            Assert(linkedNodes.Length == 2, "Shared-tag fixture did not span two sheets.");
            Assert(
                linkedNodes.All(node =>
                    node.TagName == tag.Name &&
                    node.Title == tag.Name &&
                    node.DataType == tag.DataType &&
                    node.Pins.Single().Name == tag.Name &&
                    node.Pins.Single().DataType == tag.DataType),
                "Tag edit did not propagate to every placed occurrence and terminal pin.");
            Assert(
                GetPersistentGuidState(project).SequenceEqual(guidState),
                "Shared-tag edit changed a node, pin, wire, or referenced model ID.");
            Assert(
                GetWireState(project).SequenceEqual(wireState),
                "Shared-tag edit changed a wire ID or endpoint.");

            foreach (var sheet in project.Sheets)
            {
                var generated = new DiagramSclGenerator().Generate(project.Plant, sheet);
                Assert(generated.IsSuccess, "Shared-tag edit broke generation: " + FormatIssues(generated.Issues));
                Assert(
                    generated.Scl.Contains("Open := \"Valve_Open_Request\""),
                    "Generated SCL did not use the propagated tag name on sheet " + sheet.Name + ".");
            }
        }

        private static void VerifyNodeEditGlobalCollisions()
        {
            var block = new BlockDefinition
            {
                Name = "FB_CollisionOwner",
                Kind = BlockKind.FunctionBlock,
                SclBody = "// Collision fixture"
            };
            var editableTag = new TagDefinition
            {
                Name = "Editable_Tag",
                DataType = "Bool",
                Address = "%M40.0"
            };
            var project = new DiagramProject();
            project.Plant.Name = "NodeEditCollisionTest";
            project.Plant.Blocks.Add(block);
            project.Plant.Tags.Add(editableTag);
            var sheet = new CallSheet { Name = "FB_CollisionCalls" };
            var multiCall = DiagramNodeFactory.CreateBlockCall(block, "NestedValve", 50, 50);
            multiCall.UseMultiInstance = true;
            var globalCall = DiagramNodeFactory.CreateBlockCall(block, "GlobalValveDb", 300, 50);
            var tagNode = DiagramNodeFactory.CreateTag(editableTag, TerminalDirection.Source, 50, 300);
            sheet.Nodes.Add(multiCall);
            sheet.Nodes.Add(globalCall);
            sheet.Nodes.Add(tagNode);
            project.Sheets.Add(sheet);
            var before = SerializeProjectBytes(project);

            AssertTagNameCollision(project, tagNode, editableTag, block.Name, "global block");
            AssertTagNameCollision(project, tagNode, editableTag, sheet.Name + "_DB", "sheet instance DB");
            AssertTagNameCollision(project, tagNode, editableTag, globalCall.InstanceName, "global instance DB");
            Assert(
                before.SequenceEqual(SerializeProjectBytes(project)),
                "Node-edit collision preflight mutated the project.");
        }

        private static void VerifyDiagramCrossDomainSymbolCollisions()
        {
            var visualBlock = new BlockDefinition
            {
                Name = "FB_VisualCollision",
                Kind = BlockKind.FunctionBlock,
                SclBody = "// Cross-domain collision fixture"
            };
            var project = new DiagramProject();
            project.Plant.Name = "DiagramCrossDomainCollisionTest";
            project.Plant.Blocks.Add(visualBlock);

            var explicitHost = new CallBlockDefinition
            {
                Name = "FB_ExplicitHost",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = "Plant_Explicit_DB"
            };
            var stableUnit = new UnitDefinition("Plant_Stable_Unit_DB", visualBlock);
            var fallbackUnit = new UnitDefinition
            {
                Name = "Plant_Fallback_Unit_DB",
                BlockId = Guid.NewGuid(),
                BlockName = visualBlock.Name,
                UseMultiInstance = false
            };
            explicitHost.Units.Add(stableUnit);
            explicitHost.Units.Add(fallbackUnit);
            project.Plant.CallBlocks.Add(explicitHost);

            var defaultHost = new CallBlockDefinition
            {
                Name = "FB_DefaultHost",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = string.Empty
            };
            project.Plant.CallBlocks.Add(defaultHost);

            var reservedNames = new[]
            {
                explicitHost.InstanceDbName,
                defaultHost.Name + "_DB",
                stableUnit.Name,
                fallbackUnit.Name
            };
            var generator = new DiagramSclGenerator();
            for (var index = 0; index < reservedNames.Length; index++)
            {
                var reservedName = reservedNames[index];
                var globalSheet = new CallSheet { Name = "FC_GlobalCollision_" + index };
                globalSheet.Nodes.Add(DiagramNodeFactory.CreateBlockCall(
                    visualBlock,
                    reservedName,
                    10,
                    10));
                var globalResult = generator.Generate(project.Plant, globalSheet);
                AssertIssue(
                    globalResult,
                    "GEN014",
                    "Visual global FB instance did not collide with reserved Plant symbol " + reservedName + ".");

                var sheetName = reservedName.EndsWith("_DB", StringComparison.OrdinalIgnoreCase)
                    ? reservedName.Substring(0, reservedName.Length - 3)
                    : reservedName + "_Sheet";
                var multiSheet = new CallSheet { Name = sheetName };
                var multiCall = DiagramNodeFactory.CreateBlockCall(
                    visualBlock,
                    "Nested_" + index,
                    10,
                    10);
                multiCall.UseMultiInstance = true;
                multiSheet.Nodes.Add(multiCall);
                var sheetDbResult = generator.Generate(project.Plant, multiSheet);
                AssertIssue(
                    sheetDbResult,
                    "GEN022",
                    "Visual sheet DB did not collide with reserved Plant symbol " + reservedName + ".");
            }
        }

        private static void AssertTagNameCollision(
            DiagramProject project,
            TagNode node,
            TagDefinition definition,
            string candidateName,
            string subject)
        {
            var workingTag = NodeEditingLogic.CloneTag(definition);
            workingTag.Name = candidateName;
            var candidate = NodeEditingLogic.CreateTagResult(node, workingTag);
            Assert(
                NodeEditingLogic.ValidateCandidate(project, candidate).Count > 0,
                "Node editor missed tag collision with " + subject + ".");
        }

        private static void VerifyNodeEditApplyRevalidatesStaleResult()
        {
            var project = CreateValveDemo();
            var tag = project.Plant.Tags.Single(item => item.Name == "V01_Open_Cmd");
            var node = project.Sheets[0].Nodes
                .OfType<TagNode>()
                .Single(item => item.TagDefinitionId == tag.Id);
            var workingTag = NodeEditingLogic.CloneTag(tag);
            workingTag.Name = "Fresh_Command";
            var staleResult = NodeEditingLogic.CreateTagResult(node, workingTag);
            Assert(
                NodeEditingLogic.ValidateCandidate(project, staleResult).Count == 0,
                "Fresh node-edit result was unexpectedly invalid.");

            project.Plant.Tags.Add(new TagDefinition
            {
                Name = "Fresh_Command",
                DataType = "Bool",
                Address = "%M99.0"
            });
            var beforeApply = SerializeProjectBytes(project);
            AssertThrows<InvalidOperationException>(
                () => NodeEditingLogic.Apply(project, staleResult),
                "Stale node-edit Apply");
            AssertEqual("V01_Open_Cmd", tag.Name, "Stale node-edit target");
            Assert(
                beforeApply.SequenceEqual(SerializeProjectBytes(project)),
                "Apply did not reject stale validation atomically.");
        }

        private static void VerifyNodePropertyEditUndoIsByteEquivalent()
        {
            var project = CreateValveDemo();
            var before = DiagramProjectSnapshot.Clone(project);
            var beforeBytes = SerializeProjectBytes(before);
            var constant = project.Sheets[0].Nodes.OfType<ConstantNode>().Single();
            var candidate = NodeEditingLogic.CreateConstantResult(constant, "T#9s", "Time");
            NodeEditingLogic.Apply(project, candidate);
            Assert(
                !beforeBytes.SequenceEqual(SerializeProjectBytes(project)),
                "Node property edit did not change the serialized project.");

            var history = new DiagramEditHistory();
            Assert(
                history.Record("Edit constant properties", before, project),
                "Node property edit was not recorded in history.");
            var restored = history.Undo();
            Assert(
                beforeBytes.SequenceEqual(SerializeProjectBytes(restored)),
                "Undo did not restore the pre-edit project byte-for-byte.");
        }

        private static void VerifyNodeEditingHardeningIsAtomic()
        {
            var constantProject = CreateValveDemo();
            var constant = constantProject.Sheets[0].Nodes.OfType<ConstantNode>().Single();
            var unsafeLiterals = new[]
            {
                "T#1s;",
                "T#1s // injected comment",
                "T#1s (* injected comment *)",
                "T#1s *)",
                "T#1s\r\nT#2s",
                "T#1s\tT#2s",
                "T#1s\u0001"
            };
            for (var index = 0; index < unsafeLiterals.Length; index++)
            {
                AssertNodeEditRejectedAtomically(
                    constantProject,
                    NodeEditingLogic.CreateConstantResult(
                        constant,
                        unsafeLiterals[index],
                        "Time"),
                    "Unsafe constant edit " + index);
            }

            AssertNodeEditRejectedAtomically(
                constantProject,
                NodeEditingLogic.CreateConstantResult(constant, "T#1s", " vOiD "),
                "Void constant edit");

            var tagProject = CreateValveDemo();
            var definition = tagProject.Plant.Tags.Single(tag => tag.Name == "V01_Open_Cmd");
            var tagNode = tagProject.Sheets[0].Nodes.OfType<TagNode>()
                .Single(node => node.TagDefinitionId == definition.Id);

            var voidTag = NodeEditingLogic.CloneTag(definition);
            voidTag.DataType = " VOID ";
            AssertNodeEditRejectedAtomically(
                tagProject,
                NodeEditingLogic.CreateTagResult(tagNode, voidTag),
                "Void PLC-tag edit");

            var originalDirection = tagNode.TerminalDirection;
            tagNode.TerminalDirection = (TerminalDirection)99;
            var invalidDirection = NodeEditingLogic.CreateTagResult(
                tagNode,
                NodeEditingLogic.CloneTag(definition));
            tagNode.TerminalDirection = originalDirection;
            AssertNodeEditRejectedAtomically(
                tagProject,
                invalidDirection,
                "Invalid PLC-terminal direction edit");

            var invalidComment = NodeEditingLogic.CloneTag(definition);
            invalidComment.Comment = "Invalid XML\u0001comment";
            AssertNodeEditRejectedAtomically(
                tagProject,
                NodeEditingLogic.CreateTagResult(tagNode, invalidComment),
                "Invalid XML PLC-tag comment edit");

            const string supplementaryComment =
                "Допустимый supplementary Unicode \U0001F680 сохраняется";
            var unicodeTag = NodeEditingLogic.CloneTag(definition);
            unicodeTag.Comment = supplementaryComment;
            var unicodeCandidate = NodeEditingLogic.CreateTagResult(tagNode, unicodeTag);
            Assert(
                NodeEditingLogic.ValidateCandidate(tagProject, unicodeCandidate).Count == 0,
                "A valid supplementary-Unicode PLC-tag comment was rejected.");
            NodeEditingLogic.Apply(tagProject, unicodeCandidate);
            AssertEqual(
                supplementaryComment,
                definition.Comment,
                "Supplementary-Unicode PLC-tag comment edit");
            AssertEqual(
                supplementaryComment,
                CloneProject(tagProject).Plant.Tags.Single(tag => tag.Id == definition.Id).Comment,
                "Supplementary-Unicode PLC-tag comment storage round-trip");
        }

        private static void AssertNodeEditRejectedAtomically(
            DiagramProject project,
            NodeEditResult candidate,
            string subject)
        {
            var before = SerializeProjectBytes(project);
            Assert(
                NodeEditingLogic.ValidateCandidate(project, candidate).Count > 0,
                subject + " unexpectedly passed validation.");
            AssertThrows<InvalidOperationException>(
                () => NodeEditingLogic.Apply(project, candidate),
                subject + " Apply");
            Assert(
                before.SequenceEqual(SerializeProjectBytes(project)),
                subject + " partially mutated the project.");
        }

        private static void VerifyNodeCreationFactoriesAndApply()
        {
            var project = new DiagramProject();
            project.Plant.Name = "NodeCreationFactoryTest";
            var sheet = new CallSheet { Name = "FC_NodeCreationFactory" };
            project.Sheets.Add(sheet);

            var constant = NodeCreationLogic.CreateConstant("  T#5s  ", "  Time  ");
            Assert(constant.Kind == CallNodeKind.Constant, "Constant creation result has the wrong kind.");
            AssertEqual("T#5s", constant.Literal, "Normalized constant literal");
            AssertEqual("Time", constant.DataType, "Normalized constant data type");
            Assert(constant.NodeId != Guid.Empty, "Constant creation allocated an empty node id.");
            Assert(constant.PinId != Guid.Empty, "Constant creation allocated an empty pin id.");
            Assert(constant.NodeId != constant.PinId, "Constant creation reused one id for node and pin.");
            var constantNodeId = constant.NodeId;
            var constantPinId = constant.PinId;
            Assert(
                NodeCreationLogic.Validate(project, sheet.Id, constant).Count == 0,
                "Valid constant creation was rejected.");
            Assert(
                NodeCreationLogic.Validate(project, sheet.Id, constant).Count == 0 &&
                constant.NodeId == constantNodeId &&
                constant.PinId == constantPinId,
                "Repeated constant validation regenerated a stable id.");

            NodeCreationLogic.Apply(project, sheet.Id, constant);
            var constantNode = sheet.Nodes.OfType<ConstantNode>().Single();
            AssertEqual("T#5s", constantNode.Literal, "Created constant literal");
            AssertEqual("Time", constantNode.DataType, "Created constant data type");
            AssertEqual("T#5s", constantNode.Title, "Created constant title");
            AssertTerminalContract(
                constantNode,
                constant,
                "Value",
                PinDirection.Output,
                "Time");
            AssertCreationRejectedAtomically(
                project,
                sheet.Id,
                constant,
                "Repeated constant Apply");

            const string comment = "  Команда насоса — сохраняется дословно  ";
            var source = NodeCreationLogic.CreateNewTag(
                "  Pump_Command  ",
                "  Bool  ",
                "  %M50.0  ",
                comment,
                TerminalDirection.Source);
            Assert(source.Kind == CallNodeKind.Tag, "New-tag creation result has the wrong kind.");
            Assert(source.CreateTagDefinition, "New-tag creation did not request a definition.");
            AssertEqual("Pump_Command", source.TagName, "Normalized new-tag name");
            AssertEqual("Bool", source.DataType, "Normalized new-tag data type");
            AssertEqual("%M50.0", source.TagAddress, "Normalized new-tag address");
            AssertEqual(comment, source.TagComment, "New-tag comment preservation");
            Assert(
                new[] { source.TagDefinitionId, source.NodeId, source.PinId }
                    .All(id => id != Guid.Empty) &&
                new[] { source.TagDefinitionId, source.NodeId, source.PinId }
                    .Distinct().Count() == 3,
                "New-tag creation did not allocate three distinct stable ids.");
            Assert(
                NodeCreationLogic.Validate(project, sheet.Id, source).Count == 0,
                "Valid new source tag was rejected.");
            NodeCreationLogic.Apply(project, sheet.Id, source);

            var definition = project.Plant.Tags.Single(tag => tag.Id == source.TagDefinitionId);
            AssertEqual("Pump_Command", definition.Name, "Created tag name");
            AssertEqual("Bool", definition.DataType, "Created tag data type");
            AssertEqual("%M50.0", definition.Address, "Created tag address");
            AssertEqual(comment, definition.Comment, "Created tag comment");
            var sourceNode = sheet.Nodes.OfType<TagNode>().Single(node => node.Id == source.NodeId);
            Assert(sourceNode.TagDefinitionId == definition.Id, "Created source lost its tag reference.");
            Assert(sourceNode.TerminalDirection == TerminalDirection.Source, "Created source has the wrong terminal direction.");
            AssertEqual(definition.Name, sourceNode.TagName, "Created source display name");
            AssertEqual(definition.DataType, sourceNode.DataType, "Created source data type");
            AssertEqual(definition.Name, sourceNode.Title, "Created source title");
            AssertTerminalContract(
                sourceNode,
                source,
                definition.Name,
                PinDirection.Output,
                definition.DataType);

            var definitionState = new[]
            {
                definition.Id.ToString("D"),
                definition.Name,
                definition.DataType,
                definition.Address,
                definition.Comment
            };
            var sink = NodeCreationLogic.CreateExistingTag(
                definition.Id,
                TerminalDirection.Sink);
            Assert(!sink.CreateTagDefinition, "Existing-tag placement requested a new definition.");
            AssertEqual(string.Empty, sink.TagName, "Existing-tag result leaked a display name");
            AssertEqual(string.Empty, sink.DataType, "Existing-tag result leaked a display data type");
            Assert(
                NodeCreationLogic.Validate(project, sheet.Id, sink).Count == 0,
                "Valid existing-tag sink was rejected.");
            NodeCreationLogic.Apply(project, sheet.Id, sink);

            Assert(project.Plant.Tags.Count == 1, "Existing-tag placement duplicated the definition.");
            Assert(
                ReferenceEquals(definition, project.Plant.Tags.Single()),
                "Existing-tag placement replaced the canonical definition.");
            Assert(
                definitionState.SequenceEqual(new[]
                {
                    definition.Id.ToString("D"),
                    definition.Name,
                    definition.DataType,
                    definition.Address,
                    definition.Comment
                }),
                "Existing-tag placement mutated the canonical definition.");
            var sinkNode = sheet.Nodes.OfType<TagNode>().Single(node => node.Id == sink.NodeId);
            Assert(sinkNode.TerminalDirection == TerminalDirection.Sink, "Created sink has the wrong terminal direction.");
            AssertEqual(definition.Name, sinkNode.TagName, "Existing-tag canonical name");
            AssertEqual(definition.DataType, sinkNode.DataType, "Existing-tag canonical type");
            AssertTerminalContract(
                sinkNode,
                sink,
                definition.Name,
                PinDirection.Input,
                definition.DataType);

            var alias = NodeCreationLogic.CreateNewTag(
                "Pump_Command_Alias",
                "Bool",
                definition.Address,
                string.Empty,
                TerminalDirection.Source);
            Assert(
                NodeCreationLogic.Validate(project, sheet.Id, alias).Count == 0,
                "A distinct tag sharing a physical address was rejected.");
            NodeCreationLogic.Apply(project, sheet.Id, alias);
            Assert(
                project.Plant.Tags.Count(tag => tag.Address == definition.Address) == 2,
                "Physical-address aliases were unexpectedly deduplicated.");

            sheet.Nodes.RemoveAll(node =>
                node is TagNode && ((TagNode)node).TagDefinitionId == definition.Id);
            Assert(
                project.Plant.Tags.Any(tag => tag.Id == definition.Id),
                "Removing the last terminal unexpectedly deleted its reusable tag definition.");
        }

        private static void VerifyNodeCreationValidationAndAtomicity()
        {
            var project = new DiagramProject();
            project.Plant.Name = "NodeCreationValidationTest";
            var sheet = new CallSheet { Name = "FC_NodeCreationValidation" };
            project.Sheets.Add(sheet);

            var invalidConstants = new[]
            {
                NodeCreationLogic.CreateConstant(string.Empty, "Bool"),
                NodeCreationLogic.CreateConstant("TRUE", string.Empty),
                NodeCreationLogic.CreateConstant("TRUE", "Void"),
                NodeCreationLogic.CreateConstant("TRUE;", "Bool"),
                NodeCreationLogic.CreateConstant("TRUE//comment", "Bool"),
                NodeCreationLogic.CreateConstant("(*comment*)TRUE", "Bool"),
                NodeCreationLogic.CreateConstant("TRUE*)", "Bool"),
                NodeCreationLogic.CreateConstant("TRUE\r\nFALSE", "Bool"),
                NodeCreationLogic.CreateConstant("TRUE\t", "Bool"),
                NodeCreationLogic.CreateConstant("TRUE\u0001", "Bool")
            };
            for (var index = 0; index < invalidConstants.Length; index++)
            {
                AssertCreationRejectedAtomically(
                    project,
                    sheet.Id,
                    invalidConstants[index],
                    "Unsafe or incomplete constant " + index);
            }

            var invalidTags = new[]
            {
                NodeCreationLogic.CreateNewTag(string.Empty, "Bool", "%M1.0", string.Empty, TerminalDirection.Source),
                NodeCreationLogic.CreateNewTag("BOOL", "Bool", "%M1.0", string.Empty, TerminalDirection.Source),
                NodeCreationLogic.CreateNewTag("Тег", "Bool", "%M1.0", string.Empty, TerminalDirection.Source),
                NodeCreationLogic.CreateNewTag("NoType", string.Empty, "%M1.0", string.Empty, TerminalDirection.Source),
                NodeCreationLogic.CreateNewTag("VoidType", "Void", "%M1.0", string.Empty, TerminalDirection.Source),
                NodeCreationLogic.CreateNewTag("NoAddress", "Bool", string.Empty, string.Empty, TerminalDirection.Source),
                NodeCreationLogic.CreateNewTag("BadDirection", "Bool", "%M1.0", string.Empty, (TerminalDirection)99)
            };
            for (var index = 0; index < invalidTags.Length; index++)
            {
                AssertCreationRejectedAtomically(
                    project,
                    sheet.Id,
                    invalidTags[index],
                    "Invalid new tag " + index);
            }

            var missingTag = NodeCreationLogic.CreateExistingTag(
                Guid.NewGuid(),
                TerminalDirection.Source);
            AssertCreationRejectedAtomically(
                project,
                sheet.Id,
                missingTag,
                "Missing existing tag");

            var duplicateId = Guid.NewGuid();
            project.Plant.Tags.Add(new TagDefinition
            {
                Id = duplicateId,
                Name = "DuplicateTagA",
                DataType = "Bool",
                Address = "%M2.0"
            });
            project.Plant.Tags.Add(new TagDefinition
            {
                Id = duplicateId,
                Name = "DuplicateTagB",
                DataType = "Bool",
                Address = "%M2.1"
            });
            AssertCreationRejectedAtomically(
                project,
                sheet.Id,
                NodeCreationLogic.CreateExistingTag(duplicateId, TerminalDirection.Sink),
                "Duplicate existing-tag id");

            Action<TagDefinition>[] corruptExistingTag =
            {
                tag => tag.Name = "BOOL",
                tag => tag.DataType = "Void",
                tag => tag.Address = string.Empty
            };
            for (var index = 0; index < corruptExistingTag.Length; index++)
            {
                var invalidProject = new DiagramProject();
                invalidProject.Plant.Name = "InvalidExistingTag" + index;
                var invalidSheet = new CallSheet { Name = "FC_InvalidExistingTag" + index };
                invalidProject.Sheets.Add(invalidSheet);
                var tag = new TagDefinition
                {
                    Name = "ExistingTag" + index,
                    DataType = "Bool",
                    Address = "%M3." + index
                };
                invalidProject.Plant.Tags.Add(tag);
                corruptExistingTag[index](tag);
                AssertCreationRejectedAtomically(
                    invalidProject,
                    invalidSheet.Id,
                    NodeCreationLogic.CreateExistingTag(tag.Id, TerminalDirection.Source),
                    "Invalid canonical existing tag " + index);
            }

            var missingSheetCandidate = NodeCreationLogic.CreateConstant("TRUE", "Bool");
            AssertCreationRejectedAtomically(
                project,
                Guid.NewGuid(),
                missingSheetCandidate,
                "Missing target sheet");

            var duplicateSheetProject = new DiagramProject();
            duplicateSheetProject.Plant.Name = "DuplicateSheetCreation";
            var duplicateSheet = new CallSheet { Name = "FC_DuplicateSheetA" };
            duplicateSheetProject.Sheets.Add(duplicateSheet);
            duplicateSheetProject.Sheets.Add(new CallSheet
            {
                Id = duplicateSheet.Id,
                Name = "FC_DuplicateSheetB"
            });
            AssertCreationRejectedAtomically(
                duplicateSheetProject,
                duplicateSheet.Id,
                NodeCreationLogic.CreateConstant("TRUE", "Bool"),
                "Duplicate target-sheet id");

            var nodeCollisionProject = new DiagramProject();
            nodeCollisionProject.Plant.Name = "NodeIdentityCollision";
            var nodeCollisionSheet = new CallSheet { Name = "FC_NodeIdentityCollision" };
            nodeCollisionProject.Sheets.Add(nodeCollisionSheet);
            var nodeCollision = NodeCreationLogic.CreateConstant("TRUE", "Bool");
            Assert(
                NodeCreationLogic.Validate(nodeCollisionProject, nodeCollisionSheet.Id, nodeCollision).Count == 0,
                "Fresh node-collision candidate failed before the stale-state change.");
            nodeCollisionProject.Plant.Id = nodeCollision.NodeId;
            AssertCreationRejectedAtomically(
                nodeCollisionProject,
                nodeCollisionSheet.Id,
                nodeCollision,
                "Node id colliding with Plant root");

            var pinCollisionProject = new DiagramProject();
            pinCollisionProject.Plant.Name = "PinIdentityCollision";
            var pinCollisionSheet = new CallSheet { Name = "FC_PinIdentityCollision" };
            pinCollisionProject.Sheets.Add(pinCollisionSheet);
            var pinCollision = NodeCreationLogic.CreateConstant("FALSE", "Bool");
            pinCollisionSheet.Groups.Add(new DiagramGroup
            {
                Id = pinCollision.PinId,
                Title = "Pin collision group"
            });
            AssertCreationRejectedAtomically(
                pinCollisionProject,
                pinCollisionSheet.Id,
                pinCollision,
                "Pin id colliding with diagram group");

            var tagCollisionProject = new DiagramProject();
            tagCollisionProject.Plant.Name = "TagIdentityCollision";
            var tagCollisionSheet = new CallSheet { Name = "FC_TagIdentityCollision" };
            tagCollisionProject.Sheets.Add(tagCollisionSheet);
            var tagCollision = NodeCreationLogic.CreateNewTag(
                "TagIdentityCandidate",
                "Bool",
                "%M4.0",
                string.Empty,
                TerminalDirection.Source);
            tagCollisionSheet.Wires.Add(new Wire { Id = tagCollision.TagDefinitionId });
            AssertCreationRejectedAtomically(
                tagCollisionProject,
                tagCollisionSheet.Id,
                tagCollision,
                "Tag id colliding with diagram wire");

            AssertThrows<ArgumentNullException>(
                () => NodeCreationLogic.Validate(null, sheet.Id, missingSheetCandidate),
                "Null project creation validation");
            AssertThrows<ArgumentNullException>(
                () => NodeCreationLogic.Validate(project, sheet.Id, null),
                "Null creation result validation");
        }

        private static void VerifyNodeCreationGlobalCollisions()
        {
            var project = new DiagramProject();
            project.Plant.Name = "NodeCreationSymbolCollisionTest";
            var libraryBlock = new BlockDefinition
            {
                Name = "FB_CreationLibrary",
                Kind = BlockKind.FunctionBlock,
                SclBody = "// symbol fixture"
            };
            project.Plant.Blocks.Add(libraryBlock);

            var dataType = new UdtDefinition { Name = "UDT_CreationPayload" };
            dataType.Members.Add(new UdtMember("Value", "Bool"));
            project.Plant.DataTypes.Add(dataType);
            project.Plant.Tags.Add(new TagDefinition
            {
                Name = "Existing_Creation_Tag",
                DataType = "Bool",
                Address = "%M10.0"
            });

            var explicitHost = new CallBlockDefinition
            {
                Name = "FB_CreationExplicitHost",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = "Creation_Explicit_DB"
            };
            project.Plant.CallBlocks.Add(explicitHost);
            var defaultHost = new CallBlockDefinition
            {
                Name = "FB_CreationDefaultHost",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = string.Empty
            };
            project.Plant.CallBlocks.Add(defaultHost);
            var unitHost = new CallBlockDefinition
            {
                Name = "FC_CreationUnitHost",
                Kind = BlockKind.Function,
                ReturnType = "Void"
            };
            var globalUnit = new UnitDefinition("Creation_Unit_DB", libraryBlock);
            unitHost.Units.Add(globalUnit);
            project.Plant.CallBlocks.Add(unitHost);

            var sheet = new CallSheet { Name = "FC_CreationSymbolSheet" };
            var multi = DiagramNodeFactory.CreateBlockCall(
                libraryBlock,
                "Creation_Nested",
                10,
                10);
            multi.UseMultiInstance = true;
            var global = DiagramNodeFactory.CreateBlockCall(
                libraryBlock,
                "Creation_Visual_DB",
                300,
                10);
            sheet.Nodes.Add(multi);
            sheet.Nodes.Add(global);
            project.Sheets.Add(sheet);

            var reservedNames = new[]
            {
                libraryBlock.Name,
                dataType.Name,
                project.Plant.Tags[0].Name,
                explicitHost.Name,
                explicitHost.InstanceDbName,
                defaultHost.Name + "_DB",
                globalUnit.Name,
                sheet.Name,
                sheet.Name + "_DB",
                global.InstanceName
            };
            foreach (var reservedName in reservedNames)
            {
                var candidate = NodeCreationLogic.CreateNewTag(
                    reservedName.ToLowerInvariant(),
                    "Bool",
                    "%M11.0",
                    string.Empty,
                    TerminalDirection.Source);
                AssertCreationRejectedAtomically(
                    project,
                    sheet.Id,
                    candidate,
                    "Case-insensitive tag collision with " + reservedName);
            }
        }

        private static void VerifyNodeCreationHistoryStorageAndCompiler()
        {
            var constantHistoryProject = new DiagramProject();
            constantHistoryProject.Plant.Name = "ConstantCreationHistory";
            var constantHistorySheet = new CallSheet { Name = "FC_ConstantCreationHistory" };
            constantHistoryProject.Sheets.Add(constantHistorySheet);
            var constantBefore = DiagramProjectSnapshot.Clone(constantHistoryProject);
            var constantBeforeBytes = SerializeProjectBytes(constantBefore);
            var constant = NodeCreationLogic.CreateConstant("DINT#42", "DInt");
            NodeCreationLogic.Apply(constantHistoryProject, constantHistorySheet.Id, constant);
            var constantAfterBytes = SerializeProjectBytes(constantHistoryProject);
            var constantHistory = new DiagramEditHistory();
            Assert(
                constantHistory.Record("Create constant", constantBefore, constantHistoryProject),
                "Constant creation was not recorded in history.");
            Assert(
                SerializeProjectBytes(constantHistory.Undo()).SequenceEqual(constantBeforeBytes),
                "Undo did not remove a directly created constant byte-for-byte.");
            Assert(
                SerializeProjectBytes(constantHistory.Redo()).SequenceEqual(constantAfterBytes),
                "Redo did not restore a directly created constant byte-for-byte.");

            var tagHistoryProject = new DiagramProject();
            tagHistoryProject.Plant.Name = "TagCreationHistory";
            var tagHistorySheet = new CallSheet { Name = "FC_TagCreationHistory" };
            tagHistoryProject.Sheets.Add(tagHistorySheet);
            var tagBefore = DiagramProjectSnapshot.Clone(tagHistoryProject);
            var tagBeforeBytes = SerializeProjectBytes(tagBefore);
            var tag = NodeCreationLogic.CreateNewTag(
                "History_Tag",
                "Bool",
                "%M12.0",
                "История",
                TerminalDirection.Sink);
            NodeCreationLogic.Apply(tagHistoryProject, tagHistorySheet.Id, tag);
            var tagAfterBytes = SerializeProjectBytes(tagHistoryProject);
            var tagHistory = new DiagramEditHistory();
            Assert(
                tagHistory.Record("Create tag terminal", tagBefore, tagHistoryProject),
                "Tag creation was not recorded in history.");
            var tagUndone = tagHistory.Undo();
            Assert(
                SerializeProjectBytes(tagUndone).SequenceEqual(tagBeforeBytes) &&
                tagUndone.Plant.Tags.Count == 0 &&
                tagUndone.Sheets[0].Nodes.Count == 0,
                "Undo did not remove both the tag definition and terminal atomically.");
            var tagRedone = tagHistory.Redo();
            Assert(
                SerializeProjectBytes(tagRedone).SequenceEqual(tagAfterBytes) &&
                tagRedone.Plant.Tags.Single().Id == tag.TagDefinitionId &&
                tagRedone.Sheets[0].Nodes.Single().Id == tag.NodeId,
                "Redo did not restore the tag definition and terminal with stable ids.");

            Guid sourceNodeId;
            Guid constantNodeId;
            Guid sinkNodeId;
            var compilationProject = CreateNodeCreationCompilationProject(
                out sourceNodeId,
                out constantNodeId,
                out sinkNodeId);
            var compiler = new DiagramProjectCompiler();
            var compilation = compiler.Compile(compilationProject);
            Assert(
                compilation.IsSuccess,
                "Directly created nodes did not compile: " + FormatProjectIssues(compilation.Issues));
            var bundleText = string.Join(
                Environment.NewLine,
                compilation.GeneratedSources.Select(source => source.Content));
            Assert(
                bundleText.Contains("Open := \"Create_Command\""),
                "Compiled bundle did not use the canonical created source-tag name.");
            Assert(
                bundleText.Contains("Delay := T#1s"),
                "Compiled bundle did not use the directly created constant literal.");
            Assert(
                bundleText.Contains("Q => \"Create_Result\""),
                "Compiled bundle did not bind the output to the directly created sink.");

            var restored = CloneProject(compilationProject);
            var restoredCompilation = compiler.Compile(restored);
            Assert(
                restoredCompilation.IsSuccess,
                "Stored direct-creation project did not compile: " +
                FormatProjectIssues(restoredCompilation.Issues));
            Assert(
                compilation.GeneratedSources.Select(SourceFingerprint).SequenceEqual(
                    restoredCompilation.GeneratedSources.Select(SourceFingerprint)),
                "Storage round-trip changed the direct-creation source bundle.");
            Assert(
                restored.Sheets[0].Nodes.Any(node => node.Id == sourceNodeId) &&
                restored.Sheets[0].Nodes.Any(node => node.Id == constantNodeId) &&
                restored.Sheets[0].Nodes.Any(node => node.Id == sinkNodeId),
                "Storage round-trip changed a directly created node id.");
            var restoredCommand = restored.Plant.Tags.Single(tagDefinition =>
                tagDefinition.Name == "Create_Command");
            AssertEqual(
                "  Команда с Unicode — дословно  ",
                restoredCommand.Comment,
                "Stored created-tag Unicode comment");
            foreach (var node in restored.Sheets[0].Nodes
                .Where(node => node.Id == sourceNodeId ||
                    node.Id == constantNodeId ||
                    node.Id == sinkNodeId))
            {
                Assert(
                    node.Pins.Count == 1 &&
                    node.Pins[0].NodeId == node.Id &&
                    node.Pins[0].Role == PinRole.Terminal,
                    "Storage round-trip damaged a directly created terminal contract.");
            }

            var staleTag = CloneProject(compilationProject);
            staleTag.Sheets[0].Nodes.OfType<TagNode>()
                .Single(node => node.Id == sourceNodeId)
                .DataType = "DInt";
            AssertProjectIssue(
                compiler.Compile(staleTag),
                "GEN009",
                "Stale directly created tag type");

            var staleConstant = CloneProject(compilationProject);
            staleConstant.Sheets[0].Nodes.OfType<ConstantNode>()
                .Single(node => node.Id == constantNodeId)
                .Pins[0].Role = PinRole.Parameter;
            AssertProjectIssue(
                compiler.Compile(staleConstant),
                "GEN019",
                "Stale directly created constant contract");

            var emptyAddress = CloneProject(compilationProject);
            emptyAddress.Plant.Tags.Single(tagDefinition =>
                tagDefinition.Name == "Create_Command").Address = string.Empty;
            AssertProjectIssue(
                compiler.Compile(emptyAddress),
                "CORE_TAG_ADDRESS_REQUIRED",
                "Created tag without an address");
        }

        private static DiagramProject CreateNodeCreationCompilationProject(
            out Guid sourceNodeId,
            out Guid constantNodeId,
            out Guid sinkNodeId)
        {
            var project = new DiagramProject();
            project.Plant.Name = "NodeCreationCompilation";
            var block = new BlockDefinition
            {
                Name = "FB_CreationConsumer",
                Kind = BlockKind.FunctionBlock,
                SclBody = "#Q := #Open;"
            };
            block.Interface.Add(new InterfaceMember("Open", "Bool", InterfaceSection.Input));
            block.Interface.Add(new InterfaceMember("Delay", "Time", InterfaceSection.Input));
            block.Interface.Add(new InterfaceMember("Q", "Bool", InterfaceSection.Output));
            project.Plant.Blocks.Add(block);

            var sheet = new CallSheet { Name = "FC_NodeCreationCalls" };
            project.Sheets.Add(sheet);
            var source = NodeCreationLogic.CreateNewTag(
                "Create_Command",
                "Bool",
                "%M13.0",
                "  Команда с Unicode — дословно  ",
                TerminalDirection.Source);
            var constant = NodeCreationLogic.CreateConstant("T#1s", "Time");
            var sink = NodeCreationLogic.CreateNewTag(
                "Create_Result",
                "Bool",
                "%M13.1",
                string.Empty,
                TerminalDirection.Sink);
            NodeCreationLogic.Apply(project, sheet.Id, source);
            NodeCreationLogic.Apply(project, sheet.Id, constant);
            NodeCreationLogic.Apply(project, sheet.Id, sink);

            var call = DiagramNodeFactory.CreateBlockCall(
                block,
                "CreationConsumer_DB",
                300,
                100);
            sheet.Nodes.Add(call);
            var sourceNode = sheet.Nodes.OfType<TagNode>().Single(node => node.Id == source.NodeId);
            var constantNode = sheet.Nodes.OfType<ConstantNode>().Single(node => node.Id == constant.NodeId);
            var sinkNode = sheet.Nodes.OfType<TagNode>().Single(node => node.Id == sink.NodeId);
            Connect(sheet, sourceNode, "Create_Command", call, "Open");
            Connect(sheet, constantNode, "Value", call, "Delay");
            Connect(sheet, call, "Q", sinkNode, "Create_Result");

            sourceNodeId = source.NodeId;
            constantNodeId = constant.NodeId;
            sinkNodeId = sink.NodeId;
            return project;
        }

        private static void AssertTerminalContract(
            CallNode node,
            NodeCreationResult result,
            string pinName,
            PinDirection direction,
            string dataType)
        {
            Assert(node.Id == result.NodeId, "Created terminal has the wrong stable node id.");
            Assert(node.Pins.Count == 1, "Created terminal does not have exactly one pin.");
            var pin = node.Pins[0];
            Assert(pin.Id == result.PinId, "Created terminal has the wrong stable pin id.");
            Assert(pin.NodeId == node.Id, "Created terminal pin has the wrong owner id.");
            AssertEqual(pinName, pin.Name, "Created terminal pin name");
            Assert(pin.Direction == direction, "Created terminal pin has the wrong direction.");
            Assert(pin.Role == PinRole.Terminal, "Created terminal pin has the wrong role.");
            AssertEqual(dataType, pin.DataType, "Created terminal pin data type");
            Assert(!pin.IsRequired, "Created terminal pin was unexpectedly marked required.");
            Assert(pin.Order == 0, "Created terminal pin has the wrong order.");
            Assert(pin.MemberId == Guid.Empty, "Created terminal pin has a member reference.");
        }

        private static void AssertCreationRejectedAtomically(
            DiagramProject project,
            Guid sheetId,
            NodeCreationResult candidate,
            string subject)
        {
            var before = SerializeProjectBytes(project);
            Assert(
                NodeCreationLogic.Validate(project, sheetId, candidate).Count > 0,
                subject + " unexpectedly passed validation.");
            AssertThrows<InvalidOperationException>(
                () => NodeCreationLogic.Apply(project, sheetId, candidate),
                subject + " Apply");
            Assert(
                before.SequenceEqual(SerializeProjectBytes(project)),
                subject + " partially mutated the project.");
        }

        private static string SourceFingerprint(GeneratedSource source)
        {
            return source.Order + "\u001f" + (int)source.Kind + "\u001f" +
                source.FileName + "\u001f" + source.Content;
        }

        private static void VerifyLogicNodeEditingPreservesIdentity()
        {
            var project = CreateBooleanLogicProject(LogicOperation.And);
            var logic = project.Sheets[0].Nodes.OfType<LogicNode>().Single();
            var nodeId = logic.Id;
            var pinIds = logic.Pins.OrderBy(pin => pin.Order).Select(pin => pin.Id).ToArray();
            var wireState = GetWireState(project).ToArray();
            var before = DiagramProjectSnapshot.Clone(project);
            var beforeBytes = SerializeProjectBytes(before);
            var candidate = new LogicNodeEditResult(logic.Id, LogicOperation.Or, " Bool ");

            Assert(
                LogicNodeEditingLogic.ValidateCandidate(project, candidate).Count == 0,
                "Compatible AND→OR edit was rejected.");
            LogicNodeEditingLogic.Apply(project, candidate);

            logic = project.Sheets[0].Nodes.OfType<LogicNode>().Single();
            Assert(logic.Id == nodeId, "Logic edit changed the node ID.");
            Assert(logic.Operation == LogicOperation.Or, "Logic edit did not update the operation.");
            AssertEqual("OR", logic.Title, "Edited logic title");
            Assert(
                logic.Pins.OrderBy(pin => pin.Order).Select(pin => pin.Id).SequenceEqual(pinIds),
                "Compatible logic edit changed a stable pin ID.");
            Assert(
                GetWireState(project).SequenceEqual(wireState),
                "Compatible logic edit changed a wire ID or endpoint.");

            var generated = new DiagramSclGenerator().Generate(project.Plant, project.Sheets[0]);
            Assert(generated.IsSuccess, "Edited logic did not generate: " + FormatIssues(generated.Issues));
            Assert(
                generated.Scl.Contains("(\"Logic_Input_1\" OR \"Logic_Input_2\")"),
                "Edited logic operation was not reflected in SCL.");

            var history = new DiagramEditHistory();
            Assert(history.Record("Edit logic", before, project), "Logic property edit was not recorded.");
            Assert(
                SerializeProjectBytes(history.Undo()).SequenceEqual(beforeBytes),
                "Undo did not restore logic properties byte-for-byte.");
        }

        private static void VerifyLogicNodeEditingRejectsConnectedChangesAtomically()
        {
            var project = CreateBooleanLogicProject(LogicOperation.And);
            var logic = project.Sheets[0].Nodes.OfType<LogicNode>().Single();

            var typeBefore = SerializeProjectBytes(project);
            var incompatibleType = new LogicNodeEditResult(
                logic.Id,
                LogicOperation.Equal,
                "DInt");
            Assert(
                LogicNodeEditingLogic.ValidateCandidate(project, incompatibleType).Count > 0,
                "Connected Bool inputs accepted an incompatible DInt comparison edit.");
            AssertThrows<InvalidOperationException>(
                () => LogicNodeEditingLogic.Apply(project, incompatibleType),
                "Incompatible connected logic type Apply");
            Assert(
                SerializeProjectBytes(project).SequenceEqual(typeBefore),
                "Rejected connected logic type edit mutated the project.");

            var shapeBefore = SerializeProjectBytes(project);
            var removesConnectedPin = new LogicNodeEditResult(
                logic.Id,
                LogicOperation.Not,
                "Bool");
            Assert(
                LogicNodeEditingLogic.ValidateCandidate(project, removesConnectedPin).Any(error =>
                    error.IndexOf("провод", StringComparison.OrdinalIgnoreCase) >= 0),
                "Logic edit did not explain removal of a connected pin.");
            AssertThrows<InvalidOperationException>(
                () => LogicNodeEditingLogic.Apply(project, removesConnectedPin),
                "Connected logic pin removal Apply");
            Assert(
                SerializeProjectBytes(project).SequenceEqual(shapeBefore),
                "Rejected connected-pin removal mutated the project.");
        }

        private static void VerifyNoteEditingAndUndoRoundTrip()
        {
            var project = CreateValveDemo();
            var note = NoteEditingLogic.Create("  Первая строка\r\nВторая строка  ", 321.5, 654.5);
            Assert(note.Id != Guid.Empty, "Note factory returned an empty ID.");
            Assert(note.X == 321.5 && note.Y == 654.5, "Note factory changed coordinates.");
            AssertEqual("Первая строка", note.Title, "Created note title");
            AssertEqual("Первая строка\r\nВторая строка", note.Text, "Created note text");
            project.Sheets[0].Nodes.Add(note);

            var before = DiagramProjectSnapshot.Clone(project);
            var beforeBytes = SerializeProjectBytes(before);
            var nodeId = note.Id;
            NoteEditingLogic.Apply(project, nodeId, "  Обновлённая заметка\nс продолжением  ");
            Assert(note.Id == nodeId, "Note edit changed the stable node ID.");
            Assert(note.X == 321.5 && note.Y == 654.5, "Note edit changed coordinates.");
            AssertEqual("Обновлённая заметка", note.Title, "Edited note title");
            AssertEqual("Обновлённая заметка\nс продолжением", note.Text, "Edited note text");

            var history = new DiagramEditHistory();
            Assert(history.Record("Edit note", before, project), "Note edit was not recorded in history.");
            var restored = history.Undo();
            Assert(
                SerializeProjectBytes(restored).SequenceEqual(beforeBytes),
                "Undo did not restore the note byte-for-byte.");

            string error;
            Assert(!NoteEditingLogic.TryValidateText("   ", out error), "Blank note text was accepted.");
            Assert(
                !NoteEditingLogic.TryValidateText("bad\u0001text", out error),
                "Invalid XML control character in note text was accepted.");
            Assert(
                !NoteEditingLogic.TryValidateText(new string('x', 2001), out error),
                "Oversized note text was accepted.");
            AssertThrows<InvalidOperationException>(
                () => NoteEditingLogic.Apply(project, Guid.NewGuid(), "Text"),
                "Editing a missing note");
        }

        private static BlockDefinition CloneEditorBlock(BlockDefinition source)
        {
            return new BlockDefinition
            {
                Id = source.Id,
                Name = source.Name,
                Kind = source.Kind,
                ReturnType = source.ReturnType,
                Version = source.Version,
                OptimizedAccess = source.OptimizedAccess,
                ImportedInterfaceOnly = source.ImportedInterfaceOnly,
                Description = source.Description,
                SclBody = source.SclBody,
                Interface = source.Interface.Select(member => CloneEditorMember(member, member.Name)).ToList()
            };
        }

        private static InterfaceMember CloneEditorMember(InterfaceMember source, string name)
        {
            return new InterfaceMember
            {
                Id = source.Id,
                Name = name,
                DataType = source.DataType,
                Section = source.Section,
                InitialValue = source.InitialValue,
                Comment = source.Comment
            };
        }

        private static Pin FindParameterPin(BlockCallNode node, Guid memberId)
        {
            return node.Pins.Single(pin => pin.Role == PinRole.Parameter && pin.MemberId == memberId);
        }

        private sealed class EditorSheetFixture
        {
            public EditorSheetFixture(
                CallSheet sheet,
                BlockCallNode call,
                Wire inputWire,
                Wire outputWire)
            {
                Sheet = sheet;
                Call = call;
                InputWire = inputWire;
                OutputWire = outputWire;
            }

            public CallSheet Sheet { get; private set; }

            public BlockCallNode Call { get; private set; }

            public Wire InputWire { get; private set; }

            public Wire OutputWire { get; private set; }
        }

        private static void VerifyDiagramProjectSnapshots()
        {
            var source = CreateHistoryFixture();
            var expectedGuidState = GetPersistentGuidState(source).ToArray();
            var expectedWireState = GetWireState(source).ToArray();
            var expectedObjectIds = GetPersistentObjectIds(source).ToArray();
            Assert(
                expectedObjectIds.All(id => id != Guid.Empty),
                "History fixture contains an empty persistent object ID.");
            Assert(
                expectedObjectIds.Distinct().Count() == expectedObjectIds.Length,
                "History fixture contains duplicate persistent object IDs.");

            var snapshot = DiagramProjectSnapshot.Capture(source);
            var restored = snapshot.Restore();

            AssertIndependentProjectGraphs(source, restored, "Snapshot restore");
            AssertSnapshotFixtureValues(restored);
            Assert(
                GetPersistentGuidState(restored).SequenceEqual(expectedGuidState),
                "Snapshot restore changed one or more GUID values or references.");
            Assert(
                GetWireState(restored).SequenceEqual(expectedWireState),
                "Snapshot restore changed wire IDs or endpoints.");

            source.Plant.Name = "Mutated source";
            source.Plant.Blocks[0].Name = "FB_MutatedSource";
            source.Plant.Blocks[0].Interface[0].Name = "MutatedMember";
            source.Plant.Tags[0].Comment = "Mutated tag";
            source.Sheets[0].Zoom = 9.0;
            source.Sheets[0].Nodes[0].Pins[0].Name = "MutatedPin";
            source.Sheets[0].Wires[0].SourcePinId = Guid.NewGuid();

            AssertSnapshotFixtureValues(restored);
            Assert(
                GetPersistentGuidState(restored).SequenceEqual(expectedGuidState),
                "Source mutation leaked into an already-restored snapshot graph.");
            Assert(
                GetWireState(restored).SequenceEqual(expectedWireState),
                "Source wire mutation leaked into the restored snapshot graph.");

            restored.Plant.Blocks[0].Interface.Clear();
            restored.Plant.DataTypes[0].Members.Clear();
            restored.Plant.CallBlocks[0].Units[0].Bindings.Clear();
            restored.Sheets[0].Nodes[0].Pins.Clear();
            restored.Sheets[0].Wires.Clear();

            var restoredAgain = snapshot.Restore();
            AssertSnapshotFixtureValues(restoredAgain);
            Assert(
                GetPersistentGuidState(restoredAgain).SequenceEqual(expectedGuidState),
                "Mutating one Restore result changed a later Restore result.");
            Assert(
                GetWireState(restoredAgain).SequenceEqual(expectedWireState),
                "Mutating restored wires changed a later Restore result.");
            AssertIndependentProjectGraphs(restored, restoredAgain, "Repeated snapshot restore");

            var clone = DiagramProjectSnapshot.Clone(restoredAgain);
            AssertIndependentProjectGraphs(restoredAgain, clone, "Snapshot clone");
            Assert(
                GetPersistentGuidState(clone).SequenceEqual(expectedGuidState),
                "Snapshot Clone changed one or more GUID values or references.");
            Assert(
                GetWireState(clone).SequenceEqual(expectedWireState),
                "Snapshot Clone changed wire IDs or endpoints.");

            clone.Plant.Tags.Clear();
            clone.Sheets[1].Nodes.Clear();
            Assert(restoredAgain.Plant.Tags.Count == 4, "Snapshot clone shares the tag list with its source.");
            Assert(restoredAgain.Sheets[1].Nodes.Count == 3, "Snapshot clone shares a node list with its source.");
        }

        private static void VerifyDiagramEditHistory()
        {
            var template = CreateHistoryFixture();
            Assert(
                new DiagramEditHistory().Capacity == DiagramEditHistory.DefaultCapacity,
                "Default history capacity is incorrect.");

            var history = new DiagramEditHistory(4);
            Assert(!history.CanUndo && !history.CanRedo, "A new history is not empty.");
            Assert(history.UndoCount == 0 && history.RedoCount == 0, "New history counts are incorrect.");
            AssertEqual(string.Empty, history.UndoDescription, "Empty undo description");
            AssertEqual(string.Empty, history.RedoDescription, "Empty redo description");
            AssertEqual(string.Empty, history.NextUndoDescription, "Empty next-undo description");
            AssertEqual(string.Empty, history.NextRedoDescription, "Empty next-redo description");

            var before = CreateHistoryState(template, "Before", 10.0);
            var after = CreateHistoryState(template, "After", 20.0);
            var expectedWireCount = before.Sheets[0].Wires.Count;
            var expectedInterfaceCount = after.Plant.Blocks[0].Interface.Count;
            var expectedTagCount = after.Plant.Tags.Count;
            Assert(history.Record("Move valve", before, after), "A real history edit was suppressed.");
            Assert(history.CanUndo && !history.CanRedo, "Recorded edit produced incorrect history availability.");
            Assert(history.UndoCount == 1 && history.RedoCount == 0, "Recorded edit produced incorrect counts.");
            AssertEqual("Move valve", history.UndoDescription, "Undo description");
            AssertEqual("Move valve", history.NextUndoDescription, "Next undo description");

            before.Plant.Name = "Mutated before input";
            before.Sheets[0].Wires.Clear();
            after.Plant.Name = "Mutated after input";
            after.Plant.Blocks[0].Interface.Clear();

            var undone = history.Undo();
            AssertEqual("Before", undone.Plant.Name, "Undo state");
            Assert(undone.Sheets[0].Nodes[0].X == 10.0, "Undo restored the wrong node position.");
            Assert(undone.Sheets[0].Wires.Count == expectedWireCount, "Undo retained a post-record wire mutation.");
            Assert(!history.CanUndo && history.CanRedo, "Undo produced incorrect history availability.");
            AssertEqual("Move valve", history.RedoDescription, "Redo description");
            AssertEqual("Move valve", history.NextRedoDescription, "Next redo description");

            undone.Plant.Name = "Mutated undo result";
            undone.Sheets[0].Nodes[0].Pins.Clear();
            var redone = history.Redo();
            AssertEqual("After", redone.Plant.Name, "Redo state");
            Assert(redone.Sheets[0].Nodes[0].X == 20.0, "Redo restored the wrong node position.");
            Assert(
                redone.Plant.Blocks[0].Interface.Count == expectedInterfaceCount,
                "Redo retained a post-record interface mutation.");

            redone.Plant.Name = "Mutated redo result";
            redone.Plant.Tags.Clear();
            var undoneAgain = history.Undo();
            AssertEqual("Before", undoneAgain.Plant.Name, "Repeated undo state");
            Assert(undoneAgain.Sheets[0].Nodes[0].Pins.Count == 1, "Undo results share a nested pin list.");
            Assert(!ReferenceEquals(undone, undoneAgain), "Repeated Undo returned the same project instance.");
            AssertIndependentProjectGraphs(undone, undoneAgain, "Repeated undo");

            var redoneAgain = history.Redo();
            AssertEqual("After", redoneAgain.Plant.Name, "Repeated redo state");
            Assert(redoneAgain.Plant.Tags.Count == expectedTagCount, "Redo results share a nested tag list.");
            Assert(!ReferenceEquals(redone, redoneAgain), "Repeated Redo returned the same project instance.");
            AssertIndependentProjectGraphs(redone, redoneAgain, "Repeated redo");

            var branchHistory = new DiagramEditHistory(3);
            var branchStart = CreateHistoryState(template, "Branch start", 30.0);
            var originalAfter = CreateHistoryState(template, "Original after", 40.0);
            Assert(branchHistory.Record("Original edit", branchStart, originalAfter), "Branch setup edit failed.");
            var branchBase = branchHistory.Undo();
            var identicalBranchBase = DiagramProjectSnapshot.Clone(branchBase);
            Assert(
                !branchHistory.Record("No-op", branchBase, identicalBranchBase),
                "Identical project graphs were recorded as an edit.");
            Assert(
                branchHistory.UndoCount == 0 && branchHistory.RedoCount == 1 && branchHistory.CanRedo,
                "A suppressed no-op changed the existing redo history.");
            AssertEqual("Original edit", branchHistory.RedoDescription, "Redo description after no-op");

            var replacementAfter = CreateHistoryState(branchBase, "Replacement after", 50.0);
            Assert(branchHistory.Record("Replacement edit", branchBase, replacementAfter), "Replacement edit failed.");
            Assert(
                branchHistory.RedoCount == 0 && !branchHistory.CanRedo,
                "A new edit after Undo did not clear redo history.");
            AssertEqual("Replacement edit", branchHistory.UndoDescription, "Replacement undo description");
            AssertEqual(string.Empty, branchHistory.RedoDescription, "Cleared redo description");

            branchHistory.Clear();
            Assert(
                branchHistory.UndoCount == 0 && branchHistory.RedoCount == 0 &&
                !branchHistory.CanUndo && !branchHistory.CanRedo,
                "Clear did not empty both history stacks.");

            VerifyDiagramEditHistoryCapacity(template);
        }

        private static void VerifyDiagramEditHistoryCapacity(DiagramProject template)
        {
            var state0 = CreateHistoryState(template, "State 0", 0.0);
            var state1 = CreateHistoryState(template, "State 1", 1.0);
            var state2 = CreateHistoryState(template, "State 2", 2.0);
            var state3 = CreateHistoryState(template, "State 3", 3.0);
            var history = new DiagramEditHistory(2);

            Assert(history.Record("Edit 1", state0, state1), "First capacity edit failed.");
            Assert(history.Record("Edit 2", state1, state2), "Second capacity edit failed.");
            Assert(history.Record("Edit 3", state2, state3), "Third capacity edit failed.");
            Assert(history.UndoCount == 2, "History exceeded or undershot its configured capacity.");
            AssertEqual("Edit 3", history.UndoDescription, "Newest bounded undo description");

            var undo3 = history.Undo();
            AssertEqual("State 2", undo3.Plant.Name, "First bounded undo state");
            AssertEqual("Edit 2", history.UndoDescription, "Second bounded undo description");
            var undo2 = history.Undo();
            AssertEqual("State 1", undo2.Plant.Name, "Second bounded undo state");
            Assert(!history.CanUndo && history.UndoCount == 0, "Evicted history entry remained undoable.");
            AssertThrows<InvalidOperationException>(
                () => history.Undo(),
                "Undo beyond bounded history");

            AssertEqual("Edit 2", history.RedoDescription, "First bounded redo description");
            var redo2 = history.Redo();
            AssertEqual("State 2", redo2.Plant.Name, "First bounded redo state");
            AssertEqual("Edit 3", history.RedoDescription, "Second bounded redo description");
            var redo3 = history.Redo();
            AssertEqual("State 3", redo3.Plant.Name, "Second bounded redo state");
            Assert(!history.CanRedo && history.RedoCount == 0, "Redo stack was not exhausted.");
        }

        private static void VerifyDiagramHistoryGuards()
        {
            var project = CreateHistoryFixture();
            AssertArgumentParameter(
                AssertThrows<ArgumentNullException>(
                    () => DiagramProjectSnapshot.Capture(null),
                    "Capture null project"),
                "project",
                "Capture null project");
            AssertArgumentParameter(
                AssertThrows<ArgumentNullException>(
                    () => DiagramProjectSnapshot.Clone(null),
                    "Clone null project"),
                "project",
                "Clone null project");
            AssertArgumentParameter(
                AssertThrows<ArgumentOutOfRangeException>(
                    () => new DiagramEditHistory(0),
                    "Zero history capacity"),
                "capacity",
                "Zero history capacity");
            AssertArgumentParameter(
                AssertThrows<ArgumentOutOfRangeException>(
                    () => new DiagramEditHistory(-1),
                    "Negative history capacity"),
                "capacity",
                "Negative history capacity");

            var history = new DiagramEditHistory();
            AssertArgumentParameter(
                AssertThrows<ArgumentNullException>(
                    () => history.Record(null, project, project),
                    "Null history description"),
                "description",
                "Null history description");
            AssertArgumentParameter(
                AssertThrows<ArgumentNullException>(
                    () => history.Record("Invalid before", null, project),
                    "Null history before state"),
                "before",
                "Null history before state");
            AssertArgumentParameter(
                AssertThrows<ArgumentNullException>(
                    () => history.Record("Invalid after", project, null),
                    "Null history after state"),
                "after",
                "Null history after state");
            Assert(
                history.UndoCount == 0 && history.RedoCount == 0,
                "Rejected Record input changed history counts.");
            AssertThrows<InvalidOperationException>(() => history.Undo(), "Undo on empty history");
            AssertThrows<InvalidOperationException>(() => history.Redo(), "Redo on empty history");
        }

        private static void VerifyDiagramGroups()
        {
            VerifyGroupEditingOperations();
            VerifyGroupCrossOwnerRejectionIsAtomic();
            VerifyGroupStorageAndHistory();
            VerifyGroupValidationAndProjectIdentity();
            VerifyGroupEditsDoNotChangeGeneratedScl();
        }

        private static void VerifyGroupEditingOperations()
        {
            CallSheet sheet;
            NoteNode first;
            NoteNode second;
            NoteNode third;
            NoteNode outside;
            var project = CreateGroupEditingFixture(
                out sheet,
                out first,
                out second,
                out third,
                out outside);

            var parentDraft = GroupEditingLogic.CreateDraft(
                "  Parent region  ",
                "Parent comment",
                10.0,
                20.0,
                900.0,
                600.0);
            GroupEditingLogic.Add(
                project,
                sheet.Id,
                parentDraft,
                new[] { first.Id, second.Id, third.Id });
            var parent = sheet.Groups.Single(group => group.Id == parentDraft.Id);
            Assert(!ReferenceEquals(parentDraft, parent), "Group Add stored the caller-owned draft instance.");
            AssertEqual("Parent region", parent.Title, "Normalized parent group title");
            Assert(parent.ParentGroupId == Guid.Empty, "A root group received a non-root parent.");
            Assert(
                parent.MemberNodeIds.SequenceEqual(new[] { first.Id, second.Id, third.Id }),
                "Root group did not retain its direct members in selection order.");

            var childDraft = GroupEditingLogic.CreateDraft(
                "Child region",
                "Child comment",
                60.0,
                80.0,
                600.0,
                360.0);
            GroupEditingLogic.Add(
                project,
                sheet.Id,
                childDraft,
                new[] { first.Id, second.Id });
            var child = sheet.Groups.Single(group => group.Id == childDraft.Id);
            Assert(child.ParentGroupId == parent.Id, "Same-owner selection did not create a nested group.");
            Assert(
                child.MemberNodeIds.SequenceEqual(new[] { first.Id, second.Id }),
                "Nested group did not receive the selected direct members.");
            Assert(
                parent.MemberNodeIds.SequenceEqual(new[] { third.Id }),
                "Nested group members were not removed from their previous direct owner.");

            var grandchildDraft = GroupEditingLogic.CreateDraft(
                "Grandchild region",
                string.Empty,
                90.0,
                110.0,
                320.0,
                200.0);
            GroupEditingLogic.Add(project, sheet.Id, grandchildDraft, new[] { first.Id });
            var grandchild = sheet.Groups.Single(group => group.Id == grandchildDraft.Id);
            Assert(grandchild.ParentGroupId == child.Id, "Second-level nesting chose the wrong parent.");
            Assert(
                grandchild.MemberNodeIds.SequenceEqual(new[] { first.Id }) &&
                child.MemberNodeIds.SequenceEqual(new[] { second.Id }),
                "Second-level nesting did not preserve direct-membership semantics.");

            var childId = child.Id;
            var childParentId = child.ParentGroupId;
            var childMembers = child.MemberNodeIds.ToArray();
            var childX = child.X;
            var childY = child.Y;
            var childWidth = child.Width;
            var childHeight = child.Height;
            GroupEditingLogic.Edit(
                project,
                sheet.Id,
                child.Id,
                "  Renamed child  ",
                "  Comment is preserved verbatim  ");
            AssertEqual("Renamed child", child.Title, "Edited group title");
            AssertEqual("  Comment is preserved verbatim  ", child.Comment, "Edited group comment");
            Assert(
                child.Id == childId && child.ParentGroupId == childParentId &&
                child.MemberNodeIds.SequenceEqual(childMembers) &&
                child.X == childX && child.Y == childY &&
                child.Width == childWidth && child.Height == childHeight,
                "Group Edit changed identity, ownership, membership, or geometry.");

            var subtreeIds = GroupEditingLogic.GetSubtreeGroupIds(sheet, parent.Id);
            Assert(
                subtreeIds.SetEquals(new[] { parent.Id, child.Id, grandchild.Id }),
                "Group subtree traversal returned an incomplete or extraneous set.");
            var groupPositions = sheet.Groups.ToDictionary(group => group.Id, group => new[] { group.X, group.Y });
            var nodePositions = sheet.Nodes.ToDictionary(node => node.Id, node => new[] { node.X, node.Y });
            const double deltaX = 17.5;
            const double deltaY = -8.0;
            GroupEditingLogic.Move(project, sheet.Id, parent.Id, deltaX, deltaY);
            foreach (var group in sheet.Groups)
            {
                Assert(
                    group.X == groupPositions[group.Id][0] + deltaX &&
                    group.Y == groupPositions[group.Id][1] + deltaY,
                    "Moving a parent did not move every group in its subtree exactly once.");
            }

            foreach (var member in new CallNode[] { first, second, third })
            {
                Assert(
                    member.X == nodePositions[member.Id][0] + deltaX &&
                    member.Y == nodePositions[member.Id][1] + deltaY,
                    "Moving a group subtree did not move a direct/transitive member exactly once.");
            }

            Assert(
                outside.X == nodePositions[outside.Id][0] && outside.Y == nodePositions[outside.Id][1],
                "Moving a group changed an unowned node.");

            GroupEditingLogic.Ungroup(project, sheet.Id, child.Id);
            Assert(!sheet.Groups.Any(group => group.Id == childId), "Ungroup retained the removed group.");
            Assert(grandchild.ParentGroupId == parent.Id, "Ungroup did not promote a child group.");
            Assert(
                parent.MemberNodeIds.SequenceEqual(new[] { third.Id, second.Id }),
                "Ungroup did not promote direct members to the parent in deterministic order.");
            Assert(
                grandchild.MemberNodeIds.SequenceEqual(new[] { first.Id }),
                "Ungroup changed a promoted child's direct members.");

            var removedNodeIds = new HashSet<Guid> { first.Id, third.Id };
            GroupEditingLogic.RemoveNodesFromGroups(sheet, removedNodeIds);
            sheet.Nodes.RemoveAll(node => removedNodeIds.Contains(node.Id));
            Assert(
                sheet.Groups.All(group => group.MemberNodeIds.All(id => !removedNodeIds.Contains(id))),
                "Node-delete membership cleanup left a removed node in a group.");
            Assert(
                parent.MemberNodeIds.SequenceEqual(new[] { second.Id }) &&
                grandchild.MemberNodeIds.Count == 0,
                "Node-delete membership cleanup removed or retained the wrong direct members.");
            Assert(
                new DiagramValidator().Validate(sheet).IsValid,
                "Valid group editing operations left an invalid call sheet.");
        }

        private static void VerifyGroupCrossOwnerRejectionIsAtomic()
        {
            CallSheet sheet;
            NoteNode first;
            NoteNode second;
            NoteNode third;
            NoteNode outside;
            var project = CreateGroupEditingFixture(
                out sheet,
                out first,
                out second,
                out third,
                out outside);

            // Keep both initial owners geometrically valid. The scenario below is
            // specifically about rejecting a selection spanning two direct owners.
            second.X = 560.0;
            second.Y = 120.0;

            var firstOwner = GroupEditingLogic.CreateDraft("First owner", string.Empty, 0, 0, 400, 240);
            var secondOwner = GroupEditingLogic.CreateDraft("Second owner", string.Empty, 500, 0, 400, 240);
            GroupEditingLogic.Add(project, sheet.Id, firstOwner, new[] { first.Id });
            GroupEditingLogic.Add(project, sheet.Id, secondOwner, new[] { second.Id });

            var before = SerializeProjectBytes(project);
            var rejected = GroupEditingLogic.CreateDraft("Rejected child", string.Empty, 100, 100, 300, 180);
            AssertThrows<InvalidOperationException>(
                () => GroupEditingLogic.Add(project, sheet.Id, rejected, new[] { first.Id, second.Id }),
                "Cross-owner nested group creation");
            Assert(
                before.SequenceEqual(SerializeProjectBytes(project)),
                "Rejected cross-owner group creation partially mutated the project.");
            Assert(
                sheet.Groups.Count == 2 &&
                sheet.Groups.Single(group => group.Id == firstOwner.Id).MemberNodeIds.SequenceEqual(new[] { first.Id }) &&
                sheet.Groups.Single(group => group.Id == secondOwner.Id).MemberNodeIds.SequenceEqual(new[] { second.Id }),
                "Rejected cross-owner group creation changed existing owners.");
        }

        private static void VerifyGroupStorageAndHistory()
        {
            CallSheet sheet;
            NoteNode first;
            NoteNode second;
            NoteNode third;
            NoteNode outside;
            var project = CreateGroupEditingFixture(
                out sheet,
                out first,
                out second,
                out third,
                out outside);

            var parentDraft = GroupEditingLogic.CreateDraft(
                "Stored parent",
                "Parent storage comment",
                20.25,
                30.5,
                700.75,
                420.125);
            GroupEditingLogic.Add(project, sheet.Id, parentDraft, new[] { first.Id, second.Id });
            var childDraft = GroupEditingLogic.CreateDraft(
                "Stored child",
                "Child storage comment",
                80.5,
                90.75,
                360.25,
                210.5);
            GroupEditingLogic.Add(project, sheet.Id, childDraft, new[] { first.Id });
            var parent = sheet.Groups.Single(group => group.Id == parentDraft.Id);
            var child = sheet.Groups.Single(group => group.Id == childDraft.Id);

            var storedBytes = SerializeProjectBytes(project);
            var reloaded = CloneProject(project);
            Assert(reloaded.FormatVersion == project.FormatVersion, "Group XML round-trip changed FormatVersion.");
            var reloadedSheet = reloaded.Sheets.Single(item => item.Id == sheet.Id);
            var reloadedParent = reloadedSheet.Groups.Single(group => group.Id == parent.Id);
            var reloadedChild = reloadedSheet.Groups.Single(group => group.Id == child.Id);
            Assert(
                reloadedParent.ParentGroupId == Guid.Empty &&
                reloadedParent.MemberNodeIds.SequenceEqual(parent.MemberNodeIds) &&
                reloadedChild.ParentGroupId == parent.Id &&
                reloadedChild.MemberNodeIds.SequenceEqual(child.MemberNodeIds),
                "Group XML round-trip changed stable IDs, parents, or direct members.");
            Assert(
                reloadedParent.X == parent.X && reloadedParent.Y == parent.Y &&
                reloadedParent.Width == parent.Width && reloadedParent.Height == parent.Height &&
                reloadedChild.X == child.X && reloadedChild.Y == child.Y &&
                reloadedChild.Width == child.Width && reloadedChild.Height == child.Height,
                "Group XML round-trip changed geometry.");
            Assert(
                !ReferenceEquals(parent, reloadedParent) &&
                !ReferenceEquals(parent.MemberNodeIds, reloadedParent.MemberNodeIds),
                "Group XML round-trip reused a group or member collection instance.");
            Assert(
                storedBytes.SequenceEqual(SerializeProjectBytes(reloaded)),
                "Group XML round-trip was not byte-stable.");

            var before = DiagramProjectSnapshot.Clone(project);
            var beforeBytes = SerializeProjectBytes(before);
            GroupEditingLogic.Edit(project, sheet.Id, child.Id, "History child", "History comment");
            GroupEditingLogic.Move(project, sheet.Id, parent.Id, 11.0, 7.0);
            var afterBytes = SerializeProjectBytes(project);
            var history = new DiagramEditHistory();
            Assert(history.Record("Edit group", before, project), "Group edit was not recorded in history.");
            var undone = history.Undo();
            Assert(
                beforeBytes.SequenceEqual(SerializeProjectBytes(undone)),
                "Group history Undo did not restore the project byte-for-byte.");
            var redone = history.Redo();
            Assert(
                afterBytes.SequenceEqual(SerializeProjectBytes(redone)),
                "Group history Redo did not restore the project byte-for-byte.");
            var redoneSheet = redone.Sheets.Single(item => item.Id == sheet.Id);
            var redoneParent = redoneSheet.Groups.Single(group => group.Id == parent.Id);
            var redoneChild = redoneSheet.Groups.Single(group => group.Id == child.Id);
            Assert(
                redoneChild.ParentGroupId == redoneParent.Id &&
                redoneParent.MemberNodeIds.SequenceEqual(parent.MemberNodeIds) &&
                redoneChild.MemberNodeIds.SequenceEqual(child.MemberNodeIds),
                "Group history changed stable IDs, parent links, or direct members.");
        }

        private static void VerifyGroupValidationAndProjectIdentity()
        {
            var nullGroups = new CallSheet { Name = "FC_NullGroups" };
            nullGroups.Groups = null;
            var nullGroupsValidation = new DiagramValidator().Validate(nullGroups);
            Assert(
                nullGroupsValidation.Issues.Count(issue => issue.Code == "DGM040") == 1,
                "Missing group collection was not reported as DGM040.");

            var nullMembers = new CallSheet { Name = "FC_NullGroupMembers" };
            nullMembers.Groups.Add(new DiagramGroup
            {
                Title = "Null member collection",
                MemberNodeIds = null
            });
            var nullMembersValidation = new DiagramValidator().Validate(nullMembers);
            Assert(
                nullMembersValidation.Issues.Count(issue => issue.Code == "DGM051") == 1,
                "Missing direct-member collection was not reported as DGM051.");

            var invalid = new CallSheet { Name = "FC_InvalidGroups" };
            var member = new NoteNode { Title = "Member", Text = "Member", X = 20, Y = 20 };
            invalid.Nodes.Add(member);
            var first = new DiagramGroup("Invalid X", double.NaN, 0, 300, 200);
            var second = new DiagramGroup("Invalid width", 400, 0, 0, 200);
            first.ParentGroupId = second.Id;
            second.ParentGroupId = first.Id;
            first.MemberNodeIds.Add(member.Id);
            second.MemberNodeIds.Add(member.Id);
            invalid.Groups.Add(first);
            invalid.Groups.Add(second);

            var validator = new DiagramValidator();
            var firstValidation = validator.Validate(invalid);
            var secondValidation = validator.Validate(invalid);
            Assert(
                firstValidation.Issues.Count(issue => issue.Code == "DGM044") == 2,
                "Invalid group geometry was not reported deterministically for each group.");
            Assert(
                firstValidation.Issues.Count(issue => issue.Code == "DGM049") == 2,
                "Every member of a group-parent cycle was not reported as DGM049.");
            Assert(
                firstValidation.Issues.Count(issue => issue.Code == "DGM050") == 2,
                "Both direct owners of a multiply-owned node were not reported as DGM050.");
            Assert(
                firstValidation.Issues.Select(DiagramIssueFingerprint).SequenceEqual(
                    secondValidation.Issues.Select(DiagramIssueFingerprint)),
                "Group validation issue order or content is not deterministic.");

            var outsideSheetBounds = new CallSheet
            {
                Name = "FC_GroupOutsideBounds",
                Width = 500,
                Height = 300
            };
            outsideSheetBounds.Groups.Add(new DiagramGroup(
                "Outside sheet bounds",
                400,
                200,
                150,
                120));
            var outsideBoundsValidation = validator.Validate(outsideSheetBounds);
            Assert(
                outsideBoundsValidation.Issues.Count(issue => issue.Code == "DGM044") == 1,
                "A finite positive group extending beyond the sheet was not reported as DGM044.");

            var compiler = new DiagramProjectCompiler();
            var plantCollision = CreateProjectCompilerFixture();
            plantCollision.Sheets[0].Groups.Add(new DiagramGroup
            {
                Id = plantCollision.Plant.Id,
                Title = "Plant identity collision"
            });
            AssertProjectIssue(
                compiler.Compile(plantCollision),
                "DGP006",
                "Plant-to-group stable ID collision was not rejected.");

            var crossSheetCollision = CreateProjectCompilerFixture();
            var sharedGroupId = Guid.NewGuid();
            crossSheetCollision.Sheets[0].Groups.Add(new DiagramGroup
            {
                Id = sharedGroupId,
                Title = "Cross-sheet group A"
            });
            crossSheetCollision.Sheets[1].Groups.Add(new DiagramGroup
            {
                Id = sharedGroupId,
                Title = "Cross-sheet group B"
            });
            var crossSheetResult = compiler.Compile(crossSheetCollision);
            AssertProjectIssue(
                crossSheetResult,
                "DGP006",
                "Cross-sheet group stable ID collision was not rejected.");
            Assert(
                crossSheetResult.Issues.Count(issue => issue.Code == "DGP006") == 2,
                "Cross-sheet group identity collision was not scoped to both affected sheets.");
        }

        private static void VerifyGroupEditsDoNotChangeGeneratedScl()
        {
            var project = CreateValveDemo();
            var sheet = project.Sheets[0];
            var generator = new DiagramSclGenerator();
            var before = generator.Generate(project.Plant, sheet);
            Assert(before.IsSuccess, "Baseline group-neutral SCL generation failed: " + FormatIssues(before.Issues));

            var draft = GroupEditingLogic.CreateDraft(
                "SCL-neutral region",
                "Visual metadata only",
                0,
                0,
                1800,
                1000);
            GroupEditingLogic.Add(project, sheet.Id, draft, sheet.Nodes.Select(node => node.Id));
            AssertGeneratedSclUnchanged(before.Scl, generator.Generate(project.Plant, sheet), "Group Add");

            GroupEditingLogic.Edit(project, sheet.Id, draft.Id, "Renamed SCL-neutral region", "Edited metadata");
            GroupEditingLogic.Move(project, sheet.Id, draft.Id, 25.0, 30.0);
            AssertGeneratedSclUnchanged(before.Scl, generator.Generate(project.Plant, sheet), "Group Edit/Move");

            GroupEditingLogic.Ungroup(project, sheet.Id, draft.Id);
            AssertGeneratedSclUnchanged(before.Scl, generator.Generate(project.Plant, sheet), "Ungroup");
        }

        private static DiagramProject CreateGroupEditingFixture(
            out CallSheet sheet,
            out NoteNode first,
            out NoteNode second,
            out NoteNode third,
            out NoteNode outside)
        {
            var project = new DiagramProject();
            project.Plant.Name = "GroupEditingFixture";
            sheet = new CallSheet { Name = "FC_GroupEditingFixture" };
            first = new NoteNode { Title = "First", Text = "First", X = 100, Y = 120 };
            second = new NoteNode { Title = "Second", Text = "Second", X = 260, Y = 180 };
            third = new NoteNode { Title = "Third", Text = "Third", X = 420, Y = 240 };
            outside = new NoteNode { Title = "Outside", Text = "Outside", X = 1200, Y = 700 };
            sheet.Nodes.Add(first);
            sheet.Nodes.Add(second);
            sheet.Nodes.Add(third);
            sheet.Nodes.Add(outside);
            project.Sheets.Add(sheet);
            return project;
        }

        private static string DiagramIssueFingerprint(DiagramIssue issue)
        {
            return issue.Severity + "\u001f" + issue.Code + "\u001f" + issue.Message + "\u001f" +
                (issue.NodeId.HasValue ? issue.NodeId.Value.ToString("D") : string.Empty) + "\u001f" +
                (issue.WireId.HasValue ? issue.WireId.Value.ToString("D") : string.Empty);
        }

        private static void AssertGeneratedSclUnchanged(
            string expectedScl,
            DiagramCompilationResult actual,
            string subject)
        {
            Assert(actual.IsSuccess, subject + " made generation fail: " + FormatIssues(actual.Issues));
            Assert(
                Encoding.UTF8.GetBytes(expectedScl).SequenceEqual(Encoding.UTF8.GetBytes(actual.Scl)),
                subject + " changed generated SCL bytes.");
        }

        private static DiagramProject CreateHistoryFixture()
        {
            var project = CreateValveDemo();
            project.FormatVersion = 7;
            project.Plant.Name = "HistoryFixture";

            var valve = project.Plant.Blocks[0];
            valve.Version = "2.7";
            valve.OptimizedAccess = false;
            valve.ImportedInterfaceOnly = true;
            valve.Description = "Imported valve interface";
            valve.Interface[0].InitialValue = "TRUE";
            valve.Interface[0].Comment = "Command input";
            valve.Interface.Add(new InterfaceMember("InternalLatch", "Bool", InterfaceSection.Static)
            {
                InitialValue = "FALSE",
                Comment = "Persistent state"
            });

            project.Plant.Tags[0].Comment = "Source command";
            var dataType = new UdtDefinition
            {
                Name = "UDT_History",
                Version = "1.2",
                Comment = "Snapshot UDT"
            };
            dataType.Members.Add(new UdtMember("Enabled", "Bool")
            {
                InitialValue = "TRUE",
                Comment = "UDT member"
            });
            project.Plant.DataTypes.Add(dataType);

            var function = new BlockDefinition
            {
                Name = "FC_HistoryValue",
                Kind = BlockKind.Function,
                ReturnType = "DInt",
                Version = "1.1",
                OptimizedAccess = true,
                ImportedInterfaceOnly = false,
                Description = "History function",
                SclBody = "#FC_HistoryValue := 7;"
            };
            function.Interface.Add(new InterfaceMember("Seed", "DInt", InterfaceSection.Input)
            {
                InitialValue = "7",
                Comment = "Function input"
            });
            project.Plant.Blocks.Add(function);

            var callBlock = new CallBlockDefinition
            {
                Name = "FB_HistoryCalls",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = "DB_HistoryCalls",
                Description = "Legacy call model"
            };
            callBlock.Interface.Add(new InterfaceMember("Enable", "Bool", InterfaceSection.Input));
            var unit = new UnitDefinition("ValveHistory", valve)
            {
                UseMultiInstance = true,
                Comment = "History unit",
                ManualOrder = 2
            };
            unit.Bindings.Add(new ParameterBinding(valve.Interface[0], "HistoryCommand"));
            callBlock.Units.Add(unit);
            project.Plant.CallBlocks.Add(callBlock);

            var firstSheet = project.Sheets[0];
            firstSheet.Width = 2600.0;
            firstSheet.Height = 1800.0;
            firstSheet.Zoom = 0.625;
            firstSheet.Nodes.OfType<BlockCallNode>().Single().UseMultiInstance = true;
            firstSheet.Nodes[0].ManualOrder = 3;

            var secondSheet = new CallSheet
            {
                Name = "FC_HistoryLogic",
                Width = 1200.0,
                Height = 900.0,
                Zoom = 1.75
            };
            var constant = DiagramNodeFactory.CreateConstant("TRUE", "Bool", 25.0, 40.0);
            var logic = new LogicNode
            {
                Title = "History AND",
                Operation = LogicOperation.And,
                X = 250.0,
                Y = 40.0,
                ManualOrder = 1
            };
            logic.Pins.Add(new Pin
            {
                NodeId = logic.Id,
                Name = "In1",
                Direction = PinDirection.Input,
                Role = PinRole.Logic,
                DataType = "Bool",
                IsRequired = true,
                Order = 0
            });
            logic.Pins.Add(new Pin
            {
                NodeId = logic.Id,
                Name = "Result",
                Direction = PinDirection.Output,
                Role = PinRole.Logic,
                DataType = "Bool",
                Order = 1
            });
            var note = new NoteNode
            {
                Title = "Snapshot note",
                Text = "Keep IDs and text",
                X = 500.0,
                Y = 120.0
            };
            secondSheet.Nodes.Add(constant);
            secondSheet.Nodes.Add(logic);
            secondSheet.Nodes.Add(note);
            secondSheet.Wires.Add(new Wire
            {
                SourceNodeId = constant.Id,
                SourcePinId = constant.Pins[0].Id,
                TargetNodeId = logic.Id,
                TargetPinId = logic.Pins[0].Id
            });
            project.Sheets.Add(secondSheet);

            return project;
        }

        private static DiagramProject CreateHistoryState(
            DiagramProject template,
            string name,
            double firstNodeX)
        {
            var state = DiagramProjectSnapshot.Clone(template);
            state.Plant.Name = name;
            state.Sheets[0].Nodes[0].X = firstNodeX;
            return state;
        }

        private static void AssertSnapshotFixtureValues(DiagramProject project)
        {
            Assert(project.FormatVersion == 7, "Snapshot did not preserve the format version.");
            AssertEqual("HistoryFixture", project.Plant.Name, "Snapshot plant name");

            var valve = project.Plant.Blocks.Single(block => block.Name == "FB_Valve");
            Assert(!valve.OptimizedAccess, "Snapshot did not preserve OptimizedAccess=false.");
            Assert(valve.ImportedInterfaceOnly, "Snapshot did not preserve ImportedInterfaceOnly=true.");
            AssertEqual("2.7", valve.Version, "Snapshot block version");
            var command = valve.Interface.Single(member => member.Name == "Open");
            Assert(command.Section == InterfaceSection.Input, "Snapshot changed an interface section.");
            AssertEqual("TRUE", command.InitialValue, "Snapshot interface initial value");
            AssertEqual("Command input", command.Comment, "Snapshot interface comment");
            var state = valve.Interface.Single(member => member.Name == "InternalLatch");
            Assert(state.Section == InterfaceSection.Static, "Snapshot changed a static interface section.");
            AssertEqual("FALSE", state.InitialValue, "Snapshot static initial value");

            var function = project.Plant.Blocks.Single(block => block.Name == "FC_HistoryValue");
            Assert(function.Kind == BlockKind.Function, "Snapshot changed the FC block kind.");
            AssertEqual("DInt", function.ReturnType, "Snapshot FC return type");
            Assert(!function.ImportedInterfaceOnly, "Snapshot changed ImportedInterfaceOnly=false.");

            var firstSheet = project.Sheets.Single(sheet => sheet.Name == "FC_CallUnits");
            var secondSheet = project.Sheets.Single(sheet => sheet.Name == "FC_HistoryLogic");
            Assert(firstSheet.Zoom == 0.625, "Snapshot did not preserve a fractional sheet zoom.");
            Assert(secondSheet.Zoom == 1.75, "Snapshot did not preserve the second sheet zoom.");
            Assert(
                firstSheet.Nodes.OfType<BlockCallNode>().Single().UseMultiInstance,
                "Snapshot did not preserve UseMultiInstance=true.");
            Assert(
                secondSheet.Nodes.OfType<NoteNode>().Single().Text == "Keep IDs and text",
                "Snapshot did not preserve a derived note node.");
            Assert(project.Plant.DataTypes[0].Members.Count == 1, "Snapshot lost a UDT member.");
            Assert(
                project.Plant.CallBlocks[0].Units[0].Bindings.Count == 1,
                "Snapshot lost a legacy parameter binding.");
        }

        private static void AssertIndependentProjectGraphs(
            DiagramProject expected,
            DiagramProject actual,
            string subject)
        {
            Assert(!ReferenceEquals(expected, actual), subject + " reused the project instance.");
            Assert(!ReferenceEquals(expected.Plant, actual.Plant), subject + " reused the Plant instance.");
            Assert(!ReferenceEquals(expected.Plant.Blocks, actual.Plant.Blocks), subject + " reused the block list.");
            Assert(!ReferenceEquals(expected.Plant.DataTypes, actual.Plant.DataTypes), subject + " reused the UDT list.");
            Assert(!ReferenceEquals(expected.Plant.Tags, actual.Plant.Tags), subject + " reused the tag list.");
            Assert(
                !ReferenceEquals(expected.Plant.CallBlocks, actual.Plant.CallBlocks),
                subject + " reused the call-block list.");
            Assert(!ReferenceEquals(expected.Sheets, actual.Sheets), subject + " reused the sheet list.");

            if (expected.Plant.Blocks.Count != 0 && actual.Plant.Blocks.Count != 0)
            {
                var expectedBlock = expected.Plant.Blocks[0];
                var actualBlock = actual.Plant.Blocks[0];
                Assert(!ReferenceEquals(expectedBlock, actualBlock), subject + " reused a block instance.");
                Assert(
                    !ReferenceEquals(expectedBlock.Interface, actualBlock.Interface),
                    subject + " reused an interface list.");
                if (expectedBlock.Interface.Count != 0 && actualBlock.Interface.Count != 0)
                {
                    Assert(
                        !ReferenceEquals(expectedBlock.Interface[0], actualBlock.Interface[0]),
                        subject + " reused an interface member instance.");
                }
            }

            if (expected.Plant.DataTypes.Count != 0 && actual.Plant.DataTypes.Count != 0)
            {
                Assert(
                    !ReferenceEquals(expected.Plant.DataTypes[0], actual.Plant.DataTypes[0]),
                    subject + " reused a UDT instance.");
                Assert(
                    !ReferenceEquals(expected.Plant.DataTypes[0].Members, actual.Plant.DataTypes[0].Members),
                    subject + " reused a UDT member list.");
                if (expected.Plant.DataTypes[0].Members.Count != 0 && actual.Plant.DataTypes[0].Members.Count != 0)
                {
                    Assert(
                        !ReferenceEquals(expected.Plant.DataTypes[0].Members[0], actual.Plant.DataTypes[0].Members[0]),
                        subject + " reused a UDT member instance.");
                }
            }

            if (expected.Plant.Tags.Count != 0 && actual.Plant.Tags.Count != 0)
            {
                Assert(!ReferenceEquals(expected.Plant.Tags[0], actual.Plant.Tags[0]), subject + " reused a tag instance.");
            }

            if (expected.Plant.CallBlocks.Count != 0 && actual.Plant.CallBlocks.Count != 0)
            {
                var expectedCallBlock = expected.Plant.CallBlocks[0];
                var actualCallBlock = actual.Plant.CallBlocks[0];
                Assert(
                    !ReferenceEquals(expectedCallBlock, actualCallBlock),
                    subject + " reused a call-block instance.");
                Assert(
                    !ReferenceEquals(expectedCallBlock.Units, actualCallBlock.Units),
                    subject + " reused a unit list.");
                if (expectedCallBlock.Units.Count != 0 && actualCallBlock.Units.Count != 0)
                {
                    var expectedUnit = expectedCallBlock.Units[0];
                    var actualUnit = actualCallBlock.Units[0];
                    Assert(!ReferenceEquals(expectedUnit, actualUnit), subject + " reused a unit instance.");
                    Assert(
                        !ReferenceEquals(expectedUnit.Bindings, actualUnit.Bindings),
                        subject + " reused a parameter-binding list.");
                    if (expectedUnit.Bindings.Count != 0 && actualUnit.Bindings.Count != 0)
                    {
                        Assert(
                            !ReferenceEquals(expectedUnit.Bindings[0], actualUnit.Bindings[0]),
                            subject + " reused a parameter binding instance.");
                    }
                }
            }

            if (expected.Sheets.Count != 0 && actual.Sheets.Count != 0)
            {
                var expectedSheet = expected.Sheets[0];
                var actualSheet = actual.Sheets[0];
                Assert(!ReferenceEquals(expectedSheet, actualSheet), subject + " reused a sheet instance.");
                Assert(!ReferenceEquals(expectedSheet.Nodes, actualSheet.Nodes), subject + " reused a node list.");
                Assert(!ReferenceEquals(expectedSheet.Wires, actualSheet.Wires), subject + " reused a wire list.");
                if (expectedSheet.Nodes.Count != 0 && actualSheet.Nodes.Count != 0)
                {
                    var expectedNode = expectedSheet.Nodes[0];
                    var actualNode = actualSheet.Nodes[0];
                    Assert(!ReferenceEquals(expectedNode, actualNode), subject + " reused a node instance.");
                    Assert(!ReferenceEquals(expectedNode.Pins, actualNode.Pins), subject + " reused a pin list.");
                    if (expectedNode.Pins.Count != 0 && actualNode.Pins.Count != 0)
                    {
                        Assert(
                            !ReferenceEquals(expectedNode.Pins[0], actualNode.Pins[0]),
                            subject + " reused a pin instance.");
                    }
                }

                if (expectedSheet.Wires.Count != 0 && actualSheet.Wires.Count != 0)
                {
                    Assert(
                        !ReferenceEquals(expectedSheet.Wires[0], actualSheet.Wires[0]),
                        subject + " reused a wire instance.");
                }
            }
        }

        private static IEnumerable<Guid> GetPersistentObjectIds(DiagramProject project)
        {
            yield return project.Plant.Id;
            foreach (var block in project.Plant.Blocks)
            {
                yield return block.Id;
                foreach (var member in block.Interface)
                {
                    yield return member.Id;
                }
            }

            foreach (var dataType in project.Plant.DataTypes)
            {
                yield return dataType.Id;
                foreach (var member in dataType.Members)
                {
                    yield return member.Id;
                }
            }

            foreach (var tag in project.Plant.Tags)
            {
                yield return tag.Id;
            }

            foreach (var callBlock in project.Plant.CallBlocks)
            {
                yield return callBlock.Id;
                foreach (var member in callBlock.Interface)
                {
                    yield return member.Id;
                }

                foreach (var unit in callBlock.Units)
                {
                    yield return unit.Id;
                    foreach (var binding in unit.Bindings)
                    {
                        yield return binding.Id;
                    }
                }
            }

            foreach (var sheet in project.Sheets)
            {
                yield return sheet.Id;
                foreach (var node in sheet.Nodes)
                {
                    yield return node.Id;
                    foreach (var pin in node.Pins)
                    {
                        yield return pin.Id;
                    }
                }

                foreach (var wire in sheet.Wires)
                {
                    yield return wire.Id;
                }
            }
        }

        private static IEnumerable<string> GetPersistentGuidState(DiagramProject project)
        {
            var state = new List<string> { "Plant.Id=" + project.Plant.Id.ToString("D") };
            for (var blockIndex = 0; blockIndex < project.Plant.Blocks.Count; blockIndex++)
            {
                var block = project.Plant.Blocks[blockIndex];
                state.Add("Block[" + blockIndex + "].Id=" + block.Id.ToString("D"));
                for (var memberIndex = 0; memberIndex < block.Interface.Count; memberIndex++)
                {
                    state.Add(
                        "Block[" + blockIndex + "].Interface[" + memberIndex + "].Id=" +
                        block.Interface[memberIndex].Id.ToString("D"));
                }
            }

            for (var typeIndex = 0; typeIndex < project.Plant.DataTypes.Count; typeIndex++)
            {
                var dataType = project.Plant.DataTypes[typeIndex];
                state.Add("DataType[" + typeIndex + "].Id=" + dataType.Id.ToString("D"));
                for (var memberIndex = 0; memberIndex < dataType.Members.Count; memberIndex++)
                {
                    state.Add(
                        "DataType[" + typeIndex + "].Member[" + memberIndex + "].Id=" +
                        dataType.Members[memberIndex].Id.ToString("D"));
                }
            }

            for (var tagIndex = 0; tagIndex < project.Plant.Tags.Count; tagIndex++)
            {
                state.Add("Tag[" + tagIndex + "].Id=" + project.Plant.Tags[tagIndex].Id.ToString("D"));
            }

            for (var callIndex = 0; callIndex < project.Plant.CallBlocks.Count; callIndex++)
            {
                var callBlock = project.Plant.CallBlocks[callIndex];
                state.Add("CallBlock[" + callIndex + "].Id=" + callBlock.Id.ToString("D"));
                for (var memberIndex = 0; memberIndex < callBlock.Interface.Count; memberIndex++)
                {
                    state.Add(
                        "CallBlock[" + callIndex + "].Interface[" + memberIndex + "].Id=" +
                        callBlock.Interface[memberIndex].Id.ToString("D"));
                }

                for (var unitIndex = 0; unitIndex < callBlock.Units.Count; unitIndex++)
                {
                    var unit = callBlock.Units[unitIndex];
                    state.Add("CallBlock[" + callIndex + "].Unit[" + unitIndex + "].Id=" + unit.Id.ToString("D"));
                    state.Add(
                        "CallBlock[" + callIndex + "].Unit[" + unitIndex + "].BlockId=" +
                        unit.BlockId.ToString("D"));
                    for (var bindingIndex = 0; bindingIndex < unit.Bindings.Count; bindingIndex++)
                    {
                        var binding = unit.Bindings[bindingIndex];
                        state.Add(
                            "CallBlock[" + callIndex + "].Unit[" + unitIndex + "].Binding[" +
                            bindingIndex + "].Id=" + binding.Id.ToString("D"));
                        state.Add(
                            "CallBlock[" + callIndex + "].Unit[" + unitIndex + "].Binding[" +
                            bindingIndex + "].ParameterId=" + binding.ParameterId.ToString("D"));
                    }
                }
            }

            for (var sheetIndex = 0; sheetIndex < project.Sheets.Count; sheetIndex++)
            {
                var sheet = project.Sheets[sheetIndex];
                state.Add("Sheet[" + sheetIndex + "].Id=" + sheet.Id.ToString("D"));
                for (var nodeIndex = 0; nodeIndex < sheet.Nodes.Count; nodeIndex++)
                {
                    var node = sheet.Nodes[nodeIndex];
                    state.Add(
                        "Sheet[" + sheetIndex + "].Node[" + nodeIndex + "].Id=" + node.Id.ToString("D"));
                    var blockCall = node as BlockCallNode;
                    if (blockCall != null)
                    {
                        state.Add(
                            "Sheet[" + sheetIndex + "].Node[" + nodeIndex + "].BlockDefinitionId=" +
                            blockCall.BlockDefinitionId.ToString("D"));
                    }

                    var tagNode = node as TagNode;
                    if (tagNode != null)
                    {
                        state.Add(
                            "Sheet[" + sheetIndex + "].Node[" + nodeIndex + "].TagDefinitionId=" +
                            tagNode.TagDefinitionId.ToString("D"));
                    }

                    for (var pinIndex = 0; pinIndex < node.Pins.Count; pinIndex++)
                    {
                        var pin = node.Pins[pinIndex];
                        var prefix = "Sheet[" + sheetIndex + "].Node[" + nodeIndex + "].Pin[" + pinIndex + "]";
                        state.Add(prefix + ".Id=" + pin.Id.ToString("D"));
                        state.Add(prefix + ".NodeId=" + pin.NodeId.ToString("D"));
                        state.Add(prefix + ".MemberId=" + pin.MemberId.ToString("D"));
                    }
                }

                for (var wireIndex = 0; wireIndex < sheet.Wires.Count; wireIndex++)
                {
                    var wire = sheet.Wires[wireIndex];
                    var prefix = "Sheet[" + sheetIndex + "].Wire[" + wireIndex + "]";
                    state.Add(prefix + ".Id=" + wire.Id.ToString("D"));
                    state.Add(prefix + ".SourceNodeId=" + wire.SourceNodeId.ToString("D"));
                    state.Add(prefix + ".SourcePinId=" + wire.SourcePinId.ToString("D"));
                    state.Add(prefix + ".TargetNodeId=" + wire.TargetNodeId.ToString("D"));
                    state.Add(prefix + ".TargetPinId=" + wire.TargetPinId.ToString("D"));
                }
            }

            return state;
        }

        private static IEnumerable<string> GetWireState(DiagramProject project)
        {
            var state = new List<string>();
            for (var sheetIndex = 0; sheetIndex < project.Sheets.Count; sheetIndex++)
            {
                var sheet = project.Sheets[sheetIndex];
                for (var wireIndex = 0; wireIndex < sheet.Wires.Count; wireIndex++)
                {
                    var wire = sheet.Wires[wireIndex];
                    state.Add(
                        sheet.Id.ToString("D") + ":" + wire.Id.ToString("D") + ":" +
                        wire.SourceNodeId.ToString("D") + ":" + wire.SourcePinId.ToString("D") + ":" +
                        wire.TargetNodeId.ToString("D") + ":" + wire.TargetPinId.ToString("D"));
                }
            }

            return state;
        }

        private static DiagramProject CreateAutoLayoutPreservationProject()
        {
            var definition = new BlockDefinition
            {
                Name = "FB_AutoLayoutUnit",
                Kind = BlockKind.FunctionBlock,
                Description = "Auto-layout byte preservation fixture",
                SclBody = "// Layout-neutral implementation"
            };
            definition.Interface.Add(new InterfaceMember(
                "Enable",
                "Bool",
                InterfaceSection.Input));
            definition.Interface.Add(new InterfaceMember(
                "Delay",
                "Time",
                InterfaceSection.Input));
            definition.Interface.Add(new InterfaceMember(
                "Q",
                "Bool",
                InterfaceSection.Output));

            var plant = new PlantProject { Name = "AutoLayoutPreservation" };
            plant.Blocks.Add(definition);
            var enableA = AddTag(plant, "Auto_Enable_A", "Bool");
            var enableB = AddTag(plant, "Auto_Enable_B", "Bool");
            var resultA = AddTag(plant, "Auto_Result_A", "Bool");
            var resultB = AddTag(plant, "Auto_Result_B", "Bool");
            var logicLeft = AddTag(plant, "Auto_Logic_Left", "Bool");
            var logicRight = AddTag(plant, "Auto_Logic_Right", "Bool");
            var logicResult = AddTag(plant, "Auto_Logic_Result", "Bool");

            var sheet = new CallSheet
            {
                Name = "FC_AutoLayoutPreservation",
                Width = 1800.0,
                Height = 1100.0,
                Zoom = 0.73
            };
            var enableANode = DiagramNodeFactory.CreateTag(
                enableA,
                TerminalDirection.Source,
                1300.0,
                720.0);
            var enableBNode = DiagramNodeFactory.CreateTag(
                enableB,
                TerminalDirection.Source,
                1180.0,
                100.0);
            var constant = DiagramNodeFactory.CreateConstant(
                "T#250ms",
                "Time",
                1050.0,
                470.0);
            var callB = DiagramNodeFactory.CreateBlockCall(
                definition,
                "Auto_Unit_B",
                80.0,
                380.0);
            var callA = DiagramNodeFactory.CreateBlockCall(
                definition,
                "Auto_Unit_A",
                240.0,
                80.0);
            var resultANode = DiagramNodeFactory.CreateTag(
                resultA,
                TerminalDirection.Sink,
                500.0,
                720.0);
            var resultBNode = DiagramNodeFactory.CreateTag(
                resultB,
                TerminalDirection.Sink,
                520.0,
                120.0);
            var logicLeftNode = DiagramNodeFactory.CreateTag(
                logicLeft,
                TerminalDirection.Source,
                900.0,
                210.0);
            var logicRightNode = DiagramNodeFactory.CreateTag(
                logicRight,
                TerminalDirection.Source,
                900.0,
                310.0);
            var logic = DiagramNodeFactory.CreateLogic(
                LogicOperation.And,
                "Bool",
                900.0,
                520.0);
            var logicResultNode = DiagramNodeFactory.CreateTag(
                logicResult,
                TerminalDirection.Sink,
                120.0,
                650.0);
            var laterNote = NoteEditingLogic.Create(
                "Second generated note",
                1350.0,
                650.0);
            var earlierNote = NoteEditingLogic.Create(
                "First generated note",
                1450.0,
                40.0);

            sheet.Nodes.Add(enableANode);
            sheet.Nodes.Add(enableBNode);
            sheet.Nodes.Add(constant);
            sheet.Nodes.Add(callB);
            sheet.Nodes.Add(callA);
            sheet.Nodes.Add(resultANode);
            sheet.Nodes.Add(resultBNode);
            sheet.Nodes.Add(logicLeftNode);
            sheet.Nodes.Add(logicRightNode);
            sheet.Nodes.Add(logic);
            sheet.Nodes.Add(logicResultNode);
            // Intentionally opposite to visual order: the generator sorts by Y/X.
            sheet.Nodes.Add(laterNote);
            sheet.Nodes.Add(earlierNote);

            Connect(sheet, enableANode, enableA.Name, callA, "Enable");
            Connect(sheet, constant, "Value", callA, "Delay");
            Connect(sheet, callA, "Q", resultANode, resultA.Name);
            Connect(sheet, enableBNode, enableB.Name, callB, "Enable");
            Connect(sheet, constant, "Value", callB, "Delay");
            Connect(sheet, callB, "Q", resultBNode, resultB.Name);
            Connect(sheet, logicLeftNode, logicLeft.Name, logic, "In1");
            Connect(sheet, logicRightNode, logicRight.Name, logic, "In2");
            Connect(sheet, logic, "Result", logicResultNode, logicResult.Name);

            return new DiagramProject
            {
                Plant = plant,
                Sheets = new List<CallSheet> { sheet }
            };
        }

        private static CallSheet CreateAutoLayoutCrossingSheet()
        {
            var definition = new BlockDefinition
            {
                Name = "FB_CrossingTarget",
                Kind = BlockKind.FunctionBlock
            };
            definition.Interface.Add(new InterfaceMember(
                "Enable",
                "Bool",
                InterfaceSection.Input));
            var plant = new PlantProject { Name = "CrossingFixture" };
            var sourceA = AddTag(plant, "Cross_Source_A", "Bool");
            var sourceB = AddTag(plant, "Cross_Source_B", "Bool");
            var sourceANode = DiagramNodeFactory.CreateTag(
                sourceA,
                TerminalDirection.Source,
                40.0,
                60.0);
            var sourceBNode = DiagramNodeFactory.CreateTag(
                sourceB,
                TerminalDirection.Source,
                40.0,
                360.0);
            var targetA = DiagramNodeFactory.CreateBlockCall(
                definition,
                "Cross_Target_A",
                700.0,
                360.0);
            var targetB = DiagramNodeFactory.CreateBlockCall(
                definition,
                "Cross_Target_B",
                700.0,
                60.0);
            var sheet = new CallSheet
            {
                Name = "FC_CrossingFixture",
                Width = 1400.0,
                Height = 900.0
            };
            sheet.Nodes.Add(sourceANode);
            sheet.Nodes.Add(sourceBNode);
            sheet.Nodes.Add(targetA);
            sheet.Nodes.Add(targetB);
            Connect(sheet, sourceANode, sourceA.Name, targetA, "Enable");
            Connect(sheet, sourceBNode, sourceB.Name, targetB, "Enable");
            return sheet;
        }

        private static DiagramProject CreateAutoLayoutCycleAndGroupProject()
        {
            var definition = new BlockDefinition
            {
                Name = "FB_AutoLayoutCycle",
                Kind = BlockKind.FunctionBlock
            };
            definition.Interface.Add(new InterfaceMember(
                "In",
                "Bool",
                InterfaceSection.Input));
            definition.Interface.Add(new InterfaceMember(
                "Out",
                "Bool",
                InterfaceSection.Output));
            definition.Interface.Add(new InterfaceMember(
                "Side",
                "Bool",
                InterfaceSection.Output));

            var plant = new PlantProject { Name = "AutoLayoutCycleAndGroups" };
            plant.Blocks.Add(definition);
            var healthyInput = AddTag(plant, "Cycle_Healthy_Input", "Bool");
            var healthyOutput = AddTag(plant, "Cycle_Healthy_Output", "Bool");
            var disconnectedTag = AddTag(plant, "Cycle_Disconnected_Tag", "Bool");

            var cycleA = DiagramNodeFactory.CreateBlockCall(definition, "Cycle_A_DB", 100.0, 100.0);
            cycleA.Title = "Cycle_A";
            var cycleB = DiagramNodeFactory.CreateBlockCall(definition, "Cycle_B_DB", 400.0, 100.0);
            cycleB.Title = "Cycle_B";
            var downstream = DiagramNodeFactory.CreateBlockCall(definition, "Downstream_DB", 700.0, 100.0);
            downstream.Title = "Downstream";
            var selfCycle = DiagramNodeFactory.CreateBlockCall(definition, "Self_Cycle_DB", 1000.0, 100.0);
            selfCycle.Title = "Self_Cycle";
            var healthySource = DiagramNodeFactory.CreateTag(
                healthyInput,
                TerminalDirection.Source,
                120.0,
                500.0);
            healthySource.Title = "Healthy_Source";
            var healthyBlock = DiagramNodeFactory.CreateBlockCall(
                definition,
                "Healthy_Block_DB",
                450.0,
                500.0);
            healthyBlock.Title = "Healthy_Block";
            var healthySink = DiagramNodeFactory.CreateTag(
                healthyOutput,
                TerminalDirection.Sink,
                800.0,
                500.0);
            healthySink.Title = "Healthy_Sink";
            var isolatedBlock = DiagramNodeFactory.CreateBlockCall(
                definition,
                "Disconnected_FB_DB",
                120.0,
                780.0);
            isolatedBlock.Title = "Disconnected_FB";
            var isolatedTag = DiagramNodeFactory.CreateTag(
                disconnectedTag,
                TerminalDirection.Source,
                450.0,
                780.0);
            isolatedTag.Title = "Disconnected_Tag";
            var isolatedConstant = DiagramNodeFactory.CreateConstant(
                "T#17ms",
                "Time",
                700.0,
                780.0);
            var isolatedNote = NoteEditingLogic.Create(
                "Disconnected note",
                950.0,
                780.0);

            var sheet = new CallSheet
            {
                Name = "FC_AutoLayoutCycleAndGroups",
                Width = 1800.0,
                Height = 1200.0
            };
            sheet.Nodes.Add(cycleA);
            sheet.Nodes.Add(cycleB);
            sheet.Nodes.Add(downstream);
            sheet.Nodes.Add(selfCycle);
            sheet.Nodes.Add(healthySource);
            sheet.Nodes.Add(healthyBlock);
            sheet.Nodes.Add(healthySink);
            sheet.Nodes.Add(isolatedBlock);
            sheet.Nodes.Add(isolatedTag);
            sheet.Nodes.Add(isolatedConstant);
            sheet.Nodes.Add(isolatedNote);

            Connect(sheet, cycleA, "Out", cycleB, "In");
            Connect(sheet, cycleB, "Out", cycleA, "In");
            Connect(sheet, cycleB, "Side", downstream, "In");
            Connect(sheet, selfCycle, "Out", selfCycle, "In");
            Connect(sheet, healthySource, healthyInput.Name, healthyBlock, "In");
            Connect(sheet, healthyBlock, "Out", healthySink, healthyOutput.Name);

            var parent = new DiagramGroup("Parent process", 50.0, 430.0, 1000.0, 400.0)
            {
                Comment = "Parent metadata"
            };
            parent.MemberNodeIds.Add(healthyBlock.Id);
            var child = new DiagramGroup("Child inputs", 80.0, 460.0, 260.0, 180.0)
            {
                Comment = "Child metadata",
                ParentGroupId = parent.Id
            };
            child.MemberNodeIds.Add(healthySource.Id);
            sheet.Groups.Add(parent);
            sheet.Groups.Add(child);

            return new DiagramProject
            {
                Plant = plant,
                Sheets = new List<CallSheet> { sheet }
            };
        }

        private static DiagramProject CreateAutoLayoutDenseProject()
        {
            var definition = new BlockDefinition
            {
                Name = "FB_AutoLayoutDense",
                Kind = BlockKind.FunctionBlock,
                SclBody = "// Dense auto-layout fixture"
            };
            definition.Interface.Add(new InterfaceMember("Input1", "Bool", InterfaceSection.Input));
            definition.Interface.Add(new InterfaceMember("Input2", "Bool", InterfaceSection.Input));
            definition.Interface.Add(new InterfaceMember("Input3", "Bool", InterfaceSection.Input));
            definition.Interface.Add(new InterfaceMember("Output1", "Bool", InterfaceSection.Output));
            definition.Interface.Add(new InterfaceMember("Output2", "Bool", InterfaceSection.Output));
            var plant = new PlantProject { Name = "AutoLayoutDense100" };
            plant.Blocks.Add(definition);
            var denseSheet = new CallSheet
            {
                Name = "FC_AutoLayoutDense100",
                Width = 1180.0,
                Height = 700.0,
                Zoom = 0.56
            };
            for (var index = 0; index < 100; index++)
            {
                denseSheet.Nodes.Add(DiagramNodeFactory.CreateBlockCall(
                    definition,
                    "Dense_" + (index + 1).ToString("000"),
                    240.0,
                    180.0));
            }

            var untouched = new CallSheet
            {
                Name = "FC_AutoLayoutUntouched",
                Width = 1700.0,
                Height = 950.0,
                Zoom = 0.81
            };
            return new DiagramProject
            {
                Plant = plant,
                Sheets = new List<CallSheet> { denseSheet, untouched }
            };
        }

        private static double GetAutoLayoutNodeWidth(CallNode node)
        {
            if (node is BlockCallNode)
            {
                return 272.0;
            }

            if (node is TagNode)
            {
                return 154.0;
            }

            if (node is ConstantNode)
            {
                return 128.0;
            }

            if (node is LogicNode)
            {
                return 224.0;
            }

            if (node is NoteNode)
            {
                return 238.0;
            }

            return 220.0;
        }

        private static double GetAutoLayoutNodeHeight(CallNode node)
        {
            if (node is BlockCallNode)
            {
                return 72.0 + 32.0 * Math.Max(
                    node.Pins.Count(pin => pin.Direction == PinDirection.Input),
                    node.Pins.Count(pin => pin.Direction == PinDirection.Output));
            }

            if (node is LogicNode)
            {
                return 57.0 + 32.0 * Math.Max(
                    node.Pins.Count(pin => pin.Direction == PinDirection.Input),
                    node.Pins.Count(pin => pin.Direction == PinDirection.Output));
            }

            return node is NoteNode ? 112.0 : 44.0;
        }

        private static System.Windows.Rect GetAutoLayoutNodeBounds(CallNode node)
        {
            return new System.Windows.Rect(
                node.X,
                node.Y,
                GetAutoLayoutNodeWidth(node),
                GetAutoLayoutNodeHeight(node));
        }

        private static System.Windows.Rect GetAutoLayoutContentBounds(CallSheet sheet)
        {
            var bounds = System.Windows.Rect.Empty;
            foreach (var node in sheet.Nodes)
            {
                bounds.Union(GetAutoLayoutNodeBounds(node));
            }

            foreach (var group in sheet.Groups)
            {
                bounds.Union(new System.Windows.Rect(
                    group.X,
                    group.Y,
                    group.Width,
                    group.Height));
            }

            return bounds;
        }

        private static void AssertAutoLayoutNodesDoNotOverlap(
            CallSheet sheet,
            double minimumGap,
            string subject)
        {
            for (var firstIndex = 0; firstIndex < sheet.Nodes.Count; firstIndex++)
            {
                var first = GetAutoLayoutNodeBounds(sheet.Nodes[firstIndex]);
                for (var secondIndex = firstIndex + 1;
                    secondIndex < sheet.Nodes.Count;
                    secondIndex++)
                {
                    var second = GetAutoLayoutNodeBounds(sheet.Nodes[secondIndex]);
                    var separated =
                        first.Right + minimumGap <= second.Left ||
                        second.Right + minimumGap <= first.Left ||
                        first.Bottom + minimumGap <= second.Top ||
                        second.Bottom + minimumGap <= first.Top;
                    Assert(
                        separated,
                        subject + " nodes overlap or violate the minimum gap: '" +
                        sheet.Nodes[firstIndex].Title + "' and '" +
                        sheet.Nodes[secondIndex].Title + "'.");
                }
            }
        }

        private static void AssertAutoLayoutContentInsideSheet(CallSheet sheet, string subject)
        {
            Assert(
                !double.IsNaN(sheet.Width) &&
                !double.IsInfinity(sheet.Width) &&
                !double.IsNaN(sheet.Height) &&
                !double.IsInfinity(sheet.Height) &&
                sheet.Width > 0.0 &&
                sheet.Height > 0.0,
                subject + " has invalid sheet dimensions.");
            var sheetBounds = new System.Windows.Rect(0.0, 0.0, sheet.Width, sheet.Height);
            foreach (var node in sheet.Nodes)
            {
                AssertAutoLayoutContains(
                    sheetBounds,
                    GetAutoLayoutNodeBounds(node),
                    subject + " node '" + node.Title + "'");
            }

            foreach (var group in sheet.Groups)
            {
                AssertAutoLayoutContains(
                    sheetBounds,
                    new System.Windows.Rect(group.X, group.Y, group.Width, group.Height),
                    subject + " group '" + group.Title + "'");
            }
        }

        private static void AssertAutoLayoutRoleColumns(CallSheet sheet)
        {
            var sources = sheet.Nodes.Where(node =>
                node is ConstantNode ||
                (node is TagNode &&
                    ((TagNode)node).TerminalDirection == TerminalDirection.Source)).ToArray();
            var logic = sheet.Nodes.OfType<LogicNode>().ToArray();
            var blocks = sheet.Nodes.OfType<BlockCallNode>().ToArray();
            var sinks = sheet.Nodes.OfType<TagNode>().Where(node =>
                node.TerminalDirection == TerminalDirection.Sink).ToArray();
            Assert(sources.Max(node => node.X) < logic.Min(node => node.X),
                "Source tags/constants were not placed before logic nodes.");
            Assert(logic.Max(node => node.X) < blocks.Min(node => node.X),
                "Logic nodes were not placed before FB/FC calls.");
            Assert(blocks.Max(node => node.X) < sinks.Min(node => node.X),
                "FB/FC calls were not placed before output tags.");
        }

        private static void AssertAutoLayoutEdgesMoveForward(CallSheet sheet)
        {
            var nodes = sheet.Nodes.ToDictionary(node => node.Id);
            foreach (var wire in sheet.Wires)
            {
                var source = nodes[wire.SourceNodeId];
                var target = nodes[wire.TargetNodeId];
                Assert(source.X < target.X,
                    "Auto-layout left a main-flow wire pointing backwards from '" +
                    source.Title + "' to '" + target.Title + "'.");
            }
        }

        private static void AssertAutoLayoutGroupsContainContents(CallSheet sheet)
        {
            var nodes = sheet.Nodes.ToDictionary(node => node.Id);
            foreach (var group in sheet.Groups)
            {
                var groupBounds = new System.Windows.Rect(
                    group.X,
                    group.Y,
                    group.Width,
                    group.Height);
                foreach (var nodeId in group.MemberNodeIds)
                {
                    AssertAutoLayoutContains(
                        groupBounds,
                        GetAutoLayoutNodeBounds(nodes[nodeId]),
                        "Group '" + group.Title + "' direct node");
                }

                foreach (var child in sheet.Groups.Where(item =>
                    item.ParentGroupId == group.Id))
                {
                    AssertAutoLayoutContains(
                        groupBounds,
                        new System.Windows.Rect(
                            child.X,
                            child.Y,
                            child.Width,
                            child.Height),
                        "Group '" + group.Title + "' child group '" + child.Title + "'");
                }
            }
        }

        private static void AssertAutoLayoutContains(
            System.Windows.Rect outer,
            System.Windows.Rect inner,
            string subject)
        {
            const double tolerance = 0.000001;
            Assert(
                inner.Left + tolerance >= outer.Left &&
                inner.Top + tolerance >= outer.Top &&
                inner.Right <= outer.Right + tolerance &&
                inner.Bottom <= outer.Bottom + tolerance,
                subject + " escaped its expected bounds.");
        }

        private static Guid[] GetAutoLayoutGeneratedNoteOrder(CallSheet sheet)
        {
            return sheet.Nodes
                .Select((node, index) => new { Node = node, Index = index })
                .Where(item => item.Node is NoteNode)
                .OrderBy(item => item.Node.Y)
                .ThenBy(item => item.Node.X)
                .ThenBy(item => item.Index)
                .Select(item => item.Node.Id)
                .ToArray();
        }

        private static IEnumerable<string> GetAutoLayoutWireState(CallSheet sheet)
        {
            return sheet.Wires.Select(wire =>
                wire.Id.ToString("D") + ":" +
                wire.SourceNodeId.ToString("D") + ":" +
                wire.SourcePinId.ToString("D") + ":" +
                wire.TargetNodeId.ToString("D") + ":" +
                wire.TargetPinId.ToString("D"));
        }

        private static int CountAutoLayoutWireCrossings(CallSheet sheet)
        {
            var nodes = sheet.Nodes.ToDictionary(node => node.Id);
            var segments = sheet.Wires.Select(wire => new
            {
                Wire = wire,
                Start = new System.Windows.Point(
                    nodes[wire.SourceNodeId].X +
                        GetAutoLayoutNodeWidth(nodes[wire.SourceNodeId]),
                    nodes[wire.SourceNodeId].Y +
                        GetAutoLayoutNodeHeight(nodes[wire.SourceNodeId]) / 2.0),
                End = new System.Windows.Point(
                    nodes[wire.TargetNodeId].X,
                    nodes[wire.TargetNodeId].Y +
                        GetAutoLayoutNodeHeight(nodes[wire.TargetNodeId]) / 2.0)
            }).ToArray();
            var count = 0;
            for (var firstIndex = 0; firstIndex < segments.Length; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1;
                    secondIndex < segments.Length;
                    secondIndex++)
                {
                    var first = segments[firstIndex];
                    var second = segments[secondIndex];
                    if (first.Wire.SourceNodeId == second.Wire.SourceNodeId ||
                        first.Wire.TargetNodeId == second.Wire.TargetNodeId ||
                        first.Wire.SourceNodeId == second.Wire.TargetNodeId ||
                        first.Wire.TargetNodeId == second.Wire.SourceNodeId)
                    {
                        continue;
                    }

                    if (AutoLayoutSegmentsCross(first.Start, first.End, second.Start, second.End))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool AutoLayoutSegmentsCross(
            System.Windows.Point firstStart,
            System.Windows.Point firstEnd,
            System.Windows.Point secondStart,
            System.Windows.Point secondEnd)
        {
            var firstSideA = AutoLayoutCrossProduct(firstStart, firstEnd, secondStart);
            var firstSideB = AutoLayoutCrossProduct(firstStart, firstEnd, secondEnd);
            var secondSideA = AutoLayoutCrossProduct(secondStart, secondEnd, firstStart);
            var secondSideB = AutoLayoutCrossProduct(secondStart, secondEnd, firstEnd);
            return firstSideA * firstSideB < 0.0 && secondSideA * secondSideB < 0.0;
        }

        private static double AutoLayoutCrossProduct(
            System.Windows.Point start,
            System.Windows.Point end,
            System.Windows.Point point)
        {
            return (end.X - start.X) * (point.Y - start.Y) -
                (end.Y - start.Y) * (point.X - start.X);
        }

        private static string GetAutoLayoutSemanticState(CallSheet sheet)
        {
            var state = new StringBuilder();
            AppendAutoLayoutField(state, sheet.Id);
            AppendAutoLayoutField(state, sheet.Name);
            AppendAutoLayoutField(state, sheet.Zoom);
            AppendAutoLayoutField(state, sheet.Nodes.Count);
            foreach (var node in sheet.Nodes)
            {
                AppendAutoLayoutField(state, node.GetType().FullName);
                AppendAutoLayoutField(state, node.Id);
                AppendAutoLayoutField(state, node.Title);
                var block = node as BlockCallNode;
                if (block != null)
                {
                    AppendAutoLayoutField(state, block.BlockDefinitionId);
                    AppendAutoLayoutField(state, block.InstanceName);
                    AppendAutoLayoutField(state, block.UseMultiInstance);
                }

                var tag = node as TagNode;
                if (tag != null)
                {
                    AppendAutoLayoutField(state, tag.TagDefinitionId);
                    AppendAutoLayoutField(state, tag.TagName);
                    AppendAutoLayoutField(state, tag.DataType);
                    AppendAutoLayoutField(state, (int)tag.TerminalDirection);
                }

                var constant = node as ConstantNode;
                if (constant != null)
                {
                    AppendAutoLayoutField(state, constant.Literal);
                    AppendAutoLayoutField(state, constant.DataType);
                }

                var logic = node as LogicNode;
                if (logic != null)
                {
                    AppendAutoLayoutField(state, (int)logic.Operation);
                }

                var note = node as NoteNode;
                if (note != null)
                {
                    AppendAutoLayoutField(state, note.Text);
                }

                AppendAutoLayoutField(state, node.Pins.Count);
                foreach (var pin in node.Pins)
                {
                    AppendAutoLayoutField(state, pin.Id);
                    AppendAutoLayoutField(state, pin.NodeId);
                    AppendAutoLayoutField(state, pin.MemberId);
                    AppendAutoLayoutField(state, pin.Name);
                    AppendAutoLayoutField(state, (int)pin.Direction);
                    AppendAutoLayoutField(state, (int)pin.Role);
                    AppendAutoLayoutField(state, pin.DataType);
                    AppendAutoLayoutField(state, pin.IsRequired);
                    AppendAutoLayoutField(state, pin.Order);
                }
            }

            AppendAutoLayoutField(state, sheet.Wires.Count);
            foreach (var wire in sheet.Wires)
            {
                AppendAutoLayoutField(state, wire.Id);
                AppendAutoLayoutField(state, wire.SourceNodeId);
                AppendAutoLayoutField(state, wire.SourcePinId);
                AppendAutoLayoutField(state, wire.TargetNodeId);
                AppendAutoLayoutField(state, wire.TargetPinId);
            }

            AppendAutoLayoutField(state, sheet.Groups.Count);
            foreach (var group in sheet.Groups)
            {
                AppendAutoLayoutField(state, GetAutoLayoutGroupSemanticState(group));
            }

            return state.ToString();
        }

        private static string GetAutoLayoutGroupSemanticState(DiagramGroup group)
        {
            return group.Id.ToString("D") + "|" +
                group.ParentGroupId.ToString("D") + "|" +
                (group.Title ?? string.Empty) + "|" +
                (group.Comment ?? string.Empty) + "|" +
                string.Join(",", group.MemberNodeIds.Select(id => id.ToString("D")));
        }

        private static string GetAutoLayoutFullState(CallSheet sheet)
        {
            var state = new StringBuilder(GetAutoLayoutSemanticState(sheet));
            AppendAutoLayoutField(state, sheet.Width);
            AppendAutoLayoutField(state, sheet.Height);
            foreach (var node in sheet.Nodes)
            {
                AppendAutoLayoutField(state, node.Id);
                AppendAutoLayoutField(state, node.X);
                AppendAutoLayoutField(state, node.Y);
                AppendAutoLayoutField(state, node.ManualOrder.HasValue
                    ? node.ManualOrder.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "<null>");
            }

            foreach (var group in sheet.Groups)
            {
                AppendAutoLayoutField(state, group.Id);
                AppendAutoLayoutField(state, group.X);
                AppendAutoLayoutField(state, group.Y);
                AppendAutoLayoutField(state, group.Width);
                AppendAutoLayoutField(state, group.Height);
            }

            return state.ToString();
        }

        private static string GetAutoLayoutPositionState(CallSheet sheet)
        {
            var state = new StringBuilder();
            AppendAutoLayoutField(state, sheet.Width);
            AppendAutoLayoutField(state, sheet.Height);
            foreach (var node in sheet.Nodes.OrderBy(node => node.Id))
            {
                AppendAutoLayoutField(state, node.Id);
                AppendAutoLayoutField(state, node.X);
                AppendAutoLayoutField(state, node.Y);
                AppendAutoLayoutField(state, node.ManualOrder.HasValue
                    ? node.ManualOrder.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "<null>");
            }

            foreach (var group in sheet.Groups.OrderBy(group => group.Id))
            {
                AppendAutoLayoutField(state, group.Id);
                AppendAutoLayoutField(state, group.X);
                AppendAutoLayoutField(state, group.Y);
                AppendAutoLayoutField(state, group.Width);
                AppendAutoLayoutField(state, group.Height);
            }

            return state.ToString();
        }

        private static void AppendAutoLayoutField(StringBuilder builder, object value)
        {
            string text;
            var formattable = value as IFormattable;
            if (formattable != null)
            {
                text = formattable.ToString(
                    null,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                text = value == null ? "<null>" : value.ToString();
            }

            builder.Append(text.Length).Append(':').Append(text).Append(';');
        }

        private static DiagramProject CreateValveDemo()
        {
            var block = new BlockDefinition
            {
                Name = "FB_Valve",
                Kind = BlockKind.FunctionBlock,
                Description = "Клапан подачи",
                SclBody = "// User implementation"
            };
            block.Interface.Add(new InterfaceMember("Open", "Bool", InterfaceSection.Input));
            block.Interface.Add(new InterfaceMember("FbOpened", "Bool", InterfaceSection.Input));
            block.Interface.Add(new InterfaceMember("TravelTime", "Time", InterfaceSection.Input)
            {
                InitialValue = "T#3s"
            });
            block.Interface.Add(new InterfaceMember("Q", "Bool", InterfaceSection.Output));
            block.Interface.Add(new InterfaceMember("Fault", "Bool", InterfaceSection.Output));

            var project = new PlantProject { Name = "ValveDemo" };
            project.Blocks.Add(block);

            var open = AddTag(project, "V01_Open_Cmd", "Bool");
            var feedback = AddTag(project, "V01_FB_Opened", "Bool");
            var solenoid = AddTag(project, "V01_Sol", "Bool");
            var fault = AddTag(project, "V01_Fault", "Bool");

            var sheet = new CallSheet { Name = "FC_CallUnits" };
            var openNode = DiagramNodeFactory.CreateTag(open, TerminalDirection.Source, 60, 130);
            var feedbackNode = DiagramNodeFactory.CreateTag(feedback, TerminalDirection.Source, 60, 220);
            var timeNode = DiagramNodeFactory.CreateConstant("T#3s", "Time", 80, 330);
            var valveNode = DiagramNodeFactory.CreateBlockCall(block, "V01", 390, 170);
            var solenoidNode = DiagramNodeFactory.CreateTag(solenoid, TerminalDirection.Sink, 760, 170);
            var faultNode = DiagramNodeFactory.CreateTag(fault, TerminalDirection.Sink, 760, 270);

            sheet.Nodes.Add(openNode);
            sheet.Nodes.Add(feedbackNode);
            sheet.Nodes.Add(timeNode);
            sheet.Nodes.Add(valveNode);
            sheet.Nodes.Add(solenoidNode);
            sheet.Nodes.Add(faultNode);

            Connect(sheet, openNode, "V01_Open_Cmd", valveNode, "Open");
            Connect(sheet, feedbackNode, "V01_FB_Opened", valveNode, "FbOpened");
            Connect(sheet, timeNode, "Value", valveNode, "TravelTime");
            Connect(sheet, valveNode, "Q", solenoidNode, "V01_Sol");
            Connect(sheet, valveNode, "Fault", faultNode, "V01_Fault");

            return new DiagramProject
            {
                Plant = project,
                Sheets = new List<CallSheet> { sheet }
            };
        }

        private static TagDefinition AddTag(PlantProject project, string name, string dataType)
        {
            var isBit = string.Equals(dataType, "Bool", StringComparison.OrdinalIgnoreCase);
            var tag = new TagDefinition
            {
                Name = name,
                DataType = dataType,
                Address = isBit
                    ? "%M" + project.Tags.Count + ".0"
                    : "%MD" + (project.Tags.Count * 4)
            };
            project.Tags.Add(tag);
            return tag;
        }

        private static void Connect(
            CallSheet sheet,
            CallNode source,
            string sourcePin,
            CallNode target,
            string targetPin)
        {
            sheet.Wires.Add(new Wire
            {
                SourceNodeId = source.Id,
                SourcePinId = source.Pins.Single(pin => pin.Name == sourcePin).Id,
                TargetNodeId = target.Id,
                TargetPinId = target.Pins.Single(pin => pin.Name == targetPin).Id
            });
        }

        private static void VerifyTypeMismatch(DiagramProject original)
        {
            var storage = new DiagramProjectStorage();
            var temporaryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tiasclproj");
            try
            {
                storage.Save(temporaryPath, original);
                var copy = storage.Load(temporaryPath);
                var constant = copy.Sheets[0].Nodes.OfType<ConstantNode>().Single();
                constant.Pins[0].DataType = "DInt";
                var validation = new DiagramValidator().Validate(copy.Sheets[0]);
                Assert(validation.Issues.Any(issue => issue.Code == "DGM013"), "Type mismatch was not detected.");
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void VerifyTopologicalOrder(BlockDefinition block)
        {
            var sheet = new CallSheet { Name = "OrderTest" };
            var first = DiagramNodeFactory.CreateBlockCall(block, "First", 500, 100);
            var second = DiagramNodeFactory.CreateBlockCall(block, "Second", 100, 100);
            sheet.Nodes.Add(first);
            sheet.Nodes.Add(second);
            Connect(sheet, first, "Q", second, "Open");

            var result = TopologicalSorter.Sort(sheet);
            Assert(!result.HasCycle, "Acyclic graph was reported as cyclic.");
            Assert(result.OrderedNodeIds.SequenceEqual(new[] { first.Id, second.Id }), "Dependency order was not respected.");
        }

        private static void VerifyCycleDetection(BlockDefinition block)
        {
            var sheet = new CallSheet { Name = "CycleTest" };
            var first = DiagramNodeFactory.CreateBlockCall(block, "First", 100, 100);
            var second = DiagramNodeFactory.CreateBlockCall(block, "Second", 500, 100);
            sheet.Nodes.Add(first);
            sheet.Nodes.Add(second);
            Connect(sheet, first, "Q", second, "Open");
            Connect(sheet, second, "Q", first, "Open");

            var validation = new DiagramValidator().Validate(sheet);
            Assert(validation.Issues.Any(issue => issue.Code == "DGM030"), "Graph cycle was not detected.");
        }

        private static void VerifyCoreGenerator(BlockDefinition block)
        {
            var project = new PlantProject { Name = "CoreGenerationTest" };
            project.Blocks.Add(block);

            var unit = new UnitDefinition("V01", block);
            AddBinding(unit, block, "Open", "\"V01_Open_Cmd\"");
            AddBinding(unit, block, "FbOpened", "\"V01_FB_Opened\"");
            AddBinding(unit, block, "TravelTime", "T#3s");
            AddBinding(unit, block, "Q", "\"V01_Sol\"");
            AddBinding(unit, block, "Fault", "\"V01_Fault\"");

            var callBlock = new CallBlockDefinition
            {
                Name = "FC_TableCallUnits",
                Kind = BlockKind.Function
            };
            callBlock.Units.Add(unit);
            project.CallBlocks.Add(callBlock);

            var sources = new SclGenerator().GenerateProject(project);
            var names = sources.Select(source => source.FileName).ToArray();
            Assert(
                names.SequenceEqual(new[] { "FB_Valve.scl", "90_Instances.scl", "99_FC_TableCallUnits.scl" }),
                "Core generator emitted sources in an unexpected order: " + string.Join(", ", names));
            Assert(
                sources.Last().Content.Contains("Q => \"V01_Sol\""),
                "Core call generator did not use the output binding operator.");
        }

        private static void VerifyDefaultInput(DiagramProject original)
        {
            var copy = CloneProject(original);
            var sheet = copy.Sheets[0];
            var valve = sheet.Nodes.OfType<BlockCallNode>().Single();
            var travelTimePin = valve.Pins.Single(pin => pin.Name == "TravelTime");
            sheet.Wires.RemoveAll(wire => wire.TargetPinId == travelTimePin.Id);

            var result = new DiagramSclGenerator().Generate(copy.Plant, sheet);
            Assert(result.IsSuccess, "Default input generation failed: " + FormatIssues(result.Issues));
            Assert(
                result.Scl.Contains("TravelTime := T#3s"),
                "An unconnected Input did not use InterfaceMember.InitialValue.");
        }

        private static void VerifyStalePinContracts(DiagramProject original)
        {
            var changedToOutput = CloneProject(original);
            changedToOutput.Plant.Blocks[0].Interface.Single(member => member.Name == "Open").Section =
                InterfaceSection.Output;
            var outputResult = new DiagramSclGenerator().Generate(
                changedToOutput.Plant,
                changedToOutput.Sheets[0]);
            AssertIssue(outputResult, "GEN004", "Input-to-Output interface change was not detected.");

            var changedToInOut = CloneProject(original);
            changedToInOut.Plant.Blocks[0].Interface.Single(member => member.Name == "Open").Section =
                InterfaceSection.InOut;
            var inOutNode = changedToInOut.Sheets[0].Nodes.OfType<BlockCallNode>().Single();
            var openPin = inOutNode.Pins.Single(pin => pin.Name == "Open");
            changedToInOut.Sheets[0].Wires.RemoveAll(wire => wire.TargetPinId == openPin.Id);
            var inOutResult = new DiagramSclGenerator().Generate(
                changedToInOut.Plant,
                changedToInOut.Sheets[0]);
            AssertIssue(inOutResult, "DGM020", "Current InOut requirement was not enforced.");
        }

        private static void VerifyFunctionReturnContract()
        {
            var function = new BlockDefinition
            {
                Name = "FC_Value",
                Kind = BlockKind.Function,
                ReturnType = "Real",
                SclBody = "FC_Value := 1.0;"
            };
            var resultTag = new TagDefinition
            {
                Name = "Result",
                DataType = "Real",
                Address = "%MD100"
            };
            var project = new PlantProject { Name = "FunctionReturnTest" };
            project.Blocks.Add(function);
            project.Tags.Add(resultTag);
            var sheet = new CallSheet { Name = "FC_ReturnSheet" };
            var call = DiagramNodeFactory.CreateBlockCall(function, "Value1", 100, 100);
            var sink = DiagramNodeFactory.CreateTag(resultTag, TerminalDirection.Sink, 500, 100);
            sheet.Nodes.Add(call);
            sheet.Nodes.Add(sink);
            Connect(sheet, call, "Return", sink, "Result");

            var valid = new DiagramSclGenerator().Generate(project, sheet);
            Assert(valid.IsSuccess, "Valid FC return failed: " + FormatIssues(valid.Issues));
            Assert(
                valid.Scl.Contains("\"Result\" := \"FC_Value\"();"),
                "FC return assignment was not generated.");

            function.ReturnType = "Void";
            var stale = new DiagramSclGenerator().Generate(project, sheet);
            AssertIssue(stale, "GEN007", "Stale FC return pin was not rejected.");
        }

        private static void VerifyLocalSymbolAllocation(BlockDefinition block)
        {
            var project = new PlantProject { Name = "LocalSymbolTest" };
            project.Blocks.Add(block);
            var sheet = new CallSheet { Name = "FB_LocalSymbols" };
            var source = DiagramNodeFactory.CreateBlockCall(block, "A", 100, 100);
            var consumer = DiagramNodeFactory.CreateBlockCall(block, "B", 450, 100);
            var secondConsumer = DiagramNodeFactory.CreateBlockCall(block, "C", 750, 100);
            var multiInstance = DiagramNodeFactory.CreateBlockCall(block, "tmp_A_Q", 450, 400);
            multiInstance.UseMultiInstance = true;
            sheet.Nodes.Add(source);
            sheet.Nodes.Add(consumer);
            sheet.Nodes.Add(secondConsumer);
            sheet.Nodes.Add(multiInstance);
            Connect(sheet, source, "Q", consumer, "Open");
            Connect(sheet, source, "Q", secondConsumer, "Open");

            var result = new DiagramSclGenerator().Generate(project, sheet);
            Assert(result.IsSuccess, "Local symbol allocation failed: " + FormatIssues(result.Issues));
            Assert(
                result.Scl.Contains("tmp_A_Q : \"FB_Valve\";") &&
                result.Scl.Contains("tmp_A_Q_2 : Bool;"),
                "Temporary value collided with a multi-instance name.");
            Assert(
                CountOccurrences(result.Scl, "Q => #tmp_A_Q_2") == 1 &&
                CountOccurrences(result.Scl, "Open := #tmp_A_Q_2") == 2,
                "Fan-out did not reuse exactly one temporary value.");
            Assert(
                result.GeneratedSources.Select(sourceFile => sourceFile.FileName).Last() ==
                    "99z_FB_LocalSymbols_DB.scl",
                "Multi-instance call block DB was not emitted.");

            consumer.InstanceName = "A-B";
            var invalidName = new DiagramSclGenerator().Generate(project, sheet);
            AssertIssue(invalidName, "GEN013", "Unsafe instance name was silently sanitized.");
        }

        private static void VerifyLogicNodeFactoryContract()
        {
            var operations = new[]
            {
                LogicOperation.And,
                LogicOperation.Or,
                LogicOperation.Not,
                LogicOperation.GreaterThan,
                LogicOperation.LessThan,
                LogicOperation.Equal
            };

            foreach (var operation in operations)
            {
                var isBoolean = operation == LogicOperation.And ||
                    operation == LogicOperation.Or ||
                    operation == LogicOperation.Not;
                var node = DiagramNodeFactory.CreateLogic(
                    operation,
                    isBoolean ? "  B o o l  " : "  DInt  ",
                    12.5,
                    24.5);
                var expectedNames = operation == LogicOperation.Not
                    ? new[] { "In", "Result" }
                    : isBoolean
                        ? new[] { "In1", "In2", "Result" }
                        : new[] { "Left", "Right", "Result" };

                Assert(node.Id != Guid.Empty, operation + " factory returned an empty node ID.");
                Assert(node.Operation == operation, operation + " factory changed the operation.");
                Assert(node.X == 12.5 && node.Y == 24.5, operation + " factory changed coordinates.");
                Assert(
                    node.Pins.OrderBy(pin => pin.Order).Select(pin => pin.Name).SequenceEqual(expectedNames),
                    operation + " factory returned an unexpected pin shape.");
                Assert(
                    node.Pins.Select(pin => pin.Id).All(id => id != Guid.Empty) &&
                    node.Pins.Select(pin => pin.Id).Distinct().Count() == node.Pins.Count,
                    operation + " factory did not allocate stable unique pin IDs.");

                for (var index = 0; index < node.Pins.Count; index++)
                {
                    var pin = node.Pins[index];
                    var isOutput = index == node.Pins.Count - 1;
                    Assert(pin.NodeId == node.Id, operation + " pin has the wrong owner ID.");
                    Assert(pin.MemberId == Guid.Empty, operation + " pin unexpectedly references an interface member.");
                    Assert(pin.Role == PinRole.Logic, operation + " pin has the wrong role.");
                    Assert(pin.Order == index, operation + " pin order is not deterministic.");
                    Assert(
                        pin.Direction == (isOutput ? PinDirection.Output : PinDirection.Input),
                        operation + " pin has the wrong direction.");
                    Assert(pin.IsRequired == !isOutput, operation + " pin has the wrong required flag.");
                    AssertEqual(
                        isOutput || isBoolean ? "Bool" : "DInt",
                        pin.DataType,
                        operation + " pin data type");
                }
            }

            var booleanType = AssertThrows<ArgumentException>(
                () => DiagramNodeFactory.CreateLogic(LogicOperation.And, "DInt", 0, 0),
                "AND with a non-Bool operand type");
            AssertArgumentParameter(booleanType, "operandDataType", "AND operand type guard");

            var emptyComparison = AssertThrows<ArgumentException>(
                () => DiagramNodeFactory.CreateLogic(LogicOperation.Equal, "  ", 0, 0),
                "comparison with an empty operand type");
            AssertArgumentParameter(emptyComparison, "operandDataType", "Empty comparison operand guard");

            AssertThrows<ArgumentException>(
                () => DiagramNodeFactory.CreateLogic(LogicOperation.LessThan, "Void", 0, 0),
                "comparison with the Void operand type");
            AssertThrows<ArgumentOutOfRangeException>(
                () => DiagramNodeFactory.CreateLogic((LogicOperation)999, "Bool", 0, 0),
                "unknown logic operation");
        }

        private static void VerifyLogicOperationGeneration()
        {
            var operations = new[]
            {
                LogicOperation.And,
                LogicOperation.Or,
                LogicOperation.Not,
                LogicOperation.GreaterThan,
                LogicOperation.LessThan,
                LogicOperation.Equal
            };

            foreach (var operation in operations)
            {
                var isBoolean = operation == LogicOperation.And ||
                    operation == LogicOperation.Or ||
                    operation == LogicOperation.Not;
                var operandType = isBoolean ? "Bool" : "DInt";
                var plant = new PlantProject { Name = "LogicOperationTest" };
                var firstTag = AddTag(plant, "Logic_A", operandType);
                var secondTag = AddTag(plant, "Logic_B", operandType);
                var resultTag = AddTag(plant, "Logic_Result", "Bool");
                var sheet = new CallSheet { Name = "FC_Logic_" + operation };
                CallNode first = DiagramNodeFactory.CreateTag(firstTag, TerminalDirection.Source, 10, 10);
                CallNode second = DiagramNodeFactory.CreateTag(secondTag, TerminalDirection.Source, 10, 100);
                var logic = DiagramNodeFactory.CreateLogic(operation, operandType, 250, 50);
                var sink = DiagramNodeFactory.CreateTag(resultTag, TerminalDirection.Sink, 500, 50);

                if (operation == LogicOperation.Or)
                {
                    first = DiagramNodeFactory.CreateConstant("TRUE", "Bool", 10, 10);
                    second = DiagramNodeFactory.CreateConstant("FALSE", "Bool", 10, 100);
                }
                else if (operation == LogicOperation.GreaterThan)
                {
                    second = DiagramNodeFactory.CreateConstant("5", "DInt", 10, 100);
                }
                else if (operation == LogicOperation.LessThan)
                {
                    first = DiagramNodeFactory.CreateConstant("5", "DInt", 10, 10);
                }

                // Generation must resolve the canonical tag definition, not stale display text.
                var firstTerminal = first as TagNode;
                if (firstTerminal != null)
                {
                    firstTerminal.TagName = "Stale_Display_Name";
                }

                sheet.Nodes.Add(first);
                if (operation != LogicOperation.Not)
                {
                    sheet.Nodes.Add(second);
                }

                sheet.Nodes.Add(logic);
                sheet.Nodes.Add(sink);
                Connect(sheet, first, first.Pins[0].Name, logic, logic.Pins[0].Name);
                if (operation != LogicOperation.Not)
                {
                    Connect(sheet, second, second.Pins[0].Name, logic, logic.Pins[1].Name);
                }

                Connect(sheet, logic, "Result", sink, "Logic_Result");

                var compilation = new DiagramSclGenerator().Generate(plant, sheet);
                Assert(compilation.IsSuccess, operation + " generation failed: " + FormatIssues(compilation.Issues));

                string expectedExpression;
                switch (operation)
                {
                    case LogicOperation.And:
                        expectedExpression = "(\"Logic_A\" AND \"Logic_B\")";
                        break;
                    case LogicOperation.Or:
                        expectedExpression = "(TRUE OR FALSE)";
                        break;
                    case LogicOperation.Not:
                        expectedExpression = "(NOT \"Logic_A\")";
                        break;
                    case LogicOperation.GreaterThan:
                        expectedExpression = "(\"Logic_A\" > 5)";
                        break;
                    case LogicOperation.LessThan:
                        expectedExpression = "(5 < \"Logic_B\")";
                        break;
                    default:
                        expectedExpression = "(\"Logic_A\" = \"Logic_B\")";
                        break;
                }

                Assert(
                    compilation.Scl.Contains("\"Logic_Result\" := " + expectedExpression + ";"),
                    operation + " did not emit the expected parenthesized tag-sink assignment.\n" + compilation.Scl);
                Assert(
                    !compilation.Scl.Contains("Stale_Display_Name"),
                    operation + " generated a stale TagNode display name.");
                Assert(compilation.ExecutionOrder.Count == 0, operation + " changed block-only ExecutionOrder compatibility.");
            }
        }

        private static void VerifyNestedLogicAndFanOut()
        {
            var plant = new PlantProject { Name = "NestedLogicTest" };
            var a = AddTag(plant, "Nested_A", "Bool");
            var b = AddTag(plant, "Nested_B", "Bool");
            var c = AddTag(plant, "Nested_C", "Bool");
            var firstResult = AddTag(plant, "Nested_Result_1", "Bool");
            var secondResult = AddTag(plant, "Nested_Result_2", "Bool");
            var consumerDefinition = new BlockDefinition
            {
                Name = "FC_ConsumeLogic",
                Kind = BlockKind.Function,
                ReturnType = "Void",
                SclBody = "// consume"
            };
            consumerDefinition.Interface.Add(new InterfaceMember("Gate", "Bool", InterfaceSection.Input)
            {
                InitialValue = "TRUE"
            });
            plant.Blocks.Add(consumerDefinition);

            var sheet = new CallSheet { Name = "FC_NestedLogic" };
            var sourceA = DiagramNodeFactory.CreateTag(a, TerminalDirection.Source, 10, 10);
            var sourceB = DiagramNodeFactory.CreateTag(b, TerminalDirection.Source, 10, 80);
            var sourceC = DiagramNodeFactory.CreateTag(c, TerminalDirection.Source, 10, 150);
            var and = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 220, 40);
            var not = DiagramNodeFactory.CreateLogic(LogicOperation.Not, "Bool", 220, 160);
            var or = DiagramNodeFactory.CreateLogic(LogicOperation.Or, "Bool", 430, 90);
            var sink1 = DiagramNodeFactory.CreateTag(firstResult, TerminalDirection.Sink, 700, 30);
            var sink2 = DiagramNodeFactory.CreateTag(secondResult, TerminalDirection.Sink, 700, 100);
            var consumer = DiagramNodeFactory.CreateBlockCall(consumerDefinition, "Consume", 700, 190);
            sheet.Nodes.AddRange(new CallNode[]
            {
                sourceA, sourceB, sourceC, and, not, or, sink1, sink2, consumer
            });
            Connect(sheet, sourceA, "Nested_A", and, "In1");
            Connect(sheet, sourceB, "Nested_B", and, "In2");
            Connect(sheet, sourceC, "Nested_C", not, "In");
            Connect(sheet, and, "Result", or, "In1");
            Connect(sheet, not, "Result", or, "In2");
            Connect(sheet, or, "Result", sink1, "Nested_Result_1");
            Connect(sheet, or, "Result", sink2, "Nested_Result_2");
            Connect(sheet, or, "Result", consumer, "Gate");

            var compilation = new DiagramSclGenerator().Generate(plant, sheet);
            Assert(compilation.IsSuccess, "Nested/fan-out logic failed: " + FormatIssues(compilation.Issues));
            const string temporary = "#tmp_logic_Or_Result";
            const string expression = "((\"Nested_A\" AND \"Nested_B\") OR (NOT \"Nested_C\"))";
            Assert(
                compilation.Scl.Contains("tmp_logic_Or_Result : Bool;") &&
                compilation.Scl.Contains(temporary + " := " + expression + ";"),
                "Nested logic was not folded once into a deterministic fan-out temporary.\n" + compilation.Scl);
            Assert(
                CountOccurrences(compilation.Scl, expression) == 1 &&
                compilation.Scl.Contains("\"Nested_Result_1\" := " + temporary + ";") &&
                compilation.Scl.Contains("\"Nested_Result_2\" := " + temporary + ";") &&
                compilation.Scl.Contains("Gate := " + temporary),
                "Logic fan-out did not reuse one result for tag and block sinks.");
            Assert(
                !compilation.Scl.Contains("Gate := TRUE"),
                "A connected logic expression did not override the block input default.");
            Assert(
                compilation.ExecutionOrder.SequenceEqual(new[] { consumer.Id }),
                "Logic nodes leaked into the block-only public execution order.");

            var block = new BlockDefinition
            {
                Name = "FB_LogicStage",
                Kind = BlockKind.FunctionBlock,
                SclBody = "Out := In;"
            };
            block.Interface.Add(new InterfaceMember("In", "Bool", InterfaceSection.Input));
            block.Interface.Add(new InterfaceMember("Out", "Bool", InterfaceSection.Output));
            var stagedPlant = new PlantProject { Name = "BlockLogicOrder" };
            stagedPlant.Blocks.Add(block);
            var stagedSheet = new CallSheet { Name = "FC_BlockLogicOrder" };
            var sourceBlock = DiagramNodeFactory.CreateBlockCall(block, "StageA", 100, 100);
            var inversion = DiagramNodeFactory.CreateLogic(LogicOperation.Not, "Bool", 350, 100);
            var targetBlock = DiagramNodeFactory.CreateBlockCall(block, "StageB", 600, 100);
            stagedSheet.Nodes.Add(sourceBlock);
            stagedSheet.Nodes.Add(inversion);
            stagedSheet.Nodes.Add(targetBlock);
            Connect(stagedSheet, sourceBlock, "Out", inversion, "In");
            Connect(stagedSheet, inversion, "Result", targetBlock, "In");

            var staged = new DiagramSclGenerator().Generate(stagedPlant, stagedSheet);
            Assert(staged.IsSuccess, "Block→logic→block generation failed: " + FormatIssues(staged.Issues));
            Assert(
                staged.ExecutionOrder.SequenceEqual(new[] { sourceBlock.Id, targetBlock.Id }),
                "Block→logic→block dependencies did not preserve block execution order.");
            Assert(
                staged.Scl.IndexOf("\"StageA\"(", StringComparison.Ordinal) <
                    staged.Scl.IndexOf("\"StageB\"(", StringComparison.Ordinal) &&
                staged.Scl.Contains("In := (NOT #tmp_StageA_Out)"),
                "Block output was not available to the downstream folded logic expression.\n" + staged.Scl);
        }

        private static void VerifyLogicValidationFailures()
        {
            var unknown = CreateBooleanLogicProject(LogicOperation.And);
            unknown.Sheets[0].Nodes.OfType<LogicNode>().Single().Operation = (LogicOperation)999;
            AssertIssue(
                new DiagramSclGenerator().Generate(unknown.Plant, unknown.Sheets[0]),
                "GEN023",
                "Unknown logic operation was not rejected.");

            var stale = CreateBooleanLogicProject(LogicOperation.And);
            stale.Sheets[0].Nodes.OfType<LogicNode>().Single().Pins.Single(pin => pin.Name == "Result").Role =
                PinRole.Terminal;
            AssertIssue(
                new DiagramSclGenerator().Generate(stale.Plant, stale.Sheets[0]),
                "GEN024",
                "Stale logic pin role was not rejected.");

            var emptyLegacy = new DiagramProject
            {
                Plant = new PlantProject { Name = "LegacyLogic" },
                Sheets = new List<CallSheet>
                {
                    new CallSheet { Name = "FC_LegacyLogic" }
                }
            };
            emptyLegacy.Sheets[0].Nodes.Add(new LogicNode { Operation = LogicOperation.And });
            AssertIssue(
                new DiagramSclGenerator().Generate(emptyLegacy.Plant, emptyLegacy.Sheets[0]),
                "GEN024",
                "Legacy logic node without pins did not fail safely.");

            var missing = CreateBooleanLogicProject(LogicOperation.And);
            var missingLogic = missing.Sheets[0].Nodes.OfType<LogicNode>().Single();
            var missingPin = missingLogic.Pins.Single(pin => pin.Name == "In2");
            missing.Sheets[0].Wires.RemoveAll(wire => wire.TargetPinId == missingPin.Id);
            AssertIssue(
                new DiagramSclGenerator().Generate(missing.Plant, missing.Sheets[0]),
                "DGM020",
                "Missing required logic input was not rejected.");

            var mismatch = CreateBooleanLogicProject(LogicOperation.And);
            var mismatchSource = mismatch.Sheets[0].Nodes.OfType<TagNode>()
                .First(node => node.TerminalDirection == TerminalDirection.Source);
            mismatchSource.Pins[0].DataType = "DInt";
            AssertIssue(
                new DiagramSclGenerator().Generate(mismatch.Plant, mismatch.Sheets[0]),
                "DGM013",
                "Logic input type mismatch was not rejected.");

            var duplicateInput = CreateBooleanLogicProject(LogicOperation.And);
            var duplicateSheet = duplicateInput.Sheets[0];
            var duplicateLogic = duplicateSheet.Nodes.OfType<LogicNode>().Single();
            var extra = DiagramNodeFactory.CreateConstant("FALSE", "Bool", 20, 240);
            duplicateSheet.Nodes.Add(extra);
            Connect(duplicateSheet, extra, "Value", duplicateLogic, "In1");
            AssertIssue(
                new DiagramSclGenerator().Generate(duplicateInput.Plant, duplicateSheet),
                "DGM014",
                "A second wire to one logic input was not rejected.");

            var unresolved = CreateBooleanLogicProject(LogicOperation.And);
            var unresolvedSheet = unresolved.Sheets[0];
            var unresolvedLogic = unresolvedSheet.Nodes.OfType<LogicNode>().Single();
            var unresolvedPin = unresolvedLogic.Pins.Single(pin => pin.Name == "In1");
            unresolvedSheet.Wires.RemoveAll(wire => wire.TargetPinId == unresolvedPin.Id);
            var note = new NoteNode { Title = "Unsupported source", Text = "No expression" };
            note.Pins.Add(new Pin
            {
                NodeId = note.Id,
                Name = "Value",
                Direction = PinDirection.Output,
                Role = PinRole.Logic,
                DataType = "Bool",
                Order = 0
            });
            unresolvedSheet.Nodes.Add(note);
            Connect(unresolvedSheet, note, "Value", unresolvedLogic, "In1");
            AssertIssue(
                new DiagramSclGenerator().Generate(unresolved.Plant, unresolvedSheet),
                "GEN025",
                "Unsupported logic source expression was not rejected.");
        }

        private static void VerifyLogicCyclesAndPersistence(BlockDefinition block)
        {
            var selfCycle = new CallSheet { Name = "FC_LogicSelfCycle" };
            var seed = DiagramNodeFactory.CreateConstant("TRUE", "Bool", 10, 10);
            var self = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 200, 10);
            selfCycle.Nodes.Add(seed);
            selfCycle.Nodes.Add(self);
            Connect(selfCycle, self, "Result", self, "In1");
            Connect(selfCycle, seed, "Value", self, "In2");
            var selfValidation = new DiagramValidator().Validate(selfCycle);
            var selfCycleIds = selfValidation.Issues
                .Where(issue => issue.Code == "DGM030" && issue.NodeId.HasValue)
                .Select(issue => issue.NodeId.Value)
                .ToArray();
            Assert(selfCycleIds.SequenceEqual(new[] { self.Id }), "Logic self-cycle was not isolated to its member.");

            var mixed = new CallSheet { Name = "FC_MixedLogicCycle" };
            var cyclicBlock = DiagramNodeFactory.CreateBlockCall(block, "MixedCycleA", 100, 100);
            var logic = DiagramNodeFactory.CreateLogic(LogicOperation.Not, "Bool", 400, 100);
            var downstream = DiagramNodeFactory.CreateBlockCall(block, "MixedDownstream", 700, 100);
            mixed.Nodes.Add(cyclicBlock);
            mixed.Nodes.Add(logic);
            mixed.Nodes.Add(downstream);
            Connect(mixed, cyclicBlock, "Q", logic, "In");
            Connect(mixed, logic, "Result", cyclicBlock, "Open");
            Connect(mixed, cyclicBlock, "Fault", downstream, "Open");
            var mixedValidation = new DiagramValidator().Validate(mixed);
            var mixedCycleIds = mixedValidation.Issues
                .Where(issue => issue.Code == "DGM030" && issue.NodeId.HasValue)
                .Select(issue => issue.NodeId.Value)
                .ToArray();
            Assert(mixedCycleIds.Contains(cyclicBlock.Id), "Mixed cycle omitted the block member.");
            Assert(mixedCycleIds.Contains(logic.Id), "Mixed cycle omitted the logic member.");
            Assert(!mixedCycleIds.Contains(downstream.Id), "Mixed cycle incorrectly included a downstream block.");

            var persisted = CreateBooleanLogicProject(LogicOperation.Equal);
            var beforeBytes = SerializeProjectBytes(persisted);
            var expectedIds = GetPersistentGuidState(persisted).ToArray();
            var expectedWires = GetWireState(persisted).ToArray();
            var reloaded = CloneProject(persisted);
            var reloadedLogic = reloaded.Sheets[0].Nodes.OfType<LogicNode>().Single();
            Assert(reloadedLogic.Operation == LogicOperation.Equal, "Logic operation was lost in XML round-trip.");
            Assert(
                GetPersistentGuidState(reloaded).SequenceEqual(expectedIds) &&
                GetWireState(reloaded).SequenceEqual(expectedWires),
                "Logic XML round-trip changed a stable ID or wire endpoint.");

            var history = new DiagramEditHistory();
            var before = DiagramProjectSnapshot.Clone(persisted);
            persisted.Sheets[0].Nodes.OfType<LogicNode>().Single().X += 47.0;
            var after = DiagramProjectSnapshot.Clone(persisted);
            Assert(history.Record("Move logic", before, after), "Logic move was not recorded in history.");
            var restored = history.Undo();
            Assert(
                SerializeProjectBytes(restored).SequenceEqual(beforeBytes),
                "Undo did not restore the logic project byte-for-byte.");
        }

        private static DiagramProject CreateBooleanLogicProject(LogicOperation operation)
        {
            var plant = new PlantProject { Name = "LogicValidation" };
            var firstTag = AddTag(plant, "Logic_Input_1", "Bool");
            var secondTag = AddTag(plant, "Logic_Input_2", "Bool");
            var resultTag = AddTag(plant, "Logic_Output", "Bool");
            var sheet = new CallSheet { Name = "FC_LogicValidation" };
            var first = DiagramNodeFactory.CreateTag(firstTag, TerminalDirection.Source, 10, 10);
            var second = DiagramNodeFactory.CreateTag(secondTag, TerminalDirection.Source, 10, 100);
            var logic = DiagramNodeFactory.CreateLogic(operation, "Bool", 250, 50);
            var sink = DiagramNodeFactory.CreateTag(resultTag, TerminalDirection.Sink, 500, 50);
            sheet.Nodes.Add(first);
            sheet.Nodes.Add(second);
            sheet.Nodes.Add(logic);
            sheet.Nodes.Add(sink);
            Connect(sheet, first, "Logic_Input_1", logic, logic.Pins[0].Name);
            if (operation != LogicOperation.Not)
            {
                Connect(sheet, second, "Logic_Input_2", logic, logic.Pins[1].Name);
            }

            Connect(sheet, logic, "Result", sink, "Logic_Output");
            return new DiagramProject
            {
                Plant = plant,
                Sheets = new List<CallSheet> { sheet }
            };
        }

        private static void VerifyDiagramProjectCompilerInputs()
        {
            var compiler = new DiagramProjectCompiler();
            var missingProject = compiler.Compile((DiagramProject)null);
            AssertProjectIssue(missingProject, "DGP000", "Null diagram project was not rejected.");
            Assert(missingProject.Sheets.Count == 0, "Null project unexpectedly returned sheet results.");

            var emptyProject = new DiagramProject
            {
                Plant = new PlantProject { Name = "EmptyCompilerProject" },
                Sheets = new List<CallSheet>()
            };
            AssertProjectIssue(
                compiler.Compile(emptyProject),
                "DGP011",
                "Project without call sheets was not rejected.");

            var missingSheets = compiler.Compile(
                new PlantProject { Name = "MissingSheets" },
                null);
            AssertProjectIssue(missingSheets, "DGP001", "Null call-sheet collection was not rejected.");

            var missingPlant = compiler.Compile(
                null,
                new[] { new CallSheet { Name = "FC_MissingPlant" } });
            AssertProjectIssue(missingPlant, "DGP004", "Null Plant model was not rejected.");
            Assert(
                missingPlant.Sheets.Count == 1 &&
                missingPlant.Sheets[0].Issues.Any(issue => issue.Code == "DGP002"),
                "A sheet compiled against a missing Plant model did not fail safely.");

            var nullSheet = compiler.Compile(
                new PlantProject { Name = "NullSheet" },
                new CallSheet[] { null });
            AssertProjectIssue(nullSheet, "DGM000", "Null call sheet was not rejected.");

            var malformedPlant = new PlantProject { Name = "MalformedPlant" };
            malformedPlant.Blocks = null;
            AssertProjectIssue(
                compiler.Compile(malformedPlant, new[] { new CallSheet { Name = "FC_MalformedPlant" } }),
                "DGP005",
                "Structurally invalid Plant model was not rejected.");

            var malformedSheet = new CallSheet { Name = "FC_MalformedSheet" };
            malformedSheet.Nodes = null;
            var malformedSheetResult = compiler.Compile(
                new PlantProject { Name = "MalformedSheet" },
                new[] { malformedSheet });
            AssertProjectIssue(malformedSheetResult, "DGP003", "Missing sheet collection was not rejected.");

            foreach (var result in new[]
            {
                missingProject,
                compiler.Compile(emptyProject),
                missingSheets,
                missingPlant,
                nullSheet,
                malformedSheetResult
            })
            {
                Assert(result.GeneratedSources.Count == 0, "Failed project compilation leaked generated sources.");
            }
        }

        private static void VerifyDiagramProjectCompilerBundle()
        {
            var project = CreateProjectCompilerFixture();
            var before = SerializeProjectBytes(project);
            var result = new DiagramProjectCompiler().Compile(project);
            Assert(result.IsSuccess, "Two-sheet project compilation failed: " + FormatProjectIssues(result.Issues));
            Assert(result.Sheets.Count == 2, "Project compiler did not return both sheet results.");
            Assert(
                result.Sheets[0].SheetIndex == 0 &&
                result.Sheets[0].SheetId == project.Sheets[0].Id &&
                result.Sheets[0].SheetName == project.Sheets[0].Name &&
                result.Sheets[1].SheetIndex == 1 &&
                result.Sheets[1].SheetId == project.Sheets[1].Id,
                "Project compiler lost sheet identity or input order.");
            Assert(result.Sheets.All(sheet => sheet.IsSuccess), "A successful bundle contains a failed sheet result.");

            var names = result.GeneratedSources.Select(source => source.FileName).ToArray();
            var expectedNames = new[]
            {
                "00_Types.scl",
                "FB_ProjectCompilerUnit.scl",
                "90_Instances.scl",
                "90_Instances_001_FC_Visual_A.scl",
                "90_Instances_002_FC_Visual_B.scl",
                "99_FC_PlantCalls.scl",
                "99_FB_PlantCalls.scl",
                "99_FC_Visual_A.scl",
                "99_FC_Visual_B.scl",
                "99z_DB_PlantCalls.scl"
            };
            Assert(
                names.SequenceEqual(expectedNames),
                "Project source bundle has the wrong dependency order: " + string.Join(", ", names));
            Assert(
                names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length,
                "Project compiler returned duplicate source filenames.");
            Assert(
                result.GeneratedSources.Select(source => source.Order)
                    .SequenceEqual(Enumerable.Range(0, result.GeneratedSources.Count)),
                "Project compiler did not renumber the final source bundle contiguously.");
            Assert(
                CountOccurrences(string.Join("|", names), "FB_ProjectCompilerUnit.scl") == 1 &&
                CountOccurrences(string.Join("|", names), "00_Types.scl") == 1,
                "Plant block/type common sources were duplicated per visual sheet.");
            Assert(
                names.Contains("99_FC_PlantCalls.scl") &&
                names.Contains("99_FB_PlantCalls.scl") &&
                names.Contains("99z_DB_PlantCalls.scl"),
                "Plant call-block sources were dropped from the project bundle.");
            Assert(
                result.Sheets[0].Scl.Contains("FUNCTION \"FC_Visual_A\"") &&
                result.Sheets[1].CompilationResult.Scl.Contains("FUNCTION \"FC_Visual_B\""),
                "Per-sheet SCL was not retained in project compilation results.");
            Assert(
                ReferenceEquals(result.GeneratedSources, result.Sources) &&
                ReferenceEquals(result.Sheets, result.SheetResults),
                "Project compilation result aliases do not expose the same read-only collections.");
            Assert(
                SerializeProjectBytes(project).SequenceEqual(before),
                "Project-wide compilation mutated the input model.");
        }

        private static void VerifyDiagramProjectCompilerCollisions()
        {
            var compiler = new DiagramProjectCompiler();

            var duplicateName = CreateProjectCompilerFixture();
            duplicateName.Sheets[1].Name = duplicateName.Sheets[0].Name.ToLowerInvariant();
            AssertProjectIssue(
                compiler.Compile(duplicateName),
                "DGP007",
                "Case-insensitive duplicate sheet name was not rejected.");

            var crossSheetInstance = CreateProjectCompilerFixture();
            crossSheetInstance.Sheets[1].Nodes.OfType<BlockCallNode>().Single().InstanceName =
                crossSheetInstance.Sheets[0].Nodes.OfType<BlockCallNode>().Single().InstanceName.ToLowerInvariant();
            AssertProjectIssue(
                compiler.Compile(crossSheetInstance),
                "DGP008",
                "Cross-sheet visual instance DB collision was not rejected.");

            var plantSymbol = CreateProjectCompilerFixture();
            plantSymbol.Sheets[0].Name = plantSymbol.Plant.Blocks[0].Name.ToLowerInvariant();
            AssertProjectIssue(
                compiler.Compile(plantSymbol),
                "DGP008",
                "Visual call block colliding with a Plant global symbol was not rejected.");

            var callBlockDb = CreateProjectCompilerFixture();
            var firstCall = callBlockDb.Sheets[0].Nodes.OfType<BlockCallNode>().Single();
            firstCall.UseMultiInstance = true;
            callBlockDb.Sheets[1].Nodes.OfType<BlockCallNode>().Single().InstanceName =
                callBlockDb.Sheets[0].Name + "_DB";
            AssertProjectIssue(
                compiler.Compile(callBlockDb),
                "DGP008",
                "Visual call-block DB colliding with another sheet instance DB was not rejected.");

            var crossSheetIdentity = CreateProjectCompilerFixture();
            var firstPinId = crossSheetIdentity.Sheets[0].Nodes[0].Pins[0].Id;
            crossSheetIdentity.Sheets[1].Id = firstPinId;
            var crossSheetIdentityResult = compiler.Compile(crossSheetIdentity);
            AssertProjectIssue(
                crossSheetIdentityResult,
                "DGP006",
                "Cross-sheet/cross-kind stable ID collision was not rejected.");
            Assert(
                crossSheetIdentityResult.Issues.Count(issue => issue.Code == "DGP006") == 2,
                "Cross-sheet stable ID collision was not scoped to both affected sheets.");

            var sameSheetIdentity = CreateProjectCompilerFixture();
            sameSheetIdentity.Sheets[0].Id = sameSheetIdentity.Sheets[0].Nodes[0].Id;
            AssertProjectIssue(
                compiler.Compile(sameSheetIdentity),
                "DGP006",
                "Same-sheet SheetId↔NodeId collision was not rejected by project preflight.");

            var plantIdentityTemplate = CreateProjectCompilerFixture();
            var plantIdentityIds = GetPlantIdentityIds(plantIdentityTemplate.Plant).ToArray();
            Assert(plantIdentityIds.Length >= 10, "Project compiler identity fixture is incomplete.");
            foreach (var plantIdentityId in plantIdentityIds)
            {
                var collision = CloneProject(plantIdentityTemplate);
                var node = collision.Sheets[0].Nodes[0];
                node.Id = plantIdentityId;
                foreach (var pin in node.Pins)
                {
                    pin.NodeId = node.Id;
                }

                AssertProjectIssue(
                    compiler.Compile(collision),
                    "DGP006",
                    "Plant↔diagram stable ID collision " + plantIdentityId + " was not rejected.");
            }

            var invalidSheet = CreateProjectCompilerFixture();
            var invalidLogic = DiagramNodeFactory.CreateLogic(LogicOperation.And, "Bool", 20, 300);
            invalidSheet.Sheets[1].Nodes.Add(invalidLogic);
            var invalidResult = compiler.Compile(invalidSheet);
            AssertProjectIssue(invalidResult, "DGM020", "One invalid sheet did not fail project compilation.");
            Assert(
                invalidResult.Sheets[0].IsSuccess && !invalidResult.Sheets[1].IsSuccess,
                "Project compiler did not retain per-sheet success/failure state.");

            foreach (var failed in new[]
            {
                compiler.Compile(duplicateName),
                compiler.Compile(crossSheetInstance),
                compiler.Compile(plantSymbol),
                compiler.Compile(callBlockDb),
                crossSheetIdentityResult,
                compiler.Compile(sameSheetIdentity),
                invalidResult
            })
            {
                Assert(failed.GeneratedSources.Count == 0, "Failed project compilation was not fail-closed.");
            }
        }

        private static DiagramProject CreateProjectCompilerFixture()
        {
            var block = new BlockDefinition
            {
                Name = "FB_ProjectCompilerUnit",
                Kind = BlockKind.FunctionBlock,
                Description = "Project compiler fixture",
                SclBody = "Ready := Enable;"
            };
            var enable = new InterfaceMember("Enable", "Bool", InterfaceSection.Input)
            {
                InitialValue = "TRUE"
            };
            block.Interface.Add(enable);
            block.Interface.Add(new InterfaceMember("Ready", "Bool", InterfaceSection.Output));

            var plant = new PlantProject { Name = "ProjectCompilerFixture" };
            plant.Blocks.Add(block);
            plant.Tags.Add(new TagDefinition
            {
                Name = "Compiler_GlobalTag",
                DataType = "Bool",
                Address = "%M100.0"
            });
            var dataType = new UdtDefinition { Name = "UDT_ProjectCompiler" };
            dataType.Members.Add(new UdtMember("Enabled", "Bool"));
            plant.DataTypes.Add(dataType);

            var functionCalls = new CallBlockDefinition
            {
                Name = "FC_PlantCalls",
                Kind = BlockKind.Function
            };
            functionCalls.Interface.Add(new InterfaceMember("Cycle", "Bool", InterfaceSection.Input));
            var plantUnit = new UnitDefinition("PlantUnitDb", block);
            plantUnit.Bindings.Add(new ParameterBinding(enable, "TRUE"));
            functionCalls.Units.Add(plantUnit);
            plant.CallBlocks.Add(functionCalls);
            plant.CallBlocks.Add(new CallBlockDefinition
            {
                Name = "FB_PlantCalls",
                Kind = BlockKind.FunctionBlock,
                InstanceDbName = "DB_PlantCalls"
            });

            var firstSheet = new CallSheet { Name = "FC_Visual_A" };
            firstSheet.Nodes.Add(DiagramNodeFactory.CreateBlockCall(block, "Visual_A_DB", 100, 100));
            var secondSheet = new CallSheet { Name = "FC_Visual_B" };
            secondSheet.Nodes.Add(DiagramNodeFactory.CreateBlockCall(block, "Visual_B_DB", 100, 100));
            return new DiagramProject
            {
                Plant = plant,
                Sheets = new List<CallSheet> { firstSheet, secondSheet }
            };
        }

        private static IEnumerable<Guid> GetPlantIdentityIds(PlantProject plant)
        {
            yield return plant.Id;
            foreach (var block in plant.Blocks)
            {
                yield return block.Id;
                foreach (var member in block.Interface)
                {
                    yield return member.Id;
                }
            }

            foreach (var dataType in plant.DataTypes)
            {
                yield return dataType.Id;
                foreach (var member in dataType.Members)
                {
                    yield return member.Id;
                }
            }

            foreach (var tag in plant.Tags)
            {
                yield return tag.Id;
            }

            foreach (var callBlock in plant.CallBlocks)
            {
                yield return callBlock.Id;
                foreach (var member in callBlock.Interface)
                {
                    yield return member.Id;
                }

                foreach (var unit in callBlock.Units)
                {
                    yield return unit.Id;
                    foreach (var binding in unit.Bindings)
                    {
                        yield return binding.Id;
                    }
                }
            }
        }

        private static void VerifyAtomicProjectSave(DiagramProject original)
        {
            var storage = new DiagramProjectStorage();
            var directory = Path.Combine(Path.GetTempPath(), "TiaSclStudio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Atomic.tiasclproj");
            try
            {
                storage.Save(path, original);
                var originalBytes = File.ReadAllBytes(path);
                var invalid = CloneProject(original);
                invalid.Sheets[0].Nodes.Add(new NoteNode { Text = "invalid\u0001xml" });

                var failed = false;
                try
                {
                    storage.Save(path, invalid);
                }
                catch (InvalidOperationException)
                {
                    failed = true;
                }

                Assert(failed, "Invalid XML character did not fail serialization.");
                Assert(
                    originalBytes.SequenceEqual(File.ReadAllBytes(path)),
                    "Failed save damaged the previous project file.");
                Assert(
                    Directory.GetFiles(directory, "*.tmp").Length == 0,
                    "Failed save left a temporary project file behind.");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        File.Delete(file);
                    }

                    Directory.Delete(directory);
                }
            }
        }

        private static void VerifyProjectFormatVersionCompatibility()
        {
            Assert(
                new DiagramProject().FormatVersion == DiagramProject.CurrentFormatVersion &&
                DiagramProject.CurrentFormatVersion == 4 &&
                new DiagramProject().Plant.CpuFamily == PlcCpuFamily.S71200,
                "New projects are not stamped with format v4.");

            var storage = new DiagramProjectStorage();
            var directory = Path.Combine(
                Path.GetTempPath(),
                "TiaSclStudio-FormatV4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var versionTwoPath = Path.Combine(directory, "GroupedV2.tiasclproj");
            var legacyPath = Path.Combine(directory, "LegacyV1.tiasclproj");
            var versionTwoLegacyPath = Path.Combine(directory, "GroupedV2Legacy.tiasclproj");
            var upgradedPath = Path.Combine(directory, "UpgradedV4.tiasclproj");
            var futurePath = Path.Combine(directory, "FutureV5.tiasclproj");
            var failedPath = Path.Combine(directory, "FailedUpgrade.tiasclproj");
            var unsupportedSavePath = Path.Combine(directory, "UnsupportedSave.tiasclproj");

            try
            {
                var groupedProject = CreateValveDemo();
                var groupedSheet = groupedProject.Sheets[0];
                var group = new DiagramGroup("Persisted v2 group", 40.0, 50.0, 640.0, 360.0);
                group.MemberNodeIds.Add(groupedSheet.Nodes[0].Id);
                groupedSheet.Groups.Add(group);

                storage.Save(versionTwoPath, groupedProject);
                Assert(
                    groupedProject.FormatVersion == DiagramProject.CurrentFormatVersion,
                    "Successful save did not leave the project stamped as v4.");
                Assert(
                    ReadProjectFormatVersion(versionTwoPath) == DiagramProject.CurrentFormatVersion,
                    "A project with CPU/address settings was not persisted with format v4.");

                var versionTwoReloaded = storage.Load(versionTwoPath);
                var reloadedGroup = versionTwoReloaded.Sheets[0].Groups.Single(item => item.Id == group.Id);
                Assert(
                    reloadedGroup.MemberNodeIds.SequenceEqual(group.MemberNodeIds),
                    "The v4 loader did not preserve a group's direct members.");

                RewriteProjectVersion(
                    versionTwoPath,
                    versionTwoLegacyPath,
                    DiagramProject.GroupFormatVersion,
                    false);
                var legacyVersionTwoReloaded = storage.Load(versionTwoLegacyPath);
                Assert(
                    legacyVersionTwoReloaded.FormatVersion == DiagramProject.GroupFormatVersion &&
                    legacyVersionTwoReloaded.Plant.CpuFamily == PlcCpuFamily.Unknown,
                    "The v4 loader did not retain the version of a loaded v2 project.");

                RewriteProjectVersion(versionTwoPath, legacyPath, DiagramProject.LegacyFormatVersion, true);
                var legacyReloaded = storage.Load(legacyPath);
                Assert(
                    legacyReloaded.FormatVersion == DiagramProject.LegacyFormatVersion &&
                    legacyReloaded.Plant.CpuFamily == PlcCpuFamily.Unknown,
                    "The v4 loader did not retain the version of a loaded v1 project.");
                Assert(
                    legacyReloaded.Sheets.All(sheet => sheet.Groups != null && sheet.Groups.Count == 0),
                    "A legacy v1 project without Groups did not receive empty group collections.");

                var upgradedGroup = new DiagramGroup("Added after v1 load", 10.0, 20.0, 320.0, 200.0);
                upgradedGroup.MemberNodeIds.Add(legacyReloaded.Sheets[0].Nodes[0].Id);
                legacyReloaded.Sheets[0].Groups.Add(upgradedGroup);
                storage.Save(upgradedPath, legacyReloaded);
                Assert(
                    legacyReloaded.FormatVersion == DiagramProject.CurrentFormatVersion &&
                    ReadProjectFormatVersion(upgradedPath) == DiagramProject.CurrentFormatVersion,
                    "Saving a loaded v1 project did not atomically upgrade it to v4.");
                Assert(
                    storage.Load(upgradedPath).Sheets[0].Groups.Any(item => item.Id == upgradedGroup.Id),
                    "The v1-to-v4 save upgrade lost the newly added group.");

                RewriteProjectVersion(versionTwoPath, futurePath, 5, false);
                AssertThrows<NotSupportedException>(
                    () => storage.Load(futurePath),
                    "Load future project format");

                var failedUpgrade = storage.Load(legacyPath);
                failedUpgrade.Sheets[0].Nodes.Add(new NoteNode { Text = "invalid\u0001xml" });
                AssertThrows<InvalidOperationException>(
                    () => storage.Save(failedPath, failedUpgrade),
                    "Failed v1-to-v4 save upgrade");
                Assert(
                    failedUpgrade.FormatVersion == DiagramProject.LegacyFormatVersion &&
                    !File.Exists(failedPath),
                    "A failed save changed the in-memory v1 version or exposed a partial v4 file.");

                var unsupportedSave = new DiagramProject { FormatVersion = 5 };
                AssertThrows<NotSupportedException>(
                    () => storage.Save(unsupportedSavePath, unsupportedSave),
                    "Save future project format");
                Assert(
                    unsupportedSave.FormatVersion == 5 && !File.Exists(unsupportedSavePath),
                    "Rejected future-version save mutated the project or created a file.");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        File.Delete(file);
                    }

                    Directory.Delete(directory);
                }
            }
        }

        private static int ReadProjectFormatVersion(string path)
        {
            var xml = File.ReadAllText(path, Encoding.UTF8);
            const string marker = "FormatVersion=\"";
            var valueStart = xml.IndexOf(marker, StringComparison.Ordinal);
            var formatVersion = 0;
            Assert(
                valueStart >= 0,
                "Saved project XML has no FormatVersion attribute.");
            valueStart += marker.Length;
            var valueEnd = xml.IndexOf('\"', valueStart);
            Assert(
                valueEnd > valueStart &&
                int.TryParse(xml.Substring(valueStart, valueEnd - valueStart), out formatVersion),
                "Saved project XML has no numeric FormatVersion attribute.");
            return formatVersion;
        }

        private static void RewriteProjectVersion(
            string sourcePath,
            string targetPath,
            int formatVersion,
            bool removeGroups)
        {
            var xml = File.ReadAllText(sourcePath, Encoding.UTF8);
            const string marker = "FormatVersion=\"";
            var valueStart = xml.IndexOf(marker, StringComparison.Ordinal);
            Assert(valueStart >= 0, "Project XML fixture has no FormatVersion attribute.");
            valueStart += marker.Length;
            var valueEnd = xml.IndexOf('\"', valueStart);
            Assert(valueEnd > valueStart, "Project XML fixture has an invalid FormatVersion attribute.");
            xml = xml.Substring(0, valueStart) +
                formatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                xml.Substring(valueEnd);

            if (formatVersion < DiagramProject.CurrentFormatVersion)
            {
                const string cpuStartMarker = "<CpuFamily>";
                const string cpuEndMarker = "</CpuFamily>";
                var cpuStart = xml.IndexOf(cpuStartMarker, StringComparison.Ordinal);
                if (cpuStart >= 0)
                {
                    var cpuEnd = xml.IndexOf(cpuEndMarker, cpuStart, StringComparison.Ordinal);
                    Assert(cpuEnd >= 0, "Project XML fixture has an unclosed CpuFamily element.");
                    cpuEnd += cpuEndMarker.Length;
                    xml = xml.Remove(cpuStart, cpuEnd - cpuStart);
                }
            }

            if (removeGroups)
            {
                const string groupsStartMarker = "<Groups";
                const string groupsEndMarker = "</Groups>";
                var groupsStart = xml.IndexOf(groupsStartMarker, StringComparison.Ordinal);
                while (groupsStart >= 0)
                {
                    var openingEnd = xml.IndexOf('>', groupsStart);
                    Assert(openingEnd >= 0, "Project XML fixture has an unterminated Groups element.");
                    int groupsEnd;
                    if (xml[openingEnd - 1] == '/')
                    {
                        groupsEnd = openingEnd + 1;
                    }
                    else
                    {
                        var closingStart = xml.IndexOf(
                            groupsEndMarker,
                            openingEnd + 1,
                            StringComparison.Ordinal);
                        Assert(closingStart >= 0, "Project XML fixture has an unclosed Groups element.");
                        groupsEnd = closingStart + groupsEndMarker.Length;
                    }

                    xml = xml.Remove(groupsStart, groupsEnd - groupsStart);
                    groupsStart = xml.IndexOf(groupsStartMarker, groupsStart, StringComparison.Ordinal);
                }
            }

            File.WriteAllText(targetPath, xml, new UTF8Encoding(true));
        }

        private static void VerifyIdentityAndSheetNameValidation(DiagramProject original)
        {
            var duplicateTag = CloneProject(original);
            duplicateTag.Plant.Tags[1].Id = duplicateTag.Plant.Tags[0].Id;
            var duplicateResult = new DiagramSclGenerator().Generate(
                duplicateTag.Plant,
                duplicateTag.Sheets[0]);
            AssertIssue(duplicateResult, "CORE_DUPLICATE_ID", "Duplicate model ID was masked.");

            var emptyNode = CloneProject(original);
            emptyNode.Sheets[0].Nodes[0].Id = Guid.Empty;
            var nodeValidation = new DiagramValidator().Validate(emptyNode.Sheets[0]);
            Assert(
                nodeValidation.Issues.Any(issue => issue.Code == "DGM008"),
                "Empty node ID was not detected.");

            var crossKind = CloneProject(original);
            crossKind.Sheets[0].Nodes[0].Pins[0].Id = crossKind.Sheets[0].Nodes[1].Id;
            var crossKindValidation = new DiagramValidator().Validate(crossKind.Sheets[0]);
            Assert(
                crossKindValidation.Issues.Any(issue => issue.Code == "DGM017"),
                "ID shared by a node and pin was not detected.");

            var emptyName = CloneProject(original);
            emptyName.Sheets[0].Name = string.Empty;
            var nameValidation = new DiagramValidator().Validate(emptyName.Sheets[0]);
            Assert(
                nameValidation.Issues.Any(issue => issue.Code == "DGM006"),
                "Empty call-sheet name was not detected.");
        }

        private static void VerifyOnlyCycleMembersAreReported(BlockDefinition block)
        {
            var sheet = new CallSheet { Name = "CycleMembersTest" };
            var first = DiagramNodeFactory.CreateBlockCall(block, "CycleA", 100, 100);
            var second = DiagramNodeFactory.CreateBlockCall(block, "CycleB", 400, 100);
            var downstream = DiagramNodeFactory.CreateBlockCall(block, "Downstream", 700, 100);
            sheet.Nodes.Add(first);
            sheet.Nodes.Add(second);
            sheet.Nodes.Add(downstream);
            Connect(sheet, first, "Q", second, "Open");
            Connect(sheet, second, "Q", first, "Open");
            Connect(sheet, second, "Fault", downstream, "Open");

            var validation = new DiagramValidator().Validate(sheet);
            var cycleNodeIds = validation.Issues
                .Where(issue => issue.Code == "DGM030" && issue.NodeId.HasValue)
                .Select(issue => issue.NodeId.Value)
                .ToArray();
            Assert(cycleNodeIds.Contains(first.Id), "First cycle member was not reported.");
            Assert(cycleNodeIds.Contains(second.Id), "Second cycle member was not reported.");
            Assert(!cycleNodeIds.Contains(downstream.Id), "Downstream node was incorrectly reported as cyclic.");
        }

        private static byte[] SerializeProjectBytes(DiagramProject project)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tiasclproj");
            var storage = new DiagramProjectStorage();
            try
            {
                storage.Save(path, project);
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static DiagramProject CloneProject(DiagramProject project)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tiasclproj");
            var storage = new DiagramProjectStorage();
            try
            {
                storage.Save(path, project);
                return storage.Load(path);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void AssertIssue(DiagramCompilationResult result, string code, string message)
        {
            Assert(!result.IsSuccess, message + " Generation unexpectedly succeeded.");
            Assert(result.Issues.Any(issue => issue.Code == code), message + " Issues: " + FormatIssues(result.Issues));
        }

        private static void AssertProjectIssue(
            DiagramProjectCompilationResult result,
            string code,
            string message)
        {
            Assert(!result.IsSuccess, message + " Project compilation unexpectedly succeeded.");
            Assert(
                result.Issues.Any(issue => issue.Code == code),
                message + " Issues: " + FormatProjectIssues(result.Issues));
            Assert(result.GeneratedSources.Count == 0, message + " Failed compilation leaked sources.");
        }

        private static void AddBinding(UnitDefinition unit, BlockDefinition block, string parameterName, string expression)
        {
            var parameter = block.Interface.Single(member => member.Name == parameterName);
            unit.Bindings.Add(new ParameterBinding(parameter, expression));
        }

        private static string FormatIssues(IEnumerable<DiagramIssue> issues)
        {
            return string.Join(Environment.NewLine, issues.Select(issue => issue.Code + ": " + issue.Message));
        }

        private static string FormatProjectIssues(IEnumerable<DiagramProjectIssue> issues)
        {
            return string.Join(Environment.NewLine, issues.Select(issue =>
                issue.Code + " [" + issue.SheetName + "]: " + issue.Message));
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static TException AssertThrows<TException>(Action action, string subject)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    subject + " threw " + exception.GetType().FullName +
                    " instead of " + typeof(TException).FullName + ".",
                    exception);
            }

            throw new InvalidOperationException(
                subject + " did not throw " + typeof(TException).FullName + ".");
        }

        private static void AssertArgumentParameter(
            ArgumentException exception,
            string expectedParameter,
            string subject)
        {
            AssertEqual(expectedParameter, exception.ParamName, subject + " parameter name");
        }

        private static void AssertHasUtf8Bom(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Assert(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                Path.GetFileName(path) + " must use UTF-8 with BOM.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual(string expected, string actual, string subject)
        {
            var normalizedExpected = NormalizeNewLines(expected);
            var normalizedActual = NormalizeNewLines(actual);
            if (!string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    subject + " differs.\nEXPECTED:\n" + normalizedExpected + "\nACTUAL:\n" + normalizedActual);
            }
        }

        private static string NormalizeNewLines(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private const string ExpectedValveScl =
            "FUNCTION \"FC_CallUnits\" : Void\n" +
            "{ S7_Optimized_Access := 'TRUE' }\n" +
            "VERSION : 0.1\n" +
            "BEGIN\n" +
            "   // V01 : FB_Valve — Клапан подачи\n" +
            "   \"V01\"(Open := \"V01_Open_Cmd\",\n" +
            "         FbOpened := \"V01_FB_Opened\",\n" +
            "         TravelTime := T#3s,\n" +
            "         Q => \"V01_Sol\",\n" +
            "         Fault => \"V01_Fault\");\n" +
            "END_FUNCTION\n";
    }
}
