using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using ShapePath = System.Windows.Shapes.Path;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Diagram.History;
using TiaSclStudio.Diagram.Model;
using TiaSclStudio.Diagram.Storage;
using TiaSclStudio.Diagram.Validation;
using TiaSclStudio.Openness.Discovery;

namespace TiaSclStudio.App
{
    public partial class MainWindow : Window
    {
        private const double BlockNodeWidth = 272.0;
        private const double TerminalNodeWidth = 154.0;
        private const double ConstantNodeWidth = 128.0;
        private const double LogicNodeWidth = 224.0;
        private const double NoteNodeWidth = 238.0;
        private const double TerminalNodeHeight = 44.0;
        private const double NoteNodeHeight = 112.0;
        private const double BlockHeaderHeight = 54.0;
        private const double LogicHeaderHeight = 42.0;
        private const double PinRowHeight = 32.0;

        private readonly DiagramProjectStorage _storage;
        private readonly DiagramProjectCompiler _projectCompiler;
        private readonly ObservableCollection<DiagnosticItem> _diagnostics;
        private readonly List<DiagnosticItem> _interactionDiagnostics;
        private readonly List<ShapePath> _wireVisuals;
        private readonly Dictionary<Guid, WireVisualState> _wireVisualStates;
        private readonly Dictionary<Guid, CallNode> _wireNodeIndex;
        private readonly Dictionary<Guid, HashSet<Guid>> _incidentWireIdsByNodeId;
        private readonly Dictionary<Guid, FrameworkElement> _nodeVisuals;

        private DiagramProject _project;
        private CallSheet _sheet;
        private string _currentPath;

        private PinEndpoint _pendingSource;
        private Button _pendingSourceButton;

        private CallNode _dragNode;
        private FrameworkElement _dragElement;
        private Point _dragStartMouse;
        private Point _dragStartNode;
        private bool _dragMoved;

        public MainWindow()
        {
            InitializeComponent();

            _tiaGatewayWorker = new TiaGatewayWorker();

            _storage = new DiagramProjectStorage();
            _projectCompiler = new DiagramProjectCompiler();
            _diagnostics = new ObservableCollection<DiagnosticItem>();
            _interactionDiagnostics = new List<DiagnosticItem>();
            _wireVisuals = new List<ShapePath>();
            _wireVisualStates = new Dictionary<Guid, WireVisualState>();
            _wireNodeIndex = new Dictionary<Guid, CallNode>();
            _incidentWireIdsByNodeId = new Dictionary<Guid, HashSet<Guid>>();
            _nodeVisuals = new Dictionary<Guid, FrameworkElement>();

            DiagnosticsGrid.ItemsSource = _diagnostics;
            RuntimeStatusText.Text = BuildRuntimeStatus();
            InitializeOnlineWorkflow();
            LoadProject(CreateDemoProject(), null, "Демонстрационная схема готова");
        }

        private static string BuildRuntimeStatus()
        {
            try
            {
                var discovery = new OpennessInstallationLocator().Locate();
                var newestPortal = discovery.Installations
                    .OrderByDescending(item => item.PortalVersion)
                    .FirstOrDefault();
                if (newestPortal == null)
                {
                    return "NET48 · X64 · OFFLINE · TIA НЕ НАЙДЕНА";
                }

                var apiMajors = discovery.Installations
                    .Where(item => item.PortalVersion.Major == newestPortal.PortalVersion.Major)
                    .Select(item => item.ApiVersion.Major)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                var apiText = apiMajors.Length == 1
                    ? "V" + apiMajors[0]
                    : "V" + apiMajors.First() + "–V" + apiMajors.Last();

                return "NET48 · X64 · OFFLINE · PORTAL V" +
                    newestPortal.PortalVersion.Major + " / API " + apiText;
            }
            catch (Exception)
            {
                return "NET48 · X64 · OFFLINE · TIA DISCOVERY ERROR";
            }
        }

        private static DiagramProject CreateDemoProject()
        {
            var project = new DiagramProject();
            project.Plant.Name = "Demo_Valve_Control";

            var valve = CreateDemoValveDefinition();
            project.Plant.Blocks.Add(valve);

            var openCommand = CreateTag(
                "V01_Open_Cmd",
                "Bool",
                "%M0.0",
                "Команда открыть клапан");
            var openedFeedback = CreateTag(
                "V01_FB_Opened",
                "Bool",
                "%I0.0",
                "Обратная связь открытого положения");
            var solenoid = CreateTag(
                "V01_Sol",
                "Bool",
                "%Q0.0",
                "Выход на соленоид");
            var fault = CreateTag(
                "V01_Fault",
                "Bool",
                "%M0.1",
                "Авария клапана");

            project.Plant.Tags.Add(openCommand);
            project.Plant.Tags.Add(openedFeedback);
            project.Plant.Tags.Add(solenoid);
            project.Plant.Tags.Add(fault);

            var sheet = new CallSheet
            {
                Name = "FC_CallUnits",
                Width = 1180,
                Height = 700,
                Zoom = 1.0
            };

            var openNode = DiagramNodeFactory.CreateTag(
                openCommand,
                TerminalDirection.Source,
                55,
                145);
            var feedbackNode = DiagramNodeFactory.CreateTag(
                openedFeedback,
                TerminalDirection.Source,
                55,
                250);
            var timeNode = DiagramNodeFactory.CreateConstant(
                "T#3s",
                "Time",
                78,
                355);
            var valveNode = DiagramNodeFactory.CreateBlockCall(
                valve,
                "V01",
                410,
                195);
            var solenoidNode = DiagramNodeFactory.CreateTag(
                solenoid,
                TerminalDirection.Sink,
                845,
                205);
            var faultNode = DiagramNodeFactory.CreateTag(
                fault,
                TerminalDirection.Sink,
                845,
                310);

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

            project.Sheets.Add(sheet);
            return project;
        }

        private static BlockDefinition CreateDemoValveDefinition()
        {
            var definition = new BlockDefinition
            {
                Name = "FB_Valve",
                Kind = BlockKind.FunctionBlock,
                Description = "Клапан подачи",
                Version = "0.1",
                OptimizedAccess = true,
                SclBody =
                    "Q := Open;\r\n" +
                    "Fault := Open AND NOT FbOpened;"
            };

            definition.Interface.Add(new InterfaceMember("Open", "Bool", InterfaceSection.Input)
            {
                InitialValue = "FALSE",
                Comment = "Команда открытия"
            });
            definition.Interface.Add(new InterfaceMember("FbOpened", "Bool", InterfaceSection.Input)
            {
                InitialValue = "FALSE",
                Comment = "Обратная связь"
            });
            definition.Interface.Add(new InterfaceMember("TravelTime", "Time", InterfaceSection.Input)
            {
                InitialValue = "T#3s",
                Comment = "Допустимое время хода"
            });
            definition.Interface.Add(new InterfaceMember("Q", "Bool", InterfaceSection.Output)
            {
                Comment = "Управление соленоидом"
            });
            definition.Interface.Add(new InterfaceMember("Fault", "Bool", InterfaceSection.Output)
            {
                Comment = "Признак аварии"
            });

            return definition;
        }

        private static TagDefinition CreateTag(
            string name,
            string dataType,
            string address,
            string comment)
        {
            return new TagDefinition
            {
                Name = name,
                DataType = dataType,
                Address = address,
                Comment = comment
            };
        }

