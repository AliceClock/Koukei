# SettingsPage修改
1. 修改TitleBarHelper中的ApplySystemThemeToCaptionButtons方法，参考如下代码。
```csharp
internal partial class TitleBarHelper
{
    public static void ApplySystemThemeToCaptionButtons(Window window, ElementTheme currentTheme)
    {
        if (window.AppWindow != null)
        {
            var foregroundColor = currentTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
            window.AppWindow.TitleBar.ButtonForegroundColor = foregroundColor;
            window.AppWindow.TitleBar.ButtonHoverForegroundColor = foregroundColor;

            var backgroundHoverColor = currentTheme == ElementTheme.Dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
            window.AppWindow.TitleBar.ButtonHoverBackgroundColor = backgroundHoverColor;
        }
    }
}
```
2. 使用LanuageHelper控制应用的语言。
3. 更改语言时，参考如下代码，使用InfoBar来提示用户重启应用以应用新的语言设置。
```
<InfoBar
    x:Uid="LanguageRestartInfo"
    IsClosable="False"
    IsOpen="{x:Bind Mode=OneWay, Path=ViewModel.LanguageChanged}"
    IsTabStop="True"
    Severity="Informational">
    <InfoBar.ActionButton>
        <Button x:Uid="LanguageRestartInfoButton" Click="Click_LanguageRestart" />
    </InfoBar.ActionButton>
</InfoBar>
```
4. 当应用打开时，如果数据位置的路径不存在，自动创建数据文件夹。