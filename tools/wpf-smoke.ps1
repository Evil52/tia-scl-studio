param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$resolvedArtifact = [System.IO.Path]::GetFullPath($ArtifactDirectory)

[Reflection.Assembly]::LoadFrom(
    [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.Core.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom(
    [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.Diagram.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom(
    [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.Openness.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom(
    [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.Openness.Legacy.V17.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom(
    [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.App.exe'))

$appType = $assembly.GetType('TiaSclStudio.App.App', $true)
$app = [Activator]::CreateInstance($appType)
$appType.GetMethod('InitializeComponent').Invoke($app, @()) | Out-Null

$windowType = $assembly.GetType('TiaSclStudio.App.MainWindow', $true)
$window = [Activator]::CreateInstance($windowType)
try
{
    $sheetSelector = $window.FindName('SheetSelector')
    $logicButton = $window.FindName('LogicPaletteAndButton')
    $noteButton = $window.FindName('NotePaletteButton')
    $tagSourceButton = $window.FindName('TagSourcePaletteButton')
    $tagSinkButton = $window.FindName('TagSinkPaletteButton')
    $constantButton = $window.FindName('ConstantPaletteButton')
    $importSclButton = $window.FindName('ImportSclLibraryButton')
    $addGroupButton = $window.FindName('AddGroupButton')
    $ungroupButton = $window.FindName('UngroupButton')
    $autoLayoutButton = $window.FindName('AutoLayoutButton')
    $fitAllButton = $window.FindName('FitAllButton')
    $fitSelectionButton = $window.FindName('FitSelectionButton')
    $preview = $window.FindName('SclPreviewTextBox')
    $undoButton = $window.FindName('UndoButton')

    if ($null -eq $sheetSelector -or $sheetSelector.Items.Count -lt 1)
    {
        throw 'The sheet selector was not initialized.'
    }

    if ($null -eq $logicButton -or $null -eq $noteButton)
    {
        throw 'The Logic/Note palette was not initialized.'
    }

    if ($null -eq $tagSourceButton -or
        $null -eq $tagSinkButton -or
        $null -eq $constantButton)
    {
        throw 'The Tag/Constant palette was not initialized.'
    }

    if ($null -eq $importSclButton)
    {
        throw 'The declaration-only SCL library import command was not initialized.'
    }

    if ($null -eq $addGroupButton -or $null -eq $ungroupButton)
    {
        throw 'The visual-group toolbar was not initialized.'
    }

    if ($null -eq $autoLayoutButton -or
        $null -eq $fitAllButton -or
        $null -eq $fitSelectionButton)
    {
        throw 'The auto-layout/fit toolbar was not initialized.'
    }

    if ($null -eq $preview -or [string]::IsNullOrWhiteSpace($preview.Text))
    {
        throw 'The active-sheet SCL preview was not initialized.'
    }

    $sheetField = $windowType.GetField(
        '_sheet',
        [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic)
    $sheet = $sheetField.GetValue($window)
    $nodeCountBefore = $sheet.Nodes.Count
    $logicButton.RaiseEvent(
        [Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    if ($sheet.Nodes.Count -ne $nodeCountBefore + 1)
    {
        throw 'The AND palette command did not add one logic node.'
    }

    $undoButton.RaiseEvent(
        [Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    $restoredSheet = $sheetField.GetValue($window)
    if ($restoredSheet.Nodes.Count -ne $nodeCountBefore)
    {
        throw 'Undo did not restore the diagram after adding a logic node.'
    }

    $diagramAssembly = [Reflection.Assembly]::LoadFrom(
        [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.Diagram.dll'))
    $projectField = $windowType.GetField(
        '_project',
        [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic)
    $project = $projectField.GetValue($window)

    $constantWindowType = $assembly.GetType(
        'TiaSclStudio.App.ConstantNodeCreateWindow',
        $true)
    $constantWindow = [Activator]::CreateInstance(
        $constantWindowType,
        [object[]] @($project, $restoredSheet.Id))
    try
    {
        if ($null -eq $constantWindow.FindName('LiteralTextBox') -or
            $null -eq $constantWindow.FindName('DataTypeTextBox'))
        {
            throw 'The constant-creation dialog was not initialized.'
        }
    }
    finally
    {
        $constantWindow.Close()
    }

    $terminalDirectionType = $diagramAssembly.GetType(
        'TiaSclStudio.Diagram.Model.TerminalDirection',
        $true)
    $sourceDirection = [Enum]::Parse($terminalDirectionType, 'Source')
    $tagWindowType = $assembly.GetType(
        'TiaSclStudio.App.TagNodeCreateWindow',
        $true)
    $tagWindow = [Activator]::CreateInstance(
        $tagWindowType,
        [object[]] @($project, $restoredSheet.Id, $sourceDirection))
    try
    {
        if ($null -eq $tagWindow.FindName('NewTagPanel') -or
            $null -eq $tagWindow.FindName('ExistingTagComboBox'))
        {
            throw 'The tag-creation dialog was not initialized.'
        }
    }
    finally
    {
        $tagWindow.Close()
    }

    $groupEditorType = $assembly.GetType(
        'TiaSclStudio.App.GroupEditorWindow',
        $true)
    $groupEditor = [Activator]::CreateInstance(
        $groupEditorType,
        [object[]] @('Smoke region', 'Hidden WPF smoke'))
    try
    {
        if ($null -eq $groupEditor.FindName('TitleTextBox') -or
            $null -eq $groupEditor.FindName('CommentTextBox'))
        {
            throw 'The group-editor dialog was not initialized.'
        }
    }
    finally
    {
        $groupEditor.Close()
    }

    $groupType = $diagramAssembly.GetType(
        'TiaSclStudio.Diagram.Model.DiagramGroup',
        $true)
    $group = [Activator]::CreateInstance($groupType)
    $group.Title = 'Smoke region'
    $group.X = 20.0
    $group.Y = 20.0
    $group.Width = 420.0
    $group.Height = 260.0
    $restoredSheet.Groups.Add($group)

    $renderMethod = $windowType.GetMethod(
        'RenderDiagram',
        [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic)
    $renderMethod.Invoke($window, @()) | Out-Null
    $groupVisualsField = $windowType.GetField(
        '_groupVisuals',
        [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic)
    $groupVisuals = $groupVisualsField.GetValue($window)
    if (-not $groupVisuals.ContainsKey($group.Id))
    {
        throw 'The visual group was not rendered.'
    }

    $nodeVisualsField = $windowType.GetField(
        '_nodeVisuals',
        [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic)
    $nodeVisuals = $nodeVisualsField.GetValue($window)
    $blockNode = $restoredSheet.Nodes |
        Where-Object { $_.GetType().Name -eq 'BlockCallNode' } |
        Select-Object -First 1
    $tagNodes = @($restoredSheet.Nodes |
        Where-Object { $_.GetType().Name -eq 'TagNode' })
    $constantNode = $restoredSheet.Nodes |
        Where-Object { $_.GetType().Name -eq 'ConstantNode' } |
        Select-Object -First 1

    if ($null -eq $blockNode -or $tagNodes.Count -lt 2 -or $null -eq $constantNode)
    {
        throw 'The demo does not contain the node taxonomy required by the visual smoke.'
    }

    $blockVisual = $nodeVisuals[$blockNode.Id]
    $constantVisual = $nodeVisuals[$constantNode.Id]
    if ($blockVisual.Uid -ne 'Node.Block.FB')
    {
        throw 'The FB visual identity is missing.'
    }

    if ($constantVisual.Uid -ne 'Node.Constant' -or
        $constantVisual.Width -ge $blockVisual.Width -or
        $constantVisual.Height -ge $blockVisual.Height)
    {
        throw 'The constant visual is not compact relative to the FB visual.'
    }

    $tagVisualUids = @()
    foreach ($tagNode in $tagNodes)
    {
        $tagVisual = $nodeVisuals[$tagNode.Id]
        $tagVisualUids += $tagVisual.Uid
        if ($tagVisual.Width -ge $blockVisual.Width -or
            $tagVisual.Height -ge $blockVisual.Height)
        {
            throw 'A tag visual is not compact relative to the FB visual.'
        }
    }

    if ($tagVisualUids -notcontains 'Node.Tag.Source' -or
        $tagVisualUids -notcontains 'Node.Tag.Sink')
    {
        throw 'Source and sink tags do not have distinct visual identities.'
    }

    $coreAssembly = [Reflection.Assembly]::LoadFrom(
        [System.IO.Path]::Combine($resolvedArtifact, 'TiaSclStudio.Core.dll'))
    $blockKindType = $coreAssembly.GetType(
        'TiaSclStudio.Core.Model.BlockKind',
        $true)
    $project.Plant.Blocks[0].Kind = [Enum]::Parse($blockKindType, 'Function')
    $renderMethod.Invoke($window, @()) | Out-Null
    $nodeVisuals = $nodeVisualsField.GetValue($window)
    if ($nodeVisuals[$blockNode.Id].Uid -ne 'Node.Block.FC')
    {
        throw 'The FC visual identity is not distinct from the FB visual.'
    }

    $fitAllButton.RaiseEvent(
        [Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    $fittedSheet = $sheetField.GetValue($window)
    if ([double]::IsNaN($fittedSheet.Zoom) -or
        [double]::IsInfinity($fittedSheet.Zoom) -or
        $fittedSheet.Zoom -lt 0.15 -or
        $fittedSheet.Zoom -gt 2.0)
    {
        throw 'Fit All produced an invalid diagram zoom.'
    }

    Write-Output (
        'Hidden WPF smoke passed: sheets={0}; previewChars={1}; compact tag/constant, FB/FC taxonomy, Auto-layout/Fit All, logic undo and group render=ready' -f
        $sheetSelector.Items.Count,
        $preview.Text.Length)
}
finally
{
    $window.Close()
    $app.Shutdown()
}
