$content = Get-Content 'Views\Pages\SettingsPage.xaml' -Raw -Encoding UTF8;
$content = $content -replace '<StackPanel Grid\.Column="1" VerticalAlignment="Center">', '<StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="0,0,16,0">';
$content = $content -replace '<ui:TextBlock Grid\.Column="1" Text="([^"]+)" VerticalAlignment="Center"/>', '<ui:TextBlock Grid.Column="1" Text="$1" VerticalAlignment="Center" TextWrapping="Wrap" Margin="0,0,16,0"/>';
$content = $content -replace '<ui:TextBlock Text="([^"]+)" FontTypography="BodyStrong"/>', '<ui:TextBlock Text="$1" FontTypography="BodyStrong" TextWrapping="Wrap"/>';
$content = $content -replace '<ui:TextBlock Text="([^"]+)" Foreground="\{DynamicResource TextFillColorSecondaryBrush\}"/>', '<ui:TextBlock Text="$1" Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>';
[System.IO.File]::WriteAllText('Views\Pages\SettingsPage.xaml', $content, [System.Text.Encoding]::UTF8);

$content = Get-Content 'Views\Pages\CustomPage.xaml' -Raw -Encoding UTF8;
$content = $content -replace '<ui:TextBlock Text="([^"]+)" FontTypography="BodyStrong"/>', '<ui:TextBlock Text="$1" FontTypography="BodyStrong" TextWrapping="Wrap"/>';
$content = $content -replace '<ui:TextBlock Text="([^"]+)" Foreground="\{DynamicResource TextFillColorSecondaryBrush\}"/>', '<ui:TextBlock Text="$1" Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>';
$content = $content -replace '<ui:TextBlock Text="([^"]+)"/>\s*<ui:TextBlock Text="([^"]+)" Foreground="\{DynamicResource TextFillColorSecondaryBrush\}" TextWrapping="Wrap"/>', '<ui:TextBlock Text="$1" TextWrapping="Wrap"/>`n                                <ui:TextBlock Text="$2" Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>';
$content = $content -replace '<StackPanel>\s*<ui:TextBlock Text="([^"]+)" TextWrapping="Wrap"/>\s*<ui:TextBlock Text="([^"]+)" Foreground="\{DynamicResource TextFillColorSecondaryBrush\}" TextWrapping="Wrap"/>\s*</StackPanel>', '<StackPanel Margin="0,0,64,0">`n                                <ui:TextBlock Text="$1" TextWrapping="Wrap"/>`n                                <ui:TextBlock Text="$2" Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextWrapping="Wrap"/>`n                            </StackPanel>';
[System.IO.File]::WriteAllText('Views\Pages\CustomPage.xaml', $content, [System.Text.Encoding]::UTF8);
