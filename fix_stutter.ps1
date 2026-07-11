$content = Get-Content 'Models\ScopeViewModel.cs' -Raw -Encoding UTF8;

# Rename LoadAllDataAsync to LoadAllData and remove async Task
$content = $content -replace '_ = LoadAllDataAsync\(\);', 'LoadAllData();'
$content = $content -replace 'private async Task LoadAllDataAsync\(\)', 'private void LoadAllData()'
$content = $content -replace 'var processes = await Task\.Run\(\(\) => Services\.ProcessEnumerator\.EnumerateWindowProcesses\(\)\);', 'var processes = Services.ProcessEnumerator.EnumerateWindowProcesses();'

# Add delay to ExtractIconAsync
$content = $content -replace 'private async Task ExtractIconAsync\(ScopeItemViewModel vm, string\? exePath\)\r?\n\s*\{\r?\n\s*try\r?\n\s*\{', 'private async Task ExtractIconAsync(ScopeItemViewModel vm, string? exePath)`r`n        {`r`n            try`r`n            {`r`n                // 延迟加载图标，避免在这 250ms 内使用 UI 线程造成过渡动画掉帧`r`n                await Task.Delay(300);'

[System.IO.File]::WriteAllText('Models\ScopeViewModel.cs', $content, [System.Text.Encoding]::UTF8);
