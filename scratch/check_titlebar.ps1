$a = [System.Reflection.Assembly]::LoadFile('D:\Antigravity\Test\NotiFlow\bin\Debug\net8.0-windows10.0.19041.0\Wpf.Ui.dll')
$t = $a.GetType('Wpf.Ui.Controls.TitleBar')
Write-Output "--- Properties for TitleBar ---"
$t.GetProperties() | Select-Object Name, PropertyType | Format-Table -AutoSize
