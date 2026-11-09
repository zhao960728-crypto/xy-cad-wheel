param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$generated = [IO.Path]::GetFullPath($OutputDirectory)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') + '\'
if (-not $generated.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -or $generated -notmatch '\\obj\\legacy-') {
    throw 'Legacy generated sources must remain inside a kernel project obj/legacy-* directory.'
}
New-Item -ItemType Directory -Path $generated -Force | Out-Null
foreach ($oldFile in Get-ChildItem -LiteralPath $generated -Filter '*.cs') { Remove-Item -LiteralPath $oldFile.FullName }
$source = Join-Path $PSScriptRoot '..\XiaoYang.AutoCADPlugin'
foreach ($file in Get-ChildItem -LiteralPath $source -Filter '*.cs') {
    if ($file.Name -in @('PluginUpdateService.cs','LanzouUpdateSource.cs','PluginUpdateDialog.cs','PluginUpdateHandoff.cs')) { continue }
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($file.Name -eq 'CadMenuBar.cs') {
        $text = $text.Replace('dynamic', 'LegacyComObject')
        $text = $text.Replace('LegacyComObject application = CadHostInfo.AutomationApplication;', 'LegacyComObject application = new LegacyComObject(CadHostInfo.AutomationApplication);')
        $text = $text.Replace('LegacyComObject menu = _menu;', 'LegacyComObject menu = (LegacyComObject)_menu;')
    }
    $text = $text.Replace('IReadOnlyList<','IList<').Replace('IReadOnlyDictionary<','IDictionary<')
    $text = $text.Replace('Array.Empty<','LegacyRuntime.Empty<').Replace('string.IsNullOrWhiteSpace', 'LegacyRuntime.IsNullOrWhiteSpace')
    $text = $text.Replace('System.IO.Path.Combine(', 'LegacyRuntime.Combine(').Replace('Path.Combine(', 'LegacyRuntime.Combine(')
    $text = $text.Replace('ISet<', 'HashSet<').Replace('string.Join(', 'LegacyRuntime.Join(')
    $text = $text.Replace('string.Concat(', 'LegacyRuntime.Concat(')
    $text = $text.Replace('Guid.Parse(', 'LegacyRuntime.ParseGuid(').Replace('Guid.TryParseExact(', 'LegacyRuntime.TryParseGuidExact(').Replace('Guid.TryParse(', 'LegacyRuntime.TryParseGuid(')
    $text = $text.Replace('AppContext.BaseDirectory', 'AppDomain.CurrentDomain.BaseDirectory')
    $text = $text.Replace('XDocument.Load(stream)', 'LegacyRuntime.LoadXml(stream)')
    $text = [regex]::Replace($text, '\b(\w+)\.IsDynamic\b', '($1 is System.Reflection.Emit.AssemblyBuilder)')
    $text = $text.Replace('Marshal.PtrToStructure<MouseInfo>(lParam)', '(MouseInfo)Marshal.PtrToStructure(lParam, typeof(MouseInfo))')
    $text = $text.Replace('Marshal.OffsetOf<MouseInfoEx>(nameof(MouseInfoEx.MouseData))', 'Marshal.OffsetOf(typeof(MouseInfoEx), nameof(MouseInfoEx.MouseData))')
    $text = [regex]::Replace($text, ': Form\b', ': LegacyForm')
    $text = [regex]::Replace($text, ': Control\b', ': LegacyControl')
    $text = $text.Replace('RegistryKey.OpenBaseKey(', 'LegacyRegistry.OpenBaseKey(').Replace('RegistryView.', 'LegacyRegistryView.')
    $text = [regex]::Replace($text, '(internal|private) sealed record (\w+)\(([^;]+)\);', {
        param($match)
        $access = $match.Groups[1].Value; $name = $match.Groups[2].Value; $parameters = $match.Groups[3].Value
        $fields = @($parameters -split ', ' | ForEach-Object { $p=$_ -split ' '; @{Type=$p[0];Name=$p[1]} })
        $properties = ($fields | ForEach-Object { 'public ' + $_.Type + ' ' + $_.Name + ' { get; private set; }' }) -join ' '
        $assignments = ($fields | ForEach-Object { 'this.' + $_.Name + ' = ' + $_.Name + ';' }) -join ' '
        $equals = ($fields | ForEach-Object { 'EqualityComparer<' + $_.Type + '>.Default.Equals(' + $_.Name + ', other.' + $_.Name + ')' }) -join ' && '
        $hash = ($fields | ForEach-Object { 'hash = hash * 31 + EqualityComparer<' + $_.Type + '>.Default.GetHashCode(' + $_.Name + ');' }) -join ' '
        "$access sealed class $name : IEquatable<$name> { $properties public $name($parameters) { $assignments } public bool Equals($name other) { return other != null && $equals; } public override bool Equals(object other) { return Equals(other as $name); } public override int GetHashCode() { unchecked { int hash=17; $hash return hash; } } }"
    })
    $text = $text.Replace('EventHandler<MenuItemConfig>', 'Action<object, MenuItemConfig>')
    [IO.File]::WriteAllText((Join-Path $generated $file.Name),$text,[Text.Encoding]::UTF8)
}
