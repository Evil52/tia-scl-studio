using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.Openness.Legacy.V17
{
    internal sealed class LegacyV17Gateway : ITiaGateway
    {
        private static readonly Regex SafeIdentifierPattern = new Regex(
            "^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex OwnershipMarkerPattern = new Regex(
            "^[A-Za-z_][A-Za-z0-9_]{0,7}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex S71200CpuTypePattern = new Regex(
            "\\bCPU\\s+12[0-9]{2}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex S71500CpuTypePattern = new Regex(
            "\\bCPU\\s+15[0-9]{2}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly object PreservedSessionsLock = new object();
        private static readonly IList<TiaPortal> PreservedSessions = new List<TiaPortal>();
        private readonly object syncRoot = new object();
        private readonly OpennessInstallation installation;
        private readonly SiemensAssemblyResolver assemblyResolver;
        private TiaPortal tiaPortal;
        private Project project;
        private PlcSoftware plcSoftware;
        private TiaConnectionRequest connectionRequest;
        private TiaGatewayStatus status;
        private PreviewTicket previewTicket;
        private bool projectOpenedByAdapter;
        private bool portalStartedByAdapter;
        private bool committedUnsavedChanges;
        private TiaPortalMode? connectedPortalMode;
        private bool disposed;
        private string selectedDeviceName = string.Empty;
        private string selectedSoftwareName = string.Empty;
        private DeviceItem selectedCpuItem;

        public LegacyV17Gateway(
            OpennessInstallation installation,
            SiemensAssemblyResolver assemblyResolver)
        {
            this.installation = installation ?? throw new ArgumentNullException(nameof(installation));
            this.assemblyResolver = assemblyResolver ?? throw new ArgumentNullException(nameof(assemblyResolver));
            status = CreateStatus(
                false,
                false,
                "V17 adapter is ready but not connected.",
                new OpennessDiagnostic[0]);
        }

        public TiaGatewayStatus GetStatus()
        {
            lock (syncRoot)
            {
                return status;
            }
        }

        public TiaGatewayStatus Connect(TiaConnectionRequest request)
        {
            lock (syncRoot)
            {
                var diagnostics = new List<OpennessDiagnostic>();
                InvalidatePreview();

                if (disposed)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.ConnectionFailed,
                        "The V17 gateway has already been disposed."));
                    status = CreateStatus(false, false, "Disposed V17 adapter", diagnostics);
                    return status;
                }

                if (request == null)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.ConnectionRequestMissing,
                        "A TIA connection request is required."));
                    status = CreateStatus(false, false, "Connection request rejected", diagnostics);
                    return status;
                }

                try
                {
                    if (!ReleaseConnection(diagnostics, false))
                    {
                        status = CreateStatus(
                            true,
                            project != null && plcSoftware != null,
                            BuildConnectionDescription(),
                            diagnostics);
                        return status;
                    }

                    connectionRequest = request;

                    if (request.Mode == TiaConnectionMode.AttachToRunning)
                    {
                        portalStartedByAdapter = false;
                        tiaPortal = AttachToRunningPortal(request, diagnostics);
                    }
                    else
                    {
                        var portalMode = request.Mode == TiaConnectionMode.StartWithUserInterface
                            ? TiaPortalMode.WithUserInterface
                            : TiaPortalMode.WithoutUserInterface;
                        portalStartedByAdapter = true;
                        connectedPortalMode = portalMode;
                        tiaPortal = new TiaPortal(portalMode);
                    }

                    if (tiaPortal == null)
                    {
                        status = CreateStatus(false, false, "TIA Portal connection failed", diagnostics);
                        return status;
                    }

                    if (connectedPortalMode.HasValue &&
                        connectedPortalMode.Value == TiaPortalMode.WithoutUserInterface)
                    {
                        diagnostics.Add(new OpennessDiagnostic(
                            OpennessDiagnosticCodes.ConnectedHeadless,
                            DiagnosticSeverity.Warning,
                            "The selected TIA Portal process is headless. Mutating exports require explicit project save, and the application must not close while unsaved changes remain."));
                    }

                    if (!string.IsNullOrWhiteSpace(request.ProjectPath))
                    {
                        if (!EnsureProject(request.ProjectPath, diagnostics))
                        {
                            status = CreateStatus(true, false, "Connected, but project selection failed", diagnostics);
                            return status;
                        }
                    }
                    else
                    {
                        ReuseOnlyOpenProject(diagnostics);
                    }

                    if (project != null)
                    {
                        TrySelectPlc(
                            request.TargetDeviceName,
                            request.TargetSoftwareName,
                            diagnostics);
                    }

                    var canExport = project != null && plcSoftware != null &&
                        !diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
                    var description = BuildConnectionDescription();
                    status = CreateStatus(true, canExport, description, diagnostics);
                    return status;
                }
                catch (NonRecoverableException exception)
                {
                    return HandleSessionLost(
                        diagnostics,
                        "TIA Portal closed the session while connecting: " + exception.Message,
                        exception);
                }
                catch (EngineeringSecurityException exception)
                {
                    return HandleAccessDenied(
                        diagnostics,
                        "TIA Openness access was denied while connecting: " + exception.Message,
                        exception);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.ConnectionFailed,
                        "TIA Portal connection failed: " + exception.Message,
                        string.Empty,
                        exception));
                    bool released;
                    try
                    {
                        released = ReleaseConnection(diagnostics, false);
                    }
                    catch (NonRecoverableException releaseException)
                    {
                        return HandleSessionLost(
                            diagnostics,
                            "TIA Portal closed the session while cleaning up a failed connection: " +
                            releaseException.Message,
                            releaseException);
                    }
                    catch (EngineeringSecurityException releaseException)
                    {
                        return HandleAccessDenied(
                            diagnostics,
                            "TIA Openness denied access while cleaning up a failed connection: " +
                            releaseException.Message,
                            releaseException);
                    }

                    status = CreateStatus(
                        !released && tiaPortal != null,
                        false,
                        released ? "TIA Portal connection failed" : BuildConnectionDescription(),
                        diagnostics);
                    return status;
                }
            }
        }

        public TiaHardwareIoCatalog ReadHardwareIoCatalog()
        {
            lock (syncRoot)
            {
                var diagnostics = new List<OpennessDiagnostic>();
                if (disposed)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.HardwareIoCatalogUnavailable,
                        "The V17 gateway has already been disposed."));
                    return UnavailableHardwareIoCatalog(diagnostics);
                }

                if (tiaPortal == null || project == null || plcSoftware == null || selectedCpuItem == null)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.HardwareIoCatalogUnavailable,
                        "Connect to a project and select one PLC before reading configured hardware I/O channels."));
                    return UnavailableHardwareIoCatalog(diagnostics);
                }

                try
                {
                    return BuildHardwareIoCatalog(diagnostics);
                }
                catch (NonRecoverableException exception)
                {
                    HandleSessionLost(
                        diagnostics,
                        "TIA Portal closed the session while reading configured hardware I/O: " + exception.Message,
                        exception);
                    return UnavailableHardwareIoCatalog(diagnostics);
                }
                catch (EngineeringSecurityException exception)
                {
                    HandleAccessDenied(
                        diagnostics,
                        "TIA Openness access was denied while reading configured hardware I/O: " + exception.Message,
                        exception);
                    return UnavailableHardwareIoCatalog(diagnostics);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.HardwareIoCatalogReadFailed,
                        "Configured hardware I/O could not be read: " + exception.Message,
                        selectedDeviceName,
                        exception));
                    return UnavailableHardwareIoCatalog(diagnostics);
                }
            }
        }

        public TiaLibrarySnapshot ReadLibrarySnapshot(CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                var diagnostics = new List<OpennessDiagnostic>();
                var sources = new List<TiaLibrarySource>();
                if (disposed)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.LibrarySnapshotUnavailable,
                        "The V17 gateway has already been disposed."));
                    return UnavailableLibrarySnapshot(diagnostics);
                }

                if (tiaPortal == null || project == null || plcSoftware == null || selectedCpuItem == null)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.LibrarySnapshotUnavailable,
                        "Connect to a project and select one PLC before reading FB, FC and PLC data-type sources."));
                    return UnavailableLibrarySnapshot(diagnostics);
                }

                try
                {
                    return BuildLibrarySnapshot(cancellationToken, sources, diagnostics);
                }
                catch (OperationCanceledException)
                {
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.LibrarySnapshotCancelled,
                        DiagnosticSeverity.Information,
                        "PLC library readback was cancelled between objects. The returned snapshot is partial.",
                        selectedSoftwareName));
                    return AvailableLibrarySnapshot(false, sources, diagnostics);
                }
                catch (NonRecoverableException exception)
                {
                    HandleSessionLost(
                        diagnostics,
                        "TIA Portal closed the session while reading the PLC library: " + exception.Message,
                        exception);
                    return UnavailableLibrarySnapshot(diagnostics, sources);
                }
                catch (EngineeringSecurityException exception)
                {
                    HandleAccessDenied(
                        diagnostics,
                        "TIA Openness access was denied while reading the PLC library: " + exception.Message,
                        exception);
                    return UnavailableLibrarySnapshot(diagnostics, sources);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.LibrarySnapshotReadFailed,
                        "PLC library readback failed: " + exception.Message,
                        selectedSoftwareName,
                        exception));
                    return AvailableLibrarySnapshot(false, sources, diagnostics);
                }
            }
        }

        public TiaExportPreview PreviewExport(TiaExportRequest request)
        {
            lock (syncRoot)
            {
                InvalidatePreview();
                if (disposed)
                {
                    return FailedPreview(Error(
                        OpennessDiagnosticCodes.PreflightFailed,
                        "The V17 gateway has already been disposed."));
                }

                if (request == null)
                {
                    return FailedPreview(Error(
                        OpennessDiagnosticCodes.ExportRequestMissing,
                        "An export request is required."));
                }

                PreflightPlan plan;
                try
                {
                    plan = BuildPreflight(request);
                }
                catch (NonRecoverableException exception)
                {
                    var diagnostics = new List<OpennessDiagnostic>();
                    HandleSessionLost(
                        diagnostics,
                        "TIA Portal closed the session during export preflight: " + exception.Message,
                        exception);
                    return new TiaExportPreview(false, string.Empty, null, null, diagnostics);
                }
                catch (EngineeringSecurityException exception)
                {
                    var diagnostics = new List<OpennessDiagnostic>();
                    HandleAccessDenied(
                        diagnostics,
                        "TIA Openness access was denied during export preflight: " + exception.Message,
                        exception);
                    return new TiaExportPreview(false, string.Empty, null, null, diagnostics);
                }
                catch (Exception exception)
                {
                    return FailedPreview(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.PreflightFailed,
                        "TIA export preflight failed: " + exception.Message,
                        string.Empty,
                        exception));
                }

                var token = string.Empty;
                if (plan.CanExecute)
                {
                    token = CreateConfirmationToken();
                    previewTicket = new PreviewTicket(
                        token,
                        plan.RequestFingerprint,
                        plan.PlanFingerprint);
                }

                return plan.ToPreview(token);
            }
        }

        public TiaExportResult Export(TiaExportRequest request, string confirmationToken)
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    return Rejected(Error(
                        OpennessDiagnosticCodes.ExportFailed,
                        "The V17 gateway has already been disposed."));
                }

                if (request == null)
                {
                    return Rejected(Error(
                        OpennessDiagnosticCodes.ExportRequestMissing,
                        "An export request is required."));
                }

                if (string.IsNullOrWhiteSpace(confirmationToken))
                {
                    return Rejected(Error(
                        OpennessDiagnosticCodes.ConfirmationRequired,
                        "A confirmation token from a successful current preview is required."));
                }

                var ticket = previewTicket;
                InvalidatePreview();
                if (ticket == null ||
                    !string.Equals(ticket.Token, confirmationToken, StringComparison.Ordinal))
                {
                    return Rejected(Error(
                        OpennessDiagnosticCodes.ConfirmationInvalid,
                        "The confirmation token is invalid or has already been consumed."));
                }

                try
                {
                    return ExecuteExport(request, ticket);
                }
                catch (NonRecoverableException exception)
                {
                    var diagnostics = new List<OpennessDiagnostic>();
                    HandleSessionLost(
                        diagnostics,
                        "TIA Portal closed the session during export: " + exception.Message,
                        exception);
                    return new TiaExportResult(
                        TiaExportOutcome.Failed,
                        0,
                        false,
                        diagnostics);
                }
                catch (EngineeringSecurityException exception)
                {
                    var diagnostics = new List<OpennessDiagnostic>();
                    HandleAccessDenied(
                        diagnostics,
                        "TIA Openness access was denied during export: " + exception.Message,
                        exception);
                    return new TiaExportResult(
                        TiaExportOutcome.Failed,
                        0,
                        false,
                        diagnostics);
                }
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                InvalidatePreview();
                try
                {
                    ReleaseConnection(null, true);
                }
                catch (NonRecoverableException)
                {
                    ResetConnectionStateWithoutApiCalls();
                }
                catch (EngineeringSecurityException)
                {
                    PreserveCurrentSessionWithoutApiCalls();
                }
                finally
                {
                    assemblyResolver.Dispose();
                    disposed = true;
                    status = CreateStatus(false, false, "Disposed V17 adapter", null);
                }
            }
        }

        private TiaPortal AttachToRunningPortal(
            TiaConnectionRequest request,
            IList<OpennessDiagnostic> diagnostics)
        {
            var allProcesses = TiaPortal.GetProcesses();
            var candidates = allProcesses
                .Where(IsProcessFromSelectedInstallation)
                .ToList();

            if (request.ProcessId.HasValue)
            {
                candidates = candidates
                    .Where(candidate => candidate.Id == request.ProcessId.Value)
                    .ToList();
            }
            else if (!string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                string requestedPath;
                if (TryGetFullPath(request.ProjectPath, out requestedPath))
                {
                    var matchingProject = candidates
                        .Where(candidate => PathsEqual(GetProcessProjectPath(candidate), requestedPath))
                        .ToList();
                    if (matchingProject.Count > 0)
                    {
                        candidates = matchingProject;
                    }
                }
            }

            if (candidates.Count == 0)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProcessNotFound,
                    "No running TIA Portal process from the selected Portal installation matches the request."));
                return null;
            }

            if (candidates.Count > 1)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProcessAmbiguous,
                    "More than one matching TIA Portal process is running. Specify ProcessId. Candidates: " +
                    string.Join(", ", candidates.Select(candidate => candidate.Id.ToString(CultureInfo.InvariantCulture)))));
                return null;
            }

            // TiaPortalProcess.Dispose closes the represented Portal process in
            // this API. Process handles returned by GetProcesses are therefore
            // intentionally not disposed by the adapter.
            connectedPortalMode = candidates[0].Mode;
            return candidates[0].Attach();
        }

        private bool IsProcessFromSelectedInstallation(TiaPortalProcess process)
        {
            try
            {
                return process != null && process.Path != null &&
                    IsPathInside(process.Path.FullName, installation.RootPath);
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetProcessProjectPath(TiaPortalProcess process)
        {
            try
            {
                return process.ProjectPath == null ? string.Empty : process.ProjectPath.FullName;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private void ReuseOnlyOpenProject(IList<OpennessDiagnostic> diagnostics)
        {
            var projects = tiaPortal.Projects.ToList();
            if (projects.Count == 1)
            {
                project = projects[0];
                projectOpenedByAdapter = false;
            }
            else if (projects.Count > 1)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProjectPathRequired,
                    "More than one project is open. Specify ProjectPath explicitly."));
            }
        }

        private bool EnsureProject(string requestedProjectPath, IList<OpennessDiagnostic> diagnostics)
        {
            string requestedFullPath;
            if (!TryGetFullPath(requestedProjectPath, out requestedFullPath))
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProjectFileMissing,
                    "The TIA project path is invalid.",
                    requestedProjectPath));
                return false;
            }

            if (project != null)
            {
                var currentPath = GetProjectPath(project);
                if (PathsEqual(currentPath, requestedFullPath))
                {
                    return true;
                }

                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProjectOpenFailed,
                    "A different project is already selected by this gateway. Connect again to switch projects.",
                    currentPath));
                return false;
            }

            var openProject = tiaPortal.Projects.FirstOrDefault(
                candidate => PathsEqual(GetProjectPath(candidate), requestedFullPath));
            if (openProject != null)
            {
                project = openProject;
                projectOpenedByAdapter = false;
                return true;
            }

            var expectedExtension = ".ap" + installation.PortalVersion.Major.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(Path.GetExtension(requestedFullPath), expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProjectVersionMismatch,
                    "The selected Portal V" + installation.PortalVersion.Major.ToString(CultureInfo.InvariantCulture) +
                    " can open projects with extension '" + expectedExtension + "' through this adapter. " +
                    "Open or migrate the project manually with the matching TIA Portal version; automatic upgrade is disabled.",
                    requestedFullPath));
                return false;
            }

            if (!File.Exists(requestedFullPath))
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProjectFileMissing,
                    "The TIA project file does not exist.",
                    requestedFullPath));
                return false;
            }

            try
            {
                project = tiaPortal.Projects.Open(new FileInfo(requestedFullPath));
                projectOpenedByAdapter = true;
                return true;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExceptionDiagnostic(
                    OpennessDiagnosticCodes.ProjectOpenFailed,
                    "Cannot open the TIA project: " + exception.Message,
                    requestedFullPath,
                    exception));
                return false;
            }
        }

        private bool TrySelectPlc(
            string targetDeviceName,
            string targetSoftwareName,
            IList<OpennessDiagnostic> diagnostics)
        {
            plcSoftware = null;
            selectedCpuItem = null;
            selectedDeviceName = string.Empty;
            selectedSoftwareName = string.Empty;

            if (project == null)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.ProjectPathRequired,
                    "A project must be selected before selecting a PLC."));
                return false;
            }

            var candidates = EnumeratePlcCandidates(project).ToList();
            if (!string.IsNullOrWhiteSpace(targetDeviceName))
            {
                candidates = candidates.Where(candidate =>
                    string.Equals(candidate.DeviceName, targetDeviceName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(targetSoftwareName))
            {
                candidates = candidates.Where(candidate =>
                    string.Equals(candidate.SoftwareName, targetSoftwareName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (candidates.Count == 0)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.PlcNotFound,
                    "No PLC software matches the requested device/software selection."));
                return false;
            }

            if (candidates.Count > 1)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.PlcAmbiguous,
                    "More than one PLC matches the request. Specify TargetDeviceName and TargetSoftwareName. Candidates: " +
                    string.Join(", ", candidates.Select(candidate => candidate.DisplayName))));
                return false;
            }

            var selected = candidates[0];
            plcSoftware = selected.Software;
            selectedCpuItem = selected.CpuItem;
            selectedDeviceName = selected.DeviceName;
            selectedSoftwareName = selected.SoftwareName;
            return true;
        }

        private static IEnumerable<PlcCandidate> EnumeratePlcCandidates(Project selectedProject)
        {
            var candidates = new List<PlcCandidate>();
            foreach (var device in selectedProject.Devices)
            {
                AddDeviceCandidates(device, candidates);
            }

            foreach (var group in selectedProject.DeviceGroups)
            {
                AddDeviceGroupCandidates(group, candidates);
            }

            return candidates;
        }

        private static void AddDeviceGroupCandidates(
            DeviceUserGroup group,
            ICollection<PlcCandidate> candidates)
        {
            foreach (var device in group.Devices)
            {
                AddDeviceCandidates(device, candidates);
            }

            foreach (var child in group.Groups)
            {
                AddDeviceGroupCandidates(child, candidates);
            }
        }

        private static void AddDeviceCandidates(Device device, ICollection<PlcCandidate> candidates)
        {
            foreach (var item in device.DeviceItems)
            {
                AddDeviceItemCandidates(device.Name, item, candidates);
            }
        }

        private static void AddDeviceItemCandidates(
            string deviceName,
            DeviceItem item,
            ICollection<PlcCandidate> candidates)
        {
            try
            {
                var container = item.GetService<SoftwareContainer>();
                var software = container == null ? null : container.Software as PlcSoftware;
                if (software != null && !candidates.Any(candidate => ReferenceEquals(candidate.Software, software)))
                {
                    candidates.Add(new PlcCandidate(deviceName, item.Name, item, software));
                }
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception)
            {
                // Most hardware items do not provide SoftwareContainer. A
                // provider-specific failure on one item must not hide other PLCs.
            }

            foreach (var child in item.DeviceItems)
            {
                AddDeviceItemCandidates(deviceName, child, candidates);
            }
        }

        private TiaHardwareIoCatalog BuildHardwareIoCatalog(
            IList<OpennessDiagnostic> diagnostics)
        {
            var cpuTypeIdentifier = selectedCpuItem.TypeIdentifier ?? string.Empty;
            var cpuTypeName = TryReadStringAttribute(selectedCpuItem, "TypeName");
            var cpuModel = FirstNonEmpty(cpuTypeName, selectedCpuItem.Name, selectedSoftwareName);
            var cpuFamily = ClassifyCpuFamily(cpuTypeName, cpuTypeIdentifier);

            var addressController = selectedCpuItem.GetService<AddressController>();
            if (addressController == null)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.HardwareIoCatalogUnavailable,
                    "The selected PLC does not expose the V17 AddressController service. Configured hardware channels cannot be verified safely.",
                    selectedDeviceName + "/" + selectedCpuItem.Name));
                return new TiaHardwareIoCatalog(
                    false,
                    cpuFamily,
                    cpuModel,
                    cpuTypeIdentifier,
                    null,
                    diagnostics);
            }

            var itemPaths = BuildDeviceItemPathMap(project);
            var registeredItems = new List<DeviceItem>();
            foreach (var registeredAddress in addressController.RegisteredAddresses)
            {
                if (registeredAddress.IoType != AddressIoType.Input &&
                    registeredAddress.IoType != AddressIoType.Output)
                {
                    continue;
                }

                var deviceItem = registeredAddress.Parent as DeviceItem;
                if (deviceItem == null)
                {
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.HardwareIoChannelUnsupported,
                        DiagnosticSeverity.Warning,
                        "A registered PLC I/O address has no DeviceItem parent and was skipped.",
                        selectedDeviceName));
                    continue;
                }

                if (!registeredItems.Any(existing => ReferenceEquals(existing, deviceItem) || existing.Equals(deviceItem)))
                {
                    registeredItems.Add(deviceItem);
                }
            }

            var channels = new List<TiaHardwareIoChannel>();
            foreach (var item in registeredItems)
            {
                string itemPath;
                if (!itemPaths.TryGetValue(item, out itemPath))
                {
                    itemPath = selectedDeviceName + "/" + item.Name;
                }

                var itemTypeIdentifier = item.TypeIdentifier ?? string.Empty;
                var itemTypeName = TryReadStringAttribute(item, "TypeName");
                foreach (var channel in item.Channels)
                {
                    try
                    {
                        string unsupportedReason;
                        var snapshot = TryCreateHardwareIoChannel(
                            channel,
                            itemPath,
                            itemTypeIdentifier,
                            itemTypeName,
                            out unsupportedReason);
                        if (snapshot != null)
                        {
                            channels.Add(snapshot);
                        }
                        else if (!string.IsNullOrWhiteSpace(unsupportedReason))
                        {
                            diagnostics.Add(new OpennessDiagnostic(
                                OpennessDiagnosticCodes.HardwareIoChannelUnsupported,
                                DiagnosticSeverity.Warning,
                                unsupportedReason,
                                itemPath + "/channel " + channel.Number.ToString(CultureInfo.InvariantCulture)));
                        }
                    }
                    catch (NonRecoverableException)
                    {
                        throw;
                    }
                    catch (EngineeringSecurityException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add(ExceptionDiagnostic(
                            OpennessDiagnosticCodes.HardwareIoChannelUnsupported,
                            "A configured hardware channel was skipped because its exact V17 address or width could not be read: " + exception.Message,
                            itemPath,
                            exception,
                            DiagnosticSeverity.Warning));
                    }
                }
            }

            channels = channels
                .OrderBy(channel => channel.Kind)
                .ThenBy(channel => channel.BitAddress)
                .ThenBy(channel => channel.DeviceItemPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(channel => channel.Number)
                .ToList();

            diagnostics.Add(new OpennessDiagnostic(
                OpennessDiagnosticCodes.HardwareIoCatalogRead,
                DiagnosticSeverity.Information,
                "Configured hardware I/O discovery completed with " +
                channels.Count.ToString(CultureInfo.InvariantCulture) +
                " exact supported channel(s).",
                selectedDeviceName + "/" + selectedSoftwareName));

            return new TiaHardwareIoCatalog(
                true,
                cpuFamily,
                cpuModel,
                cpuTypeIdentifier,
                channels,
                diagnostics);
        }

        private TiaLibrarySnapshot BuildLibrarySnapshot(
            CancellationToken cancellationToken,
            IList<TiaLibrarySource> sources,
            IList<OpennessDiagnostic> diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = CollectLibraryReadCandidates(cancellationToken);
            if (candidates.Count > LegacyLibraryReadbackPolicy.MaximumObjectCount)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.LibrarySnapshotReadFailed,
                    "PLC library readback was rejected because " +
                    candidates.Count.ToString(CultureInfo.InvariantCulture) +
                    " FB/FC/UDT objects exceed the safety limit of " +
                    LegacyLibraryReadbackPolicy.MaximumObjectCount.ToString(CultureInfo.InvariantCulture) + ".",
                    selectedSoftwareName));
                return AvailableLibrarySnapshot(false, sources, diagnostics);
            }

            var duplicateGroups = candidates
                .GroupBy(GetLibraryCollisionKey, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();
            var duplicateKeys = new HashSet<string>(
                duplicateGroups.Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);
            foreach (var duplicateGroup in duplicateGroups)
            {
                diagnostics.Add(Error(
                    OpennessDiagnosticCodes.LibrarySnapshotReadFailed,
                    "More than one PLC object maps to the same local library name '" +
                    duplicateGroup.First().Name + "': " +
                    string.Join(", ", duplicateGroup
                        .Select(item => item.Location)
                        .Distinct(StringComparer.OrdinalIgnoreCase)) +
                    ". The ambiguous objects were not returned.",
                    duplicateGroup.First().Name));
            }

            var isComplete = duplicateKeys.Count == 0;
            long totalSourceBytes = 0;
            foreach (var candidate in candidates
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.GroupPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (duplicateKeys.Contains(GetLibraryCollisionKey(candidate)))
                {
                    continue;
                }

                if (candidate.IsKnowHowProtected)
                {
                    isComplete = false;
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.LibrarySnapshotObjectSkipped,
                        DiagnosticSeverity.Warning,
                        "The PLC object is know-how protected and cannot be read back as SCL.",
                        candidate.Location));
                    continue;
                }

                if (candidate.Kind != TiaLibrarySourceKind.DataType &&
                    !string.Equals(candidate.ProgrammingLanguage, "SCL", StringComparison.OrdinalIgnoreCase))
                {
                    isComplete = false;
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.LibrarySnapshotObjectSkipped,
                        DiagnosticSeverity.Warning,
                        "Only SCL FB/FC blocks can be generated as SCL source; this " +
                        candidate.ProgrammingLanguage + " block was skipped.",
                        candidate.Location));
                    continue;
                }

                try
                {
                    long sourceBytes;
                    var sourceText = GenerateLibrarySource(candidate, diagnostics, out sourceBytes);
                    if (totalSourceBytes + sourceBytes > LegacyLibraryReadbackPolicy.MaximumTotalSourceBytes)
                    {
                        isComplete = false;
                        diagnostics.Add(Error(
                            OpennessDiagnosticCodes.LibrarySnapshotReadFailed,
                            "PLC library readback stopped because generated sources exceed the total safety limit of " +
                            LegacyLibraryReadbackPolicy.MaximumTotalSourceBytes.ToString(CultureInfo.InvariantCulture) +
                            " bytes.",
                            candidate.Location));
                        break;
                    }

                    ValidateGeneratedLibrarySource(candidate, sourceText);
                    totalSourceBytes += sourceBytes;
                    sources.Add(new TiaLibrarySource(
                        candidate.Kind,
                        candidate.Name,
                        candidate.GroupPath,
                        candidate.ProgrammingLanguage,
                        sourceText));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (NonRecoverableException)
                {
                    throw;
                }
                catch (EngineeringSecurityException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    isComplete = false;
                    diagnostics.Add(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.LibrarySnapshotReadFailed,
                        "PLC object source generation failed and the object was skipped: " + exception.Message,
                        candidate.Location,
                        exception));
                }
            }

            diagnostics.Add(new OpennessDiagnostic(
                OpennessDiagnosticCodes.LibrarySnapshotRead,
                DiagnosticSeverity.Information,
                "PLC library readback completed with " +
                sources.Count.ToString(CultureInfo.InvariantCulture) +
                " generated FB/FC/UDT source(s)" + (isComplete ? "." : "; the snapshot is partial."),
                selectedDeviceName + "/" + selectedSoftwareName));
            return AvailableLibrarySnapshot(isComplete, sources, diagnostics);
        }

        private static string GetLibraryCollisionKey(LibraryReadCandidate candidate)
        {
            return LegacyLibraryReadbackPolicy.GetCollisionKey(candidate.Kind, candidate.Name);
        }

        private List<LibraryReadCandidate> CollectLibraryReadCandidates(
            CancellationToken cancellationToken)
        {
            var result = new List<LibraryReadCandidate>();
            AddLibraryScopeCandidates(
                plcSoftware.BlockGroup,
                plcSoftware.TypeGroup,
                plcSoftware.ExternalSourceGroup,
                string.Empty,
                result);

            var unitProvider = plcSoftware.GetService<PlcUnitProvider>();
            if (unitProvider == null || unitProvider.UnitGroup == null)
            {
                return result;
            }

            foreach (var unit in unitProvider.UnitGroup.Units)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (unit == null)
                {
                    continue;
                }

                AddLibraryScopeCandidates(
                    unit.BlockGroup,
                    unit.TypeGroup,
                    unit.ExternalSourceGroup,
                    CombineLibraryGroupPath("Unit", unit.Name),
                    result);
            }

            return result;
        }

        private static void AddLibraryScopeCandidates(
            PlcBlockGroup blockGroup,
            PlcTypeGroup typeGroup,
            PlcExternalSourceSystemGroup externalSourceGroup,
            string scopePath,
            ICollection<LibraryReadCandidate> candidates)
        {
            if (blockGroup != null)
            {
                var blocks = LegacyLibraryReadbackPolicy.WalkGroupTree(
                    blockGroup,
                    group => group.Blocks.Cast<PlcBlock>(),
                    group => group.Groups.Cast<PlcBlockGroup>(),
                    group => group.Name);
                foreach (var entry in blocks)
                {
                    var block = entry.Item;
                    TiaLibrarySourceKind kind;
                    if (block is FB)
                    {
                        kind = TiaLibrarySourceKind.FunctionBlock;
                    }
                    else if (block is FC)
                    {
                        kind = TiaLibrarySourceKind.Function;
                    }
                    else
                    {
                        continue;
                    }

                    candidates.Add(new LibraryReadCandidate(
                        (IGenerateSource)block,
                        externalSourceGroup,
                        kind,
                        block.Name,
                        CombineLibraryGroupPath(scopePath, entry.GroupPath),
                        block.ProgrammingLanguage.ToString(),
                        block.IsKnowHowProtected));
                }
            }

            if (typeGroup == null)
            {
                return;
            }

            var types = LegacyLibraryReadbackPolicy.WalkGroupTree(
                typeGroup,
                group => group.Types.Cast<PlcType>(),
                group => group.Groups.Cast<PlcTypeGroup>(),
                group => group.Name);
            foreach (var entry in types)
            {
                var type = entry.Item as PlcStruct;
                if (type == null)
                {
                    continue;
                }

                candidates.Add(new LibraryReadCandidate(
                    type,
                    externalSourceGroup,
                    TiaLibrarySourceKind.DataType,
                    type.Name,
                    CombineLibraryGroupPath(scopePath, entry.GroupPath),
                    "SCL",
                    type.IsKnowHowProtected));
            }
        }

        private static string CombineLibraryGroupPath(string left, string right)
        {
            var normalizedLeft = (left ?? string.Empty).Trim('/');
            var normalizedRight = (right ?? string.Empty).Trim('/');
            if (string.IsNullOrEmpty(normalizedLeft))
            {
                return normalizedRight;
            }

            return string.IsNullOrEmpty(normalizedRight)
                ? normalizedLeft
                : normalizedLeft + "/" + normalizedRight;
        }

        private static string GenerateLibrarySource(
            LibraryReadCandidate candidate,
            IList<OpennessDiagnostic> diagnostics,
            out long sourceBytes)
        {
            if (candidate.ExternalSourceGroup == null)
            {
                throw new InvalidOperationException(
                    "The PLC object scope does not expose an external-source system group.");
            }

            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "TiaSclStudio-readback-" + Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(
                temporaryDirectory,
                "source" + LegacyLibraryReadbackPolicy.GetSourceExtension(candidate.Kind));
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                // GenerateSource requires a destination that does not exist. The
                // directory is fresh and the source path is intentionally not
                // pre-created.
                candidate.ExternalSourceGroup.GenerateSource(
                    new[] { candidate.SourceObject },
                    new FileInfo(sourcePath),
                    GenerateOptions.None);
                var generatedFile = new FileInfo(sourcePath);
                if (!generatedFile.Exists)
                {
                    throw new InvalidOperationException(
                        "TIA Portal returned from GenerateSource without creating the destination file.");
                }

                sourceBytes = generatedFile.Length;
                if (sourceBytes <= 0 ||
                    sourceBytes > LegacyLibraryReadbackPolicy.MaximumSourceBytesPerObject)
                {
                    throw new InvalidOperationException(
                        "Generated source size " + sourceBytes.ToString(CultureInfo.InvariantCulture) +
                        " bytes is outside the accepted range 1.." +
                        LegacyLibraryReadbackPolicy.MaximumSourceBytesPerObject.ToString(CultureInfo.InvariantCulture) + ".");
                }

                return File.ReadAllText(sourcePath, new UTF8Encoding(false, true));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, true);
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.TemporarySourceCleanupFailed,
                        "A generated readback source was returned, but its temporary directory could not be removed: " +
                        exception.Message,
                        temporaryDirectory,
                        exception,
                        DiagnosticSeverity.Warning));
                }
            }
        }

        private static void ValidateGeneratedLibrarySource(
            LibraryReadCandidate candidate,
            string sourceText)
        {
            var expectedKind = candidate.Kind == TiaLibrarySourceKind.DataType
                ? SclDeclarationKind.DataType
                : candidate.Kind == TiaLibrarySourceKind.FunctionBlock
                    ? SclDeclarationKind.FunctionBlock
                    : SclDeclarationKind.Function;
            var declarations = SclSourceInspector.FindDeclarations(sourceText);
            if (declarations.Count != 1 ||
                declarations[0].Kind != expectedKind ||
                !string.Equals(declarations[0].Name, candidate.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated source did not contain exactly the expected " +
                    expectedKind + " declaration '" + candidate.Name + "'.");
            }
        }

        private TiaLibrarySnapshot AvailableLibrarySnapshot(
            bool isComplete,
            IEnumerable<TiaLibrarySource> sources,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            return new TiaLibrarySnapshot(
                true,
                isComplete,
                selectedDeviceName,
                selectedSoftwareName,
                sources,
                diagnostics);
        }

        private TiaLibrarySnapshot UnavailableLibrarySnapshot(
            IEnumerable<OpennessDiagnostic> diagnostics,
            IEnumerable<TiaLibrarySource> sources = null)
        {
            return new TiaLibrarySnapshot(
                false,
                false,
                selectedDeviceName,
                selectedSoftwareName,
                sources,
                diagnostics);
        }

        private static TiaHardwareIoChannel TryCreateHardwareIoChannel(
            Channel channel,
            string deviceItemPath,
            string deviceItemTypeIdentifier,
            string deviceItemTypeName,
            out string unsupportedReason)
        {
            unsupportedReason = string.Empty;
            TiaIoChannelKind kind;
            var isInput = channel.IoType == ChannelIoType.Input;
            var isOutput = channel.IoType == ChannelIoType.Output;
            if ((!isInput && !isOutput) ||
                (channel.Type != ChannelType.Digital && channel.Type != ChannelType.Analog))
            {
                return null;
            }

            if (channel.Type == ChannelType.Digital)
            {
                kind = isInput
                    ? TiaIoChannelKind.DigitalInput
                    : TiaIoChannelKind.DigitalOutput;
            }
            else
            {
                kind = isInput
                    ? TiaIoChannelKind.AnalogInput
                    : TiaIoChannelKind.AnalogOutput;
            }

            var engineeringChannel = (IEngineeringObject)channel;
            var rawBitAddress = engineeringChannel.GetAttribute("ChannelAddress");
            var rawWidthBits = engineeringChannel.GetAttribute("ChannelWidth");
            if (!(rawBitAddress is int) || !(rawWidthBits is uint))
            {
                unsupportedReason =
                    "The configured channel did not expose ChannelAddress as Int32 and ChannelWidth as UInt32; no address was inferred.";
                return null;
            }

            var bitAddress = (int)rawBitAddress;
            var widthBits = (uint)rawWidthBits;
            if (bitAddress < 0 || widthBits == 0)
            {
                unsupportedReason =
                    "The configured channel reported an unassigned address or zero width and was skipped.";
                return null;
            }

            var area = isInput ? "I" : "Q";
            string logicalAddress;
            IEnumerable<string> suggestedDataTypes;
            if (channel.Type == ChannelType.Digital)
            {
                if (widthBits != 1)
                {
                    unsupportedReason =
                        "Only exact one-bit digital channels can be represented as DI/DO; this digital channel reported " +
                        widthBits.ToString(CultureInfo.InvariantCulture) + " bits and was skipped.";
                    return null;
                }

                logicalAddress = "%" + area +
                    (bitAddress / 8).ToString(CultureInfo.InvariantCulture) + "." +
                    (bitAddress % 8).ToString(CultureInfo.InvariantCulture);
                suggestedDataTypes = new[] { "Bool" };
            }
            else
            {
                if ((bitAddress % 8) != 0)
                {
                    unsupportedReason =
                        "The analog channel is not byte-aligned; no IW/QW/ID/QD address was inferred.";
                    return null;
                }

                var byteAddress = bitAddress / 8;
                if (widthBits == 16)
                {
                    logicalAddress = "%" + area + "W" + byteAddress.ToString(CultureInfo.InvariantCulture);
                    suggestedDataTypes = new[] { "Int", "Word" };
                }
                else if (widthBits == 32)
                {
                    logicalAddress = "%" + area + "D" + byteAddress.ToString(CultureInfo.InvariantCulture);
                    suggestedDataTypes = new[] { "Real" };
                }
                else
                {
                    unsupportedReason =
                        "Only exact 16-bit or 32-bit analog channels can be represented as IW/QW or ID/QD; this analog channel reported " +
                        widthBits.ToString(CultureInfo.InvariantCulture) + " bits and was skipped.";
                    return null;
                }
            }

            return new TiaHardwareIoChannel(
                kind,
                channel.Number,
                bitAddress,
                widthBits,
                logicalAddress,
                suggestedDataTypes,
                deviceItemPath,
                deviceItemTypeIdentifier,
                deviceItemTypeName);
        }

        private static Dictionary<DeviceItem, string> BuildDeviceItemPathMap(Project selectedProject)
        {
            var result = new Dictionary<DeviceItem, string>();
            foreach (var device in selectedProject.Devices)
            {
                AddDeviceItemPaths(device, device.Name, result);
            }

            foreach (var group in selectedProject.DeviceGroups)
            {
                AddDeviceGroupItemPaths(group, group.Name, result);
            }

            return result;
        }

        private static void AddDeviceGroupItemPaths(
            DeviceUserGroup group,
            string parentPath,
            IDictionary<DeviceItem, string> paths)
        {
            foreach (var device in group.Devices)
            {
                AddDeviceItemPaths(device, parentPath + "/" + device.Name, paths);
            }

            foreach (var child in group.Groups)
            {
                AddDeviceGroupItemPaths(child, parentPath + "/" + child.Name, paths);
            }
        }

        private static void AddDeviceItemPaths(
            Device device,
            string devicePath,
            IDictionary<DeviceItem, string> paths)
        {
            foreach (var item in device.DeviceItems)
            {
                AddDeviceItemPaths(item, devicePath + "/" + item.Name, paths);
            }
        }

        private static void AddDeviceItemPaths(
            DeviceItem item,
            string itemPath,
            IDictionary<DeviceItem, string> paths)
        {
            paths[item] = itemPath;
            foreach (var child in item.DeviceItems)
            {
                AddDeviceItemPaths(child, itemPath + "/" + child.Name, paths);
            }
        }

        private static string TryReadStringAttribute(DeviceItem item, string attributeName)
        {
            try
            {
                return ((IEngineeringObject)item).GetAttribute(attributeName) as string ?? string.Empty;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (EngineeringNotSupportedException)
            {
                return string.Empty;
            }
            catch (Exception)
            {
                // TypeName is optional for auto-created/fixed hardware items.
                return string.Empty;
            }
        }

        private static TiaCpuFamily ClassifyCpuFamily(string typeName, string typeIdentifier)
        {
            var normalizedTypeName = typeName ?? string.Empty;
            if (S71200CpuTypePattern.IsMatch(normalizedTypeName))
            {
                return TiaCpuFamily.S71200;
            }

            if (S71500CpuTypePattern.IsMatch(normalizedTypeName))
            {
                return TiaCpuFamily.S71500;
            }

            var normalizedIdentifier = Regex.Replace(
                typeIdentifier ?? string.Empty,
                "[^A-Za-z0-9]",
                string.Empty).ToUpperInvariant();
            if (normalizedIdentifier.Contains("6ES721"))
            {
                return TiaCpuFamily.S71200;
            }

            if (normalizedIdentifier.Contains("6ES751"))
            {
                return TiaCpuFamily.S71500;
            }

            return TiaCpuFamily.Unknown;
        }

        private TiaHardwareIoCatalog UnavailableHardwareIoCatalog(
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            return new TiaHardwareIoCatalog(
                false,
                TiaCpuFamily.Unknown,
                string.Empty,
                string.Empty,
                null,
                diagnostics);
        }

        private PreflightPlan BuildPreflight(TiaExportRequest request)
        {
            var effectiveProjectPath = FirstNonEmpty(
                request.ProjectPath,
                connectionRequest == null ? string.Empty : connectionRequest.ProjectPath,
                project == null ? string.Empty : GetProjectPath(project));
            var effectiveDeviceName = FirstNonEmpty(
                request.TargetDeviceName,
                connectionRequest == null ? string.Empty : connectionRequest.TargetDeviceName);
            var effectiveSoftwareName = FirstNonEmpty(
                request.TargetSoftwareName,
                connectionRequest == null ? string.Empty : connectionRequest.TargetSoftwareName);

            var plan = ValidateLocalRequest(
                request,
                effectiveProjectPath,
                effectiveDeviceName,
                effectiveSoftwareName);
            if (plan.HasErrors)
            {
                plan.FinalizeFingerprints();
                return plan;
            }

            if (tiaPortal == null)
            {
                plan.AddDiagnostic(Error(
                    OpennessDiagnosticCodes.GatewayNotConnected,
                    "Connect to TIA Portal explicitly before previewing an export."));
                plan.FinalizeFingerprints();
                return plan;
            }

            if (string.IsNullOrWhiteSpace(effectiveProjectPath))
            {
                plan.AddDiagnostic(Error(
                    OpennessDiagnosticCodes.ProjectPathRequired,
                    "ProjectPath is required either in Connect or in the export request."));
                plan.FinalizeFingerprints();
                return plan;
            }

            if (!EnsureProject(effectiveProjectPath, plan.Diagnostics))
            {
                plan.FinalizeFingerprints();
                return plan;
            }

            if (!TrySelectPlc(effectiveDeviceName, effectiveSoftwareName, plan.Diagnostics))
            {
                plan.FinalizeFingerprints();
                return plan;
            }

            plan.Project = project;
            plan.Software = plcSoftware;
            plan.EffectiveProjectPath = GetProjectPath(project);
            plan.EffectiveDeviceName = selectedDeviceName;
            plan.EffectiveSoftwareName = selectedSoftwareName;
            plan.AddOperation(
                TiaExportOperationKind.OpenProject,
                plan.EffectiveProjectPath,
                "Use the already open project or open the exact matching Portal project without upgrade.");
            plan.AddOperation(
                TiaExportOperationKind.SelectPlc,
                selectedDeviceName + "/" + selectedSoftwareName,
                "Select this PLC software as the only export target.");

            AddProjectState(plan);
            InspectTags(plan, request);
            InspectSources(plan, request);

            var hasTransactionalMutations = plan.TagPlans.Any(tagPlan => tagPlan.Action != TagAction.None) ||
                plan.Sources.Count > 0;
            if (hasTransactionalMutations)
            {
                plan.InsertTransactionalOperation(
                    "All tag changes and ordered source generation execute in one TIA transaction. " +
                    "Cancellation or any exception rolls the whole import back.");
            }

            if ((hasTransactionalMutations || request.CompileAfterImport) &&
                connectedPortalMode.HasValue &&
                connectedPortalMode.Value == TiaPortalMode.WithoutUserInterface &&
                !request.SaveProjectAfterExport)
            {
                plan.AddDiagnostic(Error(
                    OpennessDiagnosticCodes.SaveRequiredHeadless,
                    "SaveProjectAfterExport must be enabled for every import or compile in a headless TIA Portal. " +
                    "Otherwise resulting changes could not be inspected or released safely.",
                    plan.EffectiveProjectPath));
            }

            if (request.CompileAfterImport)
            {
                plan.AddOperation(
                    TiaExportOperationKind.Compile,
                    selectedSoftwareName,
                    "Compile the selected PLC only after transaction and ExclusiveAccess have been disposed.");
            }

            if (request.SaveProjectAfterExport)
            {
                plan.AddOperation(
                    TiaExportOperationKind.SaveProject,
                    plan.EffectiveProjectPath,
                    request.CompileAfterImport
                        ? "Save explicitly after a compile without errors; skip save when compile has errors."
                        : "Explicitly save the project after the committed import.");

                if (project.IsModified)
                {
                    plan.AddCollision(new TiaExportCollision(
                        TiaExportCollisionKind.ProjectState,
                        plan.EffectiveProjectPath,
                        false,
                        true,
                        "The project already contains unsaved changes. Confirming this preview also confirms saving those changes."));
                    plan.AddDiagnostic(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.UnsavedProjectChanges,
                        DiagnosticSeverity.Warning,
                        "SaveProjectAfterExport will also save changes that existed before this export.",
                        plan.EffectiveProjectPath));
                }
            }

            plan.FinalizeFingerprints();
            return plan;
        }

        private PreflightPlan ValidateLocalRequest(
            TiaExportRequest request,
            string effectiveProjectPath,
            string effectiveDeviceName,
            string effectiveSoftwareName)
        {
            var plan = new PreflightPlan(
                effectiveProjectPath,
                effectiveDeviceName,
                effectiveSoftwareName,
                request);

            if (request.Sources.Count > 0 && !OwnershipMarkerPattern.IsMatch(request.OwnershipMarker))
            {
                plan.AddDiagnostic(Error(
                    OpennessDiagnosticCodes.OwnershipMarkerInvalid,
                    "OwnershipMarker must be a non-empty ASCII identifier of at most 8 characters.",
                    request.OwnershipMarker));
            }

            var lastKind = -1;
            var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var importOrders = new HashSet<int>();
            var declarationNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in request.Sources
                .OrderBy(item => item.ImportOrder)
                .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!Enum.IsDefined(typeof(TiaSourceKind), source.Kind))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceOrderInvalid,
                        "The source kind is not supported.",
                        source.FilePath));
                    continue;
                }

                if (!importOrders.Add(source.ImportOrder))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceOrderInvalid,
                        "Each external source must have a unique ImportOrder.",
                        source.FilePath));
                }

                if ((int)source.Kind < lastKind)
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceOrderInvalid,
                        "Sources must be ordered Types -> Block -> Instances -> Calls -> CallBlockInstance.",
                        source.FilePath));
                }

                lastKind = Math.Max(lastKind, (int)source.Kind);

                string fullPath;
                if (!TryGetFullPath(source.FilePath, out fullPath) || !File.Exists(fullPath))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceFileMissing,
                        "The SCL source file does not exist.",
                        source.FilePath));
                    continue;
                }

                if (!string.Equals(Path.GetExtension(fullPath), ".scl", StringComparison.OrdinalIgnoreCase))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceFileInvalid,
                        "Only .scl external source files are supported.",
                        fullPath));
                    continue;
                }

                if (!sourcePaths.Add(fullPath))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceFileInvalid,
                        "The same source file is listed more than once.",
                        fullPath));
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(fullPath);
                }
                catch (Exception exception)
                {
                    plan.AddDiagnostic(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.SourceFileInvalid,
                        "Cannot read the SCL source: " + exception.Message,
                        fullPath,
                        exception));
                    continue;
                }

                if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceEncodingInvalid,
                        "The SCL source must be UTF-8 with BOM.",
                        fullPath));
                    continue;
                }

                string content;
                try
                {
                    content = new UTF8Encoding(true, true).GetString(bytes, 3, bytes.Length - 3);
                }
                catch (DecoderFallbackException exception)
                {
                    plan.AddDiagnostic(ExceptionDiagnostic(
                        OpennessDiagnosticCodes.SourceEncodingInvalid,
                        "The SCL source contains invalid UTF-8 bytes.",
                        fullPath,
                        exception));
                    continue;
                }

                var declarations = SclSourceInspector.FindDeclarations(content);
                if (declarations.Count == 0)
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.SourceFileInvalid,
                        "No supported top-level SCL declaration was found.",
                        fullPath));
                    continue;
                }

                var compatible = true;
                foreach (var declaration in declarations)
                {
                    if (declaration.Kind == SclDeclarationKind.OrganizationBlock ||
                        string.Equals(declaration.Name, "OB1", StringComparison.OrdinalIgnoreCase))
                    {
                        plan.AddDiagnostic(Error(
                            OpennessDiagnosticCodes.Ob1Protected,
                            "OB1 and organization blocks are never imported or modified by this adapter.",
                            fullPath));
                        compatible = false;
                    }

                    if (!IsDeclarationCompatible(source.Kind, declaration.Kind))
                    {
                        plan.AddDiagnostic(Error(
                            OpennessDiagnosticCodes.SourceFileInvalid,
                            "Declaration '" + declaration.Name + "' does not match source kind " + source.Kind + ".",
                            fullPath));
                        compatible = false;
                    }

                    string previousPath;
                    if (declarationNames.TryGetValue(declaration.Name, out previousPath))
                    {
                        plan.AddDiagnostic(Error(
                            OpennessDiagnosticCodes.SourceDeclarationDuplicate,
                            "Declaration '" + declaration.Name + "' is already supplied by " + previousPath + ".",
                            fullPath));
                        compatible = false;
                    }
                    else
                    {
                        declarationNames.Add(declaration.Name, fullPath);
                    }
                }

                byte[] importBytes = bytes;
                if (compatible && declarations.Any(item => !item.IsDataType))
                {
                    if (!OwnershipMarkerPattern.IsMatch(request.OwnershipMarker))
                    {
                        compatible = false;
                    }
                    else
                    {
                        try
                        {
                            var stampedContent = SclSourceInspector.StampOwnershipFamily(
                                content,
                                request.OwnershipMarker);
                            importBytes = EncodeUtf8Bom(stampedContent);
                        }
                        catch (Exception exception)
                        {
                            plan.AddDiagnostic(ExceptionDiagnostic(
                                OpennessDiagnosticCodes.SourceFileInvalid,
                                "Cannot prepare a deterministic ownership-stamped import copy: " + exception.Message,
                                fullPath,
                                exception));
                            compatible = false;
                        }
                    }
                }

                if (compatible)
                {
                    plan.Sources.Add(new ValidatedSource(
                        source,
                        fullPath,
                        ComputeHash(importBytes),
                        importBytes,
                        declarations));
                }
            }

            var requestedTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in request.Tags)
            {
                if (!SafeIdentifierPattern.IsMatch(tag.Name) || !IsSafeEngineeringName(tag.TableName))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.TagDefinitionInvalid,
                        "Tag names must be ASCII SCL identifiers and table names must not contain control or path characters.",
                        tag.TableName + "/" + tag.Name));
                }

                if (!requestedTagNames.Add(tag.Name))
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.TagDuplicate,
                        "PLC tag names must be unique across the entire request.",
                        tag.Name));
                }
            }

            plan.SetRequestFingerprint(BuildRequestFingerprint(plan, request));
            return plan;
        }

        private static bool IsDeclarationCompatible(TiaSourceKind sourceKind, SclDeclarationKind declarationKind)
        {
            switch (sourceKind)
            {
                case TiaSourceKind.Types:
                    return declarationKind == SclDeclarationKind.DataType;
                case TiaSourceKind.Block:
                case TiaSourceKind.Calls:
                    return declarationKind == SclDeclarationKind.Function ||
                        declarationKind == SclDeclarationKind.FunctionBlock;
                case TiaSourceKind.Instances:
                case TiaSourceKind.CallBlockInstance:
                    return declarationKind == SclDeclarationKind.DataBlock;
                default:
                    return false;
            }
        }

        private static bool IsSafeEngineeringName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (char.IsControl(character) || character == '/' || character == '\\')
                {
                    return false;
                }
            }

            return true;
        }

        private void AddProjectState(PreflightPlan plan)
        {
            try
            {
                var process = tiaPortal.GetCurrentProcess();
                plan.AddState("PORTAL_PROCESS_ID", process.Id.ToString(CultureInfo.InvariantCulture));
                // AcquisitionTime belongs to the transient TiaPortalProcess
                // information snapshot returned by GetCurrentProcess(). It is
                // not a stable process-start identity and therefore must not
                // invalidate a confirmation token on the repeated preflight.
                plan.AddState("PORTAL_MODE", process.Mode.ToString());
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception exception)
            {
                plan.AddDiagnostic(ExceptionDiagnostic(
                    OpennessDiagnosticCodes.PreflightFailed,
                    "Cannot identify the attached TIA Portal process.",
                    installation.RootPath,
                    exception));
            }

            plan.AddState("INSTALLATION_ROOT", installation.RootPath);
            plan.AddState("API_ASSEMBLY", installation.AssemblyPath);
            plan.AddState("PROJECT_PATH", plan.EffectiveProjectPath);
            plan.AddState("PROJECT_VERSION", project.Version);
            plan.AddState("PROJECT_LAST_MODIFIED", project.LastModified.Ticks.ToString(CultureInfo.InvariantCulture));
            plan.AddState("PROJECT_IS_MODIFIED", project.IsModified.ToString(CultureInfo.InvariantCulture));
            plan.AddState("PLC_DEVICE", plan.EffectiveDeviceName);
            plan.AddState("PLC_SOFTWARE", plan.EffectiveSoftwareName);
        }

        private void InspectTags(PreflightPlan plan, TiaExportRequest request)
        {
            var allTables = EnumerateTagTables(plcSoftware.TagTableGroup).ToList();
            var tablesByName = allTables
                .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var tagsByName = allTables
                .SelectMany(table => table.Tags.Select(tag => new TagLocation(table, tag)))
                .GroupBy(location => location.Tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var plannedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var definition in request.Tags)
            {
                List<PlcTagTable> targetTables;
                if (!tablesByName.TryGetValue(definition.TableName, out targetTables))
                {
                    targetTables = new List<PlcTagTable>();
                }

                if (targetTables.Count > 1)
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.TagConflict,
                        "More than one tag table named '" + definition.TableName + "' exists in nested groups.",
                        definition.TableName));
                    plan.AddState("TAG_TABLE", definition.TableName + "|AMBIGUOUS");
                    continue;
                }

                List<TagLocation> existingLocations;
                if (!tagsByName.TryGetValue(definition.Name, out existingLocations))
                {
                    existingLocations = new List<TagLocation>();
                }

                if (existingLocations.Count > 1)
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.TagConflict,
                        "The PLC contains more than one tag named '" + definition.Name + "'.",
                        definition.Name));
                    plan.AddState("TAG", definition.Name + "|AMBIGUOUS");
                    continue;
                }

                if (existingLocations.Count == 0)
                {
                    PlcTagTable targetTable = targetTables.Count == 1 ? targetTables[0] : null;
                    if (targetTable == null && plannedTables.Add(definition.TableName))
                    {
                        plan.AddOperation(
                            TiaExportOperationKind.CreateTagTable,
                            definition.TableName,
                            "Create the missing PLC tag table in the root tag-table group inside the transaction.");
                    }

                    plan.TagPlans.Add(new TagPlan(definition, TagAction.Create, targetTable, null));
                    plan.AddOperation(
                        TiaExportOperationKind.CreateTag,
                        definition.TableName + "/" + definition.Name,
                        string.IsNullOrEmpty(definition.LogicalAddress)
                            ? "Create an unassigned PLC tag inside the transaction."
                            : "Create the addressed PLC tag inside the transaction.");
                    plan.AddState("TAG", definition.Name + "|MISSING|" + definition.TableName);
                    continue;
                }

                var existing = existingLocations[0];
                var existingState = existing.Table.Name + "|" + existing.Tag.DataTypeName + "|" +
                    existing.Tag.LogicalAddress;
                plan.AddState("TAG", definition.Name + "|" + existingState);

                if (!string.Equals(existing.Table.Name, definition.TableName, StringComparison.OrdinalIgnoreCase))
                {
                    plan.AddCollision(new TiaExportCollision(
                        TiaExportCollisionKind.Tag,
                        definition.Name,
                        false,
                        false,
                        "The tag already exists in another table: " + existing.Table.Name + "."));
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.TagConflict,
                        "Tag '" + definition.Name + "' already exists in table '" + existing.Table.Name + "'.",
                        definition.Name));
                    continue;
                }

                var equal = string.Equals(existing.Tag.DataTypeName, definition.DataType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Tag.LogicalAddress ?? string.Empty, definition.LogicalAddress, StringComparison.OrdinalIgnoreCase);
                if (equal)
                {
                    plan.TagPlans.Add(new TagPlan(definition, TagAction.None, existing.Table, existing.Tag));
                    continue;
                }

                var canUpdate = request.AllowTagUpdates;
                plan.AddCollision(new TiaExportCollision(
                    TiaExportCollisionKind.Tag,
                    definition.Name,
                    false,
                    canUpdate,
                    "Existing tag is " + existing.Tag.DataTypeName + " at '" +
                    (existing.Tag.LogicalAddress ?? string.Empty) + "'; requested value is " +
                    definition.DataType + " at '" + definition.LogicalAddress + "'."));

                if (!canUpdate)
                {
                    plan.AddDiagnostic(Error(
                        OpennessDiagnosticCodes.TagConflict,
                        "Tag '" + definition.Name + "' differs and AllowTagUpdates is false.",
                        definition.Name));
                    continue;
                }

                plan.AddDiagnostic(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.TagConflict,
                    DiagnosticSeverity.Warning,
                    "Tag '" + definition.Name + "' will be updated after confirmation.",
                    definition.Name));
                plan.TagPlans.Add(new TagPlan(definition, TagAction.Update, existing.Table, existing.Tag));
                plan.AddOperation(
                    TiaExportOperationKind.UpdateTag,
                    definition.TableName + "/" + definition.Name,
                    "Update DataTypeName and LogicalAddress inside the transaction.");
            }
        }

        private void InspectSources(PreflightPlan plan, TiaExportRequest request)
        {
            var blocks = EnumerateBlocks(plcSoftware.BlockGroup)
                .GroupBy(block => block.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var types = EnumerateTypes(plcSoftware.TypeGroup)
                .GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var source in plan.Sources)
            {
                foreach (var declaration in source.Declarations)
                {
                    if (declaration.IsDataType)
                    {
                        List<PlcType> existingTypes;
                        if (!types.TryGetValue(declaration.Name, out existingTypes))
                        {
                            existingTypes = new List<PlcType>();
                        }

                        if (existingTypes.Count > 0)
                        {
                            plan.AddCollision(new TiaExportCollision(
                                TiaExportCollisionKind.DataType,
                                declaration.Name,
                                false,
                                false,
                                "Existing PLC data types cannot be proven to be owned by this adapter."));
                            plan.AddDiagnostic(Error(
                                OpennessDiagnosticCodes.DataTypeConflict,
                                "PLC data type '" + declaration.Name + "' already exists and cannot be overwritten safely.",
                                declaration.Name));
                            foreach (var existingType in existingTypes)
                            {
                                plan.AddState(
                                    "TYPE",
                                    declaration.Name + "|EXISTS|" +
                                    existingType.ModifiedDate.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                                    existingType.InterfaceModifiedDate.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                                    existingType.IsConsistent.ToString(CultureInfo.InvariantCulture));
                            }
                        }
                        else
                        {
                            plan.AddState("TYPE", declaration.Name + "|MISSING");
                        }

                        continue;
                    }

                    if (string.Equals(declaration.Name, "OB1", StringComparison.OrdinalIgnoreCase))
                    {
                        plan.AddDiagnostic(Error(
                            OpennessDiagnosticCodes.Ob1Protected,
                            "OB1 is protected and cannot be imported or overwritten.",
                            declaration.Name));
                        continue;
                    }

                    List<PlcBlock> existingBlocks;
                    if (!blocks.TryGetValue(declaration.Name, out existingBlocks))
                    {
                        existingBlocks = new List<PlcBlock>();
                    }

                    if (existingBlocks.Count > 1)
                    {
                        plan.AddCollision(new TiaExportCollision(
                            TiaExportCollisionKind.Block,
                            declaration.Name,
                            false,
                            false,
                            "More than one existing block has this name."));
                        plan.AddDiagnostic(Error(
                            OpennessDiagnosticCodes.BlockConflict,
                            "More than one PLC block named '" + declaration.Name + "' exists.",
                            declaration.Name));
                        plan.AddState("BLOCK", declaration.Name + "|AMBIGUOUS");
                        continue;
                    }

                    if (existingBlocks.Count == 0)
                    {
                        plan.AddState("BLOCK", declaration.Name + "|MISSING");
                        continue;
                    }

                    var existingBlock = existingBlocks[0];
                    string existingMarker;
                    try
                    {
                        existingMarker = existingBlock.HeaderFamily ?? string.Empty;
                    }
                    catch (NonRecoverableException)
                    {
                        throw;
                    }
                    catch (EngineeringSecurityException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        existingMarker = string.Empty;
                        plan.AddDiagnostic(ExceptionDiagnostic(
                            OpennessDiagnosticCodes.BlockConflict,
                            "Cannot read block ownership marker; overwrite is forbidden.",
                            declaration.Name,
                            exception));
                    }

                    var isOwned = string.Equals(
                        existingMarker,
                        request.OwnershipMarker,
                        StringComparison.Ordinal);
                    var canOverwrite = isOwned && request.AllowOwnedBlockOverwrite;
                    plan.AddState(
                        "BLOCK",
                        declaration.Name + "|EXISTS|" + existingMarker + "|" +
                        existingBlock.ModifiedDate.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                        existingBlock.CodeModifiedDate.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                        existingBlock.InterfaceModifiedDate.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                        existingBlock.IsConsistent.ToString(CultureInfo.InvariantCulture));
                    plan.AddCollision(new TiaExportCollision(
                        TiaExportCollisionKind.Block,
                        declaration.Name,
                        isOwned,
                        canOverwrite,
                        isOwned
                            ? "The block carries the matching HeaderFamily ownership marker."
                            : "The block has no matching HeaderFamily ownership marker."));

                    if (!canOverwrite)
                    {
                        plan.AddDiagnostic(Error(
                            OpennessDiagnosticCodes.BlockConflict,
                            isOwned
                                ? "Block '" + declaration.Name + "' is owned, but AllowOwnedBlockOverwrite is false."
                                : "Block '" + declaration.Name + "' is not owned by marker '" + request.OwnershipMarker + "'.",
                            declaration.Name));
                    }
                    else
                    {
                        plan.AddDiagnostic(new OpennessDiagnostic(
                            OpennessDiagnosticCodes.BlockConflict,
                            DiagnosticSeverity.Warning,
                            "Owned block '" + declaration.Name + "' will be overwritten after confirmation.",
                            declaration.Name));
                    }
                }

                plan.AddOperation(
                    TiaExportOperationKind.TransactionalImport,
                    source.FullPath,
                    "Create a temporary root external source and call GenerateBlocksFromSource(GenerateBlockOption.None) in import order " +
                    source.Source.ImportOrder.ToString(CultureInfo.InvariantCulture) + ".");

                foreach (var declaration in source.Declarations.Where(item => !item.IsDataType))
                {
                    plan.AddOperation(
                        TiaExportOperationKind.SetBlockOwnership,
                        declaration.Name,
                        "Import a deterministic staging copy with FAMILY '" + request.OwnershipMarker +
                        "' and verify the read-only HeaderFamily after generation.");
                }
            }
        }

        private TiaExportResult ExecuteExport(TiaExportRequest request, PreviewTicket ticket)
        {
            var diagnostics = new List<OpennessDiagnostic>();
            var importedSourceCount = 0;
            var compilationAttempted = false;
            var transactionCommitted = false;
            var mutationAttempted = false;
            PreflightPlan plan = null;

            try
            {
                try
                {
                using (var exclusive = tiaPortal.ExclusiveAccess("TIA SCL Studio guarded export"))
                {
                    plan = BuildPreflight(request);
                    if (!plan.CanExecute ||
                        !string.Equals(ticket.RequestFingerprint, plan.RequestFingerprint, StringComparison.Ordinal) ||
                        !string.Equals(ticket.PlanFingerprint, plan.PlanFingerprint, StringComparison.Ordinal))
                    {
                        var staleDiagnostics = new List<OpennessDiagnostic>(plan.Diagnostics)
                        {
                            Error(
                                OpennessDiagnosticCodes.ConfirmationStale,
                                "The request, staged source bytes, project, PLC, or collision state changed after preview. Run preview again.")
                        };
                        return new TiaExportResult(
                            TiaExportOutcome.Rejected,
                            0,
                            false,
                            staleDiagnostics);
                    }

                    diagnostics.AddRange(plan.Diagnostics);
                    var hasMutations = plan.TagPlans.Any(item => item.Action != TagAction.None) ||
                        plan.Sources.Count > 0;
                    if (hasMutations)
                    {
                        mutationAttempted = true;
                        using (var transaction = exclusive.Transaction(project, "TIA SCL Studio import"))
                        {
                            ThrowIfCancellationRequested(exclusive);
                            ApplyTagPlans(plan, diagnostics, exclusive);
                            GenerateSources(plan, request.OwnershipMarker, diagnostics, exclusive);
                            ThrowIfCancellationRequested(exclusive);
                            transaction.CommitOnDispose();
                        }

                        transactionCommitted = true;
                        importedSourceCount = plan.Sources.Count;
                        committedUnsavedChanges = true;
                    }
                }
            }
            catch (OperationCanceledException exception)
            {
                diagnostics.Add(ExceptionDiagnostic(
                    mutationAttempted
                        ? OpennessDiagnosticCodes.TransactionCancelled
                        : OpennessDiagnosticCodes.PreflightFailed,
                    mutationAttempted
                        ? "TIA cancelled the transactional import. No transaction changes were committed."
                        : "TIA cancelled the guarded repeated preflight.",
                    plan == null ? string.Empty : plan.EffectiveProjectPath,
                    exception));
                return new TiaExportResult(
                    TiaExportOutcome.Failed,
                    0,
                    false,
                    diagnostics);
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExceptionDiagnostic(
                    mutationAttempted
                        ? OpennessDiagnosticCodes.TransactionFailed
                        : OpennessDiagnosticCodes.PreflightFailed,
                    mutationAttempted
                        ? "Transactional import failed and was not committed: " + exception.Message
                        : "The guarded repeated preflight failed before mutation: " + exception.Message,
                    plan == null ? string.Empty : plan.EffectiveProjectPath,
                    exception));
                return new TiaExportResult(
                    TiaExportOutcome.Failed,
                    0,
                    false,
                    diagnostics);
            }

            var compileHasErrors = false;
            if (request.CompileAfterImport)
            {
                compilationAttempted = true;
                compileHasErrors = !CompilePlc(diagnostics);
                if (HasUnsavedProjectChanges())
                {
                    committedUnsavedChanges = true;
                }
            }

            var saveFailed = false;
            if (request.SaveProjectAfterExport)
            {
                if (request.CompileAfterImport && compileHasErrors)
                {
                        diagnostics.Add(new OpennessDiagnostic(
                            OpennessDiagnosticCodes.SaveSkippedCompileErrors,
                            DiagnosticSeverity.Warning,
                            "Project.Save was skipped because PLC compilation reported errors. Fix the errors and save or retry before closing this application.",
                            plan.EffectiveProjectPath));
                }
                else
                {
                    try
                    {
                        project.Save();
                        committedUnsavedChanges = false;
                        if (HasUnsavedProjectChanges())
                        {
                            saveFailed = true;
                            diagnostics.Add(Error(
                                OpennessDiagnosticCodes.ProjectSaveFailed,
                                "Project.Save returned, but the project still reports unsaved changes. Save or retry before closing this application.",
                                plan.EffectiveProjectPath));
                        }
                        else
                        {
                            diagnostics.Add(new OpennessDiagnostic(
                                OpennessDiagnosticCodes.ProjectSaved,
                                DiagnosticSeverity.Information,
                                "The project was explicitly saved after export.",
                                plan.EffectiveProjectPath));
                        }
                    }
                    catch (NonRecoverableException)
                    {
                        throw;
                    }
                    catch (EngineeringSecurityException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        saveFailed = true;
                        committedUnsavedChanges = true;
                        diagnostics.Add(ExceptionDiagnostic(
                            OpennessDiagnosticCodes.ProjectSaveFailed,
                            "The import was committed, but Project.Save failed. Save or retry before closing this application: " + exception.Message,
                            plan.EffectiveProjectPath,
                            exception));
                    }
                }
            }

            var failed = compileHasErrors || saveFailed;
            if (!request.SaveProjectAfterExport &&
                (transactionCommitted || compilationAttempted) &&
                HasUnsavedProjectChanges())
            {
                committedUnsavedChanges = true;
                diagnostics.Add(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.UnsavedChangesRequireManualSave,
                    DiagnosticSeverity.Warning,
                    "Export changes remain unsaved. Save them explicitly in TIA Portal before reconnecting or closing an adapter-opened Portal session.",
                    plan.EffectiveProjectPath));
            }

            if (request.SaveProjectAfterExport &&
                (compileHasErrors || saveFailed) &&
                HasUnsavedProjectChanges())
            {
                diagnostics.Add(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.UnsavedChangesRequireManualSave,
                    DiagnosticSeverity.Warning,
                    "Committed changes remain unsaved. Fix the reported problem and save or retry before reconnecting or closing this application.",
                    plan.EffectiveProjectPath));
            }

            if (!failed)
            {
                diagnostics.Add(new OpennessDiagnostic(
                    OpennessDiagnosticCodes.ExportSucceeded,
                    DiagnosticSeverity.Information,
                    transactionCommitted
                        ? "The transactional export completed successfully."
                        : "The requested non-mutating compile/save workflow completed successfully.",
                    plan.EffectiveProjectPath));
            }

            return new TiaExportResult(
                failed ? TiaExportOutcome.Failed : TiaExportOutcome.Succeeded,
                importedSourceCount,
                compilationAttempted,
                diagnostics);
                }
                catch (NonRecoverableException exception)
                {
                    HandleSessionLost(
                        diagnostics,
                        "TIA Portal closed the session during export: " + exception.Message,
                        exception);
                    return new TiaExportResult(
                        TiaExportOutcome.Failed,
                        importedSourceCount,
                        compilationAttempted,
                        diagnostics);
                }
                catch (EngineeringSecurityException exception)
                {
                    if (mutationAttempted || compilationAttempted)
                    {
                        committedUnsavedChanges = true;
                    }

                    HandleAccessDenied(
                        diagnostics,
                        "TIA Openness access was denied during export: " + exception.Message,
                        exception);
                    if (mutationAttempted || compilationAttempted)
                    {
                        diagnostics.Add(new OpennessDiagnostic(
                            OpennessDiagnosticCodes.UnsavedChangesRequireManualSave,
                            DiagnosticSeverity.Warning,
                            "The operation may have left committed changes unsaved. Resolve access rights and save before reconnecting or closing this application.",
                            plan == null ? string.Empty : plan.EffectiveProjectPath));
                    }

                    return new TiaExportResult(
                        TiaExportOutcome.Failed,
                        importedSourceCount,
                        compilationAttempted,
                        diagnostics);
                }
            }

        private void ApplyTagPlans(
            PreflightPlan plan,
            IList<OpennessDiagnostic> diagnostics,
            ExclusiveAccess exclusive)
        {
            var tables = EnumerateTagTables(plcSoftware.TagTableGroup)
                .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var tagPlan in plan.TagPlans.Where(item => item.Action != TagAction.None))
            {
                ThrowIfCancellationRequested(exclusive);
                PlcTagTable table;
                if (!tables.TryGetValue(tagPlan.Definition.TableName, out table))
                {
                    table = plcSoftware.TagTableGroup.TagTables.Create(tagPlan.Definition.TableName);
                    tables.Add(tagPlan.Definition.TableName, table);
                }

                PlcTag tag;
                if (tagPlan.Action == TagAction.Create)
                {
                    if (string.IsNullOrEmpty(tagPlan.Definition.LogicalAddress))
                    {
                        tag = table.Tags.Create(tagPlan.Definition.Name);
                        tag.DataTypeName = tagPlan.Definition.DataType;
                    }
                    else
                    {
                        tag = table.Tags.Create(
                            tagPlan.Definition.Name,
                            tagPlan.Definition.DataType,
                            tagPlan.Definition.LogicalAddress);
                    }
                }
                else
                {
                    tag = tagPlan.ExistingTag;
                    tag.DataTypeName = tagPlan.Definition.DataType;
                    tag.LogicalAddress = tagPlan.Definition.LogicalAddress;
                }

                TrySetTagComment(tag, tagPlan.Definition.Comment, diagnostics);
            }
        }

        private void TrySetTagComment(
            PlcTag tag,
            string comment,
            IList<OpennessDiagnostic> diagnostics)
        {
            if (tag == null || string.IsNullOrEmpty(comment))
            {
                return;
            }

            try
            {
                var language = project.LanguageSettings.EditingLanguage ??
                    project.LanguageSettings.ReferenceLanguage;
                var item = language == null ? null : tag.Comment.Items.Find(language);
                if (item == null)
                {
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.TagCommentSkipped,
                        DiagnosticSeverity.Warning,
                        "The tag was written, but no editing/reference language comment item is available.",
                        tag.Name));
                    return;
                }

                item.Text = comment;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExceptionDiagnostic(
                    OpennessDiagnosticCodes.TagCommentSkipped,
                    "The tag was written, but its multilingual comment could not be updated: " + exception.Message,
                    tag.Name,
                    exception,
                    DiagnosticSeverity.Warning));
            }
        }

        private void GenerateSources(
            PreflightPlan plan,
            string ownershipMarker,
            IList<OpennessDiagnostic> diagnostics,
            ExclusiveAccess exclusive)
        {
            foreach (var source in plan.Sources.OrderBy(item => item.Source.ImportOrder)
                .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                ThrowIfCancellationRequested(exclusive);
                var externalName = CreateExternalSourceName(source.FullPath);
                var stagedSource = WriteStagedSource(source);
                PlcExternalSource externalSource = null;
                Exception sourceFailure = null;
                var importStep = "CreateFromFile";
                try
                {
                    externalSource = plcSoftware.ExternalSourceGroup.ExternalSources.CreateFromFile(
                        externalName,
                        stagedSource.Path);
                    importStep = "GenerateBlocksFromSource";
                    var generatedObjects = externalSource.GenerateBlocksFromSource(GenerateBlockOption.None);
                    importStep = "ValidateGeneratedObjects";
                    ValidateGeneratedObjects(source, generatedObjects, ownershipMarker);
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.SourceImported,
                        DiagnosticSeverity.Information,
                        "Generated " + generatedObjects.Count.ToString(CultureInfo.InvariantCulture) +
                        " PLC object(s) from the ordered SCL source.",
                        source.FullPath));
                }
                catch (NonRecoverableException exception)
                {
                    sourceFailure = exception;
                    throw;
                }
                catch (EngineeringSecurityException exception)
                {
                    sourceFailure = exception;
                    throw;
                }
                catch (Exception exception)
                {
                    sourceFailure = exception;
                    throw new InvalidOperationException(
                        "External source step '" + importStep + "' failed for approved source '" +
                        source.FullPath + "' (temporary TIA object '" + externalName + "').",
                        exception);
                }
                finally
                {
                    try
                    {
                        try
                        {
                            // A failed source aborts the surrounding transaction,
                            // which rolls its temporary TIA object back. Do not
                            // call a possibly dead or access-denied Siemens proxy
                            // from cleanup and risk masking the primary failure.
                            if (externalSource != null && sourceFailure == null)
                            {
                                externalSource.Delete();
                            }
                        }
                        catch (NonRecoverableException)
                        {
                            throw;
                        }
                        catch (EngineeringSecurityException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            throw new InvalidOperationException(
                                "Deleting the temporary TIA external-source object '" +
                                externalName + "' failed after a successful import.",
                                exception);
                        }
                    }
                    finally
                    {
                        TryDeleteStagedSource(stagedSource.Path, diagnostics);
                    }
                }
            }
        }

        private static StagedSourceFile WriteStagedSource(ValidatedSource source)
        {
            var stagedPath = Path.Combine(
                Path.GetTempPath(),
                "TiaSclStudio-V17-" + Guid.NewGuid().ToString("N") + ".scl");
            try
            {
                // Siemens' samples pass a fully closed on-disk source. Close
                // every handle before CreateFromFile: a retained writer caused
                // a sharing violation, and even a read lock can conflict with
                // a Siemens open that requests exclusive sharing.
                using (var writer = new FileStream(
                    stagedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    writer.Write(source.ImportBytes, 0, source.ImportBytes.Length);
                    writer.Flush(true);
                }

                return new StagedSourceFile(stagedPath);
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(stagedPath))
                    {
                        File.Delete(stagedPath);
                    }
                }
                catch (Exception)
                {
                    // Preserve the original staging exception.
                }

                throw;
            }
        }

        private static void TryDeleteStagedSource(
            string stagedPath,
            ICollection<OpennessDiagnostic> diagnostics)
        {
            try
            {
                if (!string.IsNullOrEmpty(stagedPath) && File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExceptionDiagnostic(
                    OpennessDiagnosticCodes.TemporarySourceCleanupFailed,
                    "The generated staging SCL file could not be deleted: " + exception.Message,
                    stagedPath,
                    exception,
                    DiagnosticSeverity.Warning));
            }
        }

        private static void ValidateGeneratedObjects(
            ValidatedSource source,
            IList<IEngineeringObject> generatedObjects,
            string ownershipMarker)
        {
            if (generatedObjects == null)
            {
                throw new InvalidOperationException(
                    "GenerateBlocksFromSource returned no object collection for " + source.FullPath + ".");
            }

            var generatedBlocks = generatedObjects.OfType<PlcBlock>()
                .ToDictionary(block => block.Name, StringComparer.OrdinalIgnoreCase);
            var generatedTypes = generatedObjects.OfType<PlcType>()
                .ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);
            var expectedBlockNames = new HashSet<string>(
                source.Declarations.Where(item => !item.IsDataType).Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);
            var expectedTypeNames = new HashSet<string>(
                source.Declarations.Where(item => item.IsDataType).Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);

            if (generatedObjects.Any(item => !(item is PlcBlock) && !(item is PlcType)) ||
                !expectedBlockNames.SetEquals(generatedBlocks.Keys) ||
                !expectedTypeNames.SetEquals(generatedTypes.Keys))
            {
                throw new InvalidOperationException(
                    "GenerateBlocksFromSource returned a different object set than the declarations approved by preflight.");
            }

            foreach (var declaration in source.Declarations)
            {
                if (declaration.IsDataType)
                {
                    if (!generatedTypes.ContainsKey(declaration.Name))
                    {
                        throw new InvalidOperationException(
                            "The generated object list does not contain PLC data type '" + declaration.Name + "'.");
                    }

                    continue;
                }

                PlcBlock block;
                if (!generatedBlocks.TryGetValue(declaration.Name, out block))
                {
                    throw new InvalidOperationException(
                        "The generated object list does not contain PLC block '" + declaration.Name + "'.");
                }

                if (string.Equals(block.Name, "OB1", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("OB1 generation is forbidden.");
                }

                if (!string.Equals(block.HeaderFamily, ownershipMarker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Generated PLC block '" + block.Name + "' did not retain the approved FAMILY ownership marker.");
                }
            }
        }

        private bool CompilePlc(IList<OpennessDiagnostic> diagnostics)
        {
            try
            {
                var compilable = plcSoftware.GetService<ICompilable>();
                if (compilable == null)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.CompilationFailed,
                        "The selected PLC software does not provide ICompilable.",
                        selectedSoftwareName));
                    return false;
                }

                var result = compilable.Compile();
                if (result == null)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.CompilationFailed,
                        "TIA Portal returned no compiler result.",
                        selectedSoftwareName));
                    return false;
                }

                FlattenCompilerMessages(result.Messages, diagnostics);
                var hasErrors = result.ErrorCount > 0 || result.State == CompilerResultState.Error;
                diagnostics.Add(new OpennessDiagnostic(
                    hasErrors
                        ? OpennessDiagnosticCodes.CompilationFailed
                        : OpennessDiagnosticCodes.CompilationSucceeded,
                    hasErrors ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                    "PLC compile state: " + result.State + "; errors: " +
                    result.ErrorCount.ToString(CultureInfo.InvariantCulture) + "; warnings: " +
                    result.WarningCount.ToString(CultureInfo.InvariantCulture) + ".",
                    selectedSoftwareName));
                return !hasErrors;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(ExceptionDiagnostic(
                    OpennessDiagnosticCodes.CompilationFailed,
                    "PLC compilation failed: " + exception.Message,
                    selectedSoftwareName,
                    exception));
                return false;
            }
        }

        private static void FlattenCompilerMessages(
            IEnumerable<CompilerResultMessage> messages,
            ICollection<OpennessDiagnostic> diagnostics)
        {
            if (messages == null)
            {
                return;
            }

            foreach (var message in messages)
            {
                if (message == null)
                {
                    continue;
                }

                var severity = MapCompilerSeverity(message.State);
                if (!string.IsNullOrWhiteSpace(message.Description))
                {
                    diagnostics.Add(new OpennessDiagnostic(
                        OpennessDiagnosticCodes.CompilationMessage,
                        severity,
                        message.Description,
                        message.Path));
                }

                FlattenCompilerMessages(message.Messages, diagnostics);
            }
        }

        private static DiagnosticSeverity MapCompilerSeverity(CompilerResultState state)
        {
            switch (state)
            {
                case CompilerResultState.Error:
                    return DiagnosticSeverity.Error;
                case CompilerResultState.Warning:
                    return DiagnosticSeverity.Warning;
                default:
                    return DiagnosticSeverity.Information;
            }
        }

        private static IEnumerable<PlcTagTable> EnumerateTagTables(PlcTagTableGroup group)
        {
            foreach (var table in group.TagTables)
            {
                yield return table;
            }

            foreach (var child in group.Groups)
            {
                foreach (var table in EnumerateTagTables(child))
                {
                    yield return table;
                }
            }
        }

        private static IEnumerable<PlcBlock> EnumerateBlocks(PlcBlockGroup group)
        {
            foreach (var block in group.Blocks)
            {
                yield return block;
            }

            foreach (var child in group.Groups)
            {
                foreach (var block in EnumerateBlocks(child))
                {
                    yield return block;
                }
            }
        }

        private static IEnumerable<PlcType> EnumerateTypes(PlcTypeGroup group)
        {
            foreach (var type in group.Types)
            {
                yield return type;
            }

            foreach (var child in group.Groups)
            {
                foreach (var type in EnumerateTypes(child))
                {
                    yield return type;
                }
            }
        }

        private static void ThrowIfCancellationRequested(ExclusiveAccess exclusive)
        {
            if (exclusive.IsCancellationRequested)
            {
                throw new OperationCanceledException("TIA Portal requested cancellation of ExclusiveAccess.");
            }
        }

        private bool ReleaseConnection(
            IList<OpennessDiagnostic> diagnostics,
            bool preserveUnsafeConnection)
        {
            var hasUnsavedProjectChanges = HasUnsavedProjectChanges();
            var isHeadless = connectedPortalMode.HasValue &&
                connectedPortalMode.Value == TiaPortalMode.WithoutUserInterface;
            var unsafeToReleasePortal = hasUnsavedProjectChanges &&
                (portalStartedByAdapter || isHeadless);
            if (unsafeToReleasePortal && !preserveUnsafeConnection)
            {
                if (diagnostics != null)
                {
                    diagnostics.Add(Error(
                        OpennessDiagnosticCodes.ConnectionReleaseBlocked,
                        "Reconnect was blocked because releasing the current started/headless Portal session could discard unsaved project changes. " +
                        "Save the project explicitly before switching the Portal session.",
                        GetProjectPath(project)));
                }

                return false;
            }

            if (unsafeToReleasePortal && preserveUnsafeConnection)
            {
                lock (PreservedSessionsLock)
                {
                    if (tiaPortal != null)
                    {
                        PreservedSessions.Add(tiaPortal);
                    }
                }

                project = null;
                plcSoftware = null;
                selectedCpuItem = null;
                tiaPortal = null;
                connectionRequest = null;
                projectOpenedByAdapter = false;
                portalStartedByAdapter = false;
                committedUnsavedChanges = false;
                connectedPortalMode = null;
                selectedDeviceName = string.Empty;
                selectedSoftwareName = string.Empty;
                return true;
            }

            var mayCloseProject = project != null && projectOpenedByAdapter && !hasUnsavedProjectChanges;
            if (mayCloseProject)
            {
                try
                {
                    project.Close();
                }
                catch (NonRecoverableException)
                {
                    throw;
                }
                catch (EngineeringSecurityException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.Add(ExceptionDiagnostic(
                            OpennessDiagnosticCodes.ConnectionFailed,
                            "The project opened by the adapter could not be closed cleanly: " + exception.Message,
                            connectionRequest == null ? string.Empty : connectionRequest.ProjectPath,
                            exception,
                            DiagnosticSeverity.Warning));
                    }
                }
            }

            project = null;
            plcSoftware = null;
            selectedCpuItem = null;
            connectionRequest = null;
            projectOpenedByAdapter = false;
            portalStartedByAdapter = false;
            committedUnsavedChanges = false;
            connectedPortalMode = null;
            selectedDeviceName = string.Empty;
            selectedSoftwareName = string.Empty;

            if (tiaPortal != null)
            {
                try
                {
                    tiaPortal.Dispose();
                }
                catch (NonRecoverableException)
                {
                    throw;
                }
                catch (EngineeringSecurityException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.Add(ExceptionDiagnostic(
                            OpennessDiagnosticCodes.ConnectionFailed,
                            "The TIA Portal session could not be released cleanly: " + exception.Message,
                            string.Empty,
                            exception,
                            DiagnosticSeverity.Warning));
                    }
                }
            }

            tiaPortal = null;
            return true;
        }

        private bool HasUnsavedProjectChanges()
        {
            if (project == null)
            {
                return false;
            }

            try
            {
                var isModified = project.IsModified;
                if (!isModified)
                {
                    committedUnsavedChanges = false;
                }

                return isModified || committedUnsavedChanges;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception)
            {
                // If state cannot be read, never assume that closing is safe.
                return true;
            }
        }

        private TiaGatewayStatus HandleSessionLost(
            ICollection<OpennessDiagnostic> diagnostics,
            string message,
            NonRecoverableException exception)
        {
            diagnostics.Add(ExceptionDiagnostic(
                OpennessDiagnosticCodes.SessionLost,
                message + " The Portal process and this Openness connection are no longer usable; reconnect or restart TIA Portal.",
                string.Empty,
                exception));
            ResetConnectionStateWithoutApiCalls();
            status = CreateStatus(
                false,
                false,
                "TIA Portal session was lost; reconnect or restart Portal",
                diagnostics);
            return status;
        }

        private TiaGatewayStatus HandleAccessDenied(
            ICollection<OpennessDiagnostic> diagnostics,
            string message,
            EngineeringSecurityException exception)
        {
            diagnostics.Add(ExceptionDiagnostic(
                OpennessDiagnosticCodes.AccessDenied,
                message + " Verify membership in the local 'Siemens TIA Openness' Windows group, " +
                "accept the Openness firewall prompt, and restart the affected processes after changing membership.",
                string.Empty,
                exception));
            InvalidatePreview();
            var hasPortalReference = tiaPortal != null;
            status = CreateStatus(
                hasPortalReference,
                false,
                hasPortalReference
                    ? "Connected to TIA Portal, but Openness access was denied"
                    : "TIA Openness access was denied",
                diagnostics);
            return status;
        }

        private void PreserveCurrentSessionWithoutApiCalls()
        {
            var currentPortal = tiaPortal;
            if (currentPortal != null)
            {
                lock (PreservedSessionsLock)
                {
                    PreservedSessions.Add(currentPortal);
                }
            }

            ResetConnectionStateWithoutApiCalls();
        }

        private void ResetConnectionStateWithoutApiCalls()
        {
            InvalidatePreview();
            tiaPortal = null;
            project = null;
            plcSoftware = null;
            selectedCpuItem = null;
            connectionRequest = null;
            projectOpenedByAdapter = false;
            portalStartedByAdapter = false;
            committedUnsavedChanges = false;
            connectedPortalMode = null;
            selectedDeviceName = string.Empty;
            selectedSoftwareName = string.Empty;
        }

        private string BuildConnectionDescription()
        {
            if (tiaPortal == null)
            {
                return "Not connected";
            }

            if (project == null)
            {
                return "Connected to TIA Portal; no project selected";
            }

            if (plcSoftware == null)
            {
                return "Connected to " + project.Name + "; no unique PLC selected";
            }

            return "Connected to " + project.Name + " / " + selectedDeviceName + " / " + selectedSoftwareName;
        }

        private TiaGatewayStatus CreateStatus(
            bool isConnected,
            bool canExport,
            string description,
            IEnumerable<OpennessDiagnostic> diagnostics)
        {
            return new TiaGatewayStatus(
                TiaGatewayMode.LegacyAdapter,
                isConnected,
                canExport,
                description,
                installation,
                diagnostics);
        }

        private void InvalidatePreview()
        {
            previewTicket = null;
        }

        private static string BuildRequestFingerprint(PreflightPlan plan, TiaExportRequest request)
        {
            var values = new List<string>
            {
                "PROJECT=" + plan.EffectiveProjectPath,
                "DEVICE=" + plan.EffectiveDeviceName,
                "SOFTWARE=" + plan.EffectiveSoftwareName,
                "COMPILE=" + request.CompileAfterImport,
                "SAVE=" + request.SaveProjectAfterExport,
                "TAG_UPDATES=" + request.AllowTagUpdates,
                "BLOCK_OVERWRITE=" + request.AllowOwnedBlockOverwrite,
                "MARKER=" + request.OwnershipMarker
            };

            foreach (var source in plan.Sources)
            {
                values.Add("SOURCE=" + source.Source.ImportOrder.ToString(CultureInfo.InvariantCulture) + "|" +
                    source.Source.Kind + "|" + source.FullPath + "|" + source.ContentHash);
            }

            foreach (var tag in request.Tags)
            {
                values.Add("TAG=" + tag.TableName + "|" + tag.Name + "|" + tag.DataType + "|" +
                    tag.LogicalAddress + "|" + tag.Comment);
            }

            return ComputeHash(values);
        }

        private static string CreateConfirmationToken()
        {
            var bytes = new byte[32];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        private static string ComputeHash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(bytes));
            }
        }

        private static byte[] EncodeUtf8Bom(string content)
        {
            var payload = new UTF8Encoding(false, true).GetBytes(content ?? string.Empty);
            var bytes = new byte[payload.Length + 3];
            bytes[0] = 0xEF;
            bytes[1] = 0xBB;
            bytes[2] = 0xBF;
            Buffer.BlockCopy(payload, 0, bytes, 3, payload.Length);
            return bytes;
        }

        private static string ComputeHash(IEnumerable<string> values)
        {
            var builder = new StringBuilder();
            foreach (var value in values)
            {
                var normalized = value ?? string.Empty;
                builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(normalized);
            }

            return ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        }

        private static string ToHex(IEnumerable<byte> bytes)
        {
            var builder = new StringBuilder();
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string CreateExternalSourceName(string path)
        {
            var baseName = Path.GetFileNameWithoutExtension(path) ?? "Source";
            var safeName = new string(baseName
                .Where(character => char.IsLetterOrDigit(character) || character == '_')
                .Take(32)
                .ToArray());
            if (string.IsNullOrEmpty(safeName))
            {
                safeName = "Source";
            }

            return "TSS_" + Guid.NewGuid().ToString("N") + "_" + safeName;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string GetProjectPath(Project selectedProject)
        {
            try
            {
                return selectedProject == null || selectedProject.Path == null
                    ? string.Empty
                    : selectedProject.Path.FullName;
            }
            catch (NonRecoverableException)
            {
                throw;
            }
            catch (EngineeringSecurityException)
            {
                throw;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool TryGetFullPath(string path, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(path.Trim().Trim('"'));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            string normalizedLeft;
            string normalizedRight;
            return TryGetFullPath(left, out normalizedLeft) &&
                TryGetFullPath(right, out normalizedRight) &&
                string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathInside(string path, string rootPath)
        {
            string normalizedPath;
            string normalizedRoot;
            if (!TryGetFullPath(path, out normalizedPath) || !TryGetFullPath(rootPath, out normalizedRoot))
            {
                return false;
            }

            var boundary = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
        }

        private static TiaExportPreview FailedPreview(OpennessDiagnostic diagnostic)
        {
            return new TiaExportPreview(false, string.Empty, null, null, new[] { diagnostic });
        }

        private static TiaExportResult Rejected(OpennessDiagnostic diagnostic)
        {
            return new TiaExportResult(
                TiaExportOutcome.Rejected,
                0,
                false,
                new[] { diagnostic });
        }

        private static OpennessDiagnostic Error(string code, string message, string location = null)
        {
            return new OpennessDiagnostic(code, DiagnosticSeverity.Error, message, location);
        }

        private static OpennessDiagnostic ExceptionDiagnostic(
            string code,
            string message,
            string location,
            Exception exception,
            DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            return new OpennessDiagnostic(
                code,
                severity,
                BuildDetailedExceptionMessage(message, exception),
                location,
                exception == null ? string.Empty : exception.GetType().FullName);
        }

        private static string BuildDetailedExceptionMessage(string message, Exception exception)
        {
            var parts = new List<string>();
            AddExceptionMessagePart(parts, message);

            var current = exception;
            var depth = 0;
            while (current != null && depth < 8)
            {
                AddExceptionMessagePart(parts, current.Message);

                var engineeringException = current as EngineeringException;
                if (engineeringException != null)
                {
                    AddExceptionMessageData(parts, engineeringException.MessageData);
                    if (engineeringException.DetailMessageData != null)
                    {
                        foreach (var detail in engineeringException.DetailMessageData)
                        {
                            AddExceptionMessageData(parts, detail);
                        }
                    }
                }

                current = current.InnerException;
                depth++;
            }

            var result = string.Join(" | ", parts);
            const int maximumLength = 4096;
            return result.Length <= maximumLength
                ? result
                : result.Substring(0, maximumLength - 3) + "...";
        }

        private static void AddExceptionMessageData(
            ICollection<string> parts,
            ExceptionMessageData messageData)
        {
            AddExceptionMessagePart(parts, messageData.Text);
            AddExceptionMessagePart(parts, messageData.DetailText);
        }

        private static void AddExceptionMessagePart(ICollection<string> parts, string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (normalized.Length == 0 || parts.Any(existing =>
                string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            parts.Add(normalized);
        }

        private sealed class PreviewTicket
        {
            public PreviewTicket(string token, string requestFingerprint, string planFingerprint)
            {
                Token = token;
                RequestFingerprint = requestFingerprint;
                PlanFingerprint = planFingerprint;
            }

            public string Token { get; private set; }

            public string RequestFingerprint { get; private set; }

            public string PlanFingerprint { get; private set; }
        }

        private sealed class LibraryReadCandidate
        {
            public LibraryReadCandidate(
                IGenerateSource sourceObject,
                PlcExternalSourceSystemGroup externalSourceGroup,
                TiaLibrarySourceKind kind,
                string name,
                string groupPath,
                string programmingLanguage,
                bool isKnowHowProtected)
            {
                SourceObject = sourceObject ?? throw new ArgumentNullException(nameof(sourceObject));
                ExternalSourceGroup = externalSourceGroup;
                Kind = kind;
                Name = name ?? string.Empty;
                GroupPath = groupPath ?? string.Empty;
                ProgrammingLanguage = programmingLanguage ?? string.Empty;
                IsKnowHowProtected = isKnowHowProtected;
            }

            public IGenerateSource SourceObject { get; private set; }

            public PlcExternalSourceSystemGroup ExternalSourceGroup { get; private set; }

            public TiaLibrarySourceKind Kind { get; private set; }

            public string Name { get; private set; }

            public string GroupPath { get; private set; }

            public string ProgrammingLanguage { get; private set; }

            public bool IsKnowHowProtected { get; private set; }

            public string Location
            {
                get
                {
                    return string.IsNullOrEmpty(GroupPath)
                        ? Name
                        : GroupPath + "/" + Name;
                }
            }
        }

        private sealed class PlcCandidate
        {
            public PlcCandidate(
                string deviceName,
                string itemName,
                DeviceItem cpuItem,
                PlcSoftware software)
            {
                DeviceName = deviceName ?? string.Empty;
                ItemName = itemName ?? string.Empty;
                CpuItem = cpuItem;
                Software = software;
                SoftwareName = software == null ? string.Empty : software.Name;
            }

            public string DeviceName { get; private set; }

            public string ItemName { get; private set; }

            public DeviceItem CpuItem { get; private set; }

            public PlcSoftware Software { get; private set; }

            public string SoftwareName { get; private set; }

            public string DisplayName
            {
                get { return DeviceName + "/" + ItemName + "/" + SoftwareName; }
            }
        }

        private sealed class ValidatedSource
        {
            public ValidatedSource(
                TiaSourceFile source,
                string fullPath,
                string contentHash,
                byte[] importBytes,
                IEnumerable<SclDeclaration> declarations)
            {
                Source = source;
                FullPath = fullPath;
                ContentHash = contentHash;
                ImportBytes = (byte[])importBytes.Clone();
                Declarations = new List<SclDeclaration>(declarations);
            }

            public TiaSourceFile Source { get; private set; }

            public string FullPath { get; private set; }

            public string ContentHash { get; private set; }

            public byte[] ImportBytes { get; private set; }

            public IList<SclDeclaration> Declarations { get; private set; }
        }

        private sealed class StagedSourceFile
        {
            public StagedSourceFile(string path)
            {
                Path = path;
            }

            public string Path { get; private set; }
        }

        private enum TagAction
        {
            None,
            Create,
            Update
        }

        private sealed class TagPlan
        {
            public TagPlan(
                TiaPlcTag definition,
                TagAction action,
                PlcTagTable existingTable,
                PlcTag existingTag)
            {
                Definition = definition;
                Action = action;
                ExistingTable = existingTable;
                ExistingTag = existingTag;
            }

            public TiaPlcTag Definition { get; private set; }

            public TagAction Action { get; private set; }

            public PlcTagTable ExistingTable { get; private set; }

            public PlcTag ExistingTag { get; private set; }
        }

        private sealed class TagLocation
        {
            public TagLocation(PlcTagTable table, PlcTag tag)
            {
                Table = table;
                Tag = tag;
            }

            public PlcTagTable Table { get; private set; }

            public PlcTag Tag { get; private set; }
        }

        private sealed class PreflightPlan
        {
            private readonly List<TiaExportOperation> operations = new List<TiaExportOperation>();
            private readonly List<TiaExportCollision> collisions = new List<TiaExportCollision>();
            private readonly List<string> state = new List<string>();

            public PreflightPlan(
                string effectiveProjectPath,
                string effectiveDeviceName,
                string effectiveSoftwareName,
                TiaExportRequest request)
            {
                EffectiveProjectPath = effectiveProjectPath ?? string.Empty;
                EffectiveDeviceName = effectiveDeviceName ?? string.Empty;
                EffectiveSoftwareName = effectiveSoftwareName ?? string.Empty;
                Request = request;
                Diagnostics = new List<OpennessDiagnostic>();
                Sources = new List<ValidatedSource>();
                TagPlans = new List<TagPlan>();
                RequestFingerprint = string.Empty;
                PlanFingerprint = string.Empty;
            }

            public TiaExportRequest Request { get; private set; }

            public Project Project { get; set; }

            public PlcSoftware Software { get; set; }

            public string EffectiveProjectPath { get; set; }

            public string EffectiveDeviceName { get; set; }

            public string EffectiveSoftwareName { get; set; }

            public IList<OpennessDiagnostic> Diagnostics { get; private set; }

            public IList<ValidatedSource> Sources { get; private set; }

            public IList<TagPlan> TagPlans { get; private set; }

            public string RequestFingerprint { get; private set; }

            public string PlanFingerprint { get; private set; }

            public bool HasErrors
            {
                get { return Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error); }
            }

            public bool CanExecute
            {
                get { return !HasErrors && collisions.All(item => item.CanOverwrite); }
            }

            public void AddDiagnostic(OpennessDiagnostic diagnostic)
            {
                Diagnostics.Add(diagnostic);
            }

            public void AddCollision(TiaExportCollision collision)
            {
                collisions.Add(collision);
            }

            public void AddOperation(TiaExportOperationKind kind, string target, string description)
            {
                operations.Add(new TiaExportOperation(operations.Count, kind, target, description));
            }

            public void InsertTransactionalOperation(string description)
            {
                operations.Insert(
                    Math.Min(2, operations.Count),
                    new TiaExportOperation(0, TiaExportOperationKind.TransactionalImport, EffectiveProjectPath, description));
                RenumberOperations();
            }

            public void AddState(string key, string value)
            {
                state.Add((key ?? string.Empty) + "=" + (value ?? string.Empty));
            }

            public void SetRequestFingerprint(string fingerprint)
            {
                RequestFingerprint = fingerprint ?? string.Empty;
            }

            public void FinalizeFingerprints()
            {
                var planValues = new List<string>
                {
                    "REQUEST=" + RequestFingerprint
                };
                planValues.AddRange(state.OrderBy(value => value, StringComparer.Ordinal));
                planValues.AddRange(operations.Select(operation =>
                    "OP=" + operation.Order.ToString(CultureInfo.InvariantCulture) + "|" + operation.Kind + "|" +
                    operation.Target + "|" + operation.Description));
                planValues.AddRange(collisions.Select(collision =>
                    "COLLISION=" + collision.Kind + "|" + collision.Target + "|" + collision.IsOwned + "|" +
                    collision.CanOverwrite + "|" + collision.Description));
                planValues.AddRange(Diagnostics.Select(diagnostic =>
                    "DIAGNOSTIC=" + diagnostic.Code + "|" + diagnostic.Severity + "|" +
                    diagnostic.Location + "|" + diagnostic.ExceptionType + "|" + diagnostic.Message));
                PlanFingerprint = ComputeHash(planValues);
            }

            public TiaExportPreview ToPreview(string token)
            {
                return new TiaExportPreview(CanExecute, token, operations, collisions, Diagnostics);
            }

            private void RenumberOperations()
            {
                for (var index = 0; index < operations.Count; index++)
                {
                    var operation = operations[index];
                    operations[index] = new TiaExportOperation(
                        index,
                        operation.Kind,
                        operation.Target,
                        operation.Description);
                }
            }
        }
    }
}
