import xml.etree.ElementTree as ET

tree = ET.parse('Workflows.Abstraction/Workflows.Abstraction.csproj')
root = tree.getroot()

# Find the ItemGroup that has AssemblyAttribute
for itemgroup in root.findall('ItemGroup'):
    for attr in itemgroup.findall('AssemblyAttribute'):
        if attr.get('Include') == 'System.Runtime.CompilerServices.InternalsVisibleToAttribute':
            new_attr = ET.SubElement(itemgroup, 'AssemblyAttribute')
            new_attr.set('Include', 'System.Runtime.CompilerServices.InternalsVisibleToAttribute')
            param = ET.SubElement(new_attr, '_Parameter1')
            param.text = 'Workflows.Orchestrator'
            tree.write('Workflows.Abstraction/Workflows.Abstraction.csproj')
            exit(0)
