$content = Get-Content 'Views\Pages\CustomPage.xaml' -Raw -Encoding UTF8;
$content = $content.Replace('xmlns:rendering="clr-namespace:NotiFlow.Rendering"', "xmlns:rendering=""clr-namespace:NotiFlow.Rendering""`r`n      xmlns:hc=""https://handyorg.github.io/handycontrol""");
$content = $content -replace '(?s)<ui:Flyout x:Name="ColorPaletteFlyout".*?</ui:Flyout>', '<ui:Flyout x:Name="ColorPaletteFlyout" Placement="Bottom">
                                    <hc:ColorPicker SelectedBrush="{Binding CurrentColorBrush, Mode=TwoWay}" Margin="4"/>
                                </ui:Flyout>'
[System.IO.File]::WriteAllText('Views\Pages\CustomPage.xaml', $content, [System.Text.Encoding]::UTF8)
