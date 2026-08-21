using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using TiaSclStudio.Core.Model;
using TiaSclStudio.EndToEnd.Tests;
using TiaSclStudio.TestSupport;
using Xunit;

namespace TiaSclStudio.App
{
    [Collection(WpfCollection.Name)]
    public sealed class DataTypePickerUiTests
    {
        private readonly WpfHost _host;

        public DataTypePickerUiTests(WpfHost host)
        {
            _host = host;
        }

        [Fact]
        public void BlockEditorUsesClosedPickersWithBuiltInsUdtsAndContextualVoid()
        {
            _host.Invoke(() =>
            {
                var plant = ModelBuilder.Plant();
                plant.DataTypes.Add(ModelBuilder.Udt("Payload", new UdtMember("Value", "Int")));
                var block = ModelBuilder.Function(
                    "FC_Process",
                    "payload",
                    ModelBuilder.Input("Items", "Array[0..3] of Payload"));
                var window = new BlockEditorWindow(block, plant, false);
                try
                {
                    var returnPicker = MainWindowProbe.Element<ComboBox>(window, "ReturnTypeComboBox");
                    var dataTypeColumn = MainWindowProbe.Field<DataGridComboBoxColumn>(window, "DataTypeColumn");
                    var returnChoices = Choices(returnPicker.ItemsSource);
                    var memberChoices = Choices(dataTypeColumn.ItemsSource);

                    Assert.False(returnPicker.IsEditable);
                    AssertClosedComboBoxStyle(dataTypeColumn.ElementStyle);
                    AssertClosedComboBoxStyle(dataTypeColumn.EditingElementStyle);
                    Assert.Contains("Bool", returnChoices);
                    Assert.Contains("\"Payload\"", returnChoices);
                    Assert.Contains("Void", returnChoices);
                    Assert.Contains("Bool", memberChoices);
                    Assert.Contains("\"Payload\"", memberChoices);
                    Assert.Contains("Array[0..3] of \"Payload\"", memberChoices);
                    Assert.DoesNotContain("Void", memberChoices);
                    Assert.Equal("\"Payload\"", returnPicker.SelectedItem);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void UdtEditorExcludesVoidAndSelfButIncludesOtherUdtsAndExistingCompoundTypes()
        {
            _host.Invoke(() =>
            {
                var payload = ModelBuilder.Udt(
                    "Payload",
                    new UdtMember("Title", "String[42]"));
                var metadata = ModelBuilder.Udt("Metadata", new UdtMember("Code", "DInt"));
                var window = new UdtEditorWindow(payload, new[] { payload, metadata }, false);
                try
                {
                    var dataTypeColumn = MainWindowProbe.Field<DataGridComboBoxColumn>(
                        window,
                        "MemberDataTypeColumn");
                    var choices = Choices(dataTypeColumn.ItemsSource);

                    AssertClosedComboBoxStyle(dataTypeColumn.ElementStyle);
                    AssertClosedComboBoxStyle(dataTypeColumn.EditingElementStyle);
                    Assert.Contains("Bool", choices);
                    Assert.Contains("\"Metadata\"", choices);
                    Assert.Contains("String[42]", choices);
                    Assert.DoesNotContain("\"Payload\"", choices);
                    Assert.DoesNotContain("Void", choices);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void AnUnknownLegacyBlockTypeRemainsBlockedByTheSaveValidationGate()
        {
            _host.Invoke(() =>
            {
                var plant = ModelBuilder.Plant();
                var block = ModelBuilder.FunctionBlock(
                    "FB_Legacy",
                    ModelBuilder.Input("Value", "ArbitraryFreeText"));
                var window = new BlockEditorWindow(block, plant, false);
                try
                {
                    var messages = (IList<string>)MainWindowProbe.Call(window, "ValidateWorkingCopy");

                    Assert.NotEmpty(messages);
                    Assert.DoesNotContain(
                        "ArbitraryFreeText",
                        Choices(MainWindowProbe.Field<DataGridComboBoxColumn>(window, "DataTypeColumn").ItemsSource));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void AnUnknownLegacyUdtMemberTypeRemainsBlockedByTheSaveValidationGate()
        {
            _host.Invoke(() =>
            {
                var dataType = ModelBuilder.Udt(
                    "Payload",
                    new UdtMember("Value", "ArbitraryFreeText"));
                var window = new UdtEditorWindow(dataType, new[] { dataType }, false);
                try
                {
                    var messages = (IList<string>)MainWindowProbe.Call(window, "ValidateWorkingCopy");

                    Assert.NotEmpty(messages);
                    Assert.DoesNotContain(
                        "ArbitraryFreeText",
                        Choices(MainWindowProbe.Field<DataGridComboBoxColumn>(window, "MemberDataTypeColumn").ItemsSource));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static IList<string> Choices(System.Collections.IEnumerable values)
        {
            return values.Cast<object>().Select(value => value == null ? string.Empty : value.ToString()).ToList();
        }

        private static void AssertClosedComboBoxStyle(System.Windows.Style style)
        {
            var comboBox = new ComboBox { Style = style };
            Assert.False(comboBox.IsEditable);
        }
    }
}
