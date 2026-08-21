using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TiaSclStudio.Core.Generation;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Storage;
using TiaSclStudio.Diagram.Generation;
using TiaSclStudio.Openness.Diagnostics;
using TiaSclStudio.Openness.Discovery;
using TiaSclStudio.Openness.Gateway;
using TiaSclStudio.Openness.Legacy.V17;
using TiaSclStudio.Openness.Model;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private const string OnlineDiagnosticPrefix = "TIA_UI";

        private readonly TiaGatewayWorker _tiaGatewayWorker;
        private ITiaGateway _tiaGateway;
        private TiaGatewayStatus _tiaGatewayStatus;
        private TiaHardwareIoCatalog _tiaHardwareIoCatalog;
        private TiaExportPreview _tiaExportPreview;
        private TiaExportRequest _previewedExportRequest;
        private List<DiagnosticItem> _onlineDiagnostics;
        private OpennessDiscoveryResult _opennessDiscovery;
        private List<OpennessInstallation> _legacyInstallations;
        private OpennessInstallation _selectedInstallation;
        private string _connectedTargetFingerprint;
        private TiaConnectionMode? _connectedTiaMode;
        private string _stagingDirectory;
        private string _offlineRuntimeStatus;
        private bool _onlineUiReady;
        private bool _onlineBusy;
        private bool _gatewayDisposed;
        private bool _headlessUnsavedChangesRisk;

        private void InitializeOnlineWorkflow()
        {
            _onlineDiagnostics = new List<DiagnosticItem>();
            _offlineRuntimeStatus = RuntimeStatusText.Text;

            var startupDiagnostics = new List<OpennessDiagnostic>();
            _opennessDiscovery = DiscoverOpenness(startupDiagnostics);
            var legacyFactory = new LegacyV17GatewayAdapterFactory();
            _legacyInstallations = _opennessDiscovery.Installations
                .Where(item =>
                    item.Family == OpennessApiFamily.LegacyV17ToV20 &&
                    item.ApiVersion.Major == 17 &&
                    legacyFactory.Supports(item))
                .OrderBy(item => item.PortalVersion)
                .ToList();
            _tiaGateway = new OfflineGateway(_opennessDiscovery);
            _onlineUiReady = true;

            try
            {
                _tiaGatewayStatus = _tiaGateway.GetStatus();
                startupDiagnostics.AddRange(_tiaGatewayStatus.Diagnostics);
            }
            catch (Exception exception)
            {
                startupDiagnostics.Add(CreateUiOpennessDiagnostic(
                    OnlineDiagnosticPrefix + "001",
                    DiagnosticSeverity.Error,
                    "Не удалось прочитать состояние TIA Openness gateway.",
                    exception));
            }

            ReplaceOnlineDiagnostics(startupDiagnostics);
            ApplyGatewayStatus(_tiaGatewayStatus);
            UpdateOnlineCommandState();
        }

        private static OpennessDiscoveryResult DiscoverOpenness(
            ICollection<OpennessDiagnostic> startupDiagnostics)
        {
            try
            {
                return new OpennessInstallationLocator().Locate();
            }
            catch (Exception exception)
            {
                startupDiagnostics.Add(CreateUiOpennessDiagnostic(
                    OnlineDiagnosticPrefix + "002",
                    DiagnosticSeverity.Error,
                    "Не удалось выполнить поиск установленного TIA Portal Openness.",
                    exception));
                return new OpennessDiscoveryResult(null, startupDiagnostics);
            }
        }

        private void OnlineWorkflow_Click(object sender, RoutedEventArgs e)
        {
            OnlinePanel.Visibility = Visibility.Visible;
            UpdateOnlineCommandState();
            SetStatus("Панель TIA Openness открыта; автоматического подключения нет");
        }

        private void CloseOnlinePanel_Click(object sender, RoutedEventArgs e)
        {
            if (_onlineBusy)
            {
                SetStatus("Дождитесь завершения операции TIA Openness");
                return;
            }

            OnlinePanel.Visibility = Visibility.Collapsed;
        }

        private void BrowseTiaProject_Click(object sender, RoutedEventArgs e)
        {
            if (_onlineBusy)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Выбрать проект TIA Portal",
                Filter = "TIA Portal project (*.ap17;*.ap18;*.ap19;*.ap20)|*.ap17;*.ap18;*.ap19;*.ap20|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            var currentPath = TiaProjectPathTextBox.Text == null
                ? string.Empty
                : TiaProjectPathTextBox.Text.Trim();
            if (File.Exists(currentPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(currentPath));
                dialog.FileName = Path.GetFileName(currentPath);
            }

            if (dialog.ShowDialog(this) == true)
            {
                TiaProjectPathTextBox.Text = dialog.FileName;
            }
        }

        private void OnlineTarget_Changed(object sender, RoutedEventArgs e)
        {
            if (!_onlineUiReady)
            {
                return;
            }

            InvalidateOnlinePreview();
            _tiaHardwareIoCatalog = null;
            if (_tiaGatewayStatus != null &&
                _tiaGatewayStatus.IsConnected &&
                !IsConnectedToCurrentTarget())
            {
                OnlineStatusText.Text = "Цель изменена — подключитесь повторно";
                OnlineStatusDot.Fill = GetBrush("WarningBrush");
            }

            UpdateOnlineCommandState();
        }

        private async void ConnectTia_Click(object sender, RoutedEventArgs e)
        {
            if (_onlineBusy)
            {
                return;
            }

            TiaConnectionRequest request;
            string validationError;
            if (!TryCreateConnectionRequest(out request, out validationError))
            {
                ShowOnlineInputError("005", validationError);
                return;
            }

            OpennessInstallation installation;
            if (!TrySelectInstallation(request, out installation, out validationError))
            {
                ShowOnlineInputError("013", validationError);
                return;
            }

            InvalidateOnlinePreview();
            SetOnlineBusy(
                true,
                "Подключение к Portal V" + installation.PortalVersion.Major +
                " через API V" + installation.ApiVersion.Major + "…");
            try
            {
                var currentGateway = _tiaGateway;
                var replaceGateway = _selectedInstallation == null ||
                    !string.Equals(
                        _selectedInstallation.AssemblyPath,
                        installation.AssemblyPath,
                        StringComparison.OrdinalIgnoreCase);
                var response = await _tiaGatewayWorker.InvokeAsync(() =>
                {
                    var gateway = currentGateway;
                    if (replaceGateway)
                    {
                        var factory = new LegacyV17GatewayAdapterFactory();
                        if (!factory.Supports(installation))
                        {
                            throw new InvalidOperationException(
                                "Выбранная установка Portal/API не поддерживается legacy V17 adapter.");
                        }

                        gateway = factory.Create(installation);
                    }

                    try
                    {
                        var connectedStatus = gateway.Connect(request);
                        TiaHardwareIoCatalog hardwareIoCatalog = null;
                        if (connectedStatus != null && connectedStatus.IsConnected)
                        {
                            hardwareIoCatalog = gateway.ReadHardwareIoCatalog();
                            connectedStatus = gateway.GetStatus();
                        }
                        if (replaceGateway)
                        {
                            try
                            {
                                currentGateway.Dispose();
                            }
                            catch (Exception)
                            {
                                // The new gateway is already active; stale-session cleanup
                                // must not hide a successful explicit reconnection.
                            }
                        }

                        return new GatewayConnectResponse(gateway, connectedStatus, hardwareIoCatalog);
                    }
                    catch (Exception)
                    {
                        if (replaceGateway)
                        {
                            gateway.Dispose();
                        }

                        throw;
                    }
                });

                if (replaceGateway)
                {
                    _tiaGateway = response.Gateway;
                    _selectedInstallation = installation;
                }

                var status = response.Status;
                _tiaGatewayStatus = status;
                _tiaHardwareIoCatalog = status != null && status.IsConnected
                    ? response.HardwareIoCatalog
                    : null;
                _connectedTargetFingerprint = status != null && status.IsConnected
                    ? BuildConnectionFingerprint()
                    : string.Empty;
                if (status != null &&
                    status.IsConnected &&
                    !status.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
                {
                    _connectedTiaMode = status.Diagnostics.Any(item =>
                        string.Equals(
                            item.Code,
                            OpennessDiagnosticCodes.ConnectedHeadless,
                            StringComparison.Ordinal))
                        ? TiaConnectionMode.StartWithoutUserInterface
                        : request.Mode;
                    _headlessUnsavedChangesRisk = false;
                }

                ReplaceOnlineDiagnostics(MergeGatewayAndHardwareDiagnostics(
                    status,
                    _tiaHardwareIoCatalog));
                ApplyGatewayStatus(status);
                SetStatus(status != null && status.IsConnected
                    ? "Portal V" + installation.PortalVersion.Major +
                      " подключён через API V17; перед экспортом выполните dry-run"
                    : "Подключение TIA Portal не выполнено — проверьте диагностику");
            }
            catch (Exception exception)
            {
                _connectedTargetFingerprint = string.Empty;
                _tiaHardwareIoCatalog = null;
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "006",
                        DiagnosticSeverity.Error,
                        "Ошибка подключения к TIA Portal.",
                        exception)
                });
                ApplyGatewayStatus(null);
                SetStatus("Ошибка подключения TIA Portal");
            }
            finally
            {
                SetOnlineBusy(false, string.Empty);
            }
        }

        private async void PreviewExport_Click(object sender, RoutedEventArgs e)
        {
            if (_onlineBusy)
            {
                return;
            }

            if (_tiaGatewayStatus == null ||
                !_tiaGatewayStatus.IsConnected ||
                !_tiaGatewayStatus.CanExport ||
                !IsConnectedToCurrentTarget())
            {
                ShowOnlineInputError("007", "Сначала подключитесь к выбранному проекту и PLC.");
                return;
            }

            TiaExportRequest request;
            string validationError;
            try
            {
                if (!TryCreateExportRequest(out request, out validationError))
                {
                    ShowOnlineInputError("008", validationError);
                    return;
                }
            }
            catch (Exception exception)
            {
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "009",
                        DiagnosticSeverity.Error,
                        "Не удалось подготовить временный пакет SCL для dry-run.",
                        exception)
                });
                SetStatus("Dry-run не подготовлен");
                return;
            }

            SetOnlineBusy(true, "TIA проверяет план без внесения изменений…");
            try
            {
                var response = await _tiaGatewayWorker.InvokeAsync(() =>
                {
                    var preview = _tiaGateway.PreviewExport(request);
                    var status = _tiaGateway.GetStatus();
                    return new GatewayPreviewResponse(preview, status);
                });
                _tiaGatewayStatus = response.Status;
                if (response.Status == null || !response.Status.IsConnected)
                {
                    _tiaHardwareIoCatalog = null;
                }
                ApplyExportPreview(response.Preview, request);
                ReplaceOnlineDiagnostics(response.Preview == null ? null : response.Preview.Diagnostics);
                ApplyGatewayStatus(response.Status);
                SetStatus(response.Preview != null && response.Preview.CanExecute
                    ? "Dry-run готов; проверьте операции и подтвердите экспорт"
                    : "Dry-run заблокировал экспорт — проверьте коллизии и диагностику");
            }
            catch (Exception exception)
            {
                InvalidateOnlinePreview();
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "010",
                        DiagnosticSeverity.Error,
                        "Ошибка во время dry-run TIA Openness.",
                        exception)
                });
                SetStatus("Dry-run завершился ошибкой");
            }
            finally
            {
                SetOnlineBusy(false, string.Empty);
            }
        }

        private async void ConfirmExport_Click(object sender, RoutedEventArgs e)
        {
            if (_onlineBusy ||
                _tiaExportPreview == null ||
                _previewedExportRequest == null ||
                !_tiaExportPreview.CanExecute ||
                string.IsNullOrWhiteSpace(_tiaExportPreview.ConfirmationToken))
            {
                return;
            }

            if (!IsConnectedToCurrentTarget())
            {
                InvalidateOnlinePreview();
                ShowOnlineInputError("011", "Цель подключения изменилась. Подключитесь и повторите dry-run.");
                return;
            }

            var confirmation = MessageBox.Show(
                BuildGeneralConfirmationText(),
                "Подтверждение изменений TIA Portal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                SetStatus("Экспорт отменён пользователем; TIA не изменена");
                return;
            }

            var destructiveCollisions = _tiaExportPreview.Collisions
                .Where(item => item.CanOverwrite)
                .ToList();
            if (destructiveCollisions.Count > 0)
            {
                var destructiveConfirmation = MessageBox.Show(
                    BuildCollisionConfirmationText(destructiveCollisions),
                    "Отдельное подтверждение перезаписи",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Stop,
                    MessageBoxResult.No);
                if (destructiveConfirmation != MessageBoxResult.Yes)
                {
                    SetStatus("Перезапись отменена пользователем; TIA не изменена");
                    return;
                }
            }

            var request = _previewedExportRequest;
            var token = _tiaExportPreview.ConfirmationToken;
            SetOnlineBusy(true, "Выгрузка подтверждённого плана в TIA Portal…");
            try
            {
                var response = await _tiaGatewayWorker.InvokeAsync(() =>
                {
                    var result = _tiaGateway.Export(request, token);
                    var status = _tiaGateway.GetStatus();
                    return new GatewayExportResponse(result, status);
                });

                _tiaGatewayStatus = response.Status;
                if (response.Status == null || !response.Status.IsConnected)
                {
                    _tiaHardwareIoCatalog = null;
                }
                UpdateHeadlessUnsavedChangesRisk(request, response.Result);
                ReplaceOnlineDiagnostics(response.Result == null ? null : response.Result.Diagnostics);
                ApplyGatewayStatus(response.Status);

                if (response.Result != null && response.Result.Outcome == TiaExportOutcome.Succeeded)
                {
                    SetStatus(
                        "Выгрузка завершена: импортировано источников " +
                        response.Result.ImportedSourceCount +
                        (response.Result.CompilationAttempted ? ", компиляция запущена" : string.Empty));
                }
                else
                {
                    SetStatus("Выгрузка не выполнена или завершилась ошибкой — смотрите диагностику");
                }
            }
            catch (Exception exception)
            {
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "012",
                        DiagnosticSeverity.Error,
                        "Ошибка подтверждённой выгрузки в TIA Portal.",
                        exception)
                });
                SetStatus("Выгрузка завершилась ошибкой");
            }
            finally
            {
                InvalidateOnlinePreview();
                SetOnlineBusy(false, string.Empty);
            }
        }

        private void UpdateHeadlessUnsavedChangesRisk(
            TiaExportRequest request,
            TiaExportResult result)
        {
            if (_connectedTiaMode != TiaConnectionMode.StartWithoutUserInterface ||
                request == null ||
                result == null)
            {
                return;
            }

            if (result.Diagnostics.Any(item =>
                string.Equals(
                    item.Code,
                    OpennessDiagnosticCodes.SessionLost,
                    StringComparison.Ordinal)))
            {
                _headlessUnsavedChangesRisk = false;
                return;
            }

            if (request.SaveProjectAfterExport &&
                result.Outcome == TiaExportOutcome.Succeeded)
            {
                _headlessUnsavedChangesRisk = false;
                return;
            }

            var saveFailureDiagnostic = result.Diagnostics.Any(item =>
                string.Equals(
                    item.Code,
                    OpennessDiagnosticCodes.SaveSkippedCompileErrors,
                    StringComparison.Ordinal) ||
                string.Equals(
                    item.Code,
                    OpennessDiagnosticCodes.ProjectSaveFailed,
                    StringComparison.Ordinal));

            if (result.ImportedSourceCount > 0 &&
                (result.Outcome != TiaExportOutcome.Succeeded || saveFailureDiagnostic))
            {
                _headlessUnsavedChangesRisk = true;
            }
        }

        private bool TryCreateConnectionRequest(out TiaConnectionRequest request, out string error)
        {
            request = null;
            error = string.Empty;

            var projectPath = NormalizePathInput(TiaProjectPathTextBox.Text);
            if (string.IsNullOrEmpty(projectPath))
            {
                error = "Укажите путь к проекту TIA Portal.";
                return false;
            }

            if (!File.Exists(projectPath))
            {
                error = "Файл проекта TIA Portal не найден: " + projectPath;
                return false;
            }

            int? processId;
            if (!TryReadProcessId(out processId, out error))
            {
                return false;
            }

            var mode = GetSelectedConnectionMode();
            if (mode != TiaConnectionMode.AttachToRunning && processId.HasValue)
            {
                error = "Process ID допустим только в режиме подключения к запущенному TIA Portal.";
                return false;
            }

            try
            {
                request = new TiaConnectionRequest(
                    mode,
                    processId,
                    projectPath,
                    TiaDeviceNameTextBox.Text,
                    TiaSoftwareNameTextBox.Text);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private bool TrySelectInstallation(
            TiaConnectionRequest request,
            out OpennessInstallation installation,
            out string error)
        {
            installation = null;
            error = string.Empty;

            int portalMajor;
            if (!TryReadProjectPortalMajor(request.ProjectPath, out portalMajor))
            {
                error = "Версия проекта должна быть явно указана расширением .ap17–.ap20. " +
                    "Автоматическое обновление проекта запрещено.";
                return false;
            }

            if (request.Mode == TiaConnectionMode.AttachToRunning &&
                _legacyInstallations.Select(item => item.PortalVersion.Major).Distinct().Count() > 1 &&
                !request.ProcessId.HasValue)
            {
                error = "Установлено несколько версий TIA Portal. Для Attach укажите Process ID " +
                    "нужного процесса; автоматический выбор запрещён.";
                return false;
            }

            var candidates = _legacyInstallations
                .Where(item => item.PortalVersion.Major == portalMajor)
                .GroupBy(item => item.AssemblyPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (candidates.Count == 0)
            {
                error = "Для проекта .ap" + portalMajor +
                    " не найдена установленная Portal V" + portalMajor +
                    " с API V17. Проект не будет молча обновлён другой версией.";
                return false;
            }

            if (candidates.Count > 1)
            {
                error = "Для Portal V" + portalMajor +
                    " найдено несколько API-кандидатов. Нужен явный выбор установки.";
                return false;
            }

            installation = candidates[0];
            return true;
        }

        private static bool TryReadProjectPortalMajor(string projectPath, out int portalMajor)
        {
            portalMajor = 0;
            var extension = Path.GetExtension(projectPath ?? string.Empty);
            if (string.IsNullOrEmpty(extension) ||
                !extension.StartsWith(".ap", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(extension.Substring(3), out portalMajor))
            {
                return false;
            }

            return portalMajor >= 17 && portalMajor <= 20;
        }

        private bool TryCreateExportRequest(out TiaExportRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (_project == null || _sheet == null)
            {
                error = "Нет открытого листа вызовов.";
                return false;
            }

            var compilation = _projectCompiler.Compile(_project);
            if (!compilation.IsSuccess || compilation.GeneratedSources.Count == 0)
            {
                error = "Модель содержит ошибки или не сформировала пакет SCL. Сначала исправьте общую диагностику.";
                return false;
            }

            var projectPath = NormalizePathInput(TiaProjectPathTextBox.Text);
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
            {
                error = "Выбранный файл проекта TIA Portal больше недоступен.";
                return false;
            }

            RecreateStagingDirectory();
            ProjectStorage.WriteSources(_stagingDirectory, compilation.GeneratedSources);

            var sources = compilation.GeneratedSources
                .Select(source => new TiaSourceFile(
                    Path.Combine(_stagingDirectory, source.FileName),
                    source.Order,
                    MapSourceKind(source.Kind)))
                .ToList();

            var tableName = "TIA_SCL_" + GetProjectIdPrefix(6);
            var tags = _project.Plant.Tags
                .Select(tag => new TiaPlcTag(
                    tableName,
                    tag.Name,
                    tag.DataType,
                    tag.Address,
                    tag.Comment))
                .ToList();

            request = new TiaExportRequest(
                projectPath,
                sources,
                CompileAfterImportCheckBox.IsChecked == true,
                TiaDeviceNameTextBox.Text,
                TiaSoftwareNameTextBox.Text,
                tags,
                AllowTagUpdatesCheckBox.IsChecked == true,
                "TS" + GetProjectIdPrefix(6),
                AllowOwnedOverwriteCheckBox.IsChecked == true,
                SaveTiaProjectCheckBox.IsChecked == true);
            return true;
        }

        private static TiaSourceKind MapSourceKind(GeneratedSourceKind kind)
        {
            switch (kind)
            {
                case GeneratedSourceKind.DataTypes:
                    return TiaSourceKind.Types;
                case GeneratedSourceKind.Block:
                    return TiaSourceKind.Block;
                case GeneratedSourceKind.InstanceDataBlocks:
                    return TiaSourceKind.Instances;
                case GeneratedSourceKind.CallBlock:
                    return TiaSourceKind.Calls;
                case GeneratedSourceKind.CallBlockInstanceDataBlock:
                    return TiaSourceKind.CallBlockInstance;
                default:
                    throw new ArgumentOutOfRangeException("kind", kind, "Unsupported generated source kind.");
            }
        }

        private void ApplyExportPreview(TiaExportPreview preview, TiaExportRequest request)
        {
            _tiaExportPreview = preview;
            _previewedExportRequest = preview == null ? null : request;
            ExportOperationsGrid.ItemsSource = preview == null ? null : preview.Operations;

            var operationCount = preview == null ? 0 : preview.Operations.Count;
            var collisionCount = preview == null ? 0 : preview.Collisions.Count;
            if (preview == null)
            {
                ExportPreviewSummaryText.Text = "TIA не вернула план. Экспорт заблокирован.";
                CollisionWarningPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ExportPreviewSummaryText.Text = preview.CanExecute
                    ? "План разрешён: операций " + operationCount +
                      ", коллизий " + collisionCount +
                      ". Просмотрите список перед подтверждением."
                    : "План отклонён: операций " + operationCount +
                      ", коллизий " + collisionCount +
                      ". Экспорт заблокирован.";
                ShowCollisionSummary(preview.Collisions);
            }

            UpdateOnlineCommandState();
        }

        private void ShowCollisionSummary(IEnumerable<TiaExportCollision> collisions)
        {
            var collisionList = (collisions ?? new TiaExportCollision[0]).ToList();
            if (collisionList.Count == 0)
            {
                CollisionWarningPanel.Visibility = Visibility.Collapsed;
                CollisionSummaryText.Text = string.Empty;
                return;
            }

            CollisionWarningPanel.Visibility = Visibility.Visible;
            var lines = collisionList.Take(8).Select(item =>
                item.Kind + " · " + item.Target + " · " +
                (item.CanOverwrite
                    ? (item.IsOwned ? "свой объект, перезапись разрешима" : "перезапись разрешима")
                    : "перезапись запрещена"));
            CollisionSummaryText.Text = string.Join(Environment.NewLine, lines) +
                (collisionList.Count > 8
                    ? Environment.NewLine + "…и ещё " + (collisionList.Count - 8)
                    : string.Empty);
        }

        private string BuildGeneralConfirmationText()
        {
            return "Будет изменён проект TIA Portal:\n" +
                _previewedExportRequest.ProjectPath + "\n\n" +
                "PLC: " + EmptyAsAuto(_previewedExportRequest.TargetDeviceName) + "\n" +
                "PLC Software: " + EmptyAsAuto(_previewedExportRequest.TargetSoftwareName) + "\n" +
                "Операций по подтверждённому плану: " + _tiaExportPreview.Operations.Count + "\n" +
                "Компиляция: " + YesNo(_previewedExportRequest.CompileAfterImport) + "\n" +
                "Сохранение проекта TIA: " + YesNo(_previewedExportRequest.SaveProjectAfterExport) + "\n\n" +
                "OB1 не изменяется. Продолжить?";
        }

        private static string BuildCollisionConfirmationText(IList<TiaExportCollision> collisions)
        {
            var lines = collisions.Take(10).Select(item =>
                "• " + item.Kind + " · " + item.Target + " — " + item.Description);
            return "Dry-run обнаружил объекты, которые разрешено перезаписать:\n\n" +
                string.Join(Environment.NewLine, lines) +
                (collisions.Count > 10
                    ? Environment.NewLine + "…и ещё " + (collisions.Count - 10)
                    : string.Empty) +
                "\n\nЭто отдельное подтверждение потенциально разрушительной операции. Продолжить?";
        }

        private void ApplyGatewayStatus(TiaGatewayStatus status)
        {
            _tiaGatewayStatus = status;
            var installation = status == null ? null : status.Installation;
            var installationText = installation == null
                ? "API не выбран"
                : "Portal V" + installation.PortalVersion.Major +
                  " · API V" + installation.ApiVersion.Major;

            if (status != null && status.IsConnected)
            {
                OnlineStatusText.Text = "Подключено к TIA Portal";
                OnlineStatusDot.Fill = GetBrush("SuccessBrush");
                HeaderModeText.Text = "VISUAL CALL SHEET · TIA ONLINE";
                RuntimeStatusText.Text = _offlineRuntimeStatus.Replace("OFFLINE", "ONLINE");
            }
            else if ((status != null && status.Mode != TiaGatewayMode.Offline) ||
                (_legacyInstallations != null && _legacyInstallations.Count > 0))
            {
                OnlineStatusText.Text = "Gateway готов · не подключено";
                OnlineStatusDot.Fill = GetBrush("WarningBrush");
                HeaderModeText.Text = "VISUAL CALL SHEET · OFFLINE MODE";
                RuntimeStatusText.Text = _offlineRuntimeStatus;
            }
            else
            {
                OnlineStatusText.Text = "Openness-адаптер недоступен";
                OnlineStatusDot.Fill = GetBrush("ErrorBrush");
                HeaderModeText.Text = "VISUAL CALL SHEET · OFFLINE MODE";
                RuntimeStatusText.Text = _offlineRuntimeStatus;
            }

            var discoveredPortals = _legacyInstallations == null
                ? string.Empty
                : string.Join(
                    ", ",
                    _legacyInstallations
                        .Select(item => "Portal V" + item.PortalVersion.Major + " / API V17")
                        .Distinct());
            OnlineApiText.Text = installation != null
                ? (status.Description + " · " + installationText)
                : (string.IsNullOrEmpty(discoveredPortals)
                    ? "Поддерживаемый API V17 не найден"
                    : "Доступно: " + discoveredPortals + ". Версия выбирается по .apNN");
            UpdateOnlineCommandState();
        }

        private static IEnumerable<OpennessDiagnostic> MergeGatewayAndHardwareDiagnostics(
            TiaGatewayStatus status,
            TiaHardwareIoCatalog catalog)
        {
            var diagnostics = new List<OpennessDiagnostic>();
            if (status != null)
            {
                diagnostics.AddRange(status.Diagnostics);
            }

            if (catalog != null)
            {
                diagnostics.AddRange(catalog.Diagnostics);
            }

            return diagnostics
                .GroupBy(item => new
                {
                    item.Code,
                    item.Severity,
                    item.Message,
                    item.Location,
                    item.ExceptionType
                })
                .Select(group => group.First())
                .ToList();
        }

        private bool IsHardwareCatalogOnlineRequired()
        {
            return _tiaGatewayStatus != null &&
                   _tiaGatewayStatus.IsConnected &&
                   IsConnectedToCurrentTarget();
        }

        private async Task<TiaHardwareIoCatalog> RefreshHardwareIoCatalogAsync()
        {
            if (!IsHardwareCatalogOnlineRequired())
            {
                _tiaHardwareIoCatalog = null;
                return null;
            }

            SetOnlineBusy(true, "Чтение фактических каналов из конфигурации PLC…");
            try
            {
                var response = await _tiaGatewayWorker.InvokeAsync(() =>
                {
                    var catalog = _tiaGateway.ReadHardwareIoCatalog();
                    var status = _tiaGateway.GetStatus();
                    return new GatewayHardwareIoResponse(catalog, status);
                });

                _tiaGatewayStatus = response.Status;
                _tiaHardwareIoCatalog = response.Status != null && response.Status.IsConnected
                    ? response.Catalog
                    : null;
                ReplaceOnlineDiagnostics(MergeGatewayAndHardwareDiagnostics(
                    response.Status,
                    _tiaHardwareIoCatalog));
                ApplyGatewayStatus(response.Status);
                SetStatus(_tiaHardwareIoCatalog != null && _tiaHardwareIoCatalog.IsAvailable
                    ? "Конфигурация PLC обновлена: доступно каналов " +
                      _tiaHardwareIoCatalog.Channels.Count
                    : "Аппаратные каналы PLC прочитать не удалось — ввод I/O заблокирован");
                return _tiaHardwareIoCatalog;
            }
            catch (Exception exception)
            {
                _tiaHardwareIoCatalog = null;
                ReplaceOnlineDiagnostics(new[]
                {
                    CreateUiOpennessDiagnostic(
                        OnlineDiagnosticPrefix + "014",
                        DiagnosticSeverity.Error,
                        "Не удалось прочитать фактические каналы из конфигурации PLC.",
                        exception)
                });
                SetStatus("Аппаратные каналы PLC прочитать не удалось — ввод I/O заблокирован");
                return null;
            }
            finally
            {
                SetOnlineBusy(false, string.Empty);
            }
        }

        private void SetOnlineBusy(bool busy, string message)
        {
            if (busy)
            {
                CancelPendingConnection();
                CancelNodeDragForOnlineOperation();
                CancelGroupDragForOnlineOperation();
            }

            _onlineBusy = busy;
            EditorWorkspace.IsEnabled = !busy;
            CreateDemoButton.IsEnabled = !busy;
            ValidateProjectButton.IsEnabled = !busy;
            SaveProjectButton.IsEnabled = !busy;
            OpenProjectButton.IsEnabled = !busy;
            OnlineFormPanel.IsEnabled = !busy;
            OnlineWorkflowButton.IsEnabled = !busy;
            OnlineBusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            OnlineBusyText.Text = message ?? string.Empty;
            UpdateSheetCommandState();
            UpdateHistoryCommandState();
            UpdateSelectionCommandState();
            UpdateOnlineCommandState();
        }

        private void UpdateOnlineCommandState()
        {
            if (!_onlineUiReady)
            {
                return;
            }

            ConnectTiaButton.IsEnabled =
                !_onlineBusy &&
                _legacyInstallations != null &&
                _legacyInstallations.Count > 0;
            PreviewExportButton.IsEnabled =
                !_onlineBusy &&
                _tiaGatewayStatus != null &&
                _tiaGatewayStatus.IsConnected &&
                _tiaGatewayStatus.CanExport &&
                IsConnectedToCurrentTarget();
            ConfirmExportButton.IsEnabled =
                !_onlineBusy &&
                _tiaExportPreview != null &&
                _previewedExportRequest != null &&
                _tiaExportPreview.CanExecute &&
                !string.IsNullOrWhiteSpace(_tiaExportPreview.ConfirmationToken) &&
                IsConnectedToCurrentTarget();
        }

        private void InvalidateOnlinePreview()
        {
            _tiaExportPreview = null;
            _previewedExportRequest = null;
            if (!_onlineUiReady)
            {
                return;
            }

            ExportOperationsGrid.ItemsSource = null;
            ExportPreviewSummaryText.Text = "Экспорт заблокирован до успешного dry-run.";
            CollisionWarningPanel.Visibility = Visibility.Collapsed;
            CollisionSummaryText.Text = string.Empty;
            ConfirmExportButton.IsEnabled = false;
            CleanupStagingDirectory();
            UpdateOnlineCommandState();
        }

        private IEnumerable<DiagnosticItem> GetOnlineDiagnostics()
        {
            return _onlineDiagnostics ?? Enumerable.Empty<DiagnosticItem>();
        }

        private void ReplaceOnlineDiagnostics(IEnumerable<OpennessDiagnostic> diagnostics)
        {
            if (_onlineDiagnostics == null)
            {
                return;
            }

            foreach (var previous in _onlineDiagnostics)
            {
                _diagnostics.Remove(previous);
            }

            _onlineDiagnostics.Clear();
            foreach (var diagnostic in diagnostics ?? Enumerable.Empty<OpennessDiagnostic>())
            {
                var item = new DiagnosticItem(
                    MapSeverity(diagnostic.Severity),
                    diagnostic.Code,
                    diagnostic.Message,
                    BuildDiagnosticLocation(diagnostic));
                _onlineDiagnostics.Add(item);
                _diagnostics.Add(item);
            }

            UpdateDiagnosticSummary();
        }

        private void ShowOnlineInputError(string codeSuffix, string message)
        {
            ReplaceOnlineDiagnostics(new[]
            {
                new OpennessDiagnostic(
                    OnlineDiagnosticPrefix + codeSuffix,
                    DiagnosticSeverity.Error,
                    message)
            });
            SetStatus(message);
        }

        private static string MapSeverity(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return "ERROR";
                case DiagnosticSeverity.Warning:
                    return "WARNING";
                default:
                    return "INFO";
            }
        }

        private static string BuildDiagnosticLocation(OpennessDiagnostic diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.ExceptionType))
            {
                return diagnostic.Location;
            }

            return string.IsNullOrWhiteSpace(diagnostic.Location)
                ? diagnostic.ExceptionType
                : diagnostic.Location + " · " + diagnostic.ExceptionType;
        }

        private static OpennessDiagnostic CreateUiOpennessDiagnostic(
            string code,
            DiagnosticSeverity severity,
            string message,
            Exception exception)
        {
            return new OpennessDiagnostic(
                code,
                severity,
                message + (exception == null ? string.Empty : " " + exception.Message),
                string.Empty,
                exception == null ? string.Empty : exception.GetType().FullName);
        }

        private TiaConnectionMode GetSelectedConnectionMode()
        {
            var selected = TiaConnectionModeComboBox.SelectedItem as ComboBoxItem;
            TiaConnectionMode mode;
            return selected != null &&
                Enum.TryParse(selected.Tag as string, true, out mode)
                    ? mode
                    : TiaConnectionMode.AttachToRunning;
        }

        private bool TryReadProcessId(out int? processId, out string error)
        {
            processId = null;
            error = string.Empty;
            var text = TiaProcessIdTextBox.Text == null
                ? string.Empty
                : TiaProcessIdTextBox.Text.Trim();
            if (text.Length == 0)
            {
                return true;
            }

            int value;
            if (!int.TryParse(text, out value) || value <= 0)
            {
                error = "Process ID должен быть положительным целым числом.";
                return false;
            }

            processId = value;
            return true;
        }

        private bool IsConnectedToCurrentTarget()
        {
            return !string.IsNullOrEmpty(_connectedTargetFingerprint) &&
                string.Equals(
                    _connectedTargetFingerprint,
                    BuildConnectionFingerprint(),
                    StringComparison.Ordinal);
        }

        private string BuildConnectionFingerprint()
        {
            return string.Join("|", new[]
            {
                NormalizePathInput(TiaProjectPathTextBox.Text).ToUpperInvariant(),
                GetSelectedConnectionMode().ToString(),
                (TiaProcessIdTextBox.Text ?? string.Empty).Trim(),
                (TiaDeviceNameTextBox.Text ?? string.Empty).Trim().ToUpperInvariant(),
                (TiaSoftwareNameTextBox.Text ?? string.Empty).Trim().ToUpperInvariant()
            });
        }

        private string GetProjectIdPrefix(int length)
        {
            var value = _project == null || _project.Plant == null
                ? Guid.Empty.ToString("N")
                : _project.Plant.Id.ToString("N");
            return value.Substring(0, length).ToUpperInvariant();
        }

        private static string NormalizePathInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value.Trim().Trim('"'));
            }
            catch (Exception)
            {
                return value.Trim().Trim('"');
            }
        }

        private void RecreateStagingDirectory()
        {
            CleanupStagingDirectory();
            var stagingRoot = Path.Combine(Path.GetTempPath(), "TiaSclStudio", "ExportPreview");
            _stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stagingDirectory);
        }

        private void CleanupStagingDirectory()
        {
            var candidate = _stagingDirectory;
            _stagingDirectory = null;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            try
            {
                var stagingRoot = Path.GetFullPath(
                    Path.Combine(Path.GetTempPath(), "TiaSclStudio", "ExportPreview"));
                var fullCandidate = Path.GetFullPath(candidate);
                var prefix = stagingRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(fullCandidate))
                {
                    Directory.Delete(fullCandidate, true);
                }
            }
            catch (Exception)
            {
                // A failed cleanup must not hide the result of the TIA operation.
            }
        }

        private static string EmptyAsAuto(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "автовыбор" : value;
        }

        private static string YesNo(bool value)
        {
            return value ? "да" : "нет";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_onlineBusy)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "Дождитесь завершения текущей операции TIA Openness.",
                    "Операция выполняется",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_headlessUnsavedChangesRisk)
            {
                var confirmation = MessageBox.Show(
                    this,
                    "В headless-сессии TIA Portal остались изменения, которые могли не сохраниться. " +
                    "Закрытие приложения может завершить Portal без интерфейса и безвозвратно потерять эти изменения. " +
                    "Всё равно закрыть приложение?",
                    "Несохранённые изменения headless TIA Portal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupStagingDirectory();
            if (!_gatewayDisposed && _tiaGateway != null)
            {
                _gatewayDisposed = true;
                try
                {
                    _tiaGatewayWorker.Invoke(() => _tiaGateway.Dispose());
                }
                catch (Exception)
                {
                }
            }

            _tiaGatewayWorker.Dispose();

            base.OnClosed(e);
        }

        private sealed class GatewayExportResponse
        {
            public GatewayExportResponse(TiaExportResult result, TiaGatewayStatus status)
            {
                Result = result;
                Status = status;
            }

            public TiaExportResult Result { get; private set; }

            public TiaGatewayStatus Status { get; private set; }
        }

        private sealed class GatewayPreviewResponse
        {
            public GatewayPreviewResponse(TiaExportPreview preview, TiaGatewayStatus status)
            {
                Preview = preview;
                Status = status;
            }

            public TiaExportPreview Preview { get; private set; }

            public TiaGatewayStatus Status { get; private set; }
        }

        private sealed class GatewayConnectResponse
        {
            public GatewayConnectResponse(
                ITiaGateway gateway,
                TiaGatewayStatus status,
                TiaHardwareIoCatalog hardwareIoCatalog)
            {
                Gateway = gateway;
                Status = status;
                HardwareIoCatalog = hardwareIoCatalog;
            }

            public ITiaGateway Gateway { get; private set; }

            public TiaGatewayStatus Status { get; private set; }

            public TiaHardwareIoCatalog HardwareIoCatalog { get; private set; }
        }

        private sealed class GatewayHardwareIoResponse
        {
            public GatewayHardwareIoResponse(
                TiaHardwareIoCatalog catalog,
                TiaGatewayStatus status)
            {
                Catalog = catalog;
                Status = status;
            }

            public TiaHardwareIoCatalog Catalog { get; private set; }

            public TiaGatewayStatus Status { get; private set; }
        }
    }
}