        private static void Connect(
            CallSheet sheet,
            CallNode sourceNode,
            string sourcePinName,
            CallNode targetNode,
            string targetPinName)
        {
            var sourcePin = sourceNode.Pins.First(pin => pin.Name == sourcePinName);
            var targetPin = targetNode.Pins.First(pin => pin.Name == targetPinName);

            sheet.Wires.Add(new Wire
            {
                SourceNodeId = sourceNode.Id,
                SourcePinId = sourcePin.Id,
                TargetNodeId = targetNode.Id,
                TargetPinId = targetPin.Id
            });
        }

        private void LoadProject(DiagramProject project, string path, string status)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            NormalizeProject(project);
            if (project.Sheets.Count == 0)
            {
                throw new InvalidOperationException("В проекте нет листа вызовов.");
            }

            _project = project;
            _sheet = project.Sheets[0];
            _currentPath = path;
            _interactionDiagnostics.Clear();

            DiagramCanvas.Width = Math.Max(900.0, _sheet.Width);
            DiagramCanvas.Height = Math.Max(560.0, _sheet.Height);
            LibraryList.ItemsSource = _project.Plant.Blocks;
            if (_project.Plant.Blocks.Count > 0)
            {
                LibraryList.SelectedIndex = 0;
            }

            LibraryCountText.Text = _project.Plant.Blocks.Count.ToString();
            HeaderProjectNameText.Text = _project.Plant.Name;
            SheetNameText.Text = _sheet.Name;
            ProjectPathText.Text = string.IsNullOrEmpty(path) ? "Проект не сохранён" : path;

            ResetCanvasEditingForLoadedProject();
            RenderDiagram();
            RefreshCompilation(false);
            SetStatus(status);
            DiagramScrollViewer.ScrollToHorizontalOffset(0);
            DiagramScrollViewer.ScrollToVerticalOffset(0);
        }

        private static void NormalizeProject(DiagramProject project)
        {
            if (project.Plant == null)
            {
                project.Plant = new PlantProject();
            }

            if (project.Plant.Blocks == null)
            {
                project.Plant.Blocks = new List<BlockDefinition>();
            }

            if (project.Plant.Tags == null)
            {
                project.Plant.Tags = new List<TagDefinition>();
            }

            if (project.Plant.DataTypes == null)
            {
                project.Plant.DataTypes = new List<UdtDefinition>();
            }

            if (project.Plant.CallBlocks == null)
            {
                project.Plant.CallBlocks = new List<CallBlockDefinition>();
            }

            if (project.Sheets == null)
            {
                project.Sheets = new List<CallSheet>();
            }

            foreach (var sheet in project.Sheets)
            {
                if (sheet.Groups == null)
                {
                    sheet.Groups = new List<DiagramGroup>();
                }

                foreach (var group in sheet.Groups.Where(item => item != null))
                {
                    if (group.MemberNodeIds == null)
                    {
                        group.MemberNodeIds = new List<Guid>();
                    }
                }

                if (sheet.Nodes == null)
                {
                    sheet.Nodes = new List<CallNode>();
                }

                if (sheet.Wires == null)
                {
                    sheet.Wires = new List<Wire>();
                }

                foreach (var node in sheet.Nodes)
                {
                    if (node.Pins == null)
                    {
                        node.Pins = new List<Pin>();
                    }
                }
            }
        }

        private void RenderDiagram()
        {
            CancelCanvasViewGestures();
            CancelPendingConnection();
            DiagramCanvas.Children.Clear();
            ResetWireVisualCache();
            _nodeVisuals.Clear();
            _groupVisuals.Clear();

            if (_sheet == null)
            {
                ClearNodeSelectionState();
                _selectedWireId = null;
                _selectedGroupId = null;
                UpdateSelectionCommandState();
                return;
            }

            NormalizeNodeSelectionForCurrentSheet();
            NormalizeGroupSelectionForCurrentSheet();

            if (_selectedWireId.HasValue &&
                !_sheet.Wires.Any(item => item.Id == _selectedWireId.Value))
            {
                _selectedWireId = null;
            }

            RenderGroups();

            foreach (var node in _sheet.Nodes)
            {
                var visual = CreateNodeVisual(node);
                Canvas.SetLeft(visual, node.X);
                Canvas.SetTop(visual, node.Y);
                Panel.SetZIndex(visual, 10);
                DiagramCanvas.Children.Add(visual);
                if (!_nodeVisuals.ContainsKey(node.Id))
                {
                    _nodeVisuals.Add(node.Id, visual);
                }
            }

            RenderWires();
            UpdateSelectionVisuals();
            UpdateSelectionCommandState();
        }

        private FrameworkElement CreateNodeVisual(CallNode node)
        {
            var block = node as BlockCallNode;
            if (block != null)
            {
                return CreateBlockVisual(block);
            }

            var tag = node as TagNode;
            if (tag != null)
            {
                return CreateTagVisual(tag);
            }

            var constant = node as ConstantNode;
            if (constant != null)
            {
                return CreateConstantVisual(constant);
            }

            var logic = node as LogicNode;
            if (logic != null)
            {
                return CreateLogicVisual(logic);
            }

            var note = node as NoteNode;
            if (note != null)
            {
                return CreateNoteVisual(note);
            }

            return CreateGenericVisual(node);
        }

