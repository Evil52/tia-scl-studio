using System;
using System.Collections.Generic;
using System.Linq;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using Xunit;

namespace TiaSclStudio.Core.Tests
{
    /// <summary>
    /// The editor, model validator and SCL importer must all interpret a PLC
    /// type in exactly the same way. These tests lock down that shared contract:
    /// unknown text fails closed, while legacy spellings are normalized without
    /// losing the stable identity of a referenced UDT.
    /// </summary>
    public sealed class PlcDataTypesTests
    {
        [Theory]
        [InlineData(" bool ", "Bool")]
        [InlineData("DWORD", "DWord")]
        [InlineData("tod", "Time_Of_Day")]
        [InlineData("LTOD", "LTime_Of_Day")]
        [InlineData("dt", "Date_And_Time")]
        [InlineData(" string [ 42 ] ", "String[42]")]
        [InlineData("WSTRING[16382]", "WString[16382]")]
        [InlineData("array [ -2 .. 4 ] of uint", "Array[-2..4] of UInt")]
        [InlineData("ARRAY[0..1, 3..5] OF Bool", "Array[0..1, 3..5] of Bool")]
        public void ResolvesSupportedSpellingsToOneCanonicalType(string input, string expected)
        {
            string canonical;
            Guid udtId;

            var resolved = PlcDataTypes.TryResolve(
                input,
                new UdtDefinition[0],
                false,
                out canonical,
                out udtId);

            Assert.True(resolved);
            Assert.Equal(expected, canonical);
            Assert.Equal(Guid.Empty, udtId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("UnknownType")]
        [InlineData("Bool; Injected")]
        [InlineData("Bool\r\nInt")]
        [InlineData("String[0]")]
        [InlineData("String[255]")]
        [InlineData("WString[16383]")]
        [InlineData("Array[4..2] of Bool")]
        [InlineData("Array[0..1] of UnknownType")]
        [InlineData("Array[0..1] of Void")]
        [InlineData("Array[0..1,0..1,0..1,0..1,0..1,0..1,0..1] of Bool")]
        public void RejectsUnknownMalformedAndInjectableTypeText(string input)
        {
            string canonical;
            Guid udtId;

            var resolved = PlcDataTypes.TryResolve(
                input,
                new UdtDefinition[0],
                false,
                out canonical,
                out udtId);

            Assert.False(resolved);
            Assert.Equal(string.Empty, canonical);
            Assert.Equal(Guid.Empty, udtId);
        }

        [Fact]
        public void VoidIsAvailableOnlyWhenTheCallerAllowsAFunctionReturn()
        {
            string canonical;
            Guid udtId;

            Assert.False(PlcDataTypes.TryResolve(
                "Void", new UdtDefinition[0], false, out canonical, out udtId));
            Assert.True(PlcDataTypes.TryResolve(
                " void ", new UdtDefinition[0], true, out canonical, out udtId));
            Assert.Equal("Void", canonical);
            Assert.Equal(Guid.Empty, udtId);

            Assert.False(PlcDataTypes.TryResolve(
                "Array[0..1] of Void",
                new UdtDefinition[0],
                true,
                out canonical,
                out udtId));
        }

        [Theory]
        [InlineData("ton_time", "TON_TIME")]
        [InlineData(" TOF_TIME ", "TOF_TIME")]
        [InlineData("tp_time", "TP_TIME")]
        [InlineData("TONR_TIME", "TONR_TIME")]
        public void ResolvesSystemTimerInstancesSeparatelyFromScalarTypes(
            string input,
            string expected)
        {
            string canonical;
            Guid udtId;

            Assert.True(PlcDataTypes.TryResolveSystemBlockInstanceType(input, out canonical));
            Assert.Equal(expected, canonical);
            Assert.False(PlcDataTypes.TryResolve(
                input,
                new UdtDefinition[0],
                false,
                out canonical,
                out udtId));
            Assert.DoesNotContain(expected, PlcDataTypes.BuiltInTypes);
        }

        [Theory]
        [InlineData("MotorState")]
        [InlineData("motorstate")]
        [InlineData("\"MotorState\"")]
        [InlineData(" \"MOTORSTATE\" ")]
        [InlineData("Array[0..3] of MotorState")]
        public void ResolvesLegacyAndQuotedUdtReferencesToDeclaredIdentity(string input)
        {
            var udt = Udt("MotorState");
            string canonical;
            Guid udtId;

            var resolved = PlcDataTypes.TryResolve(
                input,
                new[] { udt },
                false,
                out canonical,
                out udtId);

            Assert.True(resolved);
            Assert.Equal(udt.Id, udtId);
            if (input.IndexOf("Array", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Assert.Equal("Array[0..3] of \"MotorState\"", canonical);
            }
            else
            {
                Assert.Equal("\"MotorState\"", canonical);
            }
        }

        [Fact]
        public void AnAmbiguousDuplicateUdtNameDoesNotResolve()
        {
            string canonical;
            Guid udtId;

            Assert.False(PlcDataTypes.TryResolve(
                "State",
                new[] { Udt("State"), Udt("STATE") },
                false,
                out canonical,
                out udtId));
        }

        [Fact]
        public void SelectableTypesAreDeterministicDistinctAndDoNotMutateUdts()
        {
            var second = Udt("Z_Type");
            var first = Udt("A_Type");
            var duplicate = Udt("a_type");
            var udts = new List<UdtDefinition> { second, first, duplicate };
            var original = udts.Select(item => item.Id).ToArray();

            var choices = PlcDataTypes.GetSelectableTypes(udts, true);

            Assert.Equal("Void", choices[0]);
            Assert.Contains("Bool", choices);
            Assert.Equal(1, choices.Count(item =>
                string.Equals(item, "\"A_Type\"", StringComparison.OrdinalIgnoreCase)));
            Assert.True(
                choices.IndexOf("\"A_Type\"") < choices.IndexOf("\"Z_Type\""),
                "UDT choices are not sorted by name.");
            Assert.Equal(original, udts.Select(item => item.Id).ToArray());
        }

        [Fact]
        public void FindsAReferencedUdtThroughNestedArrayTypes()
        {
            var udt = Udt("Payload");
            UdtDefinition referenced;

            Assert.True(PlcDataTypes.TryGetReferencedUdt(
                "Array[0..1] of Array[-1..1] of payload",
                new[] { udt },
                out referenced));
            Assert.Same(udt, referenced);
        }

        [Theory]
        [InlineData("OldType", "\"NewType\"")]
        [InlineData("\"OLDTYPE\"", "\"NewType\"")]
        [InlineData("Array[0 .. 2] of OldType", "Array[0..2] of \"NewType\"")]
        [InlineData("Array[0..1] of Array[2..3] of oldtype", "Array[0..1] of Array[2..3] of \"NewType\"")]
        public void RenameRewritesOnlyAnExactUdtReference(string input, string expected)
        {
            Assert.Equal(
                expected,
                PlcDataTypes.RewriteUdtReference(input, "OldType", "NewType"));
        }

        [Theory]
        [InlineData("OldTypeSuffix")]
        [InlineData("PrefixOldType")]
        [InlineData("String[20]")]
        [InlineData("Array[0..1] of OldTypeSuffix")]
        public void RenameDoesNotRewriteSubstringsOrOtherTypes(string input)
        {
            Assert.Equal(input, PlcDataTypes.RewriteUdtReference(input, "OldType", "NewType"));
        }

        [Fact]
        public void OrdersNestedUdtsDependencyFirstWithoutMutatingInput()
        {
            var root = Udt("Root", Member("Middle", "Middle"));
            var unrelated = Udt("Unrelated", Member("Flag", "Bool"));
            var leaf = Udt("Leaf", Member("Value", "Int"));
            var middle = Udt("Middle", Member("Leaves", "Array[0..2] of Leaf"));
            var input = new List<UdtDefinition> { root, unrelated, leaf, middle };
            var original = input.Select(item => item.Id).ToArray();

            var ordered = PlcDataTypes.OrderUdtsByDependency(input);

            Assert.Equal(
                new[] { "Leaf", "Middle", "Root", "Unrelated" },
                ordered.Select(item => item.Name).ToArray());
            Assert.Equal(original, input.Select(item => item.Id).ToArray());
        }

        [Fact]
        public void DependencyOrderRejectsAnUnresolvedMemberType()
        {
            var udt = Udt("Root", Member("Missing", "MissingType"));

            Assert.Throws<InvalidOperationException>(
                () => PlcDataTypes.OrderUdtsByDependency(new[] { udt }));
        }

        [Fact]
        public void DependencyOrderRejectsSelfAndMutualCycles()
        {
            var self = Udt("Self", Member("Again", "Self"));
            Assert.Throws<InvalidOperationException>(
                () => PlcDataTypes.OrderUdtsByDependency(new[] { self }));

            var left = Udt("Left", Member("Right", "Right"));
            var right = Udt("Right", Member("Left", "Left"));
            Assert.Throws<InvalidOperationException>(
                () => PlcDataTypes.OrderUdtsByDependency(new[] { left, right }));
        }

        [Fact]
        public void DependencyOrderRejectsANullSequence()
        {
            Assert.Throws<ArgumentNullException>(
                () => PlcDataTypes.OrderUdtsByDependency(null));
        }

        private static UdtDefinition Udt(string name, params UdtMember[] members)
        {
            var udt = new UdtDefinition { Name = name };
            udt.Members.AddRange(members ?? new UdtMember[0]);
            return udt;
        }

        private static UdtMember Member(string name, string dataType)
        {
            return new UdtMember(name, dataType);
        }
    }
}
