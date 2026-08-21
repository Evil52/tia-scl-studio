using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.TestSupport
{
    /// <summary>
    /// Complete, valid reference projects. These are the "known good" starting
    /// points that hardening tests deform in one specific way each.
    /// </summary>
    public static class DemoProjects
    {
        /// <summary>
        /// One FB with Input/Output/InOut plus a defaulted input, wired to tags
        /// and a constant. Compiles cleanly and exercises every argument form.
        /// </summary>
        public static DiagramProject Valve()
        {
            var project = ModelBuilder.Project("ValveDemo", "FC_CallUnits");

            var open = ModelBuilder.Input("Open");
            var feedback = ModelBuilder.Input("FbOpened");
            var travel = ModelBuilder.Input("TravelTime", "Time", "T#3s");
            var command = ModelBuilder.Output("Q");
            var fault = ModelBuilder.Output("Fault");
            var block = ModelBuilder.FunctionBlock("FB_Valve", open, feedback, travel, command, fault);
            block.Description = "Supply valve";
            project.Plant.Blocks.Add(block);

            var openCmd = ModelBuilder.Tag("V01_Open_Cmd", "Bool", "%I0.0");
            var opened = ModelBuilder.Tag("V01_FB_Opened", "Bool", "%I0.1");
            var solenoid = ModelBuilder.Tag("V01_Sol", "Bool", "%Q0.0");
            var alarm = ModelBuilder.Tag("V01_Fault", "Bool", "%Q0.1");
            project.Plant.Tags.AddRange(new[] { openCmd, opened, solenoid, alarm });

            var sheet = project.Sheets[0];
            var call = ModelBuilder.PlaceBlock(sheet, block, "V01", 400.0, 100.0);
            var openNode = ModelBuilder.PlaceTag(sheet, openCmd, TerminalDirection.Source, 40.0, 60.0);
            var openedNode = ModelBuilder.PlaceTag(sheet, opened, TerminalDirection.Source, 40.0, 140.0);
            var solenoidNode = ModelBuilder.PlaceTag(sheet, solenoid, TerminalDirection.Sink, 760.0, 60.0);
            var alarmNode = ModelBuilder.PlaceTag(sheet, alarm, TerminalDirection.Sink, 760.0, 140.0);

            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(openNode), ModelBuilder.PinOf(call, "Open"));
            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(openedNode), ModelBuilder.PinOf(call, "FbOpened"));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(call, "Q"), ModelBuilder.OnlyPin(solenoidNode));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(call, "Fault"), ModelBuilder.OnlyPin(alarmNode));

            return project;
        }

        /// <summary>
        /// Two chained FB calls with no tag between them. The intermediate value
        /// must land in VAR_TEMP, which is where temporary-name allocation bugs
        /// and execution-order bugs become visible.
        /// </summary>
        public static DiagramProject Chain()
        {
            var project = ModelBuilder.Project("ChainDemo", "FC_Chain");

            var producerOut = ModelBuilder.Output("Value", "Int");
            var producerIn = ModelBuilder.Input("Raw", "Int");
            var producer = ModelBuilder.FunctionBlock("FB_Producer", producerIn, producerOut);

            var consumerIn = ModelBuilder.Input("Value", "Int");
            var consumerOut = ModelBuilder.Output("Done");
            var consumer = ModelBuilder.FunctionBlock("FB_Consumer", consumerIn, consumerOut);

            project.Plant.Blocks.Add(producer);
            project.Plant.Blocks.Add(consumer);

            var rawTag = ModelBuilder.Tag("Raw_Value", "Int", "%IW0");
            var doneTag = ModelBuilder.Tag("Chain_Done", "Bool", "%Q1.0");
            project.Plant.Tags.Add(rawTag);
            project.Plant.Tags.Add(doneTag);

            var sheet = project.Sheets[0];
            var first = ModelBuilder.PlaceBlock(sheet, producer, "P01", 300.0, 100.0);
            var second = ModelBuilder.PlaceBlock(sheet, consumer, "C01", 600.0, 100.0);
            var raw = ModelBuilder.PlaceTag(sheet, rawTag, TerminalDirection.Source, 40.0, 100.0);
            var done = ModelBuilder.PlaceTag(sheet, doneTag, TerminalDirection.Sink, 900.0, 100.0);

            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(raw), ModelBuilder.PinOf(first, "Raw"));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(first, "Value"), ModelBuilder.PinOf(second, "Value"));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(second, "Done"), ModelBuilder.OnlyPin(done));

            return project;
        }

        /// <summary>
        /// Two boolean tags folded through AND and NOT into one FB input.
        /// The whole logic tree must be inlined into the call argument.
        /// </summary>
        public static DiagramProject LogicTree()
        {
            var project = ModelBuilder.Project("LogicDemo", "FC_Logic");

            var enable = ModelBuilder.Input("Enable");
            var block = ModelBuilder.FunctionBlock("FB_Gate", enable);
            project.Plant.Blocks.Add(block);

            var cmd = ModelBuilder.Tag("Cmd_Open", "Bool", "%I2.0");
            var alarm = ModelBuilder.Tag("Alarm", "Bool", "%I2.1");
            project.Plant.Tags.Add(cmd);
            project.Plant.Tags.Add(alarm);

            var sheet = project.Sheets[0];
            var cmdNode = ModelBuilder.PlaceTag(sheet, cmd, TerminalDirection.Source, 20.0, 40.0);
            var alarmNode = ModelBuilder.PlaceTag(sheet, alarm, TerminalDirection.Source, 20.0, 120.0);
            var notNode = ModelBuilder.PlaceLogic(sheet, LogicOperation.Not, "Bool", 200.0, 120.0);
            var andNode = ModelBuilder.PlaceLogic(sheet, LogicOperation.And, "Bool", 380.0, 60.0);
            var call = ModelBuilder.PlaceBlock(sheet, block, "G01", 620.0, 60.0);

            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(alarmNode), ModelBuilder.PinOf(notNode, "In"));
            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(cmdNode), ModelBuilder.PinOf(andNode, "In1"));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(notNode, "Result"), ModelBuilder.PinOf(andNode, "In2"));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(andNode, "Result"), ModelBuilder.PinOf(call, "Enable"));

            return project;
        }

        /// <summary>A non-Void FC whose return value must land in a temporary.</summary>
        public static DiagramProject FunctionWithReturn()
        {
            var project = ModelBuilder.Project("ScaleDemo", "FC_Scale_Calls");

            var raw = ModelBuilder.Input("Raw", "Int");
            var scale = ModelBuilder.Function("FC_Scale", "Real", raw);
            project.Plant.Blocks.Add(scale);

            var rawTag = ModelBuilder.Tag("Raw_Word", "Int", "%IW2");
            var scaled = ModelBuilder.Tag("Scaled_Value", "Real", "%MD10");
            project.Plant.Tags.Add(rawTag);
            project.Plant.Tags.Add(scaled);

            var sheet = project.Sheets[0];
            var call = ModelBuilder.PlaceBlock(sheet, scale, "Scale01", 300.0, 100.0);
            var source = ModelBuilder.PlaceTag(sheet, rawTag, TerminalDirection.Source, 40.0, 100.0);
            var sink = ModelBuilder.PlaceTag(sheet, scaled, TerminalDirection.Sink, 600.0, 100.0);

            ModelBuilder.Connect(sheet, ModelBuilder.OnlyPin(source), ModelBuilder.PinOf(call, "Raw"));
            ModelBuilder.Connect(sheet, ModelBuilder.PinOf(call, "Return"), ModelBuilder.OnlyPin(sink));

            return project;
        }

        public static BlockCallNode SingleBlockCall(DiagramProject project)
        {
            return project.Sheets[0].Nodes.OfType<BlockCallNode>().Single();
        }

        public static BlockDefinition SingleBlock(DiagramProject project)
        {
            return project.Plant.Blocks.Single();
        }
    }
}
