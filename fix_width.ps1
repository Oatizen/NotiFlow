$content = Get-Content 'Views\Pages\SettingsPage.xaml' -Raw -Encoding UTF8;
$content = $content -replace '<ui:TextBlock Grid\.Column="1" Text="([^"]+)" VerticalAlignment="Center" TextWrapping="Wrap" Margin="0,0,16,0"/>', '<ui:TextBlock Grid.Column="1" Text="$1" VerticalAlignment="Center" TextWrapping="Wrap" Margin="0,0,16,0" MinWidth="100"/>';
$content = $content -replace '<StackPanel Grid\.Column="1" VerticalAlignment="Center" Margin="0,0,16,0">', '<StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="0,0,16,0" MinWidth="100">';
[System.IO.File]::WriteAllText('Views\Pages\SettingsPage.xaml', $content, [System.Text.Encoding]::UTF8);

$content = Get-Content 'Views\Pages\CustomPage.xaml' -Raw -Encoding UTF8;
$content = $content -replace '<StackPanel Margin="0,0,64,0">', '<StackPanel Margin="0,0,64,0" MinWidth="100">';
[System.IO.File]::WriteAllText('Views\Pages\CustomPage.xaml', $content, [System.Text.Encoding]::UTF8);
