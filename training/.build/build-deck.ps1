param([switch]$Pratico,[switch]$Ampliado)
if($Ampliado){$Pratico=$true}
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Split-Path $root -Parent
$output = Join-Path $root 'output'
$renders = Join-Path $PSScriptRoot $(if($Ampliado){'renders-ampliada'}elseif($Pratico){'renders-pratica'}else{'renders'})
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $output,$renders,$assets | Out-Null
Copy-Item -LiteralPath 'C:/Users/felip/.codex/generated_images/019f60a5-19f1-7220-9373-4ae055efb0c7/exec-02482b5a-828e-4858-990d-501d5e20b174.png' -Destination (Join-Path $assets 'capa-industrial.png')
$images = @{
    client = Join-Path $project 'ModbusTcpTroubleshooter.App/ManualImages/ClientSettings.png'
    map = Join-Path $project 'ModbusTcpTroubleshooter.App/ManualImages/MapDiscovery.png'
    test = 'C:/Users/felip/AppData/Local/Temp/codex-clipboard-f2a5719e-3fde-481b-ba46-3f9f5d1c8997.png'
}
foreach ($key in @('client','map','test')) {
    $target = Join-Path $assets "$key.png"
    Copy-Item -LiteralPath $images[$key] -Destination $target
    $images[$key] = $target
}
function Color([string]$hex) {
    $hex = $hex.TrimStart('#')
    return [Convert]::ToInt32($hex.Substring(0,2),16) + 256*[Convert]::ToInt32($hex.Substring(2,2),16) + 65536*[Convert]::ToInt32($hex.Substring(4,2),16)
}
$ink = Color '18354B'; $muted = Color '556C7D'; $teal = Color '087E8B'; $white = Color 'FFFFFF'; $bg = Color 'F7F9FC'
$script:checks = [System.Collections.Generic.List[object]]::new()
function Text($slide,[string]$text,[double]$x,[double]$y,[double]$w,[double]$h,[double]$size=22,[int]$color=$ink,[bool]$bold=$false) {
    $shape=$slide.Shapes.AddTextbox(1,$x,$y,$w,$h)
    $shape.TextFrame.MarginLeft=0; $shape.TextFrame.MarginRight=0; $shape.TextFrame.MarginTop=0; $shape.TextFrame.MarginBottom=0
    $shape.TextFrame.WordWrap=-1; $shape.TextFrame.AutoSize=0
    $range=$shape.TextFrame.TextRange; $range.Text=$text
    $range.Font.Name='Segoe UI'; $range.Font.Size=$size; $range.Font.Color.RGB=$color; $range.Font.Bold=([int]$bold * -1)
    $range.ParagraphFormat.SpaceAfter=4
    $shape.TextFrame2.AutoSize=0
    $shape.Width=$w; $shape.Height=$h
    return $shape
}
function Base($pres,$data,$n) {
    $slide=$pres.Slides.Add($pres.Slides.Count+1,12)
    $slide.FollowMasterBackground=0; $slide.Background.Fill.ForeColor.RGB=$bg
    [void](Text $slide $data.section 54 22 830 24 13 $teal $true)
    [void](Text $slide $data.title 54 57 852 76 32 $ink $true)
    [void](Text $slide 'Treinamento Modbus TCP' 54 508 740 18 11 $muted)
    [void](Text $slide ('{0:00}' -f $n) 871 506 35 20 12 $muted)
    return $slide
}
function Lines($slide,$lines,$x,$y,$width,$step=68,$size=22) {
    for($i=0;$i -lt $lines.Count;$i++) {
        [void](Text $slide ([string]$lines[$i]) $x ($y+$i*$step) $width ($step-8) $size $ink)
    }
}
function Picture($slide,$path,$x,$y,$maxW,$maxH) {
    $pic=$slide.Shapes.AddPicture($path,0,-1,$x,$y,-1,-1)
    $ratio=[Math]::Min($maxW/$pic.Width,$maxH/$pic.Height)
    $pic.LockAspectRatio=-1; $pic.Width=$pic.Width*$ratio
    $pic.Left=$x+($maxW-$pic.Width)/2; $pic.Top=$y+($maxH-$pic.Height)/2
}
function Table($slide,$data) {
    $rows=$data.rows.Count+1; $cols=$data.headers.Count
    $height=if($data.callout){288}else{330}
    $shape=$slide.Shapes.AddTable($rows,$cols,54,151,852,$height)
    $tbl=$shape.Table
    if($cols -eq 2){$tbl.Columns.Item(1).Width=270; $tbl.Columns.Item(2).Width=582}
    if($cols -eq 3){$tbl.Columns.Item(1).Width=220; $tbl.Columns.Item(2).Width=292; $tbl.Columns.Item(3).Width=340}
    if($cols -eq 4){for($i=1;$i -le 4;$i++){$tbl.Columns.Item($i).Width=213}}
    for($r=1;$r -le $rows;$r++) {
        for($c=1;$c -le $cols;$c++) {
            $cell=$tbl.Cell($r,$c).Shape
            $cell.TextFrame.MarginLeft=10; $cell.TextFrame.MarginRight=10; $cell.TextFrame.MarginTop=9; $cell.TextFrame.MarginBottom=7
            $tr=$cell.TextFrame.TextRange
            $tr.Text=if($r -eq 1){[string]$data.headers[$c-1]}else{[string]$data.rows[$r-2][$c-1]}
            $tr.Font.Name='Segoe UI'; $tr.Font.Size=18
            if($r -eq 1){$tr.Font.Color.RGB=$white;$tr.Font.Bold=-1;$cell.Fill.ForeColor.RGB=$ink}
            else{$tr.Font.Color.RGB=$ink;$cell.Fill.ForeColor.RGB=if($r%2 -eq 0){$white}else{Color 'EAF0F4'}}
        }
    }
    if($data.callout){[void](Text $slide $data.callout 54 458 852 35 20 $teal $true)}
}
$data=Get-Content -LiteralPath (Join-Path $PSScriptRoot $(if($Ampliado){'slides-ampliada.json'}elseif($Pratico){'slides-pratica.json'}else{'slides.json'})) -Raw -Encoding UTF8 | ConvertFrom-Json
$ppt=New-Object -ComObject PowerPoint.Application
$ppt.Visible=-1
$ppt.WindowState=2
$pres=$null
try {
    $pres=$ppt.Presentations.Add(-1)
    while($pres.Slides.Count -gt 0){$pres.Slides.Item(1).Delete()}
    $pres.PageSetup.SlideWidth=960; $pres.PageSetup.SlideHeight=540
    for($index=0;$index -lt $data.Count;$index++) {
        $d=$data[$index]; $n=$index+1
        if(!$Pratico -and $n -eq 5){$d.kind='diagram'}
        if($d.kind -eq 'cover') {
            $slide=$pres.Slides.Add(1,12)
            Picture $slide (Join-Path $assets 'capa-industrial.png') 0 0 960 540
            [void](Text $slide $d.title 54 145 460 150 44 $white $true)
            [void](Text $slide $d.subtitle 56 327 435 87 20 $white)
            [void](Text $slide '03 OUTUBRO 2026' 56 465 420 25 13 (Color '70D0D8') $true)
        } else {
            $slide=Base $pres $d $n
            switch($d.kind) {
                'screen' {
                    Picture $slide (Join-Path $assets ('pratica/'+$d.image+'.png')) 54 145 500 332
                    Lines $slide $d.body 590 151 316 85 18
                    $caption=if($d.caption){$d.caption}else{'Tela real do software. Valores de exemplo para treinamento.'}
                    [void](Text $slide $caption 54 485 852 18 11 $muted)
                }
                'wide' {
                    Picture $slide (Join-Path $assets ('pratica/'+$d.image+'.png')) 54 139 852 242
                    for($i=0;$i -lt $d.body.Count;$i++){
                        $col=$i%2; $row=[Math]::Floor($i/2)
                        [void](Text $slide $d.body[$i] (54+$col*444) (397+$row*50) 412 47 18 $ink)
                    }
                    $caption=if($d.caption){$d.caption}else{'Interface atual. Resultados de demonstração, sem ensaio real da rede.'}
                    [void](Text $slide $caption 54 497 800 12 9 $muted)
                }
                'diagram' {
                    Picture $slide (Join-Path $assets 'transacao.png') 54 135 852 355
                }
                'table' {Table $slide $d}
                'agenda' {Lines $slide $d.left 54 154 420 64 21; Lines $slide $d.right 505 154 401 64 21}
                'question' {
                    for($i=0;$i -lt $d.body.Count;$i++){
                        [void](Text $slide ('{0:00}' -f ($i+1)) 54 (154+$i*78) 60 55 30 $teal $true)
                        [void](Text $slide $d.body[$i] 136 (155+$i*78) 735 65 24 $ink)
                    }
                }
                'split' {
                    [void](Text $slide $d.leftTitle 54 155 405 43 25 $teal $true)
                    [void](Text $slide $d.rightTitle 511 155 395 43 25 $teal $true)
                    Lines $slide $d.left 54 213 395 72 21
                    Lines $slide $d.right 511 213 395 72 21
                    [void](Text $slide $d.callout 54 457 852 45 19 $muted)
                }
                'demo' {
                    for($i=0;$i -lt $d.steps.Count;$i++){
                        [void](Text $slide ($i+1) 54 (149+$i*64) 42 42 26 $teal $true)
                        [void](Text $slide $d.steps[$i] 116 (150+$i*64) 790 55 22 $ink)
                    }
                    [void](Text $slide $d.expected 54 429 852 62 20 $teal $true)
                }
                'example' {
                    [void](Text $slide $d.big 54 157 852 100 35 $teal $true)
                    Lines $slide $d.body 54 285 852 62 22
                }
                'image' {
                    Picture $slide $images[$d.image] 54 144 430 328
                    Lines $slide $d.body 525 159 381 67 20
                    [void](Text $slide $d.caption 54 477 852 22 13 $muted)
                }
                'screenshot' {
                    Picture $slide $images[$d.image] 54 141 852 334
                    [void](Text $slide $d.caption 54 480 852 20 13 $muted)
                }
                'chart' {
                    $chartShape=$slide.Shapes.AddChart(4,54,151,540,317)
                    $chart=$chartShape.Chart; $chart.HasTitle=$false; $chart.HasLegend=$false
                    $chart.ChartData.Activate()
                    $wb=$chart.ChartData.Workbook; $sheet=$wb.Worksheets.Item(1)
                    $sheet.Cells.Clear() | Out-Null
                    $sheet.Cells.Item(1,1).Value2='Segundo'; $sheet.Cells.Item(1,2).Value2='Pacotes/s'
                    for($i=0;$i -lt $d.values.Count;$i++){$sheet.Cells.Item($i+2,1).Value2=[string]$d.categories[$i];$sheet.Cells.Item($i+2,2).Value2=[double]$d.values[$i]}
                    $source = "='" + $sheet.Name + "'!" + '$A$1:$B$11'
                    $chart.SetSourceData($source,2)
                    while($chart.SeriesCollection().Count -gt 1){$chart.SeriesCollection(1).Delete()}
                    $chart.SeriesCollection(1).Name='Pacotes/s'
                    $chart.SeriesCollection(1).XValues="='" + $sheet.Name + "'!" + '$A$2:$A$11'
                    $chart.ChartArea.Font.Name='Segoe UI'; $chart.ChartArea.Font.Size=14
                    $chart.Axes(2).MinimumScale=0; $chart.Axes(2).MaximumScale=500
                    $chart.SeriesCollection(1).Format.Line.ForeColor.RGB=$teal; $chart.SeriesCollection(1).Format.Line.Weight=3
                    $chart.ChartArea.Format.Fill.ForeColor.RGB=$bg
                    $wb.Close($true)
                    Lines $slide $d.body 633 164 273 98 20
                    [void](Text $slide 'Exemplo didático de pacotes/s. Valores ilustrativos.' 54 479 852 22 13 $muted)
                }
                default {Lines $slide $d.body 54 166 852 76 23}
            }
        }
        $slide.NotesPage.Shapes.Placeholders.Item(2).TextFrame.TextRange.Text=$d.notes+"`r`n`r`nRoteiro: docs/2026-10-03-ata-treinamento-troubleshoot-modbus.md. Operação do software: manual integrado da versão usada no treinamento."
        Write-Output "Slide $n/$($data.Count): $($d.title -replace '\n',' ')"
    }
    $final=Join-Path $PSScriptRoot $(if($Ampliado){'candidate-ampliado.pptx'}elseif($Pratico){'candidate-pratico.pptx'}else{'candidate.pptx'})
    $pres.SaveAs($final,24)
    foreach($slide in $pres.Slides){
        $slide.Export((Join-Path $renders ('slide-{0:00}.png' -f $slide.SlideIndex)),'PNG',1600,900)
        foreach($shape in $slide.Shapes){
            if($shape.HasTextFrame -eq -1 -and $shape.TextFrame.HasText -eq -1){
                $bound=$shape.TextFrame.TextRange.BoundHeight
                if($bound -gt $shape.Height+3){$script:checks.Add(@{slide=$slide.SlideIndex;shape=$shape.Name;bound=$bound;height=$shape.Height;text=$shape.TextFrame.TextRange.Text})}
            }
            if($shape.HasTable -eq -1){
                for($r=1;$r -le $shape.Table.Rows.Count;$r++){for($c=1;$c -le $shape.Table.Columns.Count;$c++){
                    $cell=$shape.Table.Cell($r,$c).Shape
                    $usable=$cell.Height-$cell.TextFrame.MarginTop-$cell.TextFrame.MarginBottom
                    if($cell.TextFrame.TextRange.BoundHeight -gt $usable+3){$script:checks.Add(@{slide=$slide.SlideIndex;cell="$r,$c";bound=$cell.TextFrame.TextRange.BoundHeight;height=$usable;text=$cell.TextFrame.TextRange.Text})}
                }}
            }
        }
    }
    $script:checks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot $(if($Ampliado){'layout-check-ampliado.json'}elseif($Pratico){'layout-check-pratico.json'}else{'layout-check.json'})) -Encoding UTF8
    Write-Output "FINAL: $final"
    Write-Output "SLIDES: $($pres.Slides.Count)"
    Write-Output "TEXT_OVERFLOW: $($script:checks.Count)"
} finally {
    if($pres){$pres.Close()}
    $ppt.Quit()
}
