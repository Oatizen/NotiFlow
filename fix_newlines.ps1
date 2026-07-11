$content = Get-Content 'Views\Pages\CustomPage.xaml' -Raw -Encoding UTF8;
$content = $content.Replace('`n', "`r`n");
[System.IO.File]::WriteAllText('Views\Pages\CustomPage.xaml', $content, [System.Text.Encoding]::UTF8);