        private FrameworkElement CreateBlockVisual(BlockCallNode node)
        {
            var height = GetNodeHeight(node);
            var definition = _project == null || _project.Plant == null
                ? null
                : _project.Plant.Blocks.FirstOrDefault(block => block.Id == node.BlockDefinitionId);
            var hasDefinition = definition != null;
            var isFunction = definition != null && definition.Kind == BlockKind.Function;
            var bodyColor = !hasDefinition ? "#20242B" : isFunction ? "#241A32" : "#142636";
            var headerColor = !hasDefinition ? "#303841" : isFunction ? "#41275F" : "#1C3A55";
            var identityColor = !hasDefinition ? "#95A3AF" : isFunction ? "#C28CFF" : "#58C7F3";
            var identityMutedColor = !hasDefinition ? "#303A43" : isFunction ? "#352349" : "#21455C";
            var border = CreateNodeBorder(node, BlockNodeWidth, height, bodyColor);
            border.Uid = !hasDefinition
                ? "Node.Block.Unknown"
                : isFunction ? "Node.Block.FC" : "Node.Block.FB";
            var blockKindText = definition == null
                ? "BLOCK ?"
                : isFunction ? "FC | FUNC" : "FB | STATE";

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BlockHeaderHeight) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var identityRail = new Border
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = CreateColorBrush(identityColor),
                CornerRadius = new CornerRadius(5, 0, 0, 5),
                IsHitTestVisible = false
            };
            Grid.SetRowSpan(identityRail, 2);
            Panel.SetZIndex(identityRail, 2);
            layout.Children.Add(identityRail);

            var header = new Border
            {
                Background = CreateColorBrush(headerColor),
                BorderBrush = GetBrush("BorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Padding = new Thickness(16, 7, 10, 7)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(node.InstanceName) ? "<без имени>" : node.InstanceName,
                Foreground = GetBrush("TextBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = node.Title,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = GetBrush("TextMutedBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10
            });
            headerGrid.Children.Add(nameStack);

            var badge = new Border
            {
                Padding = new Thickness(7, 3, 7, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = CreateColorBrush(identityMutedColor),
                BorderBrush = CreateColorBrush(identityColor),
                BorderThickness = new Thickness(1),
                CornerRadius = isFunction ? new CornerRadius(10) : new CornerRadius(2),
                Child = new TextBlock
                {
                    Text = blockKindText,
                    Foreground = CreateColorBrush(identityColor),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badge, 1);
            headerGrid.Children.Add(badge);
            header.Child = headerGrid;
            layout.Children.Add(header);

            var pinsGrid = new Grid { Margin = new Thickness(10, 10, 10, 8) };
            pinsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            pinsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(pinsGrid, 1);

            var inputStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var pin in OrderedPins(node, PinDirection.Input))
            {
                inputStack.Children.Add(CreatePinButton(node, pin));
            }

            var outputStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var pin in OrderedPins(node, PinDirection.Output))
            {
                outputStack.Children.Add(CreatePinButton(node, pin));
            }

            pinsGrid.Children.Add(inputStack);
            Grid.SetColumn(outputStack, 1);
            pinsGrid.Children.Add(outputStack);
            layout.Children.Add(pinsGrid);

            border.Child = layout;
            return border;
        }

        private FrameworkElement CreateTagVisual(TagNode node)
        {
            var isSource = node.TerminalDirection == TerminalDirection.Source;
            var accentColor = isSource ? "#42D6F4" : "#67D99B";
            var mutedColor = isSource ? "#153B48" : "#193B2B";
            var border = CreateNodeBorder(
                node,
                TerminalNodeWidth,
                TerminalNodeHeight,
                isSource ? "#112934" : "#132A20");
            border.Uid = isSource ? "Node.Tag.Source" : "Node.Tag.Sink";
            ApplyCompactNodeShadow(border);

            var grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var directionBadge = new Border
            {
                Width = 38,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center,
                Background = CreateColorBrush(mutedColor),
                BorderBrush = CreateColorBrush(accentColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = isSource ? "T \u25B6" : "\u25C0 T",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = CreateColorBrush(accentColor),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                }
            };

            var titleStack = new StackPanel
            {
                Margin = isSource ? new Thickness(0, 0, 7, 0) : new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = node.TagName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("TextBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = node.DataType,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("TextMutedBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8
            });

            if (isSource)
            {
                grid.Children.Add(titleStack);
                Grid.SetColumn(directionBadge, 1);
                grid.Children.Add(directionBadge);
            }
            else
            {
                grid.Children.Add(directionBadge);
                Grid.SetColumn(titleStack, 1);
                grid.Children.Add(titleStack);
            }

            var pin = node.Pins.FirstOrDefault();
            if (pin != null)
            {
                var pinButton = CreatePinDotButton(node, pin);
                pinButton.HorizontalAlignment = isSource
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left;
                pinButton.VerticalAlignment = VerticalAlignment.Center;
                pinButton.Margin = isSource
                    ? new Thickness(0, 0, -21, 0)
                    : new Thickness(-21, 0, 0, 0);
                Grid.SetColumnSpan(pinButton, 2);
                Panel.SetZIndex(pinButton, 2);
                grid.Children.Add(pinButton);
            }

            border.ToolTip = (isSource ? "Тег-источник: " : "Тег-приёмник: ") +
                node.TagName + " : " + node.DataType;
            border.Child = grid;
            return border;
        }

        private FrameworkElement CreateConstantVisual(ConstantNode node)
        {
            var border = CreateNodeBorder(
                node,
                ConstantNodeWidth,
                TerminalNodeHeight,
                "#2A2415");
            border.Uid = "Node.Constant";
            ApplyCompactNodeShadow(border);

            var grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new Border
            {
                Width = 22,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center,
                Background = CreateColorBrush("#3B3118"),
                BorderBrush = GetBrush("WarningBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = "#",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = GetBrush("WarningBrush"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            });

            var valueStack = new StackPanel
            {
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            valueStack.Children.Add(new TextBlock
            {
                Text = node.Literal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("TextBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
            valueStack.Children.Add(new TextBlock
            {
                Text = node.DataType.ToUpperInvariant(),
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("WarningBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8
            });
            Grid.SetColumn(valueStack, 1);
            grid.Children.Add(valueStack);

            var pin = node.Pins.FirstOrDefault();
            if (pin != null)
            {
                var pinButton = CreatePinDotButton(node, pin);
                pinButton.HorizontalAlignment = HorizontalAlignment.Right;
                pinButton.VerticalAlignment = VerticalAlignment.Center;
                pinButton.Margin = new Thickness(0, 0, -21, 0);
                Grid.SetColumnSpan(pinButton, 2);
                Panel.SetZIndex(pinButton, 2);
                grid.Children.Add(pinButton);
            }

            border.ToolTip = "Константа: " + node.Literal + " : " + node.DataType;
            border.Child = grid;
            return border;
        }

        private FrameworkElement CreateLogicVisual(LogicNode node)
        {
            var border = CreateNodeBorder(
                node,
                LogicNodeWidth,
                GetNodeHeight(node),
                "#132A26");
            border.Uid = "Node.Logic";

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LogicHeaderHeight) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border
            {
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#19423A")),
                BorderBrush = GetBrush("BorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Padding = new Thickness(12, 0, 10, 0)
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock
            {
                Text = node.Title,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetBrush("TextBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            var badge = new Border
            {
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = CreateColorBrush("#1E4A40"),
                BorderBrush = CreateColorBrush("#64D8B3"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = "LOGIC",
                    Foreground = CreateColorBrush("#64D8B3"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badge, 1);
            headerGrid.Children.Add(badge);
            header.Child = headerGrid;
            layout.Children.Add(header);

            var pinsGrid = new Grid { Margin = new Thickness(10, 8, 10, 7) };
            pinsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            pinsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(pinsGrid, 1);

            var inputs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var pin in OrderedPins(node, PinDirection.Input))
            {
                inputs.Children.Add(CreatePinButton(node, pin));
            }

            var outputs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var pin in OrderedPins(node, PinDirection.Output))
            {
                outputs.Children.Add(CreatePinButton(node, pin));
            }

            pinsGrid.Children.Add(inputs);
            Grid.SetColumn(outputs, 1);
            pinsGrid.Children.Add(outputs);
            layout.Children.Add(pinsGrid);
            border.Child = layout;
            return border;
        }

        private FrameworkElement CreateNoteVisual(NoteNode node)
        {
            var border = CreateNodeBorder(
                node,
                NoteNodeWidth,
                NoteNodeHeight,
                "#2A2418");
            border.Uid = "Node.Note";
            var layout = new Grid { Margin = new Thickness(13, 10, 13, 10) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(new TextBlock
            {
                Text = "NOTE",
                Foreground = GetBrush("WarningBrush"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9,
                FontWeight = FontWeights.Bold
            });
            var text = new TextBlock
            {
                Text = node.Text,
                Margin = new Thickness(0, 7, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("TextBrush"),
                FontSize = 11,
                LineHeight = 16
            };
            Grid.SetRow(text, 1);
            layout.Children.Add(text);
            border.Child = layout;
            border.ToolTip = node.Text;
            return border;
        }

        private static SolidColorBrush CreateColorBrush(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private static void ApplyCompactNodeShadow(Border border)
        {
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 4,
                Color = Colors.Black,
                Direction = 270,
                Opacity = 0.14,
                ShadowDepth = 1
            };
        }

        private FrameworkElement CreateGenericVisual(CallNode node)
        {
            var border = CreateNodeBorder(node, 220, 84, "#20232B");
            border.Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(node.Title) ? node.Kind.ToString() : node.Title,
                Margin = new Thickness(13),
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetBrush("TextBrush")
            };
            return border;
        }

        private Border CreateNodeBorder(
            CallNode node,
            double width,
            double height,
            string background)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
                BorderBrush = IsNodeSelected(node.Id)
                    ? GetBrush("AccentBrush")
                    : GetBrush("BorderStrongBrush"),
                BorderThickness = IsNodeSelected(node.Id)
                    ? new Thickness(2)
                    : new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Tag = node,
                Cursor = Cursors.SizeAll,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 7,
                    Color = Colors.Black,
                    Direction = 270,
                    Opacity = 0.22,
                    ShadowDepth = 2
                }
            };

            border.PreviewMouseLeftButtonDown += Node_MouseLeftButtonDown;
            border.MouseMove += Node_MouseMove;
            border.MouseLeftButtonUp += Node_MouseLeftButtonUp;
            border.LostMouseCapture += Node_LostMouseCapture;
            return border;
        }

        private Button CreatePinButton(CallNode node, Pin pin)
        {
            var isInput = pin.Direction == PinDirection.Input;
            var button = new Button
            {
                Style = (Style)FindResource("NodePinButtonStyle"),
                Tag = new PinEndpoint(node, pin),
                Content = isInput
                    ? "●  " + pin.Name + " · " + pin.DataType
                    : pin.Name + " · " + pin.DataType + "  ●",
                HorizontalContentAlignment = isInput
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Right,
                ToolTip = (isInput ? "Вход" : "Выход") + " " + pin.Name + " : " + pin.DataType,
                Cursor = Cursors.Hand,
                Margin = isInput
                    ? new Thickness(-11, 0, 0, 5)
                    : new Thickness(0, 0, -11, 5)
            };
            button.Click += Pin_Click;
            return button;
        }

        private Button CreatePinDotButton(CallNode node, Pin pin)
        {
            var button = new Button
            {
                Style = (Style)FindResource("PinDotButtonStyle"),
                Tag = new PinEndpoint(node, pin),
                Content = "●",
                ToolTip = pin.Name + " : " + pin.DataType,
                Cursor = Cursors.Hand
            };
            button.Click += Pin_Click;
            return button;
        }

        private void RenderWires()
        {
            RebuildWireNodeIndex();
            _incidentWireIdsByNodeId.Clear();
            if (_sheet == null)
            {
                RemoveWireVisualsExcept(new HashSet<Guid>());
                _wireVisuals.Clear();
                return;
            }

            var renderedWireIds = new HashSet<Guid>();
            var orderedVisuals = new List<ShapePath>();
            foreach (var wire in _sheet.Wires ?? new List<Wire>())
            {
                if (wire == null || renderedWireIds.Contains(wire.Id))
                {
                    continue;
                }

                Point start;
                Point control1;
                Point control2;
                Point end;
                CallNode sourceNode;
                CallNode targetNode;
                if (!TryCalculateWirePoints(
                    wire,
                    out start,
                    out control1,
                    out control2,
                    out end,
                    out sourceNode,
                    out targetNode))
                {
                    continue;
                }

                WireVisualState state;
                if (!_wireVisualStates.TryGetValue(wire.Id, out state))
                {
                    state = CreateWireVisualState(wire, start, control1, control2, end);
                    _wireVisualStates.Add(wire.Id, state);
                }

                UpdateWireVisualState(state, wire, start, control1, control2, end);
                renderedWireIds.Add(wire.Id);
                AddIncidentWire(sourceNode.Id, wire.Id);
                AddIncidentWire(targetNode.Id, wire.Id);
                orderedVisuals.Add(state.VisiblePath);
                orderedVisuals.Add(state.HitTarget);
            }

            RemoveWireVisualsExcept(renderedWireIds);
            _wireVisuals.Clear();
            _wireVisuals.AddRange(orderedVisuals);
        }

        private void RebuildWireNodeIndex()
        {
            _wireNodeIndex.Clear();
            if (_sheet == null)
            {
                return;
            }

            foreach (var node in _sheet.Nodes ?? new List<CallNode>())
            {
                if (node != null && !_wireNodeIndex.ContainsKey(node.Id))
                {
                    _wireNodeIndex.Add(node.Id, node);
                }
            }
        }

        private bool TryCalculateWirePoints(
            Wire wire,
            out Point start,
            out Point control1,
            out Point control2,
            out Point end,
            out CallNode sourceNode,
            out CallNode targetNode)
        {
            start = new Point();
            control1 = new Point();
            control2 = new Point();
            end = new Point();
            sourceNode = null;
            targetNode = null;
            if (wire == null ||
                !_wireNodeIndex.TryGetValue(wire.SourceNodeId, out sourceNode) ||
                !_wireNodeIndex.TryGetValue(wire.TargetNodeId, out targetNode))
            {
                return false;
            }

            var sourcePin = (sourceNode.Pins ?? new List<Pin>())
                .FirstOrDefault(pin => pin != null && pin.Id == wire.SourcePinId);
            var targetPin = (targetNode.Pins ?? new List<Pin>())
                .FirstOrDefault(pin => pin != null && pin.Id == wire.TargetPinId);
            if (sourcePin == null || targetPin == null)
            {
                return false;
            }

            try
            {
                start = GetPinPosition(sourceNode, sourcePin);
                end = GetPinPosition(targetNode, targetPin);
                if (!IsFinitePoint(start) || !IsFinitePoint(end))
                {
                    return false;
                }

                var controlDistance = Math.Max(72.0, Math.Abs(end.X - start.X) * 0.48);
                if (!IsFinite(controlDistance))
                {
                    return false;
                }

                control1 = new Point(start.X + controlDistance, start.Y);
                control2 = new Point(end.X - controlDistance, end.Y);
                return IsFinitePoint(control1) && IsFinitePoint(control2);
            }
            catch (Exception)
            {
                sourceNode = null;
                targetNode = null;
                return false;
            }
        }

        private WireVisualState CreateWireVisualState(
            Wire wire,
            Point start,
            Point control1,
            Point control2,
            Point end)
        {
            var segment = new BezierSegment(control1, control2, end, true);
            var figure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(segment);
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var path = new ShapePath
            {
                Data = geometry,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Tag = wire,
                Uid = "WireVisible"
            };
            var hitTarget = new ShapePath
            {
                Data = geometry,
                Stroke = Brushes.Transparent,
                StrokeThickness = 16.0,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Cursor = Cursors.Hand,
                IsHitTestVisible = true,
                Tag = wire,
                Uid = "WireHitTarget"
            };
            hitTarget.MouseLeftButtonDown += Wire_MouseLeftButtonDown;

            Panel.SetZIndex(path, 2);
            Panel.SetZIndex(hitTarget, 3);
            DiagramCanvas.Children.Add(path);
            DiagramCanvas.Children.Add(hitTarget);
            return new WireVisualState(path, hitTarget, figure, segment, wire);
        }

        private void UpdateWireVisualState(
            WireVisualState state,
            Wire wire,
            Point start,
            Point control1,
            Point control2,
            Point end)
        {
            state.Wire = wire;
            state.Figure.StartPoint = start;
            state.Segment.Point1 = control1;
            state.Segment.Point2 = control2;
            state.Segment.Point3 = end;
            state.VisiblePath.Tag = wire;
            state.HitTarget.Tag = wire;
            state.VisiblePath.Visibility = Visibility.Visible;
            state.HitTarget.Visibility = Visibility.Visible;
            state.HitTarget.IsHitTestVisible = true;
            var selected = _selectedWireId.HasValue && _selectedWireId.Value == wire.Id;
            state.VisiblePath.Stroke = selected ? GetBrush("WarningBrush") : GetBrush("AccentBrush");
            state.VisiblePath.StrokeThickness = selected ? 3.6 : 2.4;
            state.VisiblePath.Opacity = selected ? 1.0 : 0.88;
        }

        private void UpdateIncidentWires(Guid nodeId)
        {
            UpdateIncidentWires(new[] { nodeId });
        }

        private void UpdateIncidentWires(IEnumerable<Guid> nodeIds)
        {
            if (_sheet == null || nodeIds == null)
            {
                return;
            }

            var wireIds = new HashSet<Guid>();
            foreach (var nodeId in nodeIds)
            {
                HashSet<Guid> incident;
                if (_incidentWireIdsByNodeId.TryGetValue(nodeId, out incident))
                {
                    wireIds.UnionWith(incident);
                }
            }

            foreach (var wireId in wireIds)
            {
                WireVisualState state;
                if (!_wireVisualStates.TryGetValue(wireId, out state))
                {
                    continue;
                }

                Point start;
                Point control1;
                Point control2;
                Point end;
                CallNode sourceNode;
                CallNode targetNode;
                if (TryCalculateWirePoints(
                    state.Wire,
                    out start,
                    out control1,
                    out control2,
                    out end,
                    out sourceNode,
                    out targetNode))
                {
                    UpdateWireVisualState(
                        state,
                        state.Wire,
                        start,
                        control1,
                        control2,
                        end);
                }
                else
                {
                    state.VisiblePath.Visibility = Visibility.Collapsed;
                    state.HitTarget.Visibility = Visibility.Collapsed;
                    state.HitTarget.IsHitTestVisible = false;
                }
            }
        }

        private void AddIncidentWire(Guid nodeId, Guid wireId)
        {
            HashSet<Guid> wireIds;
            if (!_incidentWireIdsByNodeId.TryGetValue(nodeId, out wireIds))
            {
                wireIds = new HashSet<Guid>();
                _incidentWireIdsByNodeId.Add(nodeId, wireIds);
            }

            wireIds.Add(wireId);
        }

        private void RemoveWireVisualsExcept(ISet<Guid> retainedWireIds)
        {
            var staleIds = _wireVisualStates.Keys
                .Where(id => retainedWireIds == null || !retainedWireIds.Contains(id))
                .ToArray();
            foreach (var wireId in staleIds)
            {
                WireVisualState state;
                if (!_wireVisualStates.TryGetValue(wireId, out state))
                {
                    continue;
                }

                state.HitTarget.MouseLeftButtonDown -= Wire_MouseLeftButtonDown;
                DiagramCanvas.Children.Remove(state.VisiblePath);
                DiagramCanvas.Children.Remove(state.HitTarget);
                _wireVisualStates.Remove(wireId);
            }
        }

        private void ResetWireVisualCache()
        {
            foreach (var state in _wireVisualStates.Values)
            {
                state.HitTarget.MouseLeftButtonDown -= Wire_MouseLeftButtonDown;
            }

            _wireVisuals.Clear();
            _wireVisualStates.Clear();
            _wireNodeIndex.Clear();
            _incidentWireIdsByNodeId.Clear();
        }

        private static bool IsFinitePoint(Point point)
        {
            return IsFinite(point.X) && IsFinite(point.Y);
        }

        private static IEnumerable<Pin> OrderedPins(CallNode node, PinDirection direction)
        {
            return node.Pins
                .Where(pin => pin.Direction == direction)
                .OrderBy(pin => pin.Order)
                .ThenBy(pin => pin.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static double GetNodeHeight(CallNode node)
        {
            var block = node as BlockCallNode;
            if (block != null)
            {
                var inputCount = block.Pins.Count(pin => pin.Direction == PinDirection.Input);
                var outputCount = block.Pins.Count(pin => pin.Direction == PinDirection.Output);
                return BlockHeaderHeight + 18.0 + Math.Max(inputCount, outputCount) * PinRowHeight;
            }

            var logic = node as LogicNode;
            if (logic != null)
            {
                var inputCount = logic.Pins.Count(pin => pin.Direction == PinDirection.Input);
                var outputCount = logic.Pins.Count(pin => pin.Direction == PinDirection.Output);
                return LogicHeaderHeight + 15.0 + Math.Max(inputCount, outputCount) * PinRowHeight;
            }

            return node is NoteNode ? NoteNodeHeight : TerminalNodeHeight;
        }

        private static double GetNodeWidth(CallNode node)
        {
            if (node is BlockCallNode)
            {
                return BlockNodeWidth;
            }

            if (node is ConstantNode)
            {
                return ConstantNodeWidth;
            }

            if (node is TagNode)
            {
                return TerminalNodeWidth;
            }

            if (node is LogicNode)
            {
                return LogicNodeWidth;
            }

            if (node is NoteNode)
            {
                return NoteNodeWidth;
            }

            return 220.0;
        }

        private static Point GetPinPosition(CallNode node, Pin pin)
        {
            var block = node as BlockCallNode;
            if (block != null)
            {
                var sameSide = OrderedPins(block, pin.Direction).ToList();
                var index = sameSide.FindIndex(candidate => candidate.Id == pin.Id);
                if (index < 0)
                {
                    index = 0;
                }

                return new Point(
                    pin.Direction == PinDirection.Input ? node.X : node.X + BlockNodeWidth,
                    node.Y + BlockHeaderHeight + 10.0 + index * PinRowHeight + 13.5);
            }

            var logic = node as LogicNode;
            if (logic != null)
            {
                var sameSide = OrderedPins(logic, pin.Direction).ToList();
                var index = sameSide.FindIndex(candidate => candidate.Id == pin.Id);
                if (index < 0)
                {
                    index = 0;
                }

                return new Point(
                    pin.Direction == PinDirection.Input ? node.X : node.X + LogicNodeWidth,
                    node.Y + LogicHeaderHeight + 8.0 + index * PinRowHeight + 13.5);
            }

            return new Point(
                pin.Direction == PinDirection.Input ? node.X : node.X + GetNodeWidth(node),
                node.Y + TerminalNodeHeight / 2.0);
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                e.Handled = true;
                return;
            }

            var button = sender as Button;
            var endpoint = button == null ? null : button.Tag as PinEndpoint;
            if (endpoint == null || _sheet == null)
            {
                return;
            }

            if (endpoint.Pin.Direction == PinDirection.Output)
            {
                SelectSource(endpoint, button);
            }
            else
            {
                CompleteConnection(endpoint);
            }

            e.Handled = true;
        }

        private void SelectSource(PinEndpoint endpoint, Button button)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            CancelPendingConnection();
            _interactionDiagnostics.Clear();
            _pendingSource = endpoint;
            _pendingSourceButton = button;
            button.Background = GetBrush("AccentMutedBrush");
            button.BorderBrush = GetBrush("AccentBrush");
            button.Foreground = GetBrush("AccentBrush");
            ConnectionHintText.Text =
                "Источник: " + endpoint.Node.Title + "." + endpoint.Pin.Name + " → выберите вход";
            ConnectionHintText.Foreground = GetBrush("AccentBrush");
            SetStatus("Выбран выходной пин " + endpoint.Pin.Name);
        }

        private void CompleteConnection(PinEndpoint target)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            if (_pendingSource == null)
            {
                AddInteractionError(
                    "UI101",
                    "Сначала выберите выходной пин, затем входной.",
                    target.Node.Title + "." + target.Pin.Name);
                return;
            }

            var source = _pendingSource;
            if (_sheet.Wires.Any(wire => wire.TargetPinId == target.Pin.Id))
            {
                AddInteractionError(
                    "UI102",
                    "Вход '" + target.Pin.Name +
                    "' уже подключён. Существующая связь сохранена и не была заменена.",
                    target.Node.Title + "." + target.Pin.Name);
                CancelPendingConnection();
                return;
            }

            if (!PlcTypeCompatibility.AreExactlyCompatible(
                source.Pin.DataType,
                target.Pin.DataType))
            {
                AddInteractionError(
                    "UI103",
                    "Несовместимые типы: '" + source.Pin.DataType +
                    "' нельзя подключить к '" + target.Pin.DataType + "'.",
                    source.Node.Title + " → " + target.Node.Title);
                CancelPendingConnection();
                return;
            }

            var newWire = new Wire
            {
                SourceNodeId = source.Node.Id,
                SourcePinId = source.Pin.Id,
                TargetNodeId = target.Node.Id,
                TargetPinId = target.Pin.Id
            };
            if (!TryCommitSemanticEdit(
                "Создание провода " + source.Pin.Name + " → " + target.Pin.Name,
                () => _sheet.Wires.Add(newWire)))
            {
                CancelPendingConnection();
                return;
            }

            _interactionDiagnostics.Clear();
            CancelPendingConnection();
            ClearNodeSelectionState();
            _selectedWireId = newWire.Id;
            RenderWires();
            RefreshCompilation(false);
            UpdateSelectionCommandState();
            SetStatus(
                "Связь создана: " + source.Pin.Name + " → " + target.Pin.Name);
        }

        private void CancelPendingConnection()
        {
            if (_pendingSourceButton != null)
            {
                _pendingSourceButton.ClearValue(Control.BackgroundProperty);
                _pendingSourceButton.ClearValue(Control.BorderBrushProperty);
                _pendingSourceButton.ClearValue(Control.ForegroundProperty);
            }

            _pendingSource = null;
            _pendingSourceButton = null;
            if (ConnectionHintText != null)
            {
                ConnectionHintText.Text = "Готово к созданию связей";
                ConnectionHintText.Foreground = GetBrush("TextMutedBrush");
            }
        }

        private void AddInteractionError(string code, string message, string context)
        {
            _interactionDiagnostics.Clear();
            _interactionDiagnostics.Add(new DiagnosticItem("ERROR", code, message, context));
            RefreshCompilation(false);
            SetStatus(message);
        }

        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                e.Handled = true;
                return;
            }

            if (e.ChangedButton != MouseButton.Left ||
                FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            var element = sender as FrameworkElement;
            var node = element == null ? null : element.Tag as CallNode;
            if (element == null || node == null)
            {
                return;
            }

            SelectNode(node.Id);
            if (e.ClickCount >= 2)
            {
                EditNodeProperties(node);
                e.Handled = true;
                return;
            }

            _dragNode = node;
            _dragElement = element;
            _dragStartMouse = e.GetPosition(DiagramCanvas);
            _dragStartNode = new Point(node.X, node.Y);
            _dragMoved = false;
            try
            {
                _dragBeforeProject = DiagramProjectSnapshot.Clone(_project);
            }
            catch (Exception exception)
            {
                _dragNode = null;
                _dragElement = null;
                ReportUnexpectedEditFailure("Начало перемещения узла", exception);
                e.Handled = true;
                return;
            }
            bool captured;
            try
            {
                captured = element.CaptureMouse();
            }
            catch (Exception exception)
            {
                _dragNode = null;
                _dragElement = null;
                _dragBeforeProject = null;
                ReportUnexpectedEditFailure("Захват мыши для перемещения узла", exception);
                e.Handled = true;
                return;
            }

            if (!captured)
            {
                _dragNode = null;
                _dragElement = null;
                _dragBeforeProject = null;
                SetStatus("Не удалось начать перемещение узла");
                e.Handled = true;
                return;
            }

            Panel.SetZIndex(element, 30);
            e.Handled = true;
        }

        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_onlineBusy)
            {
                CancelNodeDragForOnlineOperation();
                e.Handled = true;
                return;
            }

            if (_dragNode == null || _dragElement == null)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndNodeDrag(_dragMoved);
                return;
            }

            try
            {
                var current = e.GetPosition(DiagramCanvas);
                var dx = current.X - _dragStartMouse.X;
                var dy = current.Y - _dragStartMouse.Y;
                if (!_dragMoved && Math.Abs(dx) <= 2.0 && Math.Abs(dy) <= 2.0)
                {
                    e.Handled = true;
                    return;
                }

                _dragMoved = true;

                var maxX = Math.Max(12.0, DiagramCanvas.Width - GetNodeWidth(_dragNode) - 12.0);
                var maxY = Math.Max(12.0, DiagramCanvas.Height - GetNodeHeight(_dragNode) - 12.0);
                _dragNode.X = Math.Max(12.0, Math.Min(maxX, _dragStartNode.X + dx));
                _dragNode.Y = Math.Max(12.0, Math.Min(maxY, _dragStartNode.Y + dy));
                Canvas.SetLeft(_dragElement, _dragNode.X);
                Canvas.SetTop(_dragElement, _dragNode.Y);
                UpdateIncidentWires(_dragNode.Id);
            }
            catch (Exception exception)
            {
                AbortNodeDragAfterFailure(exception);
            }

            e.Handled = true;
        }

        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_onlineBusy)
            {
                CancelNodeDragForOnlineOperation();
                e.Handled = true;
                return;
            }

            if (_dragNode == null)
            {
                return;
            }

            var moved = _dragMoved;
            EndNodeDrag(moved);
            e.Handled = true;
        }

        private void Node_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_dragElement == sender)
            {
                EndNodeDrag(_dragMoved && !_onlineBusy);
            }
        }

        private void EndNodeDrag(bool refreshCompilation)
        {
            var element = _dragElement;
            var node = _dragNode;
            var before = _dragBeforeProject;
            _dragNode = null;
            _dragElement = null;
            _dragMoved = false;
            _dragBeforeProject = null;

            if (element != null)
            {
                Panel.SetZIndex(element, 10);
                if (element.IsMouseCaptured)
                {
                    element.ReleaseMouseCapture();
                }
            }

            if (refreshCompilation && node != null && !_onlineBusy)
            {
                try
                {
                    if (before != null)
                    {
                        _editHistory.Record(
                            "Перемещение узла " + node.Title,
                            before,
                            _project);
                        UpdateHistoryCommandState();
                    }
                }
                catch (Exception exception)
                {
                    RestoreProjectAfterFailedEdit(
                        before,
                        _sheet == null ? Guid.Empty : _sheet.Id,
                        GetSelectedLibraryBlockId(),
                        CaptureSheetZooms(_project),
                        "Перемещение узла " + node.Title,
                        exception);
                    return;
                }

                RefreshCompilation(false);
                SetStatus("Узел '" + node.Title + "' перемещён");
            }
        }

        private void CancelNodeDragForOnlineOperation()
        {
            CancelCanvasViewGestures();
            var node = _dragNode;
            var element = _dragElement;
            if (node != null)
            {
                node.X = _dragStartNode.X;
                node.Y = _dragStartNode.Y;
                if (element != null)
                {
                    Canvas.SetLeft(element, node.X);
                    Canvas.SetTop(element, node.Y);
                }

                UpdateIncidentWires(node.Id);
            }

            EndNodeDrag(false);
        }

        private void AbortNodeDragAfterFailure(Exception exception)
        {
            var before = _dragBeforeProject;
            var element = _dragElement;
            _dragNode = null;
            _dragElement = null;
            _dragMoved = false;
            _dragBeforeProject = null;
            if (element != null && element.IsMouseCaptured)
            {
                element.ReleaseMouseCapture();
            }

            if (before != null)
            {
                RestoreProjectAfterFailedEdit(
                    before,
                    _sheet == null ? Guid.Empty : _sheet.Id,
                    GetSelectedLibraryBlockId(),
                    CaptureSheetZooms(_project),
                    "Перемещение узла",
                    exception);
            }
            else
            {
                ReportUnexpectedEditFailure("Перемещение узла", exception);
            }
        }

        private static T FindVisualParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                var typed = current as T;
                if (typed != null)
                {
                    return typed;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void RefreshCompilation(bool clearInteractionDiagnostics)
        {
            if (_onlineBusy)
            {
                return;
            }

            InvalidateOnlinePreview();

            if (clearInteractionDiagnostics)
            {
                _interactionDiagnostics.Clear();
            }

            _diagnostics.Clear();
            if (_project == null || _sheet == null)
            {
                SclPreviewTextBox.Text = "// Нет открытого листа вызовов.";
                AddDiagnostic(new DiagnosticItem(
                    "ERROR",
                    "UI001",
                    "Нет открытого листа вызовов.",
                    string.Empty));
                UpdateDiagnosticSummary();
                return;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var diagnostic in _interactionDiagnostics)
            {
                AddDiagnosticUnique(diagnostic, keys);
            }

            foreach (var diagnostic in GetOnlineDiagnostics())
            {
                AddDiagnosticUnique(diagnostic, keys);
            }

            try
            {
                var compilation = _projectCompiler.Compile(_project);
                foreach (var issue in compilation.Issues)
                {
                    AddDiagnosticUnique(CreateDiagnostic(issue), keys);
                }

                var activeSheet = compilation.Sheets.FirstOrDefault(
                    item => item.SheetId.HasValue && item.SheetId.Value == _sheet.Id);
                var activeScl = activeSheet == null ? string.Empty : activeSheet.Scl;
                SclPreviewTextBox.Text = string.IsNullOrEmpty(activeScl)
                    ? "// SCL не сформирован. Исправьте ошибки в диагностике."
                    : activeScl;
            }
            catch (Exception exception)
            {
                SclPreviewTextBox.Text = "// Ошибка генерации SCL.";
                AddDiagnosticUnique(
                    new DiagnosticItem(
                        "ERROR",
                        "UI900",
                        exception.Message,
                        exception.GetType().Name),
                    keys);
            }

            if (_diagnostics.Count == 0)
            {
                AddDiagnostic(new DiagnosticItem(
                    "INFO",
                    "DGM_OK",
                    "Проект валиден: листов " + _project.Sheets.Count + ", SCL активного листа обновлён.",
                    _project.Plant.Name));
            }

            UpdateDiagnosticSummary();
        }

        private DiagnosticItem CreateDiagnostic(DiagramIssue issue)
        {
            var severity = issue.Severity == DiagramIssueSeverity.Error
                ? "ERROR"
                : issue.Severity == DiagramIssueSeverity.Warning
                    ? "WARNING"
                    : "INFO";

            return new DiagnosticItem(
                severity,
                issue.Code,
                issue.Message,
                DescribeContext(issue));
        }

        private DiagnosticItem CreateDiagnostic(DiagramProjectIssue issue)
        {
            var severity = issue.Severity == DiagramIssueSeverity.Error
                ? "ERROR"
                : issue.Severity == DiagramIssueSeverity.Warning
                    ? "WARNING"
                    : "INFO";

            return new DiagnosticItem(
                severity,
                issue.Code,
                issue.Message,
                DescribeContext(issue));
        }

        private string DescribeContext(DiagramProjectIssue issue)
        {
            var sheet = issue.SheetId.HasValue && _project != null
                ? _project.Sheets.FirstOrDefault(item =>
                    item.Id == issue.SheetId.Value &&
                    string.Equals(item.Name, issue.SheetName, StringComparison.Ordinal)) ??
                  _project.Sheets.FirstOrDefault(item => item.Id == issue.SheetId.Value)
                : null;
            var sheetName = sheet == null ? issue.SheetName : sheet.Name;
            if (issue.NodeId.HasValue && sheet != null)
            {
                var node = sheet.Nodes.FirstOrDefault(item => item.Id == issue.NodeId.Value);
                if (node != null)
                {
                    var block = node as BlockCallNode;
                    var nodeName = block == null
                        ? node.Title
                        : block.InstanceName + " / " + block.Title;
                    return string.IsNullOrWhiteSpace(sheetName)
                        ? nodeName
                        : sheetName + " / " + nodeName;
                }
            }

            if (issue.WireId.HasValue)
            {
                var wireName = "wire " + issue.WireId.Value.ToString("N").Substring(0, 8);
                return string.IsNullOrWhiteSpace(sheetName)
                    ? wireName
                    : sheetName + " / " + wireName;
            }

            return string.IsNullOrWhiteSpace(sheetName)
                ? (_project == null || _project.Plant == null ? string.Empty : _project.Plant.Name)
                : sheetName;
        }

        private string DescribeContext(DiagramIssue issue)
        {
            if (issue.NodeId.HasValue && _sheet != null)
            {
                var node = _sheet.Nodes.FirstOrDefault(item => item.Id == issue.NodeId.Value);
                if (node != null)
                {
                    var block = node as BlockCallNode;
                    return block == null
                        ? node.Title
                        : block.InstanceName + " / " + block.Title;
                }
            }

            return issue.WireId.HasValue
                ? "wire " + issue.WireId.Value.ToString("N").Substring(0, 8)
                : string.Empty;
        }

        private void AddDiagnosticUnique(DiagnosticItem diagnostic, ISet<string> keys)
        {
            var key = diagnostic.Severity + "|" + diagnostic.Code + "|" +
                diagnostic.Message + "|" + diagnostic.Context;
            if (keys.Add(key))
            {
                AddDiagnostic(diagnostic);
            }
        }

        private void AddDiagnostic(DiagnosticItem diagnostic)
        {
            _diagnostics.Add(diagnostic);
        }

        private void UpdateDiagnosticSummary()
        {
            var errors = _diagnostics.Count(item => item.Severity == "ERROR");
            var warnings = _diagnostics.Count(item => item.Severity == "WARNING");
            DiagnosticsCountText.Text = _diagnostics.Count.ToString();

            if (errors > 0)
            {
                DiagnosticsSummaryText.Text = "Ошибок: " + errors + " · предупреждений: " + warnings;
                DiagnosticsSummaryText.Foreground = GetBrush("ErrorBrush");
            }
            else if (warnings > 0)
            {
                DiagnosticsSummaryText.Text = "Предупреждений: " + warnings;
                DiagnosticsSummaryText.Foreground = GetBrush("WarningBrush");
            }
            else
            {
                DiagnosticsSummaryText.Text = "Ошибок нет";
                DiagnosticsSummaryText.Foreground = GetBrush("SuccessBrush");
            }

            var modelDiagnostics = _onlineDiagnostics == null
                ? _diagnostics.AsEnumerable()
                : _diagnostics.Where(item => !_onlineDiagnostics.Contains(item));
            var modelErrors = modelDiagnostics.Count(item => item.Severity == "ERROR");
            var modelWarnings = modelDiagnostics.Count(item => item.Severity == "WARNING");

            if (modelErrors > 0)
            {
                CompilationStateDot.Fill = GetBrush("ErrorBrush");
                CompilationStateText.Text = "INVALID";
                CompilationStateText.Foreground = GetBrush("ErrorBrush");
                HeaderProjectStateText.Text = "● CHECK FAILED";
                HeaderProjectStateText.Foreground = GetBrush("ErrorBrush");
            }
            else if (modelWarnings > 0)
            {
                CompilationStateDot.Fill = GetBrush("WarningBrush");
                CompilationStateText.Text = "WARNING";
                CompilationStateText.Foreground = GetBrush("WarningBrush");
                HeaderProjectStateText.Text = "● MODEL WARNING";
                HeaderProjectStateText.Foreground = GetBrush("WarningBrush");
            }
            else
            {
                CompilationStateDot.Fill = GetBrush("SuccessBrush");
                CompilationStateText.Text = "VALID";
                CompilationStateText.Foreground = GetBrush("SuccessBrush");
                HeaderProjectStateText.Text = "● MODEL READY";
                HeaderProjectStateText.Foreground = GetBrush("SuccessBrush");
            }
        }

        private void CreateDemo_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            LoadProject(CreateDemoProject(), null, "Демонстрационная схема создана заново");
        }

        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            RefreshCompilation(true);
            SetStatus(_diagnostics.Any(item => item.Severity == "ERROR")
                ? "Проверка завершена с ошибками"
                : "Проверка завершена, SCL обновлён");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            if (_project == null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Сохранить проект TIA SCL Studio",
                Filter = "TIA SCL Studio project (*.tiasclproj)|*.tiasclproj|Все файлы (*.*)|*.*",
                DefaultExt = ".tiasclproj",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = string.IsNullOrEmpty(_currentPath)
                    ? SafeFileName(_project.Plant.Name) + ".tiasclproj"
                    : System.IO.Path.GetFileName(_currentPath),
                InitialDirectory = string.IsNullOrEmpty(_currentPath)
                    ? null
                    : System.IO.Path.GetDirectoryName(_currentPath)
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                _storage.Save(dialog.FileName, _project);
                _currentPath = dialog.FileName;
                ProjectPathText.Text = _currentPath;
                _interactionDiagnostics.Clear();
                RefreshCompilation(false);
                SetStatus("Проект сохранён: " + System.IO.Path.GetFileName(_currentPath));
            }
            catch (Exception exception)
            {
                AddInteractionError(
                    "UI201",
                    "Не удалось сохранить проект: " + exception.Message,
                    dialog.FileName);
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable())
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Открыть проект TIA SCL Studio",
                Filter = "TIA SCL Studio project (*.tiasclproj)|*.tiasclproj|Все файлы (*.*)|*.*",
                DefaultExt = ".tiasclproj",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = string.IsNullOrEmpty(_currentPath)
                    ? null
                    : System.IO.Path.GetDirectoryName(_currentPath)
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var project = _storage.Load(dialog.FileName);
                LoadProject(
                    project,
                    dialog.FileName,
                    "Проект открыт: " + System.IO.Path.GetFileName(dialog.FileName));
            }
            catch (Exception exception)
            {
                AddInteractionError(
                    "UI202",
                    "Не удалось открыть проект: " + exception.Message,
                    dialog.FileName);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_onlineBusy || IsTypingFocus())
            {
                return;
            }

            var controlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var historyShortcut = controlPressed && (e.Key == Key.Z || e.Key == Key.Y);
            if (e.Key == Key.Escape && IsGroupDragActive())
            {
                CancelGroupDragForOnlineOperation();
                SetStatus("Изменение области отменено");
                e.Handled = true;
                return;
            }

            if (_dragNode != null && (historyShortcut || e.Key == Key.Delete))
            {
                SetStatus("Сначала завершите перемещение узла");
                e.Handled = true;
                return;
            }

            if (controlPressed && e.Key == Key.Z)
            {
                ApplyHistoryStep(true);
                e.Handled = true;
                return;
            }

            if (controlPressed && e.Key == Key.Y)
            {
                ApplyHistoryStep(false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete &&
                IsDiagramDeleteShortcutContext() &&
                (_selectedNodeIds.Count != 0 ||
                 _selectedNodeId.HasValue ||
                 _selectedWireId.HasValue))
            {
                DeleteDiagramSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && IsCanvasViewGestureActive())
            {
                CancelCanvasViewGestures();
                SetStatus("Операция на холсте отменена");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _pendingSource != null)
            {
                CancelPendingConnection();
                SetStatus("Создание связи отменено");
                e.Handled = true;
            }
        }

        private static string SafeFileName(string value)
        {
            var candidate = string.IsNullOrWhiteSpace(value) ? "TiaSclProject" : value;
            foreach (var character in System.IO.Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(character, '_');
            }

            return candidate;
        }

        private Brush GetBrush(string resourceKey)
        {
            return (Brush)FindResource(resourceKey);
        }

        private void SetStatus(string value)
        {
            StatusText.Text = value;
        }

        private sealed class PinEndpoint
        {
            public PinEndpoint(CallNode node, Pin pin)
            {
                Node = node;
                Pin = pin;
            }

            public CallNode Node { get; private set; }

            public Pin Pin { get; private set; }
        }

        private sealed class WireVisualState
        {
            public WireVisualState(
                ShapePath visiblePath,
                ShapePath hitTarget,
                PathFigure figure,
                BezierSegment segment,
                Wire wire)
            {
                VisiblePath = visiblePath;
                HitTarget = hitTarget;
                Figure = figure;
                Segment = segment;
                Wire = wire;
            }

            public ShapePath VisiblePath { get; private set; }

            public ShapePath HitTarget { get; private set; }

            public PathFigure Figure { get; private set; }

            public BezierSegment Segment { get; private set; }

            public Wire Wire { get; set; }
        }

        private sealed class DiagnosticItem
        {
            public DiagnosticItem(string severity, string code, string message, string context)
            {
                Severity = severity ?? string.Empty;
                Code = code ?? string.Empty;
                Message = message ?? string.Empty;
                Context = context ?? string.Empty;
            }

            public string Severity { get; private set; }

            public string Code { get; private set; }

            public string Message { get; private set; }

            public string Context { get; private set; }
        }
    }
}
