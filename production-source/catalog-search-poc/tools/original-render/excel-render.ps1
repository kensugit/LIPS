param([Parameter(Mandatory=$true)][string]$InputFile,[Parameter(Mandatory=$true)][string]$OutputDirectory,[int]$Sheet=1,[int]$Row=1)
$ErrorActionPreference='Stop'
$excel=$null;$book=$null;$owned=$false
$phaseClock=[Diagnostics.Stopwatch]::StartNew();$timings=@{}
function Mark-RenderPhase([string]$Name){$timings[$Name]=[Math]::Round($phaseClock.Elapsed.TotalMilliseconds,1);$phaseClock.Restart()}
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class RenderWindow { [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p); }'
$existing=@(Get-Process EXCEL -ErrorAction SilentlyContinue | ForEach-Object Id)
Mark-RenderPhase 'setup'
try {
    $excel=New-Object -ComObject Excel.Application
    Mark-RenderPhase 'excelStart'
    [uint32]$excelProcess=0
    [void][RenderWindow]::GetWindowThreadProcessId([IntPtr]$excel.Hwnd,[ref]$excelProcess)
    if($existing -contains $excelProcess){throw 'A separate Excel process could not be created.'}
    $owned=$true
    $ownedStarted=(Get-Process -Id $excelProcess).StartTime
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'excel.pid') -Value $excelProcess -Encoding Ascii
    $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false
    $excel.AutomationSecurity=3;$excel.AskToUpdateLinks=$false
    # Set manual calculation in this private instance before opening the source.
    $empty=$excel.Workbooks.Add();$excel.Calculation=-4135;$excel.CalculateBeforeSave=$false
    $book=$excel.Workbooks.Open($InputFile,0,$true,[Type]::Missing,'render','render',$true)
    $empty.Close($false)
    Mark-RenderPhase 'openReadOnly'
    if($Sheet -lt 1 -or $Sheet -gt $book.Worksheets.Count){throw 'Invalid worksheet.'}
    $ws=$book.Worksheets.Item($Sheet)
    if($ws.Visible -ne -1){throw 'The source worksheet is hidden.'}
    $used=$ws.UsedRange;$lastRow=$used.Row+$used.Rows.Count-1
    $firstColumn=$used.Column;$lastColumn=$firstColumn+$used.Columns.Count-1
    if($Row -lt 1 -or $Row -gt $lastRow -or $lastColumn -gt 200){throw 'Source range exceeds renderer limits.'}
    if($ws.Rows.Item($Row).Hidden){throw 'The selected source row is hidden.'}
    $first=[Math]::Max(1,$Row-3);$last=[Math]::Min($lastRow,$Row+3)
    $range=$ws.Range($ws.Cells.Item($first,$firstColumn),$ws.Cells.Item($last,$lastColumn))
    $target=$ws.Range($ws.Cells.Item($Row,$firstColumn),$ws.Cells.Item($Row,$lastColumn))
    Mark-RenderPhase 'sourceRange'
    # Only print settings on the disposable in-memory view change. No Save call.
    $excel.PrintCommunication=$false
    try {
        $pageSetup=$ws.PageSetup
        $pageSetup.PrintArea=$range.Address()
        $pageSetup.PrintTitleRows='';$pageSetup.PrintTitleColumns=''
        $pageSetup.Zoom=$false;$pageSetup.FitToPagesWide=1;$pageSetup.FitToPagesTall=1
    } finally { $excel.PrintCommunication=$true }
    Mark-RenderPhase 'pageSetup'
    $ws.ExportAsFixedFormat(0,(Join-Path $OutputDirectory 'base.pdf'),0,$false,$false)
    Mark-RenderPhase 'originalPdf'
    $shape=$ws.Shapes.AddShape(1,$target.Left,$target.Top,$target.Width,$target.Height)
    $shape.Fill.ForeColor.RGB=65535;$shape.Fill.Transparency=0.65
    $shape.Line.ForeColor.RGB=255;$shape.Line.Weight=1.5
    $ws.ExportAsFixedFormat(0,(Join-Path $OutputDirectory 'highlight.pdf'),0,$false,$false)
    Mark-RenderPhase 'highlightPdf'
    @{sheet=$ws.Name;row=$Row;firstRow=$first;lastRow=$last;rangeWidthPoints=$range.Width;targetHeightPoints=$target.Height;renderer=('Microsoft Excel '+$excel.Version)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'excel.json') -Encoding UTF8
} finally {
    if($null -ne $book){try{$book.Close($false)}catch{}}
    # Release child COM references before Quit so they do not retain the private process.
    foreach($reference in @($shape,$target,$range,$used,$pageSetup,$ws,$book,$empty)){
        if($null -ne $reference -and [Runtime.InteropServices.Marshal]::IsComObject($reference)){
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($reference)
        }
    }
    $shape=$target=$range=$used=$pageSetup=$ws=$book=$empty=$null
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
    if($null -ne $excel){try{if($owned){$excel.Quit()}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)}}
    [GC]::Collect();[GC]::WaitForPendingFinalizers()
    # COM proxies may keep a hidden Excel process alive after Quit. Only reap our private instance.
    if($owned){
        $remaining=Get-Process -Id $excelProcess -ErrorAction SilentlyContinue
        if($null -ne $remaining -and $remaining.ProcessName -eq 'EXCEL' -and $remaining.StartTime -eq $ownedStarted){
            if(-not $remaining.WaitForExit(500)){$remaining.Kill()}
        }
    }
    Mark-RenderPhase 'cleanup'
    $timings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'timings.json') -Encoding UTF8
}
