$content = Get-Content 'Models\ScopeViewModel.cs' -Raw -Encoding UTF8;
$content = $content.Replace('`r`n        {`r`n            try`r`n            {`r`n                // 延迟加载图标，避免在这 250ms 内使用 UI 线程造成过渡动画掉帧`r`n                await Task.Delay(300);', "`r`n        {`r`n            try`r`n            {`r`n                // 延迟加载图标，避免在这 250ms 内使用 UI 线程造成过渡动画掉帧`r`n                await Task.Delay(300);")
[System.IO.File]::WriteAllText('Models\ScopeViewModel.cs', $content, [System.Text.Encoding]::UTF8);
