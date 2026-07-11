$content = Get-Content 'Models\ScopeViewModel.cs' -Raw -Encoding UTF8;

# Replace LoadAllData() call with _ = LoadAllDataAsync()
$content = $content.Replace('LoadAllData();', '_ = LoadAllDataAsync();')

# Replace private void LoadAllData() with private async Task LoadAllDataAsync()
$content = $content.Replace("private void LoadAllData()`r`n        {`r`n            try`r`n            {", "private async Task LoadAllDataAsync()`r`n        {`r`n            try`r`n            {`r`n                // 延迟执行数据加载，确保 250ms 的过渡动画能完全不受干扰地播放完毕`r`n                await Task.Delay(260);")

# Replace synchronous EnumerateWindowProcesses with async Task.Run
$content = $content.Replace('var processes = Services.ProcessEnumerator.EnumerateWindowProcesses();', 'var processes = await Task.Run(() => Services.ProcessEnumerator.EnumerateWindowProcesses());')

[System.IO.File]::WriteAllText('Models\ScopeViewModel.cs', $content, [System.Text.Encoding]::UTF8);
