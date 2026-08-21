using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

[assembly: AssemblyTitle("TiaSclStudio.EndToEnd.Tests")]
[assembly: AssemblyDescription("End-to-end tests that drive the shipped WPF application and the self-test executable.")]
[assembly: AssemblyProduct("TIA SCL Studio")]
[assembly: ComVisible(false)]
[assembly: Guid("2F1A8E10-4A62-4C4E-9C1D-6C0E2B1A0006")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

// WPF allows one Application per process and the window under test is not
// thread safe, so the whole assembly runs sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
