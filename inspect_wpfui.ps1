$a = [System.Reflection.Assembly]::LoadFile('D:\Antigravity\Test\NotiFlow\bin\Debug\net8.0-windows10.0.19041.0\Wpf.Ui.dll')
$types = $a.GetTypes()
foreach ($t in $types) {
    if ($t.Name -eq 'ThemeChangedEvent') {
        Write-Output "=== $($t.FullName) ==="
        $invoke = $t.GetMethod('Invoke')
        if ($invoke) {
            Write-Output "Invoke: $($invoke.ToString())"
            foreach ($p in $invoke.GetParameters()) {
                Write-Output "  Param: $($p.Name) Type: $($p.ParameterType.FullName)"
            }
        }
    }
}
