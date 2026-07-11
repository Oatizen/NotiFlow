$content = Get-Content 'Views\Pages\CustomPage.xaml' -Raw -Encoding UTF8;
$content = $content -replace '(?s)<ui:Flyout x:Name="ColorPaletteFlyout".*?</ui:Flyout>', '<ui:Flyout x:Name="ColorPaletteFlyout" Placement="Bottom">
                                    <colorpicker:StandardColorPicker SelectedColor="{Binding CurrentColor, Mode=TwoWay}" Width="260" Height="300" Margin="4"/>
                                </ui:Flyout>'
[System.IO.File]::WriteAllText('Views\Pages\CustomPage.xaml', $content, [System.Text.Encoding]::UTF8)
