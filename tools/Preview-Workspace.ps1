# Render the project's real WPF XAML with explicitly illustrative data only.
# Windows PowerShell 5.1 -STA. No login, database connection or clinical writes.
param([string]$OutputDirectory = 'preview')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
[System.Windows.Interop.RenderOptions]::ProcessRenderMode = [System.Windows.Interop.RenderMode]::SoftwareOnly
$root = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $root 'bin/Release'
foreach ($dll in @('LiveCharts.dll','LiveCharts.Wpf.dll','AngleMonitorWPF.exe')) {
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $bin $dll))
}
[xml]$xml = Get-Content (Join-Path $root 'DoctorWorkspaceWindow.xaml') -Raw -Encoding UTF8
# Remove only code-behind wiring from this in-memory preview; source is untouched.
foreach ($node in $xml.SelectNodes('//*')) {
    foreach ($attr in @($node.Attributes)) {
        if (($attr.LocalName -eq 'Class' -and $attr.NamespaceURI -eq 'http://schemas.microsoft.com/winfx/2006/xaml') -or
            $attr.Name -in @('Click','SelectionChanged','Closing','Loaded','TextChanged')) {
            $null = $node.Attributes.Remove($attr)
        }
    }
}
$window = [System.Windows.Markup.XamlReader]::Parse($xml.OuterXml)
$window.Width = 1600; $window.Height = 1000
function Control($name) { return $window.FindName($name) }
(Control 'DoctorLabel').Text = 'Bác sĩ minh họa'
(Control 'StatusText').Text = 'XEM TRƯỚC — DỮ LIỆU MINH HỌA — Không kết nối SQL Server; không ghi xác nhận.'
$patient = [pscustomobject]@{ Id=1; DisplayName='BN-DEMO — Bệnh nhân minh họa' }
(Control 'PatientPicker').ItemsSource = @($patient)
(Control 'PatientPicker').SelectedIndex = 0
(Control 'PatientInfo').Text = "BN-DEMO — Bệnh nhân minh họa`nTheo dõi phục hồi chức năng khuỷu tay"
(Control 'CurrentRom').Text = "110° • Gập khuỷu tay`nGóc gập (0° duỗi thẳng)"
(Control 'GoalSummary').Text = '5 / 3'
(Control 'ConfirmedSummary').Text = '2 / 5'
(Control 'TreatmentProgress').Value = 40
$goals = @()
foreach ($angle in @(90,100,110,120,130)) {
    $m = New-Object AngleMonitorWPF.Rom.RomMilestone
    $m.MilestoneId = $goals.Count + 1; $m.PatientId=1; $m.Revision=1
    $m.MovementType='ElbowFlexion'; $m.MeasurementConvention='FlexionDegrees'
    $m.TargetAngle=$angle; $m.Tolerance=2; $m.RequiredRepetitions=3
    $m.RequiredHoldSeconds=2; $m.MeasuredAngle=110
    $m.MeasurementTime=[datetime]'2026-11-08T14:00:00'; $m.SessionId=12
    $m.MeasurementSource='Khám lâm sàng (minh họa)'
    $m.AssessmentState=if($angle -le 110){'MeasuredAchieved'}else{'NotAchieved'}
    $m.Status=if($angle -le 100){'DoctorConfirmed'}elseif($angle -le 110){'MeasuredAchieved'}else{'NotAchieved'}
    $m.IsConfirmed=$angle -le 100
    if($m.IsConfirmed){$m.DoctorName='Bác sĩ minh họa'; $m.ConfirmedAt=[datetime]'2026-11-08T07:15:00'}
    $m.Notes='Dữ liệu minh họa bố cục, không phải kết quả điều trị.'
    $goals += $m
}
(Control 'ConfirmationGrid').ItemsSource = $goals
(Control 'ConfirmationGrid').SelectedIndex = 2
(Control 'GoalGrid').ItemsSource = $goals
(Control 'AssessmentTarget').Text = 'Mốc #3: Gập khuỷu tay 110° • Góc gập (0° duỗi thẳng)'
(Control 'ClinicalAngle').Text='110'; (Control 'ClinicalReps').Text='3'; (Control 'ClinicalHold').Text='2'
(Control 'ClinicalTime').Text='2026-11-08 14:00:00'
(Control 'ClinicalSource').Text='Thước đo góc — minh họa'
(Control 'ClinicalReason').Text='Minh họa nhập kết quả khám; không lưu vào hồ sơ.'
(Control 'ConfirmationReason').Text='Minh họa nhận xét trước khi lưu xác nhận.'
$null=New-Item -ItemType Directory -Force $OutputDirectory
$window.Show()
try {
    foreach ($view in @(@{Index=0;Name='overview'},@{Index=4;Name='rom-confirmation'},@{Index=3;Name='goals'})) {
        (Control 'Modules').SelectedIndex=$view.Index
        $window.UpdateLayout()
        $window.Dispatcher.Invoke([Action]{},[System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
        $visual=$window.Content
        $bitmap=New-Object System.Windows.Media.Imaging.RenderTargetBitmap ([int]$visual.ActualWidth),([int]$visual.ActualHeight),96,96,([System.Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($visual)
        $encoder=New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $stream=[IO.File]::Create((Join-Path (Resolve-Path $OutputDirectory) ($view.Name+'.png')))
        try {$encoder.Save($stream)} finally {$stream.Dispose()}
    }
} finally {$window.Close()}
