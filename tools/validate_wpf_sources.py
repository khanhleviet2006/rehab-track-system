"""Static XAML/project checks; optional C# reference compilation, NOT a WPF build.

python tools/validate_wpf_sources.py
python tools/validate_wpf_sources.py --sdk /path/to/sdk/8.0.xxx \
    --framework /path/to/.NETFramework/v4.8
Requires restored packages for the optional reference compilation. Generated partials
declare fields only and cannot validate BAML, bindings, rendering, or event execution.
"""
import argparse
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MS = '{http://schemas.microsoft.com/developer/msbuild/2003}'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
PRESENTATION = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
EVENTS = {'Click', 'Loaded', 'Closing', 'TextChanged', 'SelectionChanged',
          'MouseLeftButtonDown', 'Checked', 'Unchecked', 'ValueChanged'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk', type=Path)
    parser.add_argument('--framework', type=Path)
    args = parser.parse_args()
    project = ET.parse(ROOT / 'AngleMonitorWPF.csproj').getroot()
    items = {kind: [e.get('Include') for e in project.iter(MS + kind)]
             for kind in ['Compile', 'Page', 'ApplicationDefinition', 'Reference', 'Resource']}
    errors = []
    for kind in ['Compile', 'Page', 'ApplicationDefinition', 'Resource']:
        for path in items[kind]:
            if not (ROOT / path.replace('\\', '/')).is_file():
                errors.append('Missing project item: ' + path)
    for path in ROOT.glob('*.xaml'):
        if path.name not in items['Page'] + items['ApplicationDefinition']:
            errors.append('XAML not included in project: ' + path.name)
    partials = []
    for filename in items['Page'] + items['ApplicationDefinition']:
        path = ROOT / filename
        tree = ET.parse(path).getroot()
        class_name = tree.get(X + 'Class')
        namespace, name = class_name.rsplit('.', 1)
        source = path.with_suffix(path.suffix + '.cs').read_text(encoding='utf-8-sig')
        keys = {e.get(X+'Key') for e in tree.iter() if e.get(X+'Key')}
        for e in tree.iter():
            for attribute, value in e.attrib.items():
                if attribute in EVENTS and not re.search(r'\b' + re.escape(value) + r'\s*\(', source):
                    errors.append(f'{filename}: event {attribute} references missing handler {value}')
                for key in re.findall(r'\{StaticResource\s+([^}]+)\}', value):
                    if not key.startswith('{x:Type ') and key not in keys:
                        errors.append(f'{filename}: missing StaticResource {key}')
        fields = []

        def walk(element, templated=False):
            uri, tag = element.tag[1:].split('}')
            templated = templated or tag in {'ControlTemplate', 'DataTemplate', 'ItemsPanelTemplate'}
            field = element.get(X+'Name') or element.get('Name')
            if field and not templated:
                if uri == PRESENTATION:
                    typename = ('System.Windows.' if tag in {'Window', 'Application'} else 'System.Windows.Controls.') + tag
                else:
                    typename = uri.split('clr-namespace:', 1)[1].split(';', 1)[0] + '.' + tag
                fields.append(f'internal {typename} {field};')
            for child in element:
                walk(child, templated)

        walk(tree)
        partials.append('namespace ' + namespace + ' { public partial class ' + name +
                        ' { public void InitializeComponent() {} ' + '\n'.join(fields) + ' } }')
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'Static XML/project check: {len(items["Page"])} pages, event handlers, resources, project includes OK.')
    if args.sdk or args.framework:
        if not (args.sdk and args.framework):
            parser.error('--sdk and --framework are required together')
        references = [args.framework / 'mscorlib.dll']
        for item in project.iter(MS+'Reference'):
            hint = item.find(MS+'HintPath')
            if hint is not None:
                reference = ROOT / hint.text.replace('\\', '/')
            else:
                reference = args.framework / (item.get('Include').split(',')[0] + '.dll')
                if not reference.is_file():
                    reference = args.framework / 'Facades' / reference.name
            if not reference.is_file():
                raise SystemExit('Missing reference: ' + str(reference))
            references.append(reference)
        with tempfile.TemporaryDirectory(prefix='rehab-wpf-reference-') as temp:
            temp = Path(temp)
            stub = temp / 'XamlFieldDeclarations.cs'
            stub.write_text('\n'.join(partials))
            csc = args.sdk / 'Roslyn/bincore/csc.dll'
            dotnet = args.sdk.parent.parent / 'dotnet'
            command = [str(dotnet), str(csc), '-nologo', '-noconfig', '-nostdlib+',
                       '-target:library', '-langversion:7.3', '-warn:0', '-out:' + str(temp / 'ReferenceCheck.dll')]
            command += ['-r:' + str(r) for r in references]
            command += [str(ROOT / p.replace('\\', '/')) for p in items['Compile']] + [str(stub)]
            result = subprocess.run(command, cwd=ROOT)
            if result.returncode:
                raise SystemExit(result.returncode)
        print('C# .NET Framework 4.8 reference compilation OK (generated field declarations; NOT BAML/WPF build).')


if __name__ == '__main__':
    main()
