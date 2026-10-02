$ErrorActionPreference='Stop'
$data=Get-Content (Join-Path $PSScriptRoot 'slides-ampliada.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$ppt=New-Object -ComObject PowerPoint.Application
$pres=$null
try {
    $pres=$ppt.Presentations.Open((Join-Path $PSScriptRoot 'candidate-ampliado.pptx'),0,0,0)
    for($i=0;$i -lt $data.Count;$i++) {
        if($data[$i].image -notlike 'relatorio-*'){continue}
        $slide=$pres.Slides.Item($i+1)
        for($j=$slide.Shapes.Count;$j -gt 0;$j--){if($slide.Shapes.Item($j).Type -eq 13){$slide.Shapes.Item($j).Delete()}}
        $path=Join-Path (Split-Path $PSScriptRoot -Parent) ('assets/pratica/'+$data[$i].image+'.png')
        $pic=$slide.Shapes.AddPicture($path,0,-1,54,139,-1,-1)
        $ratio=[Math]::Min(852/$pic.Width,242/$pic.Height)
        $pic.LockAspectRatio=-1; $pic.Width=$pic.Width*$ratio
        $pic.Left=54+(852-$pic.Width)/2; $pic.Top=139+(242-$pic.Height)/2
        $slide.Export((Join-Path $PSScriptRoot ('renders-ampliada/slide-{0:00}.png' -f ($i+1))),'PNG',1600,900)
    }
    $pres.Save()
    Write-Output "Updated report crops. Slides: $($pres.Slides.Count)"
} finally {
    if($pres){$pres.Close()}
    $ppt.Quit()
}
