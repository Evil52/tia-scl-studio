using System;
using System.Collections.Generic;
using TiaSclStudio.Diagram.Model;
using Xunit;

namespace TiaSclStudio.App
{
    /// <summary>
    /// A note is the only node whose whole payload is free text, so everything that
    /// keeps that text out of the generated SCL and out of the project XML lives in
    /// <see cref="NoteEditingLogic"/> rather than in the model.
    /// </summary>
    public sealed class NoteEditingLogicTests
    {
        [Fact]
        public void CreateTrimsTheTextAndDerivesTheTitleFromTheFirstLine()
        {
            var note = NoteEditingLogic.Create("  Pump P-101\r\nCheck the setpoint  ", 40.0, 80.0);

            Assert.Equal("Pump P-101\r\nCheck the setpoint", note.Text);
            Assert.Equal("Pump P-101", note.Title);
            Assert.Equal(40.0, note.X);
            Assert.Equal(80.0, note.Y);
        }

        [Theory]
        [InlineData("First\nSecond")]
        [InlineData("First\rSecond")]
        [InlineData("First\r\nSecond")]
        public void EveryLineBreakStyleYieldsTheSameTitle(string text)
        {
            Assert.Equal("First", NoteEditingLogic.Create(text, 0.0, 0.0).Title);
        }

        [Fact]
        public void ATitleOfExactlyTheDisplayLimitIsKeptWhole()
        {
            var line = new string('X', 48);

            var note = NoteEditingLogic.Create(line, 0.0, 0.0);

            Assert.Equal(line, note.Title);
        }

        [Fact]
        public void ALongFirstLineIsEllipsizedInTheTitleButKeptWholeInTheText()
        {
            var line = new string('X', 60);

            var note = NoteEditingLogic.Create(line, 0.0, 0.0);

            Assert.Equal(line, note.Text);
            Assert.Equal(new string('X', 45) + "...", note.Title);
            Assert.Equal(48, note.Title.Length);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\r\n")]
        public void BlankTextIsRejected(string text)
        {
            string error;

            Assert.False(NoteEditingLogic.TryValidateText(text, out error));
            Assert.NotEmpty(error);
            Assert.Throws<InvalidOperationException>(() => NoteEditingLogic.Create(text, 0.0, 0.0));
        }

        [Fact]
        public void TextOfExactlyTheMaximumLengthIsAccepted()
        {
            string error;

            Assert.True(NoteEditingLogic.TryValidateText(new string('X', 2000), out error));
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void TextOneCharacterOverTheMaximumIsRejected()
        {
            string error;

            Assert.False(NoteEditingLogic.TryValidateText(new string('X', 2001), out error));
            Assert.Contains("2000", error, StringComparison.Ordinal);
        }

        /// <summary>
        /// The note text is written straight into the project XML, where a C0 control
        /// character would produce a document that cannot be read back.
        /// </summary>
        [Theory]
        [InlineData("Pump\u0001Trip")]
        [InlineData("Pump\u0000Trip")]
        [InlineData("Pump\u001FTrip")]
        public void ControlCharactersThatXmlCannotCarryAreRejected(string text)
        {
            string error;

            Assert.False(NoteEditingLogic.TryValidateText(text, out error));
            Assert.NotEmpty(error);
        }

        [Fact]
        public void TabAndNewlineRemainLegalInsideTheBody()
        {
            string error;

            Assert.True(NoteEditingLogic.TryValidateText("Line one\r\n\tIndented", out error));
        }

        [Fact]
        public void ApplyRewritesTheTextAndTheTitleOfTheAddressedNote()
        {
            NoteNode note;
            NoteNode other;
            var project = BuildProject(out note, out other);

            NoteEditingLogic.Apply(project, note.Id, "  Updated first line\nsecond  ");

            Assert.Equal("Updated first line\nsecond", note.Text);
            Assert.Equal("Updated first line", note.Title);
            Assert.Equal("Other", other.Text);
        }

        [Fact]
        public void ApplyFindsANoteOnAnySheetOfTheProject()
        {
            NoteNode note;
            NoteNode other;
            var project = BuildProject(out note, out other);

            NoteEditingLogic.Apply(project, other.Id, "Reworded");

            Assert.Equal("Reworded", other.Text);
            Assert.Equal("Reworded", other.Title);
        }

        [Fact]
        public void ApplyRejectsAnUnknownNodeId()
        {
            NoteNode note;
            NoteNode other;
            var project = BuildProject(out note, out other);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                NoteEditingLogic.Apply(project, Guid.NewGuid(), "Updated"));

            Assert.Contains("не существует", exception.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// A duplicated identifier has to stop the edit rather than silently rewrite
        /// whichever copy happens to come first out of the sheet collection.
        /// </summary>
        [Fact]
        public void ApplyRejectsADuplicatedNoteIdWithoutTouchingEitherCopy()
        {
            NoteNode note;
            NoteNode other;
            var project = BuildProject(out note, out other);
            other.Id = note.Id;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                NoteEditingLogic.Apply(project, note.Id, "Updated"));

            Assert.Contains("не уникален", exception.Message, StringComparison.Ordinal);
            Assert.Equal("Original", note.Text);
            Assert.Equal("Other", other.Text);
        }

        [Fact]
        public void ApplyRejectsInvalidTextBeforeChangingTheNode()
        {
            NoteNode note;
            NoteNode other;
            var project = BuildProject(out note, out other);

            Assert.Throws<InvalidOperationException>(() =>
                NoteEditingLogic.Apply(project, note.Id, "   "));

            Assert.Equal("Original", note.Text);
            Assert.Equal("Original title", note.Title);
        }

        [Fact]
        public void ApplyRejectsANullProject()
        {
            Assert.Throws<ArgumentNullException>(() =>
                NoteEditingLogic.Apply(null, Guid.NewGuid(), "Updated"));
        }

        [Fact]
        public void ANonNoteNodeWithTheSameIdIsNotMistakenForANote()
        {
            NoteNode note;
            NoteNode other;
            var project = BuildProject(out note, out other);
            var constant = new ConstantNode { Id = Guid.NewGuid(), Literal = "1" };
            project.Sheets[0].Nodes.Add(constant);

            Assert.Throws<InvalidOperationException>(() =>
                NoteEditingLogic.Apply(project, constant.Id, "Updated"));

            Assert.Equal("1", constant.Literal);
        }

        private static DiagramProject BuildProject(out NoteNode note, out NoteNode other)
        {
            var project = new DiagramProject();
            note = new NoteNode
            {
                Text = "Original",
                Title = "Original title",
                X = 10.0,
                Y = 20.0
            };
            other = new NoteNode
            {
                Text = "Other",
                Title = "Other"
            };

            var first = new CallSheet { Name = "FC_First", Nodes = new List<CallNode> { note } };
            var second = new CallSheet { Name = "FC_Second", Nodes = new List<CallNode> { other } };
            project.Sheets.Add(first);
            project.Sheets.Add(second);
            return project;
        }
    }
}
