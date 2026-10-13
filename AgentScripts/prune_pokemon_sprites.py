from __future__ import print_function
import os, re, json, zipfile, datetime, sys
import xml.etree.ElementTree as ET

project = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if sys.version_info[0] < 3: project = project.decode('utf8')
root = os.path.join(project, 'Assets', '_Project', 'Sprites', 'Pokemons', '1gen')
keep = set('Idle Walk Hurt Attack Charge Shoot SpAttack Withdraw MultiStrike Strike Double Swing Jab Kick Punch Chop Emit Gas Head HitGround LeapForth Lick MultiScratch QuickStrike Ricochet Rumble Shock Slam Slap Slice Sound Special0 Stomp Swell TailWhip Uppercut'.split())
plans, removed, retained = [], [], {}
for species in sorted(os.listdir(root)):
    folder = os.path.join(root, species)
    xml = os.path.join(folder, 'AnimData.xml')
    if not os.path.isfile(xml): continue
    data = open(xml, 'rb').read()
    tree = ET.fromstring(data)
    nodes = dict((n.findtext('Name'), n) for n in tree.find('Anims'))
    names = set(nodes) & keep
    # Follow aliases transitively, including source sheets with non-combat names.
    pending = list(names)
    while pending:
        source = nodes[pending.pop()].findtext('CopyOf')
        if source and source not in names:
            if source not in nodes: raise RuntimeError('Missing alias: ' + source)
            names.add(source); pending.append(source)
    retained[species.lower()] = sorted(names)
    def filter_node(match):
        name = re.search(br'<Name>(.*?)</Name>', match.group(0)).group(1).decode('utf8')
        return match.group(0) if name in names else b''
    cleaned = re.sub(br'    <Anim>.*?</Anim>\r?\n', filter_node, data, flags=re.S)
    plans.append((xml, cleaned))
    for filename in sorted(os.listdir(folder)):
        match = re.match(r'(.+)-(Anim|Shadow|Offsets)\.png$', filename)
        if match and (match.group(1) not in names or match.group(2) == 'Offsets'):
            path = os.path.join(folder, filename)
            removed.append(path)
            if os.path.isfile(path + '.meta'): removed.append(path + '.meta')

library = os.path.join(project, 'Assets', '_Project', 'Resources', 'PokemonAnimations.asset')
data = open(library, 'rb').read()
def filter_animation(match):
    block = match.group(0)
    species = re.search(br'PokemonId: (\S+)', block).group(1).decode('utf8')
    name = re.search(br'    Name: (\S+)', block).group(1).decode('utf8')
    return block if species not in retained or name in retained[species] else b''
cleaned = re.sub(br'  - PokemonId:.*?(?=  - PokemonId:|\Z)', filter_animation, data, flags=re.S)
plans.append((library, cleaned))
# Reject deletion if a remaining serialized asset still references a deleted GUID.
guids = set()
for path in removed:
    if path.endswith('.meta'):
        match = re.search(br'^guid: (\w+)', open(path, 'rb').read(), re.M)
        if match: guids.add(match.group(1))
deleted = set(removed)
for folder, dirs, files in os.walk(os.path.join(project, 'Assets')):
    for filename in files:
        path = os.path.join(folder, filename)
        if path in deleted or not filename.endswith(('.asset', '.prefab', '.unity', '.mat', '.controller', '.anim', '.meta')): continue
        content = cleaned if path == library else open(path, 'rb').read()
        broken = guids & set(re.findall(br'guid: (\w+)', content))
        if broken: raise RuntimeError('Referenced deletion in ' + path)
for species, names in retained.items():
    folder = next(os.path.join(root, n) for n in os.listdir(root) if n.lower() == species)
    nodes = dict((n.findtext('Name'), n) for n in ET.parse(os.path.join(folder, 'AnimData.xml')).getroot().find('Anims'))
    for name in names:
        if nodes[name].findtext('CopyOf'): continue
        for suffix in ('Anim', 'Shadow'):
            if not os.path.isfile(os.path.join(folder, name + '-' + suffix + '.png')): raise RuntimeError('Missing sheet: ' + species + '/' + name)
summary = {'species': len(retained), 'deleted_files': len(removed), 'deleted_png': sum(p.endswith('.png') for p in removed), 'removed_bytes': sum(os.path.getsize(p) for p in removed), 'retained_motions': sorted(keep), 'retained_by_species': retained, 'deleted': [os.path.relpath(p, project) for p in removed]}
print(json.dumps(dict((k,v) for k,v in summary.items() if k not in ('deleted', 'retained_by_species')), indent=2))
if '--apply' in sys.argv:
    out = os.path.join(project, 'TestResults', 'SpriteCleanup')
    if not os.path.isdir(out): os.makedirs(out)
    backup = os.path.join(out, 'backup-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S') + '.zip')
    with zipfile.ZipFile(backup, 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in removed + [p for p, content in plans]: archive.write(path, os.path.relpath(path, project))
    # Every deletion is a concrete file inside the verified sprite root.
    for path in removed:
        if not os.path.abspath(path).startswith(os.path.abspath(root) + os.sep): raise RuntimeError('Outside sprite root')
    for path, content in plans:
        with open(path, 'wb') as target: target.write(content)
    for path in removed: os.remove(path)
    with open(os.path.join(out, 'manifest.json'), 'w') as target: json.dump(summary, target, indent=2)
    print('Backup: ' + backup)
