# Opsis.UI 6 integration (WPF)

The merged `Themes/OpsisUI6Midnight.xaml` has **only uniquely keyed** resources, such as `OpsisCardStyle`, `OpsisDataGridStyle`, `OpsisNoticeStyle`, and `OpsisPrimaryButtonStyle`. Existing `CardStyle`, `TextBrush`, `CardBgBrush`, ThemeManager and 21 themes are **not modified**.

To opt a new GearWin view in, use `Style="{StaticResource OpsisCardStyle}"` for a Border. The default GearWin UI stays as-is. Do not replace existing implicit styles, DataGrid templates or theme dictionaries until a screen-by-screen visual and keyboard test succeeds.

For themes other than Midnight, copy the matching `design/wpf/opsis-ui6-<theme>.xaml` from `gs-applabs/Opsis.UI` and select it only when the view opts into an Opsis.UI theme. All files must be synchronized from the single upstream exporter.
